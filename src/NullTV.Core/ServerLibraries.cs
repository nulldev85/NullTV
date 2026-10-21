using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NullTV.Core;

public sealed partial class MediaServerClient
{
    private sealed class ServerResponseException(HttpStatusCode status) : HttpRequestException($"Media server returned HTTP {(int)status}. Check access and user ID.",null,status)
    {
        public HttpStatusCode Status { get; }=status;
    }
    private async Task<JsonDocument?> OptionalJsonAsync(Source source,string path,CancellationToken ct)
    {
        try { return await JsonAsync(source,path,ct); }
        catch(ServerResponseException e) when(e.Status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented) { return null; }
    }
    private static IEnumerable<JsonElement> Entries(JsonElement root)
    {
        if(root.ValueKind==JsonValueKind.Array) return root.EnumerateArray();
        if(root.ValueKind==JsonValueKind.Object && root.TryGetProperty("Items",out var entries) && entries.ValueKind==JsonValueKind.Array) return entries.EnumerateArray();
        throw new InvalidDataException("The media server returned an unreadable library response. Your previous library is preserved.");
    }
    private static bool RequiresGenre(JsonElement entry)=>entry.TryGetProperty("aiostreams",out var aio) && aio.ValueKind==JsonValueKind.Object && aio.TryGetProperty("genreRequired",out var required) && required.ValueKind==JsonValueKind.True;
    public async Task<List<string>> GenresAsync(Source source,string parentId,CancellationToken ct)
    {
        using var response=await OptionalJsonAsync(source,$"Genres?UserId={Uri.EscapeDataString(source.UserId)}&ParentId={Uri.EscapeDataString(parentId)}",ct);
        return response==null ? [] : Entries(response.RootElement).Select(entry=>XtreamClient.Text(entry,"Name")).Where(name=>name!="").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public async Task<ServerPage> LibraryPageAsync(Source source,string parentId,string libraryName,string genre,string search,string sort,int start,CancellationToken ct)
    {
        var path=$"Users/{Uri.EscapeDataString(source.UserId)}/Items?ParentId={Uri.EscapeDataString(parentId)}&Recursive=false&IncludeItemTypes=Movie,Series,BoxSet,Folder,CollectionFolder,Season,Episode,Video&Fields=Overview,ParentId,ProviderIds&Limit=60&StartIndex={Math.Max(0,start)}&EnableTotalRecordCount=true";
        if(genre!="") path+="&Genres="+Uri.EscapeDataString(genre);
        if(search!="") path+="&SearchTerm="+Uri.EscapeDataString(search);
        if(sort=="Title A–Z") path+="&SortBy=SortName&SortOrder=Ascending";
        if(sort=="Newest") path+="&SortBy=DateCreated&SortOrder=Descending";
        using var response=await JsonAsync(source,path,ct);
        var items=new List<MediaItem>(); var history=new Dictionary<string,WatchState>();
        foreach(var entry in Entries(response.RootElement).Where(entry=>XtreamClient.Text(entry,"Id")!="")) { var item=Item(source,entry) with { Category=libraryName,LibraryId=parentId }; items.Add(item); ReadUserData(entry,item,history); }
        int? total=response.RootElement.ValueKind==JsonValueKind.Object && response.RootElement.TryGetProperty("TotalRecordCount",out var count) && count.ValueKind==JsonValueKind.Number && count.TryGetInt32(out var value) && value>=0 ? value : null;
        var hasMore=items.Count>0 && (total is { } reported ? start+items.Count<reported : items.Count==60);
        return new(items,start,total,hasMore) { RemoteHistory=history };
    }
    private async Task<Catalog> BrowseJellyfinAsync(Source source,CancellationToken ct)
    {
        await LoginAsync(source,ct);
        var user=Uri.EscapeDataString(source.UserId);
        var history=new Dictionary<string,WatchState>();
        var libraries=new List<ServerLibrary>();
        using var views=await OptionalJsonAsync(source,$"Users/{user}/Views",ct);
        if(views!=null)
        {
            using var gate=new SemaphoreSlim(4);
            var entries=Entries(views.RootElement).Where(entry=>XtreamClient.Text(entry,"Id")!="" && (XtreamClient.Text(entry,"Type") is "CollectionFolder" or "Folder" || XtreamClient.Text(entry,"CollectionType")!="") && XtreamClient.Text(entry,"CollectionType") is not ("music" or "books" or "photos" or "livetv")).ToArray();
            var loaded=await Task.WhenAll(entries.Select(async entry=>
            {
                await gate.WaitAsync(ct);
                try
                {
                var id=XtreamClient.Text(entry,"Id");
                var name=XtreamClient.Text(entry,"Name"); var type=XtreamClient.Text(entry,"CollectionType");
                var required=RequiresGenre(entry);
                var genres=await GenresAsync(source,id,ct);
                var selected=required ? genres.FirstOrDefault(g=>g.Equals("None",StringComparison.OrdinalIgnoreCase))??genres.FirstOrDefault()??"" : "";
                var page=required && selected=="" ? new ServerPage([],0,0,false) : await LibraryPageAsync(source,id,name,selected,"","Server order",0,ct);
                var logo=Item(source,entry).Logo;
                return (Library:new ServerLibrary(id,name,type,logo,required) { Genres=genres,DefaultGenre=selected,Items=page.Items },History:page.RemoteHistory);
                }
                finally { gate.Release(); }
            }));
            // WhenAll preserves view order; merge history only after concurrent reads finish.
            foreach(var loadedLibrary in loaded)
            {
                libraries.Add(loadedLibrary.Library);
                foreach(var (itemId,state) in loadedLibrary.History) history[itemId]=state;
            }
        }
        var items=libraries.SelectMany(library=>library.Items).ToList();
        // Older compatible servers may expose only a flat item endpoint.
        if(libraries.Count==0)
        {
            var start=0; var seen=new HashSet<string>();
            while(start<100_000)
            {
                using var response=await JsonAsync(source,$"Users/{user}/Items?Recursive=true&IncludeItemTypes=Movie,Series&Fields=Overview,ProviderIds&Limit=200&StartIndex={start}",ct);
                var entries=Entries(response.RootElement).ToArray(); var added=0;
                foreach(var entry in entries) { var item=Item(source,entry); ReadUserData(entry,item,history); if(seen.Add(item.Id)) { items.Add(item); added++; } }
                start+=entries.Length;
                if(entries.Length==0 || added==0) break;
                var total=response.RootElement.ValueKind==JsonValueKind.Object && response.RootElement.TryGetProperty("TotalRecordCount",out var count) && count.ValueKind==JsonValueKind.Number && count.TryGetInt32(out var value) ? value : -1;
                if(total>=0 ? start>=total : entries.Length<200) break;
            }
        }
        var rows=new List<ServerRow>();
        foreach(var (name,path) in new[]{("Continue watching",$"Users/{user}/Items/Resume?Limit=24&Fields=Overview&MediaTypes=Video"),("Next up",$"Shows/NextUp?UserId={user}&Limit=24&Fields=Overview")})
        {
            using var response=await OptionalJsonAsync(source,path,ct); if(response==null) continue;
            var row=new List<MediaItem>();
            foreach(var entry in Entries(response.RootElement)) { var item=Item(source,entry); ReadUserData(entry,item,history); row.Add(item); }
            if(row.Count>0) { rows.Add(new(name,row)); items.AddRange(row); }
        }
        items=items.DistinctBy(item=>item.Id).ToList();
        source.AccountStatus=libraries.Count>0 ? $"Connected · {libraries.Count:N0} libraries · {items.Count:N0} loaded titles" : $"Connected · {items.Count:N0} titles";
        source.LastRefresh=DateTimeOffset.UtcNow;
        return new(items,"",source.AccountStatus,null) { Libraries=libraries,ServerRows=rows,RemoteHistory=history,HasServerLibraries=true };
    }
    public async Task<List<PlaybackVersion>> PlaybackVersionsAsync(Source source,MediaItem item,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,UriFor(source,$"Items/{Uri.EscapeDataString(item.ProviderId)}/PlaybackInfo?UserId={Uri.EscapeDataString(source.UserId)}")) { Content=JsonContent.Create(new { UserId=source.UserId,IsPlayback=true,EnableDirectPlay=true,EnableDirectStream=true,EnableTranscoding=false,Fresh=true },options:ServerJsonOptions) };
        Headers(request,source);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented) return [new("",item.Name,"",item)];
        if(!response.IsSuccessStatusCode) throw new ServerResponseException(response.StatusCode);
        await using var stream=await response.Content.ReadAsStreamAsync(ct); using var json=await JsonDocument.ParseAsync(stream,cancellationToken:ct);
        if(XtreamClient.Text(json.RootElement,"ErrorCode") is { Length:>0 } error && error!="None") throw new InvalidDataException("The server has no playable version for this title. Try another title or refresh the server library.");
        var versions=new List<PlaybackVersion>();
        if(json.RootElement.TryGetProperty("MediaSources",out var sources) && sources.ValueKind==JsonValueKind.Array)
            foreach(var media in sources.EnumerateArray())
            {
                var id=XtreamClient.Text(media,"Id"); var name=XtreamClient.Text(media,"Name"); var description="";
                if(media.TryGetProperty("aiostreams",out var aio) && aio.ValueKind==JsonValueKind.Object) { if(XtreamClient.Text(aio,"name") is { Length:>0 } label) name=label; description=XtreamClient.Text(aio,"description"); if(XtreamClient.Text(aio,"id") is { Length:>0 } versionId) id=versionId; }
                var path=XtreamClient.Text(media,"DirectStreamUrl");
                if(path=="" && XtreamClient.Text(media,"Protocol").Equals("Http",StringComparison.OrdinalIgnoreCase)) path=XtreamClient.Text(media,"Path");
                var baseUri=XtreamClient.BaseUri(source);
                string url;
                if(path!="" && Uri.TryCreate(baseUri,path,out var target) && target.Scheme is "http" or "https")
                {
                    url=target.AbsoluteUri;
                    // A server token belongs to this server, never an external stream host.
                    if(target.Scheme==baseUri.Scheme && target.Host==baseUri.Host && target.Port==baseUri.Port && !target.Query.Contains("api_key=",StringComparison.OrdinalIgnoreCase)) url+=(target.Query=="" ? "?" : "&")+"api_key="+Uri.EscapeDataString(source.ApiKey);
                }
                else url=item.Url+"&MediaSourceId="+Uri.EscapeDataString(id)+"&PlaySessionId="+Uri.EscapeDataString(XtreamClient.Text(json.RootElement,"PlaySessionId"));
                var headers=new Dictionary<string,string>(item.Headers);
                if(media.TryGetProperty("RequiredHttpHeaders",out var required) && required.ValueKind==JsonValueKind.Object)
                    foreach(var header in required.EnumerateObject().Where(header=>header.Name is "User-Agent" or "Referer")) headers[header.Name]=header.Value.GetString()??"";
                versions.Add(new(id,name=="" ? item.Name : name,description,item with { Url=url,Headers=headers,MediaSourceId=id,PlaySessionId=XtreamClient.Text(json.RootElement,"PlaySessionId") }));
            }
        if(versions.Count==0) throw new InvalidDataException("The server returned no playable versions for this title.");
        return versions;
    }
}

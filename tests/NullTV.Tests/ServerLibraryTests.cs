using System.Net;
using System.Text;
using System.Text.Json;
using NullTV.Core;

static class ServerLibraryTests
{
    private static Source Source()=>new() { Kind=SourceKind.Jellyfin,Address="https://fixture.invalid/jellyfin/u/studio",ApiKey="fixture-token",UserId="viewer" };
    public static void Register(Action<string,Func<Task>> test,Action<bool,string> check)
    {
        test("AIO libraries load despite an empty root crawl; genres and order survive",async ()=> {
            using var handler=new AioLibraryFixture(); using var http=new HttpClient(handler); var source=Source();
            var catalog=await new MediaServerClient(http).BrowseAsync(source,default);
            check(catalog.Libraries.Select(l=>l.Name).SequenceEqual(new[]{"Popular movies","Discover movies","Series"}),"Server library order was lost");
            check(catalog.Libraries[1].GenreRequired && catalog.Libraries[1].DefaultGenre=="None" && catalog.Libraries[1].Items.Count==20,"Required genre library is empty");
            check(catalog.Items.Count>0 && catalog.ServerRows.Select(row=>row.Name).SequenceEqual(new[]{"Continue watching","Next up"}),"Server home rows are absent");
            check(handler.StreamLookups==0 && !handler.SawMediaSourcesField,"Browsing must not resolve streams");
            check(handler.Requests.All(request=>request.AbsolutePath.StartsWith("/jellyfin/u/studio/")),"Configured base path was lost");
            check(source.AccountStatus.Contains("3 libraries") && !source.AccountStatus.Contains("0 items"),"Connection still reports an empty server");
        });
        test("AIO required genre chooses first real genre when None is absent",async ()=> {
            using var handler=new AioLibraryFixture { OfferNone=false }; using var http=new HttpClient(handler);
            var catalog=await new MediaServerClient(http).BrowseAsync(Source(),default);
            check(catalog.Libraries[1].DefaultGenre=="Action" && catalog.Libraries[1].Items.Count>0,"First required genre was not selected");
        });
        test("Server-capped pages use reported totals and query search remotely",async ()=> {
            using var handler=new AioLibraryFixture(); using var http=new HttpClient(handler); var client=new MediaServerClient(http); var source=Source();
            var first=await client.LibraryPageAsync(source,"discover","Discover movies","Action","Frame","Title A–Z",0,default);
            var second=await client.LibraryPageAsync(source,"discover","Discover movies","Action","Frame","Title A–Z",first.Items.Count,default);
            var last=await client.LibraryPageAsync(source,"discover","Discover movies","Action","Frame","Title A–Z",40,default);
            check(first.Items.Count==20 && first.HasMore && second.Items.Count==20 && last.Items.Count==15 && !last.HasMore,"A short capped page ended paging prematurely");
            check(!first.Items.Select(item=>item.Id).Intersect(second.Items.Select(item=>item.Id)).Any(),"Pages repeat titles");
            check(handler.SawSearch && handler.SawSort,"Search or sort was only applied locally");
            check(first.RemoteHistory.Values.Any(state=>state.Watched),"Library watched state was discarded");
        });
        test("Server collections remain folders and seasons return episodes",async ()=> {
            using var handler=new AioLibraryFixture(); using var http=new HttpClient(handler); var client=new MediaServerClient(http); var source=Source();
            var root=await client.LibraryPageAsync(source,"popular","Popular movies","","","Server order",0,default);
            check(root.Items[0].Kind==MediaKind.Folder && root.Items[0].Url=="","Collection became a fake movie");
            var children=await client.LibraryPageAsync(source,"collection","Collection","","","Server order",0,default);
            check(children.Items.Count==3 && children.Items.All(item=>item.Kind==MediaKind.Movie),"Collection children are absent");
        });
        test("Playback resolves versions only on play and never leaks server token to an external host",async ()=> {
            using var handler=new AioLibraryFixture(); using var http=new HttpClient(handler); var source=Source(); var client=new MediaServerClient(http);
            var page=await client.LibraryPageAsync(source,"discover","Discover movies","None","","Server order",0,default);
            var versions=await client.PlaybackVersionsAsync(source,page.Items[0],default);
            check(handler.StreamLookups==1 && handler.SawPlaybackFlags,"PlaybackInfo was not requested at play time");
            check(versions.Count==2 && versions[0].Name=="1080p · Studio" && versions[1].Description=="Alternative stream","Server version names were lost");
            check(versions[0].Item.Url.Contains("fixture-token") && versions[0].Item.Url.Contains("/jellyfin/u/studio/"),"Internal stream lost its authorization/base path");
            check(!versions[1].Item.Url.Contains("fixture-token") && versions[1].Item.Headers.GetValueOrDefault("User-Agent")=="Fixture player" && !versions[1].Item.Headers.ContainsKey("X-Emby-Token"),"Server token leaked to an external stream");
        });
        test("Unreadable or unauthorized libraries are not successful empty connections",async ()=> {
            foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.OK}) {
                using var handler=new BrokenViewsFixture(status); using var http=new HttpClient(handler); var source=Source(); source.AccountStatus="Previously connected";
                try { await new MediaServerClient(http).BrowseAsync(source,default); throw new Exception("Bad library accepted"); } catch(Exception e) when(e is InvalidDataException or HttpRequestException) { }
                check(source.AccountStatus=="Previously connected","Bad response replaced the existing library status");
            }
        });
        test("Legacy servers without Views or home routes keep all capped pages",async ()=> {
            using var http=new HttpClient(new LegacyLibraryFixture()); var catalog=await new MediaServerClient(http).BrowseAsync(Source(),default);
            check(catalog.Items.Count==2 && catalog.HasServerLibraries,"Legacy discovery lost a page or would refresh repeatedly");
        });
    }
}

sealed class AioLibraryFixture : HttpMessageHandler
{
    public bool OfferNone=true,SawMediaSourcesField,SawSearch,SawSort,SawPlaybackFlags;
    public int StreamLookups;
    public List<Uri> Requests { get; }=[];
    public string StreamUrl="https://fixture.invalid/jellyfin/u/studio/Videos/frame-0/stream";
    public static Dictionary<string,string> Query(Uri uri)=>uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(part=>part.Split('=',2)).ToDictionary(parts=>Uri.UnescapeDataString(parts[0]),parts=>parts.Length>1 ? Uri.UnescapeDataString(parts[1]) : "",StringComparer.OrdinalIgnoreCase);
    public static object Item(int index,string type="Movie")=>new { Id=(type=="Movie" ? "frame-" : "episode-")+index,Type=type,Name="Frame "+(index+1).ToString("000"),Overview="An independent studio story.",ProductionYear=2026,UserData=new { Played=index==1,PlaybackPositionTicks=index==0 ? 600_000_000L : 0,LastPlayedDate="2026-10-12T03:00:00Z" },RunTimeTicks=54_000_000_000L };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        var uri=request.RequestUri!; Requests.Add(uri); var path=uri.AbsolutePath; var query=Query(uri);
        SawMediaSourcesField|=query.GetValueOrDefault("Fields","").Contains("MediaSources",StringComparison.OrdinalIgnoreCase);
        if(path.EndsWith("/Views")) return Json(new { Items=new object[]{new { Id="popular",Name="Popular movies",Type="CollectionFolder",CollectionType="movies" },new { Id="discover",Name="Discover movies",Type="CollectionFolder",CollectionType="movies",aiostreams=new { genreRequired=true } },new { Id="series",Name="Series",Type="CollectionFolder",CollectionType="tvshows" }} });
        if(path.EndsWith("/Genres")) return Json(new { Items=(OfferNone ? new[]{"None","Action","Drama"} : new[]{"Action","Drama"}).Select(name=>new { Name=name }) });
        if(path.EndsWith("/Resume")) return Json(new { Items=new[]{Item(0)} });
        if(path.EndsWith("/NextUp")) return Json(new { Items=new[]{Item(0,"Episode")} });
        if(path.EndsWith("/PlaybackInfo")) {
            StreamLookups++; var body=await request.Content!.ReadAsStringAsync(ct); SawPlaybackFlags=body.Contains("\"Fresh\":true") && body.Contains("\"IsPlayback\":true");
            return Json(new { PlaySessionId="session",MediaSources=new object[]{new { Id="v1",Name="Version one",DirectStreamUrl=StreamUrl,aiostreams=new { name="1080p · Studio",description="Preferred stream" } },new { Id="v2",Name="Version two",Protocol="Http",Path="https://cdn.invalid/movie.mp4",RequiredHttpHeaders=new Dictionary<string,string> { ["User-Agent"]="Fixture player",["X-Emby-Token"]="must-not-leak" },aiostreams=new { name="720p · Studio",description="Alternative stream" } }} });
        }
        if(path.EndsWith("/Items")) {
            var parent=query.GetValueOrDefault("ParentId",""); if(parent=="") return Json(new { Items=Array.Empty<object>(),TotalRecordCount=0 });
            if(parent=="discover" && query.GetValueOrDefault("Genres","")=="") return Json(new { Items=Array.Empty<object>(),TotalRecordCount=0 });
            var start=int.TryParse(query.GetValueOrDefault("StartIndex"),out var offset) ? offset : 0;
            SawSearch|=query.GetValueOrDefault("SearchTerm")=="Frame"; SawSort|=query.GetValueOrDefault("SortBy")=="SortName";
            var total=parent=="collection" ? 3 : 55; var entries=Enumerable.Range(start,Math.Min(20,Math.Max(0,total-start))).Select(i=>parent=="series" ? Item(i,"Series") : Item(i)).ToList();
            if(parent=="popular" && start==0) entries[0]=new { Id="collection",Type="BoxSet",Name="Studio collection",Overview="Three studio films." };
            return Json(new { Items=entries,TotalRecordCount=total });
        }
        if(path.Contains("/Items/")) return Json(Item(0));
        return new(HttpStatusCode.NotFound);
    }
    private static HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json") };
}
sealed class BrokenViewsFixture(HttpStatusCode status) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(status) { Content=new StringContent("{\"unexpected\":true}",Encoding.UTF8,"application/json") });
}
sealed class LegacyLibraryFixture : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(!request.RequestUri!.AbsolutePath.EndsWith("/Items")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var offset=int.Parse(AioLibraryFixture.Query(request.RequestUri).GetValueOrDefault("StartIndex","0"));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new { Items=offset<2 ? new[]{AioLibraryFixture.Item(offset)} : [],TotalRecordCount=2 }),Encoding.UTF8,"application/json") });
    }
}

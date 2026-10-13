using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;

namespace NullTV.Core;
public sealed partial class MediaServerClient(HttpClient http)
{
    private static readonly JsonSerializerOptions ServerJsonOptions=new() { PropertyNamingPolicy=null };
    private static Uri UriFor(Source s,string path) => new(XtreamClient.BaseUri(s),path);
    private static void Headers(HttpRequestMessage request,Source s)
    {
        if(s.Kind==SourceKind.Plex) { request.Headers.Add("X-Plex-Token",s.ApiKey); request.Headers.Add("X-Plex-Client-Identifier",s.Id); request.Headers.Add("X-Plex-Product","NullTV"); }
        else { request.Headers.TryAddWithoutValidation("X-Emby-Token",s.ApiKey); request.Headers.TryAddWithoutValidation("Authorization",$"MediaBrowser Client=\"NullTV\", Device=\"Windows ARM64\", DeviceId=\"{s.Id}\", Version=\"{typeof(MediaServerClient).Assembly.GetName().Version?.ToString(3)}\""); }
    }
    private async Task<JsonDocument> JsonAsync(Source s,string path,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,UriFor(s,path)); Headers(request,s); request.Headers.TryAddWithoutValidation("Accept","application/json"); using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct); if(!response.IsSuccessStatusCode) throw new ServerResponseException(response.StatusCode); await using var stream=await response.Content.ReadAsStreamAsync(ct); return await JsonDocument.ParseAsync(stream,cancellationToken:ct);
    }
    public async Task LoginAsync(Source s,CancellationToken ct)
    {
        if(s.Kind==SourceKind.Plex) return;
        if(s.ApiKey=="")
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,UriFor(s,"Users/AuthenticateByName")) { Content=JsonContent.Create(new { Username=s.Username,Pw=s.Password },options:ServerJsonOptions) }; Headers(request,s);
            using var response=await http.SendAsync(request,ct); if(!response.IsSuccessStatusCode) throw new InvalidDataException("Media server login failed."); using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); s.ApiKey=XtreamClient.Text(doc.RootElement,"AccessToken"); if(doc.RootElement.TryGetProperty("User",out var user)) s.UserId=XtreamClient.Text(user,"Id");
        }
        if(s.UserId=="") { using var me=await JsonAsync(s,"Users/Me",ct); s.UserId=XtreamClient.Text(me.RootElement,"Id"); }
        if(s.UserId=="") throw new InvalidDataException("Enter the media-server user ID when using a server API key.");
    }
    public async Task<Catalog> BrowseAsync(Source s,CancellationToken ct)
    {
        if(s.Kind==SourceKind.Plex) return await BrowsePlexAsync(s,ct);
        return await BrowseJellyfinAsync(s,ct);
    }
    private static void ReadUserData(JsonElement e,MediaItem item,Dictionary<string,WatchState> history)
    {
        if(!e.TryGetProperty("UserData",out var data)) return;
        long.TryParse(XtreamClient.Text(data,"PlaybackPositionTicks"),out var ticks); long.TryParse(XtreamClient.Text(e,"RunTimeTicks"),out var duration); var played=XtreamClient.Text(data,"Played").Equals("true",StringComparison.OrdinalIgnoreCase);
        if(ticks<=0 && !played) return; var updated=DateTimeOffset.TryParse(XtreamClient.Text(data,"LastPlayedDate"),out var date) ? date : DateTimeOffset.UtcNow;
        history[item.Id]=new(Math.Max(0,ticks/10_000_000d),Math.Max(0,duration/10_000_000d),played,updated);
    }
    public async Task StartedAsync(Source s,MediaItem item,CancellationToken ct)
    {
        if(s.Kind==SourceKind.Plex) return;
        using var request=new HttpRequestMessage(HttpMethod.Post,UriFor(s,"Sessions/Playing")) { Content=JsonContent.Create(new { ItemId=item.ProviderId,MediaSourceId=item.MediaSourceId,PlaySessionId=item.PlaySessionId,PlayMethod="DirectPlay",CanSeek=true,PositionTicks=0L },options:ServerJsonOptions) }; Headers(request,s); using var response=await http.SendAsync(request,ct); if(!response.IsSuccessStatusCode) throw new InvalidDataException("The media server rejected the playback session start.");
    }
    private static MediaItem Item(Source s,JsonElement e)
    {
        var id=XtreamClient.Text(e,"Id"); var type=XtreamClient.Text(e,"Type"); var kind=type=="Series" ? MediaKind.Series : type=="Episode" ? MediaKind.Episode : type is "BoxSet" or "Folder" or "CollectionFolder" or "Season" or "Playlist" ? MediaKind.Folder : MediaKind.Movie;
        var baseUri=XtreamClient.BaseUri(s); var key=Uri.EscapeDataString(s.ApiKey);
        var url=new Uri(baseUri,$"Videos/{Uri.EscapeDataString(id)}/stream?static=true&api_key={key}").ToString();
        return new($"{s.Id}:{kind}:{id}",s.Id,XtreamClient.Text(e,"Name"),s.Name,kind,kind is MediaKind.Series or MediaKind.Folder ? "" : url) { ProviderId=id,Logo=new Uri(baseUri,$"Items/{Uri.EscapeDataString(id)}/Images/Primary?maxWidth=400&api_key={key}").ToString(),LibraryId=XtreamClient.Text(e,"ParentId"),ServerType=type,Overview=XtreamClient.Text(e,"Overview"),Year=XtreamClient.Text(e,"ProductionYear") };
    }
    public async Task<MediaDetails> DetailsAsync(Source s,MediaItem item,CancellationToken ct)
    {
        if(s.Kind==SourceKind.Plex)
        {
            var doc=await PlexXmlAsync(s,$"library/metadata/{Uri.EscapeDataString(item.ProviderId)}",ct); var info=doc.Descendants().FirstOrDefault(e=>e.Name.LocalName is "Video" or "Directory"); var eps=new List<MediaItem>(); if(item.Kind==MediaKind.Series) { var children=await PlexXmlAsync(s,$"library/metadata/{item.ProviderId}/allLeaves",ct); foreach(var e in children.Descendants("Video")) eps.Add(PlexItem(s,e)); }
            return new((string?)info?.Attribute("summary")??"",string.Join(", ",info?.Elements("Role").Select(e=>(string?)e.Attribute("tag"))??[]),(string?)info?.Attribute("rating")??"",eps);
        }
        using var d=await JsonAsync(s,$"Users/{Uri.EscapeDataString(s.UserId)}/Items/{Uri.EscapeDataString(item.ProviderId)}?Fields=Overview,People",ct); var metadata=d.RootElement; var episodes=new List<MediaItem>();
        if(item.Kind==MediaKind.Series) { using var ep=await JsonAsync(s,$"Shows/{Uri.EscapeDataString(item.ProviderId)}/Episodes?UserId={Uri.EscapeDataString(s.UserId)}&Fields=Overview&Limit=1000",ct); if(ep.RootElement.TryGetProperty("Items",out var arr)) foreach(var x in arr.EnumerateArray()) episodes.Add(Item(s,x) with { Name=$"S{XtreamClient.Text(x,"ParentIndexNumber")} E{XtreamClient.Text(x,"IndexNumber")} · {XtreamClient.Text(x,"Name")}" }); }
        return new(XtreamClient.Text(metadata,"Overview"),metadata.TryGetProperty("People",out var people) && people.ValueKind==JsonValueKind.Array ? string.Join(", ",people.EnumerateArray().Take(20).Select(x=>XtreamClient.Text(x,"Name"))) : "",XtreamClient.Text(metadata,"CommunityRating"),episodes);
    }
    public async Task WatchedAsync(Source s,MediaItem item,bool watched,CancellationToken ct)
    {
        var path=s.Kind==SourceKind.Plex ? $":/{(watched ? "scrobble" : "unscrobble")}?key={Uri.EscapeDataString(item.ProviderId)}&identifier=com.plexapp.plugins.library" : $"Users/{Uri.EscapeDataString(s.UserId)}/PlayedItems/{Uri.EscapeDataString(item.ProviderId)}";
        using var req=new HttpRequestMessage(s.Kind==SourceKind.Plex ? HttpMethod.Get : watched ? HttpMethod.Post : HttpMethod.Delete,UriFor(s,path)); Headers(req,s); using var r=await http.SendAsync(req,ct); if(!r.IsSuccessStatusCode) throw new InvalidDataException("The server could not sync watched state.");
    }
    public async Task ProgressAsync(Source s,MediaItem item,double pos,double duration,bool stopped,CancellationToken ct)
    {
        var path=s.Kind==SourceKind.Plex ? $":/timeline?ratingKey={item.ProviderId}&key=/library/metadata/{item.ProviderId}&state={(stopped ? "stopped" : "playing")}&time={(long)(pos*1000)}&duration={(long)(duration*1000)}" : $"Sessions/Playing/{(stopped ? "Stopped" : "Progress")}";
        using var req=new HttpRequestMessage(s.Kind==SourceKind.Plex ? HttpMethod.Get : HttpMethod.Post,UriFor(s,path)); Headers(req,s); if(s.Kind!=SourceKind.Plex) req.Content=JsonContent.Create(new { ItemId=item.ProviderId,MediaSourceId=item.MediaSourceId,PlaySessionId=item.PlaySessionId,PositionTicks=(long)(pos*10_000_000),IsPaused=false,PlayMethod="DirectPlay",CanSeek=true },options:ServerJsonOptions); using var response=await http.SendAsync(req,ct); if(!response.IsSuccessStatusCode) throw new InvalidDataException("The media server rejected playback progress sync.");
    }
    private async Task<XDocument> PlexXmlAsync(Source s,string path,CancellationToken ct) { using var req=new HttpRequestMessage(HttpMethod.Get,UriFor(s,path)); Headers(req,s); using var response=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct); if(!response.IsSuccessStatusCode) throw new InvalidDataException($"Plex returned HTTP {(int)response.StatusCode}."); await using var stream=await response.Content.ReadAsStreamAsync(ct); using var reader=System.Xml.XmlReader.Create(stream,new() { Async=true,DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null }); return await XDocument.LoadAsync(reader,LoadOptions.None,ct); }
    private async Task<Catalog> BrowsePlexAsync(Source s,CancellationToken ct)
    {
        var sections=await PlexXmlAsync(s,"library/sections",ct); var list=new List<MediaItem>(); var libraries=new List<ServerLibrary>();
        foreach(var section in sections.Descendants("Directory"))
        {
            var key=(string?)section.Attribute("key"); if(key==null) continue; var data=await PlexXmlAsync(s,$"library/sections/{key}/all",ct);
            var name=(string?)section.Attribute("title")??s.Name;
            var entries=data.Descendants().Where(e=>e.Name.LocalName is "Video" or "Directory" && e.Attribute("ratingKey")!=null).Select(e=>PlexItem(s,e) with { Category=name,LibraryId=key }).ToList(); list.AddRange(entries);
            libraries.Add(new(key,name,(string?)section.Attribute("type")??"","",false) { Items=entries });
        }
        s.AccountStatus=$"Connected · {libraries.Count:N0} libraries · {list.Count:N0} titles"; s.LastRefresh=DateTimeOffset.UtcNow; return new(list,"",s.AccountStatus,null) { Libraries=libraries,HasServerLibraries=true };
    }
    private static MediaItem PlexItem(Source s,XElement e)
    {
        var id=(string?)e.Attribute("ratingKey")??""; var type=(string?)e.Attribute("type")??""; var kind=type=="show" ? MediaKind.Series : type=="episode" ? MediaKind.Episode : MediaKind.Movie; var part=(string?)e.Descendants("Part").FirstOrDefault()?.Attribute("key")??"";
        var baseUri=XtreamClient.BaseUri(s);
        string AuthorizedUrl(string path) { if(path=="") return ""; var uri=new Uri(baseUri,path); if(uri.Scheme!=baseUri.Scheme || uri.Host!=baseUri.Host || uri.Port!=baseUri.Port) throw new InvalidDataException("Plex returned a media path outside this server."); return uri.AbsoluteUri+(uri.Query=="" ? "?" : "&")+"X-Plex-Token="+Uri.EscapeDataString(s.ApiKey); }
        var thumb=(string?)e.Attribute("thumb")??""; return new($"{s.Id}:{kind}:{id}",s.Id,(string?)e.Attribute("title")??"Untitled",s.Name,kind,AuthorizedUrl(part)) { ProviderId=id,Logo=AuthorizedUrl(thumb) };
    }
    public static async Task<(int Id,string Code)> PlexPinAsync(HttpClient http,string clientId,CancellationToken ct)
    {
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://plex.tv/api/v2/pins") { Content=new FormUrlEncodedContent(new Dictionary<string,string> { ["strong"]="false" }) }; req.Headers.Add("X-Plex-Client-Identifier",clientId); req.Headers.Add("X-Plex-Product","NullTV"); req.Headers.Add("Accept","application/json"); using var response=await http.SendAsync(req,ct); if(!response.IsSuccessStatusCode) throw new InvalidDataException("Plex PIN request failed."); using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); return (doc.RootElement.GetProperty("id").GetInt32(),XtreamClient.Text(doc.RootElement,"code"));
    }
    public static async Task<string?> PollPlexPinAsync(HttpClient http,string clientId,int id,CancellationToken ct)
    {
        using var req=new HttpRequestMessage(HttpMethod.Get,$"https://plex.tv/api/v2/pins/{id}"); req.Headers.Add("X-Plex-Client-Identifier",clientId); req.Headers.Add("X-Plex-Product","NullTV"); req.Headers.Add("Accept","application/json"); using var response=await http.SendAsync(req,ct); if(!response.IsSuccessStatusCode) return null; using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); var token=XtreamClient.Text(doc.RootElement,"authToken"); return token=="" ? null : token;
    }
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NullTV.Core;

public sealed class StremioAddon
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string ManifestUrl { get; set; }="";
    public bool Enabled { get; set; }=true;
    public AddonManifest Manifest { get; set; }=new();
    public DateTimeOffset Updated { get; set; }
}
public sealed class AddonManifest
{
    public string Id { get; set; }="";
    public string Name { get; set; }="";
    public string Version { get; set; }="";
    public string Description { get; set; }="";
    public bool Configurable { get; set; }
    public bool ConfigurationRequired { get; set; }
    public List<string> Types { get; set; }=[];
    public List<AddonResource> Resources { get; set; }=[];
    public List<AddonCatalog> Catalogs { get; set; }=[];
    public bool Supports(string resource,string type,string id)=>Resources.Any(r=>r.Name==resource && (r.Types.Count==0 ? Types.Contains(type) : r.Types.Contains(type)) && (r.IdPrefixes.Count==0 || r.IdPrefixes.Any(prefix=>id.StartsWith(prefix,StringComparison.Ordinal))));
}
public sealed record AddonResource(string Name,List<string> Types,List<string> IdPrefixes);
public sealed record AddonExtra(string Name,bool Required,List<string> Options);
public sealed record AddonCatalog(string Id,string Type,string Name,List<AddonExtra> Extras)
{
    public bool Supports(string name)=>Extras.Any(extra=>extra.Name==name);
}
public sealed record AddonPage(List<MediaItem> Items,int Received,bool HasMore);
public sealed record AddonStream(string Name,string Description,string Url,string ExternalUrl,string InfoHash,int? FileIndex,Dictionary<string,string> Headers,List<MediaSubtitle> Subtitles,string Unsupported)
{
    public bool Direct=>Url!="";
}
public sealed record MediaSubtitle(string Id,string Language,string Url);

public static class AddonIdentity
{
    public static string Imdb(string value)=>Regex.IsMatch(value??"",@"^tt\d{5,12}$",RegexOptions.CultureInvariant) ? value??"" : "";
    public static string Type(MediaItem item)=>item.Kind is MediaKind.Series or MediaKind.Episode ? "series" : "movie";
    public static string VideoId(MediaItem item,string identity)=>item.Kind==MediaKind.Episode && item.Season is { } season && item.Episode is >0 ? $"{identity}:{season}:{item.Episode}" : item.Kind==MediaKind.Episode ? "" : identity;
}

// HTTP addon protocol only: manifests and resources are data, never executable plugins.
public sealed class StremioClient(HttpClient http)
{
    public static Uri ManifestUri(string input)
    {
        input=input.Trim(); var localInstall=input.StartsWith("stremio://",StringComparison.OrdinalIgnoreCase);
        if(input.StartsWith("stremio://",StringComparison.OrdinalIgnoreCase)) input="https://"+input[10..];
        if(!Uri.TryCreate(input,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo!="" || uri.Fragment!="") throw new InvalidDataException("Paste an HTTP(S) addon manifest URL or a stremio:// install link.");
        if(localInstall && uri.IsLoopback) uri=new UriBuilder(uri) { Scheme="http",Port=uri.IsDefaultPort ? -1 : uri.Port }.Uri;
        if(uri.AbsolutePath.EndsWith("/configure",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Complete the addon's configuration page, then paste its generated install link.");
        if(!uri.AbsolutePath.EndsWith("manifest.json",StringComparison.OrdinalIgnoreCase)) { var builder=new UriBuilder(uri) { Path=uri.AbsolutePath.TrimEnd('/')+"/manifest.json" }; uri=builder.Uri; }
        return uri;
    }
    public static Uri ResourceUri(StremioAddon addon,string resource,string type,string id,IEnumerable<KeyValuePair<string,string>>? extras=null)
    {
        var manifest=ManifestUri(addon.ManifestUrl); var builder=new UriBuilder(manifest);
        var basePath=manifest.AbsolutePath[..(manifest.AbsolutePath.LastIndexOf('/')+1)];
        var extra=extras==null ? "" : string.Join('&',extras.Select(pair=>Uri.EscapeDataString(pair.Key)+"="+Uri.EscapeDataString(pair.Value)));
        builder.Path=basePath+Uri.EscapeDataString(resource)+"/"+Uri.EscapeDataString(type)+"/"+Uri.EscapeDataString(id)+(extra=="" ? "" : "/"+extra)+".json";
        return builder.Uri;
    }
    public static Uri ConfigureUri(StremioAddon addon)
    {
        var manifest=ManifestUri(addon.ManifestUrl); var builder=new UriBuilder(manifest) { Path=manifest.AbsolutePath[..(manifest.AbsolutePath.LastIndexOf('/')+1)]+"configure" }; return builder.Uri;
    }
    private async Task<JsonDocument> ReadAsync(Uri uri,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(22));
        using var request=new HttpRequestMessage(HttpMethod.Get,uri); request.Headers.TryAddWithoutValidation("Accept","application/json");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
        if(!response.IsSuccessStatusCode) throw new InvalidDataException($"Addon returned HTTP {(int)response.StatusCode}.");
        if(response.Content.Headers.ContentLength>8*1024*1024) throw new InvalidDataException("The addon response is too large.");
        await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false); using var bytes=new MemoryStream(); var buffer=new byte[16384];
        int read; while((read=await stream.ReadAsync(buffer,timeout.Token).ConfigureAwait(false))>0) { if(bytes.Length+read>8*1024*1024) throw new InvalidDataException("The addon response is too large."); await bytes.WriteAsync(buffer.AsMemory(0,read),timeout.Token).ConfigureAwait(false); }
        try { return JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions { MaxDepth=48 }); } catch(JsonException) { throw new InvalidDataException("The addon did not return valid JSON."); }
    }
    private static string S(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
    private static bool B(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.True;
    private static IEnumerable<JsonElement> A(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.Array ? v.EnumerateArray() : [];
    private static List<string> Strings(JsonElement e,string key)=>A(e,key).Where(v=>v.ValueKind==JsonValueKind.String).Select(v=>v.GetString()!).Where(v=>v.Length<1024).Take(128).ToList();
    public async Task<AddonManifest> ManifestAsync(string input,CancellationToken ct=default)
    {
        using var doc=await ReadAsync(ManifestUri(input),ct); var root=doc.RootElement;
        var manifest=new AddonManifest { Id=S(root,"id"),Name=S(root,"name"),Version=S(root,"version"),Description=S(root,"description"),Types=Strings(root,"types") };
        if(manifest.Id=="" || manifest.Name=="" || manifest.Version=="" || manifest.Types.Count==0) throw new InvalidDataException("The addon manifest is missing its ID, name, version or content types.");
        var prefixes=Strings(root,"idPrefixes");
        foreach(var resource in A(root,"resources").Take(32)) manifest.Resources.Add(resource.ValueKind==JsonValueKind.String ? new(resource.GetString()!,[],prefixes) : new(S(resource,"name"),Strings(resource,"types"),resource.TryGetProperty("idPrefixes",out _) ? Strings(resource,"idPrefixes") : prefixes));
        if(manifest.Resources.Count==0) throw new InvalidDataException("This addon does not declare any resources.");
        foreach(var catalog in A(root,"catalogs").Take(128))
        {
            var type=S(catalog,"type"); var id=S(catalog,"id"); if(id=="" || type is not ("movie" or "series")) continue;
            var extras=A(catalog,"extra").Select(e=>new AddonExtra(S(e,"name"),B(e,"isRequired"),Strings(e,"options"))).Where(e=>e.Name!="").ToList();
            foreach(var extraName in Strings(catalog,"extraSupported")) if(!extras.Any(e=>e.Name==extraName)) extras.Add(new(extraName,Strings(catalog,"extraRequired").Contains(extraName),[]));
            manifest.Catalogs.Add(new(id,type,S(catalog,"name") is { Length:>0 } name ? name : id,extras));
        }
        if(root.TryGetProperty("behaviorHints",out var hints)) { manifest.Configurable=B(hints,"configurable"); manifest.ConfigurationRequired=B(hints,"configurationRequired"); }
        return manifest;
    }
    private static string WebUrl(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : "";
    private static MediaItem Item(StremioAddon addon,JsonElement meta,string type,string catalog)
    {
        var id=S(meta,"id"); var genres=Strings(meta,"genres");
        return new($"addon:{type}:{id}",addon.Id,S(meta,"name"),catalog,type=="series" ? MediaKind.Series : MediaKind.Movie,"") {
            ProviderId=id,ContentId=id,ContentType=type,Logo=WebUrl(S(meta,"poster")),Backdrop=WebUrl(S(meta,"background")),Overview=S(meta,"description"),Year=S(meta,"releaseInfo"),Rating=S(meta,"imdbRating"),Runtime=S(meta,"runtime"),Cast=string.Join(", ",Strings(meta,"cast")),Genres=genres };
    }
    public async Task<AddonPage> CatalogAsync(StremioAddon addon,AddonCatalog catalog,string search="",int skip=0,string genre="",CancellationToken ct=default)
    {
        var extra=new Dictionary<string,string>(); if(search!="" && catalog.Supports("search")) extra["search"]=search; if(skip>0 && catalog.Supports("skip")) extra["skip"]=skip.ToString(); if(genre!="") extra["genre"]=genre;
        foreach(var required in catalog.Extras.Where(e=>e.Required)) if(!extra.ContainsKey(required.Name)) { if(required.Name=="search") return new([],0,false); if(required.Options.FirstOrDefault() is { } option) extra[required.Name]=option; else return new([],0,false); }
        using var doc=await ReadAsync(ResourceUri(addon,"catalog",catalog.Type,catalog.Id,extra),ct); var entries=A(doc.RootElement,"metas").Take(500).ToArray();
        var items=entries.Where(e=>S(e,"id")!="" && S(e,"name")!="").Select(e=>Item(addon,e,catalog.Type,addon.Manifest.Name+" · "+catalog.Name)).DistinctBy(item=>item.Id).ToList();
        return new(items,entries.Length,catalog.Supports("skip") && entries.Length>0);
    }
    public async Task<MediaDetails> MetadataAsync(StremioAddon addon,MediaItem item,CancellationToken ct=default)
    {
        var type=AddonIdentity.Type(item); using var doc=await ReadAsync(ResourceUri(addon,"meta",type,item.ContentId),ct);
        if(!doc.RootElement.TryGetProperty("meta",out var meta) || meta.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("No title details were returned by this addon.");
        var enriched=Item(addon,meta,type,item.Category); var episodes=new List<MediaItem>();
        foreach(var video in A(meta,"videos").Take(3000))
        {
            if(S(video,"id")=="") continue; int? season=int.TryParse(S(video,"season"),out var s) ? s : null,number=int.TryParse(S(video,"episode"),out var n) ? n : null;
            episodes.Add(new($"addon:series:{S(video,"id")}",addon.Id,S(video,"title"),item.Category,MediaKind.Episode,"") { ContentId=S(video,"id"),ContentType="series",SeriesContentId=item.ContentId,Season=season,Episode=number,ProviderId=S(video,"id"),Overview=S(video,"overview"),Logo=WebUrl(S(video,"thumbnail")),Year=S(video,"released") });
        }
        return new(enriched.Overview,enriched.Cast,enriched.Rating,episodes) { ContentId=enriched.ContentId,Poster=enriched.Logo,Backdrop=enriched.Backdrop,Year=enriched.Year,Runtime=enriched.Runtime,Genres=enriched.Genres };
    }
    public async Task<List<AddonStream>> StreamsAsync(StremioAddon addon,string type,string id,CancellationToken ct=default)
    {
        if(!addon.Manifest.Supports("stream",type,id)) return [];
        using var doc=await ReadAsync(ResourceUri(addon,"stream",type,id),ct); var results=new List<AddonStream>();
        foreach(var stream in A(doc.RootElement,"streams").Take(300))
        {
            var url=WebUrl(S(stream,"url")); var external=WebUrl(S(stream,"externalUrl")); var hash=S(stream,"infoHash"); if(!Regex.IsMatch(hash,@"^[a-fA-F0-9]{40}$")) hash="";
            var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if(stream.TryGetProperty("behaviorHints",out var hints) && hints.TryGetProperty("proxyHeaders",out var proxy) && proxy.TryGetProperty("request",out var request) && request.ValueKind==JsonValueKind.Object)
                foreach(var header in request.EnumerateObject().Take(32)) if(header.Value.ValueKind==JsonValueKind.String && Regex.IsMatch(header.Name,@"^[!#$%&'*+.^_`|~0-9A-Za-z-]{1,80}$") && !new[]{"Host","Content-Length","Connection","Transfer-Encoding"}.Contains(header.Name,StringComparer.OrdinalIgnoreCase) && !header.Value.GetString()!.Any(c=>c is '\r' or '\n' or '\0')) headers[header.Name]=header.Value.GetString()!;
            var subtitles=A(stream,"subtitles").Select(e=>new MediaSubtitle(S(e,"id"),S(e,"lang"),WebUrl(S(e,"url")))).Where(s=>s.Url!="").Take(32).ToList();
            var unsupported=url!="" || external!="" || hash!="" ? "" : S(stream,"ytId")!="" ? "YouTube playback requires an external player." : "This result uses a transport that NullTV cannot play directly.";
            if(external=="" && S(stream,"ytId") is { Length:>0 } youtube && Regex.IsMatch(youtube,@"^[A-Za-z0-9_-]{6,20}$")) { external="https://www.youtube.com/watch?v="+youtube; unsupported=""; }
            results.Add(new(S(stream,"name") is { Length:>0 } name ? name : addon.Manifest.Name,S(stream,"description") is { Length:>0 } description ? description : S(stream,"title"),url,external,hash,int.TryParse(S(stream,"fileIdx"),out var index) && index>=0 ? index : null,headers,subtitles,unsupported));
        }
        return results;
    }
}

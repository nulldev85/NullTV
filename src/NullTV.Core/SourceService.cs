using System.IO.Compression;

namespace NullTV.Core;
public sealed class SourceService(HttpClient http, SettingsStore store)
{
    public async Task<Catalog> RefreshAsync(Source source, CancellationToken ct = default)
    {
        Catalog result;
        if(source.Kind==SourceKind.Xtream) result=await new XtreamClient(http).LoadAsync(source,ct);
        else if(source.Kind is SourceKind.PlaylistFile or SourceKind.PlaylistUrl)
        {
            Stream stream;
            HttpResponseMessage? response=null;
            if(source.Kind==SourceKind.PlaylistFile) stream=new FileStream(source.Address,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
            else { if(!Uri.TryCreate(source.Address,UriKind.Absolute,out var u) || u.Scheme is not ("http" or "https")) throw new InvalidDataException("Enter an HTTP or HTTPS playlist URL."); response=await http.GetAsync(u,HttpCompletionOption.ResponseHeadersRead,ct); if(!response.IsSuccessStatusCode) { var code=(int)response.StatusCode; response.Dispose(); throw new InvalidDataException($"Playlist request returned HTTP {code}."); } stream=await response.Content.ReadAsStreamAsync(ct); }
            try { await using var owned=stream; using var reader=new StreamReader(stream); var parser=new M3uParser(); var list=new List<MediaItem>(); await foreach(var item in parser.ParseAsync(reader,source.Id,ct)) list.Add(item); if(list.Count==0) throw new InvalidDataException("No valid EXTINF entries were found in this playlist."); result=new(list,parser.EpgUrl,$"{list.Count:N0} entries; {parser.Skipped} skipped",null); }
            finally { response?.Dispose(); }
        }
        else throw new InvalidDataException("Connect media servers from the Media Servers tab.");
        source.LastRefresh=DateTimeOffset.UtcNow; source.AccountStatus=result.Status; source.Expires=result.Expires;
        await store.SaveCacheAsync(source.Id,result); return result;
    }
    public async Task<List<Programme>> LoadEpgAsync(Source s, Catalog catalog, CancellationToken ct=default)
    {
        var address=s.EpgOverride!="" ? s.EpgOverride : catalog.EpgUrl;
        if(string.IsNullOrWhiteSpace(address)) return [];
        if(!Uri.TryCreate(address,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https")) throw new InvalidDataException("EPG must be an HTTP or HTTPS XMLTV URL.");
        using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct); if(!response.IsSuccessStatusCode) throw new InvalidDataException($"EPG request returned HTTP {(int)response.StatusCode}.");
        await using var raw=await response.Content.ReadAsStreamAsync(ct);
        if(uri.AbsolutePath.EndsWith(".gz",StringComparison.OrdinalIgnoreCase)) { await using var gzip=new GZipStream(raw,CompressionMode.Decompress); return await XmlTvParser.ParseAsync(gzip,DateTimeOffset.UtcNow.AddDays(-7),DateTimeOffset.UtcNow.AddDays(7),ct); }
        return await XmlTvParser.ParseAsync(raw,DateTimeOffset.UtcNow.AddDays(-7),DateTimeOffset.UtcNow.AddDays(7),ct);
    }
    public async Task VerifyCatchupAsync(Source s,MediaItem item,Programme p,CancellationToken ct=default)
    {
        if(s.Kind==SourceKind.Xtream) { var listing=await new XtreamClient(http).EpgAsync(s,item,true,ct); if(!listing.Any(x=>Math.Abs((x.Start-p.Start).TotalMinutes)<1)) throw new InvalidDataException("The provider's archive response does not confirm this programme."); }
        var url=XtreamClient.CatchupUrl(s,item,p); using var request=new HttpRequestMessage(HttpMethod.Get,url); request.Headers.Range=new(0,1023);
        foreach(var h in item.Headers) request.Headers.TryAddWithoutValidation(h.Key,h.Value);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct); if(!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is "text/html" or "application/json") throw new InvalidDataException("The provider did not return an archived media stream for this programme.");
    }
}

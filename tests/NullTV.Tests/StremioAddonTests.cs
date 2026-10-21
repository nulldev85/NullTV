using System.Net;
using System.Text;
using System.Text.Json;
using NullTV.Core;

internal static class StremioAddonTests
{
    private const string Manifest="""
    {"id":"org.nulltv.fixture","name":"Studio Addon","version":"1.0.0","types":["movie","series"],"resources":["catalog",{"name":"meta","types":["movie","series"],"idPrefixes":["tt","studio:"]},{"name":"stream","types":["movie","series"],"idPrefixes":["tt"]}],"catalogs":[{"id":"featured","name":"Featured","type":"movie","extra":[{"name":"search"},{"name":"skip"},{"name":"genre","options":["Drama","Science Fiction"]}]},{"id":"search-only","name":"Search","type":"series","extra":[{"name":"search","isRequired":true}]}],"behaviorHints":{"configurable":true}}
    """;
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Requests { get; }=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) { ct.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(response(request)); }
    }
    private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK) { Content=new StringContent(body,Encoding.UTF8,"application/json") };
    private static StremioAddon Addon(AddonManifest manifest)=>new() { ManifestUrl="https://addon.fixture/configured-secret/manifest.json?token=fixture",Manifest=manifest };
    public static void Register(Action<string,Func<Task>> test,Action<bool,string> check)
    {
        test("Addon install URLs normalize without losing configured paths or query",()=> {
            check(StremioClient.ManifestUri("stremio://addon.fixture/secret/manifest.json?key=a%2Fb").AbsoluteUri=="https://addon.fixture/secret/manifest.json?key=a%2Fb","Configured URL changed");
            check(StremioClient.ManifestUri("https://addon.fixture/config").AbsolutePath=="/config/manifest.json","Base URL did not acquire a manifest path");
            check(StremioClient.ManifestUri("stremio://127.0.0.1:18916/catalog/manifest.json").AbsoluteUri=="http://127.0.0.1:18916/catalog/manifest.json","Local addon install scheme changed");
            foreach(var url in new[]{"file:///secret","javascript:alert(1)","https://user:pass@addon.fixture/manifest.json","https://addon.fixture/configure"}) { try { StremioClient.ManifestUri(url); throw new Exception("Unsafe or configuration-page URL accepted"); } catch(InvalidDataException) { } }
            return Task.CompletedTask;
        });
        test("Addon complex resources filter content types and ID prefixes",async ()=> {
            using var http=new HttpClient(new Handler(_=>Json(Manifest))); var manifest=await new StremioClient(http).ManifestAsync("https://addon.fixture/manifest.json");
            check(manifest.Supports("stream","series","tt1234567:0:1") && !manifest.Supports("stream","movie","tmdb:1") && !manifest.Supports("stream","tv","tt1234567"),"Resource routing ignored capabilities");
            check(manifest.Catalogs.Count==2 && manifest.Catalogs[0].Supports("skip") && manifest.Configurable,"Catalog or configuration capabilities were lost");
        });
        test("Addon resource paths preserve configuration and encode search and episode identity",async ()=> {
            using var http=new HttpClient(new Handler(_=>Json(Manifest))); var addon=Addon(await new StremioClient(http).ManifestAsync("https://addon.fixture/manifest.json"));
            var uri=StremioClient.ResourceUri(addon,"catalog","movie","featured",new Dictionary<string,string> { ["search"]="Space & Time/Part 2" });
            check(uri.AbsoluteUri.Contains("/configured-secret/catalog/movie/featured/search=Space%20%26%20Time%2FPart%202.json") && uri.Query=="?token=fixture","Addon configuration or escaped extras changed");
            check(Uri.UnescapeDataString(StremioClient.ResourceUri(addon,"stream","series","tt1234567:2:3").AbsolutePath).EndsWith("/stream/series/tt1234567:2:3.json"),"Episode ID was not routed intact");
        });
        test("Addon catalogs skip search-only feeds until a query and support paging",async ()=> {
            using var handler=new Handler(request=>Json(request.RequestUri!.AbsolutePath.EndsWith("manifest.json") ? Manifest : """{"metas":[{"id":"tt1234567","type":"movie","name":"Across the stars","releaseInfo":"2025","poster":"https://art.fixture/poster.png"}]}"""));
            using var http=new HttpClient(handler); var client=new StremioClient(http); var addon=Addon(await client.ManifestAsync("https://addon.fixture/manifest.json")); var before=handler.Requests.Count;
            var empty=await client.CatalogAsync(addon,addon.Manifest.Catalogs[1]); check(empty.Items.Count==0 && handler.Requests.Count==before,"Search-only catalog was requested without its required search");
            var page=await client.CatalogAsync(addon,addon.Manifest.Catalogs[0],"Across",100,"Science Fiction");
            check(page.HasMore && page.Received==1 && page.Items[0].ContentId=="tt1234567" && page.Items[0].Year=="2025","Catalog identity or paging was lost");
            check(handler.Requests.Last().Contains("skip=100") && handler.Requests.Last().Contains("genre=Science%20Fiction"),"Catalog extras were not sent");
        });
        test("Addon series metadata retains specials and opaque video IDs",async ()=> {
            using var http=new HttpClient(new Handler(request=>Json(request.RequestUri!.AbsolutePath.EndsWith("manifest.json") ? Manifest : """{"meta":{"id":"tt1234567","name":"Studio stories","type":"series","description":"A series","videos":[{"id":"tt1234567:0:1","title":"Special","season":0,"episode":1},{"id":"studio:opaque:episode","title":"Pilot","season":1,"episode":1}]}}""")));
            var client=new StremioClient(http); var addon=Addon(await client.ManifestAsync("https://addon.fixture/manifest.json")); var item=new MediaItem("s",addon.Id,"Studio stories","Series",MediaKind.Series,"") { ContentId="tt1234567" };
            var details=await client.MetadataAsync(addon,item);
            check(details.Episodes.Count==2 && details.Episodes[0].Season==0 && details.Episodes[1].ContentId=="studio:opaque:episode","Specials or opaque video identity was changed");
            check(AddonIdentity.VideoId(details.Episodes[0],"tt1234567")=="tt1234567:0:1","Special episode request was wrong");
        });
        test("Addon streams preserve headers and subtitles without accepting executable URLs",async ()=> {
            const string streams="""
            {"streams":[{"name":"1080p","description":"Original quality","url":"https://video.fixture/movie.mkv","behaviorHints":{"proxyHeaders":{"request":{"Cookie":"a=1, b=2","Authorization":"Bearer fixture","Referer":"https://addon.fixture/","Host":"other.fixture","X-Bad":"a\r\nb"}}},"subtitles":[{"id":"en","lang":"eng","url":"https://subs.fixture/english.srt"}]},{"name":"Torrent","infoHash":"0123456789abcdef0123456789abcdef01234567","fileIdx":2},{"name":"External","externalUrl":"https://watch.fixture/title"},{"name":"Unsafe","url":"file:///private/movie.mp4"}]}
            """;
            using var http=new HttpClient(new Handler(request=>Json(request.RequestUri!.AbsolutePath.EndsWith("manifest.json") ? Manifest : streams))); var client=new StremioClient(http); var addon=Addon(await client.ManifestAsync("https://addon.fixture/manifest.json"));
            var results=await client.StreamsAsync(addon,"movie","tt1234567");
            check(results.Count==4 && results[0].Headers["Cookie"]=="a=1, b=2" && results[0].Headers["Authorization"]=="Bearer fixture","Playback headers changed");
            check(!results[0].Headers.ContainsKey("Host") && !results[0].Headers.ContainsKey("X-Bad") && results[0].Subtitles.Count==1,"Unsafe headers or subtitle metadata were mishandled");
            check(results[1].InfoHash.Length==40 && results[1].FileIndex==2 && results[2].ExternalUrl!="" && results[3].Unsupported!="","Transport types were silently treated as direct URLs");
        });
        test("Addon errors do not contain configured secrets and invalid manifests fail",async ()=> {
            using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.Unauthorized))); try { await new StremioClient(http).ManifestAsync("https://addon.fixture/private-secret/manifest.json"); throw new Exception("Denied addon accepted"); } catch(InvalidDataException e) { check(!e.Message.Contains("private-secret"),"Configured secret appeared in an error"); }
            using var invalid=new HttpClient(new Handler(_=>Json("{}"))); try { await new StremioClient(invalid).ManifestAsync("https://addon.fixture/manifest.json"); throw new Exception("Incomplete manifest accepted"); } catch(InvalidDataException) { }
        });
        test("Server episodes use series identity for addon lookup",async ()=> {
            using var http=new HttpClient(new Handler(request=>Json(request.RequestUri!.AbsolutePath.Contains("/series") ? """{"ProviderIds":{"Imdb":"tt2222222"}}""" : """{"Name":"Pilot","SeriesId":"series","ProviderIds":{"Imdb":"tt1111111"}}""")));
            var server=new Source { Kind=SourceKind.Jellyfin,Address="https://server.fixture/",ApiKey="fixture",UserId="user" }; var episode=new MediaItem("episode",server.Id,"Pilot","Library",MediaKind.Episode,"") { ProviderId="episode",Season=1,Episode=2 };
            var details=await new MediaServerClient(http).DetailsAsync(server,episode,default);
            check(details.ContentId=="tt2222222" && AddonIdentity.VideoId(episode,details.ContentId)=="tt2222222:1:2","Episode IMDb ID was mistaken for its series ID");
        });
        test("Addon settings and title mappings survive encrypted profile storage",async ()=> {
            using var http=new HttpClient(new Handler(_=>Json(Manifest))); var addon=Addon(await new StremioClient(http).ManifestAsync("https://addon.fixture/manifest.json"));
            var directory=Path.Combine(Path.GetTempPath(),"NullTV-addons-"+Guid.NewGuid().ToString("N"));
            try {
                var settings=new Settings(); settings.Profile.Addons.Add(addon); settings.Profile.AddonTitleMappings["server:title"]="tt1234567"; var store=new SettingsStore(directory); await store.SaveAsync(settings);
                var restored=await store.LoadAsync(); check(restored.Profile.Addons[0].ManifestUrl==addon.ManifestUrl && restored.Profile.AddonTitleMappings["server:title"]=="tt1234567","Addon settings were not retained");
                check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory,"settings.dat"))).Contains("configured-secret"),"Manifest configuration was stored as plaintext");
                var legacy=JsonSerializer.Deserialize<Settings>("{\"StartupTab\":\"Media Servers\"}")!; legacy.Validate(); check(legacy.StartupTab=="Integrations" && legacy.Profile.Addons.Count==0,"Existing settings did not migrate cleanly");
            } finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
        });
    }
}

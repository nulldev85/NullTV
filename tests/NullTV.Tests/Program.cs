using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using NullTV.Core;

var tests=new List<(string Name,Func<Task> Test)>();
void Test(string name,Func<Task> test)=>tests.Add((name,test));
void Check(bool condition,string message="Assertion failed") { if(!condition) throw new Exception(message); }
async Task<List<MediaItem>> Parse(string text,M3uParser? parser=null) { var result=new List<MediaItem>(); await foreach(var item in (parser??new()).ParseAsync(new StringReader(text),"fixture")) result.Add(item); return result; }
Test("M3U quoted attributes, comma and BOM",async ()=> { var p=new M3uParser(); var list=await Parse("\uFEFF#EXTM3U x-tvg-url=\"https://fixture.invalid/epg\"\n#EXTINF:-1 tvg-id=\"id\" group-title=\"A, B\" tvg-logo=\"https://fixture.invalid/icon\",Channel, one\nhttps://fixture.invalid/a.m3u8",p); Check(list.Count==1 && list[0].Name=="Channel, one" && list[0].Category=="A, B" && list[0].EpgId=="id" && p.EpgUrl.EndsWith("/epg")); });
Test("M3U VLC and Kodi headers",async ()=> { var list=await Parse("#EXTM3U\n#EXTINF:-1,Test\n#EXTVLCOPT:http-user-agent=NullTV\n#KODIPROP:inputstream.adaptive.stream_headers=Referer=https%3A%2F%2Ffixture.invalid&User-Agent=Agent%20X\nhttps://fixture.invalid/x"); Check(list[0].Headers["User-Agent"]=="Agent X" && list[0].Headers["Referer"]=="https://fixture.invalid"); });
Test("M3U headers do not leak between items",async ()=> { var list=await Parse("#EXTINF:-1,A\n#EXTVLCOPT:http-user-agent=Agent\nhttps://fixture.invalid/a\n#EXTINF:-1,B\nhttps://fixture.invalid/b"); Check(list[1].Headers.Count==0); });
Test("M3U malformed entries skip and recover",async ()=> { var p=new M3uParser(); var list=await Parse("nonsense\n#EXTINF:-1,Bad\nfile:///private\n#EXTINF:-1,Good\nrtsp://fixture.invalid/test",p); Check(list.Count==1 && p.Skipped==2); });
Test("M3U catchup attributes and pipe headers",async ()=> { var list=await Parse("#EXTINF:-1 catchup='default' catchup-days='3' catchup-source='https://fixture.invalid/archive?start={utc}',Catchup\nhttps://fixture.invalid/a|User-Agent=Test%20Agent"); Check(list[0].ArchiveDays==3 && list[0].Headers["User-Agent"]=="Test Agent"); });
Test("M3U stable IDs",async ()=> { var a=await Parse("#EXTINF:-1,A\nhttps://fixture.invalid/a"); var b=await Parse("#EXTINF:-1,Renamed\nhttps://fixture.invalid/a"); Check(a[0].Id==b[0].Id); });
Test("M3U cancellation",async ()=> { using var c=new CancellationTokenSource(); c.Cancel(); try { await foreach(var i in new M3uParser().ParseAsync(new StringReader("#EXTINF:-1,A\nhttps://fixture.invalid/a"),"s",c.Token)) { } throw new Exception("Cancellation missing"); } catch(OperationCanceledException) { } });
Test("M3U 50,000 streaming entries",async ()=> { var text=new StringBuilder("#EXTM3U\n"); for(int i=0;i<50000;i++) text.Append($"#EXTINF:-1 tvg-id=\"c{i}\" group-title=\"Large\",Channel {i}\nhttps://fixture.invalid/{i}\n"); var watch=Stopwatch.StartNew(); var list=await Parse(text.ToString()); Check(list.Count==50000); Console.WriteLine($"  50,000 parsed in {watch.ElapsedMilliseconds} ms; IDs unique: {list.Select(i=>i.Id).Distinct().Count()}"); });
Test("XMLTV offsets and filtering",async ()=> { var xml="<tv><programme channel='id' start='20261011120000 +0530' stop='20261011130000 +0530'><title>Fixture</title><desc>Details</desc></programme><programme channel='bad' start='garbage' stop='garbage'/></tv>"; var result=await XmlTvParser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(xml)),new(2026,10,10,0,0,0,TimeSpan.Zero),new(2026,10,12,0,0,0,TimeSpan.Zero)); Check(result.Count==1 && result[0].Start.UtcDateTime.Hour==6 && result[0].Start.UtcDateTime.Minute==30 && result[0].Description=="Details"); });
Test("XMLTV rejects DTD entities",async ()=> { try { await XmlTvParser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE tv [<!ENTITY x SYSTEM 'file:///private'>]><tv/>")),DateTimeOffset.MinValue,DateTimeOffset.MaxValue); throw new Exception("DTD accepted"); } catch(System.Xml.XmlException) { } });
Test("XMLTV invalid timezone rejected",()=> { Check(!XmlTvParser.TryTime("20261011120000 +1460",out _) && !XmlTvParser.TryTime("20261011120000 +1500",out _)); return Task.CompletedTask; });
Test("Xtream fixture catalog and credentials encoding",async ()=> { using var http=new HttpClient(new FixtureHandler()); var s=new Source { Id="fixture",Address="https://fixture.invalid",Username="a/b",Password="p &?" }; var c=await new XtreamClient(http).LoadAsync(s); Check(c.Items.Count==1 && c.Status=="Active" && c.Items[0].ArchiveDays==7 && c.Items[0].Url.Contains("a%2Fb/p%20%26%3F/7.ts")); Check(XtreamClient.ApiUri(s).Query.Contains("password=p%20%26%3F")); });
Test("Xtream account denial",async ()=> { using var http=new HttpClient(new FixtureHandler(true)); try { await new XtreamClient(http).LoadAsync(new Source { Address="https://fixture.invalid" }); throw new Exception("Denied account accepted"); } catch(InvalidDataException) { } });
Test("Xtream series episodes fixture",async ()=> { using var http=new HttpClient(new FixtureHandler()); var s=new Source { Id="fixture",Address="https://fixture.invalid",Username="u",Password="p" }; var d=await new XtreamClient(http).DetailsAsync(s,new("x",s.Id,"Series","Cat",MediaKind.Series,"") { ProviderId="1" }); Check(d.Episodes.Count==1 && d.Episodes[0].Url.EndsWith("/88.mkv") && d.Rating=="8.5"); });
Test("Xtream catchup window validation",()=> { var s=new Source { Address="https://fixture.invalid",Username="u",Password="p" }; var i=new MediaItem("i",s.Id,"x","c",MediaKind.Live,"") { ArchiveDays=7,Catchup="xtream",ProviderId="7" }; var now=DateTimeOffset.UtcNow; var p=new Programme("c","x","",now.AddHours(-2),now.AddHours(-1)); Check(XtreamClient.CatchupUrl(s,i,p).Contains("timeshift/u/p/60/")); try { XtreamClient.CatchupUrl(s,i,p with { Start=DateTimeOffset.UtcNow.AddDays(-9) }); throw new Exception("Outside archive accepted"); } catch(InvalidDataException) { } return Task.CompletedTask; });
Test("Settings serialization and validation",()=> { var s=new Settings(); s.Profile.Sources.Add(new() { Password="synthetic-secret" }); s.Playback.Volume=300; var json=JsonSerializer.Serialize(s,SettingsStore.JsonOptions); var r=JsonSerializer.Deserialize<Settings>(json)!; r.Validate(); Check(r.Profile.Sources[0].Password=="synthetic-secret" && r.Playback.Volume==100 && !json.Contains("\"Profile\":")); return Task.CompletedTask; });
Test("Encrypted backup round trip",()=> { var s=new Settings(); s.Profile.Sources.Add(new() { Password="synthetic-secret" }); var b=SettingsStore.Backup(s,"long fixture password"); Check(!Encoding.UTF8.GetString(b).Contains("synthetic-secret")); Check(SettingsStore.Restore(b,"long fixture password").Profile.Sources[0].Password=="synthetic-secret"); return Task.CompletedTask; });
Test("Encrypted backup tamper and wrong password",()=> { var b=SettingsStore.Backup(new(),"long fixture password"); try { SettingsStore.Restore(b,"wrong password"); throw new Exception("Wrong password accepted"); } catch(System.Security.Cryptography.CryptographicException) { } b[^1]^=1; try { SettingsStore.Restore(b,"long fixture password"); throw new Exception("Tamper accepted"); } catch(System.Security.Cryptography.CryptographicException) { } return Task.CompletedTask; });
if(OperatingSystem.IsWindows()) Test("DPAPI settings round trip",async ()=> { var dir=Path.Combine(Path.GetTempPath(),"NullTV-test-"+Guid.NewGuid().ToString("N")); var store=new SettingsStore(dir); var s=new Settings(); s.Profile.Sources.Add(new() { Password="synthetic-secret" }); await store.SaveAsync(s); Check((await store.LoadAsync()).Profile.Sources[0].Password=="synthetic-secret"); Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(dir,"settings.dat"))).Contains("synthetic-secret")); Directory.Delete(dir,true); });
int failed=0;
Test("M3U header catchup inheritance and tvg-name",async ()=> { var list=await Parse("#EXTM3U catchup='default' catchup-days='7'\n#EXTINF:-1 tvg-name='Inherited',\nhttps://fixture.invalid/a"); Check(list[0].Name=="Inherited" && list[0].ArchiveDays==7 && list[0].Catchup=="default"); });
Test("Settings reject path traversal source IDs",()=> { var s=new Settings(); s.Profile.Sources.Add(new() { Id="../../private" }); try { s.Validate(); throw new Exception("Unsafe ID accepted"); } catch(InvalidDataException) { } return Task.CompletedTask; });
Test("Settings reject null collections",()=> { var s=JsonSerializer.Deserialize<Settings>("{\"Profiles\":null}")!; try { s.Validate(); throw new Exception("Null accepted"); } catch(InvalidDataException) { } return Task.CompletedTask; });
Test("Xtream live HLS URL",async ()=> { using var http=new HttpClient(new FixtureHandler()); var s=new Source { Address="https://fixture.invalid",LiveExtension="m3u8" }; var c=await new XtreamClient(http).LoadAsync(s); Check(c.Items[0].Url.EndsWith("7.m3u8")); });
Test("Jellyfin API auth, resume import, and watched sync",async ()=> { var handler=new ServerFixtureHandler(); using var http=new HttpClient(handler); var s=new Source { Kind=SourceKind.Jellyfin,Address="https://fixture.invalid",ApiKey="fixture-token",UserId="fixture-user" }; var client=new MediaServerClient(http); var c=await client.BrowseAsync(s,default); Check(c.Items.Count==1 && c.RemoteHistory[c.Items[0].Id].Position==60 && c.Items[0].Url.Contains("static=true")); await client.WatchedAsync(s,c.Items[0],true,default); Check(handler.SawToken && handler.SawWatched); });
foreach(var (name,test) in tests) { try { await test(); Console.WriteLine("PASS "+name); } catch(Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); } }
Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed"); return failed==0 ? 0 : 1;

sealed class FixtureHandler(bool denied=false) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        var query=request.RequestUri!.Query; string text;
        if(!query.Contains("action=")) text=denied ? "{\"user_info\":{\"auth\":0}}" : await Fixture("account.json");
        else if(query.Contains("categories")) text=await Fixture("categories.json");
        else if(query.Contains("get_live_streams")) text=await Fixture("live.json");
        else if(query.Contains("get_series_info")) text=await Fixture("series.json");
        else text="[]";
        return new(HttpStatusCode.OK) { Content=new StringContent(text,Encoding.UTF8,"application/json") };
    }
    private static Task<string> Fixture(string name)=>File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures",name));
}
sealed class ServerFixtureHandler : HttpMessageHandler
{
    public bool SawToken,SawWatched;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        SawToken|=request.Headers.TryGetValues("X-Emby-Token",out var tokens) && tokens.Contains("fixture-token"); SawWatched|=request.Method==HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("PlayedItems");
        var json="{\"Items\":[{\"Id\":\"movie-1\",\"Type\":\"Movie\",\"Name\":\"Synthetic movie\",\"RunTimeTicks\":6000000000,\"UserData\":{\"PlaybackPositionTicks\":600000000,\"Played\":false,\"LastPlayedDate\":\"2026-10-11T00:00:00Z\"}}]}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(json,Encoding.UTF8,"application/json") });
    }
}

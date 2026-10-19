using NullTV.Core;

internal static class GuidePresentationTests
{
    public static void Register(Action<string,Func<Task>> test,Action<bool,string> check)
    {
        test("Quality badges never invent missing metadata or parse words as HD",()=> {
            var item=new MediaItem("a","s","World News","",MediaKind.Live,"https://fixture.invalid/a");
            check(ChannelQuality.Labels(item,null,DateTimeOffset.UtcNow).Count==0,"Unknown stream received a quality badge");
            check(ChannelQuality.Labels(item with { Name="UHD60 News" },null,DateTimeOffset.UtcNow).Count==0,"Partial word was treated as measured quality");
            var labels=ChannelQuality.Labels(item with { Name="World News FHD 60FPS" },null,DateTimeOffset.UtcNow);
            check(labels.Select(l=>l.Text).SequenceEqual(new[]{"FHD","60 FPS"}) && labels.All(l=>l.Description.StartsWith("Provider label")),"Provider labels lost their provenance");
            return Task.CompletedTask;
        });
        test("Measured resolution and fractional FPS override advertised quality",()=> {
            var now=DateTimeOffset.UtcNow; var item=new MediaItem("a","s","News UHD 60FPS","",MediaKind.Live,"https://fixture.invalid/a");
            var measured=new StreamObservation(1280,720,59.94,"bt.1886",ChannelQuality.Fingerprint(item.Url),now);
            var labels=ChannelQuality.Labels(item,measured,now);
            check(labels.Select(l=>l.Text).SequenceEqual(new[]{"HD","59.94 FPS"}),"Measured values did not override provider labels");
            check(ChannelQuality.Labels(item,measured with { Transfer="pq" },now).Last().Text=="HDR","Verified PQ transfer was ignored");
            return Task.CompletedTask;
        });
        test("Quality cache rejects a changed URL, stale data and invalid frame rates",()=> {
            var now=DateTimeOffset.UtcNow; var item=new MediaItem("a","s","News","",MediaKind.Live,"https://fixture.invalid/a");
            var measured=new StreamObservation(1920,1080,29.97,"",ChannelQuality.Fingerprint(item.Url),now);
            check(ChannelQuality.Labels(item with { Url="https://fixture.invalid/b" },measured,now).Count==0,"Old URL reused stream facts");
            check(ChannelQuality.Labels(item,measured with { MeasuredUtc=now.AddDays(-31) },now).Count==0,"Expired measurement was shown");
            check(ChannelQuality.Labels(item,measured with { FrameRate=double.NaN },now).Count==0,"Non-finite frame rate was shown");
            return Task.CompletedTask;
        });
        test("Guide categories isolate sources and retain favorite and recent order",()=> {
            var now=DateTimeOffset.UtcNow; var channels=new[]{new MediaItem("a","one","A","Sports",MediaKind.Live,""),new MediaItem("b","two","B","Sports",MediaKind.Live,""),new MediaItem("c","one","C","News",MediaKind.Live,""),new MediaItem("movie","one","Film","Sports",MediaKind.Movie,"")};
            var profile=new Profile { Favorites=["a","c"],FavoriteGuideOrder=["c","a"],History=new() { ["a"]=new(0,0,false,now.AddHours(-1)),["b"]=new(0,0,false,now) } };
            check(ChannelBrowse.Select(channels,profile,new(ChannelScope.Category,"one","Sports")).Select(i=>i.Id).SequenceEqual(new[]{"a"}),"Same-named categories leaked across sources");
            check(ChannelBrowse.Select(channels,profile,new(ChannelScope.Favorites)).Select(i=>i.Id).SequenceEqual(new[]{"c","a"}),"Favorite custom order changed");
            check(ChannelBrowse.Select(channels,profile,new(ChannelScope.Recent)).Select(i=>i.Id).SequenceEqual(new[]{"b","a"}),"Recently watched order changed");
            var categories=ChannelBrowse.Categories(channels,[new Source { Id="one",Name="One" },new Source { Id="two",Name="Two" }]);
            check(categories.Length==3 && categories.Sum(c=>c.Count)==3,"Category counts included on-demand content");
            return Task.CompletedTask;
        });
        test("Guide geometry clips long and midnight programs to a common time scale",()=> {
            var start=new DateTimeOffset(2026,10,18,23,0,0,TimeSpan.FromHours(-7));
            check(GuideGeometry.Segment(start.AddHours(-1),start.AddHours(2),start)==(0,480),"Long program did not clip to the left edge");
            check(GuideGeometry.Segment(start.AddHours(1),start.AddHours(1.5),start)==(240,120),"Midnight program was misplaced");
            check(GuideGeometry.Segment(start.AddHours(5),start.AddHours(8),start)==(1200,240),"Program did not clip to the right edge");
            check(GuideGeometry.Segment(start.AddHours(-2),start.AddHours(-1),start)==null && GuideGeometry.Segment(start,start,start)==null,"Out-of-window or invalid program was rendered");
            return Task.CompletedTask;
        });
        test("Live progress meets the now line even when the program starts before the window",()=> {
            var window=new DateTimeOffset(2026,10,18,21,30,0,TimeSpan.FromHours(-7)); var now=window.AddMinutes(67.5);
            foreach(var start in new[]{window.AddMinutes(30),window.AddMinutes(-30)}) {
                var segment=GuideGeometry.Segment(start,window.AddMinutes(90),window)!.Value;
                var left=segment.Left+2; var width=segment.Width-4;
                check(Math.Abs(left+GuideGeometry.ElapsedWidth(now,window,left,width)-GuideGeometry.Position(now,window))<.001,"Progress endpoint drifted from the shared clock coordinate");
            }
            return Task.CompletedTask;
        });
    }
}

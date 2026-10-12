using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NullTV.Core;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private DateTimeOffset guideStart=DateTimeOffset.Now.AddMinutes(-DateTimeOffset.Now.Minute%30);
    private UIElement BuildGuide()
    {
        var page=new Grid { RowSpacing=16 }; page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        Place(page,Heading("Guide","See what is on. Find what is next.",AsyncButton("Refresh guide",()=>RefreshAllAsync(true))));
        var search=new AutoSuggestBox { PlaceholderText="Search programme titles",MinHeight=44,Width=300 };
        var controls=new StackPanel { Orientation=Orientation.Horizontal,Spacing=10 }; controls.Children.Add(IconButton("\uE76B","Previous two hours",()=> { guideStart=guideStart.AddHours(-2); Navigate("Guide"); })); controls.Children.Add(Button("Now",()=> { guideStart=DateTimeOffset.Now; Navigate("Guide"); })); controls.Children.Add(IconButton("\uE76C","Next two hours",()=> { guideStart=guideStart.AddHours(2); Navigate("Guide"); })); var date=Text(guideStart.LocalDateTime.ToString("ddd, dd MMM  ·  HH:mm"),13); date.VerticalAlignment=VerticalAlignment.Center; date.Margin=new Thickness(8,0,16,0); controls.Children.Add(date); controls.Children.Add(search); Place(page,Card(controls,new Thickness(12)),1);
        var list=new ListView { SelectionMode=ListViewSelectionMode.None }; Place(page,Card(list,new Thickness(14)),2);
        var empty=Card(EmptyState("\uE787","Your day, at a glance.","Connect a source with programme information to browse schedules, set reminders and explore catch-up.","Add a source",()=>EditSourceAsync(null))); Place(page,empty,2);
        void Populate()
        {
            list.Items.Clear();
            var programmes=epgs.Values.SelectMany(x=>x).ToList();
            empty.Visibility=programmes.Count==0 ? Visibility.Visible : Visibility.Collapsed;
            if(programmes.Count==0) return;
            var header=new StackPanel { Orientation=Orientation.Horizontal,Spacing=0 }; header.Children.Add(Text("Channel",14)); header.Children.Add(Text($"  {guideStart.LocalDateTime:ddd dd MMM HH:mm}    →    {guideStart.AddHours(2).LocalDateTime:HH:mm}",14)); list.Items.Add(header);
            var count=0;
            foreach(var channel in items.Where(i=>i.Kind==MediaKind.Live))
            {
                var source=settings.Profile.Sources.FirstOrDefault(s=>s.Id==channel.SourceId); if(source==null) continue; var id=source.EpgMappings.GetValueOrDefault(channel.Id,channel.EpgId=="" ? channel.Id : channel.EpgId);
                var shows=(epgs.GetValueOrDefault(channel.SourceId)??[]).Where(p=>p.ChannelId==id && p.End>guideStart && p.Start<guideStart.AddHours(2) && p.Title.Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToList();
                if(shows.Count==0 && search.Text!="") continue;
                var row=new Grid { MinHeight=72,ColumnSpacing=12,HorizontalAlignment=HorizontalAlignment.Stretch }; row.ColumnDefinitions.Add(new() { Width=new(170) }); row.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
                Place(row,Button(channel.Name,()=>_=GuardAsync(()=>PlayAsync(channel))));
                var timeline=new Grid { MinHeight=64 }; if(shows.Count==0) timeline.Children.Add(Text("No programmes in this time window",12));
                foreach(var show in shows)
                {
                    var start=Math.Clamp((show.Start-guideStart).TotalMinutes,0,120); var end=Math.Clamp((show.End-guideStart).TotalMinutes,0,120);
                    var cell=new Button { Content=Stack(Text(show.Title,13),Text(show.TimeLabel,11)),MinHeight=64,MinWidth=44,CornerRadius=new CornerRadius(8),Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#211F1B")),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Padding=new Thickness(10) };
                    // Three proportional columns place each programme along a two-hour timeline.
                    var segment=new Grid { IsHitTestVisible=true }; segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,start),GridUnitType.Star) }); segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,end-start),GridUnitType.Star) }); segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,120-end),GridUnitType.Star) }); Place(segment,cell,0,1); timeline.Children.Add(segment);
                    cell.Click+=(_,_)=>_=GuardAsync(()=>ProgrammeDetailsAsync(channel,show));
                }
                Place(row,timeline,0,1); list.Items.Add(row); if(++count>=1000) { list.Items.Add(Text("Showing the first 1,000 channels. Search titles to narrow the guide.",12)); break; }
            }
            if(count==0) list.Items.Add(Text("No matching programmes in this time window.",16));
        }
        search.TextChanged+=(_,_)=>Populate(); Populate(); return page;
    }
    private async Task ProgrammeDetailsAsync(MediaItem channel,Programme p)
    {
        var content=Stack(Text(p.Title,20),Text(p.TimeLabel,13),Text(p.Description=="" ? "No description supplied by the provider." : p.Description,14));
        var past=p.End<DateTimeOffset.UtcNow;
        content.Children.Add(AsyncButton("Set reminder",async ()=> { if(p.Start<=DateTimeOffset.UtcNow) throw new InvalidDataException("Reminders can be set for upcoming programmes."); if(!settings.Profile.Reminders.Any(r=>r.ChannelId==channel.Id && r.Start==p.Start)) settings.Profile.Reminders.Add(new(channel.Id,p.Title,p.Start)); await store.SaveAsync(settings); status.Text="Reminder saved. NullTV must remain open for reminders."; }));
        var result=await DialogAsync("Programme details",content,past ? "Play catch-up" : "Watch channel"); if(result!=ContentDialogResult.Primary) return;
        if(past) { var source=settings.Profile.Sources.First(s=>s.Id==channel.SourceId); await new SourceService(http,store).VerifyCatchupAsync(source,channel,p,lifetime.Token); var url=XtreamClient.CatchupUrl(source,channel,p); await PlayAsync(channel with { Url=url,Kind=MediaKind.Episode,Id=channel.Id+":"+p.Start.ToUnixTimeSeconds(),Name=p.Title }); }
        else await PlayAsync(channel);
    }
}

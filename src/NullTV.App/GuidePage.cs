using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using NullTV.Core;
namespace NullTV.App;
public sealed partial class MainWindow
{
    private DateTimeOffset guideStart=DateTimeOffset.Now.AddMinutes(-DateTimeOffset.Now.Minute%30);
    private sealed record GuideRow(MediaItem Channel,Programme[] Shows);
    private UIElement BuildGuide()
    {
        var page=new Grid { RowSpacing=16 }; page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        Place(page,Heading("Guide","What is on. What is next.",AsyncButton("Refresh guide",()=>RefreshAllAsync(true))));
        var search=SearchBox("Search programmes"); search.Width=300;
        var controls=new StackPanel { Orientation=Orientation.Horizontal,Spacing=10 };
        controls.Children.Add(IconButton("\uE76B","Previous two hours",()=> { guideStart=guideStart.AddHours(-2); Navigate("Guide"); }));
        controls.Children.Add(Button("Now",()=> { guideStart=DateTimeOffset.Now; Navigate("Guide"); }));
        controls.Children.Add(IconButton("\uE76C","Next two hours",()=> { guideStart=guideStart.AddHours(2); Navigate("Guide"); }));
        var date=Text(guideStart.LocalDateTime.ToString("ddd, dd MMM  ·  HH:mm"),13); date.VerticalAlignment=VerticalAlignment.Center; date.Margin=new Thickness(8,0,16,0); controls.Children.Add(date); controls.Children.Add(search); Place(page,Card(controls,new Thickness(12)),1);
        var list=new ListView { SelectionMode=ListViewSelectionMode.None };
        QuietItems(list); list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid MinHeight='78' Margin='0,4'/></DataTemplate>");
        list.ContainerContentChanging+=(sender,args)=>
        {
            if(args.InRecycleQueue) return;
            if(args.Phase==0) { args.RegisterUpdateCallback(1,BuildGuideContainer); return; }
            BuildGuideContainer(sender,args);
        };
        void BuildGuideContainer(ListViewBase sender,ContainerContentChangingEventArgs args)
        {
            if(args.InRecycleQueue || args.Item is not GuideRow row || args.ItemContainer.ContentTemplateRoot is not Grid grid) return;
            grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.ColumnSpacing=12;
            grid.ColumnDefinitions.Add(new() { Width=new(170) }); grid.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
            Place(grid,Button(row.Channel.Name,()=>_=GuardAsync(()=>PlayAsync(row.Channel))));
            var timeline=new Grid();
            if(row.Shows.Length==0) timeline.Children.Add(Text("No programme information",12));
            foreach(var show in row.Shows)
            {
                var start=Math.Clamp((show.Start-guideStart).TotalMinutes,0,120); var end=Math.Clamp((show.End-guideStart).TotalMinutes,0,120);
                var cell=Button("",()=>_=GuardAsync(()=>ProgrammeDetailsAsync(row.Channel,show))); cell.Content=Stack(Text(show.Title,13),Text(show.TimeLabel,11)); cell.MinHeight=66; cell.HorizontalAlignment=HorizontalAlignment.Stretch; cell.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#18191B")); cell.Padding=new Thickness(12);
                var segment=new Grid(); segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,start),GridUnitType.Star) }); segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,end-start),GridUnitType.Star) }); segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,120-end),GridUnitType.Star) }); Place(segment,cell,0,1); timeline.Children.Add(segment);
            }
            Place(grid,timeline,0,1);
        }
        Place(page,Card(list,new Thickness(14)),2);
        var empty=Card(EmptyState("\uE787","Your guide starts here.","Connect a source with programme information to browse schedules and catch-up.","Add a source",()=>EditSourceAsync(null))); Place(page,empty,2);
        CancellationTokenSource? pending=null;
        async Task PopulateAsync(bool debounce)
        {
            pending?.Cancel(); pending?.Dispose(); var request=pending=new(); var ct=request.Token;
            try
            {
                if(debounce) await Task.Delay(180,ct);
                var term=search.Text; var channelSnapshot=items.Where(i=>i.Kind==MediaKind.Live).ToArray();
                var sources=settings.Profile.Sources.ToDictionary(s=>s.Id,s=>new Dictionary<string,string>(s.EpgMappings)); var indices=guideIndices.ToDictionary(e=>e.Key,e=>e.Value.Index);
                var start=guideStart; var end=start.AddHours(2);
                var rows=await Task.Run(()=> { var found=new List<GuideRow>(); foreach(var channel in channelSnapshot) { ct.ThrowIfCancellationRequested(); var id=sources.GetValueOrDefault(channel.SourceId)?.GetValueOrDefault(channel.Id,channel.EpgId=="" ? channel.Id : channel.EpgId)??channel.EpgId; var shows=(indices.GetValueOrDefault(channel.SourceId)??EmptyGuide).Window(id,start,end).Where(p=>p.Title.Contains(term,StringComparison.OrdinalIgnoreCase)).ToArray(); if(term!="" && shows.Length==0) continue; found.Add(new(channel,shows)); if(found.Count>=1000) break; } return found.ToArray(); },ct);
                ct.ThrowIfCancellationRequested(); list.ItemsSource=rows;
                empty.Visibility=epgs.Values.Any(p=>p.Count>0) && rows.Length>0 ? Visibility.Collapsed : Visibility.Visible;
                if(term!="" && rows.Length==0) empty.Child=EmptyState("\uE721","No matching programmes.","Try a different title or time window.","Clear search",()=> { search.Text=""; _=GuardAsync(()=>PopulateAsync(false)); return Task.CompletedTask; });
            }
            catch(OperationCanceledException) { }
        }
        search.TextChanged+=(sender,e)=> { _=GuardAsync(()=>PopulateAsync(true)); };
        refreshGuide=()=>_=GuardAsync(()=>PopulateAsync(false)); list.Unloaded+=(_,_)=>pending?.Cancel(); _=GuardAsync(()=>PopulateAsync(false)); return page;
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

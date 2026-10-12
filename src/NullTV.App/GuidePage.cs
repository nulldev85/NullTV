using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;
namespace NullTV.App;
public sealed partial class MainWindow
{
    private static DateTimeOffset HalfHour(DateTimeOffset time)=>new(time.Year,time.Month,time.Day,time.Hour,time.Minute/30*30,0,time.Offset);
    private DateTimeOffset guideStart=HalfHour(DateTimeOffset.Now);
    private sealed class GuideRow(MediaItem channel,Programme[] shows,DateTimeOffset start) : ArtworkRow(channel)
    {
        public MediaItem Channel=>Item;
        public Programme[] Shows { get; }=shows;
        public DateTimeOffset Start { get; }=start;
        public override string ToString()=>Channel.Name;
    }
    private static string ProgrammeMeta(Programme show)=>string.Join(" · ",new[]{show.Category,show.Rating,show.Episode,show.IsPremiere ? "PREMIERE" : show.IsNew ? "NEW" : show.IsRepeat ? "REPEAT" : ""}.Where(value=>value!=""));
    private async Task AddReminderAsync(MediaItem channel,Programme show)
    {
        if(show.Start<=DateTimeOffset.UtcNow) throw new InvalidDataException("Reminders can be set for upcoming programmes.");
        if(!settings.Profile.Reminders.Any(reminder=>reminder.ChannelId==channel.Id && reminder.Start==show.Start)) settings.Profile.Reminders.Add(new(channel.Id,show.Title,show.Start));
        await store.SaveAsync(settings); status.Text="Reminder saved. NullTV must remain open for reminders.";
    }
    private UIElement BuildGuide()
    {
        var profile=settings.Profile;
        var page=new Grid { RowSpacing=12 };
        for(int n=0;n<3;n++) page.RowDefinitions.Add(new() { Height=GridLength.Auto });
        page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        var heading=Heading("Guide","Your favorite channels. Your order.",AsyncButton("Refresh guide",()=>RefreshAllAsync(true))); heading.Margin=new Thickness(0); Place(page,heading);
        MediaItem? selectedChannel=null; Programme? selectedShow=null;
        var info=new Grid { ColumnSpacing=24 }; info.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); info.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var infoContext=Text("SELECT A PROGRAMME",10); infoContext.CharacterSpacing=100;
        var infoTitle=Text("What is on. What is next.",21); infoTitle.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; infoTitle.TextWrapping=TextWrapping.NoWrap; infoTitle.TextTrimming=TextTrimming.CharacterEllipsis;
        var infoMeta=Text("",11); infoMeta.Foreground=new SolidColorBrush(Color(LightFields ? "#815CD6" : "#BD93F9"));
        var infoDescription=Text("Browse schedules, read programme details and watch your favorite channels.",12); infoDescription.Opacity=.7; infoDescription.MaxLines=2; infoDescription.TextTrimming=TextTrimming.CharacterEllipsis;
        var copy=Stack(infoContext,infoTitle,infoMeta,infoDescription); copy.Spacing=5; Place(info,copy);
        var watch=Primary("Watch channel",()=>selectedChannel==null ? Task.CompletedTask : PlayAsync(selectedChannel)); watch.IsEnabled=false;
        var details=AsyncButton("Full details",()=>selectedChannel==null || selectedShow==null ? Task.CompletedTask : ProgrammeDetailsAsync(selectedChannel,selectedShow)); details.IsEnabled=false;
        var infoActions=Stack(watch,details); infoActions.Spacing=8; infoActions.VerticalAlignment=VerticalAlignment.Center; Place(info,infoActions,0,1);
        var infoCard=Card(info,new Thickness(18,14,18,14)); Place(page,infoCard,1);
        void UpdateInfo()
        {
            if(selectedChannel==null || selectedShow==null) return;
            var show=selectedShow; var onNow=show.Start<=DateTimeOffset.Now && show.End>DateTimeOffset.Now;
            var state=onNow ? "ON NOW" : show.End<=DateTimeOffset.Now ? "ENDED" : "UPCOMING";
            var minutes=(int)Math.Ceiling((show.End-show.Start).TotalMinutes);
            infoContext.Text=$"{state}  ·  {selectedChannel.Number:000}  {selectedChannel.Name}  ·  {show.TimeLabel}  ·  {minutes} min"+(onNow ? $"  ·  {Math.Max(1,(int)Math.Ceiling((show.End-DateTimeOffset.Now).TotalMinutes))} min left" : "");
            infoContext.Foreground=onNow ? new SolidColorBrush(Color(LightFields ? "#089108" : "#50FA7B")) : FieldText();
            infoTitle.Text=show.Title+(show.Subtitle=="" ? "" : " — "+show.Subtitle);
            infoMeta.Text=ProgrammeMeta(show); infoMeta.Visibility=infoMeta.Text=="" ? Visibility.Collapsed : Visibility.Visible;
            infoDescription.Text=show.Description=="" ? "No description supplied by the provider." : show.Description;
            watch.IsEnabled=details.IsEnabled=true;
        }
        void SelectProgramme(MediaItem channel,Programme show) { selectedChannel=channel; selectedShow=show; UpdateInfo(); }
        var search=SearchBox("Search favorites or programmes"); search.Width=280;
        var controls=new Grid { ColumnSpacing=16 }; controls.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); controls.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); controls.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var timeActions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6 };
        timeActions.Children.Add(IconButton("\uE76B","Previous two hours",()=> { guideStart=guideStart.AddHours(-2); Navigate("Guide"); }));
        timeActions.Children.Add(Button("Now",()=> { guideStart=HalfHour(DateTimeOffset.Now); Navigate("Guide"); }));
        timeActions.Children.Add(IconButton("\uE76C","Next two hours",()=> { guideStart=guideStart.AddHours(2); Navigate("Guide"); })); Place(controls,timeActions);
        var date=Text(guideStart.LocalDateTime.ToString("ddd, dd MMM  ·  HH:mm")+"  ·  Local time",12); date.VerticalAlignment=VerticalAlignment.Center; Place(controls,date,0,1); Place(controls,search,0,2); Place(page,Card(controls,new Thickness(8)),2);
        var rows=new ObservableCollection<GuideRow>();
        var list=new ListView { ItemsSource=rows,SelectionMode=ListViewSelectionMode.None,IsSwipeEnabled=true };
        QuietItems(list); WatchArtwork(list,88);
        list.ItemContainerStyle=new Style(typeof(ListViewItem)) { Setters={ new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch),new Setter(Control.PaddingProperty,new Thickness(0)) } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list,"Favorite channel guide. Drag grips to reorder, or use channel options.");
        list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='84' Margin='0,3' Background='Transparent'/></DataTemplate>");
        var body=new Grid { RowSpacing=8 }; body.RowDefinitions.Add(new() { Height=GridLength.Auto }); body.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        var header=new Grid { ColumnSpacing=12 }; header.ColumnDefinitions.Add(new() { Width=new(280) }); header.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var summary=Text("FAVORITE CHANNELS",10); summary.CharacterSpacing=100; summary.Foreground=new SolidColorBrush(Color(LightFields ? "#A39514" : "#F1FA8C")); Place(header,summary);
        var hours=new Grid(); for(int n=0;n<4;n++) { hours.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); var time=Text(guideStart.AddMinutes(n*30).LocalDateTime.ToString("HH:mm"),11); time.Opacity=.7; Place(hours,time,0,n); } Place(header,hours,0,1);
        Place(body,header); Place(body,list,1); Place(page,Card(body,new Thickness(12)),3);
        var empty=Card(EmptyState("\uE735","Build your favorite guide.","Star channels in Live TV and they will appear here, ready to arrange.","Choose favorite channels",()=> { Navigate("Live TV"); return Task.CompletedTask; })); Place(page,empty,3);
        CancellationTokenSource? pending=null; bool refreshPending=false,unloaded=false;
        var tick=new DispatcherTimer { Interval=TimeSpan.FromSeconds(30) };
        var timeUpdates=new Dictionary<Grid,Action>();
        GuideReorder reorder=null!;
        reorder=new GuideReorder(list,rows,()=>pending?.Cancel(),changed=>_=GuardAsync(async ()=> { if(changed) await PersistOrderAsync(); if(refreshPending) { refreshPending=false; await PopulateAsync(false); } }));
        async Task PersistOrderAsync()
        {
            if(unloaded || !ReferenceEquals(settings.Profile,profile)) return;
            FavoriteGuide.SaveOrder(profile,items,rows.Select(row=>row.Channel.Id)); await store.SaveAsync(settings); status.Text="Favorite guide order saved.";
        }
        async Task MoveAsync(GuideRow row,int direction)
        {
            var from=rows.IndexOf(row); var to=from+direction; if(from<0 || to<0 || to>=rows.Count) return;
            rows.Move(from,to); await PersistOrderAsync();
        }
        void ShowOptions(Button button,GuideRow row)
        {
            var menu=new MenuFlyout(); var index=rows.IndexOf(row);
            var up=new MenuFlyoutItem { Text="Move up",IsEnabled=index>0,Icon=new FontIcon { Glyph="\uE70E" } };
            var down=new MenuFlyoutItem { Text="Move down",IsEnabled=index>=0 && index<rows.Count-1,Icon=new FontIcon { Glyph="\uE70D" } };
            up.Click+=(_,_)=>_=GuardAsync(()=>MoveAsync(row,-1)); down.Click+=(_,_)=>_=GuardAsync(()=>MoveAsync(row,1));
            var remove=new MenuFlyoutItem { Text="Remove from favorites",Icon=new FontIcon { Glyph="\uE735",Foreground=new SolidColorBrush(Color("#F1FA8C")) } }; remove.Click+=(_,_)=>_=GuardAsync(()=>ToggleFavoriteAsync(row.Channel));
            menu.Items.Add(up); menu.Items.Add(down); menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(remove); menu.ShowAt(button);
        }
        Grid NowMarker(DateTimeOffset start,out Action update)
        {
            var overlay=new Grid { IsHitTestVisible=false };
            overlay.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); overlay.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
            var line=new Border { Width=1,Background=new SolidColorBrush(Color("#50FA7B")),HorizontalAlignment=HorizontalAlignment.Right,Opacity=.55 }; Place(overlay,line);
            update=()=> { var elapsed=(DateTimeOffset.Now-start).TotalMinutes; overlay.Visibility=elapsed is >=0 and <=120 ? Visibility.Visible : Visibility.Collapsed; overlay.ColumnDefinitions[0].Width=new(Math.Max(.001,Math.Clamp(elapsed,0,120)),GridUnitType.Star); overlay.ColumnDefinitions[1].Width=new(Math.Max(.001,120-Math.Clamp(elapsed,0,120)),GridUnitType.Star); };
            update(); return overlay;
        }
        var headerMarker=NowMarker(guideStart,out var updateHeader); Grid.SetColumnSpan(headerMarker,4); hours.Children.Add(headerMarker);
        void BuildGuideContainer(ListViewBase sender,ContainerContentChangingEventArgs args)
        {
            if(args.ItemContainer.ContentTemplateRoot is not Grid grid) return;
            if(args.InRecycleQueue) { timeUpdates.Remove(grid); return; }
            if(args.Item is not GuideRow row) return;
            grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.ColumnSpacing=12;
            grid.ColumnDefinitions.Add(new() { Width=new(280) }); grid.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
            var channel=new Grid { ColumnSpacing=6 }; channel.ColumnDefinitions.Add(new() { Width=new(24) }); channel.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); channel.ColumnDefinitions.Add(new() { Width=new(34) });
            Place(channel,reorder.Grip(row,grid));
            var watchChannel=Button("",()=>_=GuardAsync(()=>PlayAsync(row.Channel))); watchChannel.Padding=new Thickness(4); watchChannel.Background=null; watchChannel.BorderThickness=new Thickness(0); watchChannel.HorizontalAlignment=HorizontalAlignment.Stretch; watchChannel.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var name=Text(row.Channel.Name,13); name.TextWrapping=TextWrapping.NoWrap; name.TextTrimming=TextTrimming.CharacterEllipsis; name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
            var number=Text($"{row.Channel.Number:000}  ·  {profile.Sources.FirstOrDefault(s=>s.Id==row.Channel.SourceId)?.Name??"Favorite"}",10); number.Opacity=.65; number.TextWrapping=TextWrapping.NoWrap; number.TextTrimming=TextTrimming.CharacterEllipsis;
            var identity=Stack(name,number); identity.Spacing=5; identity.VerticalAlignment=VerticalAlignment.Center;
            var logo=new Grid { Width=40,Height=40 }; logo.Children.Add(new FontIcon { Glyph="\uE714",FontSize=17,Opacity=.4 });
            var image=new Image { Stretch=Stretch.Uniform }; image.SetBinding(Image.SourceProperty,new Binding { Source=row,Path=new PropertyPath(nameof(ArtworkRow.Artwork)) }); logo.Children.Add(image);
            var channelLabel=new Grid { ColumnSpacing=8 }; channelLabel.ColumnDefinitions.Add(new() { Width=new(40) }); channelLabel.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); Place(channelLabel,logo); Place(channelLabel,identity,0,1);
            watchChannel.Content=channelLabel; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(watchChannel,"Watch "+row.Channel.Name); ToolTipService.SetToolTip(watchChannel,row.Channel.Name); Place(channel,watchChannel,0,1);
            Button? options=null; options=IconButton("\uE712","Options for "+row.Channel.Name,()=>ShowOptions(options!,row)); options.Width=34; Place(channel,options,0,2); Place(grid,channel);
            var timeline=new Grid(); var updates=new List<Action>();
            if(row.Shows.Length==0) { var missing=Text("Schedule unavailable · Select the channel to watch",12); missing.Opacity=.65; missing.VerticalAlignment=VerticalAlignment.Center; missing.Margin=new Thickness(12); timeline.Children.Add(missing); }
            foreach(var show in row.Shows)
            {
                var start=Math.Clamp((show.Start-row.Start).TotalMinutes,0,120); var end=Math.Clamp((show.End-row.Start).TotalMinutes,0,120);
                var title=Text(show.Title,13); title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; title.TextWrapping=TextWrapping.NoWrap; title.TextTrimming=TextTrimming.CharacterEllipsis;
                var time=Text(show.TimeLabel,10); time.Opacity=.7; time.TextWrapping=TextWrapping.NoWrap; time.TextTrimming=TextTrimming.CharacterEllipsis;
                var state=Text("",10); state.TextWrapping=TextWrapping.NoWrap; state.TextTrimming=TextTrimming.CharacterEllipsis;
                var progress=new ProgressBar { Minimum=0,Maximum=100,Height=2,MinHeight=2,Foreground=new SolidColorBrush(Color("#50FA7B")),Background=new SolidColorBrush(Color("#44475A")),Margin=new Thickness(0,3,0,0) };
                var content=Stack(title,time,state,progress); content.Spacing=4;
                var cell=Button("",()=>SelectProgramme(row.Channel,show)); cell.Content=content; cell.MinHeight=cell.MaxHeight=80; cell.MinWidth=0; cell.Margin=new Thickness(2,0,2,0); cell.HorizontalAlignment=HorizontalAlignment.Stretch; cell.HorizontalContentAlignment=HorizontalAlignment.Stretch; cell.Background=new SolidColorBrush(Color(LightFields ? "#F1F2F4" : "#282A36")); cell.Padding=new Thickness(10,8,10,8); cell.CornerRadius=new CornerRadius(8);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell,show.Title+", "+show.TimeLabel); ToolTipService.SetToolTip(cell,show.Title+" · "+show.TimeLabel);
                Action update=()=> {
                    var current=show.Start<=DateTimeOffset.Now && show.End>DateTimeOffset.Now; var selected=selectedChannel?.Id==row.Channel.Id && selectedShow?.Start==show.Start;
                    state.Text=current ? $"ON NOW · {Math.Max(1,(int)Math.Ceiling((show.End-DateTimeOffset.Now).TotalMinutes))} min left" : show.IsPremiere ? "PREMIERE" : show.IsNew ? "NEW" : show.IsRepeat ? "REPEAT" : $"{(int)Math.Ceiling((show.End-show.Start).TotalMinutes)} min";
                    state.Foreground=new SolidColorBrush(Color(current ? LightFields ? "#089108" : "#50FA7B" : LightFields ? "#815CD6" : show.IsNew || show.IsPremiere ? "#F1FA8C" : "#BD93F9")); progress.Visibility=current ? Visibility.Visible : Visibility.Collapsed;
                    progress.Value=Math.Clamp((DateTimeOffset.Now-show.Start).TotalSeconds/(show.End-show.Start).TotalSeconds*100,0,100);
                    cell.BorderThickness=new Thickness(selected ? 1 : 0); cell.BorderBrush=new SolidColorBrush(Color("#BD93F9"));
                };
                update(); updates.Add(update); cell.Click+=(_,_)=> { foreach(var updateVisible in timeUpdates.Values) updateVisible(); };
                var segment=new Grid(); segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,start),GridUnitType.Star) }); segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,end-start),GridUnitType.Star) }); segment.ColumnDefinitions.Add(new() { Width=new(Math.Max(.001,120-end),GridUnitType.Star) }); Place(segment,cell,0,1); timeline.Children.Add(segment);
            }
            timeline.Children.Add(NowMarker(row.Start,out var updateMarker)); updates.Add(updateMarker);
            timeUpdates[grid]=()=> { foreach(var update in updates) update(); }; Place(grid,timeline,0,1);
        }
        list.ContainerContentChanging+=(sender,args)=> { if(args.InRecycleQueue) { BuildGuideContainer(sender,args); return; } if(args.Phase==0) { args.RegisterUpdateCallback(1,BuildGuideContainer); return; } BuildGuideContainer(sender,args); };
        async Task PopulateAsync(bool debounce)
        {
            if(unloaded) return; if(reorder.IsDragging) { refreshPending=true; return; }
            pending?.Cancel(); pending?.Dispose(); var request=pending=new(); var ct=request.Token;
            try
            {
                if(debounce) await Task.Delay(180,ct);
                var term=search.Text.Trim(); var channelSnapshot=FavoriteGuide.Channels(items,profile);
                var sources=profile.Sources.ToDictionary(s=>s.Id,s=>new Dictionary<string,string>(s.EpgMappings)); var indices=guideIndices.ToDictionary(e=>e.Key,e=>e.Value.Index);
                var start=guideStart; var end=start.AddHours(2);
                var found=await Task.Run(()=> { var result=new List<GuideRow>(); foreach(var channel in channelSnapshot) { ct.ThrowIfCancellationRequested(); var id=sources.GetValueOrDefault(channel.SourceId)?.GetValueOrDefault(channel.Id,channel.EpgId=="" ? channel.Id : channel.EpgId)??channel.EpgId; var shows=(indices.GetValueOrDefault(channel.SourceId)??EmptyGuide).Window(id,start,end).ToArray(); if(term!="" && !channel.Name.Contains(term,StringComparison.OrdinalIgnoreCase) && !shows.Any(show=>show.Title.Contains(term,StringComparison.OrdinalIgnoreCase))) continue; result.Add(new(channel,shows,start)); } return result; },ct);
                ct.ThrowIfCancellationRequested(); rows.Clear(); timeUpdates.Clear(); foreach(var row in found) rows.Add(row);
                summary.Text=$"FAVORITES  ·  {rows.Count:N0}"; empty.Visibility=rows.Count==0 ? Visibility.Visible : Visibility.Collapsed; infoCard.Visibility=rows.Count==0 ? Visibility.Collapsed : Visibility.Visible;
                if(!found.Any(row=>row.Channel.Id==selectedChannel?.Id && row.Shows.Any(show=>show.Start==selectedShow?.Start))) { selectedChannel=null; selectedShow=null; }
                if(selectedShow==null)
                {
                    var available=found.SelectMany(row=>row.Shows.Select(show=>(row.Channel,Show:show))).ToArray();
                    var first=available.FirstOrDefault(pair=>pair.Show.Start<=DateTimeOffset.Now && pair.Show.End>DateTimeOffset.Now);
                    if(first.Show==null) first=available.FirstOrDefault();
                    if(first.Show!=null) SelectProgramme(first.Channel,first.Show);
                    else if(found.Count>0) { selectedChannel=found[0].Channel; infoContext.Text=$"CHANNEL  ·  {selectedChannel.Number:000}  {selectedChannel.Name}"; infoContext.Foreground=FieldText(); infoTitle.Text="Schedule unavailable"; infoMeta.Visibility=Visibility.Collapsed; infoDescription.Text="Select Watch channel to play live. Your favorite stays in the guide while programme information is unavailable."; watch.IsEnabled=true; details.IsEnabled=false; }
                }
                if(term!="" && rows.Count==0) empty.Child=EmptyState("\uE721","No matching favorites or programmes.","Try a channel name, a programme title, or a different time.","Clear search",()=> { search.Text=""; return Task.CompletedTask; });
                else if(channelSnapshot.Length==0) empty.Child=EmptyState("\uE735","Build your favorite guide.","Star channels in Live TV and they will appear here, ready to arrange.","Choose favorite channels",()=> { Navigate("Live TV"); return Task.CompletedTask; });
            }
            catch(OperationCanceledException) { }
        }
        tick.Tick+=(_,_)=> { if(currentTab!="Guide" || reorder.IsDragging) return; updateHeader(); UpdateInfo(); foreach(var update in timeUpdates.Values) update(); };
        list.Loaded+=(_,_)=>tick.Start(); list.Unloaded+=(_,_)=> { unloaded=true; pending?.Cancel(); tick.Stop(); reorder.Dispose(); timeUpdates.Clear(); };
        search.TextChanged+=(_,_)=>_=GuardAsync(()=>PopulateAsync(true)); refreshGuide=()=>_=GuardAsync(()=>PopulateAsync(false)); _=GuardAsync(()=>PopulateAsync(false)); return page;
    }
    private async Task ProgrammeDetailsAsync(MediaItem channel,Programme show)
    {
        var duration=(int)Math.Ceiling((show.End-show.Start).TotalMinutes);
        var content=Stack(Text(channel.Name,12),Text(show.Title,22),Text(show.Subtitle,14),Text($"{show.Start.LocalDateTime:ddd, dd MMM} · {show.TimeLabel} · {duration} min",13),Text(ProgrammeMeta(show),12),Text(show.Description=="" ? "No description supplied by the provider." : show.Description,14));
        if(show.Start>DateTimeOffset.UtcNow) { var reminder=AsyncButton("Set reminder",()=>AddReminderAsync(channel,show)); reminder.Foreground=new SolidColorBrush(Color("#F1FA8C")); content.Children.Add(reminder); }
        var past=show.End<DateTimeOffset.UtcNow; var catchup=past && channel.ArchiveDays>0 && show.Start>=DateTimeOffset.UtcNow.AddDays(-channel.ArchiveDays);
        var result=await DialogAsync("Programme details",Scroll(content),catchup ? "Play catch-up" : "Watch channel"); if(result!=ContentDialogResult.Primary) return;
        if(catchup) { var source=settings.Profile.Sources.First(s=>s.Id==channel.SourceId); await new SourceService(http,store).VerifyCatchupAsync(source,channel,show,lifetime.Token); var url=XtreamClient.CatchupUrl(source,channel,show); await PlayAsync(channel with { Url=url,Kind=MediaKind.Episode,Id=channel.Id+":"+show.Start.ToUnixTimeSeconds(),Name=show.Title }); }
        else await PlayAsync(channel);
    }
}

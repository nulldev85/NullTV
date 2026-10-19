using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;
using Windows.Foundation;
namespace NullTV.App;
public sealed partial class MainWindow
{
    private Grid? guidePage,guideContent;
    private Border? guidePreview,guideVideoExpanded;
    private bool guideRailCollapsed;
    private ChannelSelection guideSelection=new(ChannelScope.Favorites);
    private IptvCategoryRail? guideCategoryRail;
    private Action? updateGuidePanes;
    private Action<MediaItem>? selectGuideChannel;
    private Thickness GuideMargin=>new(0);
    private static DateTimeOffset HalfHour(DateTimeOffset time)=>new(time.Year,time.Month,time.Day,time.Hour,time.Minute/30*30,0,time.Offset);
    private DateTimeOffset guideStart=HalfHour(DateTimeOffset.Now).AddHours(-1);
    private sealed class GuideRow(MediaItem channel,Programme[] shows,DateTimeOffset start) : ArtworkRow(channel)
    {
        public MediaItem Channel=>Item;
        public Programme[] Shows { get; }=shows;
        public DateTimeOffset Start { get; }=start;
    }
    private void MoveVideo(Border? destination)
    {
        if(videoGrid==null || destination==null || ReferenceEquals(videoGrid.Parent,destination)) return;
        playerOverlay?.Hide(); surface?.Hide(true);
        if(videoGrid.Parent is Border previous) previous.Child=null;
        destination.Child=videoGrid;
        DispatcherQueue.TryEnqueue(()=> { if(IsPlayerVisible && surface!=null) surface.Hide(!active.ContainsKey(surface)); });
    }
    private void UpdateGuideVideoLayout()
    {
        if(currentTab!="Guide" || guidePage==null || guidePreview==null || guideVideoExpanded==null) return;
        UpdatePlayerChrome(); var expanded=fullscreen || expandedVideo;
        MoveVideo(expanded ? guideVideoExpanded : guidePreview);
        foreach(var child in guidePage.Children) child.Visibility=ReferenceEquals(child,guideVideoExpanded) ? expanded ? Visibility.Visible : Visibility.Collapsed : expanded ? Visibility.Collapsed : Visibility.Visible;
        pageHost.Margin=new Thickness(0); updateGuidePanes?.Invoke(); UpdateEmptyPlayer(); UpdatePlaybackControls();
        DispatcherQueue.TryEnqueue(()=> { if(surface!=null) surface.Hide(!IsPlayerVisible || !active.ContainsKey(surface)); });
    }
    private static string ProgrammeMeta(Programme show)=>string.Join(" · ",new[]{show.Category,show.Rating,show.Episode,show.IsPremiere ? "PREMIERE" : show.IsNew ? "NEW" : show.IsRepeat ? "REPEAT" : ""}.Where(value=>value!=""));
    private async Task AddReminderAsync(MediaItem channel,Programme show)
    {
        if(show.Start<=DateTimeOffset.UtcNow) throw new InvalidDataException("Reminders can be set for upcoming programs.");
        if(!settings.Profile.Reminders.Any(reminder=>reminder.ChannelId==channel.Id && reminder.Start==show.Start)) settings.Profile.Reminders.Add(new(channel.Id,show.Title,show.Start));
        await store.SaveAsync(settings); status.Text="Reminder saved. NullTV must remain open for reminders.";
    }
    private UIElement BuildGuide()
    {
        const double channelWidth=276,rowHeight=88,timelineWidth=GuideGeometry.WindowMinutes*GuideGeometry.PixelsPerMinute;
        var profile=settings.Profile; var page=new Grid(); guidePage=page;
        page.ColumnDefinitions.Add(new() { Width=new(guideRailCollapsed ? 56 : 240) }); page.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var main=new Grid { Background=new SolidColorBrush(Color("#21222C")) }; guideContent=main;
        main.RowDefinitions.Add(new() { Height=GridLength.Auto }); main.RowDefinitions.Add(new() { Height=new(48) }); main.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); Place(page,main,0,1);
        IptvCategoryRail? rail=null;
        rail=new(this,guideSelection,guideRailCollapsed,selection=> { guideSelection=selection; refreshGuide?.Invoke(); },collapsed=> { guideRailCollapsed=collapsed; page.ColumnDefinitions[0].Width=new(collapsed ? 56 : 240); }); guideCategoryRail=rail; Place(page,rail);
        updateGuidePanes=()=>page.ColumnDefinitions[0].Width=new(fullscreen || expandedVideo ? 0 : guideRailCollapsed ? 56 : 240);
        MediaItem? selectedChannel=null; Programme? selectedShow=null;
        var info=new Grid { ColumnSpacing=18,Padding=new Thickness(16,12,16,12),BorderBrush=new SolidColorBrush(Color("#44475A")),BorderThickness=new Thickness(0,0,0,.5) };
        info.ColumnDefinitions.Add(new() { Width=new(288) }); info.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        guidePreview=Card(new Grid(),new Thickness(0)); guidePreview.Height=162; guidePreview.CornerRadius=new CornerRadius(8); Place(info,guidePreview);
        var context=Text("LIVE TV GUIDE",10); context.CharacterSpacing=100; context.Foreground=new SolidColorBrush(Color("#50FA7B")); context.TextWrapping=TextWrapping.NoWrap; context.TextTrimming=TextTrimming.CharacterEllipsis;
        var title=Text("Choose a channel or live program",20); title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; title.TextWrapping=TextWrapping.NoWrap; title.TextTrimming=TextTrimming.CharacterEllipsis;
        var meta=Text("",11); meta.Foreground=new SolidColorBrush(Color("#BD93F9")); meta.TextWrapping=TextWrapping.NoWrap; meta.TextTrimming=TextTrimming.CharacterEllipsis;
        var description=Text("Explore favorites and your provider's categories. Watch here while you browse the schedule.",12); description.Opacity=.75; description.MaxLines=4; description.TextTrimming=TextTrimming.CharacterEllipsis;
        var copy=Stack(context,title,meta,description); copy.Spacing=6; copy.VerticalAlignment=VerticalAlignment.Center; Place(info,copy,0,1); Place(main,info);
        void UpdateInfo()
        {
            if(selectedChannel==null) return;
            var show=selectedShow; var now=DateTimeOffset.Now;
            context.Text=selectedChannel.Name+ (show==null ? " · LIVE CHANNEL" : $" · {show.TimeLabel}"+(show.Start<=now && show.End>now ? $" · {Math.Ceiling((show.End-now).TotalMinutes)} min left" : ""));
            title.Text=show==null ? selectedChannel.Name : show.Title+(show.Subtitle=="" ? "" : " — "+show.Subtitle);
            meta.Text=show==null ? "" : ProgrammeMeta(show); meta.Visibility=meta.Text=="" ? Visibility.Collapsed : Visibility.Visible;
            description.Text=show==null ? "No program data supplied for this channel. Select it to watch live." : show.Description=="" ? "No description supplied by the provider." : show.Description;
        }
        void SelectProgram(MediaItem channel,Programme? show) { selectedChannel=channel; selectedShow=show; UpdateInfo(); }
        selectGuideChannel=channel=> {
            var id=profile.Sources.FirstOrDefault(s=>s.Id==channel.SourceId)?.EpgMappings.GetValueOrDefault(channel.Id,channel.EpgId=="" ? channel.Id : channel.EpgId)??channel.EpgId;
            SelectProgram(channel,GuideFor(channel.SourceId).NowNext(id,DateTimeOffset.Now).Now);
        };
        var toolbar=new Grid { Padding=new Thickness(16,6,12,6),ColumnSpacing=10 };
        toolbar.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); for(int i=0;i<3;i++) toolbar.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var groupTitle=Text(guideSelection.Title,18); groupTitle.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; groupTitle.VerticalAlignment=VerticalAlignment.Center; groupTitle.TextWrapping=TextWrapping.NoWrap; groupTitle.TextTrimming=TextTrimming.CharacterEllipsis; Place(toolbar,groupTitle);
        var sort=Choice("",["Default order","Channel name","Channel number"],"Default order"); sort.Width=142; sort.MinHeight=sort.Height=34; sort.Padding=new Thickness(10,5,10,5); Place(toolbar,sort,0,1);
        var timeActions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=2 };
        timeActions.Children.Add(IconButton("\uE76B","Previous six hours",()=> { guideStart=guideStart.AddMinutes(-GuideGeometry.WindowMinutes); refreshGuide?.Invoke(); }));
        var nowButton=Button("Now",()=> { guideStart=HalfHour(DateTimeOffset.Now).AddHours(-1); refreshGuide?.Invoke(); }); nowButton.Foreground=new SolidColorBrush(Color("#F1FA8C")); timeActions.Children.Add(nowButton);
        timeActions.Children.Add(IconButton("\uE76C","Next six hours",()=> { guideStart=guideStart.AddMinutes(GuideGeometry.WindowMinutes); refreshGuide?.Invoke(); }));
        timeActions.Children.Add(IconButton("\uE72C","Refresh guide",()=>_=GuardAsync(()=>RefreshAllAsync(true))));
        var date=Text(guideStart.LocalDateTime.ToString("ddd, dd MMM"),11); date.VerticalAlignment=VerticalAlignment.Center; date.Margin=new Thickness(8,0,4,0); date.Opacity=.7; timeActions.Children.Add(date); Place(toolbar,timeActions,0,2);
        foreach(var button in timeActions.Children.OfType<Button>()) { button.MinHeight=button.Height=32; button.MinWidth=0; button.Padding=new Thickness(8,4,8,4); if(button.Content is FontIcon) button.Width=28; }
        var search=SearchBox("Search channels & programs"); search.Width=230; search.Compact(); Place(toolbar,search,0,3); Place(main,toolbar,1);
        toolbar.SizeChanged+=(_,_)=> { var wide=toolbar.ActualWidth>=1000; search.Width=wide ? 250 : 196; date.Visibility=toolbar.ActualWidth>=860 ? Visibility.Visible : Visibility.Collapsed; sort.Width=toolbar.ActualWidth>=800 ? 142 : 110; };
        var rows=new ObservableCollection<GuideRow>(); var list=new ListView { ItemsSource=rows,SelectionMode=ListViewSelectionMode.None,IsSwipeEnabled=true };
        QuietItems(list); WatchArtwork(list,96); ScrollViewer.SetHorizontalScrollMode(list,ScrollMode.Disabled);
        list.ItemContainerStyle=new Style(typeof(ListViewItem)) { Setters={new Setter(Control.PaddingProperty,new Thickness(0)),new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch),new Setter(Control.MinHeightProperty,rowHeight)} };
        list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='88' Background='Transparent'/></DataTemplate>");
        var body=new Grid { BorderBrush=new SolidColorBrush(Color("#44475A")),BorderThickness=new Thickness(0,.5,0,0) }; body.RowDefinitions.Add(new() { Height=new(34) }); body.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); body.RowDefinitions.Add(new() { Height=new(12) });
        var header=new Grid { BorderBrush=new SolidColorBrush(Color("#44475A")),BorderThickness=new Thickness(0,0,0,.5) }; header.ColumnDefinitions.Add(new() { Width=new(channelWidth) }); header.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var count=Text("CHANNELS",10); count.CharacterSpacing=100; count.Margin=new Thickness(16,0,0,0); count.Opacity=.65; count.VerticalAlignment=VerticalAlignment.Center; Place(header,count);
        var hours=new Canvas { Width=timelineWidth,Height=33 };
        var headerScroll=new ScrollViewer { Content=hours,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled }; Place(header,headerScroll,0,1); Place(body,header);
        var viewport=new Grid(); Place(viewport,list); var empty=EmptyState("\uE714","No channels in this view","Choose another category or add channels to Favorites.","Browse all channels",()=> { guideSelection=new(ChannelScope.All); refreshGuide?.Invoke(); return Task.CompletedTask; }); Place(viewport,empty); Place(body,viewport,1);
        var scrollRow=new Grid(); scrollRow.ColumnDefinitions.Add(new() { Width=new(channelWidth) }); scrollRow.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var scroll=new ScrollBar { Orientation=Orientation.Horizontal,Minimum=0,Maximum=timelineWidth,SmallChange=60,LargeChange=240,Height=12,MinHeight=0 }; Place(scrollRow,scroll,0,1); Place(body,scrollRow,2); Place(main,body,2);
        double offset=0; bool scrolling=false,unloaded=false,refreshPending=false; CancellationTokenSource? pending=null;
        var timelines=new Dictionary<Grid,Canvas>(); var updates=new Dictionary<Grid,Action>(); var qualityUpdates=new Dictionary<Grid,Action>();
        void SetOffset(double value)
        {
            value=Math.Clamp(value,0,Math.Max(0,timelineWidth-headerScroll.ActualWidth)); if(Math.Abs(value-offset)<.1) return; offset=value;
            foreach(var canvas in timelines.Values) ((TranslateTransform)canvas.RenderTransform).X=-offset;
            if(!scrolling) { scrolling=true; headerScroll.ChangeView(offset,null,null,true); scroll.Value=offset; scrolling=false; }
        }
        scroll.ValueChanged+=(_,args)=> { if(!scrolling) SetOffset(args.NewValue); };
        headerScroll.ViewChanged+=(_,_)=> { if(!scrolling) SetOffset(headerScroll.HorizontalOffset); };
        headerScroll.SizeChanged+=(_,_)=> { scroll.Maximum=Math.Max(0,timelineWidth-headerScroll.ActualWidth); scroll.ViewportSize=headerScroll.ActualWidth; SetOffset(offset); };
        list.AddHandler(UIElement.PointerWheelChangedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e)=> { var properties=e.GetCurrentPoint(list).Properties; if(properties.IsHorizontalMouseWheel) { SetOffset(offset+properties.MouseWheelDelta); e.Handled=true; } }),true);
        var tick=new DispatcherTimer { Interval=TimeSpan.FromSeconds(15) };
        Border? headerNow=null;
        void BuildHours()
        {
            hours.Children.Clear(); date.Text=guideStart.LocalDateTime.ToString("ddd, dd MMM");
            for(int n=0;n<6;n++) { var line=new Border { Width=1,Height=34,Background=new SolidColorBrush(Color("#44475A")) }; Canvas.SetLeft(line,n*240); hours.Children.Add(line); var time=Text(guideStart.AddHours(n).LocalDateTime.ToString("HH:mm"),12); time.Opacity=.75; Canvas.SetLeft(time,n*240+10); Canvas.SetTop(time,9); hours.Children.Add(time); }
            headerNow=new Border { Background=new SolidColorBrush(Color("#FF79C6")),Padding=new Thickness(5,1,5,1),CornerRadius=new CornerRadius(4),Child=new TextBlock { Text=DateTimeOffset.Now.ToString("HH:mm"),FontSize=10,Foreground=new SolidColorBrush(Color("#282A36")) } }; hours.Children.Add(headerNow);
            UpdateClock();
        }
        void UpdateClock()
        {
            var x=(DateTimeOffset.Now-guideStart).TotalMinutes*GuideGeometry.PixelsPerMinute;
            if(headerNow!=null) { headerNow.Visibility=x>=0 && x<timelineWidth ? Visibility.Visible : Visibility.Collapsed; Canvas.SetLeft(headerNow,Math.Clamp(x-18,0,timelineWidth-42)); Canvas.SetTop(headerNow,7); ((TextBlock)headerNow.Child).Text=DateTimeOffset.Now.ToString("HH:mm"); }
        }
        GuideReorder reorder=null!;
        async Task PersistOrderAsync() { if(!unloaded && ReferenceEquals(profile,settings.Profile) && guideSelection.Scope==ChannelScope.Favorites) { FavoriteGuide.SaveOrder(profile,items,rows.Select(row=>row.Channel.Id)); await store.SaveAsync(settings); } }
        reorder=new GuideReorder(list,rows,()=>pending?.Cancel(),changed=>_=GuardAsync(async ()=> { if(changed) await PersistOrderAsync(); if(refreshPending) { refreshPending=false; await PopulateAsync(false); } }));
        void ShowOptions(Button button,GuideRow row)
        {
            var menu=new MenuFlyout(); var watch=new MenuFlyoutItem { Text="Watch channel",Icon=new FontIcon { Glyph="\uE768" } }; watch.Click+=(_,_)=>_=GuardAsync(()=>PlayAsync(row.Channel)); menu.Items.Add(watch);
            var favorite=new MenuFlyoutItem { Text=profile.Favorites.Contains(row.Channel.Id) ? "Remove from favorites" : "Add to favorites" }; favorite.Click+=(_,_)=>_=GuardAsync(()=>ToggleFavoriteAsync(row.Channel)); menu.Items.Add(favorite); menu.ShowAt(button);
        }
        void BuildRow(ListViewBase sender,ContainerContentChangingEventArgs args)
        {
            if(args.ItemContainer.ContentTemplateRoot is not Grid grid) return;
            if(args.InRecycleQueue) { timelines.Remove(grid); updates.Remove(grid); qualityUpdates.Remove(grid); return; }
            if(args.Item is not GuideRow row) return;
            grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.ColumnDefinitions.Add(new() { Width=new(channelWidth) }); grid.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); grid.BorderBrush=new SolidColorBrush(Color("#44475A")); grid.BorderThickness=new Thickness(0,0,0,.5);
            var channel=new Grid { Padding=new Thickness(8,0,8,0),ColumnSpacing=8,Background=new SolidColorBrush(Color("#21222C")),BorderBrush=new SolidColorBrush(Color("#44475A")),BorderThickness=new Thickness(0,0,.5,0) };
            channel.ColumnDefinitions.Add(new() { Width=new(18) }); channel.ColumnDefinitions.Add(new() { Width=new(40) }); channel.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); channel.ColumnDefinitions.Add(new() { Width=new(28) });
            if(guideSelection.Scope==ChannelScope.Favorites && (string)sort.SelectedItem=="Default order") Place(channel,reorder.Grip(row,grid));
            var logo=new Grid { Width=38,Height=38 }; logo.Children.Add(new FontIcon { Glyph="\uE714",FontSize=18,Opacity=.3 }); var image=new Image { Stretch=Stretch.Uniform }; image.SetBinding(Image.SourceProperty,new Binding { Source=row,Path=new PropertyPath(nameof(ArtworkRow.Artwork)) }); logo.Children.Add(image); Place(channel,logo,0,1);
            var name=Text(row.Channel.Name,12); name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; name.TextWrapping=TextWrapping.NoWrap; name.TextTrimming=TextTrimming.CharacterEllipsis;
            var badges=new StackPanel { Orientation=Orientation.Horizontal,Spacing=4 }; FillQuality(badges,row.Channel); qualityUpdates[grid]=()=>FillQuality(badges,row.Channel);
            var current=Text("",11); current.Opacity=.65; current.TextWrapping=TextWrapping.NoWrap; current.TextTrimming=TextTrimming.CharacterEllipsis;
            var identity=Stack(name,badges,current); identity.Spacing=4; identity.VerticalAlignment=VerticalAlignment.Center;
            var watch=Button("",()=>_=GuardAsync(()=>PlayAsync(row.Channel))); watch.Content=identity; watch.Padding=new Thickness(0); watch.Background=null; watch.BorderThickness=new Thickness(0); watch.HorizontalAlignment=HorizontalAlignment.Stretch; watch.HorizontalContentAlignment=HorizontalAlignment.Stretch; ToolTipService.SetToolTip(watch,row.Channel.Name); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(watch,"Watch "+row.Channel.Name); Place(channel,watch,0,2);
            Button? heart=null; heart=IconButton("\uEB52","Favorite "+row.Channel.Name,()=>_=GuardAsync(()=>ToggleFavoriteAsync(row.Channel))); heart.Width=28; heart.MinHeight=heart.Height=32; ((FontIcon)heart.Content).FontSize=16; heart.Foreground=new SolidColorBrush(Color(profile.Favorites.Contains(row.Channel.Id) ? "#FF79C6" : "#6272A4")); Place(channel,heart,0,3); heart.RightTapped+=(_,e)=> { ShowOptions(heart,row); e.Handled=true; }; Place(grid,channel);
            var clip=new Grid(); var canvas=new Canvas { Width=timelineWidth,Height=rowHeight,RenderTransform=new TranslateTransform { X=-offset } }; clip.Children.Add(canvas); clip.SizeChanged+=(_,_)=>clip.Clip=new RectangleGeometry { Rect=new Rect(0,0,clip.ActualWidth,rowHeight) }; Place(grid,clip,0,1); timelines[grid]=canvas;
            var cellUpdates=new List<Action>();
            if(row.Shows.Length==0) { var missing=Text("No program data",12); missing.Opacity=.55; Canvas.SetLeft(missing,16); Canvas.SetTop(missing,34); canvas.Children.Add(missing); }
            foreach(var show in row.Shows)
            {
                if(GuideGeometry.Segment(show.Start,show.End,row.Start) is not { } segment) continue;
                var programTitle=Text(show.Title,13); programTitle.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; programTitle.TextWrapping=TextWrapping.NoWrap; programTitle.TextTrimming=TextTrimming.CharacterEllipsis;
                var time=Text(show.TimeLabel,11); time.Opacity=.68; time.TextWrapping=TextWrapping.NoWrap; time.TextTrimming=TextTrimming.CharacterEllipsis;
                var flag=Text(show.IsPremiere ? "PREMIERE" : show.IsNew ? "NEW" : "",9); flag.Foreground=new SolidColorBrush(Color("#F1FA8C")); flag.Visibility=flag.Text=="" ? Visibility.Collapsed : Visibility.Visible;
                var progress=new ProgressBar { Height=2,MinHeight=2,Maximum=100,Foreground=new SolidColorBrush(Color("#50FA7B")),Background=new SolidColorBrush(Color("#44475A")),VerticalAlignment=VerticalAlignment.Bottom };
                var copyStack=Stack(programTitle,time,flag); copyStack.Spacing=4; var content=new Grid(); content.Children.Add(copyStack); content.Children.Add(progress);
                var cell=Button("",()=> { SelectProgram(row.Channel,show); if(show.Start<=DateTimeOffset.Now && show.End>DateTimeOffset.Now) _=GuardAsync(()=>PlayAsync(row.Channel)); foreach(var update in updates.Values) update(); }); cell.Content=content; cell.MinWidth=cell.MinHeight=0; cell.Height=80; cell.Width=Math.Max(1,segment.Width-4); cell.Padding=new Thickness(10,8,10,8); cell.CornerRadius=new CornerRadius(6); cell.HorizontalContentAlignment=HorizontalAlignment.Stretch; cell.VerticalContentAlignment=VerticalAlignment.Stretch;
                ToolTipService.SetToolTip(cell,show.Title+" · "+show.TimeLabel); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell,row.Channel.Name+", "+show.Title+", "+show.TimeLabel);
                cell.RightTapped+=(_,e)=> { _=GuardAsync(()=>ProgrammeDetailsAsync(row.Channel,show)); e.Handled=true; };
                Canvas.SetLeft(cell,segment.Left+2); Canvas.SetTop(cell,4); canvas.Children.Add(cell);
                cellUpdates.Add(()=> { var live=show.Start<=DateTimeOffset.Now && show.End>DateTimeOffset.Now; var selected=selectedChannel?.Id==row.Channel.Id && selectedShow?.Start==show.Start; cell.Background=new SolidColorBrush(Color(live ? "#403A52" : "#282A36")); cell.BorderBrush=new SolidColorBrush(Color(live || selected ? "#BD93F9" : "#282A36")); cell.BorderThickness=new Thickness(live || selected ? 1 : 0); progress.Visibility=live ? Visibility.Visible : Visibility.Collapsed; progress.Value=Math.Clamp((DateTimeOffset.Now-show.Start).TotalSeconds/(show.End-show.Start).TotalSeconds*100,0,100); });
            }
            var marker=new Border { Width=1.5,Height=rowHeight,Background=new SolidColorBrush(Color("#FF79C6")),IsHitTestVisible=false }; canvas.Children.Add(marker);
            updates[grid]=()=> { foreach(var update in cellUpdates) update(); var now=DateTimeOffset.Now; current.Text=row.Shows.FirstOrDefault(show=>show.Start<=now && show.End>now)?.Title??"No program data"; var x=(now-row.Start).TotalMinutes*GuideGeometry.PixelsPerMinute; marker.Visibility=x>=0 && x<timelineWidth ? Visibility.Visible : Visibility.Collapsed; Canvas.SetLeft(marker,x); };
            updates[grid]();
        }
        list.ContainerContentChanging+=(sender,args)=> { if(args.InRecycleQueue) { BuildRow(sender,args); return; } if(args.Phase==0) { args.RegisterUpdateCallback(1,BuildRow); return; } BuildRow(sender,args); };
        async Task PopulateAsync(bool debounce)
        {
            if(unloaded) return; if(reorder.IsDragging) { refreshPending=true; return; }
            pending?.Cancel(); pending?.Dispose(); var request=pending=new(); var ct=request.Token;
            try
            {
                if(debounce) await Task.Delay(180,ct); var term=search.Text.Trim(); var selection=guideSelection; var channels=ChannelBrowse.Select(items,profile,selection); var order=(string)sort.SelectedItem;
                var sources=profile.Sources.ToDictionary(s=>s.Id,s=>new Dictionary<string,string>(s.EpgMappings)); var indices=guideIndices.ToDictionary(pair=>pair.Key,pair=>pair.Value.Index); var start=guideStart;
                var found=await Task.Run(()=> { IEnumerable<MediaItem> sorted=order=="Channel name" ? channels.OrderBy(c=>c.Name,StringComparer.CurrentCultureIgnoreCase) : order=="Channel number" ? channels.OrderBy(c=>c.Number) : channels; var result=new List<GuideRow>(); foreach(var channel in sorted) { ct.ThrowIfCancellationRequested(); var id=sources.GetValueOrDefault(channel.SourceId)?.GetValueOrDefault(channel.Id,channel.EpgId=="" ? channel.Id : channel.EpgId)??channel.EpgId; var shows=(indices.GetValueOrDefault(channel.SourceId)??EmptyGuide).Window(id,start,start.AddMinutes(GuideGeometry.WindowMinutes)).ToArray(); if(term!="" && !channel.Name.Contains(term,StringComparison.OrdinalIgnoreCase) && !shows.Any(show=>show.Title.Contains(term,StringComparison.OrdinalIgnoreCase))) continue; result.Add(new(channel,shows,start)); } return result; },ct);
                ct.ThrowIfCancellationRequested(); timelines.Clear(); updates.Clear(); qualityUpdates.Clear(); rows.Clear(); foreach(var row in found) rows.Add(row); BuildHours();
                groupTitle.Text=selection.Title; count.Text=$"CHANNELS · {found.Count:N0}"; rail.Select(selection); empty.Visibility=found.Count==0 ? Visibility.Visible : Visibility.Collapsed;
                if(!found.Any(row=>row.Channel.Id==selectedChannel?.Id)) { selectedChannel=null; selectedShow=null; }
                if(selectedChannel!=null && (selectedShow==null || !found.First(row=>row.Channel.Id==selectedChannel.Id).Shows.Contains(selectedShow))) { var row=found.First(row=>row.Channel.Id==selectedChannel.Id); SelectProgram(row.Channel,row.Shows.FirstOrDefault(show=>show.Start<=DateTimeOffset.Now && show.End>DateTimeOffset.Now)??row.Shows.FirstOrDefault()); }
                if(selectedChannel==null && found.Count>0) { var row=found.FirstOrDefault(row=>row.Channel.Id==playing?.Id)??found[0]; SelectProgram(row.Channel,row.Shows.FirstOrDefault(show=>show.Start<=DateTimeOffset.Now && show.End>DateTimeOffset.Now)??row.Shows.FirstOrDefault()); }
                SetOffset(0);
            }
            catch(OperationCanceledException) { }
        }
        refreshGuideQuality=()=> { foreach(var update in qualityUpdates.Values) update(); };
        tick.Tick+=(_,_)=> { if(currentTab!="Guide" || reorder.IsDragging) return; UpdateInfo(); UpdateClock(); foreach(var update in updates.Values) update(); };
        list.Loaded+=(_,_)=> { if(list.ItemsPanelRoot is ItemsStackPanel panel) panel.ItemsUpdatingScrollMode=ItemsUpdatingScrollMode.KeepScrollOffset; tick.Start(); }; list.Unloaded+=(_,_)=> { unloaded=true; pending?.Cancel(); tick.Stop(); reorder.Dispose(); timelines.Clear(); updates.Clear(); qualityUpdates.Clear(); refreshGuideQuality=null; };
        guideVideoExpanded=Card(new Grid(),new Thickness(0)); guideVideoExpanded.CornerRadius=new CornerRadius(0); guideVideoExpanded.Visibility=Visibility.Collapsed; Grid.SetColumnSpan(guideVideoExpanded,2); Place(page,guideVideoExpanded);
        page.Loaded+=(_,_)=>UpdateGuideVideoLayout(); var select=selectGuideChannel; selectGuideChannel=channel=> { select?.Invoke(channel); foreach(var update in updates.Values) update(); };
        sort.SelectionChanged+=(_,_)=>_=GuardAsync(()=>PopulateAsync(false)); search.TextChanged+=(_,_)=>_=GuardAsync(()=>PopulateAsync(true)); refreshGuide=()=>_=GuardAsync(()=>PopulateAsync(false)); _=GuardAsync(()=>PopulateAsync(false)); return page;
    }
    private async Task ProgrammeDetailsAsync(MediaItem channel,Programme show)
    {
        var duration=(int)Math.Ceiling((show.End-show.Start).TotalMinutes);
        var content=Stack(Text(channel.Name,12),Text(show.Title,22),Text(show.Subtitle,14),Text($"{show.Start.LocalDateTime:ddd, dd MMM} · {show.TimeLabel} · {duration} min",13),Text(ProgrammeMeta(show),12),Text(show.Description=="" ? "No description supplied by the provider." : show.Description,14));
        if(show.Start>DateTimeOffset.UtcNow) { var reminder=AsyncButton("Set reminder",()=>AddReminderAsync(channel,show)); reminder.Foreground=new SolidColorBrush(Color("#F1FA8C")); content.Children.Add(reminder); }
        var past=show.End<DateTimeOffset.UtcNow; var catchup=past && channel.ArchiveDays>0 && show.Start>=DateTimeOffset.UtcNow.AddDays(-channel.ArchiveDays);
        var result=await DialogAsync("Program details",Scroll(content),catchup ? "Play catch-up" : "Watch channel"); if(result!=ContentDialogResult.Primary) return;
        if(catchup) { var source=settings.Profile.Sources.First(s=>s.Id==channel.SourceId); await new SourceService(http,store).VerifyCatchupAsync(source,channel,show,lifetime.Token); var url=XtreamClient.CatchupUrl(source,channel,show); await PlayAsync(channel with { Url=url,Kind=MediaKind.Episode,Id=channel.Id+":"+show.Start.ToUnixTimeSeconds(),Name=show.Title }); }
        else await PlayAsync(channel);
    }
}

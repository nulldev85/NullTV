using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using NullTV.Core;
using NullTV.App.Playback;
using Windows.System;
using Windows.Gaming.Input;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private VideoSurface? surface;
    private readonly List<VideoSurface> surfaces=[];
    private readonly Dictionary<VideoSurface,MediaItem> active=[];
    private readonly Dictionary<VideoSurface,int> retries=[];
    private readonly Dictionary<VideoSurface,int> generations=[];
    private readonly Dictionary<VideoSurface,CancellationTokenSource> streamRequests=[];
    private MediaItem? playing;
    private CancellationTokenSource? playbackRequest;
    private ListView? channelList,categoryList;
    private SearchField? channelSearch;
    private TextBlock? playingTitle,playingInfo,liveSummary;
    private UIElement? emptyLive;
    private ComboBox? categoryPicker;
    private Grid? videoGrid;
    private Border? videoHome;
    private bool compactVideoControls;
    private Grid? playerStage;
    private Grid? libraryPlaybackHeading;
    private Button? backToBrowse;
    private UIElement? categoryRail,channelsCard,controlsCard;
    private Grid? playerControls;
    private PlayerOverlay? playerOverlay;
    private Button? rewindButton,forwardButton,subtitlesButton,expandButton,fullscreenButton;
    private UIElement? playerHeading;
    private Grid? liveBar;
    private TextBlock? livePositionLabel,streamInfo;
    private bool behindLive;
    private DateTimeOffset infoUpdated;
    private Task? infoTask;
    private Slider? seek;
    private bool seeking,fullscreen,multi;
    private Window? pipWindow;
    private string filter="All channels";
    private int filterRevision;
    private string numeric="";
    private GamepadButtons lastButtons;
    private void BuildLive()
    {
        livePage=new Grid { ColumnSpacing=16 };
        livePage.RowDefinitions.Add(new() { Height=GridLength.Auto }); livePage.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        livePage.ColumnDefinitions.Add(new() { Width=new(140) }); livePage.ColumnDefinitions.Add(new() { Width=new(304) }); livePage.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
        actions.Children.Add(IconButton("\uE8A4","Toggle categories",ToggleChannelPane));
        actions.Children.Add(AsyncButton("+  Add source",()=>EditSourceAsync(null)));
        liveHeading=Heading("Live TV","Find something worth watching.",actions); Grid.SetColumnSpan(liveHeading,3); Place(livePage,liveHeading);
        var movieActions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
        closeNowPlaying=Button("Close stream",StopPlayback); movieActions.Children.Add(closeNowPlaying);
        backToBrowse=Button("Back to browse",()=> { if(fullscreen) ToggleFullscreen(); if(expandedVideo) ToggleExpandedVideo(); Navigate(playbackOrigin); }); movieActions.Children.Add(backToBrowse);
        libraryPlaybackHeading=Heading("Now playing","Your current stream. One player.",movieActions); libraryPlaybackHeading.Visibility=Visibility.Collapsed; Grid.SetColumnSpan(libraryPlaybackHeading,3); Place(livePage,libraryPlaybackHeading);
        categoryList=new ListView { SelectionMode=ListViewSelectionMode.Single }; QuietItems(categoryList);
        categoryList.SelectionChanged+=(_,_)=> { filter=categoryList.SelectedItem as string??"All channels"; _=GuardAsync(FilterLiveAsync); };
        var categories=new Grid { RowSpacing=12 }; categoryRail=categories; categories.RowDefinitions.Add(new() { Height=GridLength.Auto }); categories.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); var catsTitle=Text("BROWSE",10); catsTitle.Opacity=.45; catsTitle.CharacterSpacing=120; Place(categories,catsTitle); Place(categories,categoryList,1); Place(livePage,categories,1);
        var channels=new Grid { RowSpacing=14 }; channels.RowDefinitions.Add(new() { Height=GridLength.Auto }); channels.RowDefinitions.Add(new() { Height=GridLength.Auto }); channels.RowDefinitions.Add(new() { Height=GridLength.Auto }); channels.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        liveSummary=Text("CHANNELS",10); liveSummary.Opacity=.5; liveSummary.CharacterSpacing=100; Place(channels,liveSummary);
        channelSearch=SearchBox("Search channels"); channelSearch.TextChanged+=(sender,e)=> { _=GuardAsync(()=>FilterLiveAsync(true)); }; Place(channels,channelSearch,1);
        categoryPicker=Choice("",["All channels"],"All channels"); categoryPicker.SelectionChanged+=(_,_)=> { if(categoryPicker.SelectedItem is string selected) { filter=selected; _=GuardAsync(FilterLiveAsync); } }; Place(channels,categoryPicker,2);
        channelList=new ListView { IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.Single };
        channelList.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Grid Padding='2,9' MinHeight='70' ColumnSpacing='10'><Grid.ColumnDefinitions><ColumnDefinition Width='38'/><ColumnDefinition Width='*'/><ColumnDefinition Width='44'/></Grid.ColumnDefinitions><Border Width='38' Height='38' CornerRadius='9' Background='{ThemeResource NullTVFieldBackground}'><Grid><FontIcon Glyph='&#xE714;' FontSize='16' Opacity='.35'/><Image Source='{Binding Artwork}' Stretch='Uniform'/></Grid></Border><StackPanel Grid.Column='1' Spacing='5' VerticalAlignment='Center'><TextBlock Text='{Binding DisplayName}' FontSize='13' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding NowNext}' FontSize='11' Opacity='.5' TextTrimming='CharacterEllipsis'/></StackPanel><Button x:Name='FavoriteChannelButton' Grid.Column='2' Width='44' MinHeight='44' Padding='0' Background='Transparent' BorderThickness='0' CornerRadius='8' ToolTipService.ToolTip='{Binding FavoriteLabel}' AutomationProperties.Name='{Binding FavoriteLabel}'><FontIcon Glyph='{Binding FavoriteGlyph}' Foreground='{Binding FavoriteBrush}' FontSize='16'/></Button></Grid></DataTemplate>");
        QuietItems(channelList); WatchArtwork(channelList,88); channelList.ContainerContentChanging+=PrepareChannelFavorite;
        channelList.ItemClick+=(_,e)=> { if(e.ClickedItem is ChannelRow row) _=GuardAsync(()=>PlayAsync(row.Item)); };
        channelList.RightTapped+=(_,e)=> { if(e.OriginalSource is FrameworkElement { DataContext:ChannelRow row }) { var menu=new MenuFlyout(); var item=new MenuFlyoutItem { Text=row.IsFavorite ? "Remove from favorites" : "Add to favorites",Icon=new FontIcon { Glyph=row.FavoriteGlyph } }; item.Click+=(_,_)=>_=GuardAsync(()=>ToggleFavoriteAsync(row.Item)); menu.Items.Add(item); menu.ShowAt(channelList,e.GetPosition(channelList)); e.Handled=true; } };
        Place(channels,channelList,3); channelsCard=Card(channels,new Thickness(14)); Place(livePage,channelsCard,1,1);
        var stage=new Grid { RowSpacing=14 }; playerStage=stage; stage.RowDefinitions.Add(new() { Height=GridLength.Auto }); stage.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        playingTitle=Text("PLAYER",10); playingTitle.CharacterSpacing=100; playingTitle.Opacity=.5; playingInfo=Text("",12); playingInfo.Opacity=.5; var heading=Stack(playingTitle,playingInfo); heading.Spacing=5; playerHeading=heading; Place(stage,heading);
        videoGrid=new Grid { Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color(SurfaceHex)),ColumnSpacing=8,RowSpacing=8 };
        surface=CreateSurface(); videoGrid.Children.Add(surface); emptyLive=new Grid(); videoGrid.Children.Add(emptyLive); videoHome=Card(videoGrid,new Thickness(0)); Place(stage,videoHome,1);
        seek=new PlaybackSlider { Minimum=0,Maximum=1,MinHeight=20,Height=20,IsEnabled=false };
        seek.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,_)=>seeking=true),true);
        seek.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,_)=> { if(surface?.Player!=null) surface.Player.Command("seek",seek.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute"); seeking=false; }),true);
        seek.KeyUp+=(_,e)=> { if(e.Key is VirtualKey.Left or VirtualKey.Right && seek.IsEnabled) surface?.Player?.Command("seek",seek.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute"); };
        progressArea=new Grid { ColumnSpacing=12 }; progressArea.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); progressArea.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); progressArea.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); elapsedLabel=Text("0:00",11); durationLabel=Text("0:00",11); elapsedLabel.VerticalAlignment=durationLabel.VerticalAlignment=VerticalAlignment.Center; elapsedLabel.Opacity=durationLabel.Opacity=.8; Place(progressArea,elapsedLabel); Place(progressArea,seek,0,1); Place(progressArea,durationLabel,0,2);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(seek,"Playback position");
        liveBar=new Grid { ColumnSpacing=8,HorizontalAlignment=HorizontalAlignment.Center }; liveBar.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); liveBar.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var goLive=Button("●  LIVE",()=>_=GuardAsync(GoLiveAsync)); goLive.Padding=new Thickness(12,4,12,4); goLive.MinHeight=32; goLive.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#50FA7B")); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(goLive,"Go live"); ToolTipService.SetToolTip(goLive,"Return to the current live feed"); Place(liveBar,goLive);
        livePositionLabel=Text("Watching live",11); livePositionLabel.VerticalAlignment=VerticalAlignment.Center; Place(liveBar,livePositionLabel,0,1);
        liveBar.RowDefinitions.Add(new() { Height=GridLength.Auto }); liveBar.RowDefinitions.Add(new() { Height=GridLength.Auto });
        var liveTrack=new Border { Height=2,CornerRadius=new CornerRadius(1),Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#50FA7B")),Margin=new Thickness(4,1,4,0) }; Grid.SetColumnSpan(liveTrack,2); Place(liveBar,liveTrack,1);
        playerControls=new Grid { ColumnSpacing=8,HorizontalAlignment=HorizontalAlignment.Center }; playerControls.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); playerControls.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var transport=new StackPanel { Orientation=Orientation.Horizontal,Spacing=3 }; var tools=new StackPanel { Orientation=Orientation.Horizontal,Spacing=3 };
        previousButton=IconButton("\uE892","Previous channel",()=>ChannelStep(-1)); transport.Children.Add(previousButton);
        rewindButton=Button("−10",()=>SeekRelative(-10)); rewindButton.Padding=new Thickness(8); ToolTipService.SetToolTip(rewindButton,"Rewind 10 seconds"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(rewindButton,"Rewind 10 seconds"); transport.Children.Add(rewindButton);
        primaryTransport=IconButton("\uE768","Play",ActivateTransport); primaryTransport.Width=48; primaryTransport.Height=48; primaryTransport.CornerRadius=new CornerRadius(24); primaryTransport.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color(settings.Appearance.Accent)); primaryTransport.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#21222C")); transport.Children.Add(primaryTransport);
        forwardButton=Button("+10",()=>SeekRelative(10)); forwardButton.Padding=new Thickness(8); ToolTipService.SetToolTip(forwardButton,"Forward 10 seconds"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(forwardButton,"Forward 10 seconds"); transport.Children.Add(forwardButton);
        nextButton=IconButton("\uE893","Next channel",()=>ChannelStep(1)); transport.Children.Add(nextButton);
        stopButton=IconButton("\uE71A","Close stream",StopPlayback); transport.Children.Add(stopButton);
        muteButton=IconButton("\uE767","Mute",()=>surface?.Player?.Command("cycle","mute")); tools.Children.Add(muteButton);
        volumeControl=new PlaybackSlider { Minimum=0,Maximum=100,Value=settings.Playback.Volume,MinHeight=44,Width=84 }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(volumeControl,"Volume"); volumeControl.ValueChanged+=(_,e)=> { if(updatingTransport) return; foreach(var v in surfaces) v.Player?.Set("volume",e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture)); }; tools.Children.Add(volumeControl);
        subtitlesButton=Button("CC",()=>_=GuardAsync(SubtitlesMenuAsync)); subtitlesButton.Padding=new Thickness(8); subtitlesButton.Width=44; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(subtitlesButton,"Subtitles and closed captions"); ToolTipService.SetToolTip(subtitlesButton,"Subtitles and closed captions"); tools.Children.Add(subtitlesButton);
        favoriteButton=IconButton("\uE734","Add to favorites",()=> { var item=playing??lastPlayed; if(item!=null) _=GuardAsync(()=>ToggleFavoriteAsync(item)); }); tools.Children.Add(favoriteButton);
        expandButton=IconButton("\uE8A7","Expand video in window",ToggleExpandedVideo); tools.Children.Add(expandButton);
        fullscreenButton=IconButton("\uE740","Fullscreen",ToggleFullscreen); tools.Children.Add(fullscreenButton); var more=IconButton("\uE712","Playback options",()=>_=GuardAsync(PlaybackMenuAsync)); tools.Children.Add(more);
        Place(playerControls,transport); Place(playerControls,tools,0,1);
        var timeline=new Grid(); timeline.Children.Add(progressArea); timeline.Children.Add(liveBar);
        var overlayContent=Stack(timeline,playerControls); overlayContent.Spacing=8;
        var transportCard=Card(overlayContent,new Thickness(12)); transportCard.CornerRadius=new CornerRadius(14); controlsCard=transportCard;
        streamInfo=Text("Waiting for stream information…",11); streamInfo.LineHeight=22; streamInfo.TextWrapping=TextWrapping.NoWrap; streamInfo.TextTrimming=TextTrimming.CharacterEllipsis;
        var infoHeading=Text("STREAM INFO",10); infoHeading.CharacterSpacing=120; infoHeading.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#F1FA8C"));
        var infoContent=Stack(infoHeading,streamInfo); infoContent.Spacing=8; var infoCard=Card(infoContent,new Thickness(14)); infoCard.CornerRadius=new CornerRadius(14);
        foreach(var card in new[]{transportCard,infoCard}) { card.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#B021222C")); card.BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#30F8F8F2")); card.BorderThickness=new Thickness(.5); card.CornerRadius=new CornerRadius(8); card.HorizontalAlignment=HorizontalAlignment.Left; card.VerticalAlignment=VerticalAlignment.Top; }
        transportCard.RequestedTheme=infoCard.RequestedTheme=ElementTheme.Dark;
        foreach(var entry in root.Resources) transportCard.Resources[entry.Key]=entry.Value;
        foreach(var slider in new[]{seek,volumeControl})
        {
            var green=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#50FA7B"));
            slider.Foreground=green; slider.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#44475A")); slider.Resources["SliderTrackThemeHeight"]=2d;
            foreach(var key in new[]{"SliderTrackValueFill","SliderTrackValueFillPointerOver","SliderTrackValueFillPressed","SliderThumbBackground","SliderThumbBackgroundPointerOver","SliderThumbBackgroundPressed","AccentFillColorDefaultBrush","AccentFillColorSecondaryBrush","AccentFillColorTertiaryBrush"}) slider.Resources[key]=green;
        }
        transportCard.KeyDown+=OnKey;
        transportCard.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler(OnEscape),true);
        playerOverlay=new(this,transportCard,infoCard,()=> (surface,IsPlayerVisible && playing!=null && dialogDepth==0 && surface!=null && active.ContainsKey(surface)),()=>surface?.Focus(FocusState.Programmatic));
        playerOverlay.LayoutChanged+=(width,compact)=> {
            compactVideoControls=compact;
            transportCard.Padding=new Thickness(compact ? 4 : 6); overlayContent.Spacing=compact ? 2 : 4;
            foreach(var button in transport.Children.Concat(tools.Children).OfType<Button>())
            {
                var size=ReferenceEquals(button,primaryTransport) ? compact ? 28 : 32 : compact ? 26 : 30;
                button.MinHeight=button.Height=button.Width=size; button.MinWidth=0; button.Padding=new Thickness(0); button.FontSize=11;
                button.CornerRadius=new CornerRadius(ReferenceEquals(button,primaryTransport) ? size/2 : 6);
                if(button.Content is FontIcon icon) icon.FontSize=compact ? 12 : 14;
                if(!ReferenceEquals(button,primaryTransport)) button.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            goLive.MinHeight=goLive.Height=compact ? 18 : 20; goLive.FontSize=10; goLive.Padding=new Thickness(5,0,5,0); goLive.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            livePositionLabel.FontSize=10; livePositionLabel.MaxWidth=230; livePositionLabel.TextWrapping=TextWrapping.NoWrap; livePositionLabel.TextTrimming=TextTrimming.CharacterEllipsis;
            elapsedLabel.FontSize=durationLabel.FontSize=10;
            progressArea.Width=Math.Min(compact ? 340 : 560,width-12);
            infoHeading.Visibility=compact ? Visibility.Collapsed : Visibility.Visible; infoHeading.FontSize=9; infoContent.Spacing=3;
            streamInfo.FontSize=compact ? 9.5 : 10.5; streamInfo.MaxLines=compact ? 2 : 0; streamInfo.LineHeight=compact ? 12 : 15; streamInfo.MaxWidth=compact ? 150 : 230;
            infoCard.Padding=new Thickness(compact ? 5 : 7);
            if(volumeControl!=null) { volumeControl.MinHeight=volumeControl.Height=24; volumeControl.Width=64; volumeControl.Visibility=width>=760 ? Visibility.Visible : Visibility.Collapsed; }
            if(expandButton!=null) expandButton.Visibility=Visibility.Visible;
            if(favoriteButton!=null) favoriteButton.Visibility=width>=520 ? Visibility.Visible : Visibility.Collapsed;
            if(stopButton!=null) stopButton.Visibility=Visibility.Visible;
            more.Visibility=compact ? Visibility.Collapsed : Visibility.Visible; UpdatePlaybackControls();
        };
        playerOverlay.VisibilityChanged+=visible=>surface?.Player?.Set("sub-pos",(visible ? Math.Min(75,settings.Playback.SubtitlePosition) : settings.Playback.SubtitlePosition).ToString());
        Place(livePage,stage,1,2); pageHost.Children.Add(livePage); UpdateLiveLayout();
        transportTimer.Tick+=(_,_)=>UpdatePlaybackControls(); transportTimer.Start();
    }
    private void UpdateLiveLayout()
    {
        UpdatePlayerChrome();
        if(livePage==null) return; var available=pageHost.ActualWidth>0 ? pageHost.ActualWidth : 1000; var browse=currentTab=="Live TV"; var categories=browse && !channelsHidden && available>=900;
        livePage.ColumnSpacing=categories ? 16 : 0;
        livePage.ColumnDefinitions[0].Width=new(categories ? 140 : 0); livePage.ColumnDefinitions[1].Width=browse ? new(1,GridUnitType.Star) : new(0); livePage.ColumnDefinitions[2].Width=browse ? new(0) : new(1,GridUnitType.Star);
        if(categoryRail!=null) categoryRail.Visibility=categories ? Visibility.Visible : Visibility.Collapsed;
        if(channelsCard is FrameworkElement channelsPane) { channelsPane.Visibility=browse ? Visibility.Visible : Visibility.Collapsed; Grid.SetColumn(channelsPane,1); Grid.SetColumnSpan(channelsPane,2); }
        if(playerStage!=null) { playerStage.Visibility=browse ? Visibility.Collapsed : Visibility.Visible; Grid.SetColumn(playerStage,0); Grid.SetColumnSpan(playerStage,3); }
        if(categoryPicker!=null) categoryPicker.Visibility=browse && !categories ? Visibility.Visible : Visibility.Collapsed;
        if(liveHeading!=null) liveHeading.Visibility=browse ? Visibility.Visible : Visibility.Collapsed;
        if(libraryPlaybackHeading!=null) libraryPlaybackHeading.Visibility=currentTab=="Now Playing" && !expandedVideo && !fullscreen ? Visibility.Visible : Visibility.Collapsed;
        if(playerHeading!=null) playerHeading.Visibility=expandedVideo || fullscreen ? Visibility.Collapsed : Visibility.Visible;
        if(backToBrowse!=null) backToBrowse.Content=playbackOrigin=="Guide" ? "Back to guide" : playbackOrigin=="Media Servers" ? "Back to server" : playbackOrigin=="Movies & Series" ? "Back to library" : "Browse channels";
        if(playerControls!=null) playerControls.Visibility=playing==null && lastPlayed==null ? Visibility.Collapsed : Visibility.Visible;
        if(controlsCard!=null) controlsCard.Visibility=playing==null && lastPlayed==null ? Visibility.Collapsed : Visibility.Visible;
        if(liveSummary!=null) liveSummary.Text=$"CHANNELS  ·  {items.Count(i=>i.Kind==MediaKind.Live):N0}";
        UpdateEmptyPlayer(); UpdatePlaybackControls();
    }
    private VideoSurface CreateSurface(Window? owner=null)
    {
        var v=new VideoSurface(owner??this,settings.Playback);
        if(owner==null) surfaces.Add(v);
        // Reparenting between Guide preview/fullscreen unloads the XAML host.
        // Restore its native child only after the new host has loaded.
        if(owner==null) v.Loaded+=(_,_)=>DispatcherQueue.TryEnqueue(()=> { v.Hide(!IsPlayerVisible || dialogDepth>0 || !v.IsLoaded || !active.ContainsKey(v)); playerOverlay?.Update(); });
        v.Activity+=()=> { lastInput=DateTimeOffset.UtcNow; if(owner!=null) return; if(surface==v) return; surface=v; foreach(var tile in surfaces) tile.Player?.Set("mute",tile==v ? "no" : "yes"); if(active.TryGetValue(v,out var item)) { playing=item; if(playingTitle!=null) playingTitle.Text=item.Name; } else { playing=null; if(playingTitle!=null) playingTitle.Text="Choose a channel for this tile"; } UpdatePlaybackControls(); };
        v.Swipe+=ChannelStep;
        if(owner==null) v.ExitRequested+=ExitCurrentPlayerMode;
        v.Pressed+=()=> { if(owner==null) { v.Focus(FocusState.Programmatic); playerOverlay?.Reveal(); } };
        v.PointerPressed+=(_,_)=> { if(owner==null) { surface=v; playing=active.GetValueOrDefault(v); } };
        v.Initialized+=()=> { if(dialogDepth>0 || !IsPlayerVisible || !active.ContainsKey(v)) v.Hide(true); if(v.Player==null) return; v.Player.StateChanged+=()=>DispatcherQueue.TryEnqueue(UpdatePlaybackControls); v.Player.Loaded+=()=>DispatcherQueue.TryEnqueue(()=> { if(!active.ContainsKey(v)) return; validationPlaybackLoads++; status.Text="Playing"; if(ReferenceEquals(surface,v)) infoUpdated=default; UpdatePlaybackControls(); }); v.Player.Ended+=error=>DispatcherQueue.TryEnqueue(()=> { if(error || active.GetValueOrDefault(v)?.Kind==MediaKind.Live) _=GuardAsync(()=>ReconnectAsync(v)); else _=GuardAsync(()=>MarkEndedAsync(v)); }); };
        return v;
    }
    private async Task PlayAsync(MediaItem item)
    {
        ValidationStep("PLAY_REQUEST");
        playbackRequest?.Cancel(); using var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); playbackRequest=request;
        try { await PlayRequestAsync(item,request); }
        finally { if(ReferenceEquals(playbackRequest,request)) playbackRequest=null; }
    }
    private async Task PlayRequestAsync(MediaItem item,CancellationTokenSource request)
    {
        if(!await UnlockAsync(item)) return;
        if(request.IsCancellationRequested) return;
        await SaveProgressAsync(); await SyncStoppedAsync();
        var inGuide=currentTab=="Guide" && item.Kind==MediaKind.Live;
        if(currentTab is "Guide" or "Live TV" or "Movies & Series" or "Media Servers") playbackOrigin=currentTab;
        if((item.Kind!=MediaKind.Live || inGuide) && multi) ToggleMulti();
        if(!inGuide) Navigate("Now Playing");
        if(surface==null) return;
        if(request.IsCancellationRequested) return;
        await surface.InitializeAsync(); if(request.IsCancellationRequested) return; playing=item; lastPlayed=item; behindLive=false; infoUpdated=default; if(streamInfo!=null) streamInfo.Text="Waiting for stream information…"; if(item.Kind!=MediaKind.Live) channelsHidden=true; active[surface]=item; retries[surface]=0; generations[surface]=generations.GetValueOrDefault(surface)+1;
        var start=settings.Profile.History.GetValueOrDefault(item.Id)?.Position??0; if(item.Kind==MediaKind.Live || settings.Profile.History.GetValueOrDefault(item.Id)?.Watched==true) start=0;
        CancelStreamRequest(surface);
        var session=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); streamRequests[surface]=session;
        using var load=CancellationTokenSource.CreateLinkedTokenSource(request.Token,session.Token);
        try { await surface.Player!.PlayAsync(item,start,load.Token); } catch(OperationCanceledException) when(load.IsCancellationRequested) { return; } if(load.IsCancellationRequested) return;
        foreach(var tile in surfaces) tile.Player?.Set("mute",tile==surface ? "no" : "yes"); surface.Hide(false); if(emptyLive!=null) emptyLive.Visibility=Visibility.Collapsed;
        if(playingTitle!=null) { playingTitle.Text=item.Name; playingTitle.FontSize=18; playingTitle.Opacity=1; playingTitle.CharacterSpacing=0; } if(playingInfo!=null) playingInfo.Text=$"{item.Category}  ·  {(item.ArchiveDays>0 ? $"{item.ArchiveDays} day archive" : item.Kind==MediaKind.Live ? "LIVE" : "Direct play")}"; UpdateLiveLayout();
        settings.Profile.History[item.Id]=new(start,0,false,DateTimeOffset.UtcNow); await store.SaveAsync(settings);
        if(inGuide) { selectGuideChannel?.Invoke(item); UpdateGuideVideoLayout(); }
        surface.Focus(FocusState.Programmatic); playerOverlay?.Reveal();
        if(settings.Profile.Sources.FirstOrDefault(s=>s.Id==item.SourceId) is { Kind: SourceKind.Jellyfin or SourceKind.Emby } server) _=GuardAsync(()=>new MediaServerClient(http).StartedAsync(server,item,lifetime.Token));
        if(item.Kind==MediaKind.Live && epgs.GetValueOrDefault(item.SourceId)?.Count is not >0 && settings.Profile.Sources.FirstOrDefault(s=>s.Id==item.SourceId) is { Kind: SourceKind.Xtream } source)
        { _=GuardAsync(async ()=> { epgs[source.Id]=await new XtreamClient(http).EpgAsync(source,item,false,lifetime.Token); await IndexGuidesAsync(); UpdateNowNext(); }); }
    }
    private async Task ReconnectAsync(VideoSurface v)
    {
        if(!active.TryGetValue(v,out var item) || item.Kind!=MediaKind.Live) { status.Text="Playback ended with an error. Try another item or refresh the source."; return; }
        var generation=generations.GetValueOrDefault(v); var attempt=retries.GetValueOrDefault(v)+1; retries[v]=attempt;
        if(attempt>6) { status.Text="The stream failed after six reconnect attempts. Select it again to retry."; return; }
        var delay=Math.Min(30,1<<Math.Min(attempt-1,5)); status.Text=$"Stream interrupted. Reconnecting in {delay}s (attempt {attempt}/6)…";
        if(!streamRequests.TryGetValue(v,out var session)) return;
        var token=session.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delay),token);
            if(generations.GetValueOrDefault(v)==generation && active.GetValueOrDefault(v)?.Id==item.Id) if(v.Player!=null) await v.Player.PlayAsync(item,ct:token);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
    }
    private void CancelStreamRequest(VideoSurface video)
    {
        if(streamRequests.Remove(video,out var request)) { request.Cancel(); request.Dispose(); }
    }
    private async Task MarkEndedAsync(VideoSurface v) { if(active.TryGetValue(v,out var item) && item.Kind!=MediaKind.Live) { var state=settings.Profile.History.GetValueOrDefault(item.Id); if(state!=null) settings.Profile.History[item.Id]=state with { Watched=true }; await store.SaveAsync(settings); } }
    private async Task SaveProgressAsync()
    {
        foreach(var (v,item) in active) { if(v.Player==null) continue; var pos=v.Player.State.Position; var dur=v.Player.State.Duration; var watched=settings.Profile.History.GetValueOrDefault(item.Id)?.Watched??false; if(dur>0 && pos/dur>.95) watched=true; settings.Profile.History[item.Id]=new(item.Kind==MediaKind.Live ? 0 : pos,dur,watched,DateTimeOffset.UtcNow); }
        UpdatePlaybackControls();
        await store.SaveAsync(settings);
    }
    private void StopPlayback()
    {
        playerOverlay?.Hide(); playbackRequest?.Cancel();
        _=GuardAsync(SaveProgressAsync); _=GuardAsync(SyncStoppedAsync);
        foreach(var v in surfaces) { CancelStreamRequest(v); generations[v]=generations.GetValueOrDefault(v)+1; v.Player?.Command("stop"); v.Hide(true); }
        pipWindow?.Close(); active.Clear(); playing=null; behindLive=false;
        if(emptyLive!=null) emptyLive.Visibility=Visibility.Visible;
        if(playingTitle!=null) { playingTitle.Text="PLAYER"; playingTitle.FontSize=10; playingTitle.Opacity=.5; playingTitle.CharacterSpacing=100; }
        if(playingInfo!=null) playingInfo.Text="";
        ExitPlayerView(); status.Text="Playback stopped.";
    }
    private void ChannelStep(int step) { var list=currentTab=="Guide" || playbackOrigin=="Guide" ? FavoriteGuide.Channels(items,settings.Profile).ToList() : (channelList?.ItemsSource as IEnumerable<ChannelRow>)?.Select(r=>r.Item).ToList()??[]; if(list.Count==0) return; var index=list.FindIndex(i=>i.Id==playing?.Id); _=GuardAsync(()=>PlayAsync(list[(index+step+list.Count)%list.Count])); }
    private void UpdateLive()
    {
        if(categoryList==null) return;
        var cats=new[] { "All channels","Favorites","Recently watched" }.Concat(items.Where(i=>i.Kind==MediaKind.Live).Select(i=>i.Category).Distinct().Order()).ToList();
        if(!categoryList.Items.Cast<string>().SequenceEqual(cats)) { categoryList.ItemsSource=cats; categoryList.SelectedItem=cats.Contains(filter) ? filter : "All channels"; }
        if(categoryPicker!=null && !categoryPicker.Items.Cast<string>().SequenceEqual(cats)) { categoryPicker.ItemsSource=cats; categoryPicker.SelectedItem=cats.Contains(filter) ? filter : "All channels"; }
        UpdateLiveLayout();
        _=GuardAsync(FilterLiveAsync);
    }
    private async Task FilterLiveAsync()=>await FilterLiveAsync(false);
    private async Task FilterLiveAsync(bool debounce)
    {
        filterPending?.Cancel(); filterPending?.Dispose(); var request=filterPending=new(); var ct=request.Token; var revision=++filterRevision;
        try
        {
            if(debounce) await Task.Delay(180,ct);
            if(liveItemsSnapshot!=items)
            {
                liveItemsSnapshot=items;
                var catalog=items; liveRowsTask=Task.Run(()=>catalog.Where(i=>i.Kind==MediaKind.Live).Select(i=>new ChannelRow(i)).ToArray());
            }
            if(liveRowsTask!=null) { var ready=await liveRowsTask.WaitAsync(ct); ct.ThrowIfCancellationRequested(); if(!ReferenceEquals(liveRows,ready)) { liveRows=ready; foreach(var row in liveRows) row.IsFavorite=settings.Profile.Favorites.Contains(row.Item.Id); UpdateNowNext(); } }
            var search=channelSearch?.Text??""; var selected=filter; var rows=liveRows; var favorites=settings.Profile.Favorites.ToHashSet(); var history=new Dictionary<string,WatchState>(settings.Profile.History);
            var matches=await Task.Run(()=> { IEnumerable<ChannelRow> found=rows.Where(r=>r.Item.Name.Contains(search,StringComparison.OrdinalIgnoreCase) && (selected=="All channels" || selected=="Favorites" && favorites.Contains(r.Item.Id) || selected=="Recently watched" && history.ContainsKey(r.Item.Id) || r.Item.Category==selected)); if(selected=="Recently watched") found=found.OrderByDescending(r=>history.GetValueOrDefault(r.Item.Id)?.Updated); return found.ToArray(); },ct);
            if(ct.IsCancellationRequested || revision!=filterRevision || closed) return;
            if(channelList!.ItemsSource is not ChannelRow[] current || !current.SequenceEqual(matches)) channelList.ItemsSource=matches;
        }
        catch(OperationCanceledException) { }
    }
    private void UpdateNowNext()
    {
        var now=DateTimeOffset.UtcNow;
        foreach(var row in liveRows)
        {
            var source=settings.Profile.Sources.FirstOrDefault(s=>s.Id==row.Item.SourceId); var id=source?.EpgMappings.GetValueOrDefault(row.Item.Id,row.Item.EpgId=="" ? row.Item.Id : row.Item.EpgId)??row.Item.EpgId;
            var pair=GuideFor(row.Item.SourceId).NowNext(id,now);
            row.NowNext=pair.Now==null ? "No program information" : $"{pair.Now.Title}  ·  Next: {pair.Next?.Title??"—"}";
        }
    }
    private static Image? FindImage(DependencyObject? obj) { if(obj==null) return null; if(obj is Image image) return image; for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(obj);i++) { var result=FindImage(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(obj,i)); if(result!=null) return result; } return null; }
    private void ToggleFullscreen()
    {
        fullscreen=!fullscreen; AppWindow.SetPresenter(fullscreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        lastInput=DateTimeOffset.UtcNow; RefreshPlayerView();
    }
    private async Task PlaybackMenuAsync()
    {
        if(surface?.Player==null) return;
        var player=surface.Player; var tracks=await Task.Run(player.Tracks); var stats=await Task.Run(player.Stats);
        var audio=Choice("Audio track",new[]{"auto"}.Concat(tracks.Where(t=>t.Type=="audio").Select(t=>$"{t.Id}: {t.Name}")),"auto");
        var aspect=Choice("Aspect ratio",new[]{"Default","16:9","4:3","2.35:1"},settings.Playback.Aspect);
        var content=Stack(Text(stats,12),audio,aspect);
        if(playing!=null) { var item=playing; content.Children.Add(AsyncButton(settings.Profile.Favorites.Contains(item.Id) ? "Remove favorite" : "Add favorite",async ()=> { await ToggleFavoriteAsync(item); })); content.Children.Add(AsyncButton("Lock channel / item",async ()=> { if(settings.Profile.PinHash=="") throw new InvalidDataException("Set a parental PIN in Settings first."); settings.Profile.LockedItems.Add(item.Id); await store.SaveAsync(settings); status.Text="Item locked."; })); }
        if(playing?.Kind==MediaKind.Live && currentTab!="Guide") content.Children.Add(Button(multi ? "Exit multiview" : "Open 4-stream multiview",ToggleMulti)); content.Children.Add(Button("Picture in picture",OpenPip));
        if(await DialogAsync("Playback",content,"Apply")==ContentDialogResult.Primary) { player.Set("aid",((string)audio.SelectedItem).Split(':')[0]); player.Set("video-aspect-override",(string)aspect.SelectedItem=="Default" ? "-1" : (string)aspect.SelectedItem); }
    }
    private void ToggleMulti()
    {
        if(videoGrid==null) return; multi=!multi;
        if(multi) { videoGrid.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); videoGrid.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); videoGrid.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); videoGrid.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); for(int i=1;i<4;i++) { var s=CreateSurface(); Place(videoGrid,s,i/2,i%2); } status.Text="Tap a video tile, then select a channel for that tile."; }
        else { foreach(var v in surfaces.Skip(1).ToArray()) { CancelStreamRequest(v); active.Remove(v); generations[v]=generations.GetValueOrDefault(v)+1; v.Dispose(); videoGrid.Children.Remove(v); surfaces.Remove(v); } surface=surfaces[0]; playing=active.GetValueOrDefault(surface); videoGrid.RowDefinitions.Clear(); videoGrid.ColumnDefinitions.Clear(); }
    }
    private void OpenPip()
    {
        if(playing==null || pipWindow!=null) return; var item=playing; pipWindow=new Window { Title="NullTV · Picture in picture" }; var v=CreateSurface(pipWindow); active[v]=item; pipWindow.Content=v; pipWindow.AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay); v.Loaded+=async (_,_)=> { await v.InitializeAsync(); surface?.Player?.Set("mute","yes"); if(v.Player!=null) await v.Player.PlayAsync(item,surface?.Player?.State.Position??0); }; pipWindow.Closed+=(_,_)=> { active.Remove(v); generations[v]=generations.GetValueOrDefault(v)+1; v.Dispose(); pipWindow=null; surface?.Player?.Set("mute","no"); }; pipWindow.Activate();
    }
    private void OnKey(object sender,KeyRoutedEventArgs e)
    {
        if(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement((sender as FrameworkElement)?.XamlRoot??root.XamlRoot) is TextBox or PasswordBox or AutoSuggestBox) return;
        if(!IsPlayerVisible) return;
        if(e.Key is >=VirtualKey.Number0 and <=VirtualKey.Number9) { if(DateTimeOffset.UtcNow-lastInput>TimeSpan.FromSeconds(2)) numeric=""; numeric+=((int)e.Key-(int)VirtualKey.Number0).ToString(); lastInput=DateTimeOffset.UtcNow; status.Text=$"Channel {numeric} · press Enter"; e.Handled=true; return; }
        if(e.Key==VirtualKey.Enter && int.TryParse(numeric,out var n)) { numeric=""; var channel=items.FirstOrDefault(i=>i.Kind==MediaKind.Live && i.Number==n); if(channel!=null) _=GuardAsync(()=>PlayAsync(channel)); e.Handled=true; return; }
        if(e.Key==VirtualKey.Tab && ReferenceEquals(sender,root) && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) is VideoSurface) { playerOverlay?.FocusControls(); e.Handled=true; return; }
        if(e.Key is VirtualKey.Left or VirtualKey.Right && playing?.Kind!=MediaKind.Live && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement((sender as FrameworkElement)?.XamlRoot??root.XamlRoot) is not Slider) { SeekRelative(e.Key==VirtualKey.Left ? -10 : 10); e.Handled=true; return; }
        if(e.Key==VirtualKey.C) { _=GuardAsync(SubtitlesMenuAsync); e.Handled=true; return; }
        var key=e.Key==VirtualKey.Space ? "Space" : e.Key.ToString(); if(settings.Keymap.TryGetValue(key,out var action)) { playerOverlay?.Reveal(); RunInput(action); e.Handled=true; }
    }
    private void RunInput(string action) { lastInput=DateTimeOffset.UtcNow; switch(action) { case "Pause": surface?.Player?.Command("cycle","pause"); break; case "Fullscreen": ToggleFullscreen(); break; case "Previous": ChannelStep(-1); break; case "Next": ChannelStep(1); break; case "Mute": surface?.Player?.Command("cycle","mute"); break; } }
    private void PollController()
    {
        if(!IsPlayerVisible || Gamepad.Gamepads.Count==0) return; var buttons=Gamepad.Gamepads[0].GetCurrentReading().Buttons; var pressed=buttons&~lastButtons; lastButtons=buttons;
        if(pressed.HasFlag(GamepadButtons.A)) RunInput("Pause"); if(pressed.HasFlag(GamepadButtons.X)) RunInput("Fullscreen"); if(pressed.HasFlag(GamepadButtons.RightShoulder)) RunInput("Next"); if(pressed.HasFlag(GamepadButtons.LeftShoulder)) RunInput("Previous");
    }
}

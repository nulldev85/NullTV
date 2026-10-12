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
    private MediaItem? playing;
    private ListView? channelList,categoryList;
    private AutoSuggestBox? channelSearch;
    private TextBlock? playingTitle,playingInfo,liveSummary;
    private UIElement? emptyLive;
    private ComboBox? categoryPicker;
    private Grid? videoGrid;
    private Grid? playerStage;
    private UIElement? categoryRail,channelsCard,controlsCard;
    private StackPanel? playerControls;
    private Slider? seek;
    private bool seeking,fullscreen,multi;
    private Window? pipWindow;
    private string filter="All channels";
    private int filterRevision;
    private string numeric="";
    private GamepadButtons lastButtons;
    private sealed record ChannelRow(MediaItem Item,string Name,string Group,string NowNext,int Number,string Logo)
    { public override string ToString()=>Name; }
    private void BuildLive()
    {
        livePage=new Grid { ColumnSpacing=20 };
        livePage.RowDefinitions.Add(new() { Height=GridLength.Auto }); livePage.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        livePage.ColumnDefinitions.Add(new() { Width=new(140) }); livePage.ColumnDefinitions.Add(new() { Width=new(272) }); livePage.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var heading=Heading("Live TV","Your live channels and favorites, in one place.",AsyncButton("+  Add source",()=>EditSourceAsync(null))); Grid.SetColumnSpan(heading,3); Place(livePage,heading);
        categoryList=new ListView { SelectionMode=ListViewSelectionMode.Single }; categoryList.SelectionChanged+=(_,_)=> { filter=categoryList.SelectedItem as string??"All channels"; _=GuardAsync(FilterLiveAsync); };
        var categories=new Grid { RowSpacing=12 }; categoryRail=categories; categories.RowDefinitions.Add(new() { Height=GridLength.Auto }); categories.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); var catsTitle=Text("CATEGORIES",10); catsTitle.Opacity=.4; catsTitle.CharacterSpacing=120; Place(categories,catsTitle); Place(categories,categoryList,1); Place(livePage,categories,1);
        // A Grid gives the virtualized list a bounded viewport, even for 50,000 entries.
        var channels=new Grid { RowSpacing=12 }; channels.RowDefinitions.Add(new() { Height=GridLength.Auto }); channels.RowDefinitions.Add(new() { Height=GridLength.Auto }); channels.RowDefinitions.Add(new() { Height=GridLength.Auto }); channels.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        liveSummary=Text("CHANNELS",10); liveSummary.Opacity=.5; liveSummary.CharacterSpacing=100; Place(channels,liveSummary);
        channelSearch=new AutoSuggestBox { PlaceholderText="Search channels",MinHeight=44 }; channelSearch.TextChanged+=(_,_)=>_=GuardAsync(FilterLiveAsync); Place(channels,channelSearch,1);
        categoryPicker=new ComboBox { MinHeight=44,HorizontalAlignment=HorizontalAlignment.Stretch }; categoryPicker.SelectionChanged+=(_,_)=> { if(categoryPicker.SelectedItem is string selected) { filter=selected; _=GuardAsync(FilterLiveAsync); } }; Place(channels,categoryPicker,2);
        channelList=new ListView { IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.Single };
        channelList.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Padding='4,10' MinHeight='68' ColumnSpacing='12'><Grid.ColumnDefinitions><ColumnDefinition Width='40'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Border Width='40' Height='40' CornerRadius='10' Background='#24211E'><Grid><FontIcon Glyph='&#xE714;' FontSize='16' Opacity='.4'/><Image Stretch='Uniform'/></Grid></Border><StackPanel Grid.Column='1' Spacing='5'><TextBlock Text='{Binding Name}' FontSize='12' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding NowNext}' FontSize='10' Opacity='.5' TextTrimming='CharacterEllipsis'/></StackPanel></Grid></DataTemplate>");
        channelList.ItemClick+=(_,e)=> { if(e.ClickedItem is ChannelRow row) _=GuardAsync(()=>PlayAsync(row.Item)); };
        Place(channels,channelList,3); channelsCard=Card(channels,new Thickness(14)); Place(livePage,channelsCard,1,1);
        var stage=new Grid { RowSpacing=12 }; playerStage=stage; stage.RowDefinitions.Add(new() { Height=GridLength.Auto }); stage.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); stage.RowDefinitions.Add(new() { Height=GridLength.Auto }); stage.RowDefinitions.Add(new() { Height=GridLength.Auto });
        playingTitle=Text("PLAYER",10); playingTitle.CharacterSpacing=100; playingTitle.Opacity=.5; playingInfo=Text("",12); playingInfo.Opacity=.5;
        var playerHeading=Stack(playingTitle,playingInfo); playerHeading.Spacing=4; Place(stage,playerHeading);
        videoGrid=new Grid { Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#050505")),ColumnSpacing=8,RowSpacing=8 };
        surface=CreateSurface(); videoGrid.Children.Add(surface); emptyLive=EmptyState("\uE714","Television, on your terms.","Connect your own account or playlist. Your channels, guide and favorites will be ready here.","Add your first source",()=>EditSourceAsync(null)); videoGrid.Children.Add(emptyLive); Place(stage,Card(videoGrid,new Thickness(0)),1);
        seek=new Slider { Minimum=0,Maximum=1,MinHeight=44,IsEnabled=false }; seek.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,_)=>seeking=true),true); seek.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,_)=> { if(surface?.Player!=null) surface.Player.Command("seek",seek.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute"); seeking=false; }),true); seek.KeyUp+=(_,e)=> { if(e.Key is VirtualKey.Left or VirtualKey.Right && seek.IsEnabled) surface?.Player?.Command("seek",seek.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute"); }; Place(stage,seek,2);
        playerControls=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6 };
        playerControls.Children.Add(IconButton("\uE892","Previous channel",()=>ChannelStep(-1)));
        var play=IconButton("\uE768","Play or pause",()=>surface?.Player?.Command("cycle","pause")); play.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color(settings.Appearance.Accent)); play.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#191613")); play.CornerRadius=new CornerRadius(22); playerControls.Children.Add(play);
        playerControls.Children.Add(IconButton("\uE893","Next channel",()=>ChannelStep(1))); playerControls.Children.Add(IconButton("\uE71A","Stop",StopPlayback));
        var divider=new Border { Width=1,Height=24,Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#302C26")),Margin=new Thickness(8,0,8,0) }; playerControls.Children.Add(divider);
        playerControls.Children.Add(IconButton("\uE767","Mute",()=>surface?.Player?.Command("cycle","mute")));
        var volume=new Slider { Minimum=0,Maximum=100,Value=settings.Playback.Volume,MinHeight=44,Width=120 }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(volume,"Volume"); volume.ValueChanged+=(_,e)=> { foreach(var v in surfaces) v.Player?.Set("volume",e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture)); }; playerControls.Children.Add(volume);
        playerControls.Children.Add(IconButton("\uE740","Fullscreen",ToggleFullscreen)); playerControls.Children.Add(IconButton("\uE712","Playback options",()=>_=GuardAsync(PlaybackMenuAsync))); controlsCard=Card(playerControls,new Thickness(10,6,10,6)); Place(stage,controlsCard,3);
        Place(livePage,stage,1,2); pageHost.Children.Add(livePage); UpdateLiveLayout();
    }
    private void UpdateLiveLayout()
    {
        if(livePage==null || fullscreen) return; var available=pageHost.ActualWidth>0 ? pageHost.ActualWidth : 1000; var hasChannels=items.Any(i=>i.Kind==MediaKind.Live);
        livePage.ColumnDefinitions[0].Width=new(hasChannels && available>=1120 ? 140 : 0); livePage.ColumnDefinitions[1].Width=new(hasChannels ? 272 : 0);
        if(categoryRail!=null) categoryRail.Visibility=hasChannels && available>=1120 ? Visibility.Visible : Visibility.Collapsed;
        if(channelsCard!=null) channelsCard.Visibility=hasChannels ? Visibility.Visible : Visibility.Collapsed;
        if(playerStage!=null) { Grid.SetColumn(playerStage,hasChannels ? 2 : 0); Grid.SetColumnSpan(playerStage,hasChannels ? 1 : 3); }
        if(categoryPicker!=null) categoryPicker.Visibility=available>=1120 ? Visibility.Collapsed : Visibility.Visible;
        if(seek!=null) seek.Visibility=playing==null ? Visibility.Collapsed : Visibility.Visible;
        if(playerControls!=null) playerControls.Visibility=playing==null ? Visibility.Collapsed : Visibility.Visible;
        if(controlsCard!=null) controlsCard.Visibility=playing==null ? Visibility.Collapsed : Visibility.Visible;
        if(liveSummary!=null) liveSummary.Text=$"CHANNELS  ·  {items.Count(i=>i.Kind==MediaKind.Live):N0}";
    }
    private VideoSurface CreateSurface(Window? owner=null)
    {
        var v=new VideoSurface(owner??this,settings.Playback);
        if(owner==null) surfaces.Add(v);
        v.Activity+=()=> { lastInput=DateTimeOffset.UtcNow; if(owner!=null) return; if(playerControls!=null) playerControls.Visibility=Visibility.Visible; surface=v; foreach(var tile in surfaces) tile.Player?.Set("mute",tile==v ? "no" : "yes"); if(active.TryGetValue(v,out var item)) { playing=item; if(playingTitle!=null) playingTitle.Text=item.Name; } else { playing=null; if(playingTitle!=null) playingTitle.Text="Choose a channel for this tile"; } };
        v.Swipe+=ChannelStep;
        v.PointerPressed+=(_,_)=> { if(owner==null) { surface=v; playing=active.GetValueOrDefault(v); } };
        v.Loaded+=(_,_)=> { if(dialogDepth>0 || currentTab!="Live TV" || !active.ContainsKey(v)) v.Hide(true); if(v.Player==null) return; v.Player.Loaded+=()=>DispatcherQueue.TryEnqueue(()=> { status.Text="Playing"; }); v.Player.Ended+=error=>DispatcherQueue.TryEnqueue(()=> { if(error || active.GetValueOrDefault(v)?.Kind==MediaKind.Live) _=GuardAsync(()=>ReconnectAsync(v)); else _=GuardAsync(()=>MarkEndedAsync(v)); }); };
        return v;
    }
    private async Task PlayAsync(MediaItem item)
    {
        if(!await UnlockAsync(item)) return;
        await SaveProgressAsync(); await SyncStoppedAsync(); Navigate("Live TV"); if(surface==null) return;
        surface.Initialize(); playing=item; active[surface]=item; retries[surface]=0; generations[surface]=generations.GetValueOrDefault(surface)+1;
        var start=settings.Profile.History.GetValueOrDefault(item.Id)?.Position??0; if(item.Kind==MediaKind.Live || settings.Profile.History.GetValueOrDefault(item.Id)?.Watched==true) start=0;
        surface.Player!.Play(item,start); foreach(var tile in surfaces) tile.Player?.Set("mute",tile==surface ? "no" : "yes"); surface.Hide(false); if(emptyLive!=null) emptyLive.Visibility=Visibility.Collapsed;
        if(playingTitle!=null) { playingTitle.Text=item.Name; playingTitle.FontSize=18; playingTitle.Opacity=1; playingTitle.CharacterSpacing=0; } if(playingInfo!=null) playingInfo.Text=$"{item.Category}  ·  {(item.ArchiveDays>0 ? $"{item.ArchiveDays} day archive" : item.Kind==MediaKind.Live ? "LIVE" : "Direct play")}"; UpdateLiveLayout();
        settings.Profile.History[item.Id]=new(start,0,false,DateTimeOffset.UtcNow); await store.SaveAsync(settings);
        channelList?.Focus(FocusState.Programmatic);
        if(settings.Profile.Sources.FirstOrDefault(s=>s.Id==item.SourceId) is { Kind: SourceKind.Jellyfin or SourceKind.Emby } server) _=GuardAsync(()=>new MediaServerClient(http).StartedAsync(server,item,lifetime.Token));
        if(item.Kind==MediaKind.Live && epgs.GetValueOrDefault(item.SourceId)?.Count is not >0 && settings.Profile.Sources.FirstOrDefault(s=>s.Id==item.SourceId) is { Kind: SourceKind.Xtream } source)
        { _=GuardAsync(async ()=> { epgs[source.Id]=await new XtreamClient(http).EpgAsync(source,item,false,lifetime.Token); UpdateLive(); }); }
    }
    private async Task ReconnectAsync(VideoSurface v)
    {
        if(!active.TryGetValue(v,out var item) || item.Kind!=MediaKind.Live) { status.Text="Playback ended with an error. Try another item or refresh the source."; return; }
        var generation=generations.GetValueOrDefault(v); var attempt=retries.GetValueOrDefault(v)+1; retries[v]=attempt;
        if(attempt>6) { status.Text="The stream failed after six reconnect attempts. Select it again to retry."; return; }
        var delay=Math.Min(30,1<<Math.Min(attempt-1,5)); status.Text=$"Stream interrupted. Reconnecting in {delay}s (attempt {attempt}/6)…";
        await Task.Delay(TimeSpan.FromSeconds(delay),lifetime.Token);
        if(generations.GetValueOrDefault(v)==generation && active.GetValueOrDefault(v)?.Id==item.Id) v.Player?.Play(item);
    }
    private async Task MarkEndedAsync(VideoSurface v) { if(active.TryGetValue(v,out var item) && item.Kind!=MediaKind.Live) { var state=settings.Profile.History.GetValueOrDefault(item.Id); if(state!=null) settings.Profile.History[item.Id]=state with { Watched=true }; await store.SaveAsync(settings); } }
    private async Task SaveProgressAsync()
    {
        foreach(var (v,item) in active) { if(v.Player==null) continue; var pos=v.Player.Number("time-pos"); var dur=v.Player.Number("duration"); var watched=settings.Profile.History.GetValueOrDefault(item.Id)?.Watched??false; if(dur>0 && pos/dur>.95) watched=true; settings.Profile.History[item.Id]=new(item.Kind==MediaKind.Live ? 0 : pos,dur,watched,DateTimeOffset.UtcNow); }
        if(seek!=null && surface?.Player!=null && !seeking) { var dur=surface.Player.Number("duration"); seek.IsEnabled=dur>0 && playing?.Kind!=MediaKind.Live; seek.Maximum=Math.Max(1,dur); seek.Value=surface.Player.Number("time-pos"); }
        await store.SaveAsync(settings);
    }
    private void StopPlayback() { _=GuardAsync(SyncStoppedAsync); foreach(var v in surfaces) { generations[v]=generations.GetValueOrDefault(v)+1; v.Player?.Command("stop"); v.Hide(true); } pipWindow?.Close(); active.Clear(); playing=null; if(emptyLive!=null) emptyLive.Visibility=Visibility.Visible; if(playingTitle!=null) { playingTitle.Text="PLAYER"; playingTitle.FontSize=10; playingTitle.Opacity=.5; playingTitle.CharacterSpacing=100; } if(playingInfo!=null) playingInfo.Text=""; UpdateLiveLayout(); status.Text="Playback stopped."; }
    private void ChannelStep(int step) { var list=(channelList?.ItemsSource as IEnumerable<ChannelRow>)?.Select(r=>r.Item).ToList()??[]; if(list.Count==0) return; var index=list.FindIndex(i=>i.Id==playing?.Id); _=GuardAsync(()=>PlayAsync(list[(index+step+list.Count)%list.Count])); }
    private void UpdateLive()
    {
        if(categoryList==null) return;
        var cats=new[] { "All channels","Favorites","Recently watched" }.Concat(items.Where(i=>i.Kind==MediaKind.Live).Select(i=>i.Category).Distinct().Order()).ToList();
        if(!categoryList.Items.Cast<string>().SequenceEqual(cats)) { categoryList.ItemsSource=cats; categoryList.SelectedItem=cats.Contains(filter) ? filter : "All channels"; }
        if(categoryPicker!=null && !categoryPicker.Items.Cast<string>().SequenceEqual(cats)) { categoryPicker.ItemsSource=cats; categoryPicker.SelectedItem=cats.Contains(filter) ? filter : "All channels"; }
        UpdateLiveLayout();
        _=GuardAsync(FilterLiveAsync);
    }
    private async Task FilterLiveAsync()
    {
        var revision=++filterRevision; var search=channelSearch?.Text??""; var selected=filter; var snapshot=items.ToArray(); var p=settings.Profile; var now=DateTimeOffset.UtcNow;
        var favorites=p.Favorites.ToHashSet(); var history=new Dictionary<string,WatchState>(p.History); var mappings=p.Sources.ToDictionary(s=>s.Id,s=>new Dictionary<string,string>(s.EpgMappings)); var guide=epgs.ToDictionary(e=>e.Key,e=>e.Value.ToArray());
        var rows=await Task.Run(()=> {
            var index=guide.ToDictionary(g=>g.Key,g=>g.Value.GroupBy(x=>x.ChannelId).ToDictionary(x=>x.Key,x=>x.OrderBy(p=>p.Start).ToArray()));
            var matches=snapshot.Where(i=>i.Kind==MediaKind.Live && i.Name.Contains(search,StringComparison.OrdinalIgnoreCase) && (selected=="All channels" || selected=="Favorites" && favorites.Contains(i.Id) || selected=="Recently watched" && history.ContainsKey(i.Id) || i.Category==selected));
            if(selected=="Recently watched") matches=matches.OrderByDescending(i=>history.GetValueOrDefault(i.Id)?.Updated);
            return matches.Select(i=> { var epgId=mappings.GetValueOrDefault(i.SourceId)?.GetValueOrDefault(i.Id,i.EpgId=="" ? i.Id : i.EpgId)??i.EpgId; var programmes=index.GetValueOrDefault(i.SourceId)?.GetValueOrDefault(epgId)??[]; var current=programmes.FirstOrDefault(x=>x.Start<=now && x.End>now); var next=programmes.FirstOrDefault(x=>x.Start>now); return new ChannelRow(i,$"{(favorites.Contains(i.Id) ? "★ " : "")}{i.Number:000}  {i.Name}",i.Category,current==null ? "No programme information" : $"{current.Title}  •  Next: {next?.Title??"—"}",i.Number,""); }).ToList();
        });
        if(revision!=filterRevision || closed) return;
        channelList!.ItemsSource=rows;
        // Cache only the first viewport and following rows; recycled containers request remaining artwork below.
        channelList.ContainerContentChanging-=LoadChannelImage; channelList.ContainerContentChanging+=LoadChannelImage;
    }
    private async void LoadChannelImage(ListViewBase sender,ContainerContentChangingEventArgs args)
    {
        var container=args.ItemContainer; var img=FindImage(container.ContentTemplateRoot);
        if(img!=null) img.Source=null;
        if(args.InRecycleQueue || args.Item is not ChannelRow row || row.Item.Logo=="" || imageCache==null) return;
        try { var path=await imageCache.GetAsync(row.Item.Logo,lifetime.Token); if(path==null || !ReferenceEquals(container.Content,row)) return; if(img!=null) { var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path); using var stream=await file.OpenReadAsync(); var bitmap=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth=88 }; await bitmap.SetSourceAsync(stream); if(ReferenceEquals(container.Content,row)) img.Source=bitmap; } } catch(Exception e) when(e is not OutOfMemoryException) { }
    }
    private static Image? FindImage(DependencyObject? obj) { if(obj==null) return null; if(obj is Image image) return image; for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(obj);i++) { var result=FindImage(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(obj,i)); if(result!=null) return result; } return null; }
    private void ToggleFullscreen()
    {
        fullscreen=!fullscreen; AppWindow.SetPresenter(fullscreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        if(sidebar!=null) sidebar.Visibility=fullscreen ? Visibility.Collapsed : Visibility.Visible; root.ColumnDefinitions[0].Width=new(fullscreen ? 0 : 184); status.Visibility=fullscreen ? Visibility.Collapsed : Visibility.Visible; if(livePage!=null && fullscreen) { livePage.ColumnDefinitions[0].Width=new(0); livePage.ColumnDefinitions[1].Width=new(0); }
        root.RowDefinitions[0].Height=new(fullscreen ? 0 : 58); if(!fullscreen) UpdateLiveLayout(); lastInput=DateTimeOffset.UtcNow;
    }
    private async Task PlaybackMenuAsync()
    {
        if(surface?.Player==null) return;
        var player=surface.Player; var tracks=player.Tracks();
        var audio=Choice("Audio track",new[]{"auto"}.Concat(tracks.Where(t=>t.Type=="audio").Select(t=>$"{t.Id}: {t.Name}")),"auto");
        var sub=Choice("Subtitle track",new[]{"no","auto"}.Concat(tracks.Where(t=>t.Type=="sub").Select(t=>$"{t.Id}: {t.Name}")),"no");
        var aspect=Choice("Aspect ratio",new[]{"Default","16:9","4:3","2.35:1"},settings.Playback.Aspect);
        var content=Stack(Text(player.Stats(),12),audio,sub,aspect);
        if(playing!=null) { var item=playing; content.Children.Add(AsyncButton(settings.Profile.Favorites.Contains(item.Id) ? "Remove favorite" : "Add favorite",async ()=> { if(!settings.Profile.Favorites.Add(item.Id)) settings.Profile.Favorites.Remove(item.Id); await store.SaveAsync(settings); UpdateLive(); })); content.Children.Add(AsyncButton("Lock channel / item",async ()=> { if(settings.Profile.PinHash=="") throw new InvalidDataException("Set a parental PIN in Settings first."); settings.Profile.LockedItems.Add(item.Id); await store.SaveAsync(settings); status.Text="Item locked."; })); }
        content.Children.Add(Button(multi ? "Exit multiview" : "Open 4-stream multiview",ToggleMulti)); content.Children.Add(Button("Picture in picture",OpenPip));
        if(await DialogAsync("Playback",content,"Apply")==ContentDialogResult.Primary) { player.Set("aid",((string)audio.SelectedItem).Split(':')[0]); player.Set("sid",((string)sub.SelectedItem).Split(':')[0]); player.Set("video-aspect-override",(string)aspect.SelectedItem=="Default" ? "-1" : (string)aspect.SelectedItem); }
    }
    private void ToggleMulti()
    {
        if(videoGrid==null) return; multi=!multi;
        if(multi) { videoGrid.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); videoGrid.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); videoGrid.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); videoGrid.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); for(int i=1;i<4;i++) { var s=CreateSurface(); Place(videoGrid,s,i/2,i%2); } status.Text="Tap a video tile, then select a channel for that tile."; }
        else { foreach(var v in surfaces.Skip(1).ToArray()) { active.Remove(v); generations[v]=generations.GetValueOrDefault(v)+1; v.Dispose(); videoGrid.Children.Remove(v); surfaces.Remove(v); } surface=surfaces[0]; playing=active.GetValueOrDefault(surface); videoGrid.RowDefinitions.Clear(); videoGrid.ColumnDefinitions.Clear(); }
    }
    private void OpenPip()
    {
        if(playing==null || pipWindow!=null) return; var item=playing; pipWindow=new Window { Title="NullTV · Picture in picture" }; var v=CreateSurface(pipWindow); active[v]=item; pipWindow.Content=v; pipWindow.AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay); v.Loaded+=(_,_)=> { surface?.Player?.Set("mute","yes"); v.Player?.Play(item,surface?.Player?.Number("time-pos")??0); }; pipWindow.Closed+=(_,_)=> { active.Remove(v); generations[v]=generations.GetValueOrDefault(v)+1; v.Dispose(); pipWindow=null; surface?.Player?.Set("mute","no"); }; pipWindow.Activate();
    }
    private void OnKey(object sender,KeyRoutedEventArgs e)
    {
        if(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) is TextBox or PasswordBox or AutoSuggestBox) return;
        if(e.Key==VirtualKey.Escape && fullscreen) { ToggleFullscreen(); e.Handled=true; return; }
        if(e.Key is >=VirtualKey.Number0 and <=VirtualKey.Number9) { if(DateTimeOffset.UtcNow-lastInput>TimeSpan.FromSeconds(2)) numeric=""; numeric+=((int)e.Key-(int)VirtualKey.Number0).ToString(); lastInput=DateTimeOffset.UtcNow; status.Text=$"Channel {numeric} · press Enter"; e.Handled=true; return; }
        if(e.Key==VirtualKey.Enter && int.TryParse(numeric,out var n)) { numeric=""; var channel=items.FirstOrDefault(i=>i.Kind==MediaKind.Live && i.Number==n); if(channel!=null) _=GuardAsync(()=>PlayAsync(channel)); e.Handled=true; return; }
        var key=e.Key==VirtualKey.Space ? "Space" : e.Key.ToString(); if(settings.Keymap.TryGetValue(key,out var action)) { RunInput(action); e.Handled=true; }
    }
    private void RunInput(string action) { lastInput=DateTimeOffset.UtcNow; switch(action) { case "Pause": surface?.Player?.Command("cycle","pause"); break; case "Fullscreen": ToggleFullscreen(); break; case "Previous": ChannelStep(-1); break; case "Next": ChannelStep(1); break; case "Mute": surface?.Player?.Command("cycle","mute"); break; } }
    private void PollController()
    {
        if(Gamepad.Gamepads.Count==0) return; var buttons=Gamepad.Gamepads[0].GetCurrentReading().Buttons; var pressed=buttons&~lastButtons; lastButtons=buttons;
        if(pressed.HasFlag(GamepadButtons.A)) RunInput("Pause"); if(pressed.HasFlag(GamepadButtons.X)) RunInput("Fullscreen"); if(pressed.HasFlag(GamepadButtons.RightShoulder)) RunInput("Next"); if(pressed.HasFlag(GamepadButtons.LeftShoulder)) RunInput("Previous");
    }
}

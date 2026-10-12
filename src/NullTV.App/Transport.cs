using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NullTV.Core;
namespace NullTV.App;
public sealed partial class MainWindow
{
    private sealed class ChannelRow(MediaItem item) : ArtworkRow(item)
    {
        public string DisplayName=>$"{Item.Number:000}  {Item.Name}";
        private string nowNext="No program information";
        public string NowNext { get=>nowNext; set { if(nowNext==value) return; nowNext=value; Changed(nameof(NowNext)); } }
        private bool favorite;
        public bool IsFavorite { get=>favorite; set { if(favorite==value) return; favorite=value; Changed(nameof(FavoriteGlyph)); Changed(nameof(FavoriteLabel)); Changed(nameof(FavoriteBrush)); } }
        public Microsoft.UI.Xaml.Media.Brush FavoriteBrush=>new Microsoft.UI.Xaml.Media.SolidColorBrush(Color(favorite ? LightFields ? "#A39514" : "#F1FA8C" : LightFields ? "#202228" : "#F8F8F2"));
        public string FavoriteGlyph=>favorite ? "\uE735" : "\uE734";
        public string FavoriteLabel=>$"{(favorite ? "Remove" : "Add")} {Item.Name} {(favorite ? "from" : "to")} favorites";
    }
    private ChannelRow[] liveRows=[];
    private List<MediaItem>? liveItemsSnapshot;
    private Task<ChannelRow[]>? liveRowsTask;
    private CancellationTokenSource? filterPending;
    private Button? primaryTransport,previousButton,nextButton,stopButton,favoriteButton,muteButton;
    private Slider? volumeControl;
    private Grid? progressArea,liveHeading;
    private TextBlock? elapsedLabel,durationLabel;
    private MediaItem? lastPlayed;
    private bool updatingTransport;
    private string emptyPlayerMode="";
    private readonly DispatcherTimer transportTimer=new() { Interval=TimeSpan.FromMilliseconds(250) };
    private void ActivateTransport()
    {
        if(playing==null) { if(lastPlayed!=null) _=GuardAsync(()=>PlayAsync(lastPlayed)); return; }
        if(surface?.Player is not { } player) return;
        if(player.State.Idle) { _=GuardAsync(()=>PlayAsync(playing)); return; }
        player.Command("cycle","pause");
        playerOverlay?.Reveal();
    }
    private static void LabelButton(Button? button,string glyph,string label)
    {
        if(button==null) return;
        if(button.Content is FontIcon icon && icon.Glyph!=glyph) icon.Glyph=glyph;
        if(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)!=label) { Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,label); ToolTipService.SetToolTip(button,label); }
    }
    private void UpdatePlaybackControls()
    {
        if(primaryTransport==null || closed) return;
        var item=playing??lastPlayed; var state=surface?.Player?.State; var running=playing!=null && state?.Idle==false; var live=item?.Kind==MediaKind.Live;
        LabelButton(primaryTransport,running && !state!.Paused ? "\uE769" : "\uE768",running && !state!.Paused ? "Pause" : "Play");
        primaryTransport.IsEnabled=item!=null;
        if(stopButton!=null) stopButton.IsEnabled=running;
        if(previousButton!=null) previousButton.Visibility=item?.Kind==MediaKind.Live && !compactVideoControls ? Visibility.Visible : Visibility.Collapsed;
        if(nextButton!=null) nextButton.Visibility=item?.Kind==MediaKind.Live && !compactVideoControls ? Visibility.Visible : Visibility.Collapsed;
        if(rewindButton!=null) { rewindButton.Visibility=live ? Visibility.Collapsed : Visibility.Visible; rewindButton.IsEnabled=running && state?.Duration>0; }
        if(forwardButton!=null) { forwardButton.Visibility=live ? Visibility.Collapsed : Visibility.Visible; forwardButton.IsEnabled=running && state?.Duration>0; }
        if(liveBar!=null) liveBar.Visibility=playing!=null && live ? Visibility.Visible : Visibility.Collapsed;
        if(live && running && state!.Paused) behindLive=true;
        if(livePositionLabel!=null) { livePositionLabel.Text=state?.Paused==true ? "Paused · Select LIVE to return" : behindLive ? "Behind live · Select LIVE to return" : state?.Buffering==true ? "Buffering live feed…" : "Watching live"; livePositionLabel.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color(behindLive ? "#F1FA8C" : "#F8F8F2")); }
        if(subtitlesButton!=null) { subtitlesButton.IsEnabled=running; subtitlesButton.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color(state?.SubtitlesVisible==true && state.SubtitleId is not ("no" or "") ? "#BD93F9" : "#F8F8F2")); }
        var favorite=item!=null && settings.Profile.Favorites.Contains(item.Id); LabelButton(favoriteButton,favorite ? "\uE735" : "\uE734",favorite ? "Remove from favorites" : "Add to favorites"); if(favoriteButton!=null) { favoriteButton.IsEnabled=item!=null; favoriteButton.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color(favorite ? "#F1FA8C" : "#F8F8F2")); }
        LabelButton(muteButton,state?.Muted==true ? "\uE74F" : "\uE767",state?.Muted==true ? "Unmute" : "Mute");
        if(progressArea!=null) progressArea.Visibility=playing!=null && !live && state?.Duration>0 ? Visibility.Visible : Visibility.Collapsed;
        if(state!=null)
        {
            updatingTransport=true;
            try { if(volumeControl!=null && Math.Abs(volumeControl.Value-state.Volume)>.5) volumeControl.Value=state.Volume; if(seek!=null && !seeking) { seek.IsEnabled=running && !live; seek.Maximum=Math.Max(1,state.Duration); seek.Value=Math.Clamp(state.Position,0,seek.Maximum); } }
            finally { updatingTransport=false; }
            if(elapsedLabel!=null) elapsedLabel.Text=TimeCode(state.Position); if(durationLabel!=null) durationLabel.Text=TimeCode(state.Duration);
        }
        if(playerOverlay?.IsVisible==true && infoTask?.IsCompleted!=false && DateTimeOffset.UtcNow-infoUpdated>TimeSpan.FromSeconds(2)) infoTask=GuardAsync(UpdateStreamInfoAsync);
    }
    private void SeekRelative(int seconds)
    {
        if(playing?.Kind==MediaKind.Live || surface?.Player is not { } player || player.State.Idle || player.State.Duration<=0) return;
        var target=Math.Clamp(player.State.Position+seconds,0,Math.Max(0,player.State.Duration-.1));
        player.Command("seek",target.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute+exact"); playerOverlay?.Reveal();
    }
    private async Task GoLiveAsync()
    {
        if(playing is not { Kind:MediaKind.Live } channel) return;
        await PlayAsync(channel); behindLive=false; UpdatePlaybackControls();
    }
    private async Task UpdateStreamInfoAsync()
    {
        infoUpdated=DateTimeOffset.UtcNow; var player=surface?.Player; var itemId=playing?.Id; var generation=surface==null ? 0 : generations.GetValueOrDefault(surface); if(player==null) return;
        var details=await Task.Run(player.StreamDetails);
        if(!closed && ReferenceEquals(surface?.Player,player) && playing?.Id==itemId && surface!=null && generations.GetValueOrDefault(surface)==generation && streamInfo!=null) streamInfo.Text=details;
    }
    private async Task SubtitlesMenuAsync()
    {
        var player=surface?.Player; if(player==null || playing==null || dialogDepth>0) return;
        var trackSnapshot=await Task.Run(player.Tracks); if(!ReferenceEquals(surface?.Player,player)) return;
        var subtitles=trackSnapshot.Where(track=>track.Type=="sub").ToArray();
        var selected=!player.State.SubtitlesVisible || player.State.SubtitleId is "no" or "" ? "Off" : subtitles.FirstOrDefault(track=>track.Id==player.State.SubtitleId) is var track && track.Id!=null ? $"{track.Id}: {track.Name.Trim()}" : "Automatic";
        var choice=Choice("Subtitles and closed captions",new[]{"Off","Automatic"}.Concat(subtitles.Select(track=>$"{track.Id}: {track.Name.Trim()}")),selected);
        var content=Stack(choice,Text("Off disables subtitle and closed-caption tracks. Automatic follows your preferred subtitle language.",12));
        content.Children.Add(AsyncButton("Load subtitle file…",async ()=> {
            var path=await OpenFileAsync(".srt",".ass",".ssa",".vtt",".sub"); if(path==null || !ReferenceEquals(surface?.Player,player)) return;
            await player.AddSubtitleAsync(path,lifetime.Token); settings.Playback.SubtitlesEnabled=true; await store.SaveAsync(settings);
            var tracks=await Task.Run(player.Tracks); var selectedId=await Task.Run(()=>player.Get("sid")); foreach(var added in tracks.Where(t=>t.Type=="sub")) { var label=$"{added.Id}: {added.Name.Trim()}"; if(!choice.Items.Contains(label)) choice.Items.Add(label); if(selectedId==added.Id) choice.SelectedItem=label; }
            status.Text="Subtitle file loaded.";
        }));
        if(await DialogAsync("Subtitles & captions",content,"Apply")!=ContentDialogResult.Primary || !ReferenceEquals(surface?.Player,player)) return;
        var value=(string)choice.SelectedItem; var id=value=="Off" ? "no" : value=="Automatic" ? "auto" : value.Split(':')[0];
        await player.SetSubtitleAsync(id,lifetime.Token); settings.Playback.SubtitlesEnabled=id!="no"; await store.SaveAsync(settings); playerOverlay?.Reveal(); UpdatePlaybackControls();
    }
    private static string TimeCode(double seconds) { var time=TimeSpan.FromSeconds(Math.Max(0,seconds)); return time.TotalHours>=1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}"; }
    private async Task ToggleFavoriteAsync(MediaItem item)
    {
        var favorites=settings.Profile.Favorites; if(!favorites.Add(item.Id)) favorites.Remove(item.Id);
        if(!favorites.Contains(item.Id)) settings.Profile.FavoriteGuideOrder.RemoveAll(id=>id==item.Id);
        foreach(var row in liveRows.Where(r=>r.Item.Id==item.Id)) row.IsFavorite=favorites.Contains(item.Id);
        UpdatePlaybackControls(); if(filter=="Favorites") await FilterLiveAsync();
        status.Text=favorites.Contains(item.Id) ? "Added to favorites." : "Removed from favorites.";
        await store.SaveAsync(settings);
        if(currentTab=="Guide") refreshGuide?.Invoke();
    }
    private void PrepareChannelFavorite(ListViewBase sender,ContainerContentChangingEventArgs args)
    {
        if(args.InRecycleQueue) return;
        if(args.Phase==0) { args.RegisterUpdateCallback(1,PrepareChannelFavorite); return; }
        if(FindNamedButton(args.ItemContainer.ContentTemplateRoot,"FavoriteChannelButton") is { } button) { button.Style=(Style)Application.Current.Resources["NullTVButtonStyle"]; button.Click-=OnFavoriteClick; button.Click+=OnFavoriteClick; }
    }
    private void OnFavoriteClick(object sender,RoutedEventArgs e) { if(sender is Button { DataContext:ChannelRow row }) _=GuardAsync(()=>ToggleFavoriteAsync(row.Item)); }
    private static Button? FindNamedButton(DependencyObject? element,string name)
    {
        if(element is Button button && button.Name==name) return button; if(element==null) return null;
        for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);i++) { var found=FindNamedButton(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element,i),name); if(found!=null) return found; } return null;
    }
    private void UpdateEmptyPlayer()
    {
        if(emptyLive is not Grid host) return;
        if(currentTab=="Guide")
        {
            host.Visibility=playing?.Kind==MediaKind.Live && surface!=null && active.ContainsKey(surface) ? Visibility.Collapsed : Visibility.Visible;
            if(emptyPlayerMode=="guide") return; emptyPlayerMode="guide"; host.Children.Clear();
            var title=Text("Select a channel to preview",14); title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; title.TextAlignment=TextAlignment.Center;
            var detail=Text("Click a channel or its live program below",11); detail.Opacity=.6; detail.TextAlignment=TextAlignment.Center;
            var placeholder=Stack(new FontIcon { Glyph="\uE714",FontSize=26,Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#BD93F9")) },title,detail); placeholder.VerticalAlignment=VerticalAlignment.Center; placeholder.HorizontalAlignment=HorizontalAlignment.Center; host.Children.Add(placeholder); host.Visibility=playing?.Kind==MediaKind.Live && surface!=null && active.ContainsKey(surface) ? Visibility.Collapsed : Visibility.Visible; return;
        }
        host.Visibility=surface!=null && active.ContainsKey(surface) ? Visibility.Collapsed : Visibility.Visible;
        var mode=lastPlayed?.Id??(items.Any(i=>i.Kind==MediaKind.Live) ? "channels" : "source"); if(mode==emptyPlayerMode) return; emptyPlayerMode=mode; host.Children.Clear();
        host.Children.Add(lastPlayed is { } last ? EmptyState("\uE768","Ready when you are.",last.Name,"Play again",()=>PlayAsync(last)) : mode=="channels" ? EmptyState("\uE714","Choose what to watch.","Browse live channels or your movie library to start playing.","Browse channels",()=> { Navigate("Live TV"); return Task.CompletedTask; }) : EmptyState("\uE714","Your screen. Your television.","Add your own account or playlist to get started.","Add a source",()=>EditSourceAsync(null)));
    }
}

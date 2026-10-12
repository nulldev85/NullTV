using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NullTV.Core;
namespace NullTV.App;
public sealed partial class MainWindow
{
    private sealed class ChannelRow(MediaItem item) : ArtworkRow(item)
    {
        public string DisplayName=>$"{Item.Number:000}  {Item.Name}";
        private string nowNext="No programme information";
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
        if(playing.Kind==MediaKind.Live) StopPlayback(); else player.Command("cycle","pause");
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
        LabelButton(primaryTransport,running ? live ? "\uE71A" : state!.Paused ? "\uE768" : "\uE769" : "\uE768",running ? live ? "Stop live playback" : state!.Paused ? "Play" : "Pause" : "Play");
        primaryTransport.IsEnabled=item!=null;
        if(stopButton!=null) { stopButton.Visibility=live ? Visibility.Collapsed : Visibility.Visible; stopButton.IsEnabled=running; }
        if(previousButton!=null) previousButton.Visibility=item?.Kind==MediaKind.Live ? Visibility.Visible : Visibility.Collapsed;
        if(nextButton!=null) nextButton.Visibility=item?.Kind==MediaKind.Live ? Visibility.Visible : Visibility.Collapsed;
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
        var mode=lastPlayed?.Id??(items.Any(i=>i.Kind==MediaKind.Live) ? "channels" : "source"); if(mode==emptyPlayerMode) return; emptyPlayerMode=mode; host.Children.Clear();
        host.Children.Add(lastPlayed is { } last ? EmptyState("\uE768","Ready when you are.",last.Name,"Play again",()=>PlayAsync(last)) : mode=="channels" ? EmptyState("\uE714","Choose what to watch.","Pick a channel from the list, or find one with search.","Show channels",()=> { channelsHidden=false; UpdateLiveLayout(); return Task.CompletedTask; }) : EmptyState("\uE714","Your screen. Your television.","Add your own account or playlist to get started.","Add a source",()=>EditSourceAsync(null)));
    }
}

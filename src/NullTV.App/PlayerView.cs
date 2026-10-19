using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private Grid? playerModeBar;
    private Button? exitPlayerView,closePlayerView,closeNowPlaying;
    private void BuildPlayerModeBar()
    {
        // Outside the native video HWND: navigation remains reachable even if
        // playback has ended, controls are hidden, or a stream is reconnecting.
        playerModeBar=new Grid { Padding=new Thickness(12,4,12,4),Visibility=Visibility.Collapsed };
        playerModeBar.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        playerModeBar.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        exitPlayerView=Button("Back to player",ExitPlayerView); exitPlayerView.HorizontalAlignment=HorizontalAlignment.Left;
        closePlayerView=Button("Close stream",StopPlayback); closePlayerView.HorizontalAlignment=HorizontalAlignment.Right;
        foreach(var button in new[]{exitPlayerView,closePlayerView}) { button.MinHeight=32; button.Padding=new Thickness(10,5,10,5); button.FontSize=12; }
        ToolTipService.SetToolTip(exitPlayerView,"Return to the normal app view (Esc also exits the current viewing mode)");
        Place(playerModeBar,exitPlayerView); Place(playerModeBar,closePlayerView,0,1);
        Grid.SetColumnSpan(playerModeBar,2); Place(root,playerModeBar,1);
    }
    private void UpdatePlayerChrome()
    {
        var expanded=fullscreen || expandedVideo;
        LabelButton(navigationToggle,"\uE700",expanded || navigationHidden ? "Show navigation" : "Hide navigation");
        root.RowDefinitions[0].Height=new(fullscreen ? 0 : 58);
        root.ColumnDefinitions[0].Width=new(expanded || navigationHidden ? 0 : 184);
        if(sidebar!=null) sidebar.Visibility=expanded || navigationHidden ? Visibility.Collapsed : Visibility.Visible;
        status.Visibility=expanded || currentTab=="Guide" ? Visibility.Collapsed : Visibility.Visible;
        pageHost.Margin=expanded ? new Thickness(0) : currentTab=="Guide" ? GuideMargin : new Thickness(28,18,28,20);
        if(playerModeBar!=null) playerModeBar.Visibility=expandedVideo && !fullscreen ? Visibility.Visible : Visibility.Collapsed;
        if(titlebarChrome!=null) titlebarChrome.Visibility=fullscreen ? Visibility.Collapsed : Visibility.Visible;
        if(exitPlayerView!=null) exitPlayerView.Content=currentTab=="Guide" ? "‹  Back to guide" : "‹  Back to player";
        UpdateCloseActions();
    }
    private void UpdateCloseActions()
    {
        // Idle includes loading and failed feeds, which must still be closable.
        var canClose=playing!=null || playbackRequest!=null || active.Count>0;
        foreach(var button in new[]{stopButton,closePlayerView,closeNowPlaying}) if(button!=null) button.IsEnabled=canClose;
    }
    private void RefreshPlayerView()
    {
        if(currentTab=="Guide") UpdateGuideVideoLayout(); else UpdateLiveLayout();
        UpdatePlayerChrome();
        if(playing!=null) playerOverlay?.Reveal(); else playerOverlay?.Hide();
    }
    private void ExitPlayerView()
    {
        if(fullscreen) { fullscreen=false; AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default); }
        expandedVideo=false;
        RefreshPlayerView();
        if(playing!=null && surface!=null && active.ContainsKey(surface)) surface.Focus(FocusState.Programmatic);
        else if(currentTab=="Now Playing") backToBrowse?.Focus(FocusState.Programmatic);
    }
    private void ExitCurrentPlayerMode()
    {
        if(dialogDepth>0) return;
        if(fullscreen) ToggleFullscreen(); else if(expandedVideo) ToggleExpandedVideo();
    }
    private void OnEscape(object sender,KeyRoutedEventArgs e)
    {
        if(e.Key!=VirtualKey.Escape || dialogDepth>0 || !(fullscreen || expandedVideo)) return;
        ExitCurrentPlayerMode(); e.Handled=true;
    }
}

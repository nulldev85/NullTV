using System.Net;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;
using NullTV.App.Playback;
using Windows.System;
using Windows.System.Power;

namespace NullTV.App;
public sealed partial class MainWindow : Window
{
    private readonly HttpClient http=new(new HttpClientHandler { AutomaticDecompression=DecompressionMethods.All }) { Timeout=TimeSpan.FromSeconds(40) };
    private readonly SettingsStore store;
    private readonly SafeLog log;
    private Settings settings=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim refreshGate=new(1);
    private readonly Dictionary<string,Catalog> catalogs=[];
    private readonly Dictionary<string,List<Programme>> epgs=[];
    private List<MediaItem> items=[];
    private readonly Grid root=new() { Background=new SolidColorBrush(Color("#282A36")),RequestedTheme=ElementTheme.Dark };
    private readonly Grid pageHost=new();
    private readonly StackPanel navigation=new() { Orientation=Orientation.Vertical, Spacing=6 };
    private Border? sidebar;
    private readonly TextBlock status=new() { Text="Starting NullTV…", TextWrapping=TextWrapping.Wrap, FontSize=12, Opacity=.75 };
    private readonly TextBlock profileLabel=new();
    private Grid? livePage;
    private string currentTab="Live TV";
    private bool libraryNowPlaying;
    private bool IsPlayerVisible=>currentTab=="Live TV" || currentTab=="Movies & Series" && libraryNowPlaying;
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromSeconds(1) };
    private int ticks;
    private bool busy;
    private bool closed;
    private bool closingConfirmed;
    private ImageCache? imageCache;
    private DateTimeOffset lastInput=DateTimeOffset.UtcNow;
    private DateTimeOffset unlockedUntil;
    private int dialogDepth;
    private bool validationFailed;
    private int validationPlaybackLoads;
    public MainWindow()
    {
        Title="NullTV";
        var args=Environment.GetCommandLineArgs();
        var dataIndex=Array.IndexOf(args,"--data-dir");
        var data=dataIndex>=0 && dataIndex+1<args.Length ? args[dataIndex+1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NullTV");
        store=new(data); log=new(Path.Combine(data,"logs"));
        ExtendsContentIntoTitleBar=true;
        root.RowDefinitions.Add(new() { Height=new(58) }); root.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height=GridLength.Auto });
        root.ColumnDefinitions.Add(new() { Width=new(184) }); root.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var titlebar=new Grid { Padding=new Thickness(24,0,150,0) }; titlebar.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); titlebar.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var brand=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center };
        brand.Children.Add(new Border { Width=28,Height=28,CornerRadius=new CornerRadius(8),Background=new SolidColorBrush(Color("#BD93F9")),Child=new TextBlock { Text="N",FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.Bold,Foreground=new SolidColorBrush(Color("#21222C")),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center } });
        brand.Children.Add(new TextBlock { Text="NULLTV",FontSize=13,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,CharacterSpacing=180,VerticalAlignment=VerticalAlignment.Center }); titlebar.Children.Add(brand);
        profileLabel.Text="ARM64 · YOUR SCREEN, YOUR SOURCES"; profileLabel.FontSize=11; profileLabel.Opacity=.65; profileLabel.VerticalAlignment=VerticalAlignment.Center; Grid.SetColumn(profileLabel,1); titlebar.Children.Add(profileLabel);
        Grid.SetColumnSpan(titlebar,2); root.Children.Add(titlebar); SetTitleBar(titlebar);
        navigation.Margin=new Thickness(12,20,12,0);
        var label=Text("LIBRARY",10); label.CharacterSpacing=150; label.Opacity=.4; label.Margin=new Thickness(14,0,0,16); navigation.Children.Add(label);
        var names=new[] { "Live TV","Guide","Movies & Series","Media Servers","Settings" }; var glyphs=new[] { "\uE714","\uE8F2","\uE8B2","\uE968","\uE713" };
        for(int i=0;i<names.Length;i++) { var name=names[i]; var b=Button(name,()=>Navigate(name)); b.Tag=name; b.Content=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12,Children={ new FontIcon { Glyph=glyphs[i],FontSize=17 },new TextBlock { Text=name=="Movies & Series" ? "Movies & series" : name,FontSize=13,VerticalAlignment=VerticalAlignment.Center } } }; b.HorizontalAlignment=HorizontalAlignment.Stretch; b.HorizontalContentAlignment=HorizontalAlignment.Left; b.Padding=new Thickness(14,12,10,12); b.MinHeight=48; navigation.Children.Add(b); }
        var rail=new Grid(); rail.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); rail.RowDefinitions.Add(new() { Height=GridLength.Auto }); Place(rail,navigation);
        var railFooter=Stack(Text("YOUR SCREEN.\nYOUR SOURCES.",10)); railFooter.Opacity=.35; railFooter.Margin=new Thickness(26,16,16,24); Place(rail,railFooter,1);
        sidebar=new Border { BorderThickness=new Thickness(0,0,1,0),BorderBrush=new SolidColorBrush(Color("#44475A")),Child=rail }; Place(root,sidebar,1);
        pageHost.Margin=new Thickness(28,18,28,20); Place(root,pageHost,1,1);
        status.Margin=new Thickness(28,0,28,14); status.FontSize=11; status.Opacity=.45; Place(root,status,2,1);
        root.KeyDown+=OnKey;
        root.PointerMoved+=(_,_)=>lastInput=DateTimeOffset.UtcNow;
        root.SizeChanged+=(_,_)=>UpdateLiveLayout();
        Content=root;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440,960));
        AppWindow.Changed+=(_,_)=> { foreach(var tile in surfaces) tile.Hide(!IsPlayerVisible || !AppWindow.IsVisible || dialogDepth>0 || !active.ContainsKey(tile)); };
        Closed+=(_,_)=> { closed=true; lifetime.Cancel(); timer.Stop(); transportTimer.Stop(); filterPending?.Cancel(); playerOverlay?.Dispose(); foreach(var s in surfaces) s.Dispose(); pipWindow?.Close(); _=store.SaveAsync(settings); http.Dispose(); };
        AppWindow.Closing+=async (_,e)=> { if(closingConfirmed) { playerOverlay?.Dispose(); return; } e.Cancel=true; timer.Stop(); await GuardAsync(SaveProgressAsync); closingConfirmed=true; Close(); };
        root.Loaded+=async (_,_)=>await GuardAsync(InitializeAsync);
        timer.Tick+=async (_,_)=>await TickAsync();
    }
    private async Task InitializeAsync()
    {
        try { settings=await store.LoadAsync(); } catch(Exception e) when(e is System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidDataException or IOException) { status.Text="Settings could not be unlocked. Original file is preserved; use Restore Backup in Settings."; await log.WriteAsync("SETTINGS_LOAD_FAILED",e); }
        var display=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        var dpi=root.XamlRoot.RasterizationScale;
        var width=(int)Math.Min(1280*dpi,display.Width*.94); var height=(int)Math.Min(820*dpi,display.Height*.94);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(display.X+(display.Width-width)/2,display.Y+(display.Height-height)/2,width,height));
        imageCache=new(http,Path.Combine(store.DirectoryPath,"images"));
        if(settings.Appearance.Accent is "#D6C3A5" or "#6EE7C4" or "#D3D6DC") settings.Appearance.Accent="#BD93F9";
        ApplyAppearance(); BuildLive(); Navigate(settings.StartupTab);
        foreach(var s in settings.Profile.Sources) { var cached=await store.LoadCacheAsync(s.Id); if(cached!=null) catalogs[s.Id]=cached; }
        RebuildItems(); await RefreshAllAsync(false); timer.Start();
        var args=Environment.GetCommandLineArgs(); var index=Array.IndexOf(args,"--smoke-video"); var playlistIndex=Array.IndexOf(args,"--smoke-playlist");
        if(playlistIndex>=0 && playlistIndex+1<args.Length && Array.IndexOf(args,"--data-dir")>=0) { var s=new Source { Name="Local validation",Kind=SourceKind.PlaylistFile,Address=args[playlistIndex+1] }; settings.Profile.Sources.Clear(); settings.Profile.Sources.Add(s); await RefreshSourceAsync(s); Navigate("Live TV"); var channel=items.FirstOrDefault(i=>i.Kind==MediaKind.Live); if(channel!=null) await PlayAsync(channel); }
        else if(index>=0 && index+1<args.Length) { Navigate("Live TV"); await PlayAsync(new("smoke","smoke","Local generated validation clip","Validation",MediaKind.Movie,args[index+1])); }
        else if(settings.ResumeOnLaunch) { var last=settings.Profile.History.OrderByDescending(x=>x.Value.Updated).FirstOrDefault(); var media=items.FirstOrDefault(i=>i.Id==last.Key && i.Kind!=MediaKind.Series); if(media!=null) await PlayAsync(media); }
        var tabIndex=Array.IndexOf(args,"--smoke-tab"); if(tabIndex>=0 && tabIndex+1<args.Length) Navigate(args[tabIndex+1]);
        var secondsIndex=Array.IndexOf(args,"--smoke-seconds");
        if(secondsIndex>=0 && secondsIndex+1<args.Length && int.TryParse(args[secondsIndex+1],out var seconds) && seconds is >=2 and <=60 && Array.IndexOf(args,"--data-dir")>=0)
        {
            var end=new DispatcherTimer { Interval=TimeSpan.FromSeconds(seconds) }; end.Tick+=async (_,_)=> { end.Stop(); Directory.CreateDirectory(store.DirectoryPath); await File.WriteAllTextAsync(Path.Combine(store.DirectoryPath,"ui-validation.json"),System.Text.Json.JsonSerializer.Serialize(new { View=currentTab,Passed=!validationFailed,Width=root.ActualWidth,Height=root.ActualHeight,NativePlayer=surface?.Player!=null,PlaybackLoads=validationPlaybackLoads,DesignResources=Application.Current.Resources.ContainsKey("NullTVButtonStyle") && File.Exists(Path.Combine(AppContext.BaseDirectory,"DesignResources.xbf")),Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() })); closingConfirmed=true; await store.SaveAsync(settings); Close(); }; end.Start();
        }
    }
    private void ApplyAppearance()
    {
        root.RequestedTheme=settings.Appearance.Theme=="Light" ? ElementTheme.Light : ElementTheme.Dark;
        root.Background=new SolidColorBrush(settings.Appearance.Theme=="Light" ? Color("#F4F6F7") : Color("#282A36"));
        if(settings.Appearance.Mica && settings.Appearance.Theme=="Light") { root.Background=null; SystemBackdrop=new MicaBackdrop(); } else SystemBackdrop=null;
        root.Resources["SystemAccentColor"]=Color(settings.Appearance.Accent);
        root.Resources["ListViewItemBackgroundSelected"]=new SolidColorBrush(Color(RaisedHex));
        root.Resources["ListViewItemForegroundSelected"]=new SolidColorBrush(Color(settings.Appearance.Accent));
        root.Resources["ControlMinHeight"]=44d;
        root.Resources["SliderTrackValueFill"]=new SolidColorBrush(Color(settings.Appearance.Accent));
        root.Resources["TextFillColorPrimaryBrush"]=FieldText();
        root.Resources["TextFillColorSecondaryBrush"]=new SolidColorBrush(Color(settings.Appearance.Theme=="Light" ? "#646974" : "#F8F8F2")) { Opacity=.65 };
        foreach(var key in new[]{"MenuFlyoutPresenterBackground","ToolTipBackground","ContentDialogBackground"}) root.Resources[key]=FieldFill();
        foreach(var key in new[]{"MenuFlyoutItemBackgroundPointerOver","MenuFlyoutItemBackgroundPressed"}) root.Resources[key]=new SolidColorBrush(Colors.Transparent);
        foreach(var key in new[]{"FocusVisualPrimaryBrush","TextControlBorderBrushFocused"}) root.Resources[key]=new SolidColorBrush(Color("#815CD6"));
        var accentBrush=new SolidColorBrush(Color(settings.Appearance.Accent));
        foreach(var key in new[] { "SystemAccentColorLight1","SystemAccentColorLight2","SystemAccentColorLight3","SystemAccentColorDark1","SystemAccentColorDark2","SystemAccentColorDark3" }) root.Resources[key]=Color(settings.Appearance.Accent);
        foreach(var key in new[] { "AccentFillColorDefaultBrush","AccentFillColorSecondaryBrush","AccentFillColorTertiaryBrush","SliderThumbBackground","ToggleSwitchFillOn","ToggleSwitchFillOnPointerOver","CheckBoxCheckBackgroundFillChecked","TextControlBorderBrushFocused" }) root.Resources[key]=accentBrush;
        AppWindow.TitleBar.ButtonBackgroundColor=Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor=Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor=Color(settings.Appearance.Theme=="Light" ? "#222222" : "#F8F8F2");
        profileLabel.Text=$"◉   {settings.Profile.Name}";
    }
    private static Windows.UI.Color Color(string hex) { try { hex=hex.TrimStart('#'); if(hex.Length==6) return Windows.UI.Color.FromArgb(255,Convert.ToByte(hex[..2],16),Convert.ToByte(hex[2..4],16),Convert.ToByte(hex[4..6],16)); if(hex.Length==8) return Windows.UI.Color.FromArgb(Convert.ToByte(hex[..2],16),Convert.ToByte(hex[2..4],16),Convert.ToByte(hex[4..6],16),Convert.ToByte(hex[6..8],16)); } catch(FormatException) { } return Windows.UI.Color.FromArgb(255,211,214,220); }
    private Button Button(string label,Action action) { var b=new Button { Style=(Style)Application.Current.Resources["NullTVButtonStyle"],Content=label,MinHeight=44,CornerRadius=new CornerRadius(8),Padding=new Thickness(16,8,16,8) }; b.Click+=async (_,_)=> { lastInput=DateTimeOffset.UtcNow; await GuardAsync(()=> { action(); return Task.CompletedTask; }); }; return b; }
    private Button AsyncButton(string label,Func<Task> action) => Button(label,()=>_=GuardAsync(action));
    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); } catch(OperationCanceledException) { if(!closed) status.Text="Operation cancelled or timed out."; } catch(Exception e) when(e is not OutOfMemoryException) { validationFailed=true; await log.WriteAsync("ACTION_FAILED",e); if(!closed) status.Text=Friendly(e); }
    }
    public void ReportUnhandled(Exception e) { validationFailed=true; _=log.WriteAsync("UI_FAILED",e); status.Text="This operation could not complete. Try another view or export Diagnostics. Your settings are preserved."; }
    private void ValidationStep(string code) { if(!Environment.GetCommandLineArgs().Contains("--smoke-tab")) return; Directory.CreateDirectory(store.DirectoryPath); File.AppendAllText(Path.Combine(store.DirectoryPath,"validation.log"),code+"\n"); }
    private static string Friendly(Exception e) => e is InvalidDataException ? e.Message : e is HttpRequestException ? "The server could not be reached. Check the address and connection; cached channels remain available." : e is IOException ? "The file could not be read or written. Check its location and permissions." : e is System.Security.Cryptography.CryptographicException ? "The password is incorrect, or this encrypted file is damaged." : "The operation failed. Export Diagnostics for its error type; credentials are excluded.";
    private TextBlock Text(string value,int size=14) => new() { Text=value,FontSize=size*settings.Appearance.FontScale,TextWrapping=TextWrapping.Wrap };
    private Border Card(UIElement content,Thickness? padding=null) => new() { Background=new SolidColorBrush(Color(SurfaceHex)),BorderBrush=new SolidColorBrush(Color(BorderHex)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Padding=padding??new Thickness(24),Child=content };
    private Button Primary(string label,Func<Task> action) { var b=AsyncButton(label,action); b.Background=new SolidColorBrush(Color(settings.Appearance.Accent)); b.Foreground=new SolidColorBrush(Color("#0A0A0A")); b.BorderBrush=b.Background; b.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; return b; }
    private Button IconButton(string glyph,string label,Action action) { var b=Button("",action); b.Content=new FontIcon { Glyph=glyph,FontSize=17 }; b.Width=44; b.Padding=new Thickness(0); b.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent); b.BorderBrush=b.Background; ToolTipService.SetToolTip(b,label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,label); return b; }
    private Grid Heading(string title,string subtitle,UIElement? action=null) { var g=new Grid { Margin=new Thickness(0,0,0,24),ColumnSpacing=24 }; g.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); g.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); var h=Text(title,30); h.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; var s=Text(subtitle,12); s.Opacity=.5; var p=Stack(h,s); p.Spacing=6; Place(g,p); if(action!=null) { action.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center); Place(g,action,0,1); } return g; }
    private UIElement EmptyState(string glyph,string title,string description,string action,Func<Task> handler)
    {
        var icon=new FontIcon { Glyph=glyph,FontSize=30,Foreground=new SolidColorBrush(Color(settings.Appearance.Accent)) };
        var plate=new Border { Width=72,Height=72,CornerRadius=new CornerRadius(22),Background=new SolidColorBrush(Color(RaisedHex)),Child=icon,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10) };
        var heading=Text(title,27); heading.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; heading.TextAlignment=TextAlignment.Center;
        var detail=Text(description,13); detail.Opacity=.5; detail.TextAlignment=TextAlignment.Center; detail.MaxWidth=430;
        var button=Primary(action,handler); button.HorizontalAlignment=HorizontalAlignment.Center; button.Margin=new Thickness(0,12,0,0);
        var p=Stack(plate,heading,detail,button); p.Spacing=12; p.HorizontalAlignment=HorizontalAlignment.Center; p.VerticalAlignment=VerticalAlignment.Center; p.Margin=new Thickness(32); return p;
    }
    private StackPanel Stack(params UIElement[] children) { var p=new StackPanel { Spacing=12 }; foreach(var c in children) p.Children.Add(c); return p; }
    private static void Place(Grid g,UIElement c,int row=0,int col=0) { Grid.SetRow((FrameworkElement)c,row); Grid.SetColumn((FrameworkElement)c,col); g.Children.Add(c); }
    private static TextBox Field(string label,string value="") { var field=new TextBox { Header=label,Text=value,MinHeight=68 }; PolishField(field); return field; }
    private static ComboBox Choice(string label,IEnumerable<string> values,string selected) { var c=new ComboBox { Header=string.IsNullOrWhiteSpace(label) ? null : label,MinHeight=string.IsNullOrWhiteSpace(label) ? 46 : 68,HorizontalAlignment=HorizontalAlignment.Stretch }; PolishField(c); foreach(var v in values) c.Items.Add(v); c.SelectedItem=selected; if(c.SelectedIndex<0 && c.Items.Count>0) c.SelectedIndex=0; return c; }
    private static ToggleSwitch Toggle(string label,bool value) => new() { Header=label,IsOn=value,MinHeight=44 };
    private ScrollViewer Scroll(UIElement element) => new() { Content=element,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
    private void Navigate(string tab,bool nowPlaying=false)
    {
        currentTab=tab; libraryNowPlaying=tab=="Movies & Series" && nowPlaying; playerOverlay?.Hide(); foreach(var s in surfaces) s.Hide(!IsPlayerVisible);
        foreach(var b in navigation.Children.OfType<Button>()) { var selected=(string)b.Tag==tab; b.Opacity=selected ? 1 : .65; b.Background=new SolidColorBrush(selected ? Color(RaisedHex) : Colors.Transparent); b.BorderBrush=new SolidColorBrush(Colors.Transparent); b.Foreground=new SolidColorBrush(selected ? Color(settings.Appearance.Accent) : settings.Appearance.Theme=="Light" ? Colors.Black : Color("#F8F8F2")); }
        foreach(var child in pageHost.Children) child.Visibility=Visibility.Collapsed;
        if(IsPlayerVisible) { if(livePage!=null) livePage.Visibility=Visibility.Visible; foreach(var s in surfaces) s.Hide(!active.ContainsKey(s)); UpdateLiveLayout(); return; }
        // Other pages are rebuilt to reflect profile and settings changes.
        foreach(var child in pageHost.Children.Where(c=>c!=livePage).ToArray()) pageHost.Children.Remove(child);
        pageHost.Children.Add(tab switch { "Guide"=>BuildGuide(), "Movies & Series"=>BuildLibrary(), "Media Servers"=>BuildServers(), _=>BuildSettings() });
    }
    private void RebuildItems() { int number=0; items=settings.Profile.Sources.SelectMany(s=>catalogs.GetValueOrDefault(s.Id)?.Items??[]).Select(i=>i.Kind==MediaKind.Live ? i with { Number=++number } : i).ToList(); foreach(var catalog in catalogs.Values) foreach(var (id,remote) in catalog.RemoteHistory) { if(!settings.Profile.History.TryGetValue(id,out var local) || remote.Updated>local.Updated) settings.Profile.History[id]=remote; } UpdateLive(); if(currentTab=="Movies & Series") refreshLibrary?.Invoke(); if(currentTab=="Guide") refreshGuide?.Invoke(); }
    private async Task RefreshAllAsync(bool force)
    {
        if(!await refreshGate.WaitAsync(0)) return;
        try
        {
            busy=true;
            foreach(var s in settings.Profile.Sources.Where(s=>s.Kind is SourceKind.Xtream or SourceKind.PlaylistFile or SourceKind.PlaylistUrl))
            {
                if(!force && catalogs.TryGetValue(s.Id,out var existing) && DateTimeOffset.UtcNow-s.LastRefresh<TimeSpan.FromHours(s.RefreshHours))
                {
                    if(!epgs.ContainsKey(s.Id)) await GuardAsync(async ()=> { var cached=await store.LoadGuideAsync(s.Id); var guide=cached?.Any(p=>p.End>DateTimeOffset.UtcNow) == true ? cached : await Task.Run(()=>new SourceService(http,store).LoadEpgAsync(s,existing,lifetime.Token)); epgs[s.Id]=guide; await IndexGuidesAsync(); UpdateNowNext(); if(!ReferenceEquals(guide,cached)) await store.SaveGuideAsync(s.Id,guide); });
                    continue;
                }
                await GuardAsync(async ()=> { status.Text=$"Refreshing {s.Name}…"; var service=new SourceService(http,store); var catalog=await Task.Run(()=>service.RefreshAsync(s,lifetime.Token)); catalogs[s.Id]=catalog; RebuildItems(); await store.SaveAsync(settings); try { epgs[s.Id]=await Task.Run(()=>service.LoadEpgAsync(s,catalog,lifetime.Token)); await IndexGuidesAsync(); UpdateNowNext(); await store.SaveGuideAsync(s.Id,epgs[s.Id]); } catch(Exception e) { status.Text="Channels loaded. EPG unavailable; set an XMLTV override in Sources."; await log.WriteAsync("EPG_FAILED",e); } });
            }
            UpdateLive(); if(!settings.Profile.Sources.Any()) status.Text="Welcome. Add your provider or playlist in Settings → Sources to get started.";
            else status.Text=$"{items.Count:N0} items · {settings.Profile.Sources.Count} sources · {epgs.Values.Sum(x=>x.Count):N0} guide programmes";
        }
        finally { busy=false; refreshGate.Release(); }
    }
    private async Task TickAsync()
    {
        ticks++; PollController();
        if(surface?.Player!=null && playing!=null && ticks%5==0) await GuardAsync(SaveProgressAsync);
        if(ticks%15==0 && playing!=null) _=GuardAsync(SyncServerProgressAsync);
        if(ticks%60==0)
        {
            if(PowerManager.EnergySaverStatus!=EnergySaverStatus.On && !busy) _=GuardAsync(()=>RefreshAllAsync(false));
            var due=settings.Profile.Reminders.Where(r=>r.Start<=DateTimeOffset.UtcNow && r.Start>DateTimeOffset.UtcNow.AddMinutes(-2)).ToList();
            foreach(var r in due) { settings.Profile.Reminders.Remove(r); status.Text=$"Reminder: {r.Title} starts now."; }
            if(due.Count>0) await store.SaveAsync(settings);
            UpdateNowNext();
        }
    }
    private async Task<ContentDialogResult> DialogAsync(string title,UIElement content,string primary="Save")
    {
        dialogDepth++; playerOverlay?.Hide(); foreach(var s in surfaces) s.Hide(true);
        try
        {
            var primaryStyle=new Style(typeof(Button)) { BasedOn=(Style)Application.Current.Resources["NullTVButtonStyle"] }; primaryStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color(settings.Appearance.Accent)))); primaryStyle.Setters.Add(new Setter(Control.ForegroundProperty,new SolidColorBrush(Color("#0A0A0A")))); primaryStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,44d));
            var dialog=new ContentDialog { Title=title,Content=content,PrimaryButtonText=primary,PrimaryButtonStyle=primaryStyle,CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary,XamlRoot=root.XamlRoot,RequestedTheme=root.RequestedTheme };
            foreach(var key in new[] { "AccentButtonBackground","AccentButtonBackgroundPointerOver","AccentButtonBackgroundPressed" }) dialog.Resources[key]=new SolidColorBrush(Color(settings.Appearance.Accent));
            foreach(var key in new[] { "AccentButtonForeground","AccentButtonForegroundPointerOver","AccentButtonForegroundPressed" }) dialog.Resources[key]=new SolidColorBrush(Color("#0A0A0A"));
            dialog.Resources["ContentDialogCornerRadius"]=new CornerRadius(16);
            dialog.Resources["ContentDialogButtonHeight"]=44d;
            dialog.Resources["ContentDialogButtonMinHeight"]=44d;
            dialog.Resources["ContentDialogBackground"]=FieldFill();
            dialog.Resources["ContentDialogTopOverlay"]=FieldFill();
            dialog.CloseButtonStyle=(Style)Application.Current.Resources["NullTVButtonStyle"];
            dialog.Opened+=(_,_)=>
            {
                foreach(var name in new[]{"PrimaryButton","SecondaryButton","CloseButton"})
                {
                    if(FindNamedButton(dialog,name) is not { } button) continue;
                    button.Template=(ControlTemplate)Application.Current.Resources["NullTVButtonTemplate"];
                    button.Height=button.MinHeight=44; button.CornerRadius=new CornerRadius(10); button.Padding=new Thickness(16,10,16,10);
                    button.Background=name=="PrimaryButton" ? new SolidColorBrush(Color(settings.Appearance.Accent)) : new SolidColorBrush(Color(RaisedHex));
                    button.Foreground=name=="PrimaryButton" ? new SolidColorBrush(Color("#0A0A0A")) : FieldText();
                }
            };
            return await dialog.ShowAsync();
        }
        finally { dialogDepth--; if(IsPlayerVisible && dialogDepth==0) foreach(var s in surfaces) s.Hide(!active.ContainsKey(s)); }
    }
    private async Task<bool> UnlockAsync(MediaItem item)
    {
        var p=settings.Profile;
        if(!p.LockedItems.Contains(item.Id) && !p.LockedCategories.Contains(item.Category) || DateTimeOffset.UtcNow<unlockedUntil) return true;
        if(p.PinHash=="") { status.Text="Set a parental PIN in Settings before unlocking this item."; return false; }
        var pin=new PasswordBox { Style=(Style)Application.Current.Resources["NullTVPasswordBoxStyle"],Foreground=FieldText(),Background=FieldFill(),BorderBrush=FieldStroke(),CornerRadius=new CornerRadius(10), Header="Parental PIN",MinHeight=44 };
        if(await DialogAsync("Unlock playback",pin,"Unlock")!=ContentDialogResult.Primary) return false;
        var password=pin.Password; if(!await Task.Run(()=>SecretStore.CheckPin(password,p))) { status.Text="Incorrect parental PIN."; return false; }
        unlockedUntil=DateTimeOffset.UtcNow.AddMinutes(settings.LockTimeoutMinutes); return true;
    }
}

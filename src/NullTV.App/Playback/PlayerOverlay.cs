using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;

namespace NullTV.App.Playback;

// Two native child XAML islands share the video's HWND parent. Unlike a XAML
// sibling in the main visual tree, these can appear above mpv's native surface.
public sealed class PlayerOverlay : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window,ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
    private readonly Window owner;
    private readonly IntPtr hwnd;
    private readonly WindowsXamlManager manager;
    private readonly DesktopWindowXamlSource controls,details;
    private readonly Func<(VideoSurface? Video,bool Available)> current;
    private readonly Action focusBack;
    private readonly DispatcherTimer hoverTimer=new() { Interval=TimeSpan.FromMilliseconds(120) };
    private RectInt32 controlBounds,detailBounds;
    private bool disposed,keyboardInteraction;
    private DateTimeOffset revealUntil;
    public bool IsVisible { get; private set; }
    private bool? compactLayout;
    public event Action<double,bool>? LayoutChanged;
    public event Action<bool>? VisibilityChanged;
    public PlayerOverlay(Window owner,FrameworkElement controlContent,FrameworkElement detailContent,Func<(VideoSurface?,bool)> current,Action focusBack)
    {
        this.owner=owner; this.current=current; this.focusBack=focusBack;
        hwnd=WinRT.Interop.WindowNative.GetWindowHandle(owner);
        manager=WindowsXamlManager.InitializeForCurrentThread();
        controls=new(); details=new();
        controls.Initialize(owner.AppWindow.Id); details.Initialize(owner.AppWindow.Id);
        controls.Content=controlContent; details.Content=detailContent;
        controls.SiteBridge.Hide(); details.SiteBridge.Hide();
        controls.TakeFocusRequested+=(_,_)=> { keyboardInteraction=false; focusBack(); };
        controlContent.KeyDown+=(_,_)=>keyboardInteraction=true;
        controlContent.PointerPressed+=(_,_)=>keyboardInteraction=false;
        hoverTimer.Tick+=(_,_)=>Update(); hoverTimer.Start();
    }
    public void Reveal() { revealUntil=DateTimeOffset.UtcNow.AddSeconds(3); Update(); }
    public void FocusControls()
    {
        keyboardInteraction=true; Reveal();
        if(IsVisible) controls.NavigateFocus(new XamlSourceFocusNavigationRequest(XamlSourceFocusNavigationReason.First));
    }
    public void Hide()
    {
        revealUntil=default; keyboardInteraction=false;
        if(controls.HasFocus) focusBack();
        controls.SiteBridge.Hide(); details.SiteBridge.Hide(); var changed=IsVisible; IsVisible=false; if(changed) VisibilityChanged?.Invoke(false);
    }
    public void Update()
    {
        if(disposed) return;
        var (video,available)=current(); var bounds=video?.ClientBounds();
        if(!available || bounds==null || !owner.AppWindow.IsVisible || IsIconic(hwnd)) { Hide(); return; }
        // XAML may deliver Unloaded from the old parent after a same-frame
        // reparent. The current, laid-out host is authoritative for HWND visibility.
        video!.Hide(false);
        var rect=bounds.Value; var scale=(owner.Content as FrameworkElement)?.XamlRoot?.RasterizationScale??1;
        if(rect.Width<220*scale || rect.Height<160*scale) { Hide(); return; }
        GetCursorPos(out var screenPoint); var hit=WindowFromPoint(screenPoint); var point=screenPoint; ScreenToClient(hwnd,ref point);
        var over=GetAncestor(hit,2)==hwnd && point.X>=rect.X && point.X<rect.X+rect.Width && point.Y>=rect.Y && point.Y<rect.Y+rect.Height;
        var foreground=GetAncestor(GetForegroundWindow(),2)==hwnd;
        var show=over || foreground && (DateTimeOffset.UtcNow<revealUntil || keyboardInteraction && controls.HasFocus);
        if(!show) { if(IsVisible) Hide(); return; }
        var compact=rect.Height/scale<340 || rect.Width/scale<500;
        var inset=(int)Math.Round(12*scale); var height=(int)Math.Round((compact ? 104 : 116)*scale);
        var nextControls=new RectInt32(rect.X+inset,rect.Y+rect.Height-height-inset,rect.Width-inset*2,height);
        var infoWidth=(int)Math.Round(Math.Min(compact ? 210 : 290,rect.Width/scale-24)*scale); var infoHeight=(int)Math.Round((compact ? 58 : 180)*scale);
        var nextDetails=new RectInt32(rect.X+rect.Width-infoWidth-inset,rect.Y+inset,infoWidth,Math.Min(infoHeight,rect.Height-height-inset*3));
        var moved=nextControls!=controlBounds || nextDetails!=detailBounds;
        if(nextControls!=controlBounds || compactLayout!=compact) { controlBounds=nextControls; compactLayout=compact; controls.SiteBridge.MoveAndResize(controlBounds); Round(controls,controlBounds,scale); LayoutChanged?.Invoke(controlBounds.Width/scale,compact); }
        if(nextDetails!=detailBounds) { detailBounds=nextDetails; details.SiteBridge.MoveAndResize(detailBounds); Round(details,detailBounds,scale); }
        // A late native video resize can raise its child after XAML layout.
        // Reassert the overlay order while visible so controls never sit behind video.
        controls.SiteBridge.MoveInZOrderAtTop(); details.SiteBridge.MoveInZOrderAtTop();
        if(!IsVisible) { controls.SiteBridge.Show(); details.SiteBridge.Show(); IsVisible=true; VisibilityChanged?.Invoke(true); }
    }
    private static void Round(DesktopWindowXamlSource source,RectInt32 bounds,double scale)
    {
        var radius=(int)Math.Round(28*scale); var region=CreateRoundRectRgn(0,0,bounds.Width+1,bounds.Height+1,radius,radius);
        if(region!=IntPtr.Zero && SetWindowRgn(Win32Interop.GetWindowFromWindowId(source.SiteBridge.WindowId),region,true)==0) DeleteObject(region);
    }
    public void Dispose()
    {
        if(disposed) return; hoverTimer.Stop(); Hide(); disposed=true;
        controls.Dispose(); details.Dispose(); manager.Dispose();
    }
}

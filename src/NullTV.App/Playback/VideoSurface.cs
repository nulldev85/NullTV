using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;
using Windows.Foundation;

namespace NullTV.App.Playback;
public sealed class VideoSurface : UserControl, IDisposable
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int ex,string cls,string text,int style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd,int cmd);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, Subclass callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
    private delegate IntPtr Subclass(IntPtr hwnd,uint msg,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
    private readonly Window window;
    private readonly PlaybackSettings settings;
    private IntPtr child;
    private Subclass? callback;
    private Point pointerStart;
    private bool hidden;
    public MpvPlayer? Player { get; private set; }
    public event Action? Activity;
    public event Action<int>? Swipe;
    public VideoSurface(Window window, PlaybackSettings settings)
    {
        this.window=window; this.settings=settings; Background=new SolidColorBrush(Microsoft.UI.Colors.Black); MinHeight=120;
        Loaded+=(_,_)=>Initialize(); LayoutUpdated+=(_,_)=>Position(); Unloaded+=(_,_)=>Hide(true);
    }
    public void Initialize()
    {
        if(child!=IntPtr.Zero) { Hide(false); return; }
        var parent=WinRT.Interop.WindowNative.GetWindowHandle(window);
        child=CreateWindowEx(0,"STATIC","",0x40000000|0x10000000|0x04000000|0x02000000,0,0,1,1,parent,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(child==IntPtr.Zero) throw new InvalidOperationException("Could not create the video surface.");
        callback=(h,m,w,l,id,d)=> { if(m is 0x200 or 0x201 or 0x245) Activity?.Invoke(); if(m==0x246) pointerStart=new((short)((long)l&65535),(short)(((long)l>>16)&65535)); if(m==0x247) { var y=(short)(((long)l>>16)&65535); if(Math.Abs(y-pointerStart.Y)>80) Swipe?.Invoke(y>pointerStart.Y ? -1 : 1); } if(m==0x202) SetFocus(parent); return DefSubclassProc(h,m,w,l); };
        SetWindowSubclass(child,callback,1,0); Player=new(child,settings); hidden=false; Position();
    }
    private void Position()
    {
        if(child==IntPtr.Zero || XamlRoot==null || ActualWidth<1 || ActualHeight<1 || hidden) return;
        var root=window.Content as UIElement; if(root==null) return;
        try { var p=TransformToVisual(root).TransformPoint(new Point(0,0)); var scale=XamlRoot.RasterizationScale; SetWindowPos(child,IntPtr.Zero,(int)(p.X*scale),(int)(p.Y*scale),(int)(ActualWidth*scale),(int)(ActualHeight*scale),0x0010); } catch(ArgumentException) { }
    }
    public void Hide(bool value) { hidden=value; if(child!=IntPtr.Zero) ShowWindow(child,value ? 0 : 5); if(!value) Position(); }
    public void Dispose() { Player?.Dispose(); Player=null; if(child!=IntPtr.Zero) { DestroyWindow(child); child=IntPtr.Zero; } GC.KeepAlive(callback); }
}

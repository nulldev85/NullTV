using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.System;

namespace NullTV.App.Playback;

// A click on native video focuses the owner HWND, outside XAML key routing.
// Listen on that UI-thread window as well as the ordinary XAML KeyDown path.
internal sealed class PlayerKeyboardRouter : IDisposable
{
    private delegate IntPtr Subclass(IntPtr window,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr window,Subclass callback,UIntPtr id,UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window,Subclass callback,UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window,uint message,IntPtr w,IntPtr l);
    private readonly Subclass callback;
    private IntPtr window;
    private static readonly UIntPtr Id=new(0x4E5456);
    public PlayerKeyboardRouter(Window owner,Func<VirtualKey,bool> pressed,Action<Exception> failed)
    {
        window=WinRT.Interop.WindowNative.GetWindowHandle(owner);
        callback=(target,message,w,l,id,data)=> {
            try { if(message==0x100 && pressed((VirtualKey)(int)w.ToInt64())) return IntPtr.Zero; }
            catch(Exception error) when(error is not (OutOfMemoryException or AccessViolationException)) { failed(error); return IntPtr.Zero; }
            if(message==0x82) { RemoveWindowSubclass(target,callback!,Id); window=IntPtr.Zero; }
            return DefSubclassProc(target,message,w,l);
        };
        if(!SetWindowSubclass(window,callback,Id,0)) window=IntPtr.Zero;
    }
    public void Dispose()
    {
        if(window!=IntPtr.Zero) { RemoveWindowSubclass(window,callback,Id); window=IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
}

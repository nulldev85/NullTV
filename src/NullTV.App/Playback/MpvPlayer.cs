using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NullTV.Core;

namespace NullTV.App.Playback;
public sealed class MpvPlayer : IDisposable
{
    private const string Dll="libmpv-2.dll";
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_create();
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_set_option_string(IntPtr ctx,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,[MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_set_property_string(IntPtr ctx,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,[MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_get_property_string(IntPtr ctx,[MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern void mpv_free(IntPtr data);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_command(IntPtr ctx,IntPtr args);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_wait_event(IntPtr ctx,double timeout);
    [StructLayout(LayoutKind.Sequential)] private struct Event { public int Id,Error; public ulong Reply; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct End { public int Reason,Error; public long Entry; public long Insert; public int Count; }
    private readonly object gate=new();
    private IntPtr handle;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Task events;
    public event Action<bool>? Ended;
    public event Action? Loaded;
    public MpvPlayer(IntPtr hwnd, PlaybackSettings settings)
    {
        handle=mpv_create(); if(handle==IntPtr.Zero) throw new InvalidOperationException("libmpv could not create a player.");
        try
        {
            Option("config","no"); Option("load-scripts","no"); Option("terminal","no"); Option("msg-level","all=no");
            Option("input-default-bindings","no"); Option("input-vo-keyboard","no"); Option("input-cursor","no"); Option("osc","no");
            Option("wid",hwnd.ToInt64().ToString()); Option("vo","gpu-next"); Option("gpu-api","d3d11"); Option("gpu-context","d3d11");
            Option("hwdec",settings.HardwareDecode ? "d3d11va" : "no"); Option("idle","yes"); Option("keep-open","yes");
            Option("cache","yes"); Option("demuxer-max-bytes",$"{settings.BufferMiB}MiB"); Option("demuxer-readahead-secs",settings.LowLatency ? "1" : settings.CacheSeconds.ToString());
            Option("cache-pause",settings.LowLatency ? "no" : "yes"); Option("network-timeout","15");
            Option("volume",settings.Volume.ToString()); Option("alang",settings.AudioLanguage); Option("slang",settings.SubtitleLanguage); Option("audio-device",settings.AudioDevice);
            Option("audio-spdif",settings.Passthrough ? "ac3,eac3,dts,truehd" : ""); Option("deinterlace",settings.Deinterlace ? "yes" : "no");
            Option("sub-font",settings.SubtitleFont); Option("sub-font-size",settings.SubtitleSize.ToString()); Option("sub-color",settings.SubtitleColor); Option("sub-back-color",settings.SubtitleBackground); Option("sub-pos",settings.SubtitlePosition.ToString());
            Option("target-colorspace-hint",settings.Hdr ? "yes" : "no");
            if(settings.Aspect != "Default") Option("video-aspect-override",settings.Aspect);
            if(mpv_initialize(handle)<0) throw new InvalidOperationException("libmpv failed to initialize D3D11 playback.");
            events=Task.Run(EventLoop);
        }
        catch { mpv_terminate_destroy(handle); handle=IntPtr.Zero; throw; }
    }
    private void Option(string name,string value) { if(mpv_set_option_string(handle,name,value)<0) throw new InvalidOperationException($"Unsupported playback option: {name}"); }
    public void Set(string name,string value) { lock(gate) { if(handle!=IntPtr.Zero) mpv_set_property_string(handle,name,value); } }
    public string Get(string name) { lock(gate) { if(handle==IntPtr.Zero) return ""; var p=mpv_get_property_string(handle,name); if(p==IntPtr.Zero) return ""; try { return Marshal.PtrToStringUTF8(p)??""; } finally { mpv_free(p); } } }
    public double Number(string name) => double.TryParse(Get(name),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var d) ? d : 0;
    public void Command(params string[] args)
    {
        lock(gate)
        {
            if(handle==IntPtr.Zero) return;
            var ptrs=args.Select(Marshal.StringToCoTaskMemUTF8).ToArray(); var array=Marshal.AllocHGlobal((ptrs.Length+1)*IntPtr.Size);
            try { for(int i=0;i<ptrs.Length;i++) Marshal.WriteIntPtr(array,i*IntPtr.Size,ptrs[i]); Marshal.WriteIntPtr(array,ptrs.Length*IntPtr.Size,IntPtr.Zero); if(mpv_command(handle,array)<0) throw new InvalidOperationException("Playback command failed."); }
            finally { foreach(var p in ptrs) Marshal.FreeCoTaskMem(p); Marshal.FreeHGlobal(array); }
        }
    }
    public void Play(MediaItem item, double start=0)
    {
        Set("http-header-fields",string.Join(',',item.Headers.Where(h=>h.Key is "User-Agent" or "Referer").Select(h=>$"{h.Key}: {h.Value.Replace(",","%2C")}")));
        Command("loadfile",item.Url,"replace","-1","start="+start.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private void EventLoop()
    {
        while(!lifetime.IsCancellationRequested)
        {
            Event e; End end=default;
            lock(gate) { if(handle==IntPtr.Zero) break; var p=mpv_wait_event(handle,.05); e=Marshal.PtrToStructure<Event>(p); if(e.Id==7 && e.Data!=IntPtr.Zero) end=Marshal.PtrToStructure<End>(e.Data); }
            if(e.Id==8) Loaded?.Invoke();
            if(e.Id==7 && end.Reason is 0 or 4) Ended?.Invoke(end.Reason==4);
        }
    }
    public string Stats() => $"Codec: {Get("video-codec")} · {Get("width")}×{Get("height")}\nDecode: {Get("hwdec-current")} · FPS: {Get("estimated-vf-fps")}\nVideo bitrate: {Number("video-bitrate")/1000:F0} kbps · Dropped: {Get("frame-drop-count")}\nAudio: {Get("audio-codec")} · Cache: {Number("demuxer-cache-duration"):F1} s";
    public List<(string Id,string Name,string Type)> Tracks()
    {
        var result=new List<(string,string,string)>();
        for(int i=0;i<Math.Min(100,Number("track-list/count"));i++) result.Add((Get($"track-list/{i}/id"),$"{Get($"track-list/{i}/title")} {Get($"track-list/{i}/lang")} {Get($"track-list/{i}/codec")}",Get($"track-list/{i}/type")));
        return result;
    }
    public void Dispose() { lifetime.Cancel(); events.Wait(TimeSpan.FromSeconds(2)); lock(gate) { if(handle!=IntPtr.Zero) { mpv_terminate_destroy(handle); handle=IntPtr.Zero; } } lifetime.Dispose(); }
}

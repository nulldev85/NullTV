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
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_get_property_string(IntPtr ctx,[MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern void mpv_free(IntPtr data);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr mpv_wait_event(IntPtr ctx,double timeout);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern void mpv_wakeup(IntPtr ctx);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_command_async(IntPtr ctx,ulong reply,IntPtr args);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_set_property_async(IntPtr ctx,ulong reply,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,int format,IntPtr data);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] private static extern int mpv_observe_property(IntPtr ctx,ulong reply,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,int format);
    [StructLayout(LayoutKind.Sequential)] private struct Event { public int Id,Error; public ulong Reply; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct End { public int Reason,Error; public long Entry; public long Insert; public int Count; }
    [StructLayout(LayoutKind.Sequential)] private struct Property { public IntPtr Name; public int Format; public IntPtr Data; }
    public sealed record PlaybackState(bool Paused=false,bool Idle=true,bool Buffering=false,bool Muted=false,double Position=0,double Duration=0,double Volume=75);
    private volatile PlaybackState state=new();
    public PlaybackState State=>state;
    public event Action? StateChanged;
    private readonly object gate=new();
    private IntPtr handle;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Task events;
    private long nextRequest;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong,TaskCompletionSource<int>> replies=new();
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
            Option("hwdec",settings.HardwareDecode ? "d3d11va" : "no"); Option("idle","yes"); Option("keep-open","no");
            Option("cache","yes"); Option("demuxer-max-bytes",$"{settings.BufferMiB}MiB"); Option("demuxer-readahead-secs",settings.LowLatency ? "1" : settings.CacheSeconds.ToString());
            Option("cache-pause",settings.LowLatency ? "no" : "yes"); Option("network-timeout","15");
            Option("volume",settings.Volume.ToString()); Option("alang",settings.AudioLanguage); Option("slang",settings.SubtitleLanguage); Option("audio-device",settings.AudioDevice);
            Option("audio-spdif",settings.Passthrough ? "ac3,eac3,dts,truehd" : ""); Option("deinterlace",settings.Deinterlace ? "yes" : "no");
            Option("sub-font",settings.SubtitleFont); Option("sub-font-size",settings.SubtitleSize.ToString()); Option("sub-color",settings.SubtitleColor); Option("sub-back-color",settings.SubtitleBackground); Option("sub-pos",settings.SubtitlePosition.ToString());
            Option("target-colorspace-hint",settings.Hdr ? "yes" : "no");
            if(settings.Aspect != "Default") Option("video-aspect-override",settings.Aspect);
            if(mpv_initialize(handle)<0) throw new InvalidOperationException("libmpv failed to initialize D3D11 playback.");
            foreach(var name in new[]{"pause","idle-active","paused-for-cache","mute"}) mpv_observe_property(handle,0,name,3);
            foreach(var name in new[]{"time-pos","duration","volume"}) mpv_observe_property(handle,0,name,5);
            events=Task.Run(EventLoop);
        }
        catch { mpv_terminate_destroy(handle); handle=IntPtr.Zero; throw; }
    }
    private void Option(string name,string value) { if(mpv_set_option_string(handle,name,value)<0) throw new InvalidOperationException($"Unsupported playback option: {name}"); }
    public void Set(string name,string value)=>QueueProperty(name,value,0);
    private int QueueProperty(string name,string value,ulong reply) { lock(gate) { if(handle==IntPtr.Zero) return -1; var text=Marshal.StringToCoTaskMemUTF8(value); var data=Marshal.AllocHGlobal(IntPtr.Size); try { Marshal.WriteIntPtr(data,text); return mpv_set_property_async(handle,reply,name,1,data); } finally { Marshal.FreeHGlobal(data); Marshal.FreeCoTaskMem(text); } } }
    private async Task SetAndWaitAsync(string name,string value,CancellationToken ct)
    {
        var id=(ulong)Interlocked.Increment(ref nextRequest); var completion=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); replies[id]=completion;
        try { ct.ThrowIfCancellationRequested(); if(QueueProperty(name,value,id)<0) throw new InvalidOperationException("Playback property could not be queued."); if(await completion.Task.WaitAsync(TimeSpan.FromSeconds(15),ct)<0) throw new InvalidOperationException("Playback property could not be applied."); }
        finally { replies.TryRemove(id,out _); }
    }
    public string Get(string name) { lock(gate) { if(handle==IntPtr.Zero) return ""; var p=mpv_get_property_string(handle,name); if(p==IntPtr.Zero) return ""; try { return Marshal.PtrToStringUTF8(p)??""; } finally { mpv_free(p); } } }
    public double Number(string name) => double.TryParse(Get(name),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var d) ? d : 0;
    public void Command(params string[] args)
    {
        lock(gate)
        {
            if(handle==IntPtr.Zero) return;
            var ptrs=args.Select(Marshal.StringToCoTaskMemUTF8).ToArray(); var array=Marshal.AllocHGlobal((ptrs.Length+1)*IntPtr.Size);
            try { for(int i=0;i<ptrs.Length;i++) Marshal.WriteIntPtr(array,i*IntPtr.Size,ptrs[i]); Marshal.WriteIntPtr(array,ptrs.Length*IntPtr.Size,IntPtr.Zero); if(mpv_command_async(handle,0,array)<0) throw new InvalidOperationException("Playback command failed."); }
            finally { foreach(var p in ptrs) Marshal.FreeCoTaskMem(p); Marshal.FreeHGlobal(array); }
        }
    }
    public async Task PlayAsync(MediaItem item, double start=0,CancellationToken ct=default)
    {
        using var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,ct);
        var headers=string.Join(',',item.Headers.Where(h=>h.Key is "User-Agent" or "Referer").Select(h=>$"{h.Key}: {h.Value.Replace(",","%2C")}"));
        await SetAndWaitAsync("http-header-fields",headers,request.Token); await SetAndWaitAsync("pause","no",request.Token);
        request.Token.ThrowIfCancellationRequested();
        Command("loadfile",item.Url,"replace","-1","start="+start.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private void EventLoop()
    {
        while(!lifetime.IsCancellationRequested)
        {
            Event e; End end=default;
            // mpv_wait_event must never hold the UI command/property gate. The client API is thread-safe;
            // Dispose wakes and joins this loop before destroying its handle.
            var p=mpv_wait_event(handle,.25); e=Marshal.PtrToStructure<Event>(p); if(e.Id==7 && e.Data!=IntPtr.Zero) end=Marshal.PtrToStructure<End>(e.Data);
            if(e.Id==4 && replies.TryRemove(e.Reply,out var completion)) completion.TrySetResult(e.Error);
            if(e.Id==22 && e.Data!=IntPtr.Zero)
            {
                var property=Marshal.PtrToStructure<Property>(e.Data);
                var propertyName=Marshal.PtrToStringUTF8(property.Name);
                if(property.Format==0) state=propertyName switch { "time-pos"=>state with { Position=0 },"duration"=>state with { Duration=0 },_=>state };
                if(property.Data!=IntPtr.Zero && property.Format is 3 or 5)
                {
                    var name=Marshal.PtrToStringUTF8(property.Name); var flag=property.Format==3 && Marshal.ReadInt32(property.Data)!=0;
                    var number=property.Format==5 ? BitConverter.Int64BitsToDouble(Marshal.ReadInt64(property.Data)) : 0;
                    if(!double.IsFinite(number)) continue;
                    var old=state; state=name switch { "pause"=>old with { Paused=flag },"idle-active"=>old with { Idle=flag },"paused-for-cache"=>old with { Buffering=flag },"mute"=>old with { Muted=flag },"time-pos"=>old with { Position=number },"duration"=>old with { Duration=number },"volume"=>old with { Volume=number },_=>old };
                    if(name is not ("time-pos" or "duration")) StateChanged?.Invoke();
                }
            }
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
    public void Dispose() { if(lifetime.IsCancellationRequested) return; lifetime.Cancel(); foreach(var reply in replies.Values) reply.TrySetCanceled(); mpv_wakeup(handle); events.GetAwaiter().GetResult(); lock(gate) { if(handle!=IntPtr.Zero) { mpv_terminate_destroy(handle); handle=IntPtr.Zero; } } lifetime.Dispose(); }
}

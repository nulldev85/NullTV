using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
namespace NullTV.App;
public sealed class ImageCache(HttpClient http,string directory)
{
    private readonly SemaphoreSlim downloads=new(4),maintenance=new(1);
    private readonly ConcurrentDictionary<string,Lazy<Task<string?>>> requests=new();
    private readonly ConcurrentDictionary<string,DateTimeOffset> failed=new();
    private int writes;
    public async Task<string?> GetAsync(string url,CancellationToken ct)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        if(failed.TryGetValue(key,out var retry) && retry>DateTimeOffset.UtcNow) return null;
        var request=requests.GetOrAdd(key,_=>new(()=>Task.Run(()=>DownloadAsync(uri,key))));
        try { return await request.Value.WaitAsync(ct); }
        finally { if(request.IsValueCreated && request.Value.IsCompleted) ((ICollection<KeyValuePair<string,Lazy<Task<string?>>>>)requests).Remove(new(key,request)); }
    }
    private async Task<string?> DownloadAsync(Uri uri,string key)
    {
        await downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory); var path=Path.Combine(directory,key+".img");
            if(File.Exists(path)) { if(DateTime.UtcNow-File.GetLastWriteTimeUtc(path)>TimeSpan.FromMinutes(5)) File.SetLastWriteTimeUtc(path,DateTime.UtcNow); return path; }
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12)); var ct=timeout.Token;
            using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength>4*1024*1024) { failed[key]=DateTimeOffset.UtcNow.AddSeconds(20); return null; }
            var type=response.Content.Headers.ContentType?.MediaType;
            if(type!=null && !type.StartsWith("image/") && type!="application/octet-stream") { failed[key]=DateTimeOffset.UtcNow.AddSeconds(20); return null; }
            await using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false); using var output=new MemoryStream(); var buffer=new byte[16384]; int count;
            while((count=await stream.ReadAsync(buffer,ct).ConfigureAwait(false))>0) { if(output.Length+count>4*1024*1024) return null; await output.WriteAsync(buffer.AsMemory(0,count),ct).ConfigureAwait(false); }
            if(output.Length==0) return null;
            var temp=path+".tmp"; await File.WriteAllBytesAsync(temp,output.ToArray(),ct).ConfigureAwait(false); File.Move(temp,path,true);
            failed.TryRemove(key,out _);
            if(Interlocked.Increment(ref writes)%16==0) await TrimAsync().ConfigureAwait(false);
            return path;
        }
        catch(Exception e) when(e is HttpRequestException or IOException or OperationCanceledException) { failed[key]=DateTimeOffset.UtcNow.AddSeconds(20); return null; }
        finally { downloads.Release(); }
    }
    private async Task TrimAsync()
    {
        if(!await maintenance.WaitAsync(0).ConfigureAwait(false)) return;
        try { var files=new DirectoryInfo(directory).GetFiles("*.img").OrderByDescending(f=>f.LastWriteTimeUtc).ToArray(); long bytes=0; for(int i=0;i<files.Length;i++) { bytes+=files[i].Length; if(i>=500 || bytes>128L*1024*1024) try { files[i].Delete(); } catch(IOException) { } } if(failed.Count>1000) failed.Clear(); }
        finally { maintenance.Release(); }
    }
}

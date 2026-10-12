using System.Security.Cryptography;
using System.Text;

namespace NullTV.App;
public sealed class ImageCache(HttpClient http,string directory)
{
    private readonly SemaphoreSlim gate=new(4);
    private readonly HashSet<string> failed=[];
    public async Task<string?> GetAsync(string url,CancellationToken ct)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        if(failed.Contains(key)) return null;
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory); var path=Path.Combine(directory,key+".img"); if(File.Exists(path)) { File.SetLastWriteTimeUtc(path,DateTime.UtcNow); return path; }
            using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength>4*1024*1024 || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/")!=true) { failed.Add(key); return null; }
            await using var stream=await response.Content.ReadAsStreamAsync(ct); using var output=new MemoryStream(); var buffer=new byte[8192]; int count;
            while((count=await stream.ReadAsync(buffer,ct))>0) { if(output.Length+count>4*1024*1024) return null; await output.WriteAsync(buffer.AsMemory(0,count),ct); }
            await File.WriteAllBytesAsync(path,output.ToArray(),ct);
            var files=new DirectoryInfo(directory).GetFiles().OrderByDescending(f=>f.LastWriteTimeUtc).ToArray(); long total=0; for(int i=0;i<files.Length;i++) { total+=files[i].Length; if(i>=500 || total>128L*1024*1024) try { files[i].Delete(); } catch(IOException) { } }
            if(failed.Count>1000) failed.Clear(); return path;
        }
        catch(Exception e) when(e is HttpRequestException or IOException or OperationCanceledException) { return null; }
        finally { gate.Release(); }
    }
}

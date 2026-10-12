using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NullTV.Core;

public sealed record UpdateRelease(int Schema,string AppId,string Version,string Architecture,int MinimumWindowsBuild,DateTimeOffset PublishedUtc,string InstallerUrl,long Size,string Sha256,string Notes);
public sealed record SignedUpdate(string Payload,string Signature);
public sealed record AvailableUpdate(UpdateRelease Release,byte[] Manifest);
public sealed record UpdateDownload(long Received,long Total);

public static class UpdateTrust
{
    public const string Repository="nulldev85/NullTV-updates";
    public const string Feed="https://github.com/"+Repository+"/releases/latest/download/update.json";
    public const string UpgradeCode="5FF6F3A7-4F45-48C7-8E58-3C80A3CC42FA";
    public const string PublicKey="MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+R4m/WM7RQAYA4JP0vqy2YTavEK3U/xttNH+JAqjopgIko/jM6FQH/m2E/GvGWOJ+QQ+CYZu6XQcLaNan7zfKQ==";
    public const int MaximumManifestBytes=96*1024;
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) { WriteIndented=true };
    public static Version ParseVersion(string value)
    {
        if(!Regex.IsMatch(value??"","^(0|[1-9][0-9]{0,4})\\.(0|[1-9][0-9]{0,4})\\.(0|[1-9][0-9]{0,4})$") || !Version.TryParse(value,out var version)) throw new InvalidDataException("The update version is invalid.");
        if(version.Major>255 || version.Minor>255 || version.Build>65535) throw new InvalidDataException("The update version is unsupported.");
        return version;
    }
    public static UpdateRelease Verify(ReadOnlySpan<byte> manifest,string? publicKey=null)
    {
        if(manifest.Length==0 || manifest.Length>MaximumManifestBytes) throw new InvalidDataException("The update manifest is invalid.");
        try
        {
            var signed=JsonSerializer.Deserialize<SignedUpdate>(manifest,Json)??throw new InvalidDataException("The update manifest is empty.");
            var payload=Convert.FromBase64String(signed.Payload); var signature=Convert.FromBase64String(signed.Signature);
            using var key=ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey??PublicKey),out _);
            if(signature.Length!=64 || !key.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new InvalidDataException("This update could not be authenticated. Nothing was installed.");
            var release=JsonSerializer.Deserialize<UpdateRelease>(payload,Json)??throw new InvalidDataException("The release is empty.");
            Validate(release); return release;
        }
        catch(Exception e) when(e is JsonException or FormatException or CryptographicException or ArgumentException) { throw new InvalidDataException("This update could not be authenticated. Nothing was installed.",e); }
    }
    public static void Validate(UpdateRelease release)
    {
        _=ParseVersion(release.Version);
        var expected=$"https://github.com/{Repository}/releases/download/v{release.Version}/NullTV-Setup-arm64-{release.Version}.msi";
        if(release.Schema!=1 || release.AppId!="NullTV" || release.Architecture!="arm64" || release.MinimumWindowsBuild<22000 || release.Size<=0 || release.Size>1024L*1024*1024 || !Regex.IsMatch(release.Sha256??"","^[a-fA-F0-9]{64}$") || release.InstallerUrl!=expected || release.Notes is null || release.Notes.Length>24000 || release.PublishedUtc==default || release.PublishedUtc>DateTimeOffset.UtcNow.AddDays(1)) throw new InvalidDataException("This release is incompatible or its details are invalid.");
    }
    public static byte[] Sign(UpdateRelease release,ECDsa key)
    {
        Validate(release); var payload=JsonSerializer.SerializeToUtf8Bytes(release,Json);
        return JsonSerializer.SerializeToUtf8Bytes(new SignedUpdate(Convert.ToBase64String(payload),Convert.ToBase64String(key.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))),Json);
    }
    public static bool IsNewer(UpdateRelease release,Version current,int windowsBuild,string architecture)=>ParseVersion(release.Version)>current && windowsBuild>=release.MinimumWindowsBuild && architecture.Equals(release.Architecture,StringComparison.OrdinalIgnoreCase);
}

public sealed class UpdateClient(HttpClient http,string? publicKey=null)
{
    public async Task<AvailableUpdate?> CheckAsync(Version current,int windowsBuild,string architecture,CancellationToken ct=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,UpdateTrust.Feed);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NullTV",current.ToString(3))); request.Headers.CacheControl=new CacheControlHeaderValue { NoCache=true };
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.NotFound) throw new InvalidDataException("No update release has been published yet. Try again later.");
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>UpdateTrust.MaximumManifestBytes) throw new InvalidDataException("The update manifest is too large.");
        await using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false); using var output=new MemoryStream(); var buffer=new byte[8192]; int count;
        while((count=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0) { if(output.Length+count>UpdateTrust.MaximumManifestBytes) throw new InvalidDataException("The update manifest is too large."); output.Write(buffer,0,count); }
        var manifest=output.ToArray(); var release=UpdateTrust.Verify(manifest,publicKey);
        if(UpdateTrust.ParseVersion(release.Version)>current && (windowsBuild<release.MinimumWindowsBuild || !architecture.Equals(release.Architecture,StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("The latest release requires a newer Windows version or a different processor.");
        return UpdateTrust.IsNewer(release,current,windowsBuild,architecture) ? new(release,manifest) : null;
    }
    public async Task<string> DownloadAsync(AvailableUpdate update,string directory,IProgress<UpdateDownload>? progress,CancellationToken ct=default)
    {
        // Re-authenticate even if a caller constructed AvailableUpdate directly.
        var release=UpdateTrust.Verify(update.Manifest,publicKey); if(release!=update.Release) throw new InvalidDataException("Release details changed after verification.");
        Directory.CreateDirectory(directory); var name=$"NullTV-Setup-arm64-{release.Version}.msi"; var partial=Path.Combine(directory,name+".part"); var target=Path.Combine(directory,name);
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,release.InstallerUrl); request.Headers.UserAgent.ParseAdd("NullTV-Updater/1.0");
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false); response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long length && length!=release.Size) throw new InvalidDataException("The update download size does not match the release.");
            await using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false); using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long received=0;
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
            {
                var buffer=new byte[65536]; int count;
                while((count=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0)
                {
                    received+=count; if(received>release.Size) throw new InvalidDataException("The update download exceeded its expected size.");
                    hash.AppendData(buffer,0,count); await output.WriteAsync(buffer.AsMemory(0,count),ct).ConfigureAwait(false); progress?.Report(new(received,release.Size));
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            if(received!=release.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),Convert.FromHexString(release.Sha256))) throw new InvalidDataException("The download failed verification. Nothing was installed. Try downloading again.");
            File.Move(partial,target,true); await File.WriteAllBytesAsync(Path.Combine(directory,"update.json"),update.Manifest,ct).ConfigureAwait(false); return target;
        }
        catch { if(File.Exists(partial)) File.Delete(partial); throw; }
    }
    public static async Task VerifyPackageAsync(string path,UpdateRelease release,CancellationToken ct=default)
    {
        await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        if(input.Length!=release.Size || !CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(input,ct).ConfigureAwait(false),Convert.FromHexString(release.Sha256))) throw new InvalidDataException("The downloaded installer changed. Download it again.");
    }
}

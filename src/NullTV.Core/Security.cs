using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NullTV.Core;

public static class SecretStore
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError=true)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
    private static byte[] Transform(byte[] data, bool encrypt)
    {
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Length = data.Length, Data = pin.AddrOfPinnedObject() };
            bool ok = encrypt ? CryptProtectData(ref input, "NullTV", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows could not unlock this file for the current user.");
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; } finally { LocalFree(output.Data); }
        }
        finally { pin.Free(); }
    }
    public static byte[] Protect(byte[] data) => Transform(data,true);
    public static byte[] Unprotect(byte[] data) => Transform(data,false);
    public static string HashPin(string pin, string salt) => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(salt), 210_000, HashAlgorithmName.SHA256, 32));
    public static bool CheckPin(string pin, Profile p) => p.PinHash != "" && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(p.PinHash), Convert.FromBase64String(HashPin(pin,p.PinSalt)));
}
public sealed class SettingsStore(string directory)
{
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented=true };
    private readonly SemaphoreSlim gate = new(1);
    public string DirectoryPath => directory;
    public async Task<Settings> LoadAsync()
    {
        var path = Path.Combine(directory,"settings.dat");
        if (!File.Exists(path)) return new();
        var bytes=await File.ReadAllBytesAsync(path); var value = await Task.Run(()=>JsonSerializer.Deserialize<Settings>(SecretStore.Unprotect(bytes), JsonOptions)) ?? throw new InvalidDataException("Settings are empty.");
        value.Validate(); return value;
    }
    public async Task SaveAsync(Settings settings)
    {
        await gate.WaitAsync();
        try { settings.Validate(); var snapshot=JsonSerializer.SerializeToUtf8Bytes(settings,JsonOptions); var bytes=await Task.Run(()=>SecretStore.Protect(snapshot)); Directory.CreateDirectory(directory); var path = Path.Combine(directory,"settings.dat"); await File.WriteAllBytesAsync(path+".tmp",bytes); File.Move(path+".tmp",path,true); }
        finally { gate.Release(); }
    }
    public async Task SaveCacheAsync(string id, Catalog catalog) { var bytes=await Task.Run(()=>SecretStore.Protect(JsonSerializer.SerializeToUtf8Bytes(catalog))); Directory.CreateDirectory(Path.Combine(directory,"cache")); await File.WriteAllBytesAsync(Path.Combine(directory,"cache",id+".dat"),bytes); }
    public async Task<Catalog?> LoadCacheAsync(string id) { var file=Path.Combine(directory,"cache",id+".dat"); if (!File.Exists(file)) return null; try { var bytes=await File.ReadAllBytesAsync(file); return await Task.Run(()=>JsonSerializer.Deserialize<Catalog>(SecretStore.Unprotect(bytes))); } catch (Exception e) when (e is JsonException or CryptographicException or IOException) { return null; } }
    public async Task SaveGuideAsync(string id,List<Programme> guide) { var bytes=await Task.Run(()=>SecretStore.Protect(JsonSerializer.SerializeToUtf8Bytes(guide))); Directory.CreateDirectory(Path.Combine(directory,"cache")); await File.WriteAllBytesAsync(Path.Combine(directory,"cache",id+".guide"),bytes); }
    public async Task<List<Programme>?> LoadGuideAsync(string id) { var file=Path.Combine(directory,"cache",id+".guide"); if(!File.Exists(file)) return null; try { var bytes=await File.ReadAllBytesAsync(file); return await Task.Run(()=>JsonSerializer.Deserialize<List<Programme>>(SecretStore.Unprotect(bytes))); } catch(Exception e) when(e is JsonException or CryptographicException or IOException) { return null; } }
    public static byte[] Backup(Settings settings, string password)
    {
        if (password.Length < 10) throw new InvalidDataException("Use a backup password of at least 10 characters.");
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12); var plain = JsonSerializer.SerializeToUtf8Bytes(settings,JsonOptions); var cipher = new byte[plain.Length]; var tag = new byte[16];
        var key = Rfc2898DeriveBytes.Pbkdf2(password,salt,210_000,HashAlgorithmName.SHA256,32);
        try { using var aes = new AesGcm(key,16); aes.Encrypt(nonce,plain,cipher,tag,Encoding.ASCII.GetBytes("NullTV01")); return [..Encoding.ASCII.GetBytes("NullTV01"),..salt,..nonce,..tag,..cipher]; } finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public static Settings Restore(byte[] data, string password)
    {
        if (data.Length < 52 || Encoding.ASCII.GetString(data,0,8)!="NullTV01") throw new InvalidDataException("This is not a NullTV backup.");
        var key=Rfc2898DeriveBytes.Pbkdf2(password,data.AsSpan(8,16),210_000,HashAlgorithmName.SHA256,32); var plain=new byte[data.Length-52];
        try { using var aes=new AesGcm(key,16); aes.Decrypt(data.AsSpan(24,12),data.AsSpan(52),data.AsSpan(36,16),plain,Encoding.ASCII.GetBytes("NullTV01")); var s=JsonSerializer.Deserialize<Settings>(plain,JsonOptions) ?? throw new InvalidDataException("Backup is empty."); s.Validate(); return s; } finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
}
public sealed class SafeLog(string directory)
{
    private readonly SemaphoreSlim gate = new(1);
    // Only fixed event codes and exception TYPES. Raw exception text, URIs, headers, titles and credentials never enter logs.
    public async Task WriteAsync(string eventCode, Exception? error = null)
    {
        await gate.WaitAsync();
        try { Directory.CreateDirectory(directory); var file=Path.Combine(directory,"current.log"); if (File.Exists(file) && new FileInfo(file).Length>512*1024) { for(int i=3;i>=1;i--) { var old=Path.Combine(directory,$"{i}.log"); if(i==3) File.Delete(old); else if(File.Exists(old)) File.Move(old,Path.Combine(directory,$"{i+1}.log"),true); } File.Move(file,Path.Combine(directory,"1.log"),true); } var frames=error==null ? "" : string.Join(" > ",new System.Diagnostics.StackTrace(error,false).GetFrames().Take(8).Select(f=>$"{f.GetMethod()?.DeclaringType?.Name}.{f.GetMethod()?.Name}")); await File.AppendAllTextAsync(file,$"{DateTimeOffset.UtcNow:O} {eventCode} {error?.GetType().Name} {frames}\n"); } finally { gate.Release(); }
    }
}

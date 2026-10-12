using System.Security.Cryptography;
using NullTV.Core;

try
{
    if(args.Length==2 && args[0]=="keygen")
    {
        if(File.Exists(args[1])) throw new InvalidDataException("The signing key already exists; it was preserved.");
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256); var secret=key.ExportPkcs8PrivateKey();
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!); await File.WriteAllBytesAsync(args[1],SecretStore.Protect(secret)); }
        finally { CryptographicOperations.ZeroMemory(secret); }
        Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())); return 0;
    }
    if(args.Length==6 && args[0]=="sign")
    {
        var msi=Path.GetFullPath(args[2]); var version=UpdateTrust.ParseVersion(args[3]).ToString(3); var bytes=SecretStore.Unprotect(await File.ReadAllBytesAsync(args[1]));
        using var key=ECDsa.Create(); try { key.ImportPkcs8PrivateKey(bytes,out _); } finally { CryptographicOperations.ZeroMemory(bytes); }
        if(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())!=UpdateTrust.PublicKey) throw new InvalidDataException("The signing key does not match the app's pinned public key.");
        await using var input=File.OpenRead(msi); var hash=Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();
        var release=new UpdateRelease(1,"NullTV",version,"arm64",22000,DateTimeOffset.UtcNow,$"https://github.com/{UpdateTrust.Repository}/releases/download/v{version}/NullTV-Setup-arm64-{version}.msi",input.Length,hash,await File.ReadAllTextAsync(args[4]));
        WindowsUpdatePackage.Validate(msi,release); await File.WriteAllBytesAsync(args[5],UpdateTrust.Sign(release,key)); Console.WriteLine($"Signed and verified NullTV {version} ARM64 release."); return 0;
    }
    if(args.Length==3 && args[0]=="verify")
    {
        var release=UpdateTrust.Verify(await File.ReadAllBytesAsync(args[1])); await UpdateClient.VerifyPackageAsync(args[2],release); WindowsUpdatePackage.Validate(args[2],release); Console.WriteLine($"PASS signed manifest, SHA-256, size, MSI family, version and ARM64: {release.Version}"); return 0;
    }
    Console.Error.WriteLine("Usage: keygen <DPAPI-key-path> | sign <key-path> <msi> <version> <notes> <manifest> | verify <manifest> <msi>"); return 2;
}
catch(Exception e) { Console.Error.WriteLine(e.Message); return 1; }

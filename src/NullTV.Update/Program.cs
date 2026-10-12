using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using NullTV.Core;

// Run from the downloaded job directory, never the installation being upgraded.
if(args.Length!=1) return 2;
var jobPath=Path.GetFullPath(args[0]); var directory=Path.GetDirectoryName(jobPath)!; UpdateJob? job=null; UpdateRelease? release=null; bool parentExited=false;
try
{
    if(!OperatingSystem.IsWindows() || !OperatingSystem.IsWindowsVersionAtLeast(10,0,22000) || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture!=System.Runtime.InteropServices.Architecture.Arm64) throw new InvalidDataException("Updates require Windows 11 ARM64.");
    job=JsonSerializer.Deserialize<UpdateJob>(await File.ReadAllBytesAsync(jobPath),UpdateTrust.Json)??throw new InvalidDataException("The update job is missing.");
    using var registration=Registry.CurrentUser.OpenSubKey("Software\\NullTV\\Installer"); var registered=registration?.GetValue("InstallDirectory") as string;
    var install=Path.GetFullPath(job.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);
    if(string.IsNullOrEmpty(registered) || !install.Equals(Path.GetFullPath(registered).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase) || directory.StartsWith(install+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("This is not the registered NullTV installation.");
    // File-sharing locks survive async continuations; named Mutex ownership is
    // thread-affine and cannot safely span WaitForExitAsync/HashDataAsync.
    var lockName="NullTV-update-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(install.ToUpperInvariant())))[..20]+".lock";
    using var installLock=new FileStream(Path.Combine(Path.GetTempPath(),lockName),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    {
        release=UpdateTrust.Verify(await File.ReadAllBytesAsync(Path.Combine(directory,"update.json")));
        var package=Path.Combine(directory,$"NullTV-Setup-arm64-{release.Version}.msi");
        if(!UpdateTrust.IsNewer(release,UpdateTrust.ParseVersion(job.CurrentVersion),Environment.OSVersion.Version.Build,"arm64")) throw new InvalidDataException("The release is no longer newer than the installed app.");
        // Do not kill any app or reuse a PID belonging to a different process.
        try
        {
            using var parent=Process.GetProcessById(job.ParentProcessId);
            if(parent.StartTime.ToUniversalTime().Ticks!=job.ParentStartTimeUtcTicks || !Path.GetFullPath(parent.MainModule!.FileName!).Equals(Path.Combine(install,"NullTV.exe"),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The running app does not match the update request.");
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60)); await parent.WaitForExitAsync(timeout.Token);
        }
        catch(ArgumentException) { }
        parentExited=true;
        var installed=FileVersionInfo.GetVersionInfo(Path.Combine(install,"NullTV.exe")).ProductVersion?.Split('+')[0]??"";
        if(UpdateTrust.ParseVersion(installed)!=UpdateTrust.ParseVersion(job.CurrentVersion)) throw new InvalidDataException("The installed version changed. Check for updates again.");
        // Hold a read-only sharing lock through installation to prevent replacement
        // after the second hash check. msiexec can still read this file.
        await using var locked=new FileStream(package,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        if(locked.Length!=release.Size || !CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(locked),Convert.FromHexString(release.Sha256))) throw new InvalidDataException("The installer changed after downloading. Nothing was installed.");
        WindowsUpdatePackage.Validate(package,release);
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")) { UseShellExecute=false,CreateNoWindow=true };
        foreach(var value in new[]{"/i",package,"/qn","/norestart","/L*v",Path.Combine(directory,"install.log")}) start.ArgumentList.Add(value);
        using var installer=Process.Start(start)??throw new IOException("Windows Installer could not start."); await installer.WaitForExitAsync();
        var code=installer.ExitCode; var success=code is 0 or 3010;
        await ResultAsync(new(success,release.Version,code,code==3010,success ? code==3010 ? "NullTV updated. Restart Windows to finish applying all files." : "NullTV is up to date." : code==1618 ? "Another installation is running. Try the update again when it finishes." : "The update did not finish. Your saved settings are preserved. Try again in Settings.",DateTimeOffset.UtcNow));
        Restart(job,install); return success ? 0 : 1;
    }
}
catch(Exception e)
{
    if(job!=null)
    {
        try { await ResultAsync(new(false,release?.Version??job.CurrentVersion,null,false,e is InvalidDataException ? e.Message : "The update could not finish. Check for updates again in Settings.",DateTimeOffset.UtcNow)); if(parentExited) Restart(job,Path.GetFullPath(job.InstallDirectory)); } catch { }
    }
    return 1;
}
async Task ResultAsync(UpdateResult result)
{
    var updates=Path.Combine(job!.DataDirectory,"updates"); Directory.CreateDirectory(updates); var path=Path.Combine(updates,"last-result.json"); await File.WriteAllBytesAsync(path+".tmp",JsonSerializer.SerializeToUtf8Bytes(result,UpdateTrust.Json)); File.Move(path+".tmp",path,true);
}
static void Restart(UpdateJob job,string install)
{
    var start=new ProcessStartInfo(Path.Combine(install,"NullTV.exe")) { UseShellExecute=false,WorkingDirectory=install }; start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(job.DataDirectory); Process.Start(start);
}

using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using NullTV.Core;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private readonly HttpClient updateHttp=new() { Timeout=TimeSpan.FromMinutes(15) };
    private bool checkingUpdate,showingUpdate;
    private TextBlock? updateStatus;
    private string updateMessage="Check for the latest stable Windows release.";
    private static string AppVersion=>typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
    private Version UpdateCurrentVersion()
    {
        var args=Environment.GetCommandLineArgs(); var index=Array.IndexOf(args,"--smoke-update-current");
        // Visual QA uses real signed releases and downloads but never enables install.
        return args.Contains("--data-dir") && index>=0 && index+1<args.Length ? UpdateTrust.ParseVersion(args[index+1]) : UpdateTrust.ParseVersion(AppVersion);
    }
    private bool IsRegisteredInstallation()
    {
        if(Environment.GetCommandLineArgs().Contains("--smoke-update-current")) return false;
        using var registry=Registry.CurrentUser.OpenSubKey("Software\\NullTV\\Installer"); var path=registry?.GetValue("InstallDirectory") as string;
        return !string.IsNullOrEmpty(path) && Path.GetFullPath(path).TrimEnd('\\').Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);
    }
    private UIElement BuildUpdates()
    {
        var version=Text($"NullTV {AppVersion}  ·  Stable",16); version.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        updateStatus=Text(updateMessage,12); updateStatus.Opacity=.75;
        var automatic=Toggle("Check for updates at startup",settings.CheckUpdatesOnStartup);
        automatic.Toggled+=(_,_)=>_=GuardAsync(async ()=> { settings.CheckUpdatesOnStartup=automatic.IsOn; await store.SaveAsync(settings); });
        var content=Stack(version,updateStatus,automatic,Text("Checks at most once a day. Downloads and installation happen only when you choose. Your sources, favorites and history stay with you.",12));
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 }; actions.Children.Add(Primary("Check for updates",()=>CheckForUpdatesAsync(true))); actions.Children.Add(Button("Release history",()=>_=Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/"+UpdateTrust.Repository+"/releases")))); content.Children.Add(actions);
        return content;
    }
    private void UpdateMessage(string message) { updateMessage=message; if(updateStatus!=null) updateStatus.Text=message; }
    private async Task InitializeUpdatesAsync()
    {
        try
        {
            var path=Path.Combine(store.DirectoryPath,"updates","last-result.json");
            if(File.Exists(path)) { var result=JsonSerializer.Deserialize<UpdateResult>(await File.ReadAllBytesAsync(path),UpdateTrust.Json); if(result!=null) { UpdateMessage(result.Message); status.Text=result.Message; } File.Delete(path); }
        }
        catch(Exception e) when(e is IOException or JsonException) { }
        var args=Environment.GetCommandLineArgs();
        if(args.Contains("--smoke-update-current"))
        {
            var manifestIndex=Array.IndexOf(args,"--smoke-update-manifest");
            if(args.Contains("--data-dir") && manifestIndex>=0 && manifestIndex+1<args.Length) { var manifest=await File.ReadAllBytesAsync(args[manifestIndex+1]); var release=UpdateTrust.Verify(manifest); if(UpdateTrust.IsNewer(release,UpdateCurrentVersion(),Environment.OSVersion.Version.Build,"arm64")) await ShowUpdateAsync(new(release,manifest)); }
            else await CheckForUpdatesAsync(true);
            return;
        }
        if(args.Any(a=>a.StartsWith("--smoke-",StringComparison.Ordinal)) || !settings.CheckUpdatesOnStartup || DateTimeOffset.UtcNow-settings.LastUpdateCheckUtc<TimeSpan.FromDays(1)) return;
        await CheckForUpdatesAsync(false);
    }
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if(checkingUpdate || showingUpdate || closed) return; checkingUpdate=true;
        UpdateMessage("Checking for updates…");
        try
        {
            settings.LastUpdateCheckUtc=DateTimeOffset.UtcNow; await store.SaveAsync(settings);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var update=await new UpdateClient(updateHttp).CheckAsync(UpdateCurrentVersion(),Environment.OSVersion.Version.Build,"arm64",timeout.Token);
            if(closed) return;
            if(update==null) { UpdateMessage($"You're up to date. NullTV {AppVersion} is the latest stable version."); return; }
            UpdateMessage($"NullTV {update.Release.Version} is available.");
            // A startup check never covers another active dialog or repeats a dismissal.
            if(!manual && (dialogDepth>0 || settings.DismissedUpdateVersion==update.Release.Version)) return;
            await ShowUpdateAsync(update);
        }
        catch(Exception e) when(e is not OutOfMemoryException)
        {
            if(!closed) UpdateMessage(e is InvalidDataException ? e.Message : e is OperationCanceledException ? "The update check timed out. Try again when you're connected." : "Couldn't reach the update service. You can keep watching and try again later.");
            await log.WriteAsync("UPDATE_CHECK_FAILED",e);
        }
        finally { checkingUpdate=false; }
    }
    private async Task ShowUpdateAsync(AvailableUpdate update)
    {
        if(showingUpdate || closed) return; showingUpdate=true; string? package=null; string? jobDirectory=null; bool ready=false; var downloading=false; var installed=IsRegisteredInstallation();
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var label=Text("NULLTV  /  UPDATE",10); label.CharacterSpacing=160; label.Foreground=new SolidColorBrush(Color("#F1FA8C"));
        var title=Text("A better NullTV is ready.",26); title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        var description=Text("Fresh improvements. The same channels, favorites and settings.",13); description.Opacity=.7;
        var versions=new Grid { ColumnSpacing=18 }; versions.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); versions.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); versions.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var current=Stack(Text("INSTALLED",10),Text(UpdateCurrentVersion().ToString(3),22)); current.Opacity=.55;
        var next=Stack(Text("AVAILABLE",10),Text(update.Release.Version,22)); next.Children[0].SetValue(TextBlock.ForegroundProperty,new SolidColorBrush(Color("#50FA7B"))); next.Children[1].SetValue(TextBlock.ForegroundProperty,new SolidColorBrush(Color("#BD93F9")));
        Place(versions,current); var arrow=Text("→",22); arrow.Opacity=.45; arrow.VerticalAlignment=VerticalAlignment.Center; Place(versions,arrow,0,1); Place(versions,next,0,2);
        var releaseNotes=Text(update.Release.Notes,13); releaseNotes.LineHeight=21; var notes=Scroll(releaseNotes); notes.MaxHeight=180;
        var info=Text($"Stable release  ·  Windows ARM64  ·  {update.Release.Size/1048576d:0.0} MB",11); info.Opacity=.55;
        var progress=PolishedProgressBar.Create(); progress.Visibility=Visibility.Collapsed;
        var state=Text("Download now. Restart when you're ready.",12); state.Opacity=.8;
        var versionsCard=Card(versions,new Thickness(16)); versionsCard.Background=new SolidColorBrush(Color("#21222C")); versionsCard.BorderBrush=new SolidColorBrush(Color("#44475A"));
        var content=Stack(label,title,description,versionsCard,info,notes,progress,state); content.Spacing=14; content.MinWidth=380;
        var primaryStyle=new Style(typeof(Button)) { BasedOn=(Style)Application.Current.Resources["NullTVButtonStyle"],Setters={ new Setter(Control.BackgroundProperty,new SolidColorBrush(Color("#BD93F9"))),new Setter(Control.ForegroundProperty,new SolidColorBrush(Color("#21222C"))),new Setter(Control.FontWeightProperty,Microsoft.UI.Text.FontWeights.SemiBold),new Setter(Control.MinHeightProperty,44d) } };
        var secondaryStyle=new Style(typeof(Button)) { BasedOn=(Style)Application.Current.Resources["NullTVButtonStyle"],Setters={ new Setter(Control.BackgroundProperty,new SolidColorBrush(Color("#343746"))),new Setter(Control.ForegroundProperty,new SolidColorBrush(Color("#F8F8F2"))),new Setter(Control.MinHeightProperty,44d) } };
        var dialog=new ContentDialog { XamlRoot=root.XamlRoot,RequestedTheme=ElementTheme.Dark,Content=content,Background=new SolidColorBrush(Color("#282A36")),BorderBrush=new SolidColorBrush(Color("#44475A")),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),PrimaryButtonStyle=primaryStyle,CloseButtonStyle=secondaryStyle,PrimaryButtonText="Download update",CloseButtonText="Later",DefaultButton=ContentDialogButton.Primary };
        foreach(var entry in root.Resources) dialog.Resources[entry.Key]=entry.Value;
        dialog.Foreground=new SolidColorBrush(Color("#F8F8F2")); dialog.Resources["TextFillColorPrimaryBrush"]=dialog.Foreground;
        dialog.Resources["ContentDialogTopOverlay"]=new SolidColorBrush(Color("#282A36"));
        foreach(var key in new[]{"AccentButtonBackground","AccentButtonBackgroundPointerOver","AccentButtonBackgroundPressed"}) dialog.Resources[key]=new SolidColorBrush(Color("#BD93F9"));
        foreach(var key in new[]{"AccentButtonForeground","AccentButtonForegroundPointerOver","AccentButtonForegroundPressed"}) dialog.Resources[key]=new SolidColorBrush(Color("#21222C"));
        dialog.Opened+=(_,_)=> {
            foreach(var name in new[]{"PrimaryButton","CloseButton"}) if(FindNamedButton(dialog,name) is { } button) { button.Template=(ControlTemplate)Application.Current.Resources["NullTVButtonTemplate"]; button.Background=new SolidColorBrush(Color(name=="PrimaryButton" ? "#BD93F9" : "#343746")); button.Foreground=new SolidColorBrush(Color(name=="PrimaryButton" ? "#21222C" : "#F8F8F2")); button.MinHeight=button.Height=44; button.CornerRadius=new CornerRadius(10); button.Padding=new Thickness(16,10,16,10); }
        };
        dialog.Resources["ContentDialogBackground"]=new SolidColorBrush(Color("#282A36")); dialog.Resources["ContentDialogMaxWidth"]=540d; dialog.Resources["ContentDialogMinWidth"]=460d; dialog.Resources["ContentDialogCornerRadius"]=new CornerRadius(18);
        dialog.PrimaryButtonClick+=async (_,args)=>
        {
            if(ready) return;
            args.Cancel=true; if(downloading) return; var defer=args.GetDeferral(); downloading=true; dialog.IsPrimaryButtonEnabled=false; dialog.CloseButtonText="Cancel download"; progress.Visibility=Visibility.Visible; progress.Value=0;
            try
            {
                jobDirectory=Path.Combine(store.DirectoryPath,"updates",Guid.NewGuid().ToString("N"));
                var updates=new Progress<UpdateDownload>(value=> { if(closed) return; progress.Value=100d*value.Received/value.Total; state.Text=$"Downloading  ·  {value.Received/1048576d:0.0} / {value.Total/1048576d:0.0} MB"; });
                package=await new UpdateClient(updateHttp).DownloadAsync(update,jobDirectory,updates,cancel.Token);
                state.Text="Verifying the Windows installer…"; await Task.Run(()=>WindowsUpdatePackage.Validate(package,update.Release),cancel.Token);
                ready=true; state.Foreground=new SolidColorBrush(Color("#50FA7B")); state.Text=installed ? "Verified and ready. Playback will close, then NullTV will reopen." : "Verified and ready. Install the Windows app to enable one-click updates.";
                dialog.PrimaryButtonText=installed ? "Restart and update" : "Open Windows installer"; dialog.CloseButtonText="Later"; dialog.IsPrimaryButtonEnabled=true; UpdateMessage($"NullTV {update.Release.Version} is ready to install.");
            }
            catch(Exception e) when(e is not OutOfMemoryException)
            {
                if(!cancel.IsCancellationRequested) { state.Foreground=new SolidColorBrush(Color("#F1FA8C")); state.Text=e is InvalidDataException ? e.Message : "The download couldn't finish. Nothing was installed. Try again."; dialog.PrimaryButtonText="Retry download"; dialog.CloseButtonText="Later"; dialog.IsPrimaryButtonEnabled=true; progress.Visibility=Visibility.Collapsed; }
                await log.WriteAsync("UPDATE_DOWNLOAD_FAILED",e);
            }
            finally { downloading=false; defer.Complete(); }
        };
        dialog.CloseButtonClick+=(_,_)=>cancel.Cancel();
        dialogDepth++; playerOverlay?.Hide(); foreach(var tile in surfaces) tile.Hide(true);
        ContentDialogResult result;
        try { result=await dialog.ShowAsync(); }
        finally { showingUpdate=false; cancel.Cancel(); dialogDepth--; foreach(var tile in surfaces) tile.Hide(!IsPlayerVisible || !active.ContainsKey(tile)); }
        if(result!=ContentDialogResult.Primary || !ready || package==null || jobDirectory==null) { settings.DismissedUpdateVersion=update.Release.Version; await store.SaveAsync(settings); return; }
        if(Environment.GetCommandLineArgs().Contains("--smoke-update-current")) { UpdateMessage("Visual validation complete. Installation is disabled for this isolated test copy."); return; }
        if(!installed)
        {
            var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")) { UseShellExecute=false }; foreach(var value in new[]{"/i",package,"/norestart"}) start.ArgumentList.Add(value); Process.Start(start); return;
        }
        var helper=Path.Combine(jobDirectory,"NullTV.Update.exe"); File.Copy(Path.Combine(AppContext.BaseDirectory,"NullTV.Update.exe"),helper,true);
        using var currentProcess=Process.GetCurrentProcess(); var job=new UpdateJob(currentProcess.Id,currentProcess.StartTime.ToUniversalTime().Ticks,AppContext.BaseDirectory,store.DirectoryPath,AppVersion);
        var path=Path.Combine(jobDirectory,"job.json"); await File.WriteAllBytesAsync(path,JsonSerializer.SerializeToUtf8Bytes(job,UpdateTrust.Json));
        await SaveProgressAsync(); await store.SaveAsync(settings);
        var launch=new ProcessStartInfo(helper) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=jobDirectory }; launch.ArgumentList.Add(path);
        _=Process.Start(launch)??throw new IOException("The update helper could not start."); closingConfirmed=true; Close();
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private UIElement BuildIntegrations()
    {
        if(settings.Profile.Sources.Any(s=>s.Id==serverBrowseId)) return BuildServers();
        var page=Stack(Heading("Integrations","Connect your servers. Bring your addons.")); page.Spacing=26;
        var addonHeading=new Grid { ColumnSpacing=12 }; addonHeading.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); addonHeading.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var label=Text("Stremio addons",21); label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; Place(addonHeading,Stack(label,Text("Catalogs, title details and playback sources, inside NullTV.",12)));
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center };
        actions.Children.Add(AsyncButton("Add Cinemeta",()=>InstallAddonAsync(null,"https://v3-cinemeta.strem.io/manifest.json")));
        actions.Children.Add(Primary("+  Add addon",()=>InstallAddonAsync(null))); Place(addonHeading,actions,0,1); page.Children.Add(addonHeading);
        if(settings.Profile.Addons.Count==0) page.Children.Add(Card(Stack(Text("Your addons belong here.",19),Text("Paste an addon's manifest or stremio:// install link. Add Cinemeta for a movie and series catalog, then connect your preferred stream addons.",13)),new Thickness(24)));
        foreach(var addon in settings.Profile.Addons)
        {
            var top=new Grid { ColumnSpacing=16 }; top.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
            var name=Text(addon.Manifest.Name,19); name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
            var host=Uri.TryCreate(addon.ManifestUrl,UriKind.Absolute,out var address) ? address.Host : "Addon";
            var capabilities=new List<string>(); if(addon.Manifest.Catalogs.Count>0) capabilities.Add($"{addon.Manifest.Catalogs.Count} catalogs"); if(addon.Manifest.Resources.Any(r=>r.Name=="stream")) capabilities.Add("Playback sources"); if(addon.Manifest.Resources.Any(r=>r.Name=="meta")) capabilities.Add("Title details");
            var info=Text($"{host}  ·  {addon.Manifest.Version}  ·  {string.Join(" · ",capabilities)}",11); info.Opacity=.65;
            Place(top,Stack(name,info)); var enabled=Toggle("Enabled",addon.Enabled); enabled.VerticalAlignment=VerticalAlignment.Center; Place(top,enabled,0,1);
            enabled.Toggled+=(_,_)=>_=GuardAsync(async ()=> { addon.Enabled=enabled.IsOn; await store.SaveAsync(settings); RebuildItems(); });
            var buttons=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
            buttons.Children.Add(AsyncButton("Refresh",async ()=> { await RefreshAddonAsync(addon); if(currentTab=="Integrations") Navigate("Integrations"); }));
            buttons.Children.Add(AsyncButton("Replace install link",()=>InstallAddonAsync(addon)));
            if(addon.Manifest.Configurable) buttons.Children.Add(Button("Configure",()=>_=Windows.System.Launcher.LaunchUriAsync(StremioClient.ConfigureUri(addon))));
            buttons.Children.Add(AsyncButton("Remove",async ()=> { if(await DialogAsync("Remove addon?",Text($"Remove {addon.Manifest.Name} from this profile? Your media servers and playback history stay with you."),"Remove")!=ContentDialogResult.Primary) return; settings.Profile.Addons.Remove(addon); catalogs.Remove(addon.Id); await store.SaveAsync(settings); RebuildItems(); Navigate("Integrations"); }));
            foreach(var button in buttons.Children.OfType<Button>()) { button.MinHeight=34; button.FontSize=12; button.Padding=new Thickness(12,6,12,6); }
            var description=Text(addon.Manifest.Description,12); description.Opacity=.75; description.MaxLines=3; description.TextTrimming=TextTrimming.CharacterEllipsis;
            page.Children.Add(Card(Stack(top,description,buttons),new Thickness(22)));
        }
        page.Children.Add(BuildServers(true));
        return Scroll(page);
    }
    private async Task InstallAddonAsync(StremioAddon? original,string initial="")
    {
        var profile=settings.Profile; using var cancelled=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); bool accepted=false;
        var url=new PasswordBox { Header="Addon install link",PlaceholderText="https://addon.example/manifest.json",Password=initial!="" ? initial : original?.ManifestUrl??"",MinWidth=360 };
        PolishField(url); var message=Text("Paste the generated link after configuring your addon. Links are saved encrypted with this profile.",12); message.Opacity=.75;
        Uri? configureAddress=null; var configure=Button("Open addon configuration",()=> { if(configureAddress!=null) _=Windows.System.Launcher.LaunchUriAsync(configureAddress); }); configure.Visibility=Visibility.Collapsed;
        var dialog=new ContentDialog { XamlRoot=root.XamlRoot,Title=original==null ? "Add Stremio addon" : "Replace addon link",Content=Stack(url,message,configure),PrimaryButtonText=original==null ? "Add addon" : "Save link",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary,RequestedTheme=root.RequestedTheme };
        AddonManifest? manifest=null; Uri? normalized=null;
        dialog.PrimaryButtonClick+=async (_,args)=> {
            args.Cancel=true; var defer=args.GetDeferral(); dialog.IsPrimaryButtonEnabled=false; message.Text="Checking addon…";
            try {
                normalized=StremioClient.ManifestUri(url.Password); manifest=await new StremioClient(http).ManifestAsync(normalized.AbsoluteUri,cancelled.Token);
                if(manifest.ConfigurationRequired) { message.Text="This addon needs configuration. Open its configuration page and paste the completed install link."; configureAddress=StremioClient.ConfigureUri(new StremioAddon { ManifestUrl=normalized.AbsoluteUri }); configure.Visibility=Visibility.Visible; manifest=null; return; }
                cancelled.Token.ThrowIfCancellationRequested(); accepted=true; dialog.Hide();
            }
            catch(Exception e) when(e is not OutOfMemoryException) { message.Text=e is InvalidDataException ? e.Message : "Could not reach this addon. Check the link and try again."; }
            finally { dialog.IsPrimaryButtonEnabled=true; defer.Complete(); }
        };
        dialog.CloseButtonClick+=(_,_)=>cancelled.Cancel(); await ShowAddonDialogAsync(dialog);
        if(!accepted || manifest==null || normalized==null || !ReferenceEquals(profile,settings.Profile)) return;
        var duplicate=profile.Addons.FirstOrDefault(a=>a.ManifestUrl==normalized.AbsoluteUri && a.Id!=original?.Id);
        if(duplicate!=null) { status.Text="This addon is already connected."; return; }
        var addon=original??new StremioAddon(); addon.ManifestUrl=normalized.AbsoluteUri; addon.Manifest=manifest; addon.Updated=DateTimeOffset.UtcNow;
        if(original==null) profile.Addons.Add(addon); await store.SaveAsync(settings);
        status.Text="Loading addon catalogs…"; await RefreshAddonCatalogAsync(addon); RebuildItems(); if(currentTab=="Integrations") Navigate("Integrations");
    }
    private async Task ShowAddonDialogAsync(ContentDialog dialog)
    {
        foreach(var entry in root.Resources) dialog.Resources[entry.Key]=entry.Value;
        dialog.Foreground=FieldText(); dialog.Background=FieldFill(); dialog.BorderBrush=FieldStroke(); dialog.CornerRadius=new CornerRadius(18);
        dialog.Resources["ContentDialogBackground"]=FieldFill(); dialog.Resources["ContentDialogTopOverlay"]=FieldFill(); dialog.Resources["ContentDialogCornerRadius"]=new CornerRadius(18);
        var primaryStyle=new Style(typeof(Button)) { BasedOn=(Style)Application.Current.Resources["NullTVButtonStyle"],Setters={new Setter(Control.BackgroundProperty,new SolidColorBrush(Color("#BD93F9"))),new Setter(Control.ForegroundProperty,new SolidColorBrush(Color("#21222C"))) } };
        dialog.PrimaryButtonStyle=primaryStyle; dialog.CloseButtonStyle=(Style)Application.Current.Resources["NullTVButtonStyle"];
        foreach(var key in new[]{"AccentButtonBackground","AccentButtonBackgroundPointerOver","AccentButtonBackgroundPressed"}) dialog.Resources[key]=new SolidColorBrush(Color("#BD93F9"));
        foreach(var key in new[]{"AccentButtonForeground","AccentButtonForegroundPointerOver","AccentButtonForegroundPressed"}) dialog.Resources[key]=new SolidColorBrush(Color("#21222C"));
        dialog.Opened+=(_,_)=> { foreach(var name in new[]{"PrimaryButton","CloseButton"}) if(FindNamedButton(dialog,name) is { } button) { button.Template=(ControlTemplate)Application.Current.Resources["NullTVButtonTemplate"]; button.Background=name=="PrimaryButton" ? new SolidColorBrush(Color("#BD93F9")) : new SolidColorBrush(Color(RaisedHex)); button.Foreground=name=="PrimaryButton" ? new SolidColorBrush(Color("#21222C")) : FieldText(); button.Height=button.MinHeight=44; button.CornerRadius=new CornerRadius(10); } };
        dialogDepth++; playerOverlay?.Hide(); foreach(var tile in surfaces) tile.Hide(true);
        try { await dialog.ShowAsync(); }
        finally { dialogDepth--; foreach(var tile in surfaces) tile.Hide(!IsPlayerVisible || !active.ContainsKey(tile)); }
    }
    private async Task RefreshAddonAsync(StremioAddon addon)
    {
        var manifest=await new StremioClient(http).ManifestAsync(addon.ManifestUrl,lifetime.Token);
        if(manifest.ConfigurationRequired) throw new InvalidDataException("This addon needs a configured install link.");
        addon.Manifest=manifest; addon.Updated=DateTimeOffset.UtcNow; await store.SaveAsync(settings); await RefreshAddonCatalogAsync(addon); RebuildItems();
    }
    private async Task RefreshAddonCatalogAsync(StremioAddon addon)
    {
        var profile=settings.Profile; var entries=new List<MediaItem>(); var failed=0; using var gate=new SemaphoreSlim(4);
        var loaded=await Task.WhenAll(addon.Manifest.Catalogs.Select(async catalog=> {
            await gate.WaitAsync(lifetime.Token);
            try { return await new StremioClient(http).CatalogAsync(addon,catalog,ct:lifetime.Token); }
            catch(Exception e) when(e is not OutOfMemoryException) { Interlocked.Increment(ref failed); return new AddonPage([],0,false); }
            finally { gate.Release(); }
        }));
        foreach(var page in loaded) entries.AddRange(page.Items);
        if(!ReferenceEquals(profile,settings.Profile) || !profile.Addons.Contains(addon)) return;
        if(failed>0 && entries.Count==0 && catalogs.ContainsKey(addon.Id)) { status.Text="Addon is connected. Catalog refresh failed; your previous titles are preserved."; return; }
        var cache=new Catalog(entries,"",failed>0 ? "Some catalogs are unavailable" : "Connected",null); catalogs[addon.Id]=cache; await store.SaveCacheAsync(addon.Id,cache);
        status.Text=$"{addon.Manifest.Name} connected · {entries.Count:N0} catalog titles";
    }
}

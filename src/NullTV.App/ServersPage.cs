using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NullTV.Core;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private readonly SemaphoreSlim serverSyncGate=new(1);
    private async Task SyncStoppedAsync()
    {
        if(surface?.Player==null || playing==null) return;
        var s=settings.Profile.Sources.FirstOrDefault(s=>s.Id==playing.SourceId); if(s?.Kind is not (SourceKind.Jellyfin or SourceKind.Emby or SourceKind.Plex)) return;
        try { await new MediaServerClient(http).ProgressAsync(s,playing,surface.Player.Number("time-pos"),surface.Player.Number("duration"),true,lifetime.Token); } catch(Exception e) when(e is not OutOfMemoryException) { await log.WriteAsync("SERVER_SYNC_FAILED",e); }
    }
    private async Task SyncServerProgressAsync()
    {
        if(!await serverSyncGate.WaitAsync(0)) return;
        try { foreach(var (v,item) in active.ToArray()) { var s=settings.Profile.Sources.FirstOrDefault(s=>s.Id==item.SourceId); if(s?.Kind is not (SourceKind.Jellyfin or SourceKind.Emby or SourceKind.Plex) || v.Player==null) continue; await new MediaServerClient(http).ProgressAsync(s,item,v.Player.Number("time-pos"),v.Player.Number("duration"),false,lifetime.Token); } }
        finally { serverSyncGate.Release(); }
    }
    private UIElement BuildServers()
    {
        var page=Stack(Heading("Media servers","Bring your library. Keep your place."));
        foreach(var source in settings.Profile.Sources.Where(s=>s.Kind is SourceKind.Jellyfin or SourceKind.Emby or SourceKind.Plex))
        {
            var s=source; page.Children.Add(Stack(Text(s.Name,20),Text(s.AccountStatus,12),AsyncButton("Browse / refresh library",async ()=> { catalogs[s.Id]=await new MediaServerClient(http).BrowseAsync(s,lifetime.Token); await store.SaveCacheAsync(s.Id,catalogs[s.Id]); await store.SaveAsync(settings); RebuildItems(); Navigate("Movies & Series"); })));
        }
        var connectors=new Grid { ColumnSpacing=16 }; for(int i=0;i<3;i++) connectors.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var jelly=Stack(new FontIcon { Glyph="\uE968",FontSize=26,HorizontalAlignment=HorizontalAlignment.Left,Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#BD93F9")) },Text("Jellyfin",22),Text("Connect your own server with a login or API key.",13),Primary("Connect Jellyfin",ConnectServerAsync));
        var emby=Stack(new FontIcon { Glyph="\uE968",FontSize=26,HorizontalAlignment=HorizontalAlignment.Left,Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#50FA7B")) },Text("Emby",22),Text("Browse your libraries and sync watched progress.",13),AsyncButton("Connect Emby",()=>ConnectServerAsync(SourceKind.Emby)));
        var plex=Stack(new FontIcon { Glyph="\uE768",FontSize=26,HorizontalAlignment=HorizontalAlignment.Left,Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Color("#BD93F9")) },Text("Plex",22),Text("Sign in with a short code and connect your server.",13),AsyncButton("Sign in with PIN",ConnectPlexAsync));
        Place(connectors,Card(jelly,new Thickness(24))); Place(connectors,Card(emby,new Thickness(24)),0,1); Place(connectors,Card(plex,new Thickness(24)),0,2); page.Children.Add(connectors);
        var note=Text("Your libraries stay with you. NullTV connects to the servers you choose and remembers where you left off.",12); note.Opacity=.45; note.Margin=new Thickness(4,12,0,0); page.Children.Add(note); return Scroll(page);
    }
    private Task ConnectServerAsync() => ConnectServerAsync(SourceKind.Jellyfin);
    private async Task ConnectServerAsync(SourceKind serverKind)
    {
        var kind=Choice("Server",new[]{"Jellyfin","Emby"},serverKind.ToString()); var name=Field("Server name"); var address=Field("Server URL (include /emby if required)"); var user=Field("Username (for login)"); var password=new PasswordBox { Style=(Style)Application.Current.Resources["NullTVPasswordBoxStyle"],Foreground=FieldText(),Background=FieldFill(),BorderBrush=FieldStroke(),CornerRadius=new CornerRadius(10), Header="Password (for login)",MinHeight=68 }; var key=new PasswordBox { Style=(Style)Application.Current.Resources["NullTVPasswordBoxStyle"],Foreground=FieldText(),Background=FieldFill(),BorderBrush=FieldStroke(),CornerRadius=new CornerRadius(10), Header="API key (alternative)",MinHeight=68 }; var userId=Field("User ID (required for server API keys)");
        if(await DialogAsync("Connect media server",Scroll(Stack(kind,name,address,user,password,key,userId)),"Connect")!=ContentDialogResult.Primary) return;
        var s=new Source { Kind=Enum.Parse<SourceKind>((string)kind.SelectedItem),Name=name.Text.Trim(),Address=address.Text.Trim(),Username=user.Text,Password=password.Password,ApiKey=key.Password,UserId=userId.Text.Trim() }; if(s.Name=="") s.Name=s.Kind.ToString();
        var c=await new MediaServerClient(http).BrowseAsync(s,lifetime.Token); settings.Profile.Sources.Add(s); catalogs[s.Id]=c; s.Password=""; await store.SaveCacheAsync(s.Id,c); await store.SaveAsync(settings); RebuildItems(); Navigate("Movies & Series");
    }
    private async Task EditServerAsync(Source s)
    {
        var name=Field("Server name",s.Name); var address=Field("Server URL",s.Address); var key=new PasswordBox { Style=(Style)Application.Current.Resources["NullTVPasswordBoxStyle"],Foreground=FieldText(),Background=FieldFill(),BorderBrush=FieldStroke(),CornerRadius=new CornerRadius(10), Header="API token",Password=s.ApiKey }; var user=Field("User ID",s.UserId);
        if(await DialogAsync("Edit media server",Stack(name,address,key,user))!=ContentDialogResult.Primary) return;
        s.Name=name.Text.Trim(); s.Address=address.Text.Trim(); s.ApiKey=key.Password; s.UserId=user.Text.Trim(); await store.SaveAsync(settings); await RefreshSourceAsync(s);
    }
    private async Task ConnectPlexAsync()
    {
        var s=new Source { Kind=SourceKind.Plex,Name="Plex" }; var pin=await MediaServerClient.PlexPinAsync(http,s.Id,lifetime.Token);
        var code=Text(pin.Code,30); var address=Field("Plex server URL (your own server)"); var panel=Stack(Text("Open plex.tv/link and enter this code:"),code,Button("Open Plex link",()=>_=Windows.System.Launcher.LaunchUriAsync(new Uri("https://plex.tv/link"))),address);
        if(await DialogAsync("Plex sign-in",panel,"Finish sign-in")!=ContentDialogResult.Primary) return;
        s.ApiKey=await MediaServerClient.PollPlexPinAsync(http,s.Id,pin.Id,lifetime.Token)??throw new InvalidDataException("The PIN has not been authorized or has expired. Sign in in the browser and try again."); s.Address=address.Text.Trim(); var c=await new MediaServerClient(http).BrowseAsync(s,lifetime.Token); settings.Profile.Sources.Add(s); catalogs[s.Id]=c; await store.SaveCacheAsync(s.Id,c); await store.SaveAsync(settings); RebuildItems(); Navigate("Movies & Series");
    }
}

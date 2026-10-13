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
        var selected=settings.Profile.Sources.FirstOrDefault(source=>source.Id==serverBrowseId && source.Kind is SourceKind.Jellyfin or SourceKind.Emby or SourceKind.Plex);
        if(selected!=null) return BuildServerBrowser(selected);
        var page=Stack(Heading("Media servers","Bring your library. Keep your place."));
        foreach(var source in settings.Profile.Sources.Where(s=>s.Kind is SourceKind.Jellyfin or SourceKind.Emby or SourceKind.Plex))
        {
            var s=source; var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
            actions.Children.Add(Button("Browse libraries",()=>OpenServerBrowser(s)));
            actions.Children.Add(AsyncButton("Refresh",async ()=> { await RefreshServerCatalogAsync(s); OpenServerBrowser(s); }));
            page.Children.Add(Card(Stack(Text(s.Name,20),Text(s.AccountStatus,12),actions),new Thickness(20)));
        }
        var connectors=new Grid { ColumnSpacing=16 }; for(int i=0;i<3;i++) connectors.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        Border Connector(string name,string asset,string description,string action,Func<Task> connect)
        {
            var content=new Grid { RowSpacing=16,MinHeight=264 };
            content.RowDefinitions.Add(new() { Height=new(56) });
            content.RowDefinitions.Add(new() { Height=GridLength.Auto });
            content.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
            content.RowDefinitions.Add(new() { Height=GridLength.Auto });
            var uri=new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","Servers",asset));
            var logo=new Image { Height=48,MaxWidth=150,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Stretch=Microsoft.UI.Xaml.Media.Stretch.Uniform };
            logo.Source=asset.EndsWith(".svg") ? new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(uri) : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(logo,name+" logo"); Place(content,logo);
            var title=Text(name,22); title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; Place(content,title,1);
            var copy=Text(description,13); copy.Opacity=.7; Place(content,copy,2);
            var button=Primary(action,connect); button.Height=44; button.HorizontalAlignment=HorizontalAlignment.Stretch; button.HorizontalContentAlignment=HorizontalAlignment.Center; Place(content,button,3);
            return Card(content,new Thickness(24));
        }
        Place(connectors,Connector("Jellyfin","jellyfin.svg","Connect your own server with a login or API key.","Connect Jellyfin",ConnectServerAsync));
        Place(connectors,Connector("Emby","emby.png","Browse your libraries and sync watched progress.","Connect Emby",()=>ConnectServerAsync(SourceKind.Emby)),0,1);
        Place(connectors,Connector("Plex","plex.svg","Sign in with a short code and connect your server.","Sign in with PIN",ConnectPlexAsync),0,2); page.Children.Add(connectors);
        var note=Text("Your libraries stay with you. NullTV connects to the servers you choose and remembers where you left off.",12); note.Opacity=.45; note.Margin=new Thickness(4,12,0,0); page.Children.Add(note); return Scroll(page);
    }
    private Task ConnectServerAsync() => ConnectServerAsync(SourceKind.Jellyfin);
    private async Task ConnectServerAsync(SourceKind serverKind)
    {
        var kind=Choice("Server",new[]{"Jellyfin","Emby"},serverKind.ToString()); var name=Field("Server name"); var address=Field("Server URL (include /emby if required)"); var user=Field("Username (for login)"); var password=new PasswordBox { Style=(Style)Application.Current.Resources["NullTVPasswordBoxStyle"],Foreground=FieldText(),Background=FieldFill(),BorderBrush=FieldStroke(),CornerRadius=new CornerRadius(10), Header="Password (for login)",MinHeight=68 }; var key=new PasswordBox { Style=(Style)Application.Current.Resources["NullTVPasswordBoxStyle"],Foreground=FieldText(),Background=FieldFill(),BorderBrush=FieldStroke(),CornerRadius=new CornerRadius(10), Header="API key (alternative)",MinHeight=68 }; var userId=Field("User ID (required for server API keys)");
        if(await DialogAsync("Connect media server",Scroll(Stack(kind,name,address,user,password,key,userId)),"Connect")!=ContentDialogResult.Primary) return;
        var s=new Source { Kind=Enum.Parse<SourceKind>((string)kind.SelectedItem),Name=name.Text.Trim(),Address=address.Text.Trim(),Username=user.Text,Password=password.Password,ApiKey=key.Password,UserId=userId.Text.Trim() }; if(s.Name=="") s.Name=s.Kind.ToString();
        var c=await new MediaServerClient(http).BrowseAsync(s,lifetime.Token); settings.Profile.Sources.Add(s); catalogs[s.Id]=c; s.Password=""; await store.SaveCacheAsync(s.Id,c); await store.SaveAsync(settings); RebuildItems(); OpenServerBrowser(s);
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
        s.ApiKey=await MediaServerClient.PollPlexPinAsync(http,s.Id,pin.Id,lifetime.Token)??throw new InvalidDataException("The PIN has not been authorized or has expired. Sign in in the browser and try again."); s.Address=address.Text.Trim(); var c=await new MediaServerClient(http).BrowseAsync(s,lifetime.Token); settings.Profile.Sources.Add(s); catalogs[s.Id]=c; await store.SaveCacheAsync(s.Id,c); await store.SaveAsync(settings); RebuildItems(); OpenServerBrowser(s);
    }
}

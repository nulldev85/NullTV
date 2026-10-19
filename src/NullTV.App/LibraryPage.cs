using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using NullTV.Core;
namespace NullTV.App;
public sealed partial class MainWindow
{
    private sealed class PosterRow(MediaItem item) : ArtworkRow(item)
    {
        private string state="";
        public string State=>state;
        public string Kind=>Item.Kind switch { MediaKind.Series=>"SERIES",MediaKind.Episode=>"EPISODE",MediaKind.Folder=>"COLLECTION",_=>"MOVIE" };
        public void Update(WatchState? history) { var next=history is null ? "" : history.Watched ? "Watched" : history.Position>0 ? $"Resume {TimeSpan.FromSeconds(history.Position):hh\\:mm\\:ss}" : ""; if(state==next) return; state=next; Changed(nameof(State)); }
    }
    private string librarySearchText="",libraryMode="All",libraryCategory="All categories",libraryProfile="";
    private UIElement BuildLibrary()
    {
        if(libraryProfile!=settings.Profile.Id) { libraryProfile=settings.Profile.Id; librarySearchText=""; libraryMode="All"; libraryCategory="All categories"; }
        var page=new Grid { RowSpacing=18 }; page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
        if((playing??lastPlayed)?.Kind is MediaKind.Movie or MediaKind.Episode) actions.Children.Add(Button("Now playing",()=>Navigate("Now Playing")));
        actions.Children.Add(AsyncButton("+  Add source",()=>EditSourceAsync(null))); Place(page,Heading("Movies & series","Your library. Ready when you are.",actions));
        var search=SearchBox("Search your library"); search.MinWidth=180; search.Text=librarySearchText;
        var kind=Choice("",new[]{"All","Movies","Series","Continue watching","Watched"},libraryMode); kind.MinWidth=160;
        var category=Choice("",new[]{"All categories"}.Concat(items.Where(i=>i.Kind!=MediaKind.Live).Select(i=>i.Category).Distinct().Order()),libraryCategory); category.MaxWidth=260; category.MinWidth=140;
        var controls=new Grid { ColumnSpacing=10 }; controls.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); controls.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); controls.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); Place(controls,search); Place(controls,kind,0,1); Place(controls,category,0,2); var filters=Card(controls,new Thickness(0)); filters.BorderThickness=new Thickness(0); filters.Background=null; Place(page,filters,1);
        var list=new GridView { IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.None };
        list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='172' Spacing='10' Margin='4,8,12,12'><Border Height='248' CornerRadius='12' Background='{ThemeResource NullTVFieldBackground}'><Grid><StackPanel VerticalAlignment='Center' HorizontalAlignment='Center' Spacing='12'><FontIcon Glyph='&#xE8B2;' FontSize='28' Opacity='.3'/><TextBlock Text='{Binding Kind}' FontSize='10' CharacterSpacing='160' Opacity='.4'/></StackPanel><Image Source='{Binding Artwork}' Stretch='UniformToFill'/></Grid></Border><TextBlock Text='{Binding Name}' FontSize='13' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding State}' Opacity='.55' FontSize='11'/></StackPanel></DataTemplate>");
        QuietItems(list); WatchArtwork(list,344);
        list.ItemClick+=(_,e)=> { if(e.ClickedItem is PosterRow row) _=GuardAsync(()=>MediaDetailsAsync(row.Item)); }; Place(page,list,2);
        var empty=Card(EmptyState("\uE8B2","Your library belongs here.","Connect your media server or an account with movies and series.","Connect a media server",()=> { Navigate("Media Servers"); return Task.CompletedTask; })); Place(page,empty,2);
        CancellationTokenSource? pending=null;
        async Task PopulateAsync(bool debounce)
        {
            pending?.Cancel(); pending?.Dispose(); var request=pending=new(); var ct=request.Token;
            try
            {
                if(debounce) await Task.Delay(180,ct);
                var term=search.Text; var group=category.SelectedItem as string??"All categories"; var mode=kind.SelectedItem as string??"All";
                librarySearchText=term; libraryCategory=group; libraryMode=mode;
                var catalog=items.ToArray(); var history=new Dictionary<string,WatchState>(settings.Profile.History);
                var found=await Task.Run(()=>catalog.Where(i=>i.Kind is MediaKind.Movie or MediaKind.Series && i.Name.Contains(term,StringComparison.OrdinalIgnoreCase) && (group=="All categories" || i.Category==group) && (mode=="All" || mode=="Movies" && i.Kind==MediaKind.Movie || mode=="Series" && i.Kind==MediaKind.Series || mode=="Watched" && history.GetValueOrDefault(i.Id)?.Watched==true || mode=="Continue watching" && history.GetValueOrDefault(i.Id) is { Watched:false,Position:>0 })).ToArray(),ct);
                ct.ThrowIfCancellationRequested(); list.ItemsSource=found.Select(GetPosterRow).ToArray();
                var hasLibrary=catalog.Any(i=>i.Kind is MediaKind.Movie or MediaKind.Series); filters.Visibility=hasLibrary ? Visibility.Visible : Visibility.Collapsed; empty.Visibility=found.Length==0 ? Visibility.Visible : Visibility.Collapsed;
                if(found.Length==0 && hasLibrary) empty.Child=EmptyState("\uE721","No matching titles.","Try a different category or search.","Clear search",()=> { search.Text=""; kind.SelectedItem="All"; category.SelectedItem="All categories"; return Task.CompletedTask; });
            }
            catch(OperationCanceledException) { }
        }
        search.TextChanged+=(sender,e)=> { _=GuardAsync(()=>PopulateAsync(true)); }; kind.SelectionChanged+=(_,_)=>_=GuardAsync(()=>PopulateAsync(false)); category.SelectionChanged+=(_,_)=>_=GuardAsync(()=>PopulateAsync(false));
        refreshLibrary=()=>_=GuardAsync(()=>PopulateAsync(false)); list.Unloaded+=(_,_)=>pending?.Cancel(); _=GuardAsync(()=>PopulateAsync(false)); return page;
    }
    private async Task MediaDetailsAsync(MediaItem item)
    {
        var source=settings.Profile.Sources.FirstOrDefault(s=>s.Id==item.SourceId); if(source==null) return;
        MediaDetails details=source.Kind==SourceKind.Xtream ? await new XtreamClient(http).DetailsAsync(source,item,lifetime.Token) : await new MediaServerClient(http).DetailsAsync(source,item,lifetime.Token);
        var panel=Stack(Text(item.Name,24),Text(details.Plot=="" ? "No plot supplied by the server." : details.Plot),Text($"Cast: {details.Cast}\nRating: {details.Rating}",12));
        panel.Children.Add(AsyncButton("Toggle watched",async ()=> { var h=settings.Profile.History.GetValueOrDefault(item.Id)??new(0,0,false,DateTimeOffset.UtcNow); settings.Profile.History[item.Id]=h with { Watched=!h.Watched }; await store.SaveAsync(settings); if(source.Kind is SourceKind.Jellyfin or SourceKind.Emby or SourceKind.Plex) await new MediaServerClient(http).WatchedAsync(source,item,!h.Watched,lifetime.Token); status.Text="Watched state updated."; }));
        if(details.Episodes.Count>0)
        {
            var episodes=new ListView { ItemsSource=details.Episodes,IsItemClickEnabled=true,MaxHeight=350 }; MediaItem? selected=null; episodes.ItemClick+=(_,e)=>selected=e.ClickedItem as MediaItem; panel.Children.Add(Text("Select an episode, then press Play",12)); panel.Children.Add(episodes);
            if(await DialogAsync("Series details",Scroll(panel),"Play")==ContentDialogResult.Primary && selected!=null) { if(!items.Any(i=>i.Id==selected.Id)) items.Add(selected); await PlayServerMediaAsync(source,selected); }
        }
        else if(await DialogAsync(item.Kind==MediaKind.Episode ? "Episode details" : "Movie details",Scroll(panel),"Play")==ContentDialogResult.Primary && item.Kind!=MediaKind.Series) await PlayServerMediaAsync(source,item);
    }
    private async Task PlayServerMediaAsync(Source source,MediaItem item)
    {
        if(source.Kind is SourceKind.Jellyfin or SourceKind.Emby)
        {
            status.Text="Loading playback versions…";
            var versions=await new MediaServerClient(http).PlaybackVersionsAsync(source,item,lifetime.Token);
            if(versions.Count==1) item=versions[0].Item;
            else
            {
                var choice=Choice("Version",versions.Select((version,index)=>$"{index+1}. {version.Name}"),$"1. {versions[0].Name}");
                var description=Text(versions[0].Description,12); description.Opacity=.75;
                choice.SelectionChanged+=(_,_)=> { if(choice.SelectedIndex>=0) description.Text=versions[choice.SelectedIndex].Description; };
                if(await DialogAsync("Choose playback version",Scroll(Stack(Text(item.Name,22),choice,description)),"Play")!=ContentDialogResult.Primary) return;
                item=versions[choice.SelectedIndex].Item;
            }
        }
        await PlayAsync(item);
    }
}

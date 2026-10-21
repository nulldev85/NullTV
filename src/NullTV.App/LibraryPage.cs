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
    private string librarySearchText="",libraryMode="All",libraryProfile="";
    private string libraryAddonCatalog="",libraryAddonGenre="";
    private sealed record CatalogChoice(StremioAddon? Addon,AddonCatalog? Catalog,string Label,string Category="")
    {
        public string Key=>Addon==null || Catalog==null ? Category=="" ? "" : "category:"+Category : Addon.Id+":"+Catalog.Type+":"+Catalog.Id;
        public override string ToString()=>Label;
    }
    private UIElement BuildLibrary()
    {
        if(libraryProfile!=settings.Profile.Id) { libraryProfile=settings.Profile.Id; librarySearchText=""; libraryMode="All"; libraryAddonCatalog=""; libraryAddonGenre=""; }
        var profile=settings.Profile; var page=new Grid { RowSpacing=18 }; page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); page.RowDefinitions.Add(new() { Height=GridLength.Auto });
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
        if((playing??lastPlayed)?.Kind is MediaKind.Movie or MediaKind.Episode) actions.Children.Add(Button("Now playing",()=>Navigate("Now Playing")));
        actions.Children.Add(AsyncButton("+  Add source",()=>EditSourceAsync(null))); actions.Children.Add(Button("Integrations",()=> { serverBrowseId=""; Navigate("Integrations"); })); Place(page,Heading("Movies & series","Your library and addon catalogs. One place to explore.",actions));
        var search=SearchBox("Search your library and addons"); search.MinWidth=160; search.Text=librarySearchText;
        var kind=Choice("",new[]{"All","Movies","Series","Continue watching","Watched"},libraryMode); kind.Width=140;
        var catalogChoice=new ComboBox { MinWidth=180,MaxWidth=280,HorizontalAlignment=HorizontalAlignment.Stretch }; PolishField(catalogChoice);
        var choices=new List<CatalogChoice> { new(null,null,"All libraries") };
        choices.AddRange(items.Where(i=>i.Kind is MediaKind.Movie or MediaKind.Series && profile.Sources.Any(s=>s.Id==i.SourceId)).Select(i=>i.Category).Distinct().Order().Select(category=>new CatalogChoice(null,null,category,category)));
        choices.AddRange(profile.Addons.Where(a=>a.Enabled).SelectMany(a=>a.Manifest.Catalogs.Select(c=>new CatalogChoice(a,c,a.Manifest.Name+" · "+c.Name+" · "+(c.Type=="series" ? "Series" : "Movies")))));
        foreach(var choice in choices) catalogChoice.Items.Add(choice); catalogChoice.SelectedItem=choices.FirstOrDefault(c=>c.Key==libraryAddonCatalog)??choices[0];
        var genre=Choice("",new[]{"All genres"},"All genres"); genre.Width=140; genre.Visibility=Visibility.Collapsed;
        var controls=new Grid { ColumnSpacing=10 }; controls.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); for(int n=0;n<3;n++) controls.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); Place(controls,search); Place(controls,kind,0,1); Place(controls,catalogChoice,0,2); Place(controls,genre,0,3); Place(page,controls,1);
        var list=new GridView { IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.None };
        list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='172' Spacing='10' Margin='4,8,12,12'><Border Height='248' CornerRadius='14' BorderThickness='.5' BorderBrush='#6044475A' Background='{ThemeResource NullTVFieldBackground}'><Grid><StackPanel VerticalAlignment='Center' HorizontalAlignment='Center' Spacing='12'><FontIcon Glyph='&#xE8B2;' FontSize='28' Opacity='.3'/><TextBlock Text='{Binding Kind}' FontSize='10' CharacterSpacing='160' Opacity='.4'/></StackPanel><Image Source='{Binding Artwork}' Stretch='UniformToFill'/></Grid></Border><TextBlock Text='{Binding Name}' FontSize='13' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding State}' Opacity='.55' FontSize='11'/></StackPanel></DataTemplate>");
        QuietItems(list); WatchArtwork(list,344); list.ItemClick+=(_,e)=> { if(e.ClickedItem is PosterRow row) _=GuardAsync(()=>MediaDetailsAsync(row.Item)); }; Place(page,list,2);
        var empty=EmptyState("\uE8B2","Find your next watch.","Connect a media server or a Stremio catalog addon in Integrations.","Open integrations",()=> { serverBrowseId=""; Navigate("Integrations"); return Task.CompletedTask; }); Place(page,empty,2);
        var footer=new Grid(); footer.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var note=Text("",11); note.Opacity=.65; note.VerticalAlignment=VerticalAlignment.Center; Place(footer,note);
        var more=Button("Load more",()=>{}); more.Visibility=Visibility.Collapsed; Place(footer,more,0,1); Place(page,footer,3);
        CancellationTokenSource? pending=null; var current=new List<MediaItem>(); var next=0; bool changing=false;
        void Genres()
        {
            changing=true; var chosen=(CatalogChoice)catalogChoice.SelectedItem; var options=chosen.Catalog?.Extras.FirstOrDefault(e=>e.Name=="genre")?.Options??[];
            genre.Items.Clear(); if(chosen.Catalog?.Extras.Any(e=>e.Name=="genre" && e.Required)!=true) genre.Items.Add("All genres"); foreach(var option in options) genre.Items.Add(option);
            genre.SelectedItem=genre.Items.Contains(libraryAddonGenre) ? libraryAddonGenre : genre.Items.Count>0 ? genre.Items[0] : null; genre.Visibility=options.Count>0 ? Visibility.Visible : Visibility.Collapsed; changing=false;
        }
        IEnumerable<MediaItem> Filter(IEnumerable<MediaItem> entries,string term,string mode,string category="",Dictionary<string,WatchState>? history=null)=>entries.Where(i=>i.Kind is MediaKind.Movie or MediaKind.Series && (term=="" || i.Name.Contains(term,StringComparison.OrdinalIgnoreCase)) && (category=="" || i.Category==category) && (mode=="All" || mode=="Movies" && i.Kind==MediaKind.Movie || mode=="Series" && i.Kind==MediaKind.Series || mode=="Watched" && (history??profile.History).GetValueOrDefault(i.Id)?.Watched==true || mode=="Continue watching" && (history??profile.History).GetValueOrDefault(i.Id) is { Watched:false,Position:>0 }));
        void Show()
        {
            list.ItemsSource=current.DistinctBy(i=>i.Id).Select(GetPosterRow).ToArray(); empty.Visibility=current.Count==0 ? Visibility.Visible : Visibility.Collapsed;
            if(current.Count==0 && (profile.Sources.Count>0 || profile.Addons.Count>0)) { empty.Visibility=Visibility.Collapsed; note.Text="No matching titles. Try another catalog or search."; }
        }
        async Task PopulateAsync(bool debounce,bool append=false)
        {
            pending?.Cancel(); pending?.Dispose(); var request=pending=new(); var ct=request.Token;
            try {
                if(debounce) await Task.Delay(250,ct); if(!ReferenceEquals(profile,settings.Profile)) return;
                var term=search.Text.Trim(); var mode=(string)kind.SelectedItem; var choice=(CatalogChoice)catalogChoice.SelectedItem; var selectedGenre=genre.SelectedItem as string??""; if(selectedGenre=="All genres") selectedGenre="";
                librarySearchText=term; libraryMode=mode; libraryAddonCatalog=choice.Key; libraryAddonGenre=selectedGenre;
                more.Visibility=Visibility.Collapsed; more.IsEnabled=false; note.Text="";
                if(!append) { next=0; var snapshot=items.ToArray(); var history=new Dictionary<string,WatchState>(profile.History); var found=choice.Addon==null ? await Task.Run(()=>Filter(snapshot,term,mode,choice.Category,history).ToList(),ct) : []; ct.ThrowIfCancellationRequested(); current=found; Show(); }
                var client=new StremioClient(http);
                if(choice.Addon!=null && choice.Catalog!=null)
                {
                    note.Text="Loading catalog…"; var result=await client.CatalogAsync(choice.Addon,choice.Catalog,term,next,selectedGenre,ct); ct.ThrowIfCancellationRequested();
                    next+=result.Received; current.AddRange(Filter(result.Items,choice.Catalog.Supports("search") ? "" : term,mode)); Show(); more.Visibility=result.HasMore ? Visibility.Visible : Visibility.Collapsed; note.Text=$"{current.DistinctBy(i=>i.Id).Count():N0} titles · {choice.Addon.Manifest.Name}";
                }
                else if(term!="" && choice.Category=="" && profile.Addons.Any(a=>a.Enabled))
                {
                    note.Text="Searching addon catalogs…"; using var gate=new SemaphoreSlim(4);
                    var results=await Task.WhenAll(choices.Where(c=>c.Catalog?.Supports("search")==true && (mode is not ("Movies" or "Series") || c.Catalog.Type==(mode=="Movies" ? "movie" : "series"))).Select(async c=> {
                        await gate.WaitAsync(ct); try { var result=await client.CatalogAsync(c.Addon!,c.Catalog!,term,ct:ct); return result.Items; } catch(Exception e) when(e is not OutOfMemoryException && !ct.IsCancellationRequested) { return []; } finally { gate.Release(); }
                    }));
                    ct.ThrowIfCancellationRequested(); foreach(var result in results) current.AddRange(Filter(result,"",mode)); Show(); note.Text=$"{current.DistinctBy(i=>i.Id).Count():N0} titles";
                }
                else if(current.Count>0) note.Text=$"{current.DistinctBy(i=>i.Id).Count():N0} titles · Choose an addon catalog to browse more";
            }
            catch(OperationCanceledException) { }
            catch(Exception e) when(e is not OutOfMemoryException) { if(!ct.IsCancellationRequested) note.Text=e is InvalidDataException ? e.Message : "This catalog could not load. Try another catalog or refresh the addon in Integrations."; }
            finally { if(ReferenceEquals(pending,request)) more.IsEnabled=true; }
        }
        Genres(); more.Click+=(_,_)=>_=GuardAsync(()=>PopulateAsync(false,true));
        search.TextChanged+=(_,_)=>_=GuardAsync(()=>PopulateAsync(true)); kind.SelectionChanged+=(_,_)=>_=GuardAsync(()=>PopulateAsync(false));
        catalogChoice.SelectionChanged+=(_,_)=> { Genres(); _=GuardAsync(()=>PopulateAsync(false)); }; genre.SelectionChanged+=(_,_)=> { if(!changing) _=GuardAsync(()=>PopulateAsync(false)); };
        page.Unloaded+=(_,_)=>pending?.Cancel(); refreshLibrary=()=>_=GuardAsync(()=>PopulateAsync(false)); _=GuardAsync(()=>PopulateAsync(false)); return page;
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

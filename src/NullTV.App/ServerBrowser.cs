using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;

namespace NullTV.App;

public sealed partial class MainWindow
{
    private string serverBrowseId="",serverLibraryId="",serverGenre="",serverSearch="",serverSort="Server order";
    private int serverOffset;
    private readonly List<int> serverPageStarts=[];
    private readonly List<(string Id,string Name)> serverFolders=[];
    private void OpenServerBrowser(Source source)
    {
        serverBrowseId=source.Id; serverLibraryId=""; serverGenre=""; serverSearch=""; ResetServerPaging(); serverFolders.Clear(); Navigate("Media Servers");
    }
    private async Task RefreshServerCatalogAsync(Source source)
    {
        var catalog=await new MediaServerClient(http).BrowseAsync(source,lifetime.Token);
        if(!settings.Profile.Sources.Contains(source)) return;
        catalogs[source.Id]=catalog; await store.SaveCacheAsync(source.Id,catalog); await store.SaveAsync(settings); RebuildItems();
    }
    private void ResetServerPaging() { serverOffset=0; serverPageStarts.Clear(); }
    private void OpenServerLibrary(ServerLibrary library,MediaItem? folder=null)
    {
        serverLibraryId=library.Id; serverGenre=library.DefaultGenre; serverSearch=""; serverSort="Server order"; ResetServerPaging(); serverFolders.Clear(); if(folder!=null) serverFolders.Add((folder.ProviderId,folder.Name)); Navigate("Media Servers");
    }
    private GridView ServerPosters(IEnumerable<MediaItem> entries,Source source,Action<MediaItem>? folder=null,bool horizontal=false,bool landscape=false)
    {
        var list=new GridView { IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.None };
        list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='152' Spacing='8' Margin='2,6,10,8'><Border Height='212' CornerRadius='14' BorderThickness='.5' BorderBrush='#6044475A' Background='{ThemeResource NullTVFieldBackground}'><Grid><StackPanel VerticalAlignment='Center' HorizontalAlignment='Center' Spacing='10'><FontIcon Glyph='&#xE8B2;' FontSize='25' Opacity='.3'/><TextBlock Text='{Binding Kind}' FontSize='10' Opacity='.5'/></StackPanel><Image Source='{Binding Artwork}' Stretch='UniformToFill'/></Grid></Border><TextBlock Text='{Binding Name}' FontSize='13' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding State}' Opacity='.6' FontSize='11'/></StackPanel></DataTemplate>");
        QuietItems(list); WatchArtwork(list,304);
        if(landscape) list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='248' Spacing='8' Margin='2,6,10,8'><Border Height='140' CornerRadius='14' BorderThickness='.5' BorderBrush='#6044475A' Background='{ThemeResource NullTVFieldBackground}'><Grid><FontIcon Glyph='&#xE8B2;' FontSize='25' Opacity='.3'/><Image Source='{Binding Artwork}' Stretch='UniformToFill'/></Grid></Border><TextBlock Text='{Binding Name}' FontSize='13' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding State}' Opacity='.6' FontSize='11'/></StackPanel></DataTemplate>");
        if(horizontal)
        {
            list.Height=landscape ? 216 : 286; list.ItemsPanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel Orientation='Horizontal'/></ItemsPanelTemplate>");
            ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Auto); ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        }
        list.ItemsSource=entries.Select(GetPosterRow).ToArray();
        list.ItemClick+=(_,args)=> { if(args.ClickedItem is not PosterRow row) return; if(row.Item.Kind==MediaKind.Folder && folder!=null) folder(row.Item); else _=GuardAsync(()=>MediaDetailsAsync(row.Item)); };
        return list;
    }
    private UIElement BuildServerBrowser(Source source)
    {
        var catalog=catalogs.GetValueOrDefault(source.Id);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
        actions.Children.Add(AsyncButton("Refresh",async ()=> { await RefreshServerCatalogAsync(source); if(currentTab=="Media Servers" && serverBrowseId==source.Id) Navigate("Media Servers"); }));
        actions.Children.Add(Button("All servers",()=> { serverBrowseId=""; Navigate("Media Servers"); }));
        if(serverLibraryId!="" && catalog?.Libraries.FirstOrDefault(library=>library.Id==serverLibraryId) is { } selectedLibrary) return BuildServerCollection(source,selectedLibrary,actions);
        var page=new Grid { RowSpacing=12 }; page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        Place(page,Heading(source.Name,$"{source.Kind}  ·  Your libraries, in their server order.",actions));
        var content=new StackPanel { Spacing=24 };
        if(catalog==null || source.Kind is SourceKind.Jellyfin or SourceKind.Emby && !catalog.HasServerLibraries)
        {
            var loading=Text("Loading your server libraries…",14); content.Children.Add(loading);
            var retry=AsyncButton("Retry",async ()=> { await RefreshServerCatalogAsync(source); Navigate("Media Servers"); }); retry.Visibility=Visibility.Collapsed; content.Children.Add(retry);
            page.Loaded+=async (_,_)=> {
                try { await RefreshServerCatalogAsync(source); if(currentTab=="Media Servers" && serverBrowseId==source.Id && serverLibraryId=="") Navigate("Media Servers"); }
                catch(Exception e) when(e is not OutOfMemoryException) { loading.Text=Friendly(e); retry.Visibility=Visibility.Visible; }
            };
        }
        else
        {
            foreach(var row in catalog.ServerRows)
            {
                var title=Text(row.Name,20); title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
                content.Children.Add(Stack(title,ServerPosters(row.Items,source,horizontal:true,landscape:true)));
            }
            foreach(var library in catalog.Libraries)
            {
                var heading=Heading(library.Name,library.GenreRequired ? library.DefaultGenre.Equals("None",StringComparison.OrdinalIgnoreCase) ? "All titles" : $"Genre · {library.DefaultGenre}" : library.CollectionType=="tvshows" ? "Series" : library.CollectionType=="movies" ? "Movies" : "Library",Button("View all",()=>OpenServerLibrary(library))); heading.Margin=new Thickness(0);
                content.Children.Add(Stack(heading,library.Items.Count>0 ? ServerPosters(library.Items.Take(16),source,item=>OpenServerLibrary(library,item),true) : Card(Stack(Text(library.GenreRequired ? "Choose a genre to explore this library." : "This library has no titles to show yet.",13),Button("Browse library",()=>OpenServerLibrary(library))),new Thickness(16))));
            }
            if(catalog.Libraries.Count==0 && catalog.Items.Count>0) content.Children.Add(ServerPosters(catalog.Items.Where(item=>item.Kind is MediaKind.Movie or MediaKind.Series),source));
            if(content.Children.Count==0) content.Children.Add(Card(Stack(Text("No accessible video libraries.",20),Text("The server is connected, but it has not exposed a video library for this user. Refresh or check this user's libraries in the server app.",13)),new Thickness(20)));
        }
        Place(page,Scroll(content),1); return page;
    }
    private UIElement BuildServerCollection(Source source,ServerLibrary library,StackPanel actions)
    {
        var page=new Grid { RowSpacing=14 }; page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) }); page.RowDefinitions.Add(new() { Height=GridLength.Auto });
        var back=Button(serverFolders.Count>0 ? "Back" : "Libraries",()=> { if(serverFolders.Count>0) serverFolders.RemoveAt(serverFolders.Count-1); else serverLibraryId=""; ResetServerPaging(); serverSearch=""; Navigate("Media Servers"); }); actions.Children.Insert(0,back);
        var title=serverFolders.LastOrDefault().Name??library.Name; var parent=serverFolders.LastOrDefault().Id??library.Id;
        Place(page,Heading(title,source.Name+"  ·  "+library.Name,actions));
        var search=SearchBox("Search this library"); search.Text=serverSearch; search.MinWidth=260;
        var genreValues=new Dictionary<string,string>(); var all=library.Genres.FirstOrDefault(name=>name.Equals("None",StringComparison.OrdinalIgnoreCase));
        if(!library.GenreRequired || all!=null) genreValues["All genres"]=all??"";
        foreach(var name in library.Genres.Where(name=>!name.Equals("None",StringComparison.OrdinalIgnoreCase))) genreValues[name]=name;
        var genre=Choice("",genreValues.Keys,serverGenre=="" || serverGenre.Equals("None",StringComparison.OrdinalIgnoreCase) ? "All genres" : serverGenre); genre.MinWidth=150; genre.MaxWidth=220; genre.Visibility=library.Genres.Count>0 && serverFolders.Count==0 ? Visibility.Visible : Visibility.Collapsed;
        var sort=Choice("",source.Kind==SourceKind.Plex ? new[]{"Server order","Title A–Z"} : new[]{"Server order","Title A–Z","Newest"},serverSort); sort.MinWidth=150;
        var controls=new Grid { ColumnSpacing=12 }; controls.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); controls.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); controls.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); Place(controls,search); Place(controls,genre,0,1); Place(controls,sort,0,2); Place(page,controls,1);
        void OpenFolder(MediaItem item) { serverFolders.Add((item.ProviderId,item.Name)); ResetServerPaging(); serverSearch=""; Navigate("Media Servers"); }
        var list=ServerPosters([],source,OpenFolder); Place(page,list,2);
        var message=Text("Loading titles…",14); message.HorizontalAlignment=HorizontalAlignment.Center; message.VerticalAlignment=VerticalAlignment.Center; Place(page,message,2);
        var footer=new Grid { ColumnSpacing=10 }; footer.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var count=Text("",12); count.Opacity=.7; count.VerticalAlignment=VerticalAlignment.Center; Place(footer,count);
        CancellationTokenSource? pending=null; bool unloaded=false; int nextOffset=0;
        Button previous=null!,next=null!;
        previous=Button("Previous",()=> { if(serverPageStarts.Count>0) { serverOffset=serverPageStarts[^1]; serverPageStarts.RemoveAt(serverPageStarts.Count-1); } else serverOffset=0; _=GuardAsync(()=>PopulateAsync(false)); });
        next=Button("Next",()=> { serverPageStarts.Add(serverOffset); serverOffset=nextOffset; _=GuardAsync(()=>PopulateAsync(false)); }); previous.IsEnabled=next.IsEnabled=false; Place(footer,previous,0,1); Place(footer,next,0,2); Place(page,footer,3);
        async Task PopulateAsync(bool debounce)
        {
            pending?.Cancel(); pending?.Dispose(); var request=pending=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var ct=request.Token;
            try
            {
                if(debounce) await Task.Delay(250,ct);
                if(unloaded) return;
                previous.IsEnabled=next.IsEnabled=false; message.Text="Loading titles…"; message.Visibility=Visibility.Visible;
                serverSearch=search.Text.Trim(); serverGenre=genreValues.GetValueOrDefault(genre.SelectedItem as string??"")??""; serverSort=sort.SelectedItem as string??"Server order";
                ServerPage result;
                if(source.Kind==SourceKind.Plex)
                {
                    IEnumerable<MediaItem> found=library.Items.Where(item=>item.Name.Contains(serverSearch,StringComparison.OrdinalIgnoreCase)); if(serverSort=="Title A–Z") found=found.OrderBy(item=>item.Name); var matching=found.ToArray(); result=new(matching.Skip(serverOffset).Take(60).ToList(),serverOffset,matching.Length,serverOffset+60<matching.Length);
                }
                else result=await new MediaServerClient(http).LibraryPageAsync(source,parent,library.Name,serverFolders.Count==0 ? serverGenre : "",serverSearch,serverSort,serverOffset,ct);
                ct.ThrowIfCancellationRequested(); if(unloaded) return;
                foreach(var (id,state) in result.RemoteHistory) if(!settings.Profile.History.TryGetValue(id,out var local) || state.Updated>local.Updated) settings.Profile.History[id]=state;
                list.ItemsSource=result.Items.Select(GetPosterRow).ToArray();
                foreach(var item in result.Items) if(!items.Any(existing=>existing.Id==item.Id)) items.Add(item);
                nextOffset=serverOffset+result.Items.Count; previous.IsEnabled=serverOffset>0; next.IsEnabled=result.HasMore;
                count.Text=result.Items.Count==0 ? "No titles" : $"{serverOffset+1:N0}–{nextOffset:N0}"+(result.Total is { } total ? $" of {total:N0}" : " titles");
                message.Text="No titles match this search or genre."; message.Visibility=result.Items.Count==0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch(OperationCanceledException) { }
            catch(Exception e) when(e is not OutOfMemoryException) { if(!unloaded) { message.Text=Friendly(e); message.Visibility=Visibility.Visible; previous.IsEnabled=serverOffset>0; } }
        }
        search.TextChanged+=(_,_)=> { ResetServerPaging(); _=GuardAsync(()=>PopulateAsync(true)); }; genre.SelectionChanged+=(_,_)=> { ResetServerPaging(); _=GuardAsync(()=>PopulateAsync(false)); }; sort.SelectionChanged+=(_,_)=> { ResetServerPaging(); _=GuardAsync(()=>PopulateAsync(false)); };
        page.Loaded+=(_,_)=>_=GuardAsync(()=>PopulateAsync(false)); page.Unloaded+=(_,_)=> { unloaded=true; pending?.Cancel(); }; return page;
    }
}

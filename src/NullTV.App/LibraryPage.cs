using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using NullTV.Core;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private sealed record PosterRow(MediaItem Item,string Name,string Category,string State);
    private UIElement BuildLibrary()
    {
        var page=new Grid { RowSpacing=16 }; page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        Place(page,Heading("Movies & series","Your collection, with your place saved.",AsyncButton("+  Add source",()=>EditSourceAsync(null))));
        var search=new AutoSuggestBox { PlaceholderText="Search movies and series",MinHeight=44,Width=300 }; var kind=Choice("Show",new[]{"All","Movies","Series","Continue watching","Watched"},"All"); var category=Choice("Category",new[]{"All categories"}.Concat(items.Where(i=>i.Kind!=MediaKind.Live).Select(i=>i.Category).Distinct().Order()),"All categories");
        var controls=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12 }; controls.Children.Add(search); controls.Children.Add(kind); controls.Children.Add(category); var filters=Card(controls,new Thickness(16)); Place(page,filters,1);
        var list=new GridView { IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.None };
        list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='164' Spacing='8' Margin='4'><Border Height='232' CornerRadius='12' Background='#24221F'><Image Stretch='UniformToFill'/></Border><TextBlock Text='{Binding Name}' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding State}' Opacity='.6' FontSize='11'/></StackPanel></DataTemplate>");
        list.ContainerContentChanging+=async (_,a)=> { var c=a.ItemContainer; var image=FindImage(c.ContentTemplateRoot); if(image!=null) image.Source=null; if(a.InRecycleQueue || a.Item is not PosterRow r || imageCache==null) return; try { var path=await imageCache.GetAsync(r.Item.Logo,lifetime.Token); if(path!=null && ReferenceEquals(c.Content,r) && image!=null) { var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path); using var stream=await file.OpenReadAsync(); var bitmap=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth=328 }; await bitmap.SetSourceAsync(stream); if(ReferenceEquals(c.Content,r)) image.Source=bitmap; } } catch(Exception e) when(e is not OutOfMemoryException) { } };
        list.ItemClick+=(_,e)=> { if(e.ClickedItem is PosterRow r) _=GuardAsync(()=>MediaDetailsAsync(r.Item)); }; Place(page,list,2);
        var empty=Card(EmptyState("\uE8B2","A home for your collection.","Connect your media server or add an account with movies and series. Pick up where you left off, on this screen.","Connect a media server",()=> { Navigate("Media Servers"); return Task.CompletedTask; })); Place(page,empty,2);
        void Populate() { list.ItemsSource=items.Where(i=>i.Kind!=MediaKind.Live && i.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase) && ((string)category.SelectedItem=="All categories" || i.Category==(string)category.SelectedItem) && ((string)kind.SelectedItem=="All" || (string)kind.SelectedItem=="Movies" && i.Kind==MediaKind.Movie || (string)kind.SelectedItem=="Series" && i.Kind==MediaKind.Series || (string)kind.SelectedItem=="Watched" && settings.Profile.History.GetValueOrDefault(i.Id)?.Watched==true || (string)kind.SelectedItem=="Continue watching" && settings.Profile.History.GetValueOrDefault(i.Id) is { Watched:false,Position:>0 })).Select(i=>new PosterRow(i,i.Name,i.Category,settings.Profile.History.GetValueOrDefault(i.Id) is { } h ? h.Watched ? "✓ Watched" : $"Resume {TimeSpan.FromSeconds(h.Position):hh\\:mm\\:ss}" : i.Kind.ToString())).ToList(); var hasLibrary=items.Any(i=>i.Kind!=MediaKind.Live); filters.Visibility=hasLibrary ? Visibility.Visible : Visibility.Collapsed; empty.Visibility=list.Items.Count==0 ? Visibility.Visible : Visibility.Collapsed; if(list.Items.Count==0 && hasLibrary) { empty.Child=EmptyState("\uE721","No matching titles.","Try another category or search term.","Reset filters",()=> { search.Text=""; kind.SelectedItem="All"; category.SelectedItem="All categories"; return Task.CompletedTask; }); } }
        search.TextChanged+=(_,_)=>Populate(); kind.SelectionChanged+=(_,_)=>Populate(); category.SelectionChanged+=(_,_)=>Populate(); Populate(); return page;
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
            if(await DialogAsync("Series details",Scroll(panel),"Play")==ContentDialogResult.Primary && selected!=null) { if(!items.Any(i=>i.Id==selected.Id)) items.Add(selected); await PlayAsync(selected); }
        }
        else if(await DialogAsync("Movie details",Scroll(panel),"Play")==ContentDialogResult.Primary && item.Kind!=MediaKind.Series) await PlayAsync(item);
    }
}

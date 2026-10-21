using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private async Task<MediaDetails> DetailsForAsync(MediaItem item,CancellationToken ct)
    {
        var source=settings.Profile.Sources.FirstOrDefault(s=>s.Id==item.SourceId);
        if(source?.Kind==SourceKind.Xtream) return await new XtreamClient(http).DetailsAsync(source,item,ct);
        if(source?.Kind is SourceKind.Jellyfin or SourceKind.Emby or SourceKind.Plex) return await new MediaServerClient(http).DetailsAsync(source,item,ct);
        var addon=settings.Profile.Addons.Where(a=>item.ContentId!="" && a.Enabled && a.Manifest.Supports("meta",AddonIdentity.Type(item),item.ContentId)).OrderByDescending(a=>a.Id==item.SourceId).FirstOrDefault();
        if(addon!=null) return await new StremioClient(http).MetadataAsync(addon,item,ct);
        return new(item.Overview,item.Cast,item.Rating,[]) { ContentId=item.ContentId,Poster=item.Logo,Backdrop=item.Backdrop,Year=item.Year,Runtime=item.Runtime,Genres=item.Genres };
    }
    private async Task MediaDetailsAsync(MediaItem original)
    {
        var origin=currentTab; var profile=settings.Profile; var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var ct=cancel.Token;
        var page=new Grid { RowSpacing=22 }; page.RowDefinitions.Add(new() { Height=GridLength.Auto }); page.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        var back=Button("‹  Back to browse",()=>Navigate(origin)); back.HorizontalAlignment=HorizontalAlignment.Left; Place(page,back);
        var content=new StackPanel { Spacing=22 }; var hero=new Grid { ColumnSpacing=28 }; hero.ColumnDefinitions.Add(new() { Width=new(196) }); hero.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });
        var posterRow=GetPosterRow(original); var poster=new Image { Stretch=Stretch.UniformToFill }; poster.SetBinding(Image.SourceProperty,new Binding { Source=posterRow,Path=new PropertyPath(nameof(ArtworkRow.Artwork)) });
        var posterCard=Card(poster,new Thickness(0)); posterCard.Width=196; posterCard.Height=290; posterCard.VerticalAlignment=VerticalAlignment.Top; Place(hero,posterCard); _=LoadArtworkAsync(posterRow,392);
        var kind=Text(original.Kind==MediaKind.Series ? "SERIES" : original.Kind==MediaKind.Episode ? "EPISODE" : "MOVIE",10); kind.CharacterSpacing=140; kind.Foreground=new SolidColorBrush(Color("#F1FA8C"));
        var title=Text(original.Name,30); title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        var facts=Text(original.Year,12); facts.Foreground=new SolidColorBrush(Color("#BD93F9"));
        var plot=Text(original.Overview=="" ? "Loading title details…" : original.Overview,14); plot.LineHeight=23;
        var cast=Text("",12); cast.Opacity=.7;
        var choose=Primary("Choose a source",()=>Task.CompletedTask); choose.IsEnabled=false; choose.HorizontalAlignment=HorizontalAlignment.Left;
        var watched=AsyncButton("Mark watched",async ()=> {
            var history=profile.History.GetValueOrDefault(original.Id)??new(0,0,false,DateTimeOffset.UtcNow); profile.History[original.Id]=history with { Watched=!history.Watched }; await store.SaveAsync(settings);
            if(profile.Sources.FirstOrDefault(s=>s.Id==original.SourceId) is { Kind:SourceKind.Jellyfin or SourceKind.Emby or SourceKind.Plex } source) await new MediaServerClient(http).WatchedAsync(source,original,!history.Watched,lifetime.Token);
        });
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Spacing=10 }; buttons.Children.Add(choose); buttons.Children.Add(watched);
        var copy=Stack(kind,title,facts,plot,cast,buttons); copy.Spacing=12; copy.VerticalAlignment=VerticalAlignment.Center; Place(hero,copy,0,1); content.Children.Add(hero);
        var episodeHost=new StackPanel { Spacing=10 }; content.Children.Add(episodeHost); var loading=Text("Loading playback options…",12); loading.Opacity=.65; content.Children.Add(loading);
        var scroll=Scroll(content); Place(page,scroll,1);
        foreach(var child in pageHost.Children.Where(child=>child!=livePage).ToArray()) pageHost.Children.Remove(child); if(livePage!=null) livePage.Visibility=Visibility.Collapsed; pageHost.Children.Add(page);
        var loaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); page.Loaded+=(_,_)=>loaded.TrySetResult(); if(page.IsLoaded) loaded.TrySetResult(); page.Unloaded+=(_,_)=> { cancel.Cancel(); cancel.Dispose(); };
        MediaDetails details;
        try { using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(22)); details=await DetailsForAsync(original,timeout.Token); }
        catch(Exception e) when(e is not OutOfMemoryException && !ct.IsCancellationRequested) { details=new(original.Overview,original.Cast,original.Rating,[]) { ContentId=original.ContentId,Year=original.Year }; loading.Text="Some title details could not load. Available playback sources are still shown."; }
        if(ct.IsCancellationRequested || !ReferenceEquals(profile,settings.Profile)) return;
        var item=original with { ContentId=details.ContentId!="" ? details.ContentId : original.ContentId,Overview=details.Plot,Year=details.Year!="" ? details.Year : original.Year };
        if(details.Poster!="" && details.Poster!=original.Logo) { var row=GetPosterRow(item with { Logo=details.Poster }); poster.SetBinding(Image.SourceProperty,new Binding { Source=row,Path=new PropertyPath(nameof(ArtworkRow.Artwork)) }); _=LoadArtworkAsync(row,392); }
        plot.Text=details.Plot=="" ? "No summary supplied by this source." : details.Plot;
        facts.Text=string.Join("  ·  ",new[]{item.Year,details.Runtime,details.Rating=="" ? "" : "Rating "+details.Rating,string.Join(" / ",details.Genres.Take(3))}.Where(value=>value!="")); cast.Text=details.Cast=="" ? "" : "Cast · "+details.Cast;
        var episodes=details.Episodes.Select(episode=>episode with { SeriesContentId=item.ContentId }).ToList();
        MediaItem? selected=item.Kind==MediaKind.Series ? episodes.FirstOrDefault(episode=>profile.History.TryGetValue(episode.Id,out var h) && h.Position>0 && !h.Watched)??episodes.FirstOrDefault(episode=>episode.Season>0)??episodes.FirstOrDefault() : item;
        choose.IsEnabled=selected!=null; choose.Click+=(_,_)=> { if(selected!=null) _=GuardAsync(()=>ShowMediaSourcesAsync(item,selected,episodes,ct)); };
        if(item.Kind==MediaKind.Series)
        {
            var seasonValues=episodes.Select(e=>e.Season).Distinct().OrderBy(s=>s??int.MaxValue).ToArray(); string SeasonLabel(int? season)=>season is null ? "Other episodes" : season==0 ? "Specials" : "Season "+season; var seasons=Choice("Season",seasonValues.Select(SeasonLabel),SeasonLabel(selected?.Season)); seasons.MaxWidth=220; episodeHost.Children.Add(seasons);
            var list=new ListView { SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=true,MaxHeight=400 }; QuietItems(list);
            void FillEpisodes() { var season=seasons.SelectedIndex>=0 ? seasonValues[seasons.SelectedIndex] : null; list.ItemsSource=episodes.Where(e=>e.Season==season).Select(e=>new EpisodeChoice(e)).ToArray(); }
            FillEpisodes(); seasons.SelectionChanged+=(_,_)=>FillEpisodes();
            list.ItemClick+=(_,args)=> { if(args.ClickedItem is EpisodeChoice episode) { selected=episode.Item; _=GuardAsync(()=>ShowMediaSourcesAsync(item,episode.Item,episodes,ct)); } }; episodeHost.Children.Add(list);
            if(episodes.Count==0) loading.Text="This source did not provide an episode list. Refresh the catalog or check the series metadata addon."; else loading.Visibility=Visibility.Collapsed;
        }
        else loading.Visibility=Visibility.Collapsed;
        if(!page.IsLoaded) await loaded.Task.WaitAsync(ct); if(selected!=null && !ct.IsCancellationRequested && currentTab==origin) await ShowMediaSourcesAsync(item,selected,episodes,ct);
    }
    private sealed record EpisodeChoice(MediaItem Item)
    {
        public override string ToString()=>Item.Season is { } season && Item.Episode is { } episode ? $"S{season:00} E{episode:00}  ·  {Item.Name}" : Item.Name;
    }
    private async Task ShowMediaSourcesAsync(MediaItem parent,MediaItem initial,List<MediaItem> episodes,CancellationToken pageToken)
    {
        if(dialogDepth>0 || pageToken.IsCancellationRequested) return;
        using var closedDialog=CancellationTokenSource.CreateLinkedTokenSource(pageToken); CancellationTokenSource? searchRequest=null;
        var profile=settings.Profile; var selected=initial; MediaItem? chosen=null; string external="";
        var heading=Text(parent.Name,22); heading.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        var explanation=Text("Choose a playback source. Results are supplied by your connected addons and media servers.",12); explanation.Opacity=.7;
        var body=new StackPanel { Spacing=12 }; var scroll=Scroll(body); scroll.MaxHeight=420;
        var container=Stack(heading,explanation); container.MinWidth=440;
        ComboBox? episodeChoice=null;
        if(episodes.Count>0) { episodeChoice=new ComboBox { Header="Episode",HorizontalAlignment=HorizontalAlignment.Stretch }; PolishField(episodeChoice); foreach(var episode in episodes) episodeChoice.Items.Add(new EpisodeChoice(episode)); episodeChoice.SelectedItem=episodeChoice.Items.Cast<EpisodeChoice>().FirstOrDefault(e=>e.Item.Id==initial.Id); container.Children.Add(episodeChoice); }
        container.Children.Add(scroll);
        var dialog=new ContentDialog { XamlRoot=root.XamlRoot,Content=container,CloseButtonText="Close",CloseButtonStyle=(Style)Application.Current.Resources["NullTVButtonStyle"],RequestedTheme=root.RequestedTheme,CornerRadius=new CornerRadius(18) };
        dialog.Resources["ContentDialogMaxWidth"]=680d; dialog.Resources["ContentDialogBackground"]=FieldFill(); dialog.Resources["ContentDialogTopOverlay"]=FieldFill();
        void Offer(StackPanel group,string name,string description,Func<Task> activate,bool enabled=true)
        {
            var title=Text(name,14); title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; var detail=Text(description,12); detail.Opacity=.72; detail.MaxLines=5; detail.TextTrimming=TextTrimming.CharacterEllipsis;
            var button=AsyncButton("",activate); button.Content=Stack(title,detail); button.HorizontalAlignment=HorizontalAlignment.Stretch; button.HorizontalContentAlignment=HorizontalAlignment.Stretch; button.Padding=new Thickness(16,12,16,12); button.IsEnabled=enabled; button.Background=PanelBrush(); button.BorderBrush=SoftStroke(); button.BorderThickness=new Thickness(.5); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,name+" "+description); group.Children.Add(button);
        }
        string Identity()
        {
            if(selected.ContentId!="" && profile.Addons.Any(a=>a.Id==selected.SourceId)) return selected.ContentId;
            var id=profile.AddonTitleMappings.GetValueOrDefault(parent.Id,parent.ContentId);
            return AddonIdentity.VideoId(selected,id);
        }
        async Task LoadAsync()
        {
            searchRequest?.Cancel(); searchRequest?.Dispose(); var request=searchRequest=CancellationTokenSource.CreateLinkedTokenSource(closedDialog.Token); var ct=request.Token;
            body.Children.Clear(); var tasks=new List<Task>();
            if(profile.Sources.FirstOrDefault(s=>s.Id==selected.SourceId) is { } source && selected.Kind!=MediaKind.Series)
            {
                var local=new StackPanel { Spacing=8 }; local.Children.Add(Text(source.Name,13)); var state=Text("Loading library sources…",12); state.Opacity=.65; local.Children.Add(state); body.Children.Add(local); var item=selected;
                tasks.Add(LoadLocalAsync());
                async Task LoadLocalAsync()
                {
                    try {
                        var versions=source.Kind is SourceKind.Jellyfin or SourceKind.Emby ? await new MediaServerClient(http).PlaybackVersionsAsync(source,item,ct) : item.Url!="" ? [new PlaybackVersion(item.Id,"Play from "+source.Name,item.Name,item)] : new List<PlaybackVersion>();
                        if(ct.IsCancellationRequested) return; state.Visibility=Visibility.Collapsed;
                        foreach(var version in versions) Offer(local,version.Name,version.Description,()=> { chosen=version.Item; dialog.Hide(); return Task.CompletedTask; });
                        if(versions.Count==0) { state.Visibility=Visibility.Visible; state.Text="No library stream was supplied."; }
                    } catch(Exception e) when(e is not OutOfMemoryException) { if(!ct.IsCancellationRequested) state.Text="This library source is unavailable. You can still choose an addon result."; }
                }
            }
            var identity=Identity(); var addons=profile.Addons.Where(a=>a.Enabled).ToArray();
            if(identity=="" && addons.Length>0)
            {
                var matching=new StackPanel { Spacing=10 }; matching.Children.Add(Text("Match this title to search addons",15)); matching.Children.Add(Text("Select the correct title from an installed catalog, or enter its IMDb ID. Library playback remains available above.",12));
                var query=SearchBox("Search addon catalogs"); query.Text=parent.Name; matching.Children.Add(query); var matches=new StackPanel { Spacing=6 }; matching.Children.Add(matches);
                matching.Children.Add(AsyncButton("Find matching titles",async ()=> {
                    matches.Children.Clear(); var label=Text("Searching catalogs…",12); matches.Children.Add(label);
                    try {
                        var found=new List<MediaItem>(); using var gate=new SemaphoreSlim(4);
                        var results=await Task.WhenAll(addons.SelectMany(a=>a.Manifest.Catalogs.Where(c=>c.Type==AddonIdentity.Type(parent) && c.Supports("search")).Select(c=>(Addon:a,Catalog:c))).Select(async entry=> { await gate.WaitAsync(ct); try { return (await new StremioClient(http).CatalogAsync(entry.Addon,entry.Catalog,query.Text.Trim(),ct:ct)).Items; } catch(Exception e) when(e is not OutOfMemoryException && !ct.IsCancellationRequested) { return new List<MediaItem>(); } finally { gate.Release(); } }));
                        if(ct.IsCancellationRequested) return; matches.Children.Clear(); foreach(var result in results) found.AddRange(result);
                        foreach(var match in found.DistinctBy(i=>i.Id).Take(24)) Offer(matches,match.Name,match.Year+" · "+match.Category,async ()=> { profile.AddonTitleMappings[parent.Id]=match.ContentId; await store.SaveAsync(settings); await LoadAsync(); });
                        if(found.Count==0) matches.Children.Add(Text("No matches. Add a searchable metadata catalog such as Cinemeta, or enter the IMDb ID below.",12));
                    } catch(OperationCanceledException) { }
                }));
                var imdb=Field("IMDb ID",""); matching.Children.Add(imdb); var matchingError=Text("",12); matchingError.Foreground=new SolidColorBrush(Color("#F1FA8C")); matching.Children.Add(matchingError); matching.Children.Add(AsyncButton("Use IMDb ID",async ()=> { var id=AddonIdentity.Imdb(imdb.Text.Trim()); if(id=="") { matchingError.Text="Enter an IMDb ID beginning with tt followed by digits."; return; } profile.AddonTitleMappings[parent.Id]=id; await store.SaveAsync(settings); await LoadAsync(); })); body.Children.Add(Card(matching,new Thickness(16)));
            }
            using var concurrency=new SemaphoreSlim(4);
            foreach(var addon in addons.Where(a=>identity!="" && a.Manifest.Supports("stream",AddonIdentity.Type(selected),identity)))
            {
                var group=new StackPanel { Spacing=8 }; var label=Text(addon.Manifest.Name,13); label.Foreground=new SolidColorBrush(Color("#BD93F9")); group.Children.Add(label); var state=Text("Searching…",12); state.Opacity=.65; group.Children.Add(state); body.Children.Add(group);
                tasks.Add(LoadAddonAsync(addon,group,state));
            }
            async Task LoadAddonAsync(StremioAddon addon,StackPanel group,TextBlock state)
            {
                try {
                    await concurrency.WaitAsync(ct); List<AddonStream> results; try { results=await new StremioClient(http).StreamsAsync(addon,AddonIdentity.Type(selected),identity,ct); } finally { concurrency.Release(); }
                    if(ct.IsCancellationRequested) return; state.Text=results.Count==0 ? "No sources for this title." : $"{results.Count} sources";
                    foreach(var result in results)
                    {
                        var reason=result.Unsupported; if(result.InfoHash!="") reason="Torrent result · a streaming service is required.";
                        Offer(group,result.Name,result.Description+(reason=="" ? "" : "\n"+reason),()=> {
                            if(result.Url=="" && result.ExternalUrl!="") external=result.ExternalUrl;
                            else chosen=selected with { Name=parent.Kind==MediaKind.Series ? parent.Name+" · "+selected.Name : selected.Name,SourceId=addon.Id,Category=addon.Manifest.Name+" · "+result.Name,Url=result.Url,Headers=result.Headers,Subtitles=result.Subtitles,MediaSourceId="",PlaySessionId="" };
                            dialog.Hide(); return Task.CompletedTask;
                        },reason=="");
                    }
                }
                catch(OperationCanceledException) { }
                catch(Exception e) when(e is not OutOfMemoryException) { if(ct.IsCancellationRequested) return; state.Text="Could not reach this addon."; group.Children.Add(AsyncButton("Retry",LoadAsync)); }
            }
            if(addons.Length==0) body.Children.Add(Text("Connect Stremio addons in Integrations to see more playback sources here.",13));
            else if(identity!="" && !addons.Any(a=>a.Manifest.Supports("stream",AddonIdentity.Type(selected),identity))) body.Children.Add(Text("No enabled addon provides streams for this title ID. Connect a compatible stream addon in Integrations.",13));
            await Task.WhenAll(tasks);
        }
        dialog.Opened+=(_,_)=>_=GuardAsync(LoadAsync);
        if(episodeChoice!=null) episodeChoice.SelectionChanged+=(_,_)=> { if(episodeChoice.SelectedItem is EpisodeChoice choice) { selected=choice.Item; _=GuardAsync(LoadAsync); } };
        try { await ShowAddonDialogAsync(dialog); }
        finally { closedDialog.Cancel(); searchRequest?.Cancel(); searchRequest?.Dispose(); }
        if(pageToken.IsCancellationRequested || !ReferenceEquals(profile,settings.Profile)) return;
        if(external!="") { await Windows.System.Launcher.LaunchUriAsync(new Uri(external)); return; }
        if(chosen!=null) { if(!items.Any(i=>i.Id==chosen.Id)) items.Add(chosen); await PlayAsync(chosen); }
    }
}

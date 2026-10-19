namespace NullTV.Core;

public enum ChannelScope { Favorites,Recent,All,Category }
public sealed record ChannelSelection(ChannelScope Scope,string SourceId="",string Category="")
{
    public string Key=>$"{Scope}\u001f{SourceId}\u001f{Category}";
    public string Title=>Scope switch { ChannelScope.Favorites=>"Favorites",ChannelScope.Recent=>"Recently watched",ChannelScope.All=>"All channels",_=>Category };
}
public sealed record ChannelCategory(string SourceId,string SourceName,string Name,int Count);
public static class ChannelBrowse
{
    public static MediaItem[] Select(IEnumerable<MediaItem> catalog,Profile profile,ChannelSelection selection)
    {
        var channels=catalog.Where(i=>i.Kind==MediaKind.Live).DistinctBy(i=>i.Id);
        return selection.Scope switch {
            ChannelScope.Favorites=>FavoriteGuide.Channels(channels,profile),
            ChannelScope.Recent=>channels.Where(i=>profile.History.ContainsKey(i.Id)).OrderByDescending(i=>profile.History[i.Id].Updated).ToArray(),
            ChannelScope.Category=>channels.Where(i=>i.SourceId==selection.SourceId && i.Category==selection.Category).ToArray(),
            _=>channels.ToArray()
        };
    }
    public static ChannelCategory[] Categories(IEnumerable<MediaItem> catalog,IEnumerable<Source> sources)
    {
        var names=sources.ToDictionary(s=>s.Id,s=>s.Name);
        return catalog.Where(i=>i.Kind==MediaKind.Live).DistinctBy(i=>i.Id).GroupBy(i=>(i.SourceId,i.Category))
            .Select(g=>new ChannelCategory(g.Key.SourceId,names.GetValueOrDefault(g.Key.SourceId,"IPTV"),g.Key.Category,g.Count()))
            .OrderBy(g=>g.SourceName,StringComparer.CurrentCultureIgnoreCase).ThenBy(g=>g.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

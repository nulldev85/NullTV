namespace NullTV.Core;

public static class FavoriteGuide
{
    public static MediaItem[] Channels(IEnumerable<MediaItem> catalog,Profile profile)
    {
        var ranks=profile.FavoriteGuideOrder.Distinct(StringComparer.Ordinal)
            .Select((id,index)=>(id,index)).ToDictionary(x=>x.id,x=>x.index,StringComparer.Ordinal);
        return catalog.Where(item=>item.Kind==MediaKind.Live && profile.Favorites.Contains(item.Id))
            .DistinctBy(item=>item.Id,StringComparer.Ordinal)
            .OrderBy(item=>ranks.GetValueOrDefault(item.Id,int.MaxValue)).ToArray();
    }

    // Replace only the positions represented by the visible list. Hidden search
    // results keep their places, and temporarily unavailable favorites keep their IDs.
    public static void SaveOrder(Profile profile,IEnumerable<MediaItem> catalog,IEnumerable<string> visibleOrder)
    {
        var channels=Channels(catalog,profile).Select(item=>item.Id).ToArray();
        var available=channels.ToHashSet(StringComparer.Ordinal);
        var reordered=visibleOrder.Where(available.Contains).Distinct(StringComparer.Ordinal).ToArray();
        var visible=reordered.ToHashSet(StringComparer.Ordinal);
        var queue=new Queue<string>(reordered);
        var result=channels.Select(id=>visible.Contains(id) ? queue.Dequeue() : id);
        var unavailable=profile.FavoriteGuideOrder.Where(id=>profile.Favorites.Contains(id) && !available.Contains(id));
        profile.FavoriteGuideOrder=result.Concat(unavailable).Distinct(StringComparer.Ordinal).ToList();
    }
}

namespace NullTV.Core;

/// <summary>An immutable per-channel guide index, shared by search and programme lookup.</summary>
public sealed class ProgrammeIndex
{
    private readonly Dictionary<string,Programme[]> channels;
    public ProgrammeIndex(IEnumerable<Programme> programmes) => channels=programmes.GroupBy(p=>p.ChannelId).ToDictionary(g=>g.Key,g=>g.OrderBy(p=>p.Start).ToArray());
    public (Programme? Now,Programme? Next) NowNext(string channelId,DateTimeOffset time)
    {
        if(!channels.TryGetValue(channelId,out var shows)) return (null,null);
        int low=0,high=shows.Length;
        while(low<high) { int mid=(low+high)/2; if(shows[mid].Start<=time) low=mid+1; else high=mid; }
        var current=low>0 && shows[low-1].End>time ? shows[low-1] : null;
        return (current,low<shows.Length ? shows[low] : null);
    }
    public IEnumerable<Programme> Window(string channelId,DateTimeOffset start,DateTimeOffset end)
    {
        if(!channels.TryGetValue(channelId,out var shows)) yield break;
        int low=0,high=shows.Length;
        while(low<high) { int mid=(low+high)/2; if(shows[mid].Start<start) low=mid+1; else high=mid; }
        for(int i=Math.Max(0,low-1);i<shows.Length && shows[i].Start<end;i++) if(shows[i].End>start) yield return shows[i];
    }
}

using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NullTV.Core;

namespace NullTV.App;
public abstract class ArtworkRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed(string name)=>PropertyChanged?.Invoke(this,new(name));
    public MediaItem Item { get; }
    public string Name=>Item.Name;
    private ImageSource? artwork;
    public ImageSource? Artwork { get=>artwork; internal set { if(ReferenceEquals(artwork,value)) return; artwork=value; Changed(nameof(Artwork)); } }
    internal bool Loading;
    internal int Pins;
    internal DateTimeOffset RetryAt;
    protected ArtworkRow(MediaItem item)=>Item=item;
}
public sealed partial class MainWindow
{
    private readonly Dictionary<string,BitmapImage> artworkMemory=[];
    private readonly LinkedList<string> artworkLru=[];
    private readonly Dictionary<string,LinkedListNode<string>> artworkNodes=[];
    private readonly Dictionary<string,List<ArtworkRow>> artworkWaiters=[];
    private readonly Dictionary<string,List<WeakReference<ArtworkRow>>> artworkOwners=[];
    private readonly Dictionary<string,PosterRow> posterRows=[];
    private async Task LoadArtworkAsync(ArtworkRow row,int width)
    {
        if(row.Artwork!=null || row.Loading || row.Item.Logo=="" || imageCache==null || row.RetryAt>DateTimeOffset.UtcNow) return;
        var key=$"{width}:{row.Item.Logo}";
        if(artworkMemory.TryGetValue(key,out var cached)) { AssignArtwork(key,row,cached); TouchArtwork(key); return; }
        row.Loading=true;
        if(artworkWaiters.TryGetValue(key,out var existing)) { existing.Add(row); return; }
        artworkWaiters[key]=[row]; BitmapImage? bitmap=null;
        try
        {
            var path=await imageCache.GetAsync(row.Item.Logo,lifetime.Token); if(path==null || closed) return;
            var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path); using var stream=await file.OpenReadAsync();
            bitmap=new BitmapImage { DecodePixelWidth=width }; await bitmap.SetSourceAsync(stream);
            artworkMemory[key]=bitmap; TouchArtwork(key);
        }
        catch(Exception e) when(e is not OutOfMemoryException) { }
        finally
        {
            if(artworkWaiters.Remove(key,out var waiting)) foreach(var target in waiting) { target.Loading=false; if(bitmap!=null) AssignArtwork(key,target,bitmap); else target.RetryAt=DateTimeOffset.UtcNow.AddSeconds(20); }
            TrimArtworkMemory();
        }
    }
    private void TouchArtwork(string key) { if(artworkNodes.Remove(key,out var previous)) artworkLru.Remove(previous); artworkNodes[key]=artworkLru.AddLast(key); }
    private void AssignArtwork(string key,ArtworkRow row,BitmapImage bitmap) { row.Artwork=bitmap; if(!artworkOwners.TryGetValue(key,out var owners)) artworkOwners[key]=owners=[]; if(!owners.Any(w=>w.TryGetTarget(out var value) && ReferenceEquals(value,row))) owners.Add(new(row)); }
    private void TrimArtworkMemory()
    {
        var remaining=artworkLru.Count;
        while(artworkMemory.Count>96 && remaining-->0 && artworkLru.First is { } first)
        {
            var owners=artworkOwners.GetValueOrDefault(first.Value)??[];
            if(owners.Any(w=>w.TryGetTarget(out var row) && row.Pins>0)) { TouchArtwork(first.Value); continue; }
            foreach(var weak in owners) if(weak.TryGetTarget(out var row)) row.Artwork=null;
            artworkOwners.Remove(first.Value); artworkMemory.Remove(first.Value); artworkNodes.Remove(first.Value); artworkLru.RemoveFirst();
        }
    }
    private void WatchArtwork(Microsoft.UI.Xaml.Controls.ListViewBase list,int width)
    {
        var pinned=new Dictionary<Microsoft.UI.Xaml.Controls.Primitives.SelectorItem,ArtworkRow>();
        list.ContainerContentChanging+=(sender,args)=>
        {
            if(args.InRecycleQueue) { if(pinned.Remove(args.ItemContainer,out var old)) old.Pins=Math.Max(0,old.Pins-1); return; }
            if(args.Item is not ArtworkRow row) return;
            if(!pinned.TryGetValue(args.ItemContainer,out var current) || !ReferenceEquals(current,row)) { if(current!=null) current.Pins=Math.Max(0,current.Pins-1); pinned[args.ItemContainer]=row; row.Pins++; }
            _=LoadArtworkAsync(row,width);
        };
        list.Unloaded+=(_,_)=> { foreach(var row in pinned.Values) row.Pins=Math.Max(0,row.Pins-1); pinned.Clear(); TrimArtworkMemory(); };
    }
    private PosterRow GetPosterRow(MediaItem item)
    {
        if(!posterRows.TryGetValue(item.Id,out var row) || row.Item.Logo!=item.Logo || row.Item.Name!=item.Name) posterRows[item.Id]=row=new(item);
        row.Update(settings.Profile.History.GetValueOrDefault(item.Id)); return row;
    }
}

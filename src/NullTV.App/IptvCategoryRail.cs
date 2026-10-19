using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private sealed record RailEntry(string Label,string Glyph,int Count,ChannelSelection? Selection,string SourceId="");
    private sealed class IptvCategoryRail : Grid
    {
        private readonly MainWindow app;
        private readonly ListView list=new() { SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true };
        private readonly Button toggle;
        private readonly HashSet<string> folded=[];
        private readonly Action<ChannelSelection> selected;
        private readonly Action<bool> resized;
        private ChannelSelection selection;
        public bool Collapsed { get; private set; }
        public IptvCategoryRail(MainWindow app,ChannelSelection selection,bool collapsed,Action<ChannelSelection> selected,Action<bool> resized)
        {
            this.app=app; this.selection=selection; this.selected=selected; this.resized=resized; Collapsed=collapsed;
            Background=new SolidColorBrush(Color("#21222C")); BorderBrush=SoftStroke(); BorderThickness=new Thickness(0,0,.5,0);
            RowDefinitions.Add(new() { Height=new(40) }); RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
            toggle=app.IconButton("\uE76B","Collapse IPTV categories",Toggle); toggle.MinHeight=toggle.Height=30; toggle.Width=32; toggle.HorizontalAlignment=HorizontalAlignment.Right; toggle.Margin=new Thickness(8,4,8,4); Place(this,toggle);
            QuietItems(list); list.Margin=new Thickness(6,0,6,0);
            list.ItemContainerStyle=new Style(typeof(ListViewItem)) { Setters={new Setter(Control.PaddingProperty,new Thickness(0)),new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch),new Setter(Control.MinHeightProperty,42d)} };
            list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='42'/></DataTemplate>");
            list.ContainerContentChanging+=(_,args)=> {
                if(args.InRecycleQueue || args.Item is not RailEntry entry || args.ItemContainer.ContentTemplateRoot is not Grid host) return;
                host.Children.Clear(); var active=entry.Selection?.Key==this.selection.Key;
                var content=new Grid { ColumnSpacing=10,Padding=new Thickness(10,0,10,0),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,2,0,2),Background=new SolidColorBrush(Color(active ? "#BD93F9" : "#21222C")) };
                content.ColumnDefinitions.Add(new() { Width=new(20) }); content.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
                var ink=new SolidColorBrush(Color(active ? "#282A36" : "#F8F8F2"));
                Place(content,new FontIcon { Glyph=entry.Glyph,FontSize=entry.Selection==null ? 11 : 18,Foreground=ink,Opacity=active ? 1 : .78 });
                var label=app.Text(entry.Label,entry.Selection==null ? 10 : 13); label.Foreground=ink; label.FontWeight=entry.Selection==null || active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal; label.VerticalAlignment=VerticalAlignment.Center; label.TextWrapping=TextWrapping.NoWrap; label.TextTrimming=TextTrimming.CharacterEllipsis; label.Visibility=Collapsed ? Visibility.Collapsed : Visibility.Visible; Place(content,label,0,1);
                var count=app.Text(entry.Count.ToString("N0"),11); count.Foreground=ink; count.Opacity=.6; count.VerticalAlignment=VerticalAlignment.Center; count.Visibility=Collapsed ? Visibility.Collapsed : Visibility.Visible; Place(content,count,0,2);
                if(Collapsed) { content.Padding=new Thickness(9,0,0,0); content.ColumnSpacing=0; }
                ToolTipService.SetToolTip(host,$"{entry.Label} · {entry.Count:N0}"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(args.ItemContainer,entry.Label+", "+entry.Count+" channels"); host.Children.Add(content);
            };
            list.ItemClick+=(_,args)=> {
                if(args.ClickedItem is not RailEntry entry) return;
                if(entry.Selection==null) { if(!folded.Add(entry.SourceId)) folded.Remove(entry.SourceId); Refresh(); return; }
                this.selection=entry.Selection; Refresh(); selected(entry.Selection);
            };
            Place(this,list,1); Refresh();
        }
        public void Toggle() { Collapsed=!Collapsed; Refresh(); resized(Collapsed); }
        public void Select(ChannelSelection value) { selection=value; Refresh(); }
        public void Refresh()
        {
            LabelButton(toggle,Collapsed ? "\uE76C" : "\uE76B",Collapsed ? "Expand IPTV categories" : "Collapse IPTV categories");
            var live=app.items.Where(i=>i.Kind==MediaKind.Live).ToArray(); var profile=app.settings.Profile;
            var entries=new List<RailEntry> {
                new("Favorites","\uEB52",live.Count(i=>profile.Favorites.Contains(i.Id)),new(ChannelScope.Favorites)),
                new("Recently watched","\uE823",live.Count(i=>profile.History.ContainsKey(i.Id)),new(ChannelScope.Recent)),
                new("All channels","\uE714",live.Length,new(ChannelScope.All))
            };
            foreach(var group in ChannelBrowse.Categories(live,profile.Sources).GroupBy(c=>c.SourceId))
            {
                entries.Add(new(group.First().SourceName.ToUpperInvariant(),folded.Contains(group.Key) ? "\uE76C" : "\uE70D",group.Sum(c=>c.Count),null,group.Key));
                if(folded.Contains(group.Key)) continue;
                foreach(var category in group) entries.Add(new(category.Name,"\uE714",category.Count,new(ChannelScope.Category,category.SourceId,category.Name)));
            }
            list.ItemsSource=entries;
        }
    }
}

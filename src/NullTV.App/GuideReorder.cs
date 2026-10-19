using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
namespace NullTV.App;
public sealed partial class MainWindow
{
    // A captured native Thumb makes only the grip draggable. Programme buttons
    // keep their normal click behavior, and no channel data leaves the view.
    private sealed class GuideReorder(ListView list,ObservableCollection<GuideRow> rows,Action starting,Action<bool> completed) : IDisposable
    {
        private readonly Dictionary<GuideRow,Grid> visuals=[];
        private readonly DispatcherTimer scrollTimer=new() { Interval=TimeSpan.FromMilliseconds(100) };
        private GuideRow? dragged;
        private int origin,target;
        private double distance,rowHeight=88,pointerY,initialScroll;
        private ScrollViewer? scroller;
        public bool IsDragging=>dragged!=null;
        public Thumb Grip(GuideRow row,Grid visual)
        {
            foreach(var old in visuals.Where(pair=>ReferenceEquals(pair.Value,visual)).Select(pair=>pair.Key).ToArray()) visuals.Remove(old);
            visuals[row]=visual;
            var thumb=new Thumb { Width=24,MinHeight=48,VerticalAlignment=VerticalAlignment.Stretch,
                Template=(ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><TextBlock Text='⠿' FontFamily='Segoe UI Symbol' FontSize='18' Foreground='#6272A4' HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid></ControlTemplate>") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(thumb,"Reorder "+row.Channel.Name); ToolTipService.SetToolTip(thumb,"Drag, or focus and use Up/Down to reorder");
            thumb.KeyDown+=(_,args)=> { if(args.Key is not (Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)) return; var from=rows.IndexOf(row); var to=from+(args.Key==Windows.System.VirtualKey.Up ? -1 : 1); if(from>=0 && to>=0 && to<rows.Count) { rows.Move(from,to); completed(true); } args.Handled=true; };
            thumb.DragStarted+=(sender,args)=>
            {
                dragged=row; origin=target=rows.IndexOf(row); distance=0; rowHeight=Math.Max(1,visual.ActualHeight+visual.Margin.Top+visual.Margin.Bottom);
                scroller=FindScroll(list); initialScroll=scroller?.VerticalOffset??0;
                pointerY=thumb.TransformToVisual(list).TransformPoint(new Point(0,thumb.ActualHeight/2)).Y;
                visual.Opacity=.55; starting(); scrollTimer.Tick-=ScrollTick; scrollTimer.Tick+=ScrollTick; scrollTimer.Start();
            };
            thumb.DragDelta+=(sender,args)=> { distance+=args.VerticalChange; pointerY+=args.VerticalChange; UpdateTarget(); };
            thumb.DragCompleted+=(sender,args)=>
            {
                scrollTimer.Stop(); var changed=!args.Canceled && origin>=0 && target>=0 && origin!=target;
                ClearIndicators(); visual.Opacity=1; dragged=null;
                if(changed)
                {
                    var firstVisible=Math.Clamp((int)(initialScroll/rowHeight),0,rows.Count-1);
                    rows.Move(origin,target);
                    // ListView otherwise anchors the moved item at the top,
                    // hiding the channel that has just moved above it.
                    list.DispatcherQueue.TryEnqueue(()=> { list.UpdateLayout(); list.ScrollIntoView(rows[firstVisible],ScrollIntoViewAlignment.Leading); });
                }
                completed(changed);
            };
            return thumb;
        }
        private void ScrollTick(object? sender,object args)
        {
            if(dragged==null || scroller==null) return;
            var step=pointerY<28 ? -18 : pointerY>list.ActualHeight-28 ? 18 : 0;
            if(step!=0) { scroller.ChangeView(null,Math.Clamp(scroller.VerticalOffset+step,0,scroller.ScrollableHeight),null,true); UpdateTarget(); }
        }
        private void UpdateTarget()
        {
            if(dragged==null || rows.Count==0) return;
            target=Math.Clamp(origin+(int)Math.Round((distance+(scroller?.VerticalOffset??0)-initialScroll)/rowHeight),0,rows.Count-1);
            ClearIndicators();
            if(target!=origin && visuals.TryGetValue(rows[target],out var destination)) { destination.BorderBrush=new SolidColorBrush(Color("#BD93F9")); destination.BorderThickness=target>origin ? new Thickness(0,0,0,2) : new Thickness(0,2,0,0); }
        }
        private void ClearIndicators() { foreach(var visual in visuals.Values) visual.BorderThickness=new Thickness(0,0,0,.5); }
        private static ScrollViewer? FindScroll(DependencyObject node)
        {
            if(node is ScrollViewer scroll) return scroll;
            for(int n=0;n<VisualTreeHelper.GetChildrenCount(node);n++) if(FindScroll(VisualTreeHelper.GetChild(node,n)) is { } found) return found;
            return null;
        }
        public void Dispose() { scrollTimer.Stop(); scrollTimer.Tick-=ScrollTick; ClearIndicators(); dragged=null; visuals.Clear(); }
    }
}

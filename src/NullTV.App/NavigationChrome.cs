using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;

namespace NullTV.App;
public sealed partial class MainWindow
{
    private Button? navigationToggle;
    private RectInt32 navigationHitRegion;
    private InputNonClientPointerSource? titlebarInput;
    private void UpdateNavigationHitRegion()
    {
        if(closed || navigationToggle?.XamlRoot==null) return;
        var next=new RectInt32();
        if(!fullscreen && navigationToggle.ActualWidth>0 && navigationToggle.ActualHeight>0)
        {
            var point=navigationToggle.TransformToVisual(root).TransformPoint(new Point(0,0));
            var scale=navigationToggle.XamlRoot.RasterizationScale;
            next=new((int)Math.Floor(point.X*scale),(int)Math.Floor(point.Y*scale),
                (int)Math.Ceiling(navigationToggle.ActualWidth*scale),(int)Math.Ceiling(navigationToggle.ActualHeight*scale));
        }
        if(next==navigationHitRegion) return;
        titlebarInput??=InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        // Only the menu button receives client input. The rest of the title
        // bar keeps native window dragging and caption-button behavior.
        titlebarInput.SetRegionRects(NonClientRegionKind.Passthrough,next.Width>0 ? [next] : []);
        navigationHitRegion=next;
    }
}

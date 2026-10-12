using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace NullTV.App;

// Retain WinUI's slider input, focus, automation and 18px thumb hit area.
// Only replace the circular thumb visual with a thin green position marker.
internal sealed class PlaybackSlider : Slider
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if(GetTemplateChild("HorizontalThumb") is Thumb thumb)
            thumb.Template=(ControlTemplate)Application.Current.Resources["NullTVPlaybackThumbTemplate"];
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;

namespace NullTV.App;

// Retain WinUI's slider input, focus, automation and 18px thumb hit area.
// Draw the rail between thumb centers so its fill meets the slim marker.
internal sealed class PlaybackSlider : Slider
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if(GetTemplateChild("HorizontalThumb") is Thumb thumb)
        {
            thumb.Template=(ControlTemplate)Application.Current.Resources["NullTVPlaybackThumbTemplate"];
            var inset=double.IsNaN(thumb.Width) ? 9 : thumb.Width/2;
            if(GetTemplateChild("HorizontalTrackRect") is Rectangle track)
            {
                track.Height=2; track.VerticalAlignment=VerticalAlignment.Center; track.Margin=new Thickness(inset,0,inset,0); track.RadiusX=track.RadiusY=1;
            }
            if(GetTemplateChild("HorizontalDecreaseRect") is Rectangle fill)
            {
                fill.Height=2; fill.VerticalAlignment=VerticalAlignment.Center; fill.Margin=new Thickness(inset,0,-inset,0); fill.RadiusX=fill.RadiusY=1;
            }
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;

namespace NullTV.App;

// Retain WinUI's slider input, focus, automation and 18px thumb hit area.
// Draw the rail between thumb centers so its fill meets the slim marker.
internal sealed class PlaybackSlider : Slider
{
    private Thumb? marker;
    private Rectangle? rail;
    private bool hovered,dragging;
    public PlaybackSlider()
    {
        PointerEntered+=(_,_)=> { hovered=true; UpdateMarker(); };
        PointerExited+=(_,_)=> { hovered=false; UpdateMarker(); };
        AddHandler(PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=> { dragging=true; UpdateMarker(); }),true);
        AddHandler(PointerReleasedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=> { dragging=false; UpdateMarker(); }),true);
        PointerCaptureLost+=(_,_)=> { dragging=false; UpdateMarker(); };
        GotFocus+=(_,_)=>UpdateMarker(); LostFocus+=(_,_)=>UpdateMarker();
    }
    private void UpdateMarker()
    {
        var engaged=hovered || dragging || FocusState==FocusState.Keyboard;
        if(marker!=null) marker.Opacity=engaged ? 1 : 0;
        if(rail!=null) rail.Opacity=engaged ? .65 : .48;
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if(GetTemplateChild("HorizontalThumb") is Thumb thumb)
        {
            marker=thumb;
            thumb.Template=(ControlTemplate)Application.Current.Resources["NullTVPlaybackThumbTemplate"];
            var inset=double.IsNaN(thumb.Width) ? 9 : thumb.Width/2;
            if(GetTemplateChild("HorizontalTrackRect") is Rectangle track)
            {
                rail=track; track.Fill=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,98,114,164)); track.Height=3; track.VerticalAlignment=VerticalAlignment.Center; track.Margin=new Thickness(inset,0,inset,0); track.RadiusX=track.RadiusY=1.5;
            }
            if(GetTemplateChild("HorizontalDecreaseRect") is Rectangle fill)
            {
                fill.Height=3; fill.VerticalAlignment=VerticalAlignment.Center; fill.Margin=new Thickness(inset,0,-inset,0); fill.RadiusX=fill.RadiusY=1.5;
            }
            UpdateMarker();
        }
    }
}

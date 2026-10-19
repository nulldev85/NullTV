using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace NullTV.App;

// Decorate a native ProgressBar: retain its range/automation contract while
// composition draws rounded ends without stretching their corner radii.
internal sealed class PolishedProgressBar
{
    private readonly ProgressBar control;
    private readonly bool animateChanges;
    private Grid? host;
    private CompositionRoundedRectangleGeometry? track,fill;
    private CompositionColorBrush? trackBrush,fillBrush;
    private ShapeVisual? visual;
    private bool initialized;
    public static ProgressBar Create(double maximum=100,double value=0,bool animate=true)
    {
        var bar=new ProgressBar { Template=(ControlTemplate)Application.Current.Resources["NullTVProgressTemplate"],MinHeight=3,Height=3,MinWidth=0,IsHitTestVisible=false,Maximum=maximum,Value=value,
            Foreground=new SolidColorBrush(Windows.UI.Color.FromArgb(255,80,250,123)),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(48,98,114,164)) };
        _=new PolishedProgressBar(bar,animate); return bar;
    }
    private PolishedProgressBar(ProgressBar bar,bool animate)
    {
        control=bar; animateChanges=animate;
        foreach(var property in new[]{ProgressBar.ValueProperty,ProgressBar.MinimumProperty,ProgressBar.MaximumProperty,Control.ForegroundProperty,Control.BackgroundProperty}) control.RegisterPropertyChangedCallback(property,(_,_)=>Draw());
        control.Loaded+=(_,_)=>Initialize(); control.SizeChanged+=(_,_)=>Draw(false);
        control.Unloaded+=(_,_)=>fill?.StopAnimation("Size");
    }
    private void Initialize()
    {
        control.ApplyTemplate();
        var nextHost=VisualTreeHelper.GetChildrenCount(control)>0 ? VisualTreeHelper.GetChild(control,0) as Grid : null;
        if(nextHost==null) return;
        if(ReferenceEquals(nextHost,host)) { Draw(false); return; }
        host=nextHost;
        var compositor=ElementCompositionPreview.GetElementVisual(host).Compositor;
        visual=compositor.CreateShapeVisual(); track=compositor.CreateRoundedRectangleGeometry(); fill=compositor.CreateRoundedRectangleGeometry();
        trackBrush=compositor.CreateColorBrush(); fillBrush=compositor.CreateColorBrush();
        var background=compositor.CreateSpriteShape(track); background.FillBrush=trackBrush; visual.Shapes.Add(background);
        var foreground=compositor.CreateSpriteShape(fill); foreground.FillBrush=fillBrush; visual.Shapes.Add(foreground);
        ElementCompositionPreview.SetElementChildVisual(host,visual); initialized=false; Draw(false);
    }
    private void Draw(bool animate=true)
    {
        if(host==null || visual==null || track==null || fill==null || trackBrush==null || fillBrush==null) return;
        var width=(float)Math.Max(0,control.ActualWidth); var height=(float)Math.Min(3,Math.Max(0,control.ActualHeight));
        visual.Size=new(width,height); visual.Offset=new(0,(float)(control.ActualHeight-height)/2,0);
        track.CornerRadius=fill.CornerRadius=new(height/2,height/2); track.Size=new(width,height);
        trackBrush.Color=(control.Background as SolidColorBrush)?.Color??Windows.UI.Color.FromArgb(48,98,114,164);
        fillBrush.Color=(control.Foreground as SolidColorBrush)?.Color??Windows.UI.Color.FromArgb(255,80,250,123);
        var next=new Vector2(width*(float)Math.Clamp((control.Value-control.Minimum)/Math.Max(double.Epsilon,control.Maximum-control.Minimum),0,1),height);
        if(animate && initialized && animateChanges && MainWindow.MotionEnabled && control.IsLoaded)
        {
            var animation=visual.Compositor.CreateVector2KeyFrameAnimation(); animation.Duration=TimeSpan.FromMilliseconds(180);
            animation.InsertExpressionKeyFrame(0,"this.StartingValue"); animation.InsertKeyFrame(1,next); fill.Size=next; fill.StartAnimation("Size",animation);
        }
        else { fill.StopAnimation("Size"); fill.Size=next; }
        initialized=width>0;
    }
}
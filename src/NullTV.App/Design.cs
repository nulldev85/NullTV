using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;
namespace NullTV.App;
public sealed partial class MainWindow
{
    internal static bool MotionEnabled=>App.Main?.settings.Appearance.ReducedMotion!=true && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private Brush PanelBrush()=>settings.Appearance.Theme=="Light" ? new SolidColorBrush(Color("#FFFFFF")) : new LinearGradientBrush {
        StartPoint=new(0,0),EndPoint=new(1,1),GradientStops={new GradientStop { Color=Color("#282A36"),Offset=0 },new GradientStop { Color=Color("#21222C"),Offset=1 }} };
    private static Brush SoftStroke()=>new SolidColorBrush(Color(LightFields ? "#D3D5DA" : "#8044475A"));
    private bool navigationHidden,channelsHidden,expandedVideo;
    private Action? refreshLibrary,refreshGuide;
    private static void QuietItems(ListViewBase list)
    {
        foreach(var key in new[]{"ListViewItemBackgroundPointerOver","ListViewItemBackgroundSelectedPointerOver","ListViewItemBackgroundPressed","ListViewItemBackgroundSelectedPressed","GridViewItemBackgroundPointerOver","GridViewItemBackgroundSelectedPointerOver","GridViewItemBackgroundPressed","GridViewItemBackgroundSelectedPressed"}) list.Resources[key]=new SolidColorBrush(Colors.Transparent);
        list.Resources["ControlCornerRadius"]=new CornerRadius(10);
        foreach(var key in new[]{"ListViewItemSelectionIndicatorBrush","ListViewItemSelectionIndicatorPointerOverBrush","ListViewItemSelectionIndicatorPressedBrush"}) list.Resources[key]=new SolidColorBrush(Color("#BD93F9"));
    }
    private readonly Dictionary<string,(List<Programme> Source,ProgrammeIndex Index)> guideIndices=[];
    private ProgrammeIndex GuideFor(string sourceId)
    {
        var source=epgs.GetValueOrDefault(sourceId);
        if(source==null) return EmptyGuide;
        if(guideIndices.TryGetValue(sourceId,out var entry) && ReferenceEquals(entry.Source,source)) return entry.Index;
        return EmptyGuide;
    }
    private static readonly ProgrammeIndex EmptyGuide=new([]);
    private async Task IndexGuidesAsync()
    {
        foreach(var (id,shows) in epgs.ToArray())
        {
            if(guideIndices.TryGetValue(id,out var current) && ReferenceEquals(current.Source,shows)) continue;
            var index=await Task.Run(()=>new ProgrammeIndex(shows));
            if(ReferenceEquals(epgs.GetValueOrDefault(id),shows)) guideIndices[id]=(shows,index);
        }
        if(currentTab=="Guide") refreshGuide?.Invoke();
    }
    private void ToggleNavigation()
    {
        if(expandedVideo || fullscreen) { navigationHidden=false; ExitPlayerView(); }
        else { navigationHidden=!navigationHidden; RefreshPlayerView(); }
    }
    private void ToggleChannelPane() { liveCategoryRail?.Toggle(); }
    private void ToggleExpandedVideo()
    {
        if(fullscreen) { ExitPlayerView(); return; }
        expandedVideo=!expandedVideo;
        RefreshPlayerView();
    }
    private static bool LightFields=>App.Main?.settings.Appearance.Theme=="Light";
    private string SurfaceHex=>settings.Appearance.Theme=="Light" ? "#FFFFFF" : "#21222C";
    private string RaisedHex=>settings.Appearance.Theme=="Light" ? "#E8EAEE" : "#343746";
    private string BorderHex=>settings.Appearance.Theme=="Light" ? "#D3D5DA" : "#44475A";
    private static SolidColorBrush FieldText()=>new(Color(LightFields ? "#202228" : "#F8F8F2"));
    private static SolidColorBrush FieldFill()=>new(Color(LightFields ? "#FFFFFF" : "#21222C"));
    private static SolidColorBrush FieldStroke()=>new(Color(LightFields ? "#D3D5DA" : "#8044475A"));
    private static void PolishField(Control control,bool preserveTemplate=false)
    {
        if(!preserveTemplate) control.Style=(Style)Application.Current.Resources[control is ComboBox ? "NullTVComboBoxStyle" : control is PasswordBox ? "NullTVPasswordBoxStyle" : "NullTVTextBoxStyle"];
        control.CornerRadius=new CornerRadius(10); control.FontSize=13; control.Padding=new Thickness(12,9,12,9); control.Background=FieldFill(); control.Foreground=FieldText(); control.BorderBrush=FieldStroke(); control.BorderThickness=new Thickness(1);
        control.Resources["TextControlForeground"]=FieldText(); control.Resources["TextControlForegroundFocused"]=FieldText(); control.Resources["TextControlForegroundPointerOver"]=FieldText(); control.Resources["TextControlBackgroundFocused"]=FieldFill(); control.Resources["TextControlBorderBrushFocused"]=new SolidColorBrush(Color("#815CD6"));
        if(control is ComboBox combo)
        {
            combo.ItemContainerStyle=(Style)Application.Current.Resources["NullTVComboBoxItemStyle"];
            foreach(var key in new[]{"ComboBoxBackgroundPointerOver","ComboBoxBackgroundPressed","ComboBoxDropDownBackground"}) combo.Resources[key]=FieldFill();
            combo.Resources["ComboBoxDropDownForeground"]=FieldText(); combo.Resources["ComboBoxDropDownBorderBrush"]=FieldStroke(); combo.Resources["OverlayCornerRadius"]=new CornerRadius(10);
        }
    }
    private sealed class SearchField : Grid
    {
        private readonly TextBox input;
        public string Text { get=>input.Text; set=>input.Text=value; }
        public void Compact() { MinHeight=input.MinHeight=34; input.Padding=new Thickness(12,6,36,6); }
        public event TextChangedEventHandler? TextChanged;
        public SearchField(string placeholder)
        {
            MinHeight=42;
            input=new TextBox { PlaceholderText=placeholder,MinHeight=42,VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Stretch,TextAlignment=TextAlignment.Left };
            PolishField(input); input.Padding=new Thickness(12,9,38,9);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,placeholder);
            Children.Add(input);
            var icon=new FontIcon { Glyph="\uE721",FontSize=14,Foreground=FieldText(),Opacity=.6,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0),IsHitTestVisible=false }; Children.Add(icon);
            input.TextChanged+=(sender,args)=> { icon.Visibility=input.Text.Length==0 ? Visibility.Visible : Visibility.Collapsed; TextChanged?.Invoke(this,args); };
        }
    }
    private static SearchField SearchBox(string placeholder)=>new(placeholder);
}

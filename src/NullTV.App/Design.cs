using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;
namespace NullTV.App;
public sealed partial class MainWindow
{
    private bool navigationHidden,channelsHidden,expandedVideo;
    private Action? refreshLibrary,refreshGuide;
    private static void QuietItems(ListViewBase list)
    {
        foreach(var key in new[]{"ListViewItemBackgroundPointerOver","ListViewItemBackgroundSelectedPointerOver","GridViewItemBackgroundPointerOver","GridViewItemBackgroundSelectedPointerOver"}) list.Resources[key]=new SolidColorBrush(Colors.Transparent);
        list.Resources["ControlCornerRadius"]=new CornerRadius(10);
        foreach(var key in new[]{"ListViewItemSelectionIndicatorBrush","ListViewItemSelectionIndicatorPointerOverBrush","ListViewItemSelectionIndicatorPressedBrush"}) list.Resources[key]=new SolidColorBrush(Color("#D3D6DC"));
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
        navigationHidden=!navigationHidden;
        if(sidebar!=null) sidebar.Visibility=navigationHidden ? Visibility.Collapsed : Visibility.Visible;
        root.ColumnDefinitions[0].Width=new(navigationHidden ? 0 : 184);
        UpdateLiveLayout();
    }
    private void ToggleChannelPane() { channelsHidden=!channelsHidden; UpdateLiveLayout(); }
    private void ToggleExpandedVideo()
    {
        expandedVideo=!expandedVideo; navigationHidden=expandedVideo; channelsHidden=expandedVideo;
        if(sidebar!=null) sidebar.Visibility=navigationHidden ? Visibility.Collapsed : Visibility.Visible;
        root.ColumnDefinitions[0].Width=new(navigationHidden ? 0 : 184);
        UpdateLiveLayout();
    }
    private static bool LightFields=>App.Main?.settings.Appearance.Theme=="Light";
    private static SolidColorBrush FieldText()=>new(Color(LightFields ? "#202228" : "#F1F2F4"));
    private static SolidColorBrush FieldFill()=>new(Color(LightFields ? "#FFFFFF" : "#151517"));
    private static SolidColorBrush FieldStroke()=>new(Color(LightFields ? "#D3D5DA" : "#303035"));
    private static void PolishField(Control control,bool preserveTemplate=false)
    {
        if(!preserveTemplate) control.Style=(Style)Application.Current.Resources[control is ComboBox ? "NullTVComboBoxStyle" : control is PasswordBox ? "NullTVPasswordBoxStyle" : "NullTVTextBoxStyle"];
        control.CornerRadius=new CornerRadius(10); control.FontSize=14; control.Padding=new Thickness(14,11,14,11); control.Background=FieldFill(); control.Foreground=FieldText(); control.BorderBrush=FieldStroke(); control.BorderThickness=new Thickness(1);
        control.Resources["TextControlForeground"]=FieldText(); control.Resources["TextControlForegroundFocused"]=FieldText(); control.Resources["TextControlForegroundPointerOver"]=FieldText(); control.Resources["TextControlBackgroundFocused"]=FieldFill(); control.Resources["TextControlBorderBrushFocused"]=new SolidColorBrush(Color("#B9BEC7"));
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
        public event TextChangedEventHandler? TextChanged;
        public SearchField(string placeholder)
        {
            MinHeight=46;
            input=new TextBox { PlaceholderText=placeholder,MinHeight=46,VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Stretch,TextAlignment=TextAlignment.Left };
            PolishField(input); input.Padding=new Thickness(14,11,40,11);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,placeholder);
            Children.Add(input);
            var icon=new FontIcon { Glyph="\uE721",FontSize=14,Foreground=FieldText(),Opacity=.6,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0),IsHitTestVisible=false }; Children.Add(icon);
            input.TextChanged+=(sender,args)=> { icon.Visibility=input.Text.Length==0 ? Visibility.Visible : Visibility.Collapsed; TextChanged?.Invoke(this,args); };
        }
    }
    private static SearchField SearchBox(string placeholder)=>new(placeholder);
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NullTV.Core;
namespace NullTV.App;
public sealed partial class MainWindow
{
    private Action? refreshGuideQuality;
    private string QualityText(MediaItem item)=>string.Join(" · ",ChannelQuality.Labels(item,settings.Profile.StreamObservations.GetValueOrDefault(item.Id),DateTimeOffset.UtcNow).Select(label=>label.Text));
    private void FillQuality(StackPanel panel,MediaItem item)
    {
        var labels=ChannelQuality.Labels(item,settings.Profile.StreamObservations.GetValueOrDefault(item.Id),DateTimeOffset.UtcNow);
        var key=string.Join('|',labels.Select(label=>label.Text+label.Description)); if((string?)panel.Tag==key) return; panel.Tag=key; panel.Children.Clear();
        foreach(var label in labels)
        {
            var color=label.Kind=="fps" ? "#F1FA8C" : label.Kind=="hdr" ? "#FF79C6" : "#BD93F9";
            var text=Text(label.Text,10); text.Foreground=new SolidColorBrush(Color(color)); text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold; text.TextWrapping=TextWrapping.NoWrap;
            var chip=new Border { Child=text,Padding=new Thickness(5,1,5,1),CornerRadius=new CornerRadius(4),Background=new SolidColorBrush(Color("#343746")) };
            ToolTipService.SetToolTip(chip,label.Description); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip,label.Text+", "+label.Description); panel.Children.Add(chip);
        }
        panel.Visibility=labels.Count==0 ? Visibility.Collapsed : Visibility.Visible;
    }
}

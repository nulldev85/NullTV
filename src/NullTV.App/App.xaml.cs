using Microsoft.UI.Xaml;
using NullTV.Core;

namespace NullTV.App;
public partial class App : Application
{
    public static MainWindow? Main { get; private set; }
    public App() { InitializeComponent(); UnhandledException += (_,e) => { if(e.Exception is OutOfMemoryException or AccessViolationException) return; e.Handled=true; if(Main!=null) Main.ReportUnhandled(e.Exception); else _ = new SafeLog(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NullTV","logs")).WriteAsync("UNHANDLED",e.Exception); }; }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { Main = new MainWindow(); Main.Activate(); }
}

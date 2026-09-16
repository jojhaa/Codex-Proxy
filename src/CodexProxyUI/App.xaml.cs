using System.Configuration;
using System.Data;
using System.Windows;

namespace CodexProxyUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) => {
            try { System.IO.File.WriteAllText("crash.log", args.Exception.ToString()); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) => {
            try { System.IO.File.WriteAllText("crash.log", args.ExceptionObject.ToString()); } catch { }
        };
        base.OnStartup(e);
        if (e.Args.Contains("--launch"))
            Dispatcher.BeginInvoke(new Action(() => {
                if (MainWindow is CodexProxyUI.MainWindow window) window.LaunchFromShortcut();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}

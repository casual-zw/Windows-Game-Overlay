using System.Windows;

namespace Overlay.Windows;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // OverlayWindow is constructed by the main window; explicitly select the lifetime owner.
        // A standalone target exercises cross-process hit testing, unlike the
        // built-in test scene which shares the overlay's UI thread.
        MainWindow = e.Args.Contains("--test-scene") ? new TestSceneWindow() : new MainWindow();
        MainWindow.Show();
    }
}

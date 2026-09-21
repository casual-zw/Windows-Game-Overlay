using System.Windows;

namespace Overlay.Windows;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // OverlayWindow is constructed by the main window; explicitly select the lifetime owner.
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}

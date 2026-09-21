using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Overlay.Windows.Interop;

namespace Overlay.Windows;

/// <summary>Interactive controls shown only on demand; the subtitle panel stays click-through.</summary>
internal sealed class OverlayControlsWindow : Window
{
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 16) };
    private readonly Button _select = new() { Content = "Select dialogue region", FontSize = 16, Padding = new Thickness(14, 10, 14, 10) };
    private readonly Button _read = new() { Content = "Read again · Ctrl+Alt+G", Padding = new Thickness(12, 7, 12, 7) };
    private bool _allowClose, _dismissing;
    internal nint Handle { get; private set; }
    internal event Action? SelectRequested;
    internal event Action? ReadRequested;
    internal event Action<bool>? Dismissed;

    internal OverlayControlsWindow()
    {
        Title = "Game Overlay controls";
        Width = 360; Height = 280;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI");
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Game Overlay", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        content.Children.Add(_status);
        content.Children.Add(_select);
        content.Children.Add(_read);
        _read.Click += (_, _) => ReadRequested?.Invoke();
        var resume = new Button { Content = "Back to game · Esc", Padding = new Thickness(12, 7, 12, 7) };
        content.Children.Add(resume);
        content.Children.Add(new TextBlock { Text = "Ctrl+Alt+O opens or closes these controls", Foreground = Brushes.LightSlateGray, FontSize = 12 });
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 16, 23, 42)),
            BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Child = content
        };
        _select.Click += (_, _) => SelectRequested?.Invoke();
        resume.Click += (_, _) => Dismiss(true);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Dismiss(true); }
        };
        Deactivated += (_, _) => { if (!_allowClose && IsVisible) Dismiss(false); };
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowDisplayAffinity(Handle, 0x11);
        };
    }

    internal void UpdateStatus(bool hasFrame, bool hasRegion)
    {
        _select.IsEnabled = hasFrame;
        _read.IsEnabled = hasFrame && hasRegion;
        _status.Text = !hasFrame ? "Waiting for the game capture…" :
            hasRegion ? "Change the area used for dialogue." : "Choose the dialogue area to get started.";
    }

    internal void Place(Native.Rect target)
    {
        if (Handle == 0 || !Native.GetWindowRect(Handle, out var panel)) return;
        int x = Math.Max(target.Left, target.Right - panel.Width - 24);
        int y = Math.Max(target.Top, Math.Min(target.Top + 24, target.Bottom - panel.Height));
        Native.SetWindowPos(Handle, new nint(-1), x, y, 0, 0, Native.SwpNoSize | Native.SwpNoActivate);
    }

    internal void Open(Native.Rect target)
    {
        new WindowInteropHelper(this).EnsureHandle();
        Place(target);
        Show();
        Place(target); // Reconcile physical bounds after WPF's initial DPI/layout pass.
        Activate();
    }

    internal void Dismiss(bool returnToGame)
    {
        if (_dismissing || !IsVisible) return;
        _dismissing = true;
        try { Hide(); Dismissed?.Invoke(returnToGame); }
        finally { _dismissing = false; }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            // Unwind Closing before changing Visibility (e.g. when Alt+F4 is used).
            Dispatcher.BeginInvoke(new Action(() => Dismiss(true)));
        }
        base.OnClosing(e);
    }

    internal void CloseForShutdown() { _allowClose = true; Close(); }
}

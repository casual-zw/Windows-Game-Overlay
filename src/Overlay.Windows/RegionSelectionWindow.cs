using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Overlay.Core;
using Overlay.Windows.Interop;

namespace Overlay.Windows;

/// <summary>A frozen view of the captured game, positioned in physical screen pixels.</summary>
internal sealed class RegionSelectionWindow : Window
{
    private readonly Canvas _surface = new() { Background = Brushes.Transparent };
    private readonly Rectangle _box = new()
    {
        Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(55, 56, 189, 248)),
        Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };
    private readonly TextBlock _hint = new()
    {
        Text = "Drag around the dialogue · Esc cancels · Game preview is frozen",
        Foreground = Brushes.White, Background = Brushes.Black, Padding = new Thickness(12),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(12), IsHitTestVisible = false
    };
    private readonly BitmapSource _frame;
    private Point? _start;
    private bool _finished;
    internal Native.Rect TargetBounds { get; }
    internal CaptureRegion? SelectedRegion { get; private set; }
    internal bool ReturnToGame { get; private set; }

    internal RegionSelectionWindow(BitmapSource frame, Native.Rect bounds)
    {
        _frame = frame;
        TargetBounds = bounds;
        Title = "Select game dialogue";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        var grid = new Grid { Background = Brushes.Black };
        grid.Children.Add(new Image { Source = frame, Stretch = Stretch.Fill, IsHitTestVisible = false });
        grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), IsHitTestVisible = false });
        grid.Children.Add(_surface);
        grid.Children.Add(_hint);
        _surface.Children.Add(_box);
        Content = grid;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(hwnd, new nint(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0);
            Native.SetWindowDisplayAffinity(hwnd, 0x11);
        };
        // WPF may apply its initial size after SourceInitialized. Reapply the
        // physical bounds after first layout, particularly on mixed-DPI monitors.
        Loaded += (_, _) => Native.SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1),
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, Native.SwpNoActivate);
        _surface.MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_surface);
            _surface.CaptureMouse();
            Draw(_start.Value);
            e.Handled = true;
        };
        _surface.MouseMove += (_, e) => { if (_start.HasValue) Draw(e.GetPosition(_surface)); };
        _surface.MouseLeftButtonUp += FinishSelection;
        _surface.LostMouseCapture += (_, _) =>
        {
            _start = null;
            _box.Visibility = Visibility.Collapsed;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; ReturnToGame = true; Close(); }
        };
        Deactivated += (_, _) => { if (!_finished) Close(); };
        Closing += (_, _) => _finished = true;
        Closed += (_, _) => { _finished = true; _surface.ReleaseMouseCapture(); };
    }

    private void Draw(Point end)
    {
        if (_start is not Point start) return;
        double left = Math.Clamp(Math.Min(start.X, end.X), 0, _surface.ActualWidth);
        double top = Math.Clamp(Math.Min(start.Y, end.Y), 0, _surface.ActualHeight);
        double right = Math.Clamp(Math.Max(start.X, end.X), 0, _surface.ActualWidth);
        double bottom = Math.Clamp(Math.Max(start.Y, end.Y), 0, _surface.ActualHeight);
        Canvas.SetLeft(_box, left); Canvas.SetTop(_box, top);
        _box.Width = right - left; _box.Height = bottom - top;
        _box.Visibility = Visibility.Visible;
    }

    private void FinishSelection(object sender, MouseButtonEventArgs e)
    {
        if (_start is not Point start) return;
        var end = e.GetPosition(_surface);
        _start = null;
        _surface.ReleaseMouseCapture();
        try
        {
            var region = CaptureRegion.FromDrag(start.X / _surface.ActualWidth, start.Y / _surface.ActualHeight,
                end.X / _surface.ActualWidth, end.Y / _surface.ActualHeight);
            var pixels = region.ToPixels(_frame.PixelWidth, _frame.PixelHeight);
            if (pixels.Width < 8 || pixels.Height < 8) throw new ArgumentOutOfRangeException(nameof(region));
            SelectedRegion = region;
            ReturnToGame = true;
            Close();
        }
        catch (ArgumentOutOfRangeException) { _hint.Text = "Select a larger region · Drag again, or press Esc to cancel"; }
    }
}

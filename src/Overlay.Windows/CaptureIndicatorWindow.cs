using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Overlay.Windows.Interop;

namespace Overlay.Windows;

/// <summary>A non-activating, click-through capture status badge.</summary>
internal sealed class CaptureIndicatorWindow : Window
{
    private const int InputFlags = Native.WsExToolWindow | Native.WsExLayered |
        Native.WsExTransparent | Native.WsExNoActivate;
    private readonly Ellipse _dot = new() { Width = 8, Height = 8, Fill = Brushes.Tomato,
        Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private nint _handle;
    private bool _excluded;

    internal CaptureIndicatorWindow()
    {
        Title = "Capture status";
        Width = 132;
        Height = 30;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        IsHitTestVisible = false;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(new TextBlock { Text = "CAPTURING", FontSize = 11,
            FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center });
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(128, 16, 23, 42)),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 12, 6), Child = row };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_handle)?.AddHook(WindowProc);
            _excluded = Native.SetWindowDisplayAffinity(_handle, 0x11);
        };
        IsVisibleChanged += (_, _) =>
        {
            _dot.BeginAnimation(OpacityProperty, IsVisible ? new DoubleAnimation(1, 0.3,
                TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        };
    }

    internal void ShowForTarget(Native.Rect bounds)
    {
        if (_handle == 0) new WindowInteropHelper(this).EnsureHandle();
        // Do not show a badge that could contaminate the OCR input or eat game clicks.
        if (!_excluded || !ApplyInputStyle()) { Hide(); return; }
        Place(bounds);
        if (!IsVisible) Show();
        if (!ApplyInputStyle()) { Hide(); return; }
        Place(bounds);
    }

    private void Place(Native.Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int inset = (int)Math.Round(12 * dpi.DpiScaleX);
        Native.SetWindowPos(_handle, new nint(-1), bounds.Left + inset,
            bounds.Top + (int)Math.Round(12 * dpi.DpiScaleY), 0, 0,
            Native.SwpNoSize | Native.SwpNoActivate);
    }

    private bool ApplyInputStyle()
    {
        int style = Native.GetWindowLong(_handle, Native.GwlExStyle);
        if (style == 0 && Marshal.GetLastWin32Error() != 0) return false;
        if ((style & InputFlags) != InputFlags)
        {
            int previous = Native.SetWindowLong(_handle, Native.GwlExStyle, style | InputFlags);
            if (previous == 0 && Marshal.GetLastWin32Error() != 0) return false;
            if (!Native.SetWindowPos(_handle, new nint(-1), 0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpFrameChanged)) return false;
        }
        return (Native.GetWindowLong(_handle, Native.GwlExStyle) & InputFlags) == InputFlags;
    }

    private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WmStyleChanging && wParam == Native.GwlExStyle && lParam != 0)
        {
            var styles = Marshal.PtrToStructure<Native.StyleStruct>(lParam);
            styles.NewStyle |= InputFlags;
            Marshal.StructureToPtr(styles, lParam, false);
        }
        if (msg == Native.WmMouseActivate) { handled = true; return 3; }
        return 0;
    }
}

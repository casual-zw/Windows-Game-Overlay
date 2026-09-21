using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Overlay.Windows.Interop;

namespace Overlay.Windows;

public partial class OverlayWindow : Window
{
    private nint _hwnd;
    private bool _editing, _positioning, _allowClose;
    private Native.Rect _targetBounds;
    private double _relativeX = 0.10, _relativeY = 0.68;
    private double _backgroundOpacity = 0.88;
    public event Action? HideRequested;
    internal nint Handle => _hwnd;
    internal string? AffinityWarning { get; private set; }
    internal string? InteractionWarning { get; private set; }
    internal string InputStatus => InteractionWarning ?? (_editing ? "Edit mode: panel accepts clicks" : "Reading mode: native click-through enabled");

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(WindowProc);
            if (source?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
            if (!Native.SetWindowDisplayAffinity(_hwnd, 0x11 /* WDA_EXCLUDEFROMCAPTURE */))
                AffinityWarning = $"Overlay capture exclusion was not accepted (Windows error {Marshal.GetLastWin32Error()}). Verify capture isolation.";
            ApplyInteractionStyle();
        };
        LocationChanged += (_, _) => SaveAnchor();
        SizeChanged += (_, _) => SaveAnchor();
        Loaded += (_, _) => ApplyInteractionStyle();
    }

    internal void SetEditing(bool editing)
    {
        _editing = editing;
        ModeLabel.Text = editing ? "编辑模式 · 按 Ctrl+Alt+E 恢复点击穿透" : "阅读模式 · 点击穿透 · 示例文字（未连接 AI）";
        UpdatePanelBrushes();
        if (!editing && IsMouseCaptureWithin) Mouse.Capture(null);
        // Do not attach WindowChrome: it sets the composition background to an
        // opaque system color when disabling glass or removing custom chrome.
        // Handle resize hit testing directly, keeping per-pixel alpha intact.
        ResizeMode = editing ? ResizeMode.CanResize : ResizeMode.NoResize;
        IsHitTestVisible = editing;
        ApplyInteractionStyle();
    }

    internal void ShowForTarget()
    {
        if (!IsVisible) Show();
        // Showing the window can rewrite cached native styles.
        // Reconcile after Show and read back the actual HWND, not just our mode flag.
        ApplyInteractionStyle();
        if (InteractionWarning is not null) Hide();
    }

    internal void SetAppearance(double fontSize, double opacity)
    {
        ChineseText.FontSize = fontSize;
        _backgroundOpacity = Math.Clamp(opacity, 0, 1);
        UpdatePanelBrushes();
    }

    private void UpdatePanelBrushes()
    {
        // Change only the brushes, never Window/Panel.Opacity: text must remain legible.
        byte alpha = (byte)Math.Round(_backgroundOpacity * 255);
        Panel.Background = new SolidColorBrush(Color.FromArgb(alpha, 16, 23, 42));
        Panel.BorderBrush = _editing ? Brushes.DeepSkyBlue :
            new SolidColorBrush(Color.FromArgb(alpha, 112, 128, 144));
    }

    private void ApplyInteractionStyle()
    {
        if (_hwnd == 0) return;
        InteractionWarning = null;
        int current = Native.GetWindowLong(_hwnd, Native.GwlExStyle);
        int error = Marshal.GetLastWin32Error();
        if (current == 0 && error != 0)
        { InteractionWarning = $"Cannot read overlay input flags (Windows error {error})."; return; }
        int desired = WithInteractionStyle(current);
        if (current != desired)
        {
            int previous = Native.SetWindowLong(_hwnd, Native.GwlExStyle, desired);
            error = Marshal.GetLastWin32Error();
            if (previous == 0 && error != 0)
            { InteractionWarning = $"Cannot set overlay input flags (Windows error {error})."; return; }
            if (!Native.SetWindowPos(_hwnd, new nint(-1), 0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpFrameChanged))
            { InteractionWarning = $"Cannot apply overlay input flags (Windows error {Marshal.GetLastWin32Error()})."; return; }
        }
        int actual = Native.GetWindowLong(_hwnd, Native.GwlExStyle);
        if (actual != WithInteractionStyle(actual))
            InteractionWarning = "Overlay input flags did not stick. The panel is hidden; stop and restart capture.";
    }

    private int WithInteractionStyle(int style)
    {
        style |= Native.WsExToolWindow | Native.WsExLayered;
        return _editing ? style & ~(Native.WsExTransparent | Native.WsExNoActivate)
                        : style | Native.WsExTransparent | Native.WsExNoActivate;
    }

    internal void Follow(Native.Rect target)
    {
        _targetBounds = target;
        if (_hwnd == 0 || target.Width <= 0 || target.Height <= 0) return;
        Native.GetWindowRect(_hwnd, out var current);
        int x = target.Left + (int)(_relativeX * target.Width);
        int y = target.Top + (int)(_relativeY * target.Height);
        // Keep the panel reachable even after the target becomes smaller.
        x = Math.Clamp(x, target.Left, Math.Max(target.Left, target.Right - current.Width));
        y = Math.Clamp(y, target.Top, Math.Max(target.Top, target.Bottom - current.Height));
        if (current.Left == x && current.Top == y) return;
        _positioning = true;
        try { Native.SetWindowPos(_hwnd, new nint(-1), x, y, 0, 0, Native.SwpNoSize | Native.SwpNoActivate); }
        finally { _positioning = false; }
    }

    private void SaveAnchor()
    {
        if (!_editing || _positioning || _hwnd == 0 || _targetBounds.Width <= 0 || _targetBounds.Height <= 0) return;
        if (!Native.GetWindowRect(_hwnd, out var current)) return;
        _relativeX = Math.Clamp((double)(current.Left - _targetBounds.Left) / _targetBounds.Width, 0, 1);
        _relativeY = Math.Clamp((double)(current.Top - _targetBounds.Top) / _targetBounds.Height, 0, 1);
    }

    internal void ResetPosition()
    {
        _relativeX = 0.10;
        _relativeY = 0.68;
        _positioning = true;
        try { Width = 650; Height = 210; }
        finally { _positioning = false; }
        Follow(_targetBounds);
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (_editing && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_editing && msg == Native.WmNcHitTest)
        {
            // WM_NCHITTEST carries signed physical screen coordinates. Convert
            // to WPF units so the resize border stays 7 DIPs across monitors.
            long packed = lParam.ToInt64();
            var point = PointFromScreen(new Point(unchecked((short)(packed & 0xffff)),
                unchecked((short)((packed >> 16) & 0xffff))));
            if (point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight)
            {
                bool left = point.X < 7, right = point.X >= ActualWidth - 7;
                bool top = point.Y < 7, bottom = point.Y >= ActualHeight - 7;
                // Win32 HTLEFT..HTBOTTOMRIGHT. The OS owns the resize gesture.
                int hit = top ? (left ? 13 : right ? 14 : 12) :
                    bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 0;
                if (hit != 0) { handled = true; return hit; }
            }
        }
        if (msg == Native.WmStyleChanging && wParam == Native.GwlExStyle && lParam != 0)
        {
            // Preserve our input-mode bits when WPF changes unrelated extended
            // styles during show/hide or resizing. Do not invoke
            // SetWindowLong recursively from inside this notification.
            var styles = Marshal.PtrToStructure<Native.StyleStruct>(lParam);
            styles.NewStyle = WithInteractionStyle(styles.NewStyle);
            Marshal.StructureToPtr(styles, lParam, false);
        }
        if (!_editing && msg == Native.WmMouseActivate) { handled = true; return 3; /* MA_NOACTIVATE */ }
        // HTTRANSPARENT only searches windows on the same thread. Cross-process
        // click-through is provided by WS_EX_LAYERED | WS_EX_TRANSPARENT above.
        return 0;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; HideRequested?.Invoke(); }
        base.OnClosing(e);
    }

    internal void CloseForShutdown() { _allowClose = true; Close(); }
}

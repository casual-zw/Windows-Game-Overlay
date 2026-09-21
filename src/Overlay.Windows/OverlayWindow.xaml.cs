using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Overlay.Windows.Interop;

namespace Overlay.Windows;

public partial class OverlayWindow : Window
{
    private nint _hwnd;
    private bool _editing, _positioning, _allowClose;
    private Native.Rect _targetBounds;
    private double _relativeX = 0.10, _relativeY = 0.68;
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
            HwndSource.FromHwnd(_hwnd)?.AddHook(WindowProc);
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
        Panel.BorderBrush = editing ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        if (!editing && IsMouseCaptureWithin) Mouse.Capture(null);
        // Custom non-client resize hit testing belongs only to edit mode.
        WindowChrome.SetWindowChrome(this, editing ? new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(7),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0)
        } : null);
        ResizeMode = editing ? ResizeMode.CanResize : ResizeMode.NoResize;
        IsHitTestVisible = editing;
        ApplyInteractionStyle();
    }

    internal void ShowForTarget()
    {
        if (!IsVisible) Show();
        // Showing or changing WPF chrome can rewrite cached native styles.
        // Reconcile after Show and read back the actual HWND, not just our mode flag.
        ApplyInteractionStyle();
        if (InteractionWarning is not null) Hide();
    }

    internal void SetAppearance(double fontSize, double opacity)
    {
        ChineseText.FontSize = fontSize;
        Panel.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 16, 23, 42));
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
        if (msg == Native.WmStyleChanging && wParam == Native.GwlExStyle && lParam != 0)
        {
            // Preserve our input-mode bits when WPF changes unrelated extended
            // styles during show/hide, resizing, or chrome changes. Do not invoke
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

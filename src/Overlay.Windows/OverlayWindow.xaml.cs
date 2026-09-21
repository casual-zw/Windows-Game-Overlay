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
    public event Action? HideRequested;
    internal nint Handle => _hwnd;
    internal string? AffinityWarning { get; private set; }

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
    }

    internal void SetEditing(bool editing)
    {
        _editing = editing;
        ModeLabel.Text = editing ? "编辑模式 · 拖动此处移动，拖动边缘调整大小" : "预览 · 示例文字（未连接 AI）";
        Panel.BorderBrush = editing ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        ApplyInteractionStyle();
    }

    internal void SetAppearance(double fontSize, double opacity)
    {
        ChineseText.FontSize = fontSize;
        Panel.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 16, 23, 42));
    }

    private void ApplyInteractionStyle()
    {
        if (_hwnd == 0) return;
        int style = Native.GetWindowLong(_hwnd, Native.GwlExStyle) | Native.WsExToolWindow | Native.WsExLayered;
        style = _editing ? style & ~(Native.WsExTransparent | Native.WsExNoActivate)
                         : style | Native.WsExTransparent | Native.WsExNoActivate;
        Native.SetWindowLong(_hwnd, Native.GwlExStyle, style);
        Native.SetWindowPos(_hwnd, new nint(-1), 0, 0, 0, 0,
            Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpFrameChanged);
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
        if (!_editing && msg == Native.WmMouseActivate) { handled = true; return 3; /* MA_NOACTIVATE */ }
        if (!_editing && msg == Native.WmNcHitTest) { handled = true; return -1; /* HTTRANSPARENT */ }
        return 0;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; HideRequested?.Invoke(); }
        base.OnClosing(e);
    }

    internal void CloseForShutdown() { _allowClose = true; Close(); }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Overlay.Core;
using Overlay.Windows.Capture;
using Overlay.Windows.Interop;

namespace Overlay.Windows;

public partial class MainWindow : Window
{
    private readonly OverlayWindow _overlay = new();
    private readonly OverlayControlsWindow _controls = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private WindowCapture? _capture;
    private WindowChoice? _target;
    private BitmapSource? _frame;
    private CaptureRegion? _region;
    private Point? _dragStart;
    private TestSceneWindow? _testWindow;
    private RegionSelectionWindow? _selector;
    private nint _hwnd;
    private bool _requested = true, _editing, _tickBusy, _closing, _closeReady, _changingCapture;
    private bool _toggleHotkey, _editHotkey, _regionHotkey, _controlsHotkey;
    private long _frames, _started;
    private string _hotkeyWarning = "";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        _timer.Tick += Tick;
        _overlay.HideRequested += () => { _requested = false; _overlay.Hide(); };
        _controls.ReadRequested += () => ReadAgain(_controls, new RoutedEventArgs());
        _controls.SelectRequested += () => SelectOnGame(_controls, new RoutedEventArgs());
        _controls.Dismissed += returnToGame => { if (returnToGame) ReturnFocusToGame(); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WindowProc);
        _toggleHotkey = Native.RegisterHotKey(_hwnd, 1, Native.ModControl | Native.ModAlt | Native.ModNoRepeat, 0x54);
        _editHotkey = Native.RegisterHotKey(_hwnd, 2, Native.ModControl | Native.ModAlt | Native.ModNoRepeat, 0x45);
        _regionHotkey = Native.RegisterHotKey(_hwnd, 3, Native.ModControl | Native.ModAlt | Native.ModNoRepeat, 0x52);
        _controlsHotkey = Native.RegisterHotKey(_hwnd, 4, Native.ModControl | Native.ModAlt | Native.ModNoRepeat, 0x4F);
        _readHotkey = Native.RegisterHotKey(_hwnd, 5, Native.ModControl | Native.ModAlt | Native.ModNoRepeat, 0x47);
        var unavailable = new List<string>();
        if (!_readHotkey) unavailable.Add("Ctrl+Alt+G");
        if (!_toggleHotkey) unavailable.Add("Ctrl+Alt+T");
        if (!_editHotkey) unavailable.Add("Ctrl+Alt+E");
        if (!_regionHotkey) unavailable.Add("Ctrl+Alt+R");
        if (!_controlsHotkey) unavailable.Add("Ctrl+Alt+O");
        if (unavailable.Count > 0)
            _hotkeyWarning = $"Hotkey conflict: {string.Join(", ", unavailable)}. Use the control-window buttons instead.";
        // Create the HWND without displaying the overlay before a target is selected.
        new WindowInteropHelper(_overlay).EnsureHandle();
        WarningText.Text = string.Join(" ", new[] { _hotkeyWarning, _overlay.AffinityWarning }.Where(s => !string.IsNullOrWhiteSpace(s)));
        RefreshWindows(this, new RoutedEventArgs());
        _timer.Start();
    }

    private void RefreshWindows(object sender, RoutedEventArgs e)
    {
        nint previous = (WindowList.SelectedItem as WindowChoice)?.Handle ?? 0;
        nint test = _testWindow is null ? 0 : new WindowInteropHelper(_testWindow).Handle;
        var windows = WindowCatalog.List(test);
        WindowList.ItemsSource = windows;
        WindowList.SelectedItem = windows.FirstOrDefault(w => w.Handle == previous) ?? windows.FirstOrDefault();
    }

    private async void StartCapture(object sender, RoutedEventArgs e)
    {
        if (_changingCapture || _closing) return;
        if (WindowList.SelectedItem is not WindowChoice choice)
        { StatusText.Text = "Open a game or the test window, then refresh the window list."; return; }
        Native.GetWindowThreadProcessId(choice.Handle, out uint selectedPid);
        if (!Native.IsWindow(choice.Handle) || selectedPid != choice.ProcessId || Native.IsIconic(choice.Handle))
        { StatusText.Text = "The selected window is closed or minimized. Restore it and refresh the list."; return; }
        _changingCapture = true;
        StartButton.IsEnabled = false;
        try
        {
            await StopCurrentCapture();
            if (_closing) return;
            var capture = new WindowCapture();
            try { capture.Start(choice.Handle); }
            catch { await capture.DisposeAsync(); throw; }
            _capture = capture;
            _target = choice;
            _started = Stopwatch.GetTimestamp();
            _frames = 0;
            _requested = true;
            StopButton.IsEnabled = true;
            StatusText.Text = "Waiting for the first frame. Keep the target window restored.";
        }
        catch (Exception ex) { StatusText.Text = $"Cannot start capture: {ex.Message}"; }
        finally { _changingCapture = false; StartButton.IsEnabled = true; }
    }

    private async void StopCapture(object sender, RoutedEventArgs e)
    {
        if (_changingCapture || _closing) return;
        _changingCapture = true;
        StartButton.IsEnabled = false;
        try { await StopCurrentCapture(); StatusText.Text = "Stopped. No frames are being captured."; }
        finally { _changingCapture = false; StartButton.IsEnabled = true; }
    }

    private async Task StopCurrentCapture()
    {
        _controls.Dismiss(false);
        _selector?.Close();
        InvalidateRead();
        ReadButton.IsEnabled = false;
        var capture = _capture;
        _capture = null;
        _target = null;
        _frame = null;
        _region = null;
        _dragStart = null;
        SelectionCanvas.ReleaseMouseCapture();
        _overlay.Hide();
        PreviewImage.Source = null;
        CropImage.Source = null;
        SelectionBox.Visibility = Visibility.Collapsed;
        RegionInfo.Text = "Start capture, then select a region.";
        DiagnosticText.Text = "";
        StopButton.IsEnabled = false;
        if (capture is not null)
        {
            try { await capture.DisposeAsync(); }
            catch (Exception ex) { WarningText.Text = $"Capture cleanup reported an error: {ex.Message}"; }
        }
    }

    private async void Tick(object? sender, EventArgs e)
    {
        if (_tickBusy || _closing || _changingCapture || _capture is null || _target is null) return;
        _tickBusy = true;
        try
        {
            var capture = _capture;
            var target = _target;
            Native.GetWindowThreadProcessId(target.Handle, out uint pid);
            if (!Native.IsWindow(target.Handle) || pid != target.ProcessId)
            { await StopCurrentCapture(); StatusText.Text = "Target closed. Choose another window."; return; }
            string? failure = capture.TakeFailure();
            if (failure is not null)
            { await StopCurrentCapture(); StatusText.Text = failure; return; }
            bool minimized = Native.IsIconic(target.Handle);
            if (minimized) _controls.Dismiss(false);
            if (_selector is { } selector)
            {
                capture.Paused = true;
                // Cancel rather than commit a crop against a window that moved underneath it.
                if (minimized || !Native.TryGetBounds(target.Handle, out var currentBounds) ||
                    !currentBounds.Equals(selector.TargetBounds)) selector.Close();
                return;
            }
            nint foreground = Native.GetForegroundWindow();
            bool gameForeground = foreground == target.Handle;
            bool controlForeground = foreground == _hwnd || foreground == _overlay.Handle || foreground == _controls.Handle;
            capture.Paused = minimized || (!gameForeground && !controlForeground) || _dragStart is not null;
            if (!capture.Paused && capture.TakeFrame() is { } frame)
            {
                _frame = frame;
                PreviewImage.Source = frame;
                _frames++;
                UpdateCrop();
                DrawRegion();
            }
            _controls.UpdateStatus(_frame is not null, _region.HasValue);
            if (_controls.IsVisible && Native.TryGetBounds(target.Handle, out var controlsBounds))
                _controls.Place(controlsBounds);
            bool show = !_controls.IsVisible && OverlayPolicy.ShouldShow(_requested, _region.HasValue, true, minimized,
                gameForeground, controlForeground, _editing);
            if (show && Native.TryGetBounds(target.Handle, out var bounds))
            {
                _overlay.Follow(bounds);
                _overlay.ShowForTarget();
            }
            else if (_overlay.IsVisible) _overlay.Hide();
            StatusText.Text = _overlay.InteractionWarning ?? (minimized ? "Paused — restore the target window." :
                _frame is null ? "Waiting for frames — if this persists, the game or remote session may not support capture." :
                _region is null ? "Switch to the game, press Ctrl+Alt+O, then click Select dialogue region." :
                !_requested ? "Overlay hidden. Ctrl+Alt+T shows it again." :
                _editing ? "Edit mode — drag the overlay header or resize its edges. Ctrl+Alt+E returns to reading mode." :
                !gameForeground ? "Ready — switch to the game to see the click-through overlay." :
                "Reading mode — Chinese filler text only. Clicks pass through to the game.");
            var elapsed = Math.Max(1, Stopwatch.GetElapsedTime(_started).TotalSeconds);
            DiagnosticText.Text = $"Preview frames: {_frames} · Session average: {_frames / elapsed:F1} fps (5 fps cap) · {_frame?.PixelWidth ?? 0} × {_frame?.PixelHeight ?? 0} px · {_overlay.InputStatus} · No AI calls";
        }
        catch (Exception ex)
        {
            await StopCurrentCapture();
            StatusText.Text = $"Capture stopped safely: {ex.Message}";
        }
        finally { _tickBusy = false; }
    }

    private Viewport ImageViewport() => _frame is null ? default :
        Viewport.Fit(SelectionCanvas.ActualWidth, SelectionCanvas.ActualHeight, _frame.PixelWidth, _frame.PixelHeight);

    private void BeginSelection(object sender, MouseButtonEventArgs e)
    {
        if (_frame is null) return;
        var point = e.GetPosition(SelectionCanvas);
        if (!ImageViewport().Contains(point.X, point.Y)) return;
        InvalidateRead();
        _dragStart = point;
        SelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void MoveSelection(object sender, MouseEventArgs e)
    {
        if (_dragStart is not Point start) return;
        var end = e.GetPosition(SelectionCanvas);
        var viewport = ImageViewport();
        SetSelectionBox(Math.Clamp(Math.Min(start.X, end.X), viewport.X, viewport.X + viewport.Width),
            Math.Clamp(Math.Min(start.Y, end.Y), viewport.Y, viewport.Y + viewport.Height),
            Math.Clamp(Math.Max(start.X, end.X), viewport.X, viewport.X + viewport.Width),
            Math.Clamp(Math.Max(start.Y, end.Y), viewport.Y, viewport.Y + viewport.Height));
    }

    private void EndSelection(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not Point start || _frame is null) return;
        var end = e.GetPosition(SelectionCanvas);
        var viewport = ImageViewport();
        _dragStart = null;
        SelectionCanvas.ReleaseMouseCapture();
        try
        {
            var region = CaptureRegion.FromDrag((start.X - viewport.X) / viewport.Width,
                (start.Y - viewport.Y) / viewport.Height, (end.X - viewport.X) / viewport.Width,
                (end.Y - viewport.Y) / viewport.Height);
            var pixels = region.ToPixels(_frame.PixelWidth, _frame.PixelHeight);
            if (pixels.Width < 8 || pixels.Height < 8)
                throw new ArgumentOutOfRangeException(nameof(region));
            _region = region;
            UpdateCrop();
            QueueRead();
        }
        catch (ArgumentOutOfRangeException) { StatusText.Text = "Select a larger region (at least 8 × 8 pixels)."; }
        DrawRegion();
    }

    private void SelectionCaptureLost(object sender, MouseEventArgs e)
    {
        if (_dragStart is null) return;
        _dragStart = null;
        DrawRegion();
    }

    private void PreviewResized(object sender, SizeChangedEventArgs e)
    {
        // A drag is in preview coordinates; cancel it if the preview layout changes mid-drag.
        _dragStart = null;
        SelectionCanvas.ReleaseMouseCapture();
        DrawRegion();
    }

    private void DrawRegion()
    {
        if (_region is not CaptureRegion region || _frame is null)
        { SelectionBox.Visibility = Visibility.Collapsed; return; }
        var v = ImageViewport();
        SetSelectionBox(v.X + region.Left * v.Width, v.Y + region.Top * v.Height,
            v.X + region.Right * v.Width, v.Y + region.Bottom * v.Height);
    }

    private void SetSelectionBox(double left, double top, double right, double bottom)
    {
        System.Windows.Controls.Canvas.SetLeft(SelectionBox, left);
        System.Windows.Controls.Canvas.SetTop(SelectionBox, top);
        SelectionBox.Width = Math.Max(0, right - left);
        SelectionBox.Height = Math.Max(0, bottom - top);
        SelectionBox.Visibility = Visibility.Visible;
    }

    private void UpdateCrop()
    {
        if (_region is not CaptureRegion region || _frame is null) return;
        var p = region.ToPixels(_frame.PixelWidth, _frame.PixelHeight);
        var crop = new CroppedBitmap(_frame, new Int32Rect(p.X, p.Y, p.Width, p.Height));
        crop.Freeze();
        CropImage.Source = crop;
        RegionInfo.Text = $"{p.Width} × {p.Height} px at ({p.X}, {p.Y}). Region scales with the window; reselect if the game rearranges its layout.";
    }

    private void ToggleOverlay(object sender, RoutedEventArgs e)
    {
        if (_selector is not null) return;
        _requested = !_requested;
        if (!_requested) _overlay.Hide();
    }

    private void ToggleEdit(object sender, RoutedEventArgs e)
    {
        if (_selector is not null) return;
        _controls.Dismiss(false);
        _editing = !_editing;
        _overlay.SetEditing(_editing);
        EditButton.Content = _editing ? "Finish editing · Ctrl+Alt+E" : "Edit position · Ctrl+Alt+E";
        if (_editing) _requested = true;
        else if (_target is not null && Native.IsWindow(_target.Handle)) Native.SetForegroundWindow(_target.Handle);
    }

    private void ResetOverlay(object sender, RoutedEventArgs e) => _overlay.ResetPosition();

    private void ReturnFocusToGame()
    {
        if (_closing || _target is not { } target || !Native.IsWindow(target.Handle) || Native.IsIconic(target.Handle)) return;
        Native.GetWindowThreadProcessId(target.Handle, out uint pid);
        if (pid == target.ProcessId) Native.SetForegroundWindow(target.Handle);
    }

    private void ToggleControls(object sender, RoutedEventArgs e)
    {
        if (_selector is not null || _closing || _changingCapture) return;
        if (_controls.IsVisible) { _controls.Dismiss(true); return; }
        if (_target is not { } target || _capture is null)
        { StatusText.Text = "Choose a game window and start capture first."; return; }
        nint foreground = Native.GetForegroundWindow();
        if (foreground != target.Handle && foreground != _hwnd && foreground != _overlay.Handle) return;
        if (Native.IsIconic(target.Handle) || !Native.TryGetBounds(target.Handle, out var bounds)) return;
        _overlay.Hide();
        _editing = false;
        _overlay.SetEditing(false);
        EditButton.Content = "Edit position · Ctrl+Alt+E";
        _controls.UpdateStatus(_frame is not null, _region.HasValue);
        _controls.Open(bounds);
    }

    private void SelectOnGame(object sender, RoutedEventArgs e)
    {
        if (_selector is not null || _closing || _changingCapture || _target is null || _capture is null) return;
        var target = _target;
        nint foreground = Native.GetForegroundWindow();
        if (foreground != target.Handle && foreground != _hwnd && foreground != _overlay.Handle && foreground != _controls.Handle) return;
        if (_frame is null || Native.IsIconic(target.Handle) || !Native.TryGetBounds(target.Handle, out var bounds))
        { StatusText.Text = "Wait for a game preview and restore the game before selecting."; return; }
        InvalidateRead();
        _capture.Paused = true;
        _overlay.Hide();
        var selector = new RegionSelectionWindow(_frame, bounds);
        _selector = selector;
        _controls.Dismiss(false);
        try
        {
            // Modeless keeps the control window usable and lets alt-tab cancel without focus theft.
            selector.Closed += (_, _) =>
            {
                _selector = null;
                if (_closing || _target != target || _capture is null) return;
                Native.GetWindowThreadProcessId(target.Handle, out uint currentPid);
                bool targetUnchanged = Native.IsWindow(target.Handle) && currentPid == target.ProcessId &&
                    !Native.IsIconic(target.Handle) && Native.TryGetBounds(target.Handle, out var currentBounds) &&
                    currentBounds.Equals(selector.TargetBounds);
                if (targetUnchanged && selector.SelectedRegion is { } region)
                {
                    _region = region;
                    _requested = true;
                    _editing = false;
                    _overlay.SetEditing(false);
                    EditButton.Content = "Edit position · Ctrl+Alt+E";
                    UpdateCrop();
                    QueueRead();
                    DrawRegion();
                }
                if (selector.ReturnToGame && targetUnchanged) Native.SetForegroundWindow(target.Handle);
            };
            selector.Show();
            selector.Activate();
        }
        catch (Exception ex)
        {
            _selector = null;
            selector.Close();
            StatusText.Text = $"Cannot open region selection: {ex.Message}";
        }
    }

    private void OverlayStyleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded) _overlay.SetAppearance(FontSizeSlider.Value, OpacitySlider.Value);
    }

    private void OpenTestWindow(object sender, RoutedEventArgs e)
    {
        if (_testWindow is null)
        {
            _testWindow = new TestSceneWindow();
            _testWindow.Closed += (_, _) => _testWindow = null;
            _testWindow.Show();
        }
        else { _testWindow.WindowState = WindowState.Normal; _testWindow.Activate(); }
        RefreshWindows(this, new RoutedEventArgs());
        nint handle = new WindowInteropHelper(_testWindow).Handle;
        WindowList.SelectedItem = ((IEnumerable<WindowChoice>)WindowList.ItemsSource).FirstOrDefault(w => w.Handle == handle);
    }

    private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Native.WmHotkey) return 0;
        if (wParam == 1) ToggleOverlay(this, new RoutedEventArgs());
        else if (wParam == 2) ToggleEdit(this, new RoutedEventArgs());
        else if (wParam == 3) SelectOnGame(this, new RoutedEventArgs());
        else if (wParam == 4) ToggleControls(this, new RoutedEventArgs());
        else if (wParam == 5) ReadAgain(this, new RoutedEventArgs());
        handled = true;
        return 0;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeReady) { base.OnClosing(e); return; }
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        if (_toggleHotkey) Native.UnregisterHotKey(_hwnd, 1);
        if (_editHotkey) Native.UnregisterHotKey(_hwnd, 2);
        if (_regionHotkey) Native.UnregisterHotKey(_hwnd, 3);
        if (_controlsHotkey) Native.UnregisterHotKey(_hwnd, 4);
        if (_readHotkey) Native.UnregisterHotKey(_hwnd, 5);
        // Unwind the original Closing event even when there is no asynchronous
        // capture work; calling Close again inside that event is illegal in WPF.
        await Dispatcher.Yield(DispatcherPriority.Background);
        // A start operation may be finishing its previous capture disposal.
        while (_changingCapture || _tickBusy) await Task.Delay(20);
        await StopCurrentCapture();
        if (_readLoop is not null) await _readLoop;
        _ocr.Dispose();
        _overlay.CloseForShutdown();
        _controls.CloseForShutdown();
        _testWindow?.Close();
        _closeReady = true;
        Close();
    }
}

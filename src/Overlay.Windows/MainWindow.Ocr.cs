using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overlay.Ocr;
using Overlay.Windows.Interop;

namespace Overlay.Windows;

public partial class MainWindow
{
    private readonly LocalOcr _ocr = new();
    private BitmapSource? _pendingRead;
    private CancellationTokenSource? _readCancellation;
    private Task? _readLoop;
    private long _readVersion;
    private bool _readHotkey, _pendingAutomatic;

    // All queue state belongs to the dispatcher: one active read and one replaceable pending crop.
    private void InvalidateRead()
    {
        _readVersion++;
        _textGate.Reset();
        _translations?.Invalidate();
        _overlay.SetTranslation("", "等待识别…");
        _pendingRead = null;
        _readCancellation?.Cancel();
        OcrText.Clear();
        OcrStatus.Text = "Finish selecting a region to read it automatically.";
    }

    private void QueueRead(bool automatic = false)
    {
        if (_closing || _frame is null || _region is not { } region) return;
        if (!automatic) InvalidateRead();
        _pendingAutomatic = automatic;
        var p = region.ToPixels(_frame.PixelWidth, _frame.PixelHeight);
        var crop = new CroppedBitmap(_frame, new Int32Rect(p.X, p.Y, p.Width, p.Height));
        crop.Freeze();
        _pendingRead = crop;
        ReadButton.IsEnabled = true;
        OcrStatus.Text = "Reading locally… First read also loads the OCR models.";
        if (_readLoop is null || _readLoop.IsCompleted) _readLoop = ProcessReads();
    }

    private async Task ProcessReads()
    {
        while (_pendingRead is { } crop && !_closing)
        {
            _pendingRead = null;
            long version = _readVersion;
            bool automatic = _pendingAutomatic;
            long started = Stopwatch.GetTimestamp();
            using var cancellation = new CancellationTokenSource();
            _readCancellation = cancellation;
            try
            {
                var result = await Task.Run(() =>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var converted = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                    int stride = checked(converted.PixelWidth * 4);
                    var pixels = new byte[checked(stride * converted.PixelHeight)];
                    converted.CopyPixels(pixels, stride, 0);
                    return _ocr.Read(pixels, converted.PixelWidth, converted.PixelHeight, cancellation.Token);
                });
                if (version != _readVersion || _closing) continue;
                OcrText.Text = result.Text;
                AcceptReading(result.Text, automatic, started);
                OcrStatus.Text = $"{(result.Text.Length == 0 ? "No readable text. Try a tighter crop." : "English recognized — check for OCR mistakes.")} " +
                    $"OCR {result.RecognitionTime.TotalMilliseconds:F0} ms · engine total {result.TotalTime.TotalMilliseconds:F0} ms" +
                    (result.ColdStart ? " (includes model loading)" : " (models warm)");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == _readVersion && !_closing)
                {
                    _textGate.Reset();
                    _translations.Invalidate();
                    _overlay.SetTranslation("", "识别失败 · 请重试");
                    OcrStatus.Text = $"OCR failed: {ex.Message} Try Read again; if models are missing, rebuild or copy the entire publish folder.";
                }
            }
            finally { _readCancellation = null; }
        }
    }

    private void ReadAgain(object sender, RoutedEventArgs e)
    {
        if (_closing || _settingsOpen || _changingCapture || _selector is not null || _dragStart is not null ||
            _capture is null || _target is not { } target || _region is null) return;
        Native.GetWindowThreadProcessId(target.Handle, out uint pid);
        if (!Native.IsWindow(target.Handle) || pid != target.ProcessId || Native.IsIconic(target.Handle)) return;
        nint foreground = Native.GetForegroundWindow();
        if (foreground != target.Handle && foreground != _hwnd && foreground != _controls.Handle && foreground != _overlay.Handle) return;
        if (_capture.TakeFrame() is { } frame)
        {
            _frame = frame;
            PreviewImage.Source = frame;
            UpdateCrop();
        }
        QueueRead();
    }
}

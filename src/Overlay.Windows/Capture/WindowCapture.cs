using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overlay.Windows.Interop;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace Overlay.Windows.Capture;

/// <summary>Bounded 5 fps CPU preview, not a full-rate recording pipeline.</summary>
internal sealed class WindowCapture : IAsyncDisposable
{
    private readonly SemaphoreSlim _frameGate = new(1, 1);
    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private System.Threading.Timer? _sampleTimer;
    private BitmapSource? _latest;
    private string? _failure;
    private SizeInt32 _size;
    private volatile bool _stopping;
    public volatile bool Paused;

    public BitmapSource? TakeFrame() => Interlocked.Exchange(ref _latest, null);
    public string? TakeFailure() => Interlocked.Exchange(ref _failure, null);

    public void Start(nint hwnd)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows Graphics Capture is unavailable in this session.");
        if (_pool is not null) throw new InvalidOperationException("Capture has already started.");
        _device = CaptureInterop.CreateDevice();
        _item = CaptureInterop.CreateItem(hwnd);
        _size = _item.Size;
        if (_size.Width <= 0 || _size.Height <= 0)
            throw new InvalidOperationException("Restore the target window before starting capture.");
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
        _item.Closed += ItemClosed;
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;
        _session.StartCapture();
        // Sample on a timer instead of dropping FrameArrived events inside a throttle.
        // A final text change on a static screen must still get picked up on the next tick.
        _sampleTimer = new System.Threading.Timer(SampleFrame, null, 0, 200);
    }

    private void ItemClosed(GraphicsCaptureItem sender, object args) =>
        Interlocked.Exchange(ref _failure, "The target window closed. Select a window and start again.");

    private async void SampleFrame(object? state)
    {
        if (_stopping || Paused || !await _frameGate.WaitAsync(0)) return;
        try
        {
            if (_stopping || Paused || _pool is not { } pool) return;
            SizeInt32 contentSize;
            using (var frame = TakeNewestFrame(pool))
            {
                if (frame is null) return;
                contentSize = frame.ContentSize;
                if (contentSize.Width <= 0 || contentSize.Height <= 0) return;
                bool resized = contentSize.Width != _size.Width || contentSize.Height != _size.Height;
                if (!resized)
                {
                    using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
                    // Surface size can differ during a resize. Never publish an inconsistent frame.
                    if (bitmap.PixelWidth == contentSize.Width && bitmap.PixelHeight == contentSize.Height)
                    {
                        int stride = checked(bitmap.PixelWidth * 4);
                        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
                        bitmap.CopyToBuffer(pixels.AsBuffer());
                        var image = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96,
                            PixelFormats.Bgr32, null, pixels, stride);
                        image.Freeze();
                        if (!_stopping) Interlocked.Exchange(ref _latest, image);
                    }
                }
            }
            // Release the frame before recreating its pool.
            if (!_stopping && (contentSize.Width != _size.Width || contentSize.Height != _size.Height))
            {
                pool.Recreate(_device!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, contentSize);
                _size = contentSize;
            }
        }
        catch (Exception ex)
        {
            if (!_stopping) Interlocked.Exchange(ref _failure, $"Capture stopped: {ex.Message}");
        }
        finally { _frameGate.Release(); }
    }

    private static Direct3D11CaptureFrame? TakeNewestFrame(Direct3D11CaptureFramePool pool)
    {
        var frame = pool.TryGetNextFrame();
        try
        {
            // The pool holds two buffers. Drain only that bounded batch, not an
            // unbounded stream from an actively rendering game.
            var next = pool.TryGetNextFrame();
            if (next is not null)
            {
                var previous = frame;
                frame = next;
                previous?.Dispose();
            }
            return frame;
        }
        catch { frame?.Dispose(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        _sampleTimer?.Dispose();
        _sampleTimer = null;
        if (_item is not null) _item.Closed -= ItemClosed;
        // Let a pending GPU-to-CPU copy finish before releasing its resources.
        await _frameGate.WaitAsync();
        try
        {
            var session = _session;
            var pool = _pool;
            var device = _device;
            _session = null;
            _pool = null;
            _device = null;
            _item = null;
            Interlocked.Exchange(ref _latest, null);
            try { session?.Dispose(); }
            finally
            {
                try { pool?.Dispose(); }
                finally { device?.Dispose(); }
            }
        }
        finally { _frameGate.Release(); }
    }
}

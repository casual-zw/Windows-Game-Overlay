using System.Diagnostics;
using System.Runtime.InteropServices;
using RapidOcrNet;
using SkiaSharp;

namespace Overlay.Ocr;

public sealed record OcrReading(string Text, TimeSpan RecognitionTime, TimeSpan TotalTime, bool ColdStart);

/// <summary>Single-consumer CPU engine. Call off the UI thread; dispose after the consumer stops.</summary>
public sealed class LocalOcr : IDisposable
{
    private RapidOcr? _engine;

    public OcrReading Read(byte[] bgra, int width, int height, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (width < 1 || height < 1 || bgra.Length != checked(width * height * 4))
            throw new ArgumentException("Expected tightly packed BGRA pixels.");
        var total = Stopwatch.StartNew();
        bool cold = _engine is null;
        if (_engine is null)
        {
            string models = Path.Combine(AppContext.BaseDirectory, "models", "v5");
            using var options = RapidOcr.GetDefaultSessionOptions(2);
            options.InterOpNumThreads = 1;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
            options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
            var engine = new RapidOcr();
            try
            {
                engine.InitModels(Path.Combine(models, "ch_PP-OCRv5_mobile_det.onnx"),
                    Path.Combine(models, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                    Path.Combine(models, "latin_PP-OCRv5_rec_mobile_infer.onnx"),
                    Path.Combine(models, "ppocrv5_latin_dict.txt"), options);
                _engine = engine;
            }
            catch { engine.Dispose(); throw; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Marshal.Copy(bgra, 0, bitmap.GetPixels(), bgra.Length);
        var recognition = Stopwatch.StartNew();
        var result = _engine.Detect(bitmap, RapidOcrOptions.Default with
        {
            DoAngle = false,
            RecMaxDegreeOfParallelism = 1
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new OcrReading(result.StrRes.Trim(), recognition.Elapsed, total.Elapsed, cold);
    }

    public void Dispose() { _engine?.Dispose(); _engine = null; }
}

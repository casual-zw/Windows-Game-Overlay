using System.Runtime.InteropServices;
using Overlay.Ocr;
using SkiaSharp;

using var engine = new LocalOcr();
using var typeface = SKTypeface.FromFamilyName("Arial");
using var font = new SKFont(typeface, 30);
using var paint = new SKPaint { IsAntialias = true };
var lines = new[] { "Doctor, the city is safe tonight.", "We must return before sunrise.", "Our next mission starts tomorrow." };
int failed = 0;
foreach (bool dark in new[] { false, true })
{
    using var bitmap = new SKBitmap(800, 190, SKColorType.Bgra8888, SKAlphaType.Opaque);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(dark ? SKColors.Black : SKColors.White);
    paint.Color = dark ? SKColors.White : SKColors.Black;
    for (int i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], 25, 45 + i * 55, font, paint);
    var result = Read(bitmap);
    Console.WriteLine($"{(dark ? "Dark" : "Light")} paragraph: {result.RecognitionTime.TotalMilliseconds:F0} ms OCR, {result.TotalTime.TotalMilliseconds:F0} ms total, cold={result.ColdStart}\n{result.Text}");
    Check(lines.All(line => result.Text.Contains(line, StringComparison.OrdinalIgnoreCase)), "Three lines recognized in order", result.Text.IndexOf("Doctor", StringComparison.OrdinalIgnoreCase) < result.Text.IndexOf("mission", StringComparison.OrdinalIgnoreCase));
}
using (var blank = new SKBitmap(800, 190, SKColorType.Bgra8888, SKAlphaType.Opaque))
{
    blank.Erase(SKColors.White);
    Check(Read(blank).Text.Length == 0, "Blank region has no text");
}
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { engine.Read(new byte[4], 1, 1, cancelled.Token); Check(false, "Cancellation"); }
    catch (OperationCanceledException) { Check(true, "Cancellation"); }
}
return failed == 0 ? 0 : 1;

OcrReading Read(SKBitmap bitmap)
{
    var pixels = new byte[bitmap.ByteCount];
    Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
    return engine.Read(pixels, bitmap.Width, bitmap.Height, CancellationToken.None);
}
void Check(bool passed, string name, bool additional = true)
{
    passed &= additional;
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
    if (!passed) failed++;
}

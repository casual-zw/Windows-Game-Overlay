namespace Overlay.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Coordinates relative to the captured frame, independent of monitor DPI.</summary>
public readonly record struct CaptureRegion
{
    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }

    public CaptureRegion(double left, double top, double right, double bottom)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) ||
            !double.IsFinite(right) || !double.IsFinite(bottom) ||
            left < 0 || top < 0 || right > 1 || bottom > 1 || left >= right || top >= bottom)
            throw new ArgumentOutOfRangeException(nameof(left), "Region must have positive area inside the frame.");
        (Left, Top, Right, Bottom) = (left, top, right, bottom);
    }

    public static CaptureRegion FromDrag(double x1, double y1, double x2, double y2) => new(
        Math.Clamp(Math.Min(x1, x2), 0, 1), Math.Clamp(Math.Min(y1, y2), 0, 1),
        Math.Clamp(Math.Max(x1, x2), 0, 1), Math.Clamp(Math.Max(y1, y2), 0, 1));

    public PixelRect ToPixels(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (Right <= Left || Bottom <= Top)
            throw new InvalidOperationException("No capture region selected.");
        var x = Math.Clamp((int)Math.Floor(Left * width), 0, width - 1);
        var y = Math.Clamp((int)Math.Floor(Top * height), 0, height - 1);
        var right = Math.Clamp((int)Math.Ceiling(Right * width), x + 1, width);
        var bottom = Math.Clamp((int)Math.Ceiling(Bottom * height), y + 1, height);
        return new(x, y, right - x, bottom - y);
    }
}

public readonly record struct Viewport(double X, double Y, double Width, double Height)
{
    public static Viewport Fit(double areaWidth, double areaHeight, int pixelWidth, int pixelHeight)
    {
        if (!double.IsFinite(areaWidth) || !double.IsFinite(areaHeight) ||
            areaWidth <= 0 || areaHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
            return default;
        double scale = Math.Min(areaWidth / pixelWidth, areaHeight / pixelHeight);
        // Clamp floating-point overshoot so letterboxing never starts outside the viewport.
        double width = Math.Min(areaWidth, pixelWidth * scale);
        double height = Math.Min(areaHeight, pixelHeight * scale);
        return new((areaWidth - width) / 2, (areaHeight - height) / 2, width, height);
    }

    public bool Contains(double x, double y) => Width > 0 && Height > 0 &&
        x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

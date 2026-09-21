using Overlay.Core;

// Dependency-free executable test harness. A failing assertion exits nonzero in CI.
int passed = 0;
var cases = new (string Name, Action Test)[]
{
    ("Full region includes last pixel", () => Equal(new PixelRect(0, 0, 1920, 1080), new CaptureRegion(0, 0, 1, 1).ToPixels(1920, 1080))),
    ("Reverse drag normalizes direction", () => Equal(new CaptureRegion(.1, .2, .9, .8), CaptureRegion.FromDrag(.9, .8, .1, .2))),
    ("Drag outside frame clamps to edge", () => Equal(new CaptureRegion(0, 0, 1, 1), CaptureRegion.FromDrag(-.5, -.2, 1.5, 1.2))),
    ("Zero-area selection rejected", () => Throws<ArgumentOutOfRangeException>(() => CaptureRegion.FromDrag(.2, .3, .2, .3))),
    ("NaN rejected", () => Throws<ArgumentOutOfRangeException>(() => new CaptureRegion(double.NaN, 0, 1, 1))),
    ("Infinite coordinate rejected", () => Throws<ArgumentOutOfRangeException>(() => new CaptureRegion(0, 0, double.PositiveInfinity, 1))),
    ("Uninitialized region rejected", () => Throws<InvalidOperationException>(() => default(CaptureRegion).ToPixels(1920, 1080))),
    ("Invalid frame dimensions rejected", () => Throws<ArgumentOutOfRangeException>(() => new CaptureRegion(0, 0, 1, 1).ToPixels(0, 1080))),
    ("Fractional edges round outward", () => Equal(new PixelRect(10, 20, 81, 61), new CaptureRegion(.101, .201, .901, .801).ToPixels(100, 100))),
    ("Selection scales with frame", () => Equal(new PixelRect(200, 400, 1600, 1200), new CaptureRegion(.1, .2, .9, .8).ToPixels(2000, 2000))),
    ("Tiny edge selection remains in bounds", () => Equal(new PixelRect(99, 99, 1, 1), new CaptureRegion(.999, .999, 1, 1).ToPixels(100, 100))),
    ("Letterboxed preview coordinates", () => Equal(new Viewport(0, 218.75, 1000, 562.5), Viewport.Fit(1000, 1000, 1920, 1080))),
    ("Pillarboxed preview coordinates", () => Equal(new Viewport(250, 0, 500, 500), Viewport.Fit(1000, 500, 100, 100))),
    ("Letterbox bars are not selectable", () => Equal(false, Viewport.Fit(1000, 1000, 1920, 1080).Contains(500, 100))),
    ("Empty preview is not selectable", () => Equal(false, default(Viewport).Contains(0, 0))),
    ("Unmeasured preview rejects non-finite size", () => Equal(default(Viewport), Viewport.Fit(double.NaN, 500, 100, 100))),
    ("Infinite preview rejects non-finite size", () => Equal(default(Viewport), Viewport.Fit(500, double.PositiveInfinity, 100, 100))),
    ("Reading overlay visible over game", () => Equal(true, OverlayPolicy.ShouldShow(true, true, true, false, true, false, false))),
    ("Alt-tab hides reading overlay", () => Equal(false, OverlayPolicy.ShouldShow(true, true, true, false, false, false, false))),
    ("Control window alone does not show reading overlay", () => Equal(false, OverlayPolicy.ShouldShow(true, true, true, false, false, true, false))),
    ("Edit mode allows control window", () => Equal(true, OverlayPolicy.ShouldShow(true, true, true, false, false, true, true))),
    ("Edit mode still hides over unrelated apps", () => Equal(false, OverlayPolicy.ShouldShow(true, true, true, false, false, false, true))),
    ("Minimized target hides even in edit mode", () => Equal(false, OverlayPolicy.ShouldShow(true, true, true, true, false, true, true))),
    ("Requested hide wins", () => Equal(false, OverlayPolicy.ShouldShow(false, true, true, false, true, false, true))),
    ("No region means no overlay", () => Equal(false, OverlayPolicy.ShouldShow(true, false, true, false, true, false, false))),
    ("Closed target hides overlay", () => Equal(false, OverlayPolicy.ShouldShow(true, true, false, false, true, false, false))),
    ("Randomized crops never exceed their frame", () =>
    {
        var random = new Random(42);
        for (int i = 0; i < 10000; i++)
        {
            int width = random.Next(1, 7681), height = random.Next(1, 4321);
            double left = random.NextDouble() * .99, top = random.NextDouble() * .99;
            double right = left + (1 - left) * Math.Max(.00001, random.NextDouble());
            double bottom = top + (1 - top) * Math.Max(.00001, random.NextDouble());
            var p = new CaptureRegion(left, top, right, bottom).ToPixels(width, height);
            Equal(true, p.X >= 0 && p.Y >= 0 && p.Width > 0 && p.Height > 0 &&
                p.X + p.Width <= width && p.Y + p.Height <= height);
        }
    })
};

foreach (var (name, test) in cases)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{passed}/{cases.Length} tests passed.");
return passed == cases.Length ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

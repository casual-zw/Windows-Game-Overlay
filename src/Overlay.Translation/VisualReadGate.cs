namespace Overlay.Translation;

/// <summary>Local grayscale sampling scheduler; all calls use one monotonic clock.</summary>
public sealed class VisualReadGate
{
    private byte[]? _reference;
    private TimeSpan _changedAt, _lastRead;
    private bool _dirty;
    public long Version { get; private set; }
    public void Reset() { _reference = null; _dirty = false; Version++; }

    public void Observe(byte[] grayscale, TimeSpan now)
    {
        bool changed = _reference is null || _reference.Length != grayscale.Length;
        if (!changed)
        {
            int different = 0;
            for (int i = 0; i < grayscale.Length; i++)
                if (Math.Abs(grayscale[i] - _reference![i]) >= 20) different++;
            // Compare against the last significant change, so small cumulative changes count.
            changed = different >= Math.Max(2, (int)Math.Ceiling(grayscale.Length * 0.001));
        }
        if (!changed) return;
        if (_reference is null) _lastRead = now;
        _reference = (byte[])grayscale.Clone();
        _changedAt = now;
        _dirty = true;
        Version++;
    }

    public bool TryRead(TimeSpan now, out bool visuallyStable)
    {
        visuallyStable = _dirty && now - _changedAt >= TimeSpan.FromMilliseconds(300);
        if (!_dirty || (!visuallyStable && now - _lastRead < TimeSpan.FromMilliseconds(900))) return false;
        _lastRead = now;
        // Moving artwork keeps the periodic fallback alive. Settled regions stop polling OCR.
        if (visuallyStable) _dirty = false;
        return true;
    }
}

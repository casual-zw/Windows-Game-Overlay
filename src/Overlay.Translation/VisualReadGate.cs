namespace Overlay.Translation;

/// <summary>Local grayscale sampling scheduler; all calls use one monotonic clock.</summary>
public sealed class VisualReadGate
{
    private byte[]? _reference;
    private TimeSpan _changedAt, _lastRead;
    private TimeSpan? _confirmationAt;
    private bool _dirty, _immediateRead, _largeChangeSeen;
    public long Version { get; private set; }
    public void Reset() { _reference = null; _confirmationAt = null; _dirty = _immediateRead = _largeChangeSeen = false; Version++; }

    // OCR requests a follow-up only while a new text candidate needs confirmation.
    public void ConfirmTextAt(TimeSpan? when) => _confirmationAt = when;

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
            // A large change gets one early read per burst of motion. Continuing animation
            // still uses the bounded fallback until a settled read rearms this fast path.
            if (!_largeChangeSeen && different >= Math.Max(2, (int)Math.Ceiling(grayscale.Length * 0.05)))
            {
                _immediateRead = true;
                _largeChangeSeen = true;
            }
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
        bool confirm = _confirmationAt is { } at && now >= at;
        if (!confirm && (!_dirty || (!_immediateRead && !visuallyStable && now - _lastRead < TimeSpan.FromMilliseconds(900)))) return false;
        _lastRead = now;
        _confirmationAt = null;
        _immediateRead = false;
        // Moving artwork keeps the periodic fallback alive. Settled regions stop polling OCR.
        if (visuallyStable) _dirty = _largeChangeSeen = false;
        return true;
    }
}

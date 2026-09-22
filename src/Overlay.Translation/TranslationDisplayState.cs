namespace Overlay.Translation;

/// <summary>Briefly retains a completed translation while its replacement is prepared.</summary>
public sealed class TranslationDisplayState
{
    public static TimeSpan RetentionDuration { get; } = TimeSpan.FromMilliseconds(600);
    private TimeSpan? _retainedSince;
    public string Text { get; private set; } = "";
    public bool IsPrevious { get; private set; }

    public void Show(string text)
    {
        Text = text;
        IsPrevious = false;
        _retainedSince = null;
    }

    public void Reset() => Show("");

    public void BeginUpdate(TimeSpan now)
    {
        IsPrevious = Text.Length > 0;
        // Further OCR changes, retries, or errors must not extend the old result's lifetime.
        if (IsPrevious) _retainedSince ??= now;
    }

    public bool ExpireRetained(TimeSpan now)
    {
        if (_retainedSince is not { } since || now - since < RetentionDuration) return false;
        Reset();
        return true;
    }
}

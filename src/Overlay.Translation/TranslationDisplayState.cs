namespace Overlay.Translation;

/// <summary>Keeps a completed translation readable while its replacement is prepared.</summary>
public sealed class TranslationDisplayState
{
    private TimeSpan? _blankSince;
    public string Text { get; private set; } = "";
    public bool IsPrevious { get; private set; }

    public void Show(string text)
    {
        Text = text;
        IsPrevious = false;
        _blankSince = null;
    }

    public void Reset() => Show("");

    public void BeginUpdate()
    {
        IsPrevious = Text.Length > 0;
        _blankSince = null;
    }

    public void SourceChanged(bool hasText, TimeSpan now)
    {
        IsPrevious = Text.Length > 0;
        _blankSince = hasText ? null : _blankSince ?? now;
    }

    public bool ExpireBlank(TimeSpan now)
    {
        if (_blankSince is not { } since || now - since < TimeSpan.FromSeconds(1)) return false;
        Reset();
        return true;
    }
}

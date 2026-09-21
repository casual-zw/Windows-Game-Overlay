using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overlay.Translation;

namespace Overlay.Windows;

public partial class MainWindow
{
    private readonly HttpClient _translationHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private TranslationQueue _translations = null!;
    private string _apiKey = "";
    private bool _rememberKey, _settingsOpen, _autoWasEligible;
    private readonly StableTextGate _textGate = new();
    private readonly Stopwatch _autoClock = Stopwatch.StartNew();
    private readonly VisualReadGate _visualGate = new();
    private TimeSpan _lastVisualCheck;
    private long _sourceStarted;
    private TargetLanguage SelectedTargetLanguage => TargetLanguageBox.SelectedItem as TargetLanguage ?? TargetLanguage.SimplifiedChinese;

    private void InitializeTranslation()
    {
        TargetLanguageBox.ItemsSource = TargetLanguage.Supported;
        TargetLanguageBox.SelectedIndex = 0;
        _translations = new(new LunaTranslator(_translationHttp));
        _translations.Updated += (version, status, result) =>
        {
            if (_closing || _settingsOpen || version != _translations.Version) return;
            TranslationStatus.Text = status + (result is null ? "" : $" · read-to-display {Stopwatch.GetElapsedTime(_sourceStarted).TotalMilliseconds:F0} ms");
            _overlay.SetTranslation(result?.Text ?? "", result is null ? status : "简体中文 · Luna");
        };
        _translations.UsageChanged += UpdateUsage;
        try { _apiKey = ApiKeyStore.Load(); _rememberKey = _apiKey.Length > 0; }
        catch { TranslationStatus.Text = "Saved key could not be read. Open Translation settings to replace or forget it."; }
        _overlay.SetTranslation("", "等待翻译 · 请启用翻译并选择区域");
        UpdateUsage();
    }

    private void UpdateUsage()
    {
        UsageText.Text = $"API attempts {_translations.Attempts}/{_translations.SessionLimit} · cache {_translations.CacheHits} · tokens {_translations.InputTokens} in / {_translations.OutputTokens} out · estimated ${_translations.EstimatedCost:F4}" +
            (_translations.UnknownUsage > 0 ? $" + {_translations.UnknownUsage} attempts with unknown usage" : "");
    }

    private void TranslationOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _translations is null) return;
        InvalidateRead();
        TranslationStatus.Text = EnableTranslation.IsChecked == true
            ? (_apiKey.Length == 0 ? "Enter an API key in Translation settings." : "Ready. Select a region or Read again.")
            : "Translation disabled. Local OCR remains available.";
    }

    private async void OpenTranslationSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsOpen || _closing) return;
        _settingsOpen = true;
        _autoWasEligible = false;
        InvalidateRead();
        // Wait for any cancelled translation to leave the single-request queue before testing.
        await _translations.Completion;
        if (_closing) { _settingsOpen = false; return; }
        var dialog = new TranslationSettingsWindow(_apiKey, _rememberKey, _translations.SessionLimit, async key =>
        {
            if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace)) return "Enter a valid key first.";
            string status = "Connection test cancelled.";
            void Result(long _, string message, TranslationResult? result) => status = result is null ? message : "Connected to Luna successfully.";
            _translations.Updated += Result;
            try
            {
                _translations.Invalidate(true);
                _translations.Submit("Hello, doctor.", SelectedTargetLanguage, key);
                await _translations.Completion;
            }
            finally { _translations.Updated -= Result; _translations.Invalidate(true); }
            return status;
        }) { Owner = this };
        try
        {
            if (dialog.ShowDialog() == true)
            {
                _apiKey = dialog.Key; _rememberKey = dialog.Remember; _translations.SessionLimit = dialog.RequestLimit;
                if (_apiKey.Length == 0) EnableTranslation.IsChecked = false;
            }
        }
        finally
        {
            InvalidateRead();
            _translations.Invalidate(true);
            await _translations.Completion;
            _settingsOpen = false;
            UpdateUsage();
        }
    }

    private void AcceptReading(string text, bool automatic, long started, bool visuallyStable)
    {
        var observation = _textGate.Observe(text, _autoClock.Elapsed, manual: !automatic, visuallyStable: visuallyStable);
        if (observation.Changed)
        {
            _translations.Invalidate();
            _sourceStarted = started;
            _overlay.SetTranslation("", observation.Text.Length == 0 ? "未识别到文字" : "等待文字稳定…");
        }
        if (!observation.Ready || EnableTranslation.IsChecked != true || _settingsOpen) return;
        if (_apiKey.Length == 0) { TranslationStatus.Text = "Enter an API key in Translation settings."; return; }
        _translations.Submit(observation.Text, SelectedTargetLanguage, _apiKey);
    }

    private void AutoReadTick(bool eligible)
    {
        if (_settingsOpen) return; // Connection testing owns the queue while settings are open.
        if (!eligible)
        {
            if (_autoWasEligible || _readLoop is { IsCompleted: false } || !_translations.Completion.IsCompleted) InvalidateRead();
            _autoWasEligible = false;
            return;
        }
        _autoWasEligible = true;
        if (AutoRead.IsChecked != true || _frame is null || _region is not { } region) return;
        var now = _autoClock.Elapsed;
        if (now - _lastVisualCheck < TimeSpan.FromMilliseconds(190)) return;
        _lastVisualCheck = now;
        var p = region.ToPixels(_frame.PixelWidth, _frame.PixelHeight);
        var crop = new CroppedBitmap(_frame, new Int32Rect(p.X, p.Y, p.Width, p.Height));
        // Keep narrow text changes visible while bounding comparison work to 640 × 640 pixels.
        double scale = Math.Min(1, 640.0 / Math.Max(crop.PixelWidth, crop.PixelHeight));
        BitmapSource small = scale < 1 ? new TransformedBitmap(crop, new ScaleTransform(scale, scale)) : crop;
        var gray = new FormatConvertedBitmap(small, PixelFormats.Gray8, null, 0);
        var pixels = new byte[checked(gray.PixelWidth * gray.PixelHeight)];
        gray.CopyPixels(pixels, gray.PixelWidth, 0);
        _visualGate.Observe(pixels, now);
        if (_readLoop is { IsCompleted: false }) return;
        if (_visualGate.TryRead(now, out bool stable)) QueueRead(automatic: true, visuallyStable: stable);
    }
}

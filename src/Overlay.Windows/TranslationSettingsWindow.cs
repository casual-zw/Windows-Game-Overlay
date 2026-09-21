using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Overlay.Translation;

namespace Overlay.Windows;

internal static class ApiKeyStore
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameOverlay", "api-key.dpapi");
    internal static string Load()
    {
        if (!File.Exists(FilePath)) return "";
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    internal static void Save(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var bytes = Encoding.UTF8.GetBytes(key);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath + ".tmp", encrypted);
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    internal static void Delete() { File.Delete(FilePath); File.Delete(FilePath + ".tmp"); }
}

internal sealed class TranslationSettingsWindow : Window
{
    internal string Key { get; private set; }
    internal bool Remember { get; private set; }
    internal int RequestLimit { get; private set; }
    private readonly PasswordBox _key = new() { Margin = new Thickness(0, 6, 0, 10), MaxLength = 512 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    internal TranslationSettingsWindow(string key, bool remember, int limit, Func<string, Task<string>> test)
    {
        Key = key; Remember = remember; RequestLimit = limit;
        Title = "Translation settings"; Width = 530; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "OpenAI API key · GPT-5.6 Luna", FontSize = 20 });
        panel.Children.Add(new TextBlock { Text = "Recognized English is sent to OpenAI when translation is enabled. Screenshots stay local. Connection testing makes a small paid translation request.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) });
        _key.Password = key; panel.Children.Add(_key);
        var save = new CheckBox { Content = "Remember on this Windows account (encrypted)", IsChecked = remember };
        panel.Children.Add(save);
        panel.Children.Add(new TextBlock { Text = "Maximum network attempts this app session (1–10,000)", Margin = new Thickness(0, 12, 0, 4) });
        var cap = new TextBox { Text = limit.ToString() }; panel.Children.Add(cap);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(buttons);
        var testButton = new Button { Content = "Test connection" }; buttons.Children.Add(testButton);
        testButton.Click += async (_, _) =>
        {
            testButton.IsEnabled = false;
            try { _status.Text = await test(_key.Password.Trim()); }
            finally { testButton.IsEnabled = true; }
        };
        var apply = new Button { Content = "Apply", IsDefault = true }; buttons.Children.Add(apply);
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(cap.Text, out int n) || n < 1 || n > 10000)
            { _status.Text = "Enter a request limit from 1 to 10,000."; return; }
            var value = _key.Password.Trim();
            if (value.Any(char.IsWhiteSpace)) { _status.Text = "The key must not contain whitespace."; return; }
            try
            {
                if (save.IsChecked == true && value.Length > 0) ApiKeyStore.Save(value); else ApiKeyStore.Delete();
                Key = value; Remember = save.IsChecked == true && value.Length > 0; RequestLimit = n;
                DialogResult = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            { _status.Text = "Could not update saved credentials. Check Windows account storage permissions."; }
        };
        var forget = new Button { Content = "Forget key" }; buttons.Children.Add(forget);
        forget.Click += (_, _) =>
        {
            try { ApiKeyStore.Delete(); Key = ""; Remember = false; _key.Clear(); DialogResult = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _status.Text = "Could not remove the saved key. Check Windows account storage permissions."; }
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true }; buttons.Children.Add(cancel);
        panel.Children.Add(_status);
        Closed += (_, _) => _key.Clear();
    }
}

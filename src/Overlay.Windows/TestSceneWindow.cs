using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Overlay.Windows;

/// <summary>Independent, in-process capture target; no game assets or network required.</summary>
internal sealed class TestSceneWindow : Window
{
    private readonly TextBlock _dialogue = new() { FontSize = 26, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
    private readonly TextBlock _counter = new() { Foreground = Brushes.LightSkyBlue, FontSize = 18 };
    private readonly DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly string[] _lines =
    [
        "Doctor, the road ahead is long. We should rest before continuing our journey.",
        "Do not open the northern gate. Wait until all 3 scouts have returned.",
        "Choose a route:\n1. Cross the old bridge.\n2. Follow the river through the forest.",
        "This is a longer dialogue paragraph for checking wrapping, region selection, and window resizing. The Chinese panel contains fixed sample text; it is not translating these words."
    ];
    private int _line, _characters, _clicks;

    public TestSceneWindow()
    {
        Title = "Overlay test scene";
        Width = 1000; Height = 700; MinWidth = 550; MinHeight = 450;
        Background = new SolidColorBrush(Color.FromRgb(20, 29, 48));
        var grid = new Grid { Margin = new Thickness(28) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = "CAPTURE TEST SCENE\nResize this window, move it between displays, or minimize it.",
            Foreground = Brushes.LightGray, FontSize = 20, TextWrapping = TextWrapping.Wrap
        };
        grid.Children.Add(heading);
        var dialogueBox = new Border { Padding = new Thickness(24), Background = Brushes.Black,
            CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Bottom, Child = _dialogue };
        Grid.SetRow(dialogueBox, 1); grid.Children.Add(dialogueBox);
        var controls = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var buttons = new WrapPanel();
        var next = new Button { Content = "Next line / typewriter" };
        next.Click += (_, _) => { _line = (_line + 1) % _lines.Length; _characters = 0; _dialogue.Text = ""; _typing.Start(); };
        var click = new Button { Content = "Click-through test — put the overlay over this button" };
        click.Click += (_, _) => { _clicks++; UpdateCounter(); };
        buttons.Children.Add(next); buttons.Children.Add(click);
        controls.Children.Add(buttons); controls.Children.Add(_counter);
        Grid.SetRow(controls, 2); grid.Children.Add(controls);
        Content = grid;
        _dialogue.Text = _lines[0]; UpdateCounter();
        _typing.Tick += (_, _) =>
        {
            var text = _lines[_line];
            _characters = Math.Min(_characters + 1, text.Length);
            _dialogue.Text = text[.._characters];
            if (_characters == text.Length) _typing.Stop();
        };
        Closed += (_, _) => _typing.Stop();
    }

    private void UpdateCounter() => _counter.Text = $"Successful clicks: {_clicks}";
}

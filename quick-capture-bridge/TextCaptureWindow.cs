using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace QuickCaptureBridge;

public sealed class TextCaptureWindow : Window
{
    private readonly WpfTextBox _input;
    private readonly Action<string> _save;

    public TextCaptureWindow(Action<string> save)
    {
        _save = save;
        Width = 480;
        Height = 170;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var border = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(28, 31, 36)), CornerRadius = new CornerRadius(12), Padding = new Thickness(16) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Quick capture", Foreground = WpfBrushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _input = new WpfTextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, FontSize = 15, Padding = new Thickness(8), Background = new SolidColorBrush(WpfColor.FromRgb(45, 49, 58)), Foreground = WpfBrushes.White, BorderThickness = new Thickness(0) };
        _input.PreviewKeyDown += OnKeyDown;
        stack.Children.Add(_input);
        stack.Children.Add(new TextBlock { Text = "Enter saves  •  Shift+Enter adds a line  •  Esc cancels", Foreground = WpfBrushes.Gray, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        border.Child = stack;
        Content = border;
        Loaded += (_, _) => _input.Focus();
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            var text = _input.Text.Trim();
            if (text.Length > 0) _save(text);
        }
    }
}

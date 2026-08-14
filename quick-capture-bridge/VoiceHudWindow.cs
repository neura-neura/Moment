using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace QuickCaptureBridge;

public sealed class VoiceHudWindow : Window
{
    private readonly DateTimeOffset _startedAt;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _timer;
    private readonly Action _stop;
    private readonly Action _cancel;

    public VoiceHudWindow(DateTimeOffset startedAt, Action stopAction, Action cancel)
    {
        _startedAt = startedAt;
        _stop = stopAction;
        _cancel = cancel;
        Width = 310;
        Height = 86;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var border = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(28, 31, 36)), CornerRadius = new CornerRadius(12), Padding = new Thickness(14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = WpfBrushes.IndianRed, Margin = new Thickness(0, 0, 8, 0) });
        _status = new TextBlock { Text = "Recording 00:00", Foreground = WpfBrushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_status);
        grid.Children.Add(info);
        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var stopButton = new WpfButton { Content = "Stop", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(10, 0, 4, 0) };
        stopButton.Click += (_, _) => _stop();
        var cancelButton = new WpfButton { Content = "Cancel", Padding = new Thickness(8, 3, 8, 3) };
        cancelButton.Click += (_, _) => _cancel();
        buttons.Children.Add(stopButton);
        buttons.Children.Add(cancelButton);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        border.Child = grid;
        Content = border;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateTime();
        Loaded += (_, _) => { Activate(); _timer.Start(); UpdateTime(); };
        Closed += (_, _) => _timer.Stop();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; _cancel(); } };
    }

    public void SetSaving() => _status.Text = "Saving…";

    private void UpdateTime()
    {
        var elapsed = DateTimeOffset.Now - _startedAt;
        _status.Text = $"Recording {Math.Max(0, (int)elapsed.TotalMinutes):00}:{elapsed.Seconds:00}";
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;

namespace QuickCaptureBridge;

public sealed class SettingsWindow : Window
{
    private bool _allowClose;
    private readonly BridgeSettings _settings;
    private readonly WpfComboBox _voiceHotkey;
    private readonly WpfComboBox _textHotkey;
    private readonly WpfTextBox _vaultPath;
    private readonly WpfCheckBox _startup;
    private readonly Action<BridgeSettings> _save;
    private readonly Func<string?> _browse;

    public SettingsWindow(BridgeSettings settings, Action<BridgeSettings> save, Func<string?> browse)
    {
        _settings = new BridgeSettings
        {
            VaultPath = settings.VaultPath,
            VoiceHotkey = settings.VoiceHotkey,
            TextHotkey = settings.TextHotkey,
            StartWithWindows = settings.StartWithWindows,
            AudioFolder = settings.AudioFolder
        };
        _save = save;
        _browse = browse;
        Title = "Quick Capture Bridge";
        Width = 520;
        Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "Quick Capture Bridge", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(new TextBlock { Text = "Global F13–F24 capture without keeping Obsidian open.", Foreground = WpfBrushes.Gray, Margin = new Thickness(0, 0, 0, 18) });

        root.Children.Add(new TextBlock { Text = "Obsidian vault", FontWeight = FontWeights.SemiBold });
        var vaultRow = new DockPanel { Margin = new Thickness(0, 4, 0, 14) };
        _vaultPath = new WpfTextBox { Text = _settings.VaultPath, IsReadOnly = true, Padding = new Thickness(6) };
        DockPanel.SetDock(_vaultPath, Dock.Left);
        vaultRow.Children.Add(_vaultPath);
        var browseButton = new WpfButton { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
        browseButton.Click += (_, _) => { var path = _browse(); if (path is not null) _vaultPath.Text = path; };
        DockPanel.SetDock(browseButton, Dock.Right);
        vaultRow.Children.Add(browseButton);
        root.Children.Add(vaultRow);

        _voiceHotkey = MakeHotkeyRow(root, "Voice hotkey", _settings.VoiceHotkey);
        _textHotkey = MakeHotkeyRow(root, "Text hotkey", _settings.TextHotkey);
        _startup = new WpfCheckBox { Content = "Start bridge with Windows", IsChecked = _settings.StartWithWindows, Margin = new Thickness(0, 14, 0, 16) };
        root.Children.Add(_startup);
        var saveButton = new WpfButton { Content = "Save and enable global hotkeys", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Padding = new Thickness(14, 7, 14, 7) };
        saveButton.Click += (_, _) => Save();
        root.Children.Add(saveButton);
        Content = root;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        Hide();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private WpfComboBox MakeHotkeyRow(WpfPanel root, string label, int selectedCode)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center });
        var combo = new WpfComboBox { ItemsSource = HotkeyOptions.All, DisplayMemberPath = "Name", SelectedValuePath = "Code", SelectedValue = selectedCode, Width = 120, Padding = new Thickness(5) };
        row.Children.Add(combo);
        root.Children.Add(row);
        return combo;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_vaultPath.Text) || !Directory.Exists(_vaultPath.Text))
        {
            System.Windows.MessageBox.Show("Choose an existing Obsidian vault folder.", "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_voiceHotkey.SelectedValue is not int voice || _textHotkey.SelectedValue is not int text || voice == text)
        {
            System.Windows.MessageBox.Show("Choose two different F13–F24 keys.", "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.VaultPath = _vaultPath.Text;
        _settings.VoiceHotkey = voice;
        _settings.TextHotkey = text;
        _settings.StartWithWindows = _startup.IsChecked == true;
        _save(_settings);
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using NAudio.Wave;
using WpfApplication = System.Windows.Application;

namespace QuickCaptureBridge;

public sealed class BridgeController : IDisposable
{
    private const int VoiceHotkeyId = 1;
    private const int TextHotkeyId = 2;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly WpfApplication _application;
    private readonly bool _background;
    private readonly SettingsStore _store = new();
    private readonly HwndSource _messageWindow;
    private readonly Forms.NotifyIcon _tray;
    private BridgeSettings _settings;
    private VaultInbox _inbox;
    private SettingsWindow? _settingsWindow;
    private TextCaptureWindow? _textWindow;
    private VoiceHudWindow? _voiceWindow;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _voicePath;
    private DateTimeOffset _voiceStartedAt;
    private bool _cancelVoice;
    private bool _voiceStopping;
    private bool _disposed;

    public BridgeController(WpfApplication application, bool background)
    {
        _application = application;
        _background = background;
        _settings = _store.Load();
        _inbox = new VaultInbox(_settings);
        var parameters = new HwndSourceParameters("QuickCaptureBridgeHotkeys") { Width = 0, Height = 0 };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WindowProc);
        _tray = CreateTrayIcon();
    }

    public void Start()
    {
        RegisterHotkeys();
        if (!_background || !_inbox.IsConfigured) ShowSettings();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, SaveSettings, BrowseForVault);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelVoice();
        _settingsWindow?.CloseForExit();
        _textWindow?.Close();
        _voiceWindow?.Close();
        UnregisterHotkeys();
        _tray.Visible = false;
        _tray.Dispose();
        _messageWindow.RemoveHook(WindowProc);
        _messageWindow.Dispose();
    }

    private void SaveSettings(BridgeSettings settings)
    {
        try
        {
            if (settings.VoiceHotkey == settings.TextHotkey) throw new InvalidOperationException("Voice and text hotkeys must be different.");
            _settings = settings;
            _inbox = new VaultInbox(_settings);
            _store.Save(_settings);
            StartupManager.SetEnabled(_settings.StartWithWindows);
            RegisterHotkeys();
            _settingsWindow?.Close();
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show(error.Message, "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string? BrowseForVault()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose the Obsidian vault used by Quick Capture Bridge",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_settings.VaultPath) ? _settings.VaultPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void ToggleVoice()
    {
        if (_waveIn is null) StartVoice();
        else StopVoice(false);
    }

    private void StartVoice()
    {
        try
        {
            if (!_inbox.IsConfigured) { ShowSettings(); return; }
            _voiceStartedAt = DateTimeOffset.Now;
            _voicePath = _inbox.EnsureAudioPath(_voiceStartedAt);
            _cancelVoice = false;
            _voiceStopping = false;
            _waveWriter = new WaveFileWriter(_voicePath, new WaveFormat(16_000, 16, 1));
            _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16_000, 16, 1), BufferMilliseconds = 50 };
            _waveIn.DataAvailable += OnAudioData;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            _voiceWindow = new VoiceHudWindow(_voiceStartedAt, () => StopVoice(false), CancelVoice);
            _voiceWindow.Show();
        }
        catch (Exception error)
        {
            CleanupVoice(deleteFile: true);
            System.Windows.MessageBox.Show(error.Message, "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopVoice(bool cancel)
    {
        if (_waveIn is null || _voiceStopping) return;
        _cancelVoice = cancel;
        _voiceStopping = true;
        _voiceWindow?.SetSaving();
        _waveIn.StopRecording();
    }

    private void CancelVoice()
    {
        if (_waveIn is null)
        {
            _voiceWindow?.Close();
            _voiceWindow = null;
            return;
        }
        StopVoice(true);
    }

    private void OnAudioData(object? sender, WaveInEventArgs args) => _waveWriter?.Write(args.Buffer, 0, args.BytesRecorded);

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        _application.Dispatcher.Invoke(() =>
        {
            var path = _voicePath;
            var startedAt = _voiceStartedAt;
            var cancelled = _cancelVoice;
            _voiceStopping = false;
            _voiceWindow?.Close();
            _voiceWindow = null;
            CleanupVoice(deleteFile: cancelled);
            if (args.Exception is not null)
            {
                System.Windows.MessageBox.Show(args.Exception.Message, "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (!cancelled && path is not null)
            {
                try { _inbox.WriteVoice(path, startedAt); }
                catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        });
    }

    private void CleanupVoice(bool deleteFile)
    {
        _waveIn?.Dispose();
        _waveWriter?.Dispose();
        _waveIn = null;
        _waveWriter = null;
        if (deleteFile && _voicePath is not null)
        {
            try { File.Delete(_voicePath); } catch { /* best effort cleanup */ }
        }
        _voicePath = null;
    }

    private void OpenText()
    {
        if (!_inbox.IsConfigured) { ShowSettings(); return; }
        _textWindow?.Close();
        _textWindow = new TextCaptureWindow(text =>
        {
            try
            {
                _inbox.WriteText(text, DateTimeOffset.Now);
                _textWindow?.Close();
                _textWindow = null;
            }
            catch (Exception error)
            {
                System.Windows.MessageBox.Show(error.Message, "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
        _textWindow.Closed += (_, _) => _textWindow = null;
        _textWindow.Show();
        _textWindow.Activate();
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        var handle = _messageWindow.Handle;
        if (!RegisterHotKey(handle, VoiceHotkeyId, ModNoRepeat, (uint)_settings.VoiceHotkey))
            System.Windows.MessageBox.Show("The selected voice hotkey is already registered by another application.", "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        if (!RegisterHotKey(handle, TextHotkeyId, ModNoRepeat, (uint)_settings.TextHotkey))
            System.Windows.MessageBox.Show("The selected text hotkey is already registered by another application.", "Quick Capture Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void UnregisterHotkeys()
    {
        if (_messageWindow.Handle != IntPtr.Zero)
        {
            UnregisterHotKey(_messageWindow.Handle, VoiceHotkeyId);
            UnregisterHotKey(_messageWindow.Handle, TextHotkeyId);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey)
        {
            handled = true;
            if (wParam.ToInt32() == VoiceHotkeyId) ToggleVoice();
            else if (wParam.ToInt32() == TextHotkeyId) OpenText();
        }
        return IntPtr.Zero;
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => _application.Shutdown());
        return new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Quick Capture Bridge",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

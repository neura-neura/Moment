using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NAudio.Wave;
using System.Diagnostics;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace Moment;

public sealed class BridgeController : IDisposable
{
    private const int VoiceId = 1;
    private const int TextId = 2;
    private BridgeSettings settings;
    private VaultInbox inbox;
    private readonly NativeVoiceProcessor voiceProcessor;
    private NativeHotkeyWindow? hook;
    private bool voiceRegistered;
    private bool textRegistered;
    private VoicePiP? voicePiP;
    private TextPiP? textPiP;

    public event Action<string, bool>? StatusChanged;
    public bool VoiceRegistered => voiceRegistered;
    public bool TextRegistered => textRegistered;
    public string LastStatus { get; private set; } = "";
    public bool LastStatusSucceeded { get; private set; }

    public int RetryFailedJobs() => voiceProcessor.RetryFailedJobs();

    public BridgeController(BridgeSettings initialSettings)
    {
        settings = initialSettings;
        inbox = new VaultInbox(settings);
        voiceProcessor = new NativeVoiceProcessor(settings);
        voiceProcessor.StatusChanged += OnVoiceProcessorStatusChanged;
        hook = new NativeHotkeyWindow();
        hook.HotkeyPressed += HotkeyPressed;
        Register(settings);
    }

    public void SuspendHotkeysForCapture() => Unregister();

    public void Register(BridgeSettings next)
    {
        Unregister();
        settings = next;
        inbox = new VaultInbox(settings);
        voiceProcessor.UpdateSettings(settings);

        var voiceError = "The native hotkey window is unavailable.";
        var voiceOk = hook?.Register(VoiceId, settings.VoiceHotkey, out voiceError) == true;
        voiceRegistered = voiceOk;

        var textError = "Voice and text shortcuts must be different.";
        var duplicate = settings.TextHotkey.VirtualKey == settings.VoiceHotkey.VirtualKey &&
                        settings.TextHotkey.Modifiers == settings.VoiceHotkey.Modifiers;
        var textOk = !duplicate && hook?.Register(TextId, settings.TextHotkey, out textError) == true;
        textRegistered = textOk;

        if (!voiceOk || !textOk)
        {
            var voiceState = voiceOk ? "registered" : voiceError;
            var textState = textOk ? "registered" : textError;
            SetStatus($"Voice: {voiceState}  Text: {textState}", false);
        }
        else
        {
            SetStatus($"Registered {HotkeyFormatter.Format(settings.VoiceHotkey)} for voice and {HotkeyFormatter.Format(settings.TextHotkey)} for text.", true);
        }
    }

    public void ShowVoicePiP()
    {
        if (voicePiP is not null)
        {
            voicePiP.StopAndSave();
            return;
        }
        if (!inbox.IsConfigured)
        {
            SetStatus("Choose an existing Obsidian vault before recording.", false);
            return;
        }

        try
        {
            var panel = new VoicePiP(inbox, settings);
            panel.Saved += path =>
            {
                SetStatus(settings.NativeProcessingEnabled ? "Voice note saved; processing in the background." : $"Voice note saved to {path}.", true);
                voiceProcessor.Schedule();
            };
            panel.Failed += message => SetStatus(message, false);
            panel.Closed += (_, _) => voicePiP = null;
            voicePiP = panel;
            panel.Activate();
        }
        catch (Exception error)
        {
            SetStatus($"Voice capture could not start: {error.Message}", false);
        }
    }

    public void ShowTextPiP()
    {
        if (textPiP is not null)
        {
            // The shortcut is a bring-to-front action while the editor is
            // already open. Saving is explicit (Ctrl+Enter) so a repeated
            // hardware key press cannot discard an unfinished thought.
            textPiP.BringToFrontAndFocus();
            return;
        }
        if (!inbox.IsConfigured)
        {
            SetStatus("Choose an existing Obsidian vault before writing a note.", false);
            return;
        }

        var panel = new TextPiP(inbox, settings);
        panel.Saved += path => SetStatus($"Text capture saved to {path}.", true);
        panel.Failed += message => SetStatus(message, false);
        panel.Closed += (_, _) => textPiP = null;
        textPiP = panel;
        panel.Activate();
        panel.BringToFrontAndFocus();
    }

    public void Dispose()
    {
        Unregister();
        if (hook is not null)
        {
            hook.HotkeyPressed -= HotkeyPressed;
            hook.Dispose();
            hook = null;
        }
        voicePiP?.Close();
        textPiP?.Close();
        voicePiP = null;
        textPiP = null;
        voiceProcessor.StatusChanged -= OnVoiceProcessorStatusChanged;
        voiceProcessor.Dispose();
    }

    private void HotkeyPressed(int id) => App.CurrentWindow.DispatcherQueue.TryEnqueue(() =>
    {
        if (id == VoiceId) ShowVoicePiP();
        if (id == TextId) ShowTextPiP();
    });

    private void Unregister()
    {
        hook?.Unregister(VoiceId);
        hook?.Unregister(TextId);
        voiceRegistered = false;
        textRegistered = false;
    }

    private void SetStatus(string message, bool succeeded)
    {
        LastStatus = message;
        LastStatusSucceeded = succeeded;
        StatusChanged?.Invoke(message, succeeded);
    }

    private void OnVoiceProcessorStatusChanged(string message, bool succeeded) => SetStatus(message, succeeded);
}

public sealed class TextPiP : Window
{
    private readonly VaultInbox inbox;
    private readonly BridgeSettings settings;
    private readonly TextBox input;
    private readonly Border surface;
    private readonly ScaleTransform scale = new() { ScaleX = 0.94, ScaleY = 0.94 };
    private bool saving;
    private bool isActive;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? closeTimer;

    public event Action<string>? Saved;
    public event Action<string>? Failed;

    public void BringToFrontAndFocus()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeWindowUtilities.ConfigureOverlay(hwnd, 320, 192, OverlayPlacement.BottomRight, true);
        NativeWindowUtilities.Activate(hwnd);
        QueueFocus();
    }

    public TextPiP(VaultInbox inbox, BridgeSettings settings)
    {
        this.inbox = inbox;
        this.settings = settings;
        Title = "";
        input = new TextBox
        {
            // This is a freeform writing surface. Enter saves, Shift+Enter
            // inserts a new line, and Ctrl+Enter is an explicit save chord.
            AcceptsReturn = true,
            PlaceholderText = "Write a quick note...",
            Height = double.NaN,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(8),
            FontSize = 15,
            VerticalContentAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        input.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(InputKeyDown), true);
        ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(input, ScrollBarVisibility.Disabled);

        surface = new Border
        {
            Width = 320,
            Height = 192,
            Padding = new Thickness(8),
            // The HWND receives the standard Windows rounded corners from DWM;
            // this content surface deliberately does not impose its own shape.
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(1),
            BorderBrush = NativeBorderBrush(),
            Background = NativeSurfaceBrush(),
            RenderTransform = scale,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            Child = input
        };
        Content = surface;
        input.Loaded += (_, _) => QueueFocus();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                isActive = false;
                Cancel();
                return;
            }
            isActive = true;
            var hwnd = WindowNative.GetWindowHandle(this);
            NativeWindowUtilities.ConfigureOverlay(hwnd, 320, 192, OverlayPlacement.BottomRight, true);
            NativeWindowUtilities.Activate(hwnd);
            AnimateIn();
            QueueFocus();
        };
    }

    private void QueueFocus()
    {
        void FocusEditor()
        {
            if (saving || !isActive) return;
            input.Focus(FocusState.Programmatic);
            input.SelectionStart = input.Text.Length;
            input.SelectionLength = 0;
            input.Focus(FocusState.Keyboard);
        }

        DispatcherQueue.TryEnqueue(FocusEditor);
        foreach (var delay in new[] { 40, 120 })
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(delay);
            timer.Tick += (_, _) => { timer.Stop(); FocusEditor(); };
            timer.Start();
        }
    }

    private void InputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Cancel();
            return;
        }
        if (args.Key == VirtualKey.Enter)
        {
            var shift = HotkeyCapture.IsModifierDown(VirtualKey.Shift);
            if (!shift && settings.DailyEnterToSave)
            {
                args.Handled = true;
                Save();
            }
        }
    }

    private void Save()
    {
        if (saving) return;
        var value = input.Text.Trim();
        if (value.Length == 0)
        {
            input.Focus(FocusState.Programmatic);
            return;
        }
        saving = true;
        try
        {
            var location = inbox.WriteText(value, DateTimeOffset.Now);
            Saved?.Invoke(location);
            if (settings.DailyCloseAfterSave)
            {
                HideForClose();
                CloseSoon(0);
            }
            else
            {
                saving = false;
                input.ClearValue(TextBox.TextProperty);
                QueueFocus();
            }
        }
        catch (Exception error)
        {
            saving = false;
            Failed?.Invoke($"Text capture could not be saved: {error.Message}");
            input.Focus(FocusState.Programmatic);
        }
    }

    private void Cancel()
    {
        if (saving) return;
        saving = true;
        HideForClose();
        CloseSoon(0);
    }

    private void HideForClose() => NativeWindowUtilities.Hide(WindowNative.GetWindowHandle(this));

    private void CloseSoon(int delayMs)
    {
        closeTimer?.Stop();
        if (delayMs <= 0)
        {
            // Close on the next dispatcher turn so the click/key event can
            // finish without exposing a disabled or half-rendered frame.
            DispatcherQueue.TryEnqueue(Close);
            return;
        }
        closeTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        closeTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        closeTimer.Tick += (_, _) =>
        {
            closeTimer!.Stop();
            AnimateOutAndClose();
        };
        closeTimer.Start();
    }

    private void AnimateIn()
    {
        surface.Opacity = 0;
        scale.ScaleX = 0.94;
        scale.ScaleY = 0.94;
        AnimateSurface(true, 160);
    }

    private void AnimateOutAndClose()
    {
        Close();
    }

    private void AnimateSurface(bool entering, int durationMs)
    {
        var ticks = 0;
        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            ticks++;
            var progress = Math.Min(1d, ticks * 16d / durationMs);
            var eased = 1 - Math.Pow(1 - progress, 3);
            surface.Opacity = entering ? eased : 1 - eased;
            var value = entering ? 0.94 + eased * 0.06 : 1 - eased * 0.06;
            scale.ScaleX = value;
            scale.ScaleY = value;
            if (progress >= 1) timer.Stop();
        };
        timer.Start();
    }

    private static Brush NativeSurfaceBrush()
    {
        var resources = Application.Current.Resources;
        // A solid system surface keeps the writing canvas calm and readable;
        // it also follows the user's light/dark Windows theme without custom
        // corner treatments or a synthetic backdrop.
        foreach (var key in new[] { "CardBackgroundFillColorDefaultBrush", "SolidBackgroundFillColorBaseBrush", "AcrylicInAppFillColorDefaultBrush" })
            if (resources.ContainsKey(key) && resources[key] is Brush brush) return brush;
        return new SolidColorBrush(Colors.Transparent);
    }

    private static Brush NativeBorderBrush()
    {
        var resources = Application.Current.Resources;
        foreach (var key in new[] { "ControlStrokeColorDefaultBrush", "DividerStrokeColorDefaultBrush" })
            if (resources.ContainsKey(key) && resources[key] is Brush brush) return brush;
        return new SolidColorBrush(Colors.Transparent);
    }

}

public sealed class VoicePiP : Window
{
    private const double MinimumDetectedAudioLevel = 0.002;
    private static Brush NativeSurfaceBrush()
    {
        var resources = Application.Current.Resources;
        foreach (var key in new[] { "AcrylicInAppFillColorDefaultBrush", "SolidBackgroundFillColorBaseBrush", "CardBackgroundFillColorDefaultBrush" })
            if (resources.ContainsKey(key) && resources[key] is Brush brush) return brush;
        return new SolidColorBrush(Colors.Transparent);
    }

    private static Brush NativeBorderBrush()
    {
        var resources = Application.Current.Resources;
        foreach (var key in new[] { "ControlStrokeColorDefaultBrush", "DividerStrokeColorDefaultBrush" })
            if (resources.ContainsKey(key) && resources[key] is Brush brush) return brush;
        return new SolidColorBrush(Colors.Transparent);
    }

    private readonly VaultInbox inbox;
    private readonly BridgeSettings settings;
    private readonly List<Border> waveBars = new();
    private readonly Border surface;
    private readonly InfoBar noSignalInfoBar;
    private readonly ScaleTransform scale = new() { ScaleX = 0.9, ScaleY = 0.9 };
    private readonly object writerLock = new();
    private readonly WaveFormat recordingFormat = new(16_000, 16, 1);
    private WaveInEvent? input;
    private Process? encoder;
    private Stream? encoderInput;
    private string? audioPath;
    private DateTimeOffset startedAt;
    private bool stopping;
    private bool started;
    private double audioLevel;
    private bool detectedAudio;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? waveTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? closeTimer;
    private int animationTick;
    private bool noSignalWarningShown;
    private int? encoderExitCode;
    private string? encoderError;
    private readonly Button stopButton;
    private readonly Button cancelButton;

    public event Action<string>? Saved;
    public event Action<string>? Failed;

    public VoicePiP(VaultInbox inbox, BridgeSettings settings)
    {
        this.inbox = inbox;
        this.settings = settings;
        Title = "";
        var recordingIndicator = new Grid
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Stroke = NativeRecordingBrush(),
                    StrokeThickness = 1.5,
                    Fill = new SolidColorBrush(Colors.Transparent),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = NativeRecordingBrush(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        var waves = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 20
        };
        for (var i = 0; i < 5; i++)
        {
            var bar = new Border { Width = 2, Height = 6 + (i % 3) * 3, CornerRadius = new CornerRadius(1), Background = NativeAccentBrush(), VerticalAlignment = VerticalAlignment.Center };
            waveBars.Add(bar);
            waves.Children.Add(bar);
        }

        stopButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { new SymbolIcon { Symbol = Symbol.Stop }, new TextBlock { Text = "Stop", VerticalAlignment = VerticalAlignment.Center } }
            },
            Width = 62,
            Height = 36,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0)
        };
        ToolTipService.SetToolTip(stopButton, "Stop and save");
        AutomationProperties.SetName(stopButton, "Stop and save");
        stopButton.Click += (_, _) => StopAndSave();
        cancelButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { new SymbolIcon { Symbol = Symbol.Cancel }, new TextBlock { Text = "Cancel", VerticalAlignment = VerticalAlignment.Center } }
            },
            Width = 76,
            Height = 36,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0)
        };
        ToolTipService.SetToolTip(cancelButton, "Cancel recording");
        AutomationProperties.SetName(cancelButton, "Cancel recording");
        cancelButton.Click += (_, _) => CancelRecording();

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { stopButton, cancelButton }
        };

        var recordingGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children = { recordingIndicator, waves, actions }
        };
        Grid.SetColumn(waves, 1);
        Grid.SetColumn(actions, 2);

        noSignalInfoBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            IsIconVisible = true,
            Severity = InfoBarSeverity.Warning,
            Title = "No audio detected",
            Message = "Check the selected microphone.",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 0)
        };
        AutomationProperties.SetName(noSignalInfoBar, "No audio detected. Check the selected microphone.");

        var recordingLayout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(60) },
                new RowDefinition { Height = GridLength.Auto }
            },
            Children = { recordingGrid, noSignalInfoBar }
        };
        Grid.SetRow(noSignalInfoBar, 1);

        surface = new Border
        {
            // With the status copy removed, keep the island deliberately compact
            // while leaving enough room for the native Stop and Cancel actions.
            Width = 260,
            Height = 60,
            Padding = new Thickness(8, 6, 8, 6),
            // Use the same native DWM corner treatment as the text panel;
            // the island is compact by size, not by a custom capsule region.
            CornerRadius = new CornerRadius(0),
            Background = NativeSurfaceBrush(),
            BorderBrush = NativeBorderBrush(),
            BorderThickness = new Thickness(1),
            RenderTransform = scale,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            Child = recordingLayout
        };
        Content = surface;
        Activated += (_, _) =>
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            NativeWindowUtilities.ConfigureOverlay(hwnd, 260, noSignalInfoBar.IsOpen ? 128 : 60, OverlayPlacement.CenterTop, false);
            AnimateIn();
            if (!started)
            {
                started = true;
                StartRecording();
            }
        };
        Closed += (_, _) =>
        {
            if (!stopping && input is not null) FinishAfterWindowClose();
        };
    }

    public void StopAndSave()
    {
        if (stopping) return;
        stopping = true;
        try
        {
            input?.StopRecording();
            DisposeRecorder();
            if (audioPath is null) throw new InvalidOperationException("No audio file was created.");
            if (!Volatile.Read(ref detectedAudio))
            {
                DeleteAudioArtifact();
                ShowNoAudioDetected();
                return;
            }
            EnsureEncodedAudio();
            HideForClose();
            inbox.WriteVoice(audioPath, startedAt);
            Saved?.Invoke(audioPath);
            CloseSoon(0);
        }
        catch (Exception error)
        {
            DeleteAudioArtifact();
            HideForClose();
            Failed?.Invoke($"Voice capture could not be saved: {error.Message}");
            CloseSoon(0);
        }
    }

    private void CancelRecording()
    {
        if (stopping) return;
        stopping = true;
        HideForClose();
        input?.StopRecording();
        DisposeRecorder();
        DeleteAudioArtifact();
        CloseSoon(0);
    }

    private void StartRecording()
    {
        try
        {
            startedAt = DateTimeOffset.Now;
            audioPath = inbox.EnsureAudioPath(startedAt);
            encoderExitCode = null;
            encoderError = null;
            detectedAudio = false;
            audioLevel = 0;
            noSignalWarningShown = false;
            noSignalInfoBar.IsOpen = false;
            surface.Height = 60;
            encoder = StartWebmEncoder(audioPath, recordingFormat, settings.AudioBitsPerSecond);
            encoderInput = encoder.StandardInput.BaseStream;
            var inputDeviceIndex = AudioInputDevices.ResolveIndex(settings.AudioInputDevice);
            if (inputDeviceIndex < 0) throw new InvalidOperationException("No microphone input device is available. Connect a microphone and try again.");
            input = new WaveInEvent { DeviceNumber = inputDeviceIndex, WaveFormat = recordingFormat };
            input.DataAvailable += OnDataAvailable;
            input.StartRecording();
            waveTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            waveTimer.Interval = TimeSpan.FromMilliseconds(90);
            waveTimer.Tick += (_, _) => AnimateWaves();
            waveTimer.Start();
        }
        catch (Exception error)
        {
            DisposeRecorder();
            if (audioPath is not null) try { File.Delete(audioPath); } catch { }
            audioPath = null;
            stopping = true;
            Failed?.Invoke($"Voice capture could not start: {error.Message}");
            CloseSoon(1200);
        }
    }

    private void AnimateWaves()
    {
        if (stopping) return;
        animationTick++;
        var level = Math.Clamp(Volatile.Read(ref audioLevel), 0d, 1d);
        for (var i = 0; i < waveBars.Count; i++)
        {
            var phase = (animationTick + i * 2) % 8;
            var motion = Math.Abs(phase - 4) / 4d;
            waveBars[i].Height = 7 + level * 22 + motion * 5;
        }

        if (!Volatile.Read(ref detectedAudio) && !noSignalWarningShown && DateTimeOffset.Now - startedAt >= TimeSpan.FromSeconds(2))
            ShowNoSignalWarning(final: false);
        else if (Volatile.Read(ref detectedAudio) && noSignalWarningShown)
            HideNoSignalWarning();
    }

    private void ShowNoAudioDetected()
    {
        const string title = "No audio detected";
        const string detail = "Recording discarded. Check the selected microphone.";
        Failed?.Invoke($"{title}. {detail}");
        ShowNoSignalWarning(final: true);
        CloseSoon(1800);
    }

    private void ShowNoSignalWarning(bool final)
    {
        noSignalWarningShown = true;
        noSignalInfoBar.Title = "No audio detected";
        noSignalInfoBar.Message = final
            ? "Recording discarded. Check the selected microphone."
            : "Speak or check the selected microphone.";
        noSignalInfoBar.IsOpen = true;
        surface.Height = 128;
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeWindowUtilities.ConfigureOverlay(hwnd, 260, 128, OverlayPlacement.CenterTop, false);
    }

    private void HideNoSignalWarning()
    {
        noSignalWarningShown = false;
        noSignalInfoBar.IsOpen = false;
        surface.Height = 60;
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeWindowUtilities.ConfigureOverlay(hwnd, 260, 60, OverlayPlacement.CenterTop, false);
    }

    private void AnimateIn()
    {
        surface.Opacity = 0;
        scale.ScaleX = 0.9;
        scale.ScaleY = 0.9;
        AnimateSurface(entering: true, 180);
    }

    private void AnimateSurface(bool entering, int durationMs)
    {
        var ticks = 0;
        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            ticks++;
            var progress = Math.Min(1d, ticks * 16d / durationMs);
            var eased = entering ? 1 - Math.Pow(1 - progress, 3) : Math.Pow(1 - progress, 3);
            surface.Opacity = entering ? eased : 1 - eased;
            var value = entering ? 0.9 + eased * 0.1 : 1 - eased * 0.1;
            scale.ScaleX = value;
            scale.ScaleY = value;
            if (progress >= 1) timer.Stop();
        };
        timer.Start();
    }

    private void HideForClose() => NativeWindowUtilities.Hide(WindowNative.GetWindowHandle(this));

    private void CloseSoon(int delayMs)
    {
        waveTimer?.Stop();
        closeTimer?.Stop();
        if (delayMs <= 0)
        {
            // Do not fade through a disabled-looking frame after Stop/Cancel.
            HideForClose();
            DispatcherQueue.TryEnqueue(Close);
            return;
        }
        closeTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        closeTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        closeTimer.Tick += (_, _) =>
        {
            closeTimer!.Stop();
            Close();
        };
        closeTimer.Start();
    }

    private void FinishAfterWindowClose()
    {
        stopping = true;
        try
        {
            input?.StopRecording();
            DisposeRecorder();
            if (audioPath is not null)
            {
                if (!Volatile.Read(ref detectedAudio))
                {
                    DeleteAudioArtifact();
                    Failed?.Invoke("No audio detected. The silent recording was discarded.");
                    return;
                }
                EnsureEncodedAudio();
                inbox.WriteVoice(audioPath, startedAt);
                Saved?.Invoke(audioPath);
            }
        }
        catch (Exception error)
        {
            DeleteAudioArtifact();
            Failed?.Invoke($"Voice capture could not be saved: {error.Message}");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (writerLock)
        {
            try
            {
                encoderInput?.Write(args.Buffer, 0, args.BytesRecorded);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The encoder can fail independently of the microphone. The
                // stop path reports the captured error to the user.
            }
        }
        var sum = 0d;
        var samples = args.BytesRecorded / 2;
        for (var offset = 0; offset + 1 < args.BytesRecorded; offset += 2)
        {
            var sample = BitConverter.ToInt16(args.Buffer, offset);
            sum += sample * (double)sample;
        }
        if (samples > 0)
        {
            var level = Math.Sqrt(sum / samples) / 12000d;
            Volatile.Write(ref audioLevel, level);
            if (level >= MinimumDetectedAudioLevel) Volatile.Write(ref detectedAudio, true);
        }
    }

    private void DisposeRecorder()
    {
        lock (writerLock)
        {
            if (input is not null) input.DataAvailable -= OnDataAvailable;
            input?.Dispose();
            try { encoderInput?.Dispose(); } catch { }
            encoderInput = null;
            if (encoder is not null)
            {
                try
                {
                    if (!encoder.WaitForExit(5_000))
                    {
                        encoder.Kill(entireProcessTree: true);
                        encoder.WaitForExit();
                    }
                    encoderExitCode = encoder.ExitCode;
                    encoderError = encoder.StandardError.ReadToEnd();
                }
                catch (Exception error)
                {
                    encoderError = error.Message;
                }
                finally
                {
                    encoder.Dispose();
                }
                encoder = null;
            }
            input = null;
        }
    }

    private void EnsureEncodedAudio()
    {
        if (audioPath is null) throw new InvalidOperationException("No audio file was created.");
        if (encoderExitCode is not 0)
        {
            var detail = string.IsNullOrWhiteSpace(encoderError) ? "The WebM encoder did not finish successfully." : encoderError.Trim();
            throw new InvalidOperationException(detail);
        }
        if (!File.Exists(audioPath) || new FileInfo(audioPath).Length < 64)
            throw new InvalidOperationException("The WebM encoder produced an empty audio file.");
    }

    private void DeleteAudioArtifact()
    {
        if (audioPath is null) return;
        try { File.Delete(audioPath); } catch { }
    }

    private static Process StartWebmEncoder(string outputPath, WaveFormat format, int bitsPerSecond)
    {
        var ffmpeg = System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) throw new InvalidOperationException("The bundled WebM encoder is missing.");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("s16le");
        process.StartInfo.ArgumentList.Add("-ar");
        process.StartInfo.ArgumentList.Add(format.SampleRate.ToString());
        process.StartInfo.ArgumentList.Add("-ac");
        process.StartInfo.ArgumentList.Add(format.Channels.ToString());
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add("pipe:0");
        process.StartInfo.ArgumentList.Add("-c:a");
        process.StartInfo.ArgumentList.Add("libopus");
        process.StartInfo.ArgumentList.Add("-b:a");
        process.StartInfo.ArgumentList.Add($"{Math.Clamp(bitsPerSecond, 16_000, 256_000) / 1_000}k");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("webm");
        process.StartInfo.ArgumentList.Add(outputPath);
        process.Start();
        return process;
    }

    private static Brush NativeAccentBrush()
    {
        var resources = Application.Current.Resources;
        foreach (var key in new[] { "AccentFillColorDefaultBrush", "SystemControlHighlightAccentBrush", "SystemControlForegroundAccentBrush" })
            if (resources.ContainsKey(key) && resources[key] is Brush brush) return brush;
        return new SolidColorBrush(Colors.DodgerBlue);
    }

    private static Brush NativeRecordingBrush()
    {
        var resources = Application.Current.Resources;
        foreach (var key in new[] { "SystemFillColorCriticalBrush", "SystemControlErrorTextForegroundBrush" })
            if (resources.ContainsKey(key) && resources[key] is Brush brush) return brush;
        return new SolidColorBrush(Colors.Red);
    }

}


using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace Moment;

public sealed partial class MainPage : Page
{
    public event Action<string, string, bool>? NotificationRequested;
    private readonly SettingsStore settingsStore = new();
    private readonly BridgeSettings settings;
    private readonly BridgeController controller;
    private readonly TextBlock vaultPathText = new();
    private readonly TextBox voiceHotkeyText = new();
    private readonly TextBox textHotkeyText = new();
    private readonly TextBlock voiceStatusText = new();
    private readonly TextBlock textStatusText = new();
    private readonly TextBox dailyHeadingText = new();
    private readonly TextBox dailyTimestampText = new();
    private readonly ComboBox dailyInsertionCombo = new();
    private readonly ComboBox dailyMissingHeadingCombo = new();
    private readonly CheckBox timestampEnabledCheck = new() { Content = "Add capture timestamp" };
    private StackPanel? dailyTargetHeadingField;
    private StackPanel? dailyMissingHeadingField;
    private readonly CheckBox dailyCloseAfterSaveCheck = new() { Content = "Close the quick note after saving" };
    private readonly CheckBox dailyEnterToSaveCheck = new() { Content = "Enter saves (Shift+Enter inserts a new line)" };
    private readonly TextBox audioFolderText = new();
    private readonly ComboBox audioBitrateCombo = new();
    private readonly CheckBox transcriptionCheck = new() { Content = "Transcribe recordings locally with Whisper" };
    private readonly ComboBox whisperLanguageCombo = new();
    private readonly ComboBox whisperModelCombo = new();
    private readonly TextBox transcriptionFolderText = new();
    private readonly CheckBox includeAudioEmbedCheck = new() { Content = "Include the WebM recording as an Obsidian embed" };
    private readonly ComboBox transcriptionDestinationCombo = new();
    private readonly TextBox voicePrefixText = new();
    private readonly TextBlock whisperStatusText = new();
    private readonly Button whisperInstallButton = new();
    private readonly NativeWhisperEngine whisperEngine = new();
    private readonly CheckBox startWithWindowsCheck = new() { Content = "Start with Windows" };
    private readonly CheckBox closeToTrayCheck = new() { Content = "Keep running in the tray when the window is closed", IsChecked = true };
    private readonly UpdateService updateService = new();
    private readonly Button updateButton = new();
    private readonly TextBlock updateStatusText = new();
    private readonly TextBlock statusBar = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Grid sectionHost = new();
    private readonly Dictionary<string, ScrollViewer> sectionPages = new(StringComparer.Ordinal);
    private StackPanel? transcriptionFolderField;

    public MainPage()
    {
        InitializeComponent();
        settings = settingsStore.Load();
        BuildUi();
        controller = new BridgeController(settings);
        controller.StatusChanged += OnStatusChanged;
        UpdateFields();
        _ = RefreshWhisperStatusAsync();
    }

    private void BuildUi()
    {
        var navigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            AlwaysShowHeader = false,
            IsTitleBarAutoPaddingEnabled = false,
            IsPaneOpen = true,
            OpenPaneLength = 220,
            CompactPaneLength = 48,
            IsPaneToggleButtonVisible = true,
            Background = ThemeBrush("AcrylicInAppFillColorDefaultBrush", "SolidBackgroundFillColorBaseBrush")
        };

        var textItem = NavigationItem("Text Note", Symbol.Edit, "text");
        var voiceItem = NavigationItem("Voice", Symbol.Microphone, "voice");
        var shortcutsItem = NavigationItem("Shortcuts", Symbol.Keyboard, "shortcuts");
        var settingsItem = NavigationItem("Settings", Symbol.Setting, "settings");
        navigation.MenuItems.Add(textItem);
        navigation.MenuItems.Add(voiceItem);
        navigation.MenuItems.Add(shortcutsItem);
        navigation.FooterMenuItems.Add(settingsItem);
        navigation.SelectionChanged += NavigationSelectionChanged;

        sectionHost.Children.Clear();
        sectionPages.Clear();
        sectionHost.Background = ThemeBrush("SolidBackgroundFillColorBaseBrush", "AcrylicInAppFillColorDefaultBrush");

        BuildCaptureSection();
        BuildVoiceSection();
        BuildShortcutsSection();
        BuildSettingsSection();

        var contentRoot = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };
        contentRoot.Children.Add(sectionHost);

        var footer = new Border
        {
            Padding = new Thickness(24, 8, 24, 8),
            BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush", "ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush", "SolidBackgroundFillColorBaseBrush")
        };
        var footerGrid = new Grid { ColumnSpacing = 16 };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusBar.VerticalAlignment = VerticalAlignment.Center;
        statusBar.Opacity = 0.72;
        AddGrid(footerGrid, statusBar, 0, 0);
        var save = Button("Save settings", SaveClick);
        save.MinWidth = 128;
        AddGrid(footerGrid, save, 1, 0);
        footer.Child = footerGrid;
        Grid.SetRow(footer, 1);
        contentRoot.Children.Add(footer);

        navigation.Content = contentRoot;
        navigation.SelectedItem = textItem;
        Content = navigation;
        Background = ThemeBrush("SolidBackgroundFillColorBaseBrush", "AcrylicInAppFillColorDefaultBrush");
    }

    private void BuildCaptureSection()
    {
        var content = CreateSection("text", "Text Note", "Write a text note from any app into the selected vault, even while Obsidian is closed.");
        vaultPathText.TextWrapping = TextWrapping.WrapWholeWords;
        vaultPathText.Opacity = 0.72;
        vaultPathText.FontSize = 14;

        var vaultCard = Card();
        vaultCard.Child = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Heading("Selected vault", 18),
                Body("Moment writes directly to this vault, even while Obsidian is closed. Text notes are inserted into the configured daily file, and voice recordings are processed independently."),
                vaultPathText,
                Button("Choose vault...", ChooseVaultClick)
            }
        };
        content.Children.Add(vaultCard);

        var dailyCard = Card();
        dailyInsertionCombo.Items.Add("End of daily note");
        dailyInsertionCombo.Items.Add("Beginning of daily note");
        dailyInsertionCombo.Items.Add("Under a heading");
        dailyMissingHeadingCombo.Items.Add("Create the heading");
        dailyMissingHeadingCombo.Items.Add("Append at the end");
        dailyMissingHeadingCombo.Items.Add("Show an error");
        dailyInsertionCombo.SelectionChanged += (_, _) => UpdateDailyInsertionVisibility();
        ToolTipService.SetToolTip(dailyEnterToSaveCheck, "When enabled, Enter saves the text note and Shift+Enter inserts a new line.");
        ToolTipService.SetToolTip(dailyCloseAfterSaveCheck, "When enabled, the text-note overlay closes after a successful save.");
        dailyTargetHeadingField = Labeled("Target heading", dailyHeadingText, "Heading used by Under a heading. Example: Notes.");
        dailyMissingHeadingField = Labeled("Missing heading", dailyMissingHeadingCombo, "Action when the target heading is missing.");
        dailyCard.Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading("Text note", 18),
                Body("Moment follows Obsidian's configured daily-file folder, filename format, and template. If today's file does not exist, it creates it before inserting the text note."),
                Labeled("Insertion", dailyInsertionCombo, "Where the text note is placed in the daily file."),
                dailyTargetHeadingField,
                dailyMissingHeadingField,
                Labeled("Timestamp format", dailyTimestampText, "Format for capture times. HH:mm produces 22:17."),
                timestampEnabledCheck,
                dailyEnterToSaveCheck,
                dailyCloseAfterSaveCheck
            }
        };
        timestampEnabledCheck.Checked += (_, _) => dailyTimestampText.IsEnabled = true;
        timestampEnabledCheck.Unchecked += (_, _) => dailyTimestampText.IsEnabled = false;
        ToolTipService.SetToolTip(timestampEnabledCheck, "Adds a timestamp heading to text and voice entries. Uncheck it when you want only the note/transcription text.");
        UpdateDailyInsertionVisibility();
        content.Children.Add(dailyCard);

        var flowCard = Card();
        flowCard.Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Heading("How capture works", 18),
                Body("Use the global shortcuts while Obsidian is closed, in a game, or in another application. Text notes create the daily file automatically when needed and use the capture timestamp for insertion.")
            }
        };
        content.Children.Add(flowCard);
    }

    private void BuildShortcutsSection()
    {
        var content = CreateSection("shortcuts", "Shortcuts", "Set the global combinations used for quick text capture and voice notes.");
        var shortcutCard = Card();
        var shortcutPanel = new StackPanel { Spacing = 14 };
        shortcutPanel.Children.Add(Heading("Global shortcut assignments", 18));
        shortcutPanel.Children.Add(Body("F13-F24, Stream Deck keys, letters, arrows, and modifier combinations are supported."));

        var shortcutGrid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        shortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        shortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        shortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddGrid(shortcutGrid, new TextBlock { Text = "Voice capture", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        ConfigureHotkeyBox(voiceHotkeyText, VoiceHotkeyKeyDown);
        AddGrid(shortcutGrid, voiceHotkeyText, 1, 0);
        AddGrid(shortcutGrid, voiceStatusText, 2, 0);
        AddGrid(shortcutGrid, new TextBlock { Text = "Text capture", VerticalAlignment = VerticalAlignment.Center }, 0, 1);
        ConfigureHotkeyBox(textHotkeyText, TextHotkeyKeyDown);
        AddGrid(shortcutGrid, textHotkeyText, 1, 1);
        AddGrid(shortcutGrid, textStatusText, 2, 1);
        shortcutPanel.Children.Add(shortcutGrid);

        var shortcutButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        shortcutButtons.Children.Add(Button("Register shortcuts", RegisterClick));
        shortcutButtons.Children.Add(Button("Test voice shortcut", TestVoiceClick));
        shortcutButtons.Children.Add(Button("Test text shortcut", TestTextClick));
        shortcutPanel.Children.Add(shortcutButtons);
        shortcutCard.Child = shortcutPanel;
        content.Children.Add(shortcutCard);

        var note = Card();
        note.Child = Body("Register shortcuts after changing them. Moment keeps listening even when this settings window is closed to the notification tray.");
        content.Children.Add(note);
    }

    private void BuildVoiceSection()
    {
        var content = CreateSection("voice", "Voice", "Save WebM recordings and, when enabled, transcribe them locally without an Obsidian plugin.");

        audioBitrateCombo.Items.Add("32 kbps");
        audioBitrateCombo.Items.Add("64 kbps");
        audioBitrateCombo.Items.Add("96 kbps");
        foreach (var language in new[] { "auto", "English (en)", "Spanish (es)", "French (fr)", "German (de)", "Italian (it)", "Portuguese (pt)", "Chinese (zh)", "Japanese (ja)", "Korean (ko)", "Russian (ru)" })
            whisperLanguageCombo.Items.Add(language);
        whisperModelCombo.DisplayMemberPath = "Label";
        foreach (var model in NativeWhisperEngine.Models) whisperModelCombo.Items.Add(model);
        transcriptionDestinationCombo.Items.Add("Separate transcription note");
        transcriptionDestinationCombo.Items.Add("Text Note");
        transcriptionDestinationCombo.Items.Add("Both (Text Note + separate note)");
        transcriptionDestinationCombo.SelectionChanged += (_, _) => UpdateTranscriptionFolderVisibility();

        var audioCard = Card();
        audioCard.Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading("Recording", 18),
                Body("Recordings are stored as compact WebM/Opus files inside the selected vault."),
                Labeled("Audio folder", FolderEditor(audioFolderText, ChooseAudioFolderClick), "WebM recordings are saved here. Default: Voice Notes in the selected vault."),
                Labeled("Quality", audioBitrateCombo, "WebM/Opus bitrate. 64 kbps is recommended for speech."),
                includeAudioEmbedCheck
            }
        };
        ToolTipService.SetToolTip(includeAudioEmbedCheck, "Adds an Obsidian link such as ![[Voice Notes/recording.webm]] to the generated voice Markdown note.");
        content.Children.Add(audioCard);

        var transcriptionCard = Card();
        whisperInstallButton.Content = "Install / repair Whisper";
        whisperInstallButton.MinWidth = 164;
        whisperInstallButton.Click += WhisperInstallClick;
        whisperStatusText.Opacity = 0.72;
        ToolTipService.SetToolTip(transcriptionCheck, "After recording, run local Whisper and route the recognized text to the selected destination.");
        transcriptionFolderField = Labeled("Separate note folder", FolderEditor(transcriptionFolderText, ChooseTranscriptionFolderClick), "Transcript Markdown files are saved here. Default: Voice Transcriptions in the selected vault.");
        var transcriptionDestinationField = Labeled("Destination", transcriptionDestinationCombo, "Choose Text Note, a separate note, or both.");
        transcriptionCard.Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading("Local transcription", 18),
                Body("Whisper runs locally and is downloaded only when you enable it. The selected model is stored in your Windows user profile. If a transcription fails, the audio remains in the Audio folder and Moment shows a Windows notification; no audio is copied into the transcription folder."),
                transcriptionCheck,
                Labeled("Language", whisperLanguageCombo, "Language passed to Whisper. Auto detects the spoken language."),
                Labeled("Model", whisperModelCombo, "Local Whisper model. Larger models can be more accurate and slower."),
                transcriptionDestinationField,
                transcriptionFolderField,
                Labeled("Voice prefix", voicePrefixText, "Optional text placed before each transcript. Example: Meeting."),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { whisperInstallButton, Button("Retry failed jobs", RetryFailedClick), whisperStatusText }
                }
            }
        };
        content.Children.Add(transcriptionCard);
        UpdateTranscriptionFolderVisibility();
    }

    private void BuildSettingsSection()
    {
        var content = CreateSection("settings", "Settings", "Choose how Moment starts and what happens when its window is closed.");
        var startupCard = Card();
        startupCard.Child = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Heading("Startup and tray", 18),
                startWithWindowsCheck,
                closeToTrayCheck,
                Body("Start with Windows keeps global capture available after sign-in. Closing this window can hide Moment in the notification tray instead of stopping capture.")
            }
        };
        content.Children.Add(startupCard);

        var privacyCard = Card();
        privacyCard.Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Heading("Native companion", 18),
                Body("Moment runs locally and owns capture routing, Text Note insertion, WebM storage, and Whisper transcription. Obsidian only needs to be open later to display the files.")
            }
        };
        content.Children.Add(privacyCard);

        var updateCard = Card();
        updateButton.Content = "Check for updates";
        updateButton.MinWidth = 148;
        updateButton.Click += UpdateClick;
        updateStatusText.Text = $"Current version: {UpdateService.CurrentVersion}";
        updateStatusText.Opacity = 0.72;
        var authorLink = new HyperlinkButton
        {
            Content = "Made by neura-neura",
            NavigateUri = new Uri(UpdateService.RepositoryUrl),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        updateCard.Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading("Updates", 18),
                Body("Check GitHub for a newer Moment installer. The normal Windows installer will close and update the running app safely."),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { updateButton, updateStatusText }
                },
                authorLink
            }
        };
        content.Children.Add(updateCard);
    }

    private StackPanel CreateSection(string key, string title, string subtitle)
    {
        var content = new StackPanel
        {
            Padding = new Thickness(32, 28, 32, 32),
            Spacing = 18,
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(new StackPanel
        {
            Spacing = 5,
            Children = { Heading(title, 28), Body(subtitle) }
        });

        var scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        sectionPages[key] = scroll;
        sectionHost.Children.Add(scroll);
        return content;
    }

    private void NavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var key = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "text";
        foreach (var pair in sectionPages)
            pair.Value.Visibility = pair.Key == key ? Visibility.Visible : Visibility.Collapsed;
    }

    private static NavigationViewItem NavigationItem(string label, Symbol symbol, string key) => new()
    {
        Content = label,
        Icon = new SymbolIcon(symbol),
        Tag = key
    };

    private void ConfigureHotkeyBox(TextBox box, KeyEventHandler handler)
    {
        box.PlaceholderText = "Press a shortcut";
        box.IsReadOnly = true;
        box.GotFocus += HotkeyFieldGotFocus;
        box.LostFocus += HotkeyFieldLostFocus;
        box.AddHandler(UIElement.KeyDownEvent, handler, true);
    }

    private void UpdateFields()
    {
        vaultPathText.Text = string.IsNullOrWhiteSpace(settings.VaultPath) ? "No vault selected" : settings.VaultPath;
        voiceHotkeyText.Text = HotkeyFormatter.Format(settings.VoiceHotkey);
        textHotkeyText.Text = HotkeyFormatter.Format(settings.TextHotkey);
        dailyInsertionCombo.SelectedIndex = settings.DailyInsertionLocation switch { "beginning" => 1, "under-heading" => 2, _ => 0 };
        dailyHeadingText.Text = settings.DailyTargetHeading;
        dailyMissingHeadingCombo.SelectedIndex = settings.DailyMissingHeadingBehavior switch { "end" => 1, "error" => 2, _ => 0 };
        dailyTimestampText.Text = settings.DailyTimestampFormat;
        timestampEnabledCheck.IsChecked = settings.IncludeTimestamp;
        dailyTimestampText.IsEnabled = settings.IncludeTimestamp;
        dailyEnterToSaveCheck.IsChecked = settings.DailyEnterToSave;
        dailyCloseAfterSaveCheck.IsChecked = settings.DailyCloseAfterSave;
        audioFolderText.Text = settings.AudioFolder;
        audioBitrateCombo.SelectedItem = $"{Math.Clamp(settings.AudioBitsPerSecond, 32_000, 96_000) / 1_000} kbps";
        transcriptionCheck.IsChecked = settings.EnableTranscription;
        whisperLanguageCombo.SelectedItem = LanguageLabel(settings.WhisperLanguage);
        whisperModelCombo.SelectedItem = NativeWhisperEngine.Models.FirstOrDefault(model => string.Equals(model.Id, settings.WhisperModel, StringComparison.OrdinalIgnoreCase)) ?? NativeWhisperEngine.Models[1];
        transcriptionFolderText.Text = settings.TranscriptionFolder;
        includeAudioEmbedCheck.IsChecked = settings.IncludeAudioEmbed;
        transcriptionDestinationCombo.SelectedIndex = settings.TranscriptionDestination switch { "daily-note" => 1, "both" => 2, _ => 0 };
        voicePrefixText.Text = settings.VoicePrefix;
        startWithWindowsCheck.IsChecked = settings.StartWithWindows;
        closeToTrayCheck.IsChecked = settings.CloseToTray;
        voiceStatusText.Text = controller.VoiceRegistered ? "Registered" : "Not registered";
        textStatusText.Text = controller.TextRegistered ? "Registered" : "Not registered";
        statusBar.Text = controller.LastStatus;
    }

    private static string LanguageLabel(string language) => language.Trim().ToLowerInvariant() switch
    {
        "en" => "English (en)",
        "es" => "Spanish (es)",
        "fr" => "French (fr)",
        "de" => "German (de)",
        "it" => "Italian (it)",
        "pt" => "Portuguese (pt)",
        "zh" => "Chinese (zh)",
        "ja" => "Japanese (ja)",
        "ko" => "Korean (ko)",
        "ru" => "Russian (ru)",
        _ => "auto"
    };

    private void UpdateDailyInsertionVisibility()
    {
        var visible = dailyInsertionCombo.SelectedIndex == 2;
        if (dailyTargetHeadingField is not null) dailyTargetHeadingField.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (dailyMissingHeadingField is not null) dailyMissingHeadingField.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTranscriptionFolderVisibility()
    {
        if (transcriptionFolderField is not null)
            transcriptionFolderField.Visibility = transcriptionDestinationCombo.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ChooseVaultClick(object sender, RoutedEventArgs args)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            settings.VaultPath = folder.Path;
            UpdateFields();
            statusBar.Text = "Vault selected. Save settings before capturing.";
        }
    }

    private async void ChooseAudioFolderClick(object sender, RoutedEventArgs args) => await ChooseVaultRelativeFolderAsync(audioFolderText, "Voice Notes");

    private async void ChooseTranscriptionFolderClick(object sender, RoutedEventArgs args) => await ChooseVaultRelativeFolderAsync(transcriptionFolderText, "Voice Transcriptions");

    private async Task ChooseVaultRelativeFolderAsync(TextBox target, string fallback)
    {
        if (string.IsNullOrWhiteSpace(settings.VaultPath) || !Directory.Exists(settings.VaultPath))
        {
            statusBar.Text = "Choose an existing vault before selecting a subfolder.";
            return;
        }
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        try
        {
            var root = Path.GetFullPath(settings.VaultPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var selected = Path.GetFullPath(folder.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relative = Path.GetRelativePath(root, selected);
            if (relative == ".") relative = fallback;
            if (Path.IsPathRooted(relative) || relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }).Any(part => part == ".."))
                throw new InvalidOperationException("Choose a folder inside the selected vault.");
            target.Text = relative.Replace(Path.DirectorySeparatorChar, '/');
            statusBar.Text = $"Folder selected: {target.Text}";
        }
        catch (Exception error)
        {
            statusBar.Text = error.Message;
        }
    }

    private void HotkeyFieldGotFocus(object sender, RoutedEventArgs args)
    {
        controller.SuspendHotkeysForCapture();
        statusBar.Text = "Press the desired shortcut combination.";
    }

    private void HotkeyFieldLostFocus(object sender, RoutedEventArgs args) { }
    private void VoiceHotkeyKeyDown(object sender, KeyRoutedEventArgs args) => CaptureHotkey(args, true);
    private void TextHotkeyKeyDown(object sender, KeyRoutedEventArgs args) => CaptureHotkey(args, false);

    private void CaptureHotkey(KeyRoutedEventArgs args, bool voice)
    {
        try
        {
            var binding = HotkeyCapture.FromKeyEvent(args);
            if (voice) settings.VoiceHotkey = binding;
            else settings.TextHotkey = binding;
            UpdateFields();
            args.Handled = true;
        }
        catch (Exception error)
        {
            statusBar.Text = error.Message;
            args.Handled = true;
        }
    }

    private void RegisterClick(object sender, RoutedEventArgs args)
    {
        controller.Register(settings);
        UpdateFields();
    }

    private void TestVoiceClick(object sender, RoutedEventArgs args) => controller.ShowVoicePiP();
    private void TestTextClick(object sender, RoutedEventArgs args) => controller.ShowTextPiP();
    private void RetryFailedClick(object sender, RoutedEventArgs args)
    {
        var count = controller.RetryFailedJobs();
        statusBar.Text = count == 0 ? "No failed native voice jobs are waiting for retry." : $"Queued {count} failed voice job(s) for retry.";
    }

    private void SaveClick(object sender, RoutedEventArgs args)
    {
        settings.DailyInsertionLocation = dailyInsertionCombo.SelectedIndex switch { 1 => "beginning", 2 => "under-heading", _ => "end" };
        settings.DailyTargetHeading = dailyHeadingText.Text.Trim();
        settings.DailyMissingHeadingBehavior = dailyMissingHeadingCombo.SelectedIndex switch { 1 => "end", 2 => "error", _ => "create" };
        settings.DailyTimestampFormat = string.IsNullOrWhiteSpace(dailyTimestampText.Text) ? "HH:mm" : dailyTimestampText.Text.Trim();
        settings.IncludeTimestamp = timestampEnabledCheck.IsChecked == true;
        settings.DailyEnterToSave = dailyEnterToSaveCheck.IsChecked == true;
        settings.DailyCloseAfterSave = dailyCloseAfterSaveCheck.IsChecked == true;
        settings.AudioFolder = string.IsNullOrWhiteSpace(audioFolderText.Text) ? "Voice Notes" : audioFolderText.Text.Trim();
        settings.AudioBitsPerSecond = audioBitrateCombo.SelectedIndex switch { 0 => 32_000, 2 => 96_000, _ => 64_000 };
        settings.EnableTranscription = transcriptionCheck.IsChecked == true;
        settings.WhisperLanguage = LanguageCode(whisperLanguageCombo.SelectedItem as string);
        settings.WhisperModel = (whisperModelCombo.SelectedItem as WhisperModelInfo)?.Id ?? "base";
        settings.TranscriptionFolder = string.IsNullOrWhiteSpace(transcriptionFolderText.Text) ? "Voice Transcriptions" : transcriptionFolderText.Text.Trim();
        settings.IncludeAudioEmbed = includeAudioEmbedCheck.IsChecked == true;
        settings.TranscriptionDestination = transcriptionDestinationCombo.SelectedIndex switch { 1 => "daily-note", 2 => "both", _ => "separate-note" };
        settings.VoicePrefix = voicePrefixText.Text.Trim();
        settings.NativeProcessingEnabled = true;
        settings.StartWithWindows = startWithWindowsCheck.IsChecked == true;
        settings.CloseToTray = closeToTrayCheck.IsChecked == true;
        settingsStore.Save(settings);
        StartupManager.SetEnabled(settings.StartWithWindows);
        controller.Register(settings);
        UpdateFields();
        statusBar.Text = "Settings saved and shortcuts registered.";
    }

    private static string LanguageCode(string? label) => label switch
    {
        "English (en)" => "en",
        "Spanish (es)" => "es",
        "French (fr)" => "fr",
        "German (de)" => "de",
        "Italian (it)" => "it",
        "Portuguese (pt)" => "pt",
        "Chinese (zh)" => "zh",
        "Japanese (ja)" => "ja",
        "Korean (ko)" => "ko",
        "Russian (ru)" => "ru",
        _ => "auto"
    };

    private async void WhisperInstallClick(object sender, RoutedEventArgs args)
    {
        whisperInstallButton.IsEnabled = false;
        whisperStatusText.Text = "Checking Whisper files...";
        try
        {
            await whisperEngine.InstallAsync(settings, progress => DispatcherQueue.TryEnqueue(() =>
            {
                var phase = progress.Phase == "engine" ? "engine" : "model";
                whisperStatusText.Text = progress.TotalBytes is > 0
                    ? $"Downloading {phase}: {progress.ReceivedBytes / 1_048_576d:0.0} / {progress.TotalBytes.Value / 1_048_576d:0.0} MB"
                    : $"Downloading {phase}: {progress.ReceivedBytes / 1_048_576d:0.0} MB";
            }));
            whisperStatusText.Text = "Whisper is ready.";
        }
        catch (Exception error)
        {
            var message = $"Whisper setup failed: {error.Message}";
            whisperStatusText.Text = message;
            statusBar.Text = message;
            NotificationRequested?.Invoke("Whisper installation failed", message, true);
        }
        finally
        {
            whisperInstallButton.IsEnabled = true;
            _ = RefreshWhisperStatusAsync();
        }
    }

    private async Task RefreshWhisperStatusAsync()
    {
        try
        {
            var status = await whisperEngine.GetStatusAsync(settings);
            whisperStatusText.Text = status.EngineInstalled && status.ModelInstalled
                ? $"Ready: {status.ModelLabel}"
                : "Not installed. Choose Install / repair Whisper when transcription is enabled.";
        }
        catch (Exception error)
        {
            whisperStatusText.Text = $"Whisper status unavailable: {error.Message}";
        }
    }

    private async void UpdateClick(object sender, RoutedEventArgs args)
    {
        updateButton.IsEnabled = false;
        updateStatusText.Text = "Checking GitHub releases...";
        try
        {
            var update = await updateService.CheckAsync();
            if (!update.IsUpdateAvailable)
            {
                updateStatusText.Text = update.Message;
                return;
            }

            updateStatusText.Text = $"Downloading {update.LatestVersion}...";
            var progress = new Progress<long>(bytes => updateStatusText.Text = $"Downloading {update.LatestVersion}: {bytes / 1_048_576d:0.0} MB");
            var installer = await updateService.DownloadInstallerAsync(update, progress);
            updateStatusText.Text = "Starting the installer...";
            UpdateService.LaunchInstaller(installer);
            ((App)Application.Current).ExitForUpdate();
        }
        catch (Exception error)
        {
            updateStatusText.Text = $"Update check failed: {error.Message}";
        }
        finally
        {
            updateButton.IsEnabled = true;
        }
    }

    public void Dispose() => controller.Dispose();

    private void OnStatusChanged(string message, bool succeeded)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            statusBar.Text = message;
            voiceStatusText.Text = controller.VoiceRegistered ? "Registered" : "Not registered";
            textStatusText.Text = controller.TextRegistered ? "Registered" : "Not registered";
            if (!succeeded && (message.Contains("Whisper", StringComparison.OrdinalIgnoreCase) || message.Contains("transcription", StringComparison.OrdinalIgnoreCase)))
                NotificationRequested?.Invoke("Voice transcription needs attention", message, true);
        });
    }

    private static Border Card() => new()
    {
        Padding = new Thickness(20),
        CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1),
        BorderBrush = ThemeBrush("ControlStrokeColorDefaultBrush", "DividerStrokeColorDefaultBrush"),
        Background = ThemeBrush("CardBackgroundFillColorDefaultBrush", "SolidBackgroundFillColorBaseBrush")
    };

    private static Brush ThemeBrush(params string[] keys)
    {
        var resources = Application.Current.Resources;
        foreach (var key in keys)
            if (resources.ContainsKey(key) && resources[key] is Brush brush) return brush;
        return new SolidColorBrush(Colors.Transparent);
    }

    private static TextBlock Heading(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold
    };

    private static TextBlock Body(string text) => new()
    {
        Text = text,
        Opacity = 0.72,
        TextWrapping = TextWrapping.WrapWholeWords
    };

    private static StackPanel Labeled(string label, FrameworkElement control, string? help = null)
    {
        if (control is Control nativeControl)
        {
            nativeControl.HorizontalAlignment = HorizontalAlignment.Stretch;
            nativeControl.MinWidth = 220;
        }
        var labelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        labelRow.Children.Add(new TextBlock { Text = label, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrWhiteSpace(help))
        {
            var helpIcon = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(5),
                Background = ThemeBrush("ControlAltFillColorSecondaryBrush", "ControlFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon
                {
                    Glyph = "\uE946",
                    FontSize = 10,
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush", "TextFillColorPrimaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            AutomationProperties.SetName(helpIcon, $"{label} help");
            ToolTipService.SetToolTip(helpIcon, help);
            labelRow.Children.Add(helpIcon);
        }
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                labelRow,
                control
            }
        };
    }

    private static Grid FolderEditor(TextBox textBox, RoutedEventHandler browseHandler)
    {
        var browse = Button("Browse...", browseHandler);
        browse.MinWidth = 92;
        var editor = new Grid { ColumnSpacing = 8 };
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(textBox, 0);
        Grid.SetColumn(browse, 1);
        editor.Children.Add(textBox);
        editor.Children.Add(browse);
        return editor;
    }

    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, MinWidth = 96 };
        button.Click += handler;
        return button;
    }

    private static void AddGrid(Grid grid, FrameworkElement element, int column, int row)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }
}

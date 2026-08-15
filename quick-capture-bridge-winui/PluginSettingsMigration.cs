using System.Text.Json;

namespace Moment;

public sealed record PluginSettingsMigrationResult(
    int ImportedPluginFiles,
    IReadOnlyList<string> ImportedFields,
    IReadOnlyList<string> Warnings)
{
    public bool Changed => ImportedPluginFiles > 0;
}

/// <summary>
/// Copies the user-facing settings owned by the two Obsidian plugins into the
/// native bridge settings file. The vault is never modified by this operation.
/// </summary>
public static class PluginSettingsMigration
{
    public static PluginSettingsMigrationResult Import(BridgeSettings settings)
    {
        var importedFiles = 0;
        var importedFields = new List<string>();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.VaultPath) || !Directory.Exists(settings.VaultPath))
        {
            return new PluginSettingsMigrationResult(0, importedFields, new[] { "Choose an existing vault before importing plugin settings." });
        }

        var pluginRoot = Path.Combine(settings.VaultPath, ".obsidian", "plugins");
        var dailyPath = Path.Combine(pluginRoot, "quick-daily-capture", "data.json");
        var voicePath = Path.Combine(pluginRoot, "quick-voice-notes", "data.json");
        if (TryRead(dailyPath, out var daily, warnings))
        {
            importedFiles++;
            ImportString(daily, "insertionLocation", value => settings.DailyInsertionLocation = Valid(value, "end", "beginning", "under-heading"), "daily insertion location", importedFields);
            ImportString(daily, "targetHeading", value => settings.DailyTargetHeading = value, "daily target heading", importedFields);
            ImportString(daily, "missingHeadingBehavior", value => settings.DailyMissingHeadingBehavior = Valid(value, "create", "end", "error"), "missing-heading behavior", importedFields);
            ImportString(daily, "timestampFormat", value => settings.DailyTimestampFormat = value, "daily timestamp format", importedFields);
            ImportBool(daily, "closeAfterSave", value => settings.DailyCloseAfterSave = value, "close after save", importedFields);
            ImportBool(daily, "enterToSave", value => settings.DailyEnterToSave = value, "Enter-to-save", importedFields);
        }

        if (TryRead(voicePath, out var voice, warnings))
        {
            importedFiles++;
            ImportString(voice, "audioFolder", value => settings.AudioFolder = value, "audio folder", importedFields);
            ImportInt(voice, "audioBitsPerSecond", value => settings.AudioBitsPerSecond = value is 32_000 or 64_000 or 96_000 ? value : 64_000, "recording quality", importedFields);
            ImportBool(voice, "enableTranscription", value => settings.EnableTranscription = value, "automatic transcription", importedFields);
            ImportString(voice, "language", value => settings.WhisperLanguage = Valid(value, "auto", "es", "en", "zh", "ja", "de", "fr", "it", "pt", "ru", "ko"), "Whisper language", importedFields);
            ImportString(voice, "model", value => settings.WhisperModel = Valid(value, "tiny", "base", "small", "medium", "large-v3-turbo-q5_0"), "Whisper model", importedFields);
            ImportString(voice, "transcriptionFolder", value => settings.TranscriptionFolder = value, "transcription folder", importedFields);
            ImportBool(voice, "includeAudioEmbed", value => settings.IncludeAudioEmbed = value, "audio embeds", importedFields);
            ImportString(voice, "destination", value => settings.TranscriptionDestination = Valid(value, "separate-note", "daily-note", "both"), "transcription destination", importedFields);

            // The old integration toggle only controlled whether the second
            // plugin was available. The native bridge owns Daily Note routing,
            // so a disabled integration safely becomes separate-note output.
            if (TryGetBool(voice, "enableDailyCaptureIntegration", out var integrationEnabled) && !integrationEnabled &&
                settings.TranscriptionDestination is "daily-note" or "both")
            {
                settings.TranscriptionDestination = "separate-note";
                importedFields.Add("integration fallback to separate transcript note");
            }
        }

        if (importedFiles > 0)
        {
            settings.NativeProcessingEnabled = true;
            settings.PluginSettingsImported = true;
        }

        return new PluginSettingsMigrationResult(importedFiles, importedFields, warnings);
    }

    private static bool TryRead(string path, out JsonElement document, ICollection<string> warnings)
    {
        document = default;
        if (!File.Exists(path)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(File.ReadAllText(path));
            document = parsed.RootElement.Clone();
            return document.ValueKind == JsonValueKind.Object;
        }
        catch (Exception error)
        {
            warnings.Add($"Could not read {Path.GetFileName(Path.GetDirectoryName(path) ?? path)} settings: {error.Message}");
            return false;
        }
    }

    private static void ImportString(JsonElement document, string name, Action<string> assign, string label, ICollection<string> imported)
    {
        if (!TryGetString(document, name, out var value)) return;
        assign(value);
        imported.Add(label);
    }

    private static void ImportBool(JsonElement document, string name, Action<bool> assign, string label, ICollection<string> imported)
    {
        if (!TryGetBool(document, name, out var value)) return;
        assign(value);
        imported.Add(label);
    }

    private static void ImportInt(JsonElement document, string name, Action<int> assign, string label, ICollection<string> imported)
    {
        if (!TryGetInt(document, name, out var value)) return;
        assign(value);
        imported.Add(label);
    }

    private static bool TryGetString(JsonElement document, string name, out string value)
    {
        value = "";
        if (!TryGet(document, name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()?.Trim() ?? "";
        return true;
    }

    private static bool TryGetBool(JsonElement document, string name, out bool value)
    {
        value = false;
        if (!TryGet(document, name, out var property) || (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False)) return false;
        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetInt(JsonElement document, string name, out int value)
    {
        value = 0;
        if (!TryGet(document, name, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out value)) return false;
        return true;
    }

    private static bool TryGet(JsonElement document, string name, out JsonElement value)
    {
        foreach (var property in document.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string Valid(string value, params string[] allowed) => allowed.Contains(value, StringComparer.OrdinalIgnoreCase) ? value : allowed[0];
}

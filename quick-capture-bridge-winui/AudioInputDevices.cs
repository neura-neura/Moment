using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Moment;

public sealed record AudioInputDeviceOption(string Key, string Label, int Index, bool IsWindowsDefault)
{
    public override string ToString() => Label;
}

/// <summary>
/// Lists the WinMM capture endpoints that can be opened at the requested
/// 16 kHz mono format. The Windows default is kept as an explicit option, but
/// users can choose a physical microphone when a virtual cable is the default.
/// </summary>
public static class AudioInputDevices
{
    public const string DefaultKey = "default";

    public static IReadOnlyList<AudioInputDeviceOption> GetOptions()
    {
        var defaultName = GetWindowsDefaultName();
        var options = new List<AudioInputDeviceOption>
        {
            new(
                DefaultKey,
                string.IsNullOrWhiteSpace(defaultName) ? "Windows default" : $"Windows default ({defaultName})",
                -1,
                true)
        };

        try
        {
            for (var index = 0; index < WaveInEvent.DeviceCount; index++)
            {
                var capabilities = WaveInEvent.GetCapabilities(index);
                var name = string.IsNullOrWhiteSpace(capabilities.ProductName)
                    ? $"Input device {index + 1}"
                    : capabilities.ProductName.Trim();
                options.Add(new($"wavein:{index}", name, index,
                    !string.IsNullOrWhiteSpace(defaultName) && NamesMatch(name, defaultName)));
            }
        }
        catch
        {
            // Device enumeration is best effort; the default option remains
            // usable even when a driver temporarily refuses enumeration.
        }

        return options;
    }

    public static int ResolveIndex(string? key)
    {
        var options = GetOptions();
        var selected = options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
        if (selected is not null && selected.Index >= 0) return selected.Index;

        var defaultMatch = options.FirstOrDefault(option => option.IsWindowsDefault && option.Index >= 0);
        if (defaultMatch is not null) return defaultMatch.Index;

        try { return WaveInEvent.DeviceCount > 0 ? 0 : -1; }
        catch { return -1; }
    }

    public static string GetLabel(string? key)
    {
        var options = GetOptions();
        return options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))?.Label
            ?? options[0].Label;
    }

    private static string GetWindowsDefaultName()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            return device.FriendlyName?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool NamesMatch(string left, string right) =>
        string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase) ||
        NormalizeName(left).Contains(NormalizeName(right), StringComparison.OrdinalIgnoreCase) ||
        NormalizeName(right).Contains(NormalizeName(left), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray());
}

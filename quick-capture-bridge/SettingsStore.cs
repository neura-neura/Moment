using System.Text.Json;

namespace QuickCaptureBridge;

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickCaptureBridge",
        "settings.json");

    public BridgeSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new BridgeSettings();
            return JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(_path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new BridgeSettings();
        }
        catch
        {
            return new BridgeSettings();
        }
    }

    public void Save(BridgeSettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        File.Move(temporary, _path, true);
    }
}

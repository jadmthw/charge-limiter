using System.Text.Json;
using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

public interface ISettingsStore
{
    SoftCapSettings Load();
    void Save(SoftCapSettings settings);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonSettingsStore(string? path = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChargeLimiter");
        Directory.CreateDirectory(dir);
        _path = path ?? Path.Combine(dir, "softcap-settings.json");
    }

    public SoftCapSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new SoftCapSettings();
            }

            return JsonSerializer.Deserialize<SoftCapSettings>(File.ReadAllText(_path), Options)
                   ?? new SoftCapSettings();
        }
        catch
        {
            return new SoftCapSettings();
        }
    }

    public void Save(SoftCapSettings settings) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
}

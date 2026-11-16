using System.Text.Json;
using System.Text.Json.Serialization;

namespace RefreshToggle;

/// <summary>
/// Settings stored in %APPDATA%\RefreshToggle\config.json. Field names match v0.4.0, so existing
/// files keep working; unknown fields (e.g. the legacy StartWithWindows) are ignored.
/// </summary>
internal sealed record Config(
    int RateA = 60,
    int RateB = 120,
    string HotkeyModifiers = "Ctrl+Shift",
    string HotkeyKey = "R",
    bool AutoByPowerSource = true)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RefreshToggle",
        "config.json");

#pragma warning disable CA1822 // Serialized so the file format stays compatible with v0.4.0.
    public int SchemaVersion => 1;
#pragma warning restore CA1822

    [JsonIgnore]
    public bool IsValid =>
        RateA > 0 && RateB > 0 && RateA != RateB &&
        HotkeyBinding.TryParse(HotkeyModifiers, HotkeyKey, out _);

    /// <summary>Loads the config; a missing file gives defaults, a broken one defaults plus a warning.</summary>
    public static (Config Config, string? Warning) Load(string path)
    {
        if (!File.Exists(path))
        {
            return (new Config(), null);
        }

        try
        {
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), JsonOptions);
            return config is { IsValid: true }
                ? (config, null)
                : (new Config(), "config.json has invalid values – using defaults.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return (new Config(), $"config.json could not be read – using defaults. ({exception.Message})");
        }
    }

    /// <summary>Writes to a temp file first so a crash never leaves a half-written config.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}

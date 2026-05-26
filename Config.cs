using System.Text.Json;

namespace RefreshToggle;

internal sealed class AppConfig
{
    public int RateA { get; set; } = 60;
    public int RateB { get; set; } = 120;
    public bool StartWithWindows { get; set; } = false;

    private static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RefreshToggle");

    private static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        return LoadWithResult().Config;
    }

    public static LoadResult LoadWithResult()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfig();
            defaultConfig.Save();
            return new LoadResult(defaultConfig, WasReset: false, ResetReason: null);
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json);

            if (config is null || config.RateA <= 0 || config.RateB <= 0 || config.RateA == config.RateB)
            {
                var fallback = new AppConfig();
                fallback.Save();
                return new LoadResult(fallback, WasReset: true, ResetReason: "Config contained invalid values (rates <= 0 or equal).");
            }

            return new LoadResult(config, WasReset: false, ResetReason: null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Config load failed: {ex.Message}");
            var fallback = new AppConfig();
            fallback.Save();
            return new LoadResult(fallback, WasReset: true, ResetReason: $"Config file could not be read: {ex.Message}");
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigPath, overwrite: true);
    }
}

internal sealed record LoadResult(AppConfig Config, bool WasReset, string? ResetReason);

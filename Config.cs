using System.Text.Json;

namespace RefreshToggle;

internal sealed class AppConfig
{
    public int RateA { get; set; } = 60;
    public int RateB { get; set; } = 120;

    private static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RefreshToggle");

    private static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfig();
            defaultConfig.Save();
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json);

            if (config is null || config.RateA <= 0 || config.RateB <= 0 || config.RateA == config.RateB)
            {
                var fallback = new AppConfig();
                fallback.Save();
                return fallback;
            }

            return config;
        }
        catch
        {
            var fallback = new AppConfig();
            fallback.Save();
            return fallback;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}

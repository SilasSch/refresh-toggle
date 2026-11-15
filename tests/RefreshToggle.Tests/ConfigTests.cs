namespace RefreshToggle.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("refreshtoggle-tests-").FullName;

    private string ConfigPath => Path.Combine(_directory, "RefreshToggle", "config.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults_without_warning()
    {
        var (config, warning) = Config.Load(ConfigPath);
        Assert.Equal(new Config(), config);
        Assert.Null(warning);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var saved = new Config(RateA: 75, RateB: 165, HotkeyModifiers: "Alt", HotkeyKey: "F9", AutoByPowerSource: false);
        saved.Save(ConfigPath);

        var (loaded, warning) = Config.Load(ConfigPath);
        Assert.Equal(saved, loaded);
        Assert.Null(warning);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Reads_v040_file_and_ignores_legacy_fields()
    {
        Write("""
            {
              "SchemaVersion": 1,
              "RateA": 60,
              "RateB": 144,
              "HotkeyModifiers": "Ctrl+Alt",
              "HotkeyKey": "T",
              "StartWithWindows": true
            }
            """);

        var (config, warning) = Config.Load(ConfigPath);
        Assert.Equal(new Config(60, 144, "Ctrl+Alt", "T"), config);
        Assert.Null(warning);
        Assert.True(config.AutoByPowerSource); // field did not exist in v0.4.0
    }

    [Fact]
    public void Missing_fields_fall_back_to_defaults()
    {
        Write("""{ "RateB": 144 }""");
        var (config, _) = Config.Load(ConfigPath);
        Assert.Equal(new Config(RateB: 144), config);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""{ "RateA": 60, "RateB": 60 }""")]
    [InlineData("""{ "RateA": -1 }""")]
    [InlineData("""{ "HotkeyKey": "Nope" }""")]
    public void Invalid_file_gives_defaults_with_warning(string json)
    {
        Write(json);
        var (config, warning) = Config.Load(ConfigPath);
        Assert.Equal(new Config(), config);
        Assert.NotNull(warning);
    }

    [Fact]
    public void Saved_file_contains_schema_version()
    {
        new Config().Save(ConfigPath);
        var json = File.ReadAllText(ConfigPath);
        Assert.Contains("\"SchemaVersion\": 1", json);
        Assert.DoesNotContain("IsValid", json);
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, json);
    }
}

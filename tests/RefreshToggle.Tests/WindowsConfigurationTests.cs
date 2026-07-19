using System.Text.Json;
using RefreshToggle.Core;
using RefreshToggle.Windows.Configuration;

namespace RefreshToggle.Tests;

public sealed class WindowsConfigurationTests
{
    [Fact]
    public async Task MissingConfigCreatesSchemaOneDefaults()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "config.json");
        await using var store = new JsonConfigStore(path);

        var result = await store.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WasReset);
        Assert.Equal(AppConfig.CurrentSchemaVersion, result.Value.Config.SchemaVersion);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task LegacyStartupFieldIsReadAndRemovedDuringMigration()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "config.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "RateA": 60,
              "RateB": 144,
              "StartWithWindows": true,
              "HotkeyModifiers": "Ctrl+Alt",
              "HotkeyKey": "T"
            }
            """);
        await using var store = new JsonConfigStore(path);

        var result = await store.LoadAsync();
        var migratedJson = await File.ReadAllTextAsync(path);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.NeedsMigration);
        Assert.Equal(144, result.Value.Config.RateB);
        Assert.DoesNotContain("StartWithWindows", migratedJson, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(migratedJson);
        Assert.Equal(1, document.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Theory]
    [InlineData("{ invalid")]
    [InlineData("null")]
    public async Task InvalidConfigIsBackedUpAndRecovered(string invalidJson)
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "config.json");
        await File.WriteAllTextAsync(path, invalidJson);
        await using var store = new JsonConfigStore(path);

        var result = await store.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasReset);
        Assert.Single(Directory.GetFiles(temp.Path, "config.corrupt-*.json"));
        Assert.True(AppConfig.Default.Validate().IsSuccess);
        Assert.Contains("SchemaVersion", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LockedConfigReturnsFailureWithoutChangingPersistedState()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "config.json");
        await using var engine = new ConfigFileStoreEngine(path);
        Assert.True((await engine.SaveAsync(AppConfig.Default)).IsSuccess);
        var original = await File.ReadAllBytesAsync(path);

        await using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await engine.SaveAsync(AppConfig.Default with { RateB = 144 });

        Assert.True(result.IsFailure);
        Assert.Equal(original, await ReadLockedFileAsync(locked));
    }

    [Fact]
    public async Task ConcurrentSavesRemainValidJsonAndLeaveNoTempFiles()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "config.json");
        await using var engine = new ConfigFileStoreEngine(path);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(index =>
                engine.SaveAsync(AppConfig.Default with { RateB = 120 + index })));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(1, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Empty(Directory.GetFiles(temp.Path, ".config.json.*.tmp"));
    }

    private static async Task<byte[]> ReadLockedFileAsync(FileStream stream)
    {
        stream.Position = 0;
        var bytes = new byte[stream.Length];
        _ = await stream.ReadAsync(bytes);
        return bytes;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "RefreshToggle.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

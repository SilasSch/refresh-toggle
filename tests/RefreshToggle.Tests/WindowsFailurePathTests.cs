using Microsoft.Win32;
using RefreshToggle.Core;
using RefreshToggle.Windows.Configuration;
using RefreshToggle.Windows.Deployment;
using RefreshToggle.Windows.Startup;

namespace RefreshToggle.Tests;

public sealed class WindowsFailurePathTests
{
    [Fact]
    public async Task LockedConfigLoadUsesDefaultsWithoutOverwritingPersistedBytes()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "config.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "SchemaVersion": 1,
              "RateA": 75,
              "RateB": 144,
              "HotkeyModifiers": "Ctrl+Alt",
              "HotkeyKey": "T"
            }
            """);
        var persisted = await File.ReadAllBytesAsync(path);
        await using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await using var store = new ConfigFileStoreEngine(path);

        var result = await store.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasRecovered);
        Assert.Equal(AppConfig.Default, result.Value.Config);
        Assert.Contains("could not", result.Value.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(persisted, await ReadLockedFileAsync(locked));
        Assert.Empty(Directory.GetFiles(temp.Path, ".config.json.*.tmp"));
    }

    [Fact]
    public async Task LockedDirectUninstallRestoresStartupAndKeepsExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var installDirectory = Path.Combine(temp.Path, "Install ü with spaces");
        var target = Path.Combine(installDirectory, InstallationPaths.ExecutableName);
        Directory.CreateDirectory(installDirectory);
        await File.WriteAllBytesAsync(target, [1, 2, 3, 4]);

        var deployment = new DeploymentManifest(
            DeploymentFlavor.Distribution,
            "win-arm64",
            IsSelfContained: true,
            IsSingleFile: true,
            Reason: null);
        var paths = new InstallationPaths(installDirectory, target);
        var registryPath = $@"Software\RefreshToggle.Tests\{Guid.NewGuid():N}";
        var startupStore = new RegistryStartupStoreEngine(paths, deployment, registryPath, "RefreshToggle");

        try
        {
            Assert.True(startupStore.Enable().IsSuccess);
            await using var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            var service = new WindowsInstallationService(deployment, paths, startupStore);

            var result = await service.BeginUninstallAsync();

            Assert.True(result.IsFailure);
            Assert.True(File.Exists(target));
            var startup = startupStore.GetState();
            Assert.True(startup.IsSuccess);
            Assert.True(startup.Value.IsEnabled);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void CleanupProtocolNeverAcceptsAPathArgument()
    {
        var args = new[]
        {
            CleanupCommand.Switch,
            "1",
            "1",
            Guid.NewGuid().ToString("N"),
            @"C:\foreign\target.exe"
        };

        Assert.False(CleanupCommand.TryParse(args, out _));
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

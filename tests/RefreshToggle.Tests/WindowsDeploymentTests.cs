using Microsoft.Win32;
using RefreshToggle.Windows.Deployment;
using RefreshToggle.Windows.Startup;

namespace RefreshToggle.Tests;

public sealed class WindowsDeploymentTests
{
    [Fact]
    public void SourceBuildIsAlwaysPortable()
    {
        var manifest = DeploymentManifest.InspectCurrentProcess(typeof(WindowsDeploymentTests).Assembly);

        Assert.Equal(DeploymentFlavor.DevelopmentPortable, manifest.Flavor);
        Assert.False(manifest.MayMutateInstallation);
    }

    [Fact]
    public async Task AtomicInstallerKeepsExistingExecutableWhenSourceIsLocked()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "source.exe");
        var installDirectory = Path.Combine(temp.Path, "installed");
        var target = Path.Combine(installDirectory, InstallationPaths.ExecutableName);
        Directory.CreateDirectory(installDirectory);
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(target, [9, 8, 7]);
        var paths = new InstallationPaths(installDirectory, target);
        var installer = new AtomicInstaller(paths, () => paths);

        await using var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = await installer.EnsureInstalledAsync(source);

        Assert.True(result.IsFailure);
        Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task DirectUninstallRemovesStartupAndExecutableButKeepsConfig()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var installDirectory = Path.Combine(temp.Path, "installed");
        var target = Path.Combine(installDirectory, InstallationPaths.ExecutableName);
        var config = Path.Combine(temp.Path, "config.json");
        Directory.CreateDirectory(installDirectory);
        await File.WriteAllBytesAsync(target, [1, 2, 3]);
        await File.WriteAllTextAsync(config, "keep me");
        var configBefore = await File.ReadAllBytesAsync(config);

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
            var service = new WindowsInstallationService(deployment, paths, startupStore);

            var result = await service.BeginUninstallAsync();

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.ExitRequired);
            Assert.False(File.Exists(target));
            Assert.False(startupStore.GetState().Value.HasEntry);
            Assert.Equal(configBefore, await File.ReadAllBytesAsync(config));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
        }
    }

    [Theory]
    [InlineData("--cleanup-v1", "1", "1", "not-a-guid")]
    [InlineData("--cleanup-v1", "0", "1", "0123456789abcdef0123456789abcdef")]
    [InlineData("--cleanup-v1", "1", "0", "0123456789abcdef0123456789abcdef")]
    public void CleanupProtocolRejectsInvalidArguments(params string[] args)
    {
        Assert.False(CleanupCommand.TryParse(args, out _));
    }

    [Fact]
    public void StaleCleanupOnlyRemovesOldNonceBoundArtifacts()
    {
        var oldNonce = Guid.NewGuid();
        var freshNonce = Guid.NewGuid();
        var oldPath = Path.Combine(Path.GetTempPath(), $"RefreshToggle-cleanup-{oldNonce:N}.result");
        var freshPath = Path.Combine(Path.GetTempPath(), $"RefreshToggle-cleanup-{freshNonce:N}.result");
        File.WriteAllText(oldPath, "old");
        File.WriteAllText(freshPath, "fresh");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-2));

        try
        {
            StaleCleanup.RemoveRecognizedArtifacts();

            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(freshPath));
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(freshPath);
        }
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

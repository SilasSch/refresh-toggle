using RefreshToggle.Core;
using RefreshToggle.Windows.Displays;

namespace RefreshToggle.Tests;

public sealed class WindowsDisplayServiceTests
{
    [Fact]
    public void DevModeLayoutMatchesWindowsDevModeW()
    {
        Assert.Equal(220, User32DisplayNativeBackend.DevModeSize);
    }

    [Fact]
    public async Task SnapshotFiltersModesToCurrentResolutionAndColorDepth()
    {
        var backend = FakeDisplayBackend.CreateSingleDisplay();
        backend.Modes["DISPLAY1"] =
        [
            FakeDisplayBackend.Mode(60),
            FakeDisplayBackend.Mode(120),
            FakeDisplayBackend.Mode(144) with { Width = 2560 },
            FakeDisplayBackend.Mode(165) with { BitsPerPixel = 24 }
        ];

        var result = await new WindowsDisplayService(backend).GetSnapshotAsync();

        Assert.True(result.IsSuccess);
        var display = Assert.Single(result.Value.Displays);
        Assert.Equal([60, 120], display.SupportedRefreshRates);
    }

    [Fact]
    public async Task ApplyTestsEveryDisplayBeforeFirstMutation()
    {
        var backend = FakeDisplayBackend.CreateTwoDisplays();
        var service = new WindowsDisplayService(backend);
        var plan = new RefreshRatePlan(
            120,
            [new("DISPLAY1", 60, 120), new("DISPLAY2", 60, 120)]);

        var result = await service.ApplyAsync(plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["test:DISPLAY1:120", "test:DISPLAY2:120", "apply:DISPLAY1:120", "apply:DISPLAY2:120"],
            backend.ChangeCalls);
        Assert.All(result.Value.Displays, display => Assert.Equal(120, display.CurrentRefreshRate));
    }

    [Fact]
    public async Task FailedApplyRollsPreviouslyChangedDisplaysBack()
    {
        var backend = FakeDisplayBackend.CreateTwoDisplays();
        backend.FailApplyForDevice = "DISPLAY2";
        var service = new WindowsDisplayService(backend);
        var plan = new RefreshRatePlan(
            120,
            [new("DISPLAY1", 60, 120), new("DISPLAY2", 60, 120)]);

        var result = await service.ApplyAsync(plan);

        Assert.True(result.IsFailure);
        Assert.Equal(60, backend.CurrentModes["DISPLAY1"].RefreshRate);
        Assert.Contains("test:DISPLAY1:60", backend.ChangeCalls);
        Assert.Contains("apply:DISPLAY1:60", backend.ChangeCalls);
    }

    [Fact]
    public async Task RejectedPreflightNeverMutatesAnyDisplay()
    {
        var backend = FakeDisplayBackend.CreateTwoDisplays();
        backend.FailTestForDevice = "DISPLAY2";
        var service = new WindowsDisplayService(backend);
        var plan = new RefreshRatePlan(
            120,
            [new("DISPLAY1", 60, 120), new("DISPLAY2", 60, 120)]);

        var result = await service.ApplyAsync(plan);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(backend.ChangeCalls, call => call.StartsWith("apply:", StringComparison.Ordinal));
        Assert.All(backend.CurrentModes.Values, mode => Assert.Equal(60, mode.RefreshRate));
    }

    private sealed class FakeDisplayBackend : IDisplayNativeBackend
    {
        public List<NativeDisplayDevice> Devices { get; } = [];

        public Dictionary<string, NativeDisplayMode> CurrentModes { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<NativeDisplayMode>> Modes { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> ChangeCalls { get; } = [];

        public string? FailTestForDevice { get; set; }

        public string? FailApplyForDevice { get; set; }

        public NativeCallResult<IReadOnlyList<NativeDisplayDevice>> EnumerateActiveDisplays() =>
            NativeCallResult<IReadOnlyList<NativeDisplayDevice>>.Success(Devices);

        public NativeCallResult<NativeDisplayMode> GetCurrentMode(string deviceName) =>
            CurrentModes.TryGetValue(deviceName, out var mode)
                ? NativeCallResult<NativeDisplayMode>.Success(mode)
                : NativeCallResult<NativeDisplayMode>.Failure("display missing");

        public NativeCallResult<IReadOnlyList<NativeDisplayMode>> EnumerateModes(string deviceName) =>
            Modes.TryGetValue(deviceName, out var modes)
                ? NativeCallResult<IReadOnlyList<NativeDisplayMode>>.Success(modes)
                : NativeCallResult<IReadOnlyList<NativeDisplayMode>>.Failure("modes missing");

        public NativeCallResult TestMode(string deviceName, NativeDisplayMode mode)
        {
            ChangeCalls.Add($"test:{deviceName}:{mode.RefreshRate}");
            return string.Equals(deviceName, FailTestForDevice, StringComparison.OrdinalIgnoreCase)
                ? NativeCallResult.Failure("test rejected")
                : NativeCallResult.Success();
        }

        public NativeCallResult ApplyMode(string deviceName, NativeDisplayMode mode)
        {
            ChangeCalls.Add($"apply:{deviceName}:{mode.RefreshRate}");
            if (string.Equals(deviceName, FailApplyForDevice, StringComparison.OrdinalIgnoreCase) &&
                mode.RefreshRate != 60)
            {
                return NativeCallResult.Failure("apply rejected");
            }

            CurrentModes[deviceName] = mode;
            return NativeCallResult.Success();
        }

        public static FakeDisplayBackend CreateSingleDisplay()
        {
            var backend = new FakeDisplayBackend();
            backend.AddDisplay("DISPLAY1", isPrimary: true);
            return backend;
        }

        public static FakeDisplayBackend CreateTwoDisplays()
        {
            var backend = CreateSingleDisplay();
            backend.AddDisplay("DISPLAY2", isPrimary: false);
            return backend;
        }

        public static NativeDisplayMode Mode(int rate) => new(1920, 1080, 32, rate, 0, 0, 0, 0);

        private void AddDisplay(string name, bool isPrimary)
        {
            Devices.Add(new NativeDisplayDevice(name, name, isPrimary));
            CurrentModes[name] = Mode(60);
            Modes[name] = [Mode(60), Mode(120)];
        }
    }
}

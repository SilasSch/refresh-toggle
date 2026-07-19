using RefreshToggle.App;
using RefreshToggle.Core;
using RefreshToggle.Windows.Hotkeys;

namespace RefreshToggle.Tests;

public sealed class WindowsHotkeyAndIconTests
{
    [Theory]
    [InlineData(60, "60")]
    [InlineData(100, "100")]
    [InlineData(105, "105")]
    [InlineData(120, "120")]
    [InlineData(200, "200")]
    public void TrayIconLabelsNeverTruncateRefreshRate(int rate, string expected)
    {
        Assert.Equal(expected, TrayIconRenderer.FormatLabel(rate));
    }

    [Fact]
    public async Task FailedNativeUnregisterKeepsSlotOccupied()
    {
        var backend = new FakeHotkeyBackend();
        using var window = new FakeHotkeyWindow();
        await using var service = new Win32HotkeyService(backend, window);
        var first = await service.RegisterAsync(HotkeyBinding.Default);
        var secondBinding = HotkeyBinding.Parse("Ctrl+Alt", "T").Value;
        var second = await service.RegisterAsync(secondBinding);
        backend.FailUnregister = true;

        var unregister = await service.UnregisterAsync(first.Value);
        var third = await service.RegisterAsync(HotkeyBinding.Parse("Ctrl+Shift", "F").Value);

        Assert.True(unregister.IsFailure);
        Assert.True(third.IsFailure);
        Assert.Contains("slots", third.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task NativeWindowEventIsForwardedOnlyForRegisteredSlot()
    {
        var backend = new FakeHotkeyBackend();
        using var window = new FakeHotkeyWindow();
        await using var service = new Win32HotkeyService(backend, window);
        var pressCount = 0;
        service.HotkeyPressed += (_, _) => pressCount++;
        var registration = await service.RegisterAsync(HotkeyBinding.Default);

        window.Raise(registration.Value.Slot);
        window.Raise(99);

        Assert.Equal(1, pressCount);
    }

    private sealed class FakeHotkeyBackend : IHotkeyNativeBackend
    {
        public bool FailUnregister { get; set; }

        public HotkeyNativeResult Register(IntPtr windowHandle, int slot, uint modifiers, uint virtualKey) =>
            HotkeyNativeResult.Success();

        public HotkeyNativeResult Unregister(IntPtr windowHandle, int slot) =>
            FailUnregister ? HotkeyNativeResult.Failure("locked") : HotkeyNativeResult.Success();
    }

    private sealed class FakeHotkeyWindow : IHotkeyWindow
    {
        public IntPtr Handle => new(42);

        public event EventHandler<int>? SlotPressed;

        public void Raise(int slot) => SlotPressed?.Invoke(this, slot);

        public void Dispose()
        {
        }
    }
}

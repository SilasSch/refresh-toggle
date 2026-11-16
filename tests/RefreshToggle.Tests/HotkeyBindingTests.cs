namespace RefreshToggle.Tests;

public class HotkeyBindingTests
{
    [Theory]
    [InlineData("Ctrl+Shift", "R", "Ctrl+Shift+R")]
    [InlineData("shift + control", "r", "Ctrl+Shift+R")]
    [InlineData("Alt", "F12", "Alt+F12")]
    [InlineData("Win+Alt", "5", "Alt+Win+5")]
    [InlineData("Ctrl", "Esc", "Ctrl+Escape")]
    [InlineData("Ctrl", "PgDn", "Ctrl+PageDown")]
    public void TryParse_accepts_and_normalizes(string modifiers, string key, string expected)
    {
        Assert.True(HotkeyBinding.TryParse(modifiers, key, out var binding));
        Assert.Equal(expected, binding.ToString());
    }

    [Theory]
    [InlineData("", "R")]          // no modifier
    [InlineData("Ctrl", "")]       // no key
    [InlineData("Ctrl+Ctrl", "R")] // duplicate
    [InlineData("Hyper", "R")]     // unknown modifier
    [InlineData("Ctrl", "F25")]    // unknown key
    [InlineData("Ctrl", "12")]     // multi-digit
    [InlineData(null, null)]
    public void TryParse_rejects_invalid(string? modifiers, string? key) =>
        Assert.False(HotkeyBinding.TryParse(modifiers, key, out _));

    [Fact]
    public void Key_names_round_trip_through_virtual_key_codes()
    {
        Assert.True(HotkeyBinding.TryParse("Ctrl", "F5", out var binding));
        Assert.Equal(0x74u, binding.VirtualKey);
        Assert.Equal("F5", HotkeyBinding.GetKeyName(0x74));
        Assert.Null(HotkeyBinding.GetKeyName(0x10)); // Shift itself is not a valid key
    }

    [Fact]
    public void Default_is_ctrl_shift_r() =>
        Assert.Equal("Ctrl+Shift+R", HotkeyBinding.Default.ToString());
}

using RefreshToggle.Core;

namespace RefreshToggle.Tests;

public sealed class HotkeyBindingTests
{
    [Theory]
    [InlineData("Ctrl+Shift", "R", (uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift), (uint)HotkeyKey.R, "Ctrl+Shift+R")]
    [InlineData("control+alt", "Esc", (uint)(HotkeyModifiers.Control | HotkeyModifiers.Alt), (uint)HotkeyKey.Escape, "Ctrl+Alt+Escape")]
    [InlineData("Windows", "PgDn", (uint)HotkeyModifiers.Windows, (uint)HotkeyKey.PageDown, "Win+PageDown")]
    [InlineData("Alt", "1", (uint)HotkeyModifiers.Alt, (uint)HotkeyKey.D1, "Alt+1")]
    public void ParseAcceptsNamedKeysAndAliases(
        string modifiers,
        string key,
        uint expectedModifiers,
        uint expectedKey,
        string expectedText)
    {
        var result = HotkeyBinding.Parse(modifiers, key);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedModifiers, (uint)result.Value.Modifiers);
        Assert.Equal(expectedKey, (uint)result.Value.Key);
        Assert.Equal(expectedKey, result.Value.VirtualKeyCode);
        Assert.Equal(expectedText, result.Value.DisplayText);
    }

    [Theory]
    [InlineData(null, "R")]
    [InlineData("Ctrl", null)]
    [InlineData("", "R")]
    [InlineData("Ctrl+Hyper", "R")]
    [InlineData("Ctrl+Ctrl", "R")]
    [InlineData("Ctrl", "82")]
    [InlineData("Ctrl", "Shift")]
    [InlineData("Ctrl", "None")]
    [InlineData("Ctrl", "DefinitelyNotAKey")]
    public void ParseRejectsInvalidOrNumericEnumInput(string? modifiers, string? key)
    {
        var result = HotkeyBinding.Parse(modifiers, key);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }

    [Fact]
    public void CreateRejectsMissingUnknownModifiersAndUnknownKey()
    {
        Assert.True(HotkeyBinding.Create(HotkeyModifiers.None, HotkeyKey.R).IsFailure);
        Assert.True(HotkeyBinding.Create((HotkeyModifiers)0x8000, HotkeyKey.R).IsFailure);
        Assert.True(HotkeyBinding.Create(HotkeyModifiers.Control, (HotkeyKey)0xFFFF).IsFailure);
    }

    [Fact]
    public void DefaultMatchesPersistedDefaults()
    {
        Assert.Equal("Ctrl+Shift", HotkeyBinding.Default.ModifierText);
        Assert.Equal("R", HotkeyBinding.Default.KeyText);
    }
}

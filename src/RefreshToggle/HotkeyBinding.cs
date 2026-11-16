namespace RefreshToggle;

/// <summary>Modifier flags with the values RegisterHotKey expects.</summary>
[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A validated global hotkey, e.g. Ctrl+Shift+R. Pure, no Win32 calls.</summary>
internal readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public static HotkeyBinding Default { get; } = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'R');

    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Ctrl, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Win, "Win"),
    ];

    private static readonly Dictionary<string, uint> KeysByName = CreateKeyTable();

    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = "Escape",
        ["Return"] = "Enter",
        ["PgUp"] = "PageUp",
        ["PgDn"] = "PageDown",
        ["Del"] = "Delete",
        ["Ins"] = "Insert",
    };

    public string ModifierText
    {
        get
        {
            var modifiers = Modifiers;
            return string.Join('+', ModifierNames.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name));
        }
    }

    public string KeyText => GetKeyName(VirtualKey) ?? $"0x{VirtualKey:X2}";

    public override string ToString() => $"{ModifierText}+{KeyText}";

    public static bool TryParse(string? modifierText, string? keyText, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(modifierText) || string.IsNullOrWhiteSpace(keyText))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in modifierText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var flag = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (flag == HotkeyModifiers.None || modifiers.HasFlag(flag))
            {
                return false;
            }

            modifiers |= flag;
        }

        var key = keyText.Trim();
        key = KeyAliases.GetValueOrDefault(key, key);
        if (modifiers == HotkeyModifiers.None || !KeysByName.TryGetValue(key, out var virtualKey))
        {
            return false;
        }

        binding = new HotkeyBinding(modifiers, virtualKey);
        return true;
    }

    /// <summary>Display name for a supported virtual-key code, or null if the key is not allowed.</summary>
    public static string? GetKeyName(uint virtualKey) =>
        KeysByName.FirstOrDefault(entry => entry.Value == virtualKey).Key;

    private static Dictionary<string, uint> CreateKeyTable()
    {
        var keys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backspace"] = 0x08,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Escape"] = 0x1B,
            ["Space"] = 0x20,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["End"] = 0x23,
            ["Home"] = 0x24,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
        };

        for (var digit = '0'; digit <= '9'; digit++)
        {
            keys[digit.ToString()] = digit;
        }

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            keys[letter.ToString()] = letter;
        }

        for (var number = 1u; number <= 24; number++)
        {
            keys[$"F{number}"] = 0x6F + number;
        }

        return keys;
    }
}

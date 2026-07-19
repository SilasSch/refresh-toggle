namespace RefreshToggle.Core;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

internal enum HotkeyKey : uint
{
    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Escape = 0x1B,
    Space = 0x20,
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Insert = 0x2D,
    Delete = 0x2E,
    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,
    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,
    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B,
    F13 = 0x7C,
    F14 = 0x7D,
    F15 = 0x7E,
    F16 = 0x7F,
    F17 = 0x80,
    F18 = 0x81,
    F19 = 0x82,
    F20 = 0x83,
    F21 = 0x84,
    F22 = 0x85,
    F23 = 0x86,
    F24 = 0x87
}

internal readonly record struct HotkeyBinding
{
    private const HotkeyModifiers AllModifiers =
        HotkeyModifiers.Alt |
        HotkeyModifiers.Control |
        HotkeyModifiers.Shift |
        HotkeyModifiers.Windows;

    private HotkeyBinding(HotkeyModifiers modifiers, HotkeyKey key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public HotkeyModifiers Modifiers { get; }

    public HotkeyKey Key { get; }

    public uint VirtualKeyCode => (uint)Key;

    public string ModifierText => FormatModifiers(Modifiers);

    public string KeyText => FormatKey(Key);

    public string DisplayText => $"{ModifierText}+{KeyText}";

    public static HotkeyBinding Default { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyKey.R);

    public static Result<HotkeyBinding> Create(HotkeyModifiers modifiers, HotkeyKey key)
    {
        if (modifiers == HotkeyModifiers.None || (modifiers & ~AllModifiers) != 0)
        {
            return Result.Failure<HotkeyBinding>(
                AppError.Validation("At least one valid modifier (Ctrl, Alt, Shift, or Win) is required."));
        }

        if (!Enum.IsDefined(key))
        {
            return Result.Failure<HotkeyBinding>(AppError.Validation("The hotkey key is not supported."));
        }

        return Result.Success(new HotkeyBinding(modifiers, key));
    }

    public static Result<HotkeyBinding> Parse(string? modifierText, string? keyText)
    {
        if (string.IsNullOrWhiteSpace(modifierText) || string.IsNullOrWhiteSpace(keyText))
        {
            return Result.Failure<HotkeyBinding>(AppError.Validation("Modifier or key is empty."));
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in modifierText.Split(
                     '+',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None
            };

            if (modifier == HotkeyModifiers.None)
            {
                return Result.Failure<HotkeyBinding>(AppError.Validation($"Unknown modifier: {part}"));
            }

            if ((modifiers & modifier) != 0)
            {
                return Result.Failure<HotkeyBinding>(AppError.Validation($"Duplicate modifier: {part}"));
            }

            modifiers |= modifier;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            return Result.Failure<HotkeyBinding>(
                AppError.Validation("At least one modifier (Ctrl, Alt, Shift, or Win) is required."));
        }

        var parsedKey = ParseKey(keyText.Trim());
        return parsedKey.IsSuccess
            ? Create(modifiers, parsedKey.Value)
            : Result.Failure<HotkeyBinding>(parsedKey.Error!);
    }

    private static Result<HotkeyKey> ParseKey(string text)
    {
        if (text.Length == 0 || (text.Length > 1 && text.All(char.IsDigit)))
        {
            return Result.Failure<HotkeyKey>(AppError.Validation($"Unknown key: {text}"));
        }

        var normalized = text.ToUpperInvariant() switch
        {
            "ESC" => nameof(HotkeyKey.Escape),
            "RETURN" => nameof(HotkeyKey.Enter),
            "PGUP" or "PRIOR" => nameof(HotkeyKey.PageUp),
            "PGDN" or "NEXT" => nameof(HotkeyKey.PageDown),
            _ => text
        };

        if (text.Length == 1 && char.IsDigit(text[0]))
        {
            normalized = $"D{text}";
        }

        if (!Enum.TryParse<HotkeyKey>(normalized, ignoreCase: true, out var key) || !Enum.IsDefined(key))
        {
            return Result.Failure<HotkeyKey>(AppError.Validation($"Unknown key: {text}"));
        }

        return Result.Success(key);
    }

    private static string FormatModifiers(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        return string.Join('+', parts);
    }

    private static string FormatKey(HotkeyKey key) => key switch
    {
        HotkeyKey.D0 => "0",
        HotkeyKey.D1 => "1",
        HotkeyKey.D2 => "2",
        HotkeyKey.D3 => "3",
        HotkeyKey.D4 => "4",
        HotkeyKey.D5 => "5",
        HotkeyKey.D6 => "6",
        HotkeyKey.D7 => "7",
        HotkeyKey.D8 => "8",
        HotkeyKey.D9 => "9",
        _ => key.ToString()
    };
}

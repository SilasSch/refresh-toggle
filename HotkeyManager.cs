using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RefreshToggle;

internal sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 1;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HiddenWindow _window;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public HotkeyManager()
    {
        _window = new HiddenWindow(this);
    }

    /// <summary>
    /// Registers the global hotkey. Call this after the message pump is running.
    /// </summary>
    public bool TryRegister(ModifierKeys modifiers, Keys key, out string? error)
    {
        Unregister();

        if (!RegisterHotKey(_window.Handle, HOTKEY_ID, (uint)modifiers | MOD_NOREPEAT, (uint)key))
        {
            var errorCode = Marshal.GetLastWin32Error();
            error = $"RegisterHotKey failed (error {errorCode}). The combination may be in use by another application.";
            return false;
        }

        _registered = true;
        error = null;
        return true;
    }

    /// <summary>
    /// Re-registers the hotkey with new settings. Safe to call even if not currently registered.
    /// </summary>
    public bool TryReregister(ModifierKeys modifiers, Keys key, out string? error)
    {
        return TryRegister(modifiers, key, out error);
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HOTKEY_ID);
            _registered = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        _window.DestroyHandle();
    }

    internal void OnHotkeyPressed()
    {
        HotkeyPressed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Parses a modifier+key combination from config values into the Win32 types.
    /// Returns false if the input is invalid — never throws.
    /// </summary>
    public static bool TryParse(string? modifierString, string? keyString, out ModifierKeys modifiers, out Keys key, out string? error)
    {
        modifiers = ModifierKeys.None;
        key = Keys.None;
        error = null;

        if (string.IsNullOrWhiteSpace(modifierString) || string.IsNullOrWhiteSpace(keyString))
        {
            error = "Modifier or key is empty.";
            return false;
        }

        // Parse modifiers
        foreach (var part in modifierString!.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parsed = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => (ModifierKeys?)ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => null
            };

            if (parsed is null)
            {
                error = $"Unknown modifier: {part}";
                return false;
            }

            modifiers |= parsed.Value;
        }

        if (modifiers == ModifierKeys.None)
        {
            error = "At least one modifier key (Ctrl, Alt, Shift, Win) is required.";
            return false;
        }

        // Parse key
        if (!Enum.TryParse<Keys>(keyString, ignoreCase: true, out key) || key == Keys.None)
        {
            error = $"Unknown key: {keyString}";
            return false;
        }

        // Strip modifier flags that may be encoded in the high bits of the Keys enum value
        // (e.g. Keys.R is 82 which is clean, but Keys.ShiftKey would carry modifier bits).
        key = key & ~Keys.Shift & ~Keys.Control & ~Keys.Alt;

        return true;
    }

    /// <summary>
    /// Returns a human-readable representation like "Ctrl+Shift+R".
    /// </summary>
    public static string GetDisplayText(ModifierKeys modifiers, Keys key)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        parts.Add(key.ToString());

        return string.Join("+", parts);
    }

    // -----------------------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed class HiddenWindow : NativeWindow
    {
        private readonly HotkeyManager _owner;

        public HiddenWindow(HotkeyManager owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                _owner.OnHotkeyPressed();
                return;
            }

            base.WndProc(ref m);
        }
    }
}

[Flags]
internal enum ModifierKeys : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

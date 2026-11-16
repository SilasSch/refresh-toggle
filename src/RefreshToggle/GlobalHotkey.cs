using System.Runtime.InteropServices;

namespace RefreshToggle;

/// <summary>One system-wide hotkey, delivered through a hidden message-only window.</summary>
internal sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private int _id;  // 0 = nothing registered; alternates between 1 and 2
    private HotkeyBinding? _current;

    public GlobalHotkey() => CreateHandle(new CreateParams { Parent = MessageOnlyParent });

    public event Action? Pressed;

    /// <summary>
    /// Registers the binding. The previous hotkey is released only once the new one is active,
    /// so a failed change (combination taken by another app) keeps the old hotkey working.
    /// </summary>
    public bool TryRegister(HotkeyBinding binding)
    {
        if (_current == binding)
        {
            return true;
        }

        var newId = _id == 1 ? 2 : 1;
        if (!RegisterHotKey(Handle, newId, (uint)binding.Modifiers | ModNoRepeat, binding.VirtualKey))
        {
            return false;
        }

        if (_id != 0)
        {
            UnregisterHotKey(Handle, _id);
        }

        _id = newId;
        _current = binding;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            Pressed?.Invoke();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_id != 0)
        {
            UnregisterHotKey(Handle, _id);
            _id = 0;
        }

        DestroyHandle();
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

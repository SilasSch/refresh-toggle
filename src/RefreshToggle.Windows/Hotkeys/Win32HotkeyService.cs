using System.ComponentModel;
using System.Runtime.InteropServices;
using RefreshToggle.Application;
using RefreshToggle.Core;

namespace RefreshToggle.Windows.Hotkeys;

internal interface IHotkeyNativeBackend
{
    HotkeyNativeResult Register(IntPtr windowHandle, int slot, uint modifiers, uint virtualKey);

    HotkeyNativeResult Unregister(IntPtr windowHandle, int slot);
}

internal interface IHotkeyWindow : IDisposable
{
    IntPtr Handle { get; }

    event EventHandler<int>? SlotPressed;
}

internal readonly record struct HotkeyNativeResult(bool IsSuccess, string? Error)
{
    public static HotkeyNativeResult Success() => new(true, null);

    public static HotkeyNativeResult Failure(string error) => new(false, error);
}

/// <summary>
/// Keeps the old registration alive while a replacement is being reserved. The
/// application commits by persisting config and then unregistering the returned old
/// token; on save failure it unregisters the new token instead.
/// </summary>
internal sealed class Win32HotkeyService : IHotkeyService
{
    private const int FirstSlot = 1;
    private const int SecondSlot = 2;
    private const uint ModNoRepeat = 0x4000;

    private readonly object _sync = new();
    private readonly IHotkeyNativeBackend _backend;
    private readonly IHotkeyWindow _window;
    private readonly Dictionary<int, HotkeyBinding> _registrations = [];
    private bool _disposed;

    public Win32HotkeyService(
        IHotkeyNativeBackend? backend = null,
        IHotkeyWindow? window = null)
    {
        _backend = backend ?? new User32HotkeyNativeBackend();
        _window = window ?? new HotkeyMessageWindow();
        _window.SlotPressed += OnSlotPressed;
    }

    public event EventHandler? HotkeyPressed;

    public Task<Result<HotkeyRegistration>> RegisterAsync(
        HotkeyBinding binding,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result.Failure<HotkeyRegistration>(AppError.Cancelled()));
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return Task.FromResult(Result.Failure<HotkeyRegistration>(new AppError(
                    ErrorCode.Unavailable,
                    "The hotkey service has already been disposed.")));
            }

            var validation = HotkeyBinding.Create(binding.Modifiers, binding.Key);
            if (validation.IsFailure)
            {
                return Task.FromResult(Result.Failure<HotkeyRegistration>(validation.Error!));
            }

            if (_registrations.ContainsValue(binding))
            {
                return Task.FromResult(Result.Failure<HotkeyRegistration>(new AppError(
                    ErrorCode.Conflict,
                    $"The hotkey {binding.DisplayText} is already registered by RefreshToggle.")));
            }

            var slot = !_registrations.ContainsKey(FirstSlot)
                ? FirstSlot
                : !_registrations.ContainsKey(SecondSlot)
                    ? SecondSlot
                    : 0;
            if (slot == 0)
            {
                return Task.FromResult(Result.Failure<HotkeyRegistration>(new AppError(
                    ErrorCode.Conflict,
                    "Both transactional hotkey slots are occupied.")));
            }

            var nativeResult = _backend.Register(
                _window.Handle,
                slot,
                (uint)binding.Modifiers | ModNoRepeat,
                binding.VirtualKeyCode);
            if (!nativeResult.IsSuccess)
            {
                return Task.FromResult(Result.Failure<HotkeyRegistration>(new AppError(
                    ErrorCode.Registration,
                    nativeResult.Error ??
                    $"The hotkey {binding.DisplayText} could not be registered.")));
            }

            _registrations.Add(slot, binding);
            return Task.FromResult(Result.Success(new HotkeyRegistration(slot, binding)));
        }
    }

    public Task<Result> UnregisterAsync(
        HotkeyRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result.Failure(AppError.Cancelled()));
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return Task.FromResult(Result.Failure(new AppError(
                    ErrorCode.Unavailable,
                    "The hotkey service has already been disposed.")));
            }

            if (!_registrations.TryGetValue(registration.Slot, out var registeredBinding))
            {
                // Idempotent removal makes shutdown and rollback safe.
                return Task.FromResult(Result.Success());
            }

            if (registeredBinding != registration.Binding)
            {
                return Task.FromResult(Result.Failure(new AppError(
                    ErrorCode.Conflict,
                    "The hotkey registration token is stale.")));
            }

            var nativeResult = _backend.Unregister(_window.Handle, registration.Slot);
            if (!nativeResult.IsSuccess)
            {
                return Task.FromResult(Result.Failure(new AppError(
                    ErrorCode.Registration,
                    nativeResult.Error ?? "The hotkey could not be unregistered.")));
            }

            _registrations.Remove(registration.Slot);
            return Task.FromResult(Result.Success());
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            foreach (var slot in _registrations.Keys.ToArray())
            {
                _backend.Unregister(_window.Handle, slot);
            }

            _registrations.Clear();
            _window.SlotPressed -= OnSlotPressed;
            _window.Dispose();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }

    private void OnSlotPressed(object? sender, int slot)
    {
        lock (_sync)
        {
            if (_disposed || !_registrations.ContainsKey(slot))
            {
                return;
            }
        }

        HotkeyPressed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class User32HotkeyNativeBackend : IHotkeyNativeBackend
{
    public HotkeyNativeResult Register(IntPtr windowHandle, int slot, uint modifiers, uint virtualKey)
    {
        if (windowHandle == IntPtr.Zero || slot <= 0 || virtualKey == 0)
        {
            return HotkeyNativeResult.Failure("Invalid hotkey registration parameters.");
        }

        if (RegisterHotKey(windowHandle, slot, modifiers, virtualKey))
        {
            return HotkeyNativeResult.Success();
        }

        var errorCode = Marshal.GetLastWin32Error();
        var message = new Win32Exception(errorCode).Message;
        return HotkeyNativeResult.Failure(
            $"RegisterHotKey failed for slot {slot}: {message} (Win32 {errorCode}). " +
            "The combination may already be in use.");
    }

    public HotkeyNativeResult Unregister(IntPtr windowHandle, int slot)
    {
        if (UnregisterHotKey(windowHandle, slot))
        {
            return HotkeyNativeResult.Success();
        }

        var errorCode = Marshal.GetLastWin32Error();
        return HotkeyNativeResult.Failure(
            $"UnregisterHotKey failed for slot {slot}: {new Win32Exception(errorCode).Message} " +
            $"(Win32 {errorCode}).");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

internal sealed class HotkeyMessageWindow : NativeWindow, IHotkeyWindow
{
    private const int WmHotkey = 0x0312;
    private bool _disposed;

    public HotkeyMessageWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "RefreshToggle.HotkeyWindow",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        });
    }

    public event EventHandler<int>? SlotPressed;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey)
        {
            SlotPressed?.Invoke(this, message.WParam.ToInt32());
            return;
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyHandle();
        GC.SuppressFinalize(this);
    }
}

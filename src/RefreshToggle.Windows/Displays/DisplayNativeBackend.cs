using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RefreshToggle.Windows.Displays;

/// <summary>
/// The smallest test seam around the display-related User32 API. The application
/// service builds its transactional semantics on top of this interface.
/// </summary>
internal interface IDisplayNativeBackend
{
    NativeCallResult<IReadOnlyList<NativeDisplayDevice>> EnumerateActiveDisplays();

    NativeCallResult<NativeDisplayMode> GetCurrentMode(string deviceName);

    NativeCallResult<IReadOnlyList<NativeDisplayMode>> EnumerateModes(string deviceName);

    NativeCallResult TestMode(string deviceName, NativeDisplayMode mode);

    NativeCallResult ApplyMode(string deviceName, NativeDisplayMode mode);
}

internal sealed record NativeDisplayDevice(string DeviceName, string DisplayName, bool IsPrimary);

internal sealed record NativeDisplayMode(
    int Width,
    int Height,
    int BitsPerPixel,
    int RefreshRate,
    int PositionX,
    int PositionY,
    uint Orientation,
    uint FixedOutput);

internal readonly record struct NativeCallResult(bool IsSuccess, string? Error)
{
    public static NativeCallResult Success() => new(true, null);

    public static NativeCallResult Failure(string error) => new(false, error);
}

internal readonly record struct NativeCallResult<T>(bool IsSuccess, T? Value, string? Error)
{
    public static NativeCallResult<T> Success(T value) => new(true, value, null);

    public static NativeCallResult<T> Failure(string error) => new(false, default, error);
}

/// <summary>
/// Production implementation of the display API. It deliberately uses a complete
/// 220-byte DEVMODEW structure: an undersized display-only approximation can shift
/// fields on some Windows versions and produce incorrect modes.
/// </summary>
internal sealed class User32DisplayNativeBackend : IDisplayNativeBackend
{
    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const uint CdsTest = 0x00000002;
    private const uint DisplayDeviceAttachedToDesktop = 0x00000001;
    private const uint DisplayDevicePrimaryDevice = 0x00000004;
    private const uint DisplayDeviceMirroringDriver = 0x00000008;
    private const uint DmDisplayFrequency = 0x00400000;

    public NativeCallResult<IReadOnlyList<NativeDisplayDevice>> EnumerateActiveDisplays()
    {
        var result = new List<NativeDisplayDevice>();

        for (uint deviceIndex = 0; ; deviceIndex++)
        {
            var device = DISPLAY_DEVICE.Create();
            if (!EnumDisplayDevices(null, deviceIndex, ref device, 0))
            {
                break;
            }

            if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0 ||
                (device.StateFlags & DisplayDeviceMirroringDriver) != 0 ||
                string.IsNullOrWhiteSpace(device.DeviceName))
            {
                continue;
            }

            var isPrimary = (device.StateFlags & DisplayDevicePrimaryDevice) != 0;
            var friendlyName = string.IsNullOrWhiteSpace(device.DeviceString)
                ? TrimDevicePrefix(device.DeviceName)
                : device.DeviceString.Trim();

            result.Add(new NativeDisplayDevice(device.DeviceName, friendlyName, isPrimary));
        }

        return NativeCallResult<IReadOnlyList<NativeDisplayDevice>>.Success(
            result
                .OrderByDescending(display => display.IsPrimary)
                .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public NativeCallResult<NativeDisplayMode> GetCurrentMode(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return NativeCallResult<NativeDisplayMode>.Failure("A display device name is required.");
        }

        var mode = DEVMODE.Create();
        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref mode, 0))
        {
            return NativeCallResult<NativeDisplayMode>.Failure(CreateLastError(
                $"EnumDisplaySettingsEx failed for '{deviceName}'"));
        }

        return NativeCallResult<NativeDisplayMode>.Success(ToNativeMode(mode));
    }

    public NativeCallResult<IReadOnlyList<NativeDisplayMode>> EnumerateModes(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return NativeCallResult<IReadOnlyList<NativeDisplayMode>>.Failure(
                "A display device name is required.");
        }

        var modes = new List<NativeDisplayMode>();
        for (var modeIndex = 0; ; modeIndex++)
        {
            var mode = DEVMODE.Create();
            if (!EnumDisplaySettingsEx(deviceName, modeIndex, ref mode, 0))
            {
                break;
            }

            modes.Add(ToNativeMode(mode));
        }

        return NativeCallResult<IReadOnlyList<NativeDisplayMode>>.Success(modes);
    }

    public NativeCallResult TestMode(string deviceName, NativeDisplayMode mode) =>
        ChangeMode(deviceName, mode, CdsTest, "test");

    public NativeCallResult ApplyMode(string deviceName, NativeDisplayMode mode) =>
        ChangeMode(deviceName, mode, 0, "apply");

    public static int DevModeSize => Marshal.SizeOf<DEVMODE>();

    private static NativeCallResult ChangeMode(
        string deviceName,
        NativeDisplayMode requestedMode,
        uint flags,
        string operation)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return NativeCallResult.Failure("A display device name is required.");
        }

        if (requestedMode.RefreshRate <= 1)
        {
            return NativeCallResult.Failure("Refresh rate must be greater than 1 Hz.");
        }

        var mode = DEVMODE.Create();
        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref mode, 0))
        {
            return NativeCallResult.Failure(CreateLastError(
                $"EnumDisplaySettingsEx failed for '{deviceName}' before {operation}"));
        }

        // Preserve every part of the current mode and change only the frequency.
        mode.dmDisplayFrequency = checked((uint)requestedMode.RefreshRate);
        mode.dmFields |= DmDisplayFrequency;

        var changeResult = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, flags, IntPtr.Zero);
        return changeResult == DispChangeSuccessful
            ? NativeCallResult.Success()
            : NativeCallResult.Failure(
                $"ChangeDisplaySettingsEx {operation} failed for '{deviceName}' with code {changeResult}.");
    }

    private static NativeDisplayMode ToNativeMode(DEVMODE mode) => new(
        checked((int)mode.dmPelsWidth),
        checked((int)mode.dmPelsHeight),
        checked((int)mode.dmBitsPerPel),
        checked((int)mode.dmDisplayFrequency),
        mode.dmPositionX,
        mode.dmPositionY,
        mode.dmDisplayOrientation,
        mode.dmDisplayFixedOutput);

    private static string CreateLastError(string prefix)
    {
        var error = Marshal.GetLastWin32Error();
        return error == 0
            ? prefix + "."
            : $"{prefix}: {new Win32Exception(error).Message} (Win32 {error}).";
    }

    private static string TrimDevicePrefix(string deviceName) =>
        deviceName.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            ? deviceName[4..]
            : deviceName;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(
        string lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode,
        uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ChangeDisplaySettingsEx(
        string lpszDeviceName,
        ref DEVMODE lpDevMode,
        IntPtr hwnd,
        uint dwFlags,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public uint cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;

        public static DISPLAY_DEVICE Create() => new()
        {
            cb = checked((uint)Marshal.SizeOf<DISPLAY_DEVICE>()),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceID = string.Empty,
            DeviceKey = string.Empty
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;

        // This is the display arm of the 16-byte printer/display union.
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;

        public static DEVMODE Create() => new()
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = checked((ushort)Marshal.SizeOf<DEVMODE>())
        };
    }
}

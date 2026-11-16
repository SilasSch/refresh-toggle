using System.Runtime.InteropServices;

namespace RefreshToggle;

internal sealed record Display(string DeviceName, string Name, bool IsPrimary, int CurrentRate, IReadOnlyList<int> SupportedRates);

/// <summary>Thin wrapper over the Win32 display APIs.</summary>
internal static class Displays
{
    private const int EnumCurrentSettings = -1;
    private const uint AttachedToDesktop = 0x1;
    private const uint PrimaryDevice = 0x4;
    private const uint MirroringDriver = 0x8;
    private const uint DmDisplayFrequency = 0x00400000;

    /// <summary>All monitors attached to the desktop, primary first.</summary>
    public static List<Display> GetActive()
    {
        var displays = new List<Display>();
        var device = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint index = 0; EnumDisplayDevices(null, index, ref device, 0); index++)
        {
            if ((device.StateFlags & AttachedToDesktop) == 0 || (device.StateFlags & MirroringDriver) != 0)
            {
                continue;
            }

            var current = NewDevMode();
            if (!EnumDisplaySettingsEx(device.DeviceName, EnumCurrentSettings, ref current, 0))
            {
                continue;
            }

            var isPrimary = (device.StateFlags & PrimaryDevice) != 0;
            var name = device.DeviceName.Replace(@"\\.\DISPLAY", "Display ", StringComparison.OrdinalIgnoreCase);
            displays.Add(new Display(
                device.DeviceName,
                isPrimary ? $"{name} (primary)" : name,
                isPrimary,
                (int)current.dmDisplayFrequency,
                GetSupportedRates(device.DeviceName, current)));
        }

        return [.. displays.OrderByDescending(display => display.IsPrimary)];
    }

    /// <summary>Changes only the refresh rate of a monitor. Returns the DISP_CHANGE code (0 = success).</summary>
    public static int SetRate(string deviceName, int rate)
    {
        var mode = NewDevMode();
        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref mode, 0))
        {
            return -1;
        }

        mode.dmDisplayFrequency = (uint)rate;
        mode.dmFields |= DmDisplayFrequency;
        return ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
    }

    /// <summary>Refresh rates available at the monitor's current resolution and colour depth.</summary>
    private static int[] GetSupportedRates(string deviceName, DEVMODE current)
    {
        var rates = new SortedSet<int>();
        var mode = NewDevMode();
        for (var index = 0; EnumDisplaySettingsEx(deviceName, index, ref mode, 0); index++)
        {
            if (mode.dmPelsWidth == current.dmPelsWidth &&
                mode.dmPelsHeight == current.dmPelsHeight &&
                mode.dmBitsPerPel == current.dmBitsPerPel &&
                mode.dmDisplayFrequency > 1)
            {
                rates.Add((int)mode.dmDisplayFrequency);
            }
        }

        return [.. rates];
    }

    private static DEVMODE NewDevMode() => new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwFlags, IntPtr lParam);

#pragma warning disable CS0649, IDE1006 // Fields are written by Win32; names mirror the Win32 structs.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;            // display arm of the printer/display union
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
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
    }
#pragma warning restore CS0649, IDE1006
}

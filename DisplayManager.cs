using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RefreshToggle;

internal sealed record DisplayInfo(string DeviceName, string Label, bool IsPrimary);

internal sealed class DisplayManager
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var screens = Screen.AllScreens
            .OrderByDescending(screen => screen.Primary)
            .ThenBy(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var displays = new List<DisplayInfo>(screens.Length);

        for (var index = 0; index < screens.Length; index++)
        {
            var screen = screens[index];
            var label = CreateDisplayLabel(screen);

            displays.Add(new DisplayInfo(screen.DeviceName, label, screen.Primary));
        }

        return displays;
    }

    private static string CreateDisplayLabel(Screen screen)
    {
        var friendlyName = screen.DeviceName.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            ? screen.DeviceName[4..]
            : screen.DeviceName;
        var label = $"Display {friendlyName}";
        if (screen.Primary)
        {
            label += " (Primary)";
        }

        return label;
    }

    public bool TryGetCurrentRefreshRate(out int refreshRate, out string? error) =>
        TryGetCurrentRefreshRate(null, out refreshRate, out error);

    public bool TryGetCurrentRefreshRate(string? deviceName, out int refreshRate, out string? error)
    {
        var mode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode))
        {
            refreshRate = 0;
            error = $"EnumDisplaySettings failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        refreshRate = mode.dmDisplayFrequency;
        error = null;
        return true;
    }

    public bool TrySetRefreshRate(int refreshRate, out string? error) =>
        TrySetRefreshRate(null, refreshRate, out error);

    public bool TrySetRefreshRate(string? deviceName, int refreshRate, out string? error)
    {
        if (refreshRate <= 0)
        {
            error = "Refresh rate must be greater than 0.";
            return false;
        }

        var mode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode))
        {
            error = $"EnumDisplaySettings failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        mode.dmDisplayFrequency = refreshRate;
        mode.dmFields |= DM_DISPLAYFREQUENCY;

        var result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
        if (result != DISP_CHANGE_SUCCESSFUL)
        {
            error = $"ChangeDisplaySettingsEx failed with code {result}.";
            return false;
        }

        error = null;
        return true;
    }

    public IReadOnlyList<int> GetSupportedRefreshRates(string? deviceName = null)
    {
        var rates = new HashSet<int>();
        for (var modeIndex = 0; ; modeIndex++)
        {
            var mode = CreateDevMode();
            if (!EnumDisplaySettings(deviceName, modeIndex, ref mode))
            {
                break;
            }

            if (mode.dmDisplayFrequency > 0)
            {
                rates.Add(mode.dmDisplayFrequency);
            }
        }

        return rates.OrderBy(rate => rate).ToArray();
    }

    private static DEVMODE CreateDevMode()
    {
        var mode = new DEVMODE();
        mode.dmDeviceName = string.Empty;
        mode.dmFormName = string.Empty;
        mode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        return mode;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplaySettings(
        string? lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        ref DEVMODE lpDevMode,
        IntPtr hwnd,
        uint dwflags,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public ushort dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}

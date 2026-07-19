using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace RefreshToggle.App;

internal static class TrayIconRenderer
{
    private static readonly Color RateAColor = Color.FromArgb(0x33, 0x88, 0xFF);
    private static readonly Color RateBColor = Color.FromArgb(0x22, 0xCC, 0x55);
    private static readonly Color UnknownColor = Color.FromArgb(0x88, 0x88, 0x88);

    internal static string FormatLabel(int refreshRate) => refreshRate > 1
        ? refreshRate.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "?";

    public static Icon CreateForRate(int refreshRate, int rateA, int rateB)
    {
        var background = refreshRate == rateA
            ? RateAColor
            : refreshRate == rateB
                ? RateBColor
                : UnknownColor;

        return TryBuildIcon(FormatLabel(refreshRate), background);
    }

    public static Icon CreateUnknown() => TryBuildIcon("?", UnknownColor);

    private static Icon TryBuildIcon(string label, Color background)
    {
        try
        {
            return BuildIcon(label, background);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Dynamic tray icon rendering failed: {exception}");
            return LoadFallbackIcon();
        }
    }

    private static Icon BuildIcon(string label, Color background)
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var radius = Math.Max(2, size * 3 / 16);
        using var backgroundBrush = new SolidBrush(background);
        using var path = CreateRoundedRectangle(new Rectangle(0, 0, size, size), radius);
        graphics.FillPath(backgroundBrush, path);

        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var fontSize = FindFontSize(graphics, label, size);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString(label, font, textBrush, new RectangleF(0, 0, size, size), format);

        var nativeIcon = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(nativeIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            if (!DestroyIcon(nativeIcon))
            {
                System.Diagnostics.Debug.WriteLine($"DestroyIcon failed: {Marshal.GetLastWin32Error()}");
            }
        }
    }

    private static float FindFontSize(Graphics graphics, string label, int size)
    {
        var candidate = size * (label.Length >= 3 ? 0.48f : 0.62f);
        var maximumWidth = size - 2f;
        var maximumHeight = size - 2f;

        while (candidate > 5f)
        {
            using var font = new Font("Segoe UI", candidate, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(label, font, int.MaxValue, StringFormat.GenericTypographic);
            if (measured.Width <= maximumWidth && measured.Height <= maximumHeight)
            {
                return candidate;
            }

            candidate -= 0.5f;
        }

        return 5f;
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Icon LoadFallbackIcon()
    {
        using var stream = typeof(TrayIconRenderer).Assembly.GetManifestResourceStream("RefreshToggle.icon.ico")
            ?? throw new InvalidOperationException("Embedded fallback icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}

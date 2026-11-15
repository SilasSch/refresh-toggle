using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RefreshToggle;

/// <summary>Draws the tray icon: a rounded square with the current refresh rate.</summary>
internal static class IconRenderer
{
    private static readonly Color RateAColor = Color.FromArgb(0x33, 0x88, 0xFF);
    private static readonly Color RateBColor = Color.FromArgb(0x22, 0xCC, 0x55);
    private static readonly Color OtherColor = Color.FromArgb(0x88, 0x88, 0x88);

    /// <summary>Blue for Rate A, green for Rate B, grey for anything else or unknown (rate 0).</summary>
    public static Icon Create(int rate, int rateA, int rateB)
    {
        var color = rate == rateA ? RateAColor : rate == rateB ? RateBColor : OtherColor;
        var label = rate > 1 ? rate.ToString(CultureInfo.InvariantCulture) : "?";
        try
        {
            return Draw(label, color);
        }
        catch (Exception exception) when (exception is ExternalException or ArgumentException)
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    private static Icon Draw(string label, Color background)
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var brush = new SolidBrush(background);
            using var shape = RoundedSquare(size, radius: Math.Max(2, size * 3 / 16));
            graphics.FillPath(brush, shape);

            using var font = FitFont(graphics, label, size);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(label, font, Brushes.White, new RectangleF(0, 0, size, size), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Largest bold Segoe UI that fits, so three-digit rates (144, 240) stay readable.</summary>
    private static Font FitFont(Graphics graphics, string label, int size)
    {
        for (var pixels = size * (label.Length >= 3 ? 0.48f : 0.62f); pixels > 5f; pixels -= 0.5f)
        {
            var font = new Font("Segoe UI", pixels, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(label, font, int.MaxValue, StringFormat.GenericTypographic);
            if (measured.Width <= size - 2 && measured.Height <= size - 2)
            {
                return font;
            }

            font.Dispose();
        }

        return new Font("Segoe UI", 5f, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static GraphicsPath RoundedSquare(int size, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(size - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(size - diameter, size - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, size - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}

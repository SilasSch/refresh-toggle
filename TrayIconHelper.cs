using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace RefreshToggle;

internal static class TrayIconHelper
{
    // Blue for the lower rate (e.g. 60 Hz)
    private static readonly Color ColorRateA = Color.FromArgb(0x33, 0x88, 0xFF);

    // Green for the higher rate (e.g. 120 Hz)
    private static readonly Color ColorRateB = Color.FromArgb(0x22, 0xCC, 0x55);

    // Grey when the current rate is unknown or matches neither configured rate
    private static readonly Color ColorUnknown = Color.FromArgb(0x88, 0x88, 0x88);

    /// <summary>
    /// Creates a 16×16 tray icon that shows <paramref name="refreshRate"/> on a
    /// coloured background: blue when it matches <see cref="AppConfig.RateA"/>,
    /// green when it matches <see cref="AppConfig.RateB"/>, grey otherwise.
    /// The caller is responsible for disposing the returned <see cref="Icon"/>.
    /// </summary>
    public static Icon CreateForRate(int refreshRate, AppConfig config)
    {
        Color bg;
        if (refreshRate == config.RateA)
            bg = ColorRateA;
        else if (refreshRate == config.RateB)
            bg = ColorRateB;
        else
            bg = ColorUnknown;

        return BuildIcon(refreshRate.ToString(), bg);
    }

    /// <summary>
    /// Creates a 16×16 grey tray icon with a "?" label, used when the refresh
    /// rate cannot be determined.
    /// The caller is responsible for disposing the returned <see cref="Icon"/>.
    /// </summary>
    public static Icon CreateUnknown() => BuildIcon("?", ColorUnknown);

    // -------------------------------------------------------------------------

    private static Icon BuildIcon(string label, Color background)
    {
        const int size = 16;

        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        // Rounded-rectangle background
        using var bgBrush = new SolidBrush(background);
        using var path = RoundedRect(new Rectangle(0, 0, size, size), radius: 3);
        g.FillPath(bgBrush, path);

        // White label – shrink font for 3-digit numbers
        float fontSize = label.Length >= 3 ? 5.5f : 7f;
        using var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Color.White);

        var textSize = g.MeasureString(label, font);
        float x = (size - textSize.Width) / 2f;
        float y = (size - textSize.Height) / 2f;
        g.DrawString(label, font, textBrush, x, y);

        // Convert Bitmap → Icon (clone so we can safely destroy the GDI handle)
        var hicon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hicon).Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

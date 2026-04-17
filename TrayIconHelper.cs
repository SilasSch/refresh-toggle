using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace RefreshToggle;

internal static class TrayIconHelper
{
    // Blue for RateA (e.g. 60 Hz by default)
    private static readonly Color ColorRateA = Color.FromArgb(0x33, 0x88, 0xFF);

    // Green for RateB (e.g. 120 Hz by default)
    private static readonly Color ColorRateB = Color.FromArgb(0x22, 0xCC, 0x55);

    // Grey when the current rate is unknown or matches neither configured rate
    private static readonly Color ColorUnknown = Color.FromArgb(0x88, 0x88, 0x88);

    /// <summary>
    /// Creates a tray icon sized to <see cref="SystemInformation.SmallIconSize"/> that
    /// shows <paramref name="refreshRate"/> on a coloured background: blue when it
    /// matches <see cref="AppConfig.RateA"/>, green when it matches
    /// <see cref="AppConfig.RateB"/>, grey when it matches neither (rate is unknown or
    /// outside configured values).
    /// The caller is responsible for disposing the returned <see cref="Icon"/>.
    /// </summary>
    public static Icon CreateForRate(int refreshRate, AppConfig config)
    {
        if (refreshRate <= 1)
        {
            return CreateUnknown();
        }

        Color bg;
        if (refreshRate == config.RateA)
        {
            bg = ColorRateA;
        }
        else if (refreshRate == config.RateB)
        {
            bg = ColorRateB;
        }
        else
        {
            bg = ColorUnknown;
        }

        return BuildIcon(refreshRate.ToString(), bg);
    }

    /// <summary>
    /// Creates a grey tray icon with a "?" label, sized to
    /// <see cref="SystemInformation.SmallIconSize"/>, used when the refresh rate cannot
    /// be determined.
    /// The caller is responsible for disposing the returned <see cref="Icon"/>.
    /// </summary>
    public static Icon CreateUnknown() => BuildIcon("?", ColorUnknown);

    // -------------------------------------------------------------------------

    private static Icon BuildIcon(string label, Color background)
    {
        int size = SystemInformation.SmallIconSize.Width;

        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        // Rounded-rectangle background – corner radius scales with icon size
        int radius = Math.Max(2, size * 3 / 16);
        using var bgBrush = new SolidBrush(background);
        using var path = RoundedRect(new Rectangle(0, 0, size, size), radius);
        g.FillPath(bgBrush, path);

        // White label – font size scales with icon size; shrink further for 3-digit numbers
        float fontSize = label.Length >= 3 ? size * 5.5f / 16f : size * 7f / 16f;
        using var font = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);

        using var stringFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var textBounds = new RectangleF(0, 0, size, size);
        g.DrawString(label, font, textBrush, textBounds, stringFormat);

        // Convert Bitmap → Icon (clone so we can safely destroy the GDI handle)
        var hicon = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(hicon);
            return (Icon)icon.Clone();
        }
        finally
        {
            bool destroyed = DestroyIcon(hicon);
            if (!destroyed)
            {
                System.Diagnostics.Debug.Fail($"DestroyIcon failed with error {Marshal.GetLastWin32Error()}");
            }
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

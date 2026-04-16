using System.Threading;
using System.Windows.Forms;

namespace RefreshToggle;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Global\RefreshToggle_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var trayApp = new TrayApp();
        Application.Run();
    }
}

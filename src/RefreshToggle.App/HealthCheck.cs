using RefreshToggle.Core;
using RefreshToggle.Windows.Deployment;
using RefreshToggle.Windows.Displays;

namespace RefreshToggle.App;

internal static class HealthCheck
{
    public static int Run(TextWriter? output = null)
    {
        output ??= Console.Out;
        try
        {
            var configValidation = AppConfig.Default.Validate();
            if (configValidation.IsFailure)
            {
                return Fail(output, configValidation.Error!.Message);
            }

            var hotkey = HotkeyBinding.Parse(
                AppConfig.Default.HotkeyModifiers,
                AppConfig.Default.HotkeyKey);
            if (hotkey.IsFailure)
            {
                return Fail(output, hotkey.Error!.Message);
            }

            if (User32DisplayNativeBackend.DevModeSize != 220)
            {
                return Fail(
                    output,
                    $"Unexpected DEVMODEW size: {User32DisplayNativeBackend.DevModeSize}.");
            }

            using (var iconStream = typeof(HealthCheck).Assembly
                       .GetManifestResourceStream("RefreshToggle.icon.ico"))
            {
                if (iconStream is null || iconStream.Length == 0)
                {
                    return Fail(output, "The embedded fallback icon is missing or empty.");
                }
            }

            var deployment = DeploymentManifest.InspectCurrentProcess();
            if (OperatingSystem.IsWindows())
            {
                var displays = new User32DisplayNativeBackend().EnumerateActiveDisplays();
                if (!displays.IsSuccess)
                {
                    return Fail(output, displays.Error ?? "Read-only display enumeration failed.");
                }
            }

            output.WriteLine(
                $"RefreshToggle health check passed ({deployment.Flavor}, {deployment.RuntimeIdentifier}).");
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Fail(output, $"Unexpected health-check failure: {exception.Message}");
        }
    }

    private static int Fail(TextWriter output, string message)
    {
        output.WriteLine("RefreshToggle health check failed: " + message);
        return 1;
    }
}

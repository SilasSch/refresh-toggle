using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RefreshToggle.Windows.Deployment;

internal static class ReaperRunner
{
    private const uint MoveFileDelayUntilReboot = 0x00000004;

    public static async Task<int> RunAsync(
        ReaperRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currentPath = Environment.ProcessPath;
        var expectedPath = CleanupNames.GetReaperPath(request.Nonce);
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(currentPath) ||
            !PathsEqual(currentPath, expectedPath) ||
            HasReparsePoint(expectedPath))
        {
            return 2;
        }

        Process helper;
        try
        {
            helper = Process.GetProcessById(request.HelperProcessId);
            if (helper.StartTime.ToUniversalTime().ToFileTimeUtc() != request.HelperStartFileTimeUtc)
            {
                helper.Dispose();
                return 3;
            }
        }
        catch (Exception exception) when (IsReaperException(exception))
        {
            return 3;
        }

        using (helper)
        using (var ready = OpenEvent(CleanupNames.ReaperReadyEvent(request.Nonce)))
        using (var abort = OpenEvent(CleanupNames.AbortEvent(request.Nonce)))
        {
            if (ready is null || abort is null)
            {
                return 4;
            }

            ready.Set();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                var helperExit = helper.WaitForExitAsync(timeout.Token);
                var abortSignal = CleanupWaitHandle.WaitOneAsync(abort, TimeSpan.FromMinutes(3), timeout.Token);
                var first = await Task.WhenAny(helperExit, abortSignal).ConfigureAwait(false);
                if (first == abortSignal && await abortSignal.ConfigureAwait(false))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                }

                await helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return 5;
            }
        }

        var helperPath = CleanupNames.GetHelperPath(request.Nonce);
        for (var attempt = 0; attempt < 120 && File.Exists(helperPath); attempt++)
        {
            try
            {
                if (!HasReparsePoint(helperPath))
                {
                    File.Delete(helperPath);
                }
            }
            catch (Exception exception) when (IsReaperException(exception))
            {
            }

            if (File.Exists(helperPath))
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        if (File.Exists(helperPath))
        {
            MoveFileEx(helperPath, null, MoveFileDelayUntilReboot);
        }

        // A process cannot unlink its own mapped executable reliably on Windows.
        // Scheduling this exact nonce-bound file is the final, shell-free fallback;
        // StaleCleanup also removes it after 24h if Windows allows it sooner.
        MoveFileEx(expectedPath, null, MoveFileDelayUntilReboot);
        return 0;
    }

    private static EventWaitHandle? OpenEvent(string name)
    {
        try
        {
            return EventWaitHandle.OpenExisting(name);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsReaperException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        ArgumentException or
        InvalidOperationException or
        NotSupportedException or
        System.ComponentModel.Win32Exception;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);
}

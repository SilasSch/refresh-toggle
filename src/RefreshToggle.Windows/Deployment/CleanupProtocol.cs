using System.Globalization;

namespace RefreshToggle.Windows.Deployment;

internal sealed record CleanupRequest(
    int ParentProcessId,
    long ParentStartFileTimeUtc,
    Guid Nonce);

internal sealed record ReaperRequest(
    int HelperProcessId,
    long HelperStartFileTimeUtc,
    Guid Nonce);

internal static class CleanupCommand
{
    public const string Switch = "--cleanup-v1";

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Switch, StringComparison.Ordinal);

    public static bool TryParse(string[] args, out CleanupRequest? request)
    {
        request = null;
        if (args.Length != 4 || !string.Equals(args[0], Switch, StringComparison.Ordinal) ||
            !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
            processId <= 0 ||
            !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var startTime) ||
            startTime <= 0 ||
            !TryParseNonce(args[3], out var nonce))
        {
            return false;
        }

        request = new CleanupRequest(processId, startTime, nonce);
        return true;
    }

    internal static bool TryParseNonce(string value, out Guid nonce)
    {
        nonce = Guid.Empty;
        return value.Length == 32 && Guid.TryParseExact(value, "N", out nonce) && nonce != Guid.Empty;
    }
}

internal static class ReaperCommand
{
    public const string Switch = "--reaper-v1";

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Switch, StringComparison.Ordinal);

    public static bool TryParse(string[] args, out ReaperRequest? request)
    {
        request = null;
        if (args.Length != 4 || !string.Equals(args[0], Switch, StringComparison.Ordinal) ||
            !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
            processId <= 0 ||
            !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var startTime) ||
            startTime <= 0 ||
            !CleanupCommand.TryParseNonce(args[3], out var nonce))
        {
            return false;
        }

        request = new ReaperRequest(processId, startTime, nonce);
        return true;
    }
}

internal static class CleanupNames
{
    public static string HelperFile(Guid nonce) => $"RefreshToggle-cleanup-{nonce:N}.exe";

    public static string ReaperFile(Guid nonce) => $"RefreshToggle-reaper-{nonce:N}.exe";

    public static string ResultFile(Guid nonce) => $"RefreshToggle-cleanup-{nonce:N}.result";

    public static string HelperReadyEvent(Guid nonce) => $@"Local\RefreshToggle.Cleanup.HelperReady.{nonce:N}";

    public static string ReaperReadyEvent(Guid nonce) => $@"Local\RefreshToggle.Cleanup.ReaperReady.{nonce:N}";

    public static string CommitEvent(Guid nonce) => $@"Local\RefreshToggle.Cleanup.Commit.{nonce:N}";

    public static string AbortEvent(Guid nonce) => $@"Local\RefreshToggle.Cleanup.Abort.{nonce:N}";

    public static string GetHelperPath(Guid nonce) =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), HelperFile(nonce)));

    public static string GetReaperPath(Guid nonce) =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), ReaperFile(nonce)));

    public static string GetResultPath(Guid nonce) =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), ResultFile(nonce)));
}

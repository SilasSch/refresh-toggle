using System.Text.RegularExpressions;

namespace RefreshToggle.Windows.Deployment;

internal static partial class StaleCleanup
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);

    public static void RemoveRecognizedArtifacts(TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;
        var tempDirectory = Path.GetFullPath(Path.GetTempPath());
        var threshold = timeProvider.GetUtcNow() - MinimumAge;

        try
        {
            foreach (var path in Directory.EnumerateFiles(tempDirectory, "RefreshToggle-*", SearchOption.TopDirectoryOnly))
            {
                TryRemoveRecognizedFile(path, tempDirectory, threshold);
            }
        }
        catch (Exception exception) when (IsCleanupException(exception))
        {
            System.Diagnostics.Debug.WriteLine($"Stale cleanup enumeration failed: {exception.Message}");
        }
    }

    private static void TryRemoveRecognizedFile(
        string path,
        string expectedDirectory,
        DateTimeOffset threshold)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!string.Equals(
                    Path.GetDirectoryName(fullPath),
                    Path.TrimEndingDirectorySeparator(expectedDirectory),
                    StringComparison.OrdinalIgnoreCase) ||
                !RecognizedName().IsMatch(Path.GetFileName(fullPath)) ||
                (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0 ||
                File.GetLastWriteTimeUtc(fullPath) > threshold.UtcDateTime)
            {
                return;
            }

            File.Delete(fullPath);
        }
        catch (Exception exception) when (IsCleanupException(exception))
        {
            System.Diagnostics.Debug.WriteLine($"Stale cleanup skipped '{path}': {exception.Message}");
        }
    }

    private static bool IsCleanupException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        ArgumentException or
        NotSupportedException;

    [GeneratedRegex(
        "^RefreshToggle-(?:cleanup|reaper)-[0-9a-f]{32}\\.exe$|^RefreshToggle-cleanup-[0-9a-f]{32}\\.result$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RecognizedName();
}

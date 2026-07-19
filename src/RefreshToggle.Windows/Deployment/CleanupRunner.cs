using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RefreshToggle.Core;

namespace RefreshToggle.Windows.Deployment;

internal static class CleanupRunner
{
    private const int DeleteAttempts = 40;
    private static readonly TimeSpan DeleteDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CommitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);

    public static async Task<int> RunAsync(
        CleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = ValidateCleanupRequest(request);
        if (validation.IsFailure)
        {
            WriteResult(request.Nonce, "validation-failed", validation.Error!.Message);
            return 2;
        }

        using var parentProcess = validation.Value.ParentProcess;
        using var helperReady = OpenEvent(CleanupNames.HelperReadyEvent(request.Nonce));
        using var reaperReady = OpenEvent(CleanupNames.ReaperReadyEvent(request.Nonce));
        using var commit = OpenEvent(CleanupNames.CommitEvent(request.Nonce));
        using var abort = OpenEvent(CleanupNames.AbortEvent(request.Nonce));
        if (helperReady is null || reaperReady is null || commit is null || abort is null)
        {
            WriteResult(request.Nonce, "handshake-failed", "One or more cleanup events were unavailable.");
            return 3;
        }

        var reaper = await StartReaperAsync(request.Nonce, cancellationToken).ConfigureAwait(false);
        if (reaper.IsFailure)
        {
            WriteResult(request.Nonce, "reaper-failed", reaper.Error!.Message);
            return 4;
        }

        using (reaper.Value)
        {
            helperReady.Set();

            var handshake = await WaitForCommitAsync(commit, abort, cancellationToken).ConfigureAwait(false);
            if (handshake != CleanupHandshake.Commit)
            {
                WriteResult(
                    request.Nonce,
                    handshake == CleanupHandshake.Abort ? "aborted" : "commit-timeout",
                    "Cleanup was not committed by the parent process.");
                return 5;
            }

            if (!await WaitForProcessExitAsync(parentProcess, ParentExitTimeout, cancellationToken)
                    .ConfigureAwait(false))
            {
                WriteResult(
                    request.Nonce,
                    "parent-timeout",
                    "The exact parent process did not exit before the cleanup timeout.");
                return 6;
            }

            var paths = validation.Value.Paths;
            var deleteResult = await DeleteWithRetriesAsync(
                    paths.InstalledExecutablePath,
                    cancellationToken)
                .ConfigureAwait(false);

            var scheduledForReboot = false;
            if (!deleteResult)
            {
                scheduledForReboot = ScheduleDeleteOnReboot(paths.InstalledExecutablePath);
            }

            var directoryRemoved = TryDeleteEmptyDirectory(paths.InstallDirectory);
            if (!directoryRemoved && scheduledForReboot)
            {
                ScheduleDeleteOnReboot(paths.InstallDirectory);
            }

            if (!deleteResult)
            {
                WriteResult(
                    request.Nonce,
                    scheduledForReboot ? "scheduled-for-reboot" : "delete-failed",
                    scheduledForReboot
                        ? "The installed executable is locked and will be removed at the next reboot."
                        : "The installed executable could not be removed or scheduled for removal.");
                return scheduledForReboot ? 10 : 7;
            }

            WriteResult(
                request.Nonce,
                "success",
                directoryRemoved
                    ? "Installed executable and empty installation directory were removed. User configuration was retained."
                    : "Installed executable was removed. The non-empty installation directory was retained.");
            return 0;
        }
    }

    private static Result<ValidatedCleanupRequest> ValidateCleanupRequest(CleanupRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Failure<ValidatedCleanupRequest>(new AppError(
                ErrorCode.Unavailable,
                "Cleanup is only supported on Windows."));
        }

        var currentPath = Environment.ProcessPath;
        var expectedHelperPath = CleanupNames.GetHelperPath(request.Nonce);
        if (string.IsNullOrWhiteSpace(currentPath) || !PathsEqual(currentPath, expectedHelperPath))
        {
            return Result.Failure<ValidatedCleanupRequest>(new AppError(
                ErrorCode.Security,
                "The cleanup command is not running from its exact nonce-bound temporary helper path."));
        }

        if (HasReparsePoint(expectedHelperPath))
        {
            return Result.Failure<ValidatedCleanupRequest>(new AppError(
                ErrorCode.Security,
                "The cleanup helper path is a reparse point."));
        }

        InstallationPaths paths;
        try
        {
            paths = InstallationPaths.ForCurrentUser();
        }
        catch (Exception exception) when (IsCleanupException(exception))
        {
            return Result.Failure<ValidatedCleanupRequest>(new AppError(
                ErrorCode.Security,
                $"Canonical installation paths could not be derived: {exception.Message}",
                exception));
        }

        if (Directory.Exists(paths.InstallDirectory) && HasReparsePoint(paths.InstallDirectory))
        {
            return Result.Failure<ValidatedCleanupRequest>(new AppError(
                ErrorCode.Security,
                "The canonical installation directory is a reparse point."));
        }

        if (File.Exists(paths.InstalledExecutablePath) && HasReparsePoint(paths.InstalledExecutablePath))
        {
            return Result.Failure<ValidatedCleanupRequest>(new AppError(
                ErrorCode.Security,
                "The installed executable is a reparse point."));
        }

        Process parentProcess;
        try
        {
            parentProcess = Process.GetProcessById(request.ParentProcessId);
            var actualStartTime = parentProcess.StartTime.ToUniversalTime().ToFileTimeUtc();
            if (actualStartTime != request.ParentStartFileTimeUtc)
            {
                parentProcess.Dispose();
                return Result.Failure<ValidatedCleanupRequest>(new AppError(
                    ErrorCode.Security,
                    "The parent process start time does not match; possible PID reuse was detected."));
            }
        }
        catch (Exception exception) when (IsProcessException(exception))
        {
            return Result.Failure<ValidatedCleanupRequest>(new AppError(
                ErrorCode.Security,
                $"The exact parent process could not be validated: {exception.Message}",
                exception));
        }

        return Result.Success(new ValidatedCleanupRequest(paths, parentProcess));
    }

    private static async Task<Result<Process>> StartReaperAsync(Guid nonce, CancellationToken cancellationToken)
    {
        var helperPath = CleanupNames.GetHelperPath(nonce);
        var reaperPath = CleanupNames.GetReaperPath(nonce);
        try
        {
            File.Copy(helperPath, reaperPath, overwrite: false);
            var helperHash = await ComputeHashAsync(helperPath, cancellationToken).ConfigureAwait(false);
            var reaperHash = await ComputeHashAsync(reaperPath, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(helperHash, reaperHash))
            {
                return Result.Failure<Process>(new AppError(
                    ErrorCode.Security,
                    "The cleanup reaper failed its SHA-256 integrity check."));
            }

            using var currentProcess = Process.GetCurrentProcess();
            var startInfo = new ProcessStartInfo
            {
                FileName = reaperPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            startInfo.ArgumentList.Add(ReaperCommand.Switch);
            startInfo.ArgumentList.Add(currentProcess.Id.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(
                currentProcess.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(nonce.ToString("N"));

            var reaper = Process.Start(startInfo);
            return reaper is null
                ? Result.Failure<Process>(new AppError(ErrorCode.Installation, "The cleanup reaper could not be started."))
                : Result.Success(reaper);
        }
        catch (Exception exception) when (IsCleanupException(exception))
        {
            return Result.Failure<Process>(new AppError(
                ErrorCode.Installation,
                $"The cleanup reaper could not be prepared: {exception.Message}",
                exception));
        }
    }

    private static async Task<CleanupHandshake> WaitForCommitAsync(
        EventWaitHandle commit,
        EventWaitHandle abort,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                    () => WaitHandle.WaitAny([commit, abort], CommitTimeout) switch
                    {
                        0 => CleanupHandshake.Commit,
                        1 => CleanupHandshake.Abort,
                        _ => CleanupHandshake.Timeout
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CleanupHandshake.Abort;
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> DeleteWithRetriesAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < DeleteAttempts; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return true;
                }

                File.Delete(path);
                if (!File.Exists(path))
                {
                    return true;
                }
            }
            catch (Exception exception) when (IsCleanupException(exception))
            {
            }

            if (attempt + 1 < DeleteAttempts)
            {
                await Task.Delay(DeleteDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return !File.Exists(path);
    }

    private static bool TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return true;
            }

            if (HasReparsePoint(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return false;
            }

            Directory.Delete(directory, recursive: false);
            return !Directory.Exists(directory);
        }
        catch (Exception exception) when (IsCleanupException(exception))
        {
            return false;
        }
    }

    private static bool ScheduleDeleteOnReboot(string path) =>
        File.Exists(path) || Directory.Exists(path)
            ? MoveFileEx(path, null, MoveFileDelayUntilReboot)
            : true;

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

    private static void WriteResult(Guid nonce, string status, string detail)
    {
        try
        {
            var content = string.Join(
                Environment.NewLine,
                $"status={status}",
                $"utc={DateTimeOffset.UtcNow:O}",
                $"detail={detail.Replace(Environment.NewLine, " ", StringComparison.Ordinal)}");
            File.WriteAllText(CleanupNames.GetResultPath(nonce), content);
        }
        catch (Exception exception) when (IsCleanupException(exception))
        {
        }
    }

    private static async Task<byte[]> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsProcessException(Exception exception) => exception is
        ArgumentException or
        InvalidOperationException or
        NotSupportedException or
        System.ComponentModel.Win32Exception;

    private static bool IsCleanupException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        ArgumentException or
        InvalidOperationException or
        NotSupportedException or
        CryptographicException or
        System.ComponentModel.Win32Exception;

    private const uint MoveFileDelayUntilReboot = 0x00000004;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    private sealed record ValidatedCleanupRequest(InstallationPaths Paths, Process ParentProcess);

    private enum CleanupHandshake
    {
        Commit,
        Abort,
        Timeout
    }
}

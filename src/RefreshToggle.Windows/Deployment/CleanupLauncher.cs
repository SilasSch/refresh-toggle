using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using RefreshToggle.Core;

namespace RefreshToggle.Windows.Deployment;

internal sealed class CleanupLaunchSession : IDisposable
{
    private readonly EventWaitHandle _commit;
    private readonly EventWaitHandle _abort;
    private readonly Process _helperProcess;
    private bool _committed;
    private bool _disposed;

    internal CleanupLaunchSession(
        Guid nonce,
        EventWaitHandle commit,
        EventWaitHandle abort,
        Process helperProcess)
    {
        Nonce = nonce;
        _commit = commit;
        _abort = abort;
        _helperProcess = helperProcess;
    }

    public Guid Nonce { get; }

    public Result Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_helperProcess.HasExited)
        {
            return Result.Failure(new AppError(
                ErrorCode.Installation,
                $"The cleanup helper exited before commit (code {_helperProcess.ExitCode})."));
        }

        if (!_commit.Set())
        {
            return Result.Failure(new AppError(
                ErrorCode.Installation,
                "The cleanup helper could not be committed."));
        }

        _committed = true;
        return Result.Success();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_committed)
        {
            try
            {
                _abort.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _helperProcess.Dispose();
        _commit.Dispose();
        _abort.Dispose();
        _disposed = true;
    }
}

internal sealed class CleanupLauncher
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(20);
    private readonly TimeProvider _timeProvider;

    public CleanupLauncher(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<CleanupLaunchSession>> StartAsync(
        string sourceExecutablePath,
        CancellationToken cancellationToken = default)
    {
        var nonce = Guid.NewGuid();
        var helperPath = CleanupNames.GetHelperPath(nonce);
        EventWaitHandle? helperReady = null;
        EventWaitHandle? reaperReady = null;
        EventWaitHandle? commit = null;
        EventWaitHandle? abort = null;
        Process? helperProcess = null;

        try
        {
            var sourceValidation = ValidateSource(sourceExecutablePath);
            if (sourceValidation.IsFailure)
            {
                return Result.Failure<CleanupLaunchSession>(sourceValidation.Error!);
            }

            helperReady = CreateFreshEvent(CleanupNames.HelperReadyEvent(nonce));
            reaperReady = CreateFreshEvent(CleanupNames.ReaperReadyEvent(nonce));
            commit = CreateFreshEvent(CleanupNames.CommitEvent(nonce));
            abort = CreateFreshEvent(CleanupNames.AbortEvent(nonce));

            File.Copy(sourceExecutablePath, helperPath, overwrite: false);
            var sourceHash = await ComputeHashAsync(sourceExecutablePath, cancellationToken).ConfigureAwait(false);
            var helperHash = await ComputeHashAsync(helperPath, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, helperHash))
            {
                return Result.Failure<CleanupLaunchSession>(new AppError(
                    ErrorCode.Installation,
                    "The cleanup helper failed its SHA-256 integrity check."));
            }

            using var currentProcess = Process.GetCurrentProcess();
            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            startInfo.ArgumentList.Add(CleanupCommand.Switch);
            startInfo.ArgumentList.Add(currentProcess.Id.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(
                currentProcess.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(nonce.ToString("N"));

            helperProcess = Process.Start(startInfo);
            if (helperProcess is null)
            {
                return Result.Failure<CleanupLaunchSession>(new AppError(
                    ErrorCode.Installation,
                    "The cleanup helper process could not be started."));
            }

            var deadline = _timeProvider.GetUtcNow() + ReadyTimeout;
            if (!await WaitUntilAsync(helperReady, deadline, cancellationToken).ConfigureAwait(false) ||
                !await WaitUntilAsync(reaperReady, deadline, cancellationToken).ConfigureAwait(false))
            {
                abort.Set();
                var detail = helperProcess.HasExited
                    ? $" It exited with code {helperProcess.ExitCode}."
                    : string.Empty;
                return Result.Failure<CleanupLaunchSession>(new AppError(
                    ErrorCode.Installation,
                    "Cleanup helper readiness timed out." + detail));
            }

            helperReady.Dispose();
            helperReady = null;
            reaperReady.Dispose();
            reaperReady = null;

            var session = new CleanupLaunchSession(nonce, commit, abort, helperProcess);
            commit = null;
            abort = null;
            helperProcess = null;
            return Result.Success(session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<CleanupLaunchSession>(AppError.Cancelled());
        }
        catch (Exception exception) when (IsCleanupException(exception))
        {
            return Result.Failure<CleanupLaunchSession>(new AppError(
                ErrorCode.Installation,
                $"The cleanup helper could not be prepared: {exception.Message}",
                exception));
        }
        finally
        {
            if (helperProcess is not null)
            {
                try
                {
                    if (!helperProcess.HasExited)
                    {
                        abort?.Set();
                    }
                }
                catch (Exception exception) when (IsCleanupException(exception))
                {
                }

                helperProcess.Dispose();
            }

            helperReady?.Dispose();
            reaperReady?.Dispose();
            commit?.Dispose();
            abort?.Dispose();
            TryDeleteFile(helperPath);
        }
    }

    private static Result ValidateSource(string sourceExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(sourceExecutablePath) ||
            !Path.IsPathFullyQualified(sourceExecutablePath) ||
            !File.Exists(sourceExecutablePath))
        {
            return Result.Failure(new AppError(
                ErrorCode.NotFound,
                "The current executable could not be located for cleanup."));
        }

        if (!string.Equals(Path.GetExtension(sourceExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(sourceExecutablePath) & FileAttributes.ReparsePoint) != 0)
        {
            return Result.Failure(new AppError(
                ErrorCode.Security,
                "The cleanup source is not a regular Windows executable."));
        }

        return Result.Success();
    }

    private static EventWaitHandle CreateFreshEvent(string name)
    {
        var waitHandle = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            name,
            out var createdNew);
        if (createdNew)
        {
            return waitHandle;
        }

        waitHandle.Dispose();
        throw new IOException($"A cleanup synchronization object already exists: '{name}'.");
    }

    private async Task<bool> WaitUntilAsync(
        EventWaitHandle waitHandle,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var remaining = deadline - _timeProvider.GetUtcNow();
        return remaining > TimeSpan.Zero &&
               await CleanupWaitHandle.WaitOneAsync(waitHandle, remaining, cancellationToken)
                   .ConfigureAwait(false);
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsCleanupException(exception))
        {
        }
    }

    private static bool IsCleanupException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        ArgumentException or
        InvalidOperationException or
        NotSupportedException or
        CryptographicException;
}

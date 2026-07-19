using System.Security.Cryptography;
using RefreshToggle.Core;

namespace RefreshToggle.Windows.Deployment;

internal sealed record AtomicInstallOutcome(bool InstalledNow, bool AlreadyRunningInstalledCopy);

internal sealed class AtomicInstaller
{
    private const int CopyBufferSize = 1024 * 128;

    private readonly InstallationPaths _paths;
    private readonly Func<InstallationPaths> _canonicalPathsFactory;

    public AtomicInstaller(
        InstallationPaths paths,
        Func<InstallationPaths>? canonicalPathsFactory = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _canonicalPathsFactory = canonicalPathsFactory ?? InstallationPaths.ForCurrentUser;
    }

    public async Task<Result<AtomicInstallOutcome>> EnsureInstalledAsync(
        string sourceExecutablePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Failure<AtomicInstallOutcome>(new AppError(
                ErrorCode.Unavailable,
                "Installation is only supported on Windows."));
        }

        var validation = ValidatePaths(sourceExecutablePath);
        if (validation.IsFailure)
        {
            return Result.Failure<AtomicInstallOutcome>(validation.Error!);
        }

        var sourcePath = Path.GetFullPath(sourceExecutablePath);
        if (PathsEqual(sourcePath, _paths.InstalledExecutablePath))
        {
            return Result.Success(new AtomicInstallOutcome(
                InstalledNow: false,
                AlreadyRunningInstalledCopy: true));
        }

        var tempPath = Path.Combine(
            _paths.InstallDirectory,
            $".RefreshToggle.install-{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(
            _paths.InstallDirectory,
            $".RefreshToggle.backup-{Guid.NewGuid():N}.tmp");
        var replacementCommitted = false;

        try
        {
            Directory.CreateDirectory(_paths.InstallDirectory);
            if (HasReparsePoint(_paths.InstallDirectory))
            {
                return Result.Failure<AtomicInstallOutcome>(new AppError(
                    ErrorCode.Security,
                    "The installation directory is a reparse point and cannot be used safely."));
            }

            byte[] sourceHash;
            await using (var source = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[CopyBufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
                sourceHash = hasher.GetHashAndReset();
            }

            var copiedHash = await ComputeHashAsync(tempPath, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, copiedHash))
            {
                return Result.Failure<AtomicInstallOutcome>(new AppError(
                    ErrorCode.Installation,
                    "The temporary installation copy failed its SHA-256 integrity check."));
            }

            if (File.Exists(_paths.InstalledExecutablePath))
            {
                if (HasReparsePoint(_paths.InstalledExecutablePath))
                {
                    return Result.Failure<AtomicInstallOutcome>(new AppError(
                        ErrorCode.Security,
                        "The installed executable is a reparse point and cannot be replaced safely."));
                }

                File.Replace(tempPath, _paths.InstalledExecutablePath, backupPath, ignoreMetadataErrors: true);
                replacementCommitted = true;
            }
            else
            {
                File.Move(tempPath, _paths.InstalledExecutablePath, overwrite: false);
                replacementCommitted = true;
            }

            var installedHash = await ComputeHashAsync(
                    _paths.InstalledExecutablePath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, installedHash))
            {
                var rollback = TryRestoreBackup(backupPath);
                return Result.Failure<AtomicInstallOutcome>(AppError.Combine(
                    new AppError(
                        ErrorCode.Installation,
                        "The installed executable failed its post-commit SHA-256 verification."),
                    rollback));
            }

            TryDeleteFile(backupPath);
            return Result.Success(new AtomicInstallOutcome(
                InstalledNow: true,
                AlreadyRunningInstalledCopy: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (replacementCommitted)
            {
                TryRestoreBackup(backupPath);
            }

            return Result.Failure<AtomicInstallOutcome>(AppError.Cancelled());
        }
        catch (Exception exception) when (IsInstallationException(exception))
        {
            var rollback = replacementCommitted ? TryRestoreBackup(backupPath) : null;
            return Result.Failure<AtomicInstallOutcome>(AppError.Combine(
                new AppError(
                    ErrorCode.Installation,
                    $"RefreshToggle could not be installed atomically: {exception.Message}",
                    exception),
                rollback));
        }
        finally
        {
            TryDeleteFile(tempPath);
            if (!replacementCommitted)
            {
                TryDeleteFile(backupPath);
            }
        }
    }

    private Result ValidatePaths(string sourceExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(sourceExecutablePath) || !Path.IsPathFullyQualified(sourceExecutablePath))
        {
            return Result.Failure(AppError.Validation("A fully-qualified source executable path is required."));
        }

        if (!File.Exists(sourceExecutablePath))
        {
            return Result.Failure(new AppError(
                ErrorCode.NotFound,
                $"The source executable was not found: '{sourceExecutablePath}'."));
        }

        if (!string.Equals(Path.GetExtension(sourceExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(AppError.Validation("Only a Windows .exe can be installed."));
        }

        if (HasReparsePoint(sourceExecutablePath))
        {
            return Result.Failure(new AppError(
                ErrorCode.Security,
                "The source executable is a reparse point and cannot be installed safely."));
        }

        var expected = _canonicalPathsFactory();
        if (!PathsEqual(expected.InstallDirectory, _paths.InstallDirectory) ||
            !PathsEqual(expected.InstalledExecutablePath, _paths.InstalledExecutablePath))
        {
            return Result.Failure(new AppError(
                ErrorCode.Security,
                "The installation target is not the canonical per-user RefreshToggle directory."));
        }

        return Result.Success();
    }

    private AppError? TryRestoreBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            return null;
        }

        try
        {
            if (File.Exists(_paths.InstalledExecutablePath))
            {
                File.Replace(backupPath, _paths.InstalledExecutablePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(backupPath, _paths.InstalledExecutablePath, overwrite: false);
            }

            return null;
        }
        catch (Exception exception) when (IsInstallationException(exception))
        {
            return new AppError(
                ErrorCode.Installation,
                $"The previous installed executable could not be restored: {exception.Message}",
                exception);
        }
    }

    private static async Task<byte[]> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsInstallationException(exception))
        {
            // A unique temp/backup file is safer left behind than deleting a less
            // constrained path during error handling.
        }
    }

    private static bool IsInstallationException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        ArgumentException or
        NotSupportedException or
        CryptographicException;
}

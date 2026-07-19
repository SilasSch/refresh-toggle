using System.Text.Json;
using System.Text.Json.Serialization;
using RefreshToggle.Core;

namespace RefreshToggle.Windows.Configuration;

internal sealed record ConfigFileLoadOutcome(
    AppConfig Config,
    bool WasRecovered,
    bool WasMigrated,
    string? Warning,
    string? CorruptBackupPath);

/// <summary>
/// Durable JSON persistence used by the application-facing config adapter. The
/// public engine is intentionally path-injectable so all failure modes can be
/// exercised without touching the real user profile.
/// </summary>
internal sealed class ConfigFileStoreEngine : IAsyncDisposable
{
    private const string ConfigFileName = "config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly string _configPath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public ConfigFileStoreEngine(string? configPath = null, TimeProvider? timeProvider = null)
    {
        _configPath = Path.GetFullPath(configPath ?? GetDefaultConfigPath());
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string ConfigPath => _configPath;

    public async Task<Result<ConfigFileLoadOutcome>> LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_configPath))
            {
                var createResult = await SaveCoreAsync(AppConfig.Default, cancellationToken).ConfigureAwait(false);
                var creationWarning = createResult.IsFailure
                    ? $"Default configuration is being used but could not be persisted: {createResult.Error!.Message}"
                    : null;

                return Result.Success(new ConfigFileLoadOutcome(
                    AppConfig.Default,
                    WasRecovered: false,
                    WasMigrated: false,
                    creationWarning,
                    CorruptBackupPath: null));
            }

            PersistedConfig? persisted;
            try
            {
                await using var stream = new FileStream(
                    _configPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                persisted = await JsonSerializer.DeserializeAsync<PersistedConfig>(
                        stream,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableFileException(exception))
            {
                return await RecoverFromInvalidConfigAsync(
                        $"Configuration could not be read: {exception.Message}",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (persisted is null)
            {
                return await RecoverFromInvalidConfigAsync(
                        "Configuration contained JSON null instead of an object.",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var conversion = persisted.ToCurrentConfig();
            if (conversion.IsFailure)
            {
                return await RecoverFromInvalidConfigAsync(
                        conversion.Error!.Message,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var config = conversion.Value;
            var wasMigrated = persisted.SchemaVersion is null or 0 || persisted.StartWithWindows.HasValue;
            string? warning = null;
            if (wasMigrated)
            {
                var migrationSave = await SaveCoreAsync(config, cancellationToken).ConfigureAwait(false);
                if (migrationSave.IsFailure)
                {
                    warning = $"Configuration was migrated in memory but could not be persisted: " +
                              migrationSave.Error!.Message;
                }
            }

            return Result.Success(new ConfigFileLoadOutcome(
                config,
                WasRecovered: false,
                wasMigrated,
                warning,
                CorruptBackupPath: null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<ConfigFileLoadOutcome>(AppError.Cancelled());
        }
        catch (Exception exception) when (IsRecoverableFileException(exception))
        {
            return Result.Failure<ConfigFileLoadOutcome>(new AppError(
                ErrorCode.Persistence,
                $"Configuration could not be loaded from '{_configPath}': {exception.Message}",
                exception));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var validation = config.Validate();
        if (validation.IsFailure)
        {
            return validation;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SaveCoreAsync(config, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(AppError.Cancelled());
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Result<ConfigFileLoadOutcome>> RecoverFromInvalidConfigAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        string? backupPath = null;
        string? backupWarning = null;

        if (File.Exists(_configPath))
        {
            backupPath = CreateCorruptBackupPath();
            try
            {
                File.Move(_configPath, backupPath, overwrite: false);
            }
            catch (Exception exception) when (IsRecoverableFileException(exception))
            {
                backupPath = null;
                backupWarning = $" The corrupt file could not be backed up: {exception.Message}";
            }
        }

        var saveResult = await SaveCoreAsync(AppConfig.Default, cancellationToken).ConfigureAwait(false);
        var saveWarning = saveResult.IsFailure
            ? $" Default configuration could not be persisted: {saveResult.Error!.Message}"
            : string.Empty;

        return Result.Success(new ConfigFileLoadOutcome(
            AppConfig.Default,
            WasRecovered: true,
            WasMigrated: false,
            $"Configuration was reset: {reason}{backupWarning}{saveWarning}",
            backupPath));
    }

    private async Task<Result> SaveCoreAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Result.Failure(new AppError(
                ErrorCode.Persistence,
                $"Configuration path has no parent directory: '{_configPath}'."));
        }

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_configPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);

            var persisted = PersistedConfig.FromCurrent(config);
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        persisted,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_configPath))
            {
                File.Move(tempPath, _configPath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, _configPath, overwrite: false);
            }

            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(AppError.Cancelled());
        }
        catch (Exception exception) when (IsRecoverableFileException(exception))
        {
            return Result.Failure(new AppError(
                ErrorCode.Persistence,
                $"Configuration could not be saved to '{_configPath}': {exception.Message}",
                exception));
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private string CreateCorruptBackupPath()
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        var baseName = Path.GetFileNameWithoutExtension(_configPath);
        var extension = Path.GetExtension(_configPath);
        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMddTHHmmssfffffffZ", System.Globalization.CultureInfo.InvariantCulture);

        for (var suffix = 0; ; suffix++)
        {
            var discriminator = suffix == 0 ? string.Empty : $"-{suffix}";
            var candidate = Path.Combine(
                directory,
                $"{baseName}.corrupt-{timestamp}{discriminator}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception exception) when (IsRecoverableFileException(exception))
        {
            // Best effort: a unique temp file can safely be ignored and cleaned up
            // by normal profile maintenance if another process still holds it.
        }
    }

    private static bool IsRecoverableFileException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        JsonException or
        NotSupportedException or
        ArgumentException;

    private static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            throw new InvalidOperationException("The ApplicationData directory is unavailable.");
        }

        return Path.Combine(appData, "RefreshToggle", ConfigFileName);
    }

    private sealed record PersistedConfig
    {
        public int? SchemaVersion { get; init; }

        public int? RateA { get; init; }

        public int? RateB { get; init; }

        public string? HotkeyModifiers { get; init; }

        public string? HotkeyKey { get; init; }

        // Read-only migration shim. It is intentionally never emitted by FromCurrent.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? StartWithWindows { get; init; }

        public Result<AppConfig> ToCurrentConfig()
        {
            var schema = SchemaVersion.GetValueOrDefault();
            if (schema < 0 || schema > AppConfig.CurrentSchemaVersion)
            {
                return Result.Failure<AppConfig>(AppError.Validation(
                    $"Unsupported config schema version: {SchemaVersion}."));
            }

            var config = new AppConfig
            {
                SchemaVersion = AppConfig.CurrentSchemaVersion,
                RateA = RateA ?? AppConfig.Default.RateA,
                RateB = RateB ?? AppConfig.Default.RateB,
                HotkeyModifiers = string.IsNullOrWhiteSpace(HotkeyModifiers)
                    ? AppConfig.Default.HotkeyModifiers
                    : HotkeyModifiers,
                HotkeyKey = string.IsNullOrWhiteSpace(HotkeyKey)
                    ? AppConfig.Default.HotkeyKey
                    : HotkeyKey
            };

            var validation = config.Validate();
            return validation.IsSuccess
                ? Result.Success(config)
                : Result.Failure<AppConfig>(validation.Error!);
        }

        public static PersistedConfig FromCurrent(AppConfig config) => new()
        {
            SchemaVersion = AppConfig.CurrentSchemaVersion,
            RateA = config.RateA,
            RateB = config.RateB,
            HotkeyModifiers = config.HotkeyModifiers,
            HotkeyKey = config.HotkeyKey,
            StartWithWindows = null
        };
    }
}

using RefreshToggle.Application;
using RefreshToggle.Core;

namespace RefreshToggle.Windows.Configuration;

internal sealed class JsonConfigStore : IConfigStore, IAsyncDisposable
{
    private readonly ConfigFileStoreEngine _engine;
    private readonly bool _ownsEngine;

    public JsonConfigStore()
        : this(new ConfigFileStoreEngine(), ownsEngine: true)
    {
    }

    public JsonConfigStore(string configPath, TimeProvider? timeProvider = null)
        : this(new ConfigFileStoreEngine(configPath, timeProvider), ownsEngine: true)
    {
    }

    public JsonConfigStore(ConfigFileStoreEngine engine)
        : this(engine, ownsEngine: false)
    {
    }

    private JsonConfigStore(ConfigFileStoreEngine engine, bool ownsEngine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _ownsEngine = ownsEngine;
    }

    public async Task<Result<ConfigLoadResult>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _engine.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return Result.Failure<ConfigLoadResult>(loaded.Error!);
        }

        var outcome = loaded.Value;
        var warning = string.IsNullOrWhiteSpace(outcome.Warning)
            ? null
            : new AppError(ErrorCode.Persistence, outcome.Warning);

        return Result.Success(new ConfigLoadResult(
            outcome.Config,
            WasReset: outcome.WasRecovered,
            NeedsMigration: outcome.WasMigrated,
            warning));
    }

    public Task<Result> SaveAsync(AppConfig config, CancellationToken cancellationToken = default) =>
        _engine.SaveAsync(config, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_ownsEngine)
        {
            await _engine.DisposeAsync().ConfigureAwait(false);
        }
    }
}

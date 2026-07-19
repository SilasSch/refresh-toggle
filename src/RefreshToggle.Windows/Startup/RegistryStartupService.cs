using RefreshToggle.Application;
using RefreshToggle.Core;
using RefreshToggle.Windows.Deployment;

namespace RefreshToggle.Windows.Startup;

internal sealed class RegistryStartupService : IStartupService
{
    private readonly RegistryStartupStoreEngine _engine;
    private readonly DeploymentManifest _deployment;

    public RegistryStartupService(RegistryStartupStoreEngine engine, DeploymentManifest deployment)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(deployment);
        _engine = engine;
        _deployment = deployment;
    }

    public Task<Result<StartupState>> GetStateAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result.Failure<StartupState>(AppError.Cancelled()));
        }

        if (!_deployment.MayMutateInstallation)
        {
            return Task.FromResult(Result.Success(new StartupState(IsEnabled: false)));
        }

        var state = _engine.GetState();
        return Task.FromResult(state.IsSuccess
            ? Result.Success(new StartupState(state.Value.IsEnabled))
            : Result.Failure<StartupState>(state.Error!));
    }

    public Task<Result<StartupState>> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result.Failure<StartupState>(AppError.Cancelled()));
        }

        var mutation = enabled ? _engine.Enable() : _engine.Disable();
        if (mutation.IsFailure)
        {
            return Task.FromResult(Result.Failure<StartupState>(mutation.Error!));
        }

        var readBack = _engine.GetState();
        if (readBack.IsFailure)
        {
            return Task.FromResult(Result.Failure<StartupState>(readBack.Error!));
        }

        if (readBack.Value.IsEnabled != enabled)
        {
            return Task.FromResult(Result.Failure<StartupState>(new AppError(
                ErrorCode.Persistence,
                "The startup registry change could not be verified.")));
        }

        return Task.FromResult(Result.Success(new StartupState(enabled)));
    }
}

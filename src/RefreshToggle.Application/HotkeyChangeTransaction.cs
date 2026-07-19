using RefreshToggle.Core;

namespace RefreshToggle.Application;

internal sealed record HotkeyActivation(AppConfig Config, HotkeyRegistration Registration);

internal sealed partial class HotkeyChangeTransaction
{
    private readonly IConfigStore _configStore;
    private readonly IHotkeyService _hotkeyService;

    public HotkeyChangeTransaction(IConfigStore configStore, IHotkeyService hotkeyService)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
    }

    public async Task<Result<HotkeyActivation>> RegisterInitialAsync(
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var binding = config.GetHotkeyBinding();
        if (binding.IsFailure)
        {
            return Result.Failure<HotkeyActivation>(binding.Error!);
        }

        var registration = await _hotkeyService
            .RegisterAsync(binding.Value, cancellationToken)
            .ConfigureAwait(false);

        return registration.IsSuccess
            ? Result.Success(new HotkeyActivation(config, registration.Value))
            : Result.Failure<HotkeyActivation>(registration.Error!);
    }

    public async Task<Result<HotkeyActivation>> ChangeAsync(
        HotkeyActivation current,
        HotkeyBinding proposedBinding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.Registration.Binding == proposedBinding)
        {
            return Result.Success(current);
        }

        var newRegistration = await _hotkeyService
            .RegisterAsync(proposedBinding, cancellationToken)
            .ConfigureAwait(false);
        if (newRegistration.IsFailure)
        {
            return Result.Failure<HotkeyActivation>(newRegistration.Error!);
        }

        var proposedConfig = current.Config.WithHotkey(proposedBinding);
        var saveNewConfig = await _configStore
            .SaveAsync(proposedConfig, cancellationToken)
            .ConfigureAwait(false);
        if (saveNewConfig.IsFailure)
        {
            var unregisterNew = await _hotkeyService
                .UnregisterAsync(newRegistration.Value, CancellationToken.None)
                .ConfigureAwait(false);
            return Result.Failure<HotkeyActivation>(
                AppError.Combine(saveNewConfig.Error!, unregisterNew.Error));
        }

        var unregisterOld = await _hotkeyService
            .UnregisterAsync(current.Registration, cancellationToken)
            .ConfigureAwait(false);
        if (unregisterOld.IsFailure)
        {
            var unregisterNew = await _hotkeyService
                .UnregisterAsync(newRegistration.Value, CancellationToken.None)
                .ConfigureAwait(false);
            var restoreConfig = await _configStore
                .SaveAsync(current.Config, CancellationToken.None)
                .ConfigureAwait(false);

            return Result.Failure<HotkeyActivation>(
                AppError.Combine(unregisterOld.Error!, unregisterNew.Error, restoreConfig.Error));
        }

        return Result.Success(
            new HotkeyActivation(proposedConfig, newRegistration.Value));
    }
}

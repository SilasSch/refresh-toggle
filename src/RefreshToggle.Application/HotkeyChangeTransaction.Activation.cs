using RefreshToggle.Core;

namespace RefreshToggle.Application;

internal sealed partial class HotkeyChangeTransaction
{
    public async Task<Result<HotkeyActivation>> RegisterAndSaveAsync(
        AppConfig currentConfig,
        HotkeyBinding proposedBinding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);

        var registration = await _hotkeyService
            .RegisterAsync(proposedBinding, cancellationToken)
            .ConfigureAwait(false);
        if (registration.IsFailure)
        {
            return Result.Failure<HotkeyActivation>(registration.Error!);
        }

        var proposedConfig = currentConfig.WithHotkey(proposedBinding);
        var saveConfig = await _configStore
            .SaveAsync(proposedConfig, cancellationToken)
            .ConfigureAwait(false);
        if (saveConfig.IsFailure)
        {
            var unregister = await _hotkeyService
                .UnregisterAsync(registration.Value, CancellationToken.None)
                .ConfigureAwait(false);
            return Result.Failure<HotkeyActivation>(
                AppError.Combine(saveConfig.Error!, unregister.Error));
        }

        return Result.Success(
            new HotkeyActivation(proposedConfig, registration.Value));
    }
}

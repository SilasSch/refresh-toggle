using RefreshToggle.Application;
using RefreshToggle.Core;

namespace RefreshToggle.Tests;

public sealed class HotkeyChangeTransactionTests
{
    private static readonly AppError RegisterError = new(ErrorCode.Registration, "register failed");
    private static readonly AppError SaveError = new(ErrorCode.Persistence, "save failed");
    private static readonly AppError UnregisterError = new(ErrorCode.Registration, "unregister failed");

    [Fact]
    public async Task InitialRegistrationUsesPersistedBinding()
    {
        var configStore = new FakeConfigStore();
        var hotkeys = new FakeHotkeyService();
        var transaction = new HotkeyChangeTransaction(configStore, hotkeys);

        var result = await transaction.RegisterInitialAsync(AppConfig.Default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HotkeyBinding.Default, result.Value.Registration.Binding);
        Assert.Single(hotkeys.ActiveRegistrations);
        Assert.Empty(configStore.SavedConfigs);
    }

    [Fact]
    public async Task InitialRegistrationRejectsInvalidConfigWithoutNativeCall()
    {
        var configStore = new FakeConfigStore();
        var hotkeys = new FakeHotkeyService();
        var transaction = new HotkeyChangeTransaction(configStore, hotkeys);
        var invalid = AppConfig.Default with { HotkeyKey = "82" };

        var result = await transaction.RegisterInitialAsync(invalid);

        Assert.True(result.IsFailure);
        Assert.Empty(hotkeys.Operations);
    }

    [Fact]
    public async Task ChangeRegistersSavesThenUnregistersOld()
    {
        var fixture = await CreateFixtureAsync();
        var proposed = HotkeyBinding.Parse("Alt", "F12").Value;

        var result = await fixture.Transaction.ChangeAsync(fixture.Current, proposed);

        Assert.True(result.IsSuccess);
        Assert.Equal(proposed, result.Value.Registration.Binding);
        Assert.Equal(proposed, result.Value.Config.GetHotkeyBinding().Value);
        Assert.Equal(
            ["register:Ctrl+Shift+R", "register:Alt+F12", "unregister:Ctrl+Shift+R"],
            fixture.Hotkeys.Operations);
        Assert.Single(fixture.Hotkeys.ActiveRegistrations);
        Assert.Equal(proposed, fixture.Hotkeys.ActiveRegistrations[0].Binding);
        Assert.Single(fixture.ConfigStore.SavedConfigs);
    }

    [Fact]
    public async Task RegisterFailureLeavesOldRegistrationAndConfigUntouched()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Hotkeys.RegisterResults.Enqueue(Result.Failure<HotkeyRegistration>(RegisterError));

        var result = await fixture.Transaction.ChangeAsync(
            fixture.Current,
            HotkeyBinding.Parse("Alt", "F12").Value);

        Assert.True(result.IsFailure);
        Assert.Equal(RegisterError, result.Error);
        Assert.Single(fixture.Hotkeys.ActiveRegistrations);
        Assert.Equal(fixture.Current.Registration, fixture.Hotkeys.ActiveRegistrations[0]);
        Assert.Empty(fixture.ConfigStore.SavedConfigs);
    }

    [Fact]
    public async Task SaveFailureUnregistersNewAndKeepsOldActive()
    {
        var fixture = await CreateFixtureAsync();
        fixture.ConfigStore.SaveResults.Enqueue(Result.Failure(SaveError));
        var proposed = HotkeyBinding.Parse("Alt", "F12").Value;

        var result = await fixture.Transaction.ChangeAsync(fixture.Current, proposed);

        Assert.True(result.IsFailure);
        Assert.Equal(SaveError, result.Error);
        Assert.Single(fixture.Hotkeys.ActiveRegistrations);
        Assert.Equal(fixture.Current.Registration, fixture.Hotkeys.ActiveRegistrations[0]);
        Assert.Equal("unregister:Alt+F12", fixture.Hotkeys.Operations[^1]);
    }

    [Fact]
    public async Task OldUnregisterFailureRollsBackNewRegistrationAndPersistedConfig()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Hotkeys.UnregisterResults.Enqueue(Result.Failure(UnregisterError));
        fixture.Hotkeys.UnregisterResults.Enqueue(Result.Success());
        fixture.ConfigStore.SaveResults.Enqueue(Result.Success());
        fixture.ConfigStore.SaveResults.Enqueue(Result.Success());

        var result = await fixture.Transaction.ChangeAsync(
            fixture.Current,
            HotkeyBinding.Parse("Win", "T").Value);

        Assert.True(result.IsFailure);
        Assert.Equal(UnregisterError.Code, result.Error!.Code);
        Assert.Single(fixture.Hotkeys.ActiveRegistrations);
        Assert.Equal(fixture.Current.Registration, fixture.Hotkeys.ActiveRegistrations[0]);
        Assert.Equal(2, fixture.ConfigStore.SavedConfigs.Count);
        Assert.Equal(fixture.Current.Config, fixture.ConfigStore.SavedConfigs[^1]);
    }

    [Fact]
    public async Task RollbackFailuresAreReturnedWithPrimaryFailure()
    {
        var fixture = await CreateFixtureAsync();
        fixture.ConfigStore.SaveResults.Enqueue(Result.Failure(SaveError));
        fixture.Hotkeys.UnregisterResults.Enqueue(Result.Failure(UnregisterError));

        var result = await fixture.Transaction.ChangeAsync(
            fixture.Current,
            HotkeyBinding.Parse("Alt", "B").Value);

        Assert.Equal(SaveError.Code, result.Error!.Code);
        Assert.Contains(UnregisterError.Message, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdenticalBindingIsNoOp()
    {
        var fixture = await CreateFixtureAsync();
        var operationsBefore = fixture.Hotkeys.Operations.Count;

        var result = await fixture.Transaction.ChangeAsync(
            fixture.Current,
            fixture.Current.Registration.Binding);

        Assert.True(result.IsSuccess);
        Assert.Same(fixture.Current, result.Value);
        Assert.Equal(operationsBefore, fixture.Hotkeys.Operations.Count);
        Assert.Empty(fixture.ConfigStore.SavedConfigs);
    }

    [Fact]
    public async Task RegisterAndSaveRollsBackRegistrationWhenSaveFails()
    {
        var configStore = new FakeConfigStore();
        configStore.SaveResults.Enqueue(Result.Failure(SaveError));
        var hotkeys = new FakeHotkeyService();
        var transaction = new HotkeyChangeTransaction(configStore, hotkeys);

        var result = await transaction.RegisterAndSaveAsync(
            AppConfig.Default,
            HotkeyBinding.Parse("Alt", "F1").Value);

        Assert.True(result.IsFailure);
        Assert.Empty(hotkeys.ActiveRegistrations);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var configStore = new FakeConfigStore();
        var hotkeys = new FakeHotkeyService();
        var transaction = new HotkeyChangeTransaction(configStore, hotkeys);
        var current = await transaction.RegisterInitialAsync(AppConfig.Default);
        Assert.True(current.IsSuccess);
        return new Fixture(configStore, hotkeys, transaction, current.Value);
    }

    private sealed record Fixture(
        FakeConfigStore ConfigStore,
        FakeHotkeyService Hotkeys,
        HotkeyChangeTransaction Transaction,
        HotkeyActivation Current);
}

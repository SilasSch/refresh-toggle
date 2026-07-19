using Microsoft.Win32;
using RefreshToggle.Application;
using RefreshToggle.Core;

namespace RefreshToggle.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly RefreshToggleController _controller;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Control _dispatcher = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem = new("Current: Unknown") { Enabled = false };
    private readonly ToolStripSeparator _displayStart = new();
    private readonly ToolStripSeparator _displayEnd = new();
    private readonly ToolStripMenuItem _rateAItem = new();
    private readonly ToolStripMenuItem _rateBItem = new();
    private readonly ToolStripMenuItem _hotkeyItem = new("Hotkey: …");
    private readonly ToolStripMenuItem _startupItem = new("Start with Windows");
    private readonly ToolStripMenuItem _uninstallItem = new("Uninstall");
    private readonly ToolStripMenuItem _exitItem = new("Exit");
    private readonly List<ToolStripMenuItem> _displayItems = [];
    private AppState _state = new();
    private Icon? _currentIcon;
    private string? _iconKey;
    private string? _shownError;
    private string? _shownNotification;
    private DateTimeOffset _renderedSnapshotAt = DateTimeOffset.MinValue;
    private int _renderedRateA;
    private int _renderedRateB;
    private bool _displayEventsSubscribed;
    private bool _exitRequested;
    private int _shutdownStarted;

    public TrayApplicationContext(RefreshToggleController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dispatcher.CreateControl();

        BuildMenu();
        _currentIcon = TrayIconRenderer.CreateUnknown();
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _currentIcon,
            Text = "RefreshToggle",
            Visible = true
        };

        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
        _controller.StateChanged += OnStateChanged;
        System.Windows.Forms.Application.Idle += InitializeOnFirstIdle;
    }

    private void BuildMenu()
    {
        _rateAItem.Click += async (_, _) => await ConfigureRateAsync(configureRateA: true);
        _rateBItem.Click += async (_, _) => await ConfigureRateAsync(configureRateA: false);
        _hotkeyItem.Click += async (_, _) => await ConfigureHotkeyAsync();
        _startupItem.Click += async (_, _) => await SetStartupAsync(!_state.StartupEnabled);
        _uninstallItem.Click += async (_, _) => await BeginUninstallAsync();
        _exitItem.Click += (_, _) => Observe(ShutdownAsync());
        _menu.Opening += async (_, _) => await RunCommandAsync(_controller.RefreshDisplaysAsync);

        _menu.Items.AddRange(
        [
            _statusItem,
            _displayStart,
            _displayEnd,
            _rateAItem,
            _rateBItem,
            _hotkeyItem,
            _startupItem,
            _uninstallItem,
            _exitItem
        ]);
    }

    private async void InitializeOnFirstIdle(object? sender, EventArgs eventArgs)
    {
        System.Windows.Forms.Application.Idle -= InitializeOnFirstIdle;
        await RunCommandAsync(_controller.InitializeAsync);

        if (_controller.State.IsDistributionBuild)
        {
            await RunCommandAsync(_controller.EnsureInstalledAsync);
        }

        if (!_exitRequested)
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _displayEventsSubscribed = true;
        }
    }

    private void OnStateChanged(object? sender, AppStateChangedEventArgs eventArgs) =>
        Dispatch(() => Render(eventArgs.State));

    private void Dispatch(Action action)
    {
        if (_exitRequested || _dispatcher.IsDisposed)
        {
            return;
        }

        if (_dispatcher.InvokeRequired)
        {
            try
            {
                _dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException) when (_exitRequested || _dispatcher.IsDisposed)
            {
                // The message loop is already shutting down.
            }

            return;
        }

        action();
    }

    private void Render(AppState state)
    {
        if (_exitRequested)
        {
            return;
        }

        _state = state;
        var rates = state.Config.GetRatePair();
        var primary = state.Displays.PrimaryOrFirst;
        var currentRate = primary?.CurrentRefreshRate;

        _statusItem.Text = primary is null
            ? "Current: No displays detected"
            : currentRate is > 1
                ? $"{primary.Label}: {currentRate} Hz"
                : $"{primary.Label}: Unknown";

        _rateAItem.Text = $"Set Rate A… ({state.Config.RateA} Hz)";
        _rateBItem.Text = $"Set Rate B… ({state.Config.RateB} Hz)";
        _hotkeyItem.Text = state.Config.GetHotkeyBinding() is { IsSuccess: true } binding
            ? $"Hotkey: {binding.Value.DisplayText}"
            : "Hotkey: Invalid — click to fix";

        var hasRateOptions = RefreshRatePolicy.GetSupportedRateOptions(state.Displays).Count > 0;
        _rateAItem.Enabled = hasRateOptions && !state.IsBusy;
        _rateBItem.Enabled = hasRateOptions && !state.IsBusy;
        _hotkeyItem.Enabled = !state.IsBusy;
        _startupItem.Checked = state.StartupEnabled;
        _startupItem.Enabled = state.StartupAvailable && !state.IsBusy;
        _startupItem.ToolTipText = state.StartupAvailable
            ? string.Empty
            : "Unavailable in portable mode or because the registry could not be read.";
        _uninstallItem.Enabled = state.CanManageInstallation && state.IsInstalled && !state.IsBusy;
        _uninstallItem.ToolTipText = state.CanManageInstallation
            ? string.Empty
            : "Unavailable in portable development builds.";

        if (_renderedSnapshotAt != state.Displays.CapturedAt ||
            _renderedRateA != state.Config.RateA ||
            _renderedRateB != state.Config.RateB)
        {
            RebuildDisplayItems(state, rates);
            _renderedSnapshotAt = state.Displays.CapturedAt;
            _renderedRateA = state.Config.RateA;
            _renderedRateB = state.Config.RateB;
        }
        else
        {
            foreach (var item in _displayItems)
            {
                item.Enabled = item.Tag is true && !state.IsBusy;
            }
        }

        UpdateIconAndTooltip(currentRate, state.Config);
        ShowStateMessages(state);

        if (state.ExitRequested)
        {
            ShowBalloon("Uninstall will be completed in the background.", ToolTipIcon.Info);
            _dispatcher.BeginInvoke((Action)(() => Observe(ShutdownAsync())));
        }
    }

    private void RebuildDisplayItems(AppState state, Result<RatePair> rates)
    {
        foreach (var item in _displayItems)
        {
            _menu.Items.Remove(item);
            item.Dispose();
        }

        _displayItems.Clear();
        var insertionIndex = _menu.Items.IndexOf(_displayEnd);

        if (state.Displays.Displays.Count == 0)
        {
            var noDisplays = new ToolStripMenuItem("No displays detected") { Enabled = false };
            _displayItems.Add(noDisplays);
            _menu.Items.Insert(insertionIndex, noDisplays);
            return;
        }

        var plan = rates.IsSuccess
            ? RefreshRatePolicy.CreateTogglePlan(state.Displays, rates.Value)
            : Result.Failure<RefreshRatePlan>(rates.Error!);
        var target = rates.IsSuccess && state.Displays.PrimaryOrFirst?.CurrentRefreshRate is int current
            ? RefreshRatePolicy.DetermineTargetRate(current, rates.Value)
            : 0;

        foreach (var display in state.Displays.Displays)
        {
            var canToggle = plan.IsSuccess;
            var text = display.CurrentRefreshRate is int refreshRate
                ? canToggle
                    ? $"{display.Label}: {refreshRate} Hz → {plan.Value.TargetRate} Hz"
                    : target > 0
                        ? $"{display.Label}: {refreshRate} Hz — {target} Hz unavailable"
                        : $"{display.Label}: {refreshRate} Hz"
                : $"{display.Label}: Unknown";

            var item = new ToolStripMenuItem(text)
            {
                Enabled = canToggle && !state.IsBusy,
                Tag = canToggle,
                ToolTipText = canToggle ? "Toggle all active displays." : plan.Error?.Message
            };
            item.Click += async (_, _) => await RunCommandAsync(_controller.ToggleAsync);
            _displayItems.Add(item);
            _menu.Items.Insert(insertionIndex++, item);
        }
    }

    private void UpdateIconAndTooltip(int? currentRate, AppConfig config)
    {
        var iconKey = $"{currentRate}:{config.RateA}:{config.RateB}";
        if (!string.Equals(iconKey, _iconKey, StringComparison.Ordinal))
        {
            var nextIcon = currentRate is > 1
                ? TrayIconRenderer.CreateForRate(currentRate.Value, config.RateA, config.RateB)
                : TrayIconRenderer.CreateUnknown();
            _notifyIcon.Icon = nextIcon;
            _currentIcon?.Dispose();
            _currentIcon = nextIcon;
            _iconKey = iconKey;
        }

        var tooltip = currentRate is > 1
            ? $"RefreshToggle: {currentRate} Hz ({config.RateA}/{config.RateB})"
            : "RefreshToggle";
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    private void ShowStateMessages(AppState state)
    {
        if (state.LastError is not null &&
            !string.Equals(state.LastError.Message, _shownError, StringComparison.Ordinal))
        {
            _shownError = state.LastError.Message;
            ShowBalloon(state.LastError.Message, ToolTipIcon.Error);
        }

        if (!string.IsNullOrWhiteSpace(state.Notification) &&
            !string.Equals(state.Notification, _shownNotification, StringComparison.Ordinal))
        {
            _shownNotification = state.Notification;
            ShowBalloon(state.Notification, ToolTipIcon.Info);
        }
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            _ = RunCommandAsync(_controller.ToggleAsync);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs eventArgs) =>
        Dispatch(() => Observe(_controller.QueueDisplayRefreshAsync()));

    private async Task ConfigureRateAsync(bool configureRateA)
    {
        var options = RefreshRatePolicy
            .GetSupportedRateOptions(_state.Displays)
            .Select(option => new RateSupportOption(
                option.Rate,
                option.SupportedDisplayCount,
                option.TotalDisplayCount))
            .ToArray();
        if (options.Length == 0)
        {
            ShowBalloon("No supported refresh rates were detected.", ToolTipIcon.Error);
            return;
        }

        var configuredRate = configureRateA ? _state.Config.RateA : _state.Config.RateB;
        var selectedRate = ConfigurationDialogs.ShowRateSelection(
            configureRateA ? "Set Rate A" : "Set Rate B",
            options,
            configuredRate);
        if (selectedRate is null)
        {
            return;
        }

        var rates = RatePair.Create(
            configureRateA ? selectedRate.Value : _state.Config.RateA,
            configureRateA ? _state.Config.RateB : selectedRate.Value);
        if (rates.IsFailure)
        {
            ShowBalloon(rates.Error!.Message, ToolTipIcon.Error);
            return;
        }

        await RunCommandAsync(token => _controller.ChangeRatesAsync(rates.Value, token));
    }

    private async Task ConfigureHotkeyAsync()
    {
        var current = _state.Config.GetHotkeyBinding();
        var active = current.IsSuccess ? current.Value : HotkeyBinding.Default;
        var selection = ConfigurationDialogs.ShowHotkey(active.ModifierText, active.KeyText);
        if (selection is null)
        {
            return;
        }

        var binding = HotkeyBinding.Parse(selection.Modifiers, selection.Key);
        if (binding.IsFailure)
        {
            ShowBalloon(binding.Error!.Message, ToolTipIcon.Error);
            return;
        }

        await RunCommandAsync(token => _controller.ChangeHotkeyAsync(binding.Value, token));
    }

    private Task SetStartupAsync(bool enabled) =>
        RunCommandAsync(token => _controller.SetStartupEnabledAsync(enabled, token));

    private Task BeginUninstallAsync()
    {
        if (MessageBox.Show(
                "Remove the installed RefreshToggle executable and startup entry? Your configuration will be kept.",
                "Uninstall RefreshToggle",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return Task.CompletedTask;
        }

        return RunCommandAsync(_controller.BeginUninstallAsync);
    }

    private async Task RunCommandAsync(
        Func<CancellationToken, Task<Result<AppState>>> operation)
    {
        if (_exitRequested)
        {
            return;
        }

        try
        {
            await operation(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (ObjectDisposedException) when (_exitRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Dispatch(() => ShowBalloon(
                $"An unexpected command error occurred: {exception.Message}",
                ToolTipIcon.Error));
        }
    }

    private static async void Observe(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Superseded display events and shutdown are expected.
        }
        catch (ObjectDisposedException)
        {
            // The controller is shutting down.
        }
    }

    private void ShowBalloon(string message, ToolTipIcon icon)
    {
        if (_exitRequested || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = "RefreshToggle";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4000);
    }

    protected override void ExitThreadCore()
    {
        Observe(ShutdownAsync());
    }

    private async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        _exitRequested = true;
        System.Windows.Forms.Application.Idle -= InitializeOnFirstIdle;
        if (_displayEventsSubscribed)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _displayEventsSubscribed = false;
        }

        _controller.StateChanged -= OnStateChanged;
        _shutdown.Cancel();
        _notifyIcon.MouseClick -= OnNotifyIconMouseClick;
        _notifyIcon.Visible = false;
        try
        {
            await _controller.DisposeAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Controller shutdown failed: {exception}");
        }

        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        _menu.Dispose();
        _dispatcher.Dispose();
        _shutdown.Dispose();
        base.ExitThreadCore();
    }
}

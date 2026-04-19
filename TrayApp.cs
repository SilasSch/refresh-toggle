using System.Drawing;
using System.Windows.Forms;

namespace RefreshToggle;

internal sealed class TrayApp : IDisposable
{
    private readonly DisplayManager _displayManager = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _startWithWindowsItem;
    private readonly ToolStripMenuItem _uninstallItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly AppConfig _config;
    private Icon? _currentIcon;

    public TrayApp(bool showInstallNotification, string? installError, string? startupMigrationError)
    {
        _config = AppConfig.Load();

        var startupEnabled = false;
        var startupStateAvailable = true;
        string? deferredError = null;

        try
        {
            startupEnabled = StartupManager.IsEnabled();
        }
        catch (Exception ex)
        {
            startupStateAvailable = false;
            deferredError = $"Could not determine startup state: {ex.Message}";
        }

        if (startupStateAvailable && _config.StartWithWindows != startupEnabled)
        {
            _config.StartWithWindows = startupEnabled;

            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                deferredError = $"Failed to save startup setting to the application configuration.{Environment.NewLine}{Environment.NewLine}{ex.Message}";
            }
        }

        // Remove stale Run entry if it exists but doesn't match the current executable path.
        if (startupStateAvailable && !startupEnabled)
        {
            try
            {
                if (StartupManager.HasEntry())
                {
                    StartupManager.Disable();
                }
            }
            catch
            {
                // Best effort: don't crash startup if the stale entry can't be removed.
            }
        }

        _statusItem = new ToolStripMenuItem("Current: Unknown") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Toggle Refresh Rate");
        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = startupEnabled,
            Enabled = startupStateAvailable
        };
        _uninstallItem = new ToolStripMenuItem("Uninstall")
        {
            Enabled = InstallationManager.HasInstalledCopy()
        };
        _exitItem = new ToolStripMenuItem("Exit");

        _toggleItem.Click += (_, _) => ToggleRefreshRate();
        _startWithWindowsItem.Click += (_, _) => ToggleStartWithWindows();
        _uninstallItem.Click += (_, _) => Uninstall();
        _exitItem.Click += (_, _) => ExitApplication();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(_startWithWindowsItem);
        _menu.Items.Add(_uninstallItem);
        _menu.Items.Add(_exitItem);

        _currentIcon = TrayIconHelper.CreateUnknown();
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Visible = true,
            ContextMenuStrip = _menu
        };

        _notifyIcon.MouseClick += NotifyIconOnMouseClick;
        _notifyIcon.DoubleClick += (_, _) => ToggleRefreshRate();

        UpdateStatusText();

        if (deferredError is not null)
        {
            ShowError(deferredError);
        }

        if (showInstallNotification)
        {
            ShowInfo(@"Installed to %LOCALAPPDATA%\RefreshToggle");
        }

        if (!string.IsNullOrWhiteSpace(installError))
        {
            ShowError(installError);
        }

        if (!string.IsNullOrWhiteSpace(startupMigrationError))
        {
            ShowError(startupMigrationError);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        _currentIcon = null;
        _menu.Dispose();
    }

    private void NotifyIconOnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleRefreshRate();
        }
    }

    private void ToggleRefreshRate()
    {
        if (!_displayManager.TryGetCurrentRefreshRate(out var current, out var getError))
        {
            ShowError(getError ?? "Unable to read refresh rate.");
            return;
        }

        var target = DetermineTargetRate(current);
        if (!_displayManager.TrySetRefreshRate(target, out var setError))
        {
            ShowError(setError ?? "Unable to set refresh rate.");
            return;
        }

        UpdateStatusText();
    }

    private int DetermineTargetRate(int current)
    {
        if (current == _config.RateA)
        {
            return _config.RateB;
        }

        if (current == _config.RateB)
        {
            return _config.RateA;
        }

        var distanceToA = Math.Abs(current - _config.RateA);
        var distanceToB = Math.Abs(current - _config.RateB);
        return distanceToA <= distanceToB ? _config.RateB : _config.RateA;
    }

    private void UpdateStatusText()
    {
        if (_displayManager.TryGetCurrentRefreshRate(out var current, out _))
        {
            _statusItem.Text = $"Current: {current} Hz";
            _notifyIcon.Text = TrimTooltip($"RefreshToggle: {current} Hz ({_config.RateA}/{_config.RateB})");
            UpdateIcon(TrayIconHelper.CreateForRate(current, _config));
        }
        else
        {
            _statusItem.Text = "Current: Unknown";
            _notifyIcon.Text = TrimTooltip("RefreshToggle");
            UpdateIcon(TrayIconHelper.CreateUnknown());
        }
    }

    private void UpdateIcon(Icon newIcon)
    {
        _notifyIcon.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;
    }

    private void ShowError(string message)
    {
        _notifyIcon.BalloonTipTitle = "RefreshToggle";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Error;
        _notifyIcon.ShowBalloonTip(4000);
        UpdateStatusText();
    }

    private void ShowInfo(string message)
    {
        _notifyIcon.BalloonTipTitle = "RefreshToggle";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private static string TrimTooltip(string text)
    {
        const int maxLength = 63;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private void ToggleStartWithWindows()
    {
        bool previousState;
        try
        {
            previousState = StartupManager.IsEnabled();
        }
        catch (Exception ex)
        {
            _startWithWindowsItem.Enabled = false;
            ShowError($"Could not read startup state: {ex.Message}");
            return;
        }

        var newState = !previousState;

        // Sync UI to the actual registry state in case they diverged.
        _startWithWindowsItem.Checked = previousState;

        try
        {
            if (newState)
            {
                StartupManager.Enable();
            }
            else
            {
                StartupManager.Disable();
            }

            _startWithWindowsItem.Checked = newState;
            _config.StartWithWindows = newState;
            _config.Save();
        }
        catch (Exception ex)
        {
            string rollbackError = string.Empty;
            try
            {
                if (previousState)
                {
                    StartupManager.Enable();
                }
                else
                {
                    StartupManager.Disable();
                }
            }
            catch (Exception rollbackEx)
            {
                rollbackError = rollbackEx.Message;
            }

            try
            {
                var actualState = StartupManager.IsEnabled();
                _startWithWindowsItem.Checked = actualState;
                _config.StartWithWindows = actualState;

                try
                {
                    _config.Save();
                }
                catch
                {
                    // Best effort only: avoid throwing while already handling an error.
                }

                if (actualState == newState)
                {
                    ShowError($"Could not save startup setting: {ex.Message}");
                }
                else
                {
                    var message = string.IsNullOrEmpty(rollbackError)
                        ? $"Could not update startup setting: {ex.Message}"
                        : $"Could not update startup setting: {ex.Message} (rollback also failed: {rollbackError})";
                    ShowError(message);
                }
            }
            catch
            {
                // Leave the current UI/config state unchanged if the actual state can't be determined.
                var message = string.IsNullOrEmpty(rollbackError)
                    ? $"Could not update startup setting: {ex.Message}"
                    : $"Could not update startup setting: {ex.Message} (rollback also failed: {rollbackError})";
                ShowError(message);
            }
        }
    }

    private static void ExitApplication()
    {
        Application.Exit();
    }

    private void Uninstall()
    {
        try
        {
            StartupManager.Disable();
            _startWithWindowsItem.Checked = false;
            _config.StartWithWindows = false;
            _config.Save();
        }
        catch (Exception ex)
        {
            ShowError($"Could not disable startup entry: {ex.Message}");
            return;
        }

        try
        {
            var exitAfterUninstall = InstallationManager.RemoveInstalledCopy();

            if (exitAfterUninstall)
            {
                ShowInfo("Uninstall started. RefreshToggle will now exit.");
                ExitApplication();
                return;
            }

            ShowInfo(@"Uninstalled from %LOCALAPPDATA%\RefreshToggle");
        }
        catch (Exception ex)
        {
            ShowError($"Could not remove installed copy: {ex.Message}");
        }
        finally
        {
            _uninstallItem.Enabled = InstallationManager.HasInstalledCopy();
        }
    }
}

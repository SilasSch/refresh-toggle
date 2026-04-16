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
    private readonly ToolStripMenuItem _exitItem;
    private readonly AppConfig _config;

    public TrayApp()
    {
        _config = AppConfig.Load();

        var startupEnabled = false;
        var startupStateAvailable = true;

        try
        {
            startupEnabled = StartupManager.IsEnabled();
            if (_config.StartWithWindows != startupEnabled)
            {
                _config.StartWithWindows = startupEnabled;
                _config.Save();
            }
        }
        catch
        {
            startupStateAvailable = false;
        }

        _statusItem = new ToolStripMenuItem("Current: Unknown") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Toggle Refresh Rate");
        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = startupEnabled,
            Enabled = startupStateAvailable
        };
        _exitItem = new ToolStripMenuItem("Exit");

        _toggleItem.Click += (_, _) => ToggleRefreshRate();
        _startWithWindowsItem.Click += (_, _) => ToggleStartWithWindows();
        _exitItem.Click += (_, _) => ExitApplication();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(_startWithWindowsItem);
        _menu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = _menu
        };

        _notifyIcon.MouseClick += NotifyIconOnMouseClick;
        _notifyIcon.DoubleClick += (_, _) => ToggleRefreshRate();

        UpdateStatusText();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
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
        }
        else
        {
            _statusItem.Text = "Current: Unknown";
            _notifyIcon.Text = TrimTooltip("RefreshToggle");
        }
    }

    private void ShowError(string message)
    {
        _notifyIcon.BalloonTipTitle = "RefreshToggle";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Error;
        _notifyIcon.ShowBalloonTip(4000);
        UpdateStatusText();
    }

    private static string TrimTooltip(string text)
    {
        const int maxLength = 63;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private void ToggleStartWithWindows()
    {
        var previousState = _startWithWindowsItem.Checked;
        var newState = !previousState;

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

            var actualState = StartupManager.IsEnabled();
            _startWithWindowsItem.Checked = actualState;
            _config.StartWithWindows = actualState;

            var message = string.IsNullOrEmpty(rollbackError)
                ? $"Could not update startup setting: {ex.Message}"
                : $"Could not update startup setting: {ex.Message} (rollback also failed: {rollbackError})";
            ShowError(message);
        }
    }

    private static void ExitApplication()
    {
        Application.Exit();
    }
}

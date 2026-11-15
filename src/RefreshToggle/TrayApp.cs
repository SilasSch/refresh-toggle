using Microsoft.Win32;

namespace RefreshToggle;

/// <summary>
/// The whole app: tray icon, menu, hotkey and toggling. Everything runs on the UI thread,
/// so there is no locking.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly Font _boldFont;
    private readonly GlobalHotkey _hotkey = new();
    private readonly PowerSourceTracker _power = new();
    private readonly System.Windows.Forms.Timer _displayChangeDebounce = new() { Interval = 500 };
    private Config _config;
    private List<Display> _displays = [];
    private string? _iconKey;
    private bool _busy;
    private bool _powerRuleOwed; // a real power transition happened but could not be applied yet

    public TrayApp()
    {
        (_config, var configWarning) = Config.Load(Config.DefaultPath);

        _boldFont = new Font(_menu.Font, FontStyle.Bold);
        _menu.Items.Add("…"); // an empty ContextMenuStrip never raises Opening
        _menu.Opening += (_, e) =>
        {
            RebuildMenu();
            e.Cancel = false;
        };

        _tray = new NotifyIcon { ContextMenuStrip = _menu, Text = "RefreshToggle", Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Toggle();
            }
        };

        _hotkey.Pressed += Toggle;
        _displayChangeDebounce.Tick += (_, _) =>
        {
            _displayChangeDebounce.Stop();
            RefreshDisplays();
            if (_powerRuleOwed && _config.AutoByPowerSource && ReadPluggedIn() is bool pluggedIn)
            {
                ApplyPowerRule(pluggedIn); // e.g. displays were not back yet right after resume
            }
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        RefreshDisplays();
        CheckPowerSource(); // before the warnings below, so their balloons aren't replaced by "🔌 60 → 120 Hz"
        if (configWarning is not null)
        {
            Notify(configWarning, ToolTipIcon.Warning);
        }

        HotkeyBinding.TryParse(_config.HotkeyModifiers, _config.HotkeyKey, out var hotkey);
        if (!_hotkey.TryRegister(hotkey))
        {
            Notify($"Hotkey {hotkey} is already used by another app. Pick another one from the menu.", ToolTipIcon.Warning);
        }
    }

    private void Toggle()
    {
        _powerRuleOwed = false; // a manual choice wins over a pending automatic one
        RefreshDisplays();
        if (_displays.Count == 0)
        {
            Notify("No active displays found.", ToolTipIcon.Error);
            return;
        }

        SwitchTo(RateLogic.NextRate(_displays[0].CurrentRate, _config.RateA, _config.RateB), prefix: "");
    }

    /// <summary>
    /// Switches every display that isn't at <paramref name="target"/> yet and supports it.
    /// Silent when nothing needs to change; <paramref name="prefix"/> marks automatic switches.
    /// Returns false when it could not run (busy, or no displays yet) so the caller can retry.
    /// </summary>
    private bool SwitchTo(int target, string prefix)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        try
        {
            RefreshDisplays();
            if (_displays.Count == 0)
            {
                return false;
            }

            var pending = _displays.Where(display => display.CurrentRate != target).ToList();
            if (pending.Count == 0)
            {
                return true;
            }

            var fromRate = pending[0].CurrentRate; // primary first, unless it is already at target
            var unsupported = new List<string>();
            var failed = new List<string>();
            foreach (var display in pending)
            {
                if (!display.SupportedRates.Contains(target))
                {
                    unsupported.Add(display.Name);
                }
                else if (Displays.SetRate(display.DeviceName, target) is var code and not 0)
                {
                    failed.Add($"{display.Name} (code {code})");
                }
            }

            RefreshDisplays();
            if (failed.Count > 0)
            {
                Notify($"{prefix}Could not switch to {target} Hz: {string.Join(", ", failed)}", ToolTipIcon.Error);
            }
            else if (unsupported.Count == pending.Count)
            {
                Notify($"{prefix}{target} Hz is not supported by: {string.Join(", ", unsupported)}", ToolTipIcon.Warning);
            }
            else
            {
                var note = unsupported.Count > 0 ? $"\nSkipped (no {target} Hz): {string.Join(", ", unsupported)}" : "";
                Notify($"{prefix}{fromRate} → {target} Hz{note}", ToolTipIcon.Info);
            }

            return true;
        }
        finally
        {
            _busy = false;
        }
    }

    private static bool HasBattery =>
        !SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery);

    private static bool? ReadPluggedIn() => SystemInformation.PowerStatus.PowerLineStatus switch
    {
        PowerLineStatus.Online => true,
        PowerLineStatus.Offline => false,
        _ => null,
    };

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Resume covers "unplugged while asleep"; the tracker filters battery-percentage ticks.
        if (e.Mode is PowerModes.StatusChange or PowerModes.Resume)
        {
            CheckPowerSource();
        }
    }

    /// <summary>Applies the power rule only on a real plugged-in / on-battery transition (or the first reading).</summary>
    private void CheckPowerSource()
    {
        if (_power.Observe(ReadPluggedIn()) is bool pluggedIn && _config.AutoByPowerSource && HasBattery)
        {
            ApplyPowerRule(pluggedIn);
        }
    }

    private void ApplyPowerRule(bool pluggedIn) =>
        _powerRuleOwed = !SwitchTo(
            RateLogic.RateForPowerSource(pluggedIn, _config.RateA, _config.RateB),
            pluggedIn ? "🔌 " : "🔋 ");

    private void SetAutoByPowerSource(bool enabled)
    {
        var updated = _config with { AutoByPowerSource = enabled };
        if (enabled && ReadPluggedIn() is bool pluggedIn)
        {
            ApplyPowerRule(pluggedIn);
        }

        SaveConfig(updated); // last, so a save error isn't hidden behind the switch balloon
    }

    private void RefreshDisplays()
    {
        _displays = Displays.GetActive();
        var rate = _displays.Count > 0 ? _displays[0].CurrentRate : 0;

        var iconKey = $"{rate}/{_config.RateA}/{_config.RateB}";
        if (iconKey != _iconKey)
        {
            var oldIcon = _tray.Icon;
            _tray.Icon = IconRenderer.Create(rate, _config.RateA, _config.RateB);
            oldIcon?.Dispose();
            _iconKey = iconKey;
        }

        _tray.Text = rate > 1 ? $"RefreshToggle – {rate} Hz" : "RefreshToggle";
    }

    private void RebuildMenu()
    {
        RefreshDisplays();
        foreach (var item in _menu.Items.Cast<ToolStripItem>().ToArray())
        {
            item.Dispose();
        }

        _menu.Items.Clear();

        foreach (var display in _displays)
        {
            _menu.Items.Add(new ToolStripMenuItem($"{display.Name}: {display.CurrentRate} Hz") { Enabled = false });
        }

        if (_displays.Count > 0)
        {
            var target = RateLogic.NextRate(_displays[0].CurrentRate, _config.RateA, _config.RateB);
            _menu.Items.Add(new ToolStripMenuItem($"Switch to {target} Hz", null, (_, _) => Toggle())
            {
                Font = _boldFont,
            });
        }
        else
        {
            _menu.Items.Add(new ToolStripMenuItem("No displays detected") { Enabled = false });
        }

        _menu.Items.Add(new ToolStripSeparator());
        var commonRates = RateLogic.CommonRates(_displays.Select(display => display.SupportedRates));
        _menu.Items.Add(CreateRateMenu("Rate A", _config.RateA, _config.RateB, commonRates, rate => _config with { RateA = rate }));
        _menu.Items.Add(CreateRateMenu("Rate B", _config.RateB, _config.RateA, commonRates, rate => _config with { RateB = rate }));

        HotkeyBinding.TryParse(_config.HotkeyModifiers, _config.HotkeyKey, out var hotkey);
        _menu.Items.Add(new ToolStripMenuItem($"Hotkey: {hotkey}…", null, (_, _) => ChangeHotkey(hotkey)));

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = SafeIsStartupEnabled() };
        startup.Click += (_, _) => SetStartup(!startup.Checked);
        _menu.Items.Add(startup);

        if (HasBattery)
        {
            var high = Math.Max(_config.RateA, _config.RateB);
            var low = Math.Min(_config.RateA, _config.RateB);
            var auto = new ToolStripMenuItem($"Auto: {high} Hz plugged in, {low} Hz on battery") { Checked = _config.AutoByPowerSource };
            auto.Click += (_, _) => SetAutoByPowerSource(!auto.Checked);
            _menu.Items.Add(auto);
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
    }

    /// <summary>Submenu listing the rates all displays support; the configured rate is checked.</summary>
    private ToolStripMenuItem CreateRateMenu(
        string title,
        int selected,
        int otherProfile,
        IReadOnlyList<int> commonRates,
        Func<int, Config> withRate)
    {
        var menu = new ToolStripMenuItem($"{title}: {selected} Hz");
        foreach (var rate in commonRates.Append(selected).Distinct().Order())
        {
            var label = commonRates.Contains(rate) ? $"{rate} Hz" : $"{rate} Hz (not supported by all displays)";
            menu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => SaveConfig(withRate(rate)))
            {
                Checked = rate == selected,
                Enabled = rate != otherProfile,
            });
        }

        return menu;
    }

    private void ChangeHotkey(HotkeyBinding current)
    {
        // The current hotkey would otherwise fire (and toggle) while it is being re-entered.
        _hotkey.Pressed -= Toggle;
        try
        {
            using var dialog = new HotkeyDialog(current);
            if (dialog.ShowDialog() != DialogResult.OK || dialog.Selected == current)
            {
                return;
            }

            if (!_hotkey.TryRegister(dialog.Selected))
            {
                Notify($"{dialog.Selected} is already used by another app.", ToolTipIcon.Warning);
                return;
            }

            SaveConfig(_config with
            {
                HotkeyModifiers = dialog.Selected.ModifierText,
                HotkeyKey = dialog.Selected.KeyText,
            });
        }
        finally
        {
            _hotkey.Pressed += Toggle;
        }
    }

    private void SaveConfig(Config config)
    {
        _config = config;
        RefreshDisplays(); // icon colours depend on Rate A/B
        try
        {
            config.Save(Config.DefaultPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Notify($"Settings could not be saved: {exception.Message}", ToolTipIcon.Error);
        }
    }

    private bool SafeIsStartupEnabled()
    {
        try
        {
            return Startup.IsEnabled();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            Startup.SetEnabled(enabled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Notify($"Autostart could not be changed: {exception.Message}", ToolTipIcon.Error);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _displayChangeDebounce.Stop();
        _displayChangeDebounce.Start();
    }

    private void Notify(string message, ToolTipIcon icon) =>
        _tray.ShowBalloonTip(3000, "RefreshToggle", message, icon);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _displayChangeDebounce.Dispose();
            _hotkey.Dispose();
            _tray.Visible = false;
            _tray.Icon?.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _boldFont.Dispose();
        }

        base.Dispose(disposing);
    }
}

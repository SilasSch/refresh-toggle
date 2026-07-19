namespace RefreshToggle.App;

internal sealed record RateSupportOption(int RefreshRate, int SupportedDisplayCount, int DisplayCount)
{
    public override string ToString() =>
        $"{RefreshRate} Hz (supported by {SupportedDisplayCount}/{DisplayCount} displays)";
}

internal sealed record HotkeyDialogSelection(string Modifiers, string Key);

internal static class ConfigurationDialogs
{
    private static readonly string[] CommonKeys =
    [
        "R", "T", "F", "G", "V", "B", "Space",
        "F1", "F2", "F3", "F4", "F5", "F6",
        "F7", "F8", "F9", "F10", "F11", "F12"
    ];

    public static int? ShowRateSelection(
        string title,
        IReadOnlyList<RateSupportOption> options,
        int configuredRate)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(390, 132)
        };

        var explanation = new Label
        {
            AutoSize = true,
            Text = "Select a global target refresh rate:",
            Left = 12,
            Top = 15
        };

        var combo = new ComboBox
        {
            Left = 12,
            Top = 42,
            Width = 366,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        foreach (var option in options)
        {
            combo.Items.Add(option);
        }

        var selectedIndex = options
            .Select((option, index) => (option, index))
            .Where(item => item.option.RefreshRate == configuredRate)
            .Select(item => item.index)
            .DefaultIfEmpty(0)
            .First();
        combo.SelectedIndex = options.Count == 0 ? -1 : selectedIndex;

        var applyButton = new Button
        {
            Text = "Apply",
            Left = 222,
            Top = 84,
            Width = 75,
            DialogResult = DialogResult.OK,
            Enabled = options.Count > 0
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 303,
            Top = 84,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        dialog.Controls.Add(explanation);
        dialog.Controls.Add(combo);
        dialog.Controls.Add(applyButton);
        dialog.Controls.Add(cancelButton);
        dialog.AcceptButton = applyButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog() == DialogResult.OK && combo.SelectedItem is RateSupportOption selected
            ? selected.RefreshRate
            : null;
    }

    public static HotkeyDialogSelection? ShowHotkey(string currentModifiers, string currentKey)
    {
        var parsedModifiers = currentModifiers
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var dialog = new Form
        {
            Text = "Configure Hotkey",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(320, 220)
        };

        var instruction = new Label
        {
            AutoSize = true,
            Text = "Select the desired key combination:",
            Left = 12,
            Top = 12
        };
        var current = new Label
        {
            AutoSize = true,
            Text = $"Current: {currentModifiers}+{currentKey}",
            Left = 12,
            Top = 36,
            ForeColor = SystemColors.GrayText
        };

        var ctrl = CreateModifierCheckBox("Ctrl", 12, parsedModifiers.Contains("Ctrl") || parsedModifiers.Contains("Control"));
        var alt = CreateModifierCheckBox("Alt", 82, parsedModifiers.Contains("Alt"));
        var shift = CreateModifierCheckBox("Shift", 152, parsedModifiers.Contains("Shift"));
        var windows = CreateModifierCheckBox("Win", 232, parsedModifiers.Contains("Win") || parsedModifiers.Contains("Windows"));

        var keyLabel = new Label { AutoSize = true, Text = "Key:", Left = 12, Top = 105 };
        var keyCombo = new ComboBox
        {
            Left = 52,
            Top = 101,
            Width = 254,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        keyCombo.Items.AddRange(CommonKeys.Cast<object>().ToArray());
        var keyIndex = Array.FindIndex(CommonKeys, key => string.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase));
        keyCombo.SelectedIndex = keyIndex >= 0 ? keyIndex : 0;

        var preview = new Label { AutoSize = true, Left = 12, Top = 140 };
        var applyButton = new Button
        {
            Text = "Apply",
            Left = 150,
            Top = 174,
            Width = 75,
            DialogResult = DialogResult.OK
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 231,
            Top = 174,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        void UpdatePreview(object? sender, EventArgs eventArgs)
        {
            var modifiers = GetModifiers(ctrl, alt, shift, windows);
            applyButton.Enabled = modifiers.Length > 0 && keyCombo.SelectedIndex >= 0;
            preview.Text = applyButton.Enabled
                ? $"Preview: {modifiers}+{keyCombo.SelectedItem}"
                : "Select at least one modifier.";
        }

        ctrl.CheckedChanged += UpdatePreview;
        alt.CheckedChanged += UpdatePreview;
        shift.CheckedChanged += UpdatePreview;
        windows.CheckedChanged += UpdatePreview;
        keyCombo.SelectedIndexChanged += UpdatePreview;

        dialog.Controls.AddRange(
        [
            instruction, current, ctrl, alt, shift, windows, keyLabel, keyCombo,
            preview, applyButton, cancelButton
        ]);
        dialog.AcceptButton = applyButton;
        dialog.CancelButton = cancelButton;
        UpdatePreview(null, EventArgs.Empty);

        if (dialog.ShowDialog() != DialogResult.OK || keyCombo.SelectedItem is not string selectedKey)
        {
            return null;
        }

        return new HotkeyDialogSelection(GetModifiers(ctrl, alt, shift, windows), selectedKey);
    }

    private static CheckBox CreateModifierCheckBox(string text, int left, bool isChecked) => new()
    {
        Text = text,
        Left = left,
        Top = 68,
        Width = 70,
        Checked = isChecked
    };

    private static string GetModifiers(params CheckBox[] boxes) => string.Join(
        "+",
        boxes.Where(box => box.Checked).Select(box => box.Text));
}

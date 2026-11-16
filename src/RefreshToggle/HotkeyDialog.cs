namespace RefreshToggle;

/// <summary>"Press the new combination" dialog. Selected holds the captured hotkey.</summary>
internal sealed class HotkeyDialog : Form
{
    private readonly Label _preview;
    private readonly Button _ok;

    public HotkeyDialog(HotkeyBinding current)
    {
        Selected = current;
        Text = "RefreshToggle – Hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(340, 120);

        var hint = new Label
        {
            Text = "Press the new combination (Ctrl / Alt / Shift + key):",
            AutoSize = true,
            Location = new Point(12, 12),
        };
        _preview = new Label
        {
            Text = current.ToString(),
            AutoSize = true,
            Location = new Point(12, 40),
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
        };
        _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(172, 84) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(254, 84) };

        Controls.AddRange([hint, _preview, _ok, cancel]);
        CancelButton = cancel;
    }

    public HotkeyBinding Selected { get; private set; }

    /// <summary>Sees every key combination before the buttons do, including Alt combinations.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var modifierKeys = keyData & Keys.Modifiers;
        if (modifierKeys == Keys.None)
        {
            return base.ProcessCmdKey(ref msg, keyData); // plain Enter / Esc / Tab drive the dialog
        }

        var modifiers = HotkeyModifiers.None;
        if (modifierKeys.HasFlag(Keys.Control))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (modifierKeys.HasFlag(Keys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (modifierKeys.HasFlag(Keys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        var key = (uint)(keyData & Keys.KeyCode);
        if (HotkeyBinding.GetKeyName(key) is null)
        {
            _preview.Text = new HotkeyBinding(modifiers, 0).ModifierText + "+…";
            _ok.Enabled = false;
            return true;
        }

        Selected = new HotkeyBinding(modifiers, key);
        _preview.Text = Selected.ToString();
        _ok.Enabled = true;
        return true;
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

internal sealed class MaskShortcutDialog : Form
{
    private readonly ComboBox[] keys = new ComboBox[MaskHotkeys.ScopeCount];
    private readonly CheckBox[,] modifiers = new CheckBox[MaskHotkeys.ScopeCount, 4];
    private readonly List<Keys> choices = new List<Keys> { Keys.None };
    private static readonly string[] ModifierNames = { "Ctrl", "Alt", "Shift", "Win" };
    private static readonly uint[] ModifierFlags = { MaskHotkeys.ModControl, MaskHotkeys.ModAlt, MaskHotkeys.ModShift, MaskHotkeys.ModWin };

    internal MaskShortcutDialog(MaskLanguage language, int[] bindings, string warning, Func<int[], string> apply)
    {
        MaskUi.StyleWindow(this);
        Text = language.Text("Global Shortcuts", "全局快捷键");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        ClientSize = new Size(542, 313);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 15, 20, 12), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        for (int i = 0; i < keys.Length; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var title = MaskUi.Title();
        title.Text = Text;
        layout.Controls.Add(title, 0, 0);
        var hint = MaskUi.Caption();
        hint.Text = language.Text("Shortcuts use your saved appearance and end mode.", "快捷键使用已保存的外观和结束方式。");
        layout.Controls.Add(hint, 0, 1);
        choices.AddRange(MaskHotkeys.AvailableKeys());

        for (int row = 0; row < keys.Length; row++)
        {
            string name = MaskPanel.DisplayName(language, row);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = MaskUi.Surface,
                Margin = new Padding(0, 0, 0, 5), Padding = new Padding(12, 7, 0, 0) };
            controls.Controls.Add(new Label { Text = name, Font = MaskUi.Subheading, Width = 120, Margin = new Padding(0, 3, 0, 0) });
            var picker = new ComboBox { AccessibleName = name, Width = 84 };
            MaskUi.StyleSelector(picker);
            picker.Items.Add(language.Text("Disabled", "禁用"));
            picker.SelectedIndex = 0;
            for (int index = 1; index < choices.Count; index++)
            {
                Keys key = choices[index];
                picker.Items.Add(MaskHotkeys.KeyName(key));
                if (key == (Keys)(bindings[row] & 0xff))
                    picker.SelectedIndex = index;
            }
            keys[row] = picker;
            for (int column = 0; column < ModifierNames.Length; column++)
            {
                string modifier = ModifierNames[column];
                var check = new CheckBox { Text = modifier, AccessibleName = name + " " + modifier, AutoSize = true,
                    Checked = (((uint)bindings[row] >> 8) & ModifierFlags[column]) != 0 };
                modifiers[row, column] = check;
                controls.Controls.Add(check);
            }
            controls.Controls.Add(picker);
            layout.Controls.Add(controls, 0, row + 2);
        }

        var feedback = MaskUi.Caption();
        feedback.Text = warning;
        feedback.ForeColor = MaskUi.WarningInk;
        layout.Controls.Add(feedback, 0, 5);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = language.Text("Cancel", "取消"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = language.Text("Save", "保存"), AutoSize = true };
        MaskUi.StyleButton(cancel);
        MaskUi.StyleButton(save, primary: true);
        buttons.Controls.AddRange(new Control[] { save, cancel });
        layout.Controls.Add(buttons, 0, 6);
        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(layout);
        save.Click += (sender, args) =>
        {
            string error = apply(ReadBindings());
            if (error == null)
                DialogResult = DialogResult.OK;
            else
                feedback.Text = error;
        };
    }

    private int[] ReadBindings()
    {
        var bindings = new int[keys.Length];
        for (int row = 0; row < keys.Length; row++)
        {
            int selected = keys[row].SelectedIndex;
            if (selected <= 0)
                continue;
            uint flags = 0;
            for (int column = 0; column < ModifierFlags.Length; column++)
                if (modifiers[row, column].Checked)
                    flags |= ModifierFlags[column];
            bindings[row] = MaskHotkeys.Encode(flags, choices[selected]);
        }
        return bindings;
    }
}

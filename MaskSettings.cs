using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class MaskSettings : Form
{
    internal static void Open(IWin32Window owner)
    {
        bool recovered;
        using (var appearance = MaskAppearance.Load(out recovered))
        using (var form = new MaskSettings(appearance, recovered))
            form.ShowDialog(owner);
    }

    private MaskSettings(MaskAppearance appearance, bool recovered)
    {
        Text = "遮罩设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(456, 442);
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Font;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var mode = new ComboBox { Width = 154, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "遮罩外观" };
        mode.Items.AddRange(new object[] { "纯黑", "自定义图片", "大字提示" });
        mode.SelectedIndex = (int)appearance.Mode;
        layout.Controls.Add(mode, 0, 0);

        var preview = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, AccessibleName = "遮罩预览" };
        preview.Paint += (sender, args) => appearance.Draw(args.Graphics, preview.ClientRectangle);
        layout.Controls.Add(preview, 0, 1);

        var picture = new Button { Text = "选择图片…", AutoSize = true };
        layout.Controls.Add(picture, 0, 2);
        var text = new TextBox { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Fill, MaxLength = MaskAppearance.TextLimit,
            Text = appearance.Text, AccessibleName = "遮罩大字提示" };
        layout.Controls.Add(text, 0, 3);

        var password = new Button { Text = "密码与结束方式…", AutoSize = true };
        password.Click += (sender, args) => PasswordDialog.OpenSettings(this);
        layout.Controls.Add(password, 0, 4);

        var hint = new Label { Dock = DockStyle.Fill, Text = recovered ? "原外观设置无法读取，当前使用纯黑。" : "外观将在下次开启时生效。" };
        layout.Controls.Add(hint, 0, 5);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "保存外观", AutoSize = true };
        buttons.Controls.AddRange(new Control[] { save, cancel });
        layout.Controls.Add(buttons, 0, 6);
        CancelButton = cancel;
        Controls.Add(layout);

        Action refresh = () =>
        {
            appearance.Mode = (MaskAppearanceMode)mode.SelectedIndex;
            picture.Enabled = appearance.Mode == MaskAppearanceMode.Picture;
            text.Enabled = appearance.Mode == MaskAppearanceMode.Text;
            preview.Invalidate();
        };
        mode.SelectedIndexChanged += (sender, args) => refresh();
        text.TextChanged += (sender, args) => { appearance.Text = text.Text; preview.Invalidate(); };
        picture.Click += (sender, args) =>
        {
            using (var picker = new OpenFileDialog { Title = "选择遮罩图片", Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    appearance.Import(picker.FileName);
                    hint.Text = "图片已载入，会按比例显示在每块屏幕上。";
                    preview.Invalidate();
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                        ex is ExternalException || ex is OutOfMemoryException))
                        throw;
                    hint.Text = "无法读取图片，请选择有效的 PNG、JPG 或 BMP 文件。";
                }
            }
        };
        save.Click += (sender, args) =>
        {
            if (appearance.Mode == MaskAppearanceMode.Picture && appearance.Picture == null)
                hint.Text = "请先选择一张图片。";
            else if (appearance.Mode == MaskAppearanceMode.Text && string.IsNullOrWhiteSpace(appearance.Text))
                hint.Text = "请输入要显示的文字。";
            else
            {
                try { appearance.Save(); DialogResult = DialogResult.OK; }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ExternalException))
                        throw;
                    hint.Text = "外观设置保存失败，请检查本机应用数据目录。";
                }
            }
        };
        refresh();
    }
}

internal sealed class PasswordDialog : Form
{
    internal const int ActivateMessage = 0x8006;
    internal const int RaiseMessage = 0x8007;
    private readonly FlowLayoutPanel fields;

    private PasswordDialog(string title, bool topmost)
    {
        Text = title;
        StartPosition = topmost ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = topmost;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Font;
        Font = SystemFonts.MessageBoxFont;
        fields = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(16), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        Controls.Add(fields);
    }

    private TextBox AddPassword(string label)
    {
        fields.Controls.Add(new Label { Text = label, AutoSize = true });
        var input = new TextBox { UseSystemPasswordChar = true, MaxLength = MaskPassword.LengthLimit, Width = 276, AccessibleName = label };
        fields.Controls.Add(input);
        return input;
    }

    private Button AddButtons(string submit, out FlowLayoutPanel buttons)
    {
        var ok = new Button { Text = submit, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        fields.Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        return ok;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == ActivateMessage)
        {
            BringToFront();
            Activate();
            return;
        }
        if (message.Msg == RaiseMessage)
        {
            PrivacyMask.RaiseUnlockWindow(Handle);
            return;
        }
        base.WndProc(ref message);
    }

    internal static void Unlock(Func<string, string> attempt, Action<IntPtr> windowReady)
    {
        using (var form = new PasswordDialog("解除遮罩", true))
        {
            // Expose this separate window to capture and window pickers. The password box masks its value.
            form.ShowInTaskbar = true;
            var password = form.AddPassword("请输入遮罩密码");
            var feedback = new Label { AutoSize = true };
            form.fields.Controls.Add(feedback);
            FlowLayoutPanel buttons;
            var ok = form.AddButtons("解除遮罩", out buttons);
            buttons.Controls[1].Click += (sender, args) => form.Close();
            ok.Click += async (sender, args) =>
            {
                string value = password.Text;
                password.Clear();
                ok.Enabled = false;
                string reason;
                try { reason = await System.Threading.Tasks.Task.Run(() => attempt(value)); }
                catch (Exception) { reason = "unavailable"; }
                if (form.IsDisposed)
                    return;
                if (reason == null)
                    form.Close();
                else
                {
                    feedback.Text = reason == "password-invalid" ? "密码错误，遮罩仍然开启。" : "暂时无法解除遮罩，请重试。";
                    ok.Enabled = true;
                    password.Focus();
                }
            };
            form.Shown += (sender, args) =>
            {
                PrivacyMask.RaiseUnlockWindow(form.Handle);
                form.Activate();
                password.Focus();
            };
            form.HandleCreated += (sender, args) => windowReady(form.Handle);
            form.Show();
            // A worker started with SW_HIDE can have its first managed ShowWindow call suppressed.
            PrivacyMask.RaiseUnlockWindow(form.Handle);
            Application.Run(new ApplicationContext(form));
        }
    }

    internal static void OpenSettings(IWin32Window owner)
    {
        MaskPassword existing;
        try { existing = MaskPassword.Load(); }
        catch (Exception ex)
        {
            if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException))
                throw;
            MessageBox.Show(owner, "无法读取密码设置。请检查本机应用数据目录。", "密码设置");
            return;
        }

        using (var form = new PasswordDialog("密码与结束方式", false))
        {
            var current = existing == null ? null : form.AddPassword("当前密码");
            var replacement = form.AddPassword(existing == null ? "设置密码" : "新密码（留空则保留）");
            var confirmation = form.AddPassword("确认新密码");
            var continuous = new CheckBox { Text = "默认持续，输入密码后结束", AutoSize = true,
                Checked = existing == null || existing.DefaultContinuous };
            form.fields.Controls.Add(continuous);
            var feedback = new Label { Text = "取消勾选时，新会话默认到时结束。", AutoSize = true };
            form.fields.Controls.Add(feedback);

            FlowLayoutPanel buttons;
            var save = form.AddButtons("保存密码设置", out buttons);
            EventHandler apply = (sender, args) =>
            {
                bool disable = sender != save;
                try
                {
                    if (!disable && replacement.Text != confirmation.Text)
                        feedback.Text = "两次输入的密码不一致。";
                    else if (MaskPassword.Change(current == null ? "" : current.Text, replacement.Text, continuous.Checked, disable))
                        form.DialogResult = DialogResult.OK;
                    else
                        feedback.Text = "当前密码错误。";
                }
                catch (ArgumentException ex) { feedback.Text = ex.Message; }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException))
                        throw;
                    feedback.Text = "密码设置保存失败。";
                }
            };
            save.Click += apply;
            if (existing != null)
            {
                var disable = new Button { Text = "关闭密码保护", AutoSize = true };
                disable.Click += apply;
                buttons.Controls.Add(disable);
            }
            form.ShowDialog(owner);
        }
    }
}

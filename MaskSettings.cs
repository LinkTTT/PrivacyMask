using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class MaskSettings : Form
{
    internal static void Open(IWin32Window owner, MaskLanguage language, Action<MaskLanguage> onLanguageChanged)
    {
        bool recovered;
        using (var appearance = MaskAppearance.Load(out recovered))
        using (var form = new MaskSettings(appearance, recovered, language, onLanguageChanged))
            form.ShowDialog(owner);
    }

    private MaskSettings(MaskAppearance appearance, bool recovered, MaskLanguage language, Action<MaskLanguage> onLanguageChanged)
    {
        MaskUi.StyleWindow(this);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(604, 521);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 13), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        var title = MaskUi.Title();
        title.Margin = new Padding(0, 0, 0, 7);
        layout.Controls.Add(title, 0, 0);

        var appearanceCard = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = MaskUi.Surface, ColumnCount = 2,
            RowCount = 3, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12) };
        appearanceCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        appearanceCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        appearanceCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        appearanceCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        appearanceCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var previewLabel = MaskUi.Caption();
        var modeLabel = MaskUi.Caption();
        previewLabel.Font = modeLabel.Font = MaskUi.Subheading;
        appearanceCard.Controls.Add(previewLabel, 0, 0);
        appearanceCard.Controls.Add(modeLabel, 1, 0);
        var preview = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Margin = new Padding(0, 0, 15, 0) };
        preview.Paint += (sender, args) => appearance.Draw(args.Graphics, preview.ClientRectangle);
        appearanceCard.Controls.Add(preview, 0, 1);
        appearanceCard.SetRowSpan(preview, 2);

        var mode = new ComboBox { Dock = DockStyle.Top, Margin = Padding.Empty };
        MaskUi.StyleSelector(mode);
        appearanceCard.Controls.Add(mode, 1, 1);
        var picture = new Button { AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty };
        MaskUi.StyleButton(picture);
        var text = new TextBox { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Fill, MaxLength = MaskAppearance.TextLimit,
            Text = appearance.Text, Margin = Padding.Empty };
        var modeOptions = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 0) };
        modeOptions.Controls.Add(picture);
        modeOptions.Controls.Add(text);
        appearanceCard.Controls.Add(modeOptions, 1, 2);
        layout.Controls.Add(appearanceCard, 0, 1);

        var preferences = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = MaskUi.Surface, ColumnCount = 1,
            RowCount = 4, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 0, 0, 9) };
        preferences.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        preferences.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        preferences.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        preferences.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        preferences.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var languageRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var languageLabel = new Label { AutoSize = true, Font = MaskUi.Subheading, Margin = new Padding(0, 5, 13, 0) };
        var englishOption = new RadioButton { Text = "English", AutoSize = true, Checked = !language.IsChinese };
        var chineseOption = new RadioButton { Text = "简体中文", AutoSize = true, Checked = language.IsChinese };
        languageRow.Controls.AddRange(new Control[] { languageLabel, englishOption, chineseOption });
        preferences.Controls.Add(languageRow, 0, 0);

        bool startupEnabled;
        bool startupReadable = MaskStartup.TryRead(out startupEnabled);
        bool startupFailed = !startupReadable;
        var startup = new CheckBox { AutoCheck = false, Checked = startupEnabled, Enabled = startupReadable,
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        var startupDetail = MaskUi.Caption();
        startupDetail.Margin = new Padding(18, 0, 0, 0);
        preferences.Controls.Add(startup, 0, 1);
        preferences.Controls.Add(startupDetail, 0, 2);
        Action describeStartup = () =>
        {
            startupDetail.ForeColor = startupFailed ? MaskUi.WarningInk : MaskUi.Muted;
            startupDetail.Text = startupFailed ? language.Text("Could not access the Windows startup setting.", "无法访问 Windows 自启动设置。") :
                language.Text("Starts in the tray without opening a window. Saves immediately.", "静默启动到托盘，设置即时保存。");
        };
        startup.Click += (sender, args) =>
        {
            bool enable = !startup.Checked;
            startupFailed = !MaskStartup.TrySet(enable);
            if (!startupFailed)
                startup.Checked = enable;
            describeStartup();
        };

        var password = new Button { AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty };
        MaskUi.StyleButton(password);
        password.Click += (sender, args) => PasswordDialog.OpenSettings(this, language);
        preferences.Controls.Add(password, 0, 3);
        layout.Controls.Add(preferences, 0, 2);

        var hint = MaskUi.Caption();
        layout.Controls.Add(hint, 0, 3);
        string hintEnglish = recovered ? "Could not read the saved appearance. The mask will use black." : "Appearance changes apply the next time you start the mask.";
        string hintChinese = recovered ? "原外观设置无法读取，当前使用纯黑。" : "外观将在下次开启时生效。";
        Action<string, string> showHint = (english, chinese) =>
        {
            hintEnglish = english;
            hintChinese = chinese;
            hint.Text = language.Text(english, chinese);
        };

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var cancel = new Button { AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { AutoSize = true };
        MaskUi.StyleButton(cancel);
        MaskUi.StyleButton(save, primary: true);
        buttons.Controls.AddRange(new Control[] { save, cancel });
        layout.Controls.Add(buttons, 0, 4);
        CancelButton = cancel;
        Controls.Add(layout);

        Action refresh = () =>
        {
            if (mode.SelectedIndex < 0)
                return;
            appearance.Mode = (MaskAppearanceMode)mode.SelectedIndex;
            picture.Visible = appearance.Mode == MaskAppearanceMode.Picture;
            text.Visible = appearance.Mode == MaskAppearanceMode.Text;
            preview.Invalidate();
        };
        Action applyLanguage = () =>
        {
            Text = language.Text("Mask Settings", "遮罩设置");
            title.Text = language.Text("Settings", "设置");
            languageLabel.Text = language.Text("Language", "语言");
            startup.Text = language.Text("Start when I sign in to Windows", "随当前用户登录 Windows 启动");
            describeStartup();
            previewLabel.Text = language.Text("Preview", "预览");
            mode.AccessibleName = modeLabel.Text = language.Text("Mask appearance", "遮罩外观");
            mode.BeginUpdate();
            mode.Items.Clear();
            mode.Items.AddRange(new object[] { language.Text("Black", "纯黑"), language.Text("Custom Image", "自定义图片"),
                language.Text("Large Text", "大字提示") });
            mode.SelectedIndex = (int)appearance.Mode;
            mode.EndUpdate();
            preview.AccessibleName = language.Text("Mask preview", "遮罩预览");
            picture.Text = language.Text("Choose Image…", "选择图片…");
            text.AccessibleName = language.Text("Mask text", "遮罩大字提示");
            password.Text = language.Text("Password and End Mode…", "密码与结束方式…");
            hint.Text = language.Text(hintEnglish, hintChinese);
            cancel.Text = language.Text("Cancel", "取消");
            save.Text = language.Text("Save Appearance", "保存外观");
        };
        EventHandler chooseLanguage = (sender, args) =>
        {
            var option = (RadioButton)sender;
            MaskLanguage selected = option == chineseOption ? MaskLanguage.Chinese : MaskLanguage.English;
            if (!option.Checked || selected == language)
                return;
            try { selected.Save(); }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException))
                    throw;
                (language.IsChinese ? chineseOption : englishOption).Checked = true;
                showHint("Could not save the language preference.", "语言设置保存失败。");
                return;
            }
            language = selected;
            applyLanguage();
            onLanguageChanged(language);
        };
        englishOption.CheckedChanged += chooseLanguage;
        chineseOption.CheckedChanged += chooseLanguage;
        mode.SelectedIndexChanged += (sender, args) => refresh();
        text.TextChanged += (sender, args) => { appearance.Text = text.Text; preview.Invalidate(); };
        picture.Click += (sender, args) =>
        {
            using (var picker = new OpenFileDialog { Title = language.Text("Choose Mask Image", "选择遮罩图片"),
                Filter = language.Text("Images", "图片") + " (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    appearance.Import(picker.FileName);
                    showHint("The image will be centered on each screen without stretching.", "图片已载入，会按比例显示在每块屏幕上。");
                    preview.Invalidate();
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                        ex is ExternalException || ex is OutOfMemoryException))
                        throw;
                    showHint("Could not read the image. Choose a valid PNG, JPG, or BMP file.", "无法读取图片，请选择有效的 PNG、JPG 或 BMP 文件。");
                }
            }
        };
        save.Click += (sender, args) =>
        {
            if (appearance.Mode == MaskAppearanceMode.Picture && appearance.Picture == null)
                showHint("Choose an image first.", "请先选择一张图片。");
            else if (appearance.Mode == MaskAppearanceMode.Text && string.IsNullOrWhiteSpace(appearance.Text))
                showHint("Enter the text to display.", "请输入要显示的文字。");
            else
            {
                try { appearance.Save(); DialogResult = DialogResult.OK; }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ExternalException))
                        throw;
                    showHint("Could not save the appearance. Check the local app data folder.", "外观设置保存失败，请检查本机应用数据目录。");
                }
            }
        };
        applyLanguage();
    }
}

internal sealed class PasswordDialog : Form
{
    internal const int ActivateMessage = 0x8006;
    internal const int RaiseMessage = 0x8007;
    private readonly FlowLayoutPanel fields;

    private PasswordDialog(string title, bool topmost)
    {
        MaskUi.StyleWindow(this);
        Text = title;
        StartPosition = topmost ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = topmost;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        fields = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(19), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        Controls.Add(fields);
    }

    private TextBox AddPassword(string label)
    {
        fields.Controls.Add(new Label { Text = label, AutoSize = true, Font = MaskUi.Subheading, Margin = new Padding(3, 7, 3, 2) });
        var input = new TextBox { UseSystemPasswordChar = true, MaxLength = MaskPassword.LengthLimit, Width = 276, AccessibleName = label };
        fields.Controls.Add(input);
        return input;
    }

    private Button AddButtons(string submit, MaskLanguage language, out FlowLayoutPanel buttons)
    {
        var ok = new Button { Text = submit, AutoSize = true };
        var cancel = new Button { Text = language.Text("Cancel", "取消"), DialogResult = DialogResult.Cancel, AutoSize = true };
        MaskUi.StyleButton(ok, primary: true);
        MaskUi.StyleButton(cancel);
        buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
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
        MaskLanguage language = MaskLanguage.Load();
        using (var form = new PasswordDialog(language.UnlockTitle, true))
        {
            // Expose this separate window to capture and window pickers. The password box masks its value.
            form.ShowInTaskbar = true;
            var password = form.AddPassword(language.Text("Enter the mask password", "请输入遮罩密码"));
            var feedback = new Label { AutoSize = true };
            form.fields.Controls.Add(feedback);
            FlowLayoutPanel buttons;
            var ok = form.AddButtons(language.UnlockTitle, language, out buttons);
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
                    feedback.Text = reason == "password-invalid" ? language.Text("Incorrect password. The mask stays active.", "密码错误，遮罩仍然开启。") :
                        language.Text("Could not unlock the mask. Try again.", "暂时无法解除遮罩，请重试。");
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

    internal static void OpenSettings(IWin32Window owner, MaskLanguage language)
    {
        MaskPassword existing;
        try { existing = MaskPassword.Load(); }
        catch (Exception ex)
        {
            if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException))
                throw;
            MessageBox.Show(owner, language.Text("Could not read the password settings. Check the local app data folder.", "无法读取密码设置。请检查本机应用数据目录。"),
                language.Text("Password Settings", "密码设置"));
            return;
        }

        using (var form = new PasswordDialog(language.Text("Password and End Mode", "密码与结束方式"), false))
        {
            var current = existing == null ? null : form.AddPassword(language.Text("Current password", "当前密码"));
            var replacement = form.AddPassword(existing == null ? language.Text("Set password", "设置密码") :
                language.Text("New password (optional)", "新密码（留空则保留）"));
            var confirmation = form.AddPassword(language.Text("Confirm new password", "确认新密码"));
            var continuous = new CheckBox { Text = language.Text("Keep the mask active until unlocked by default", "默认持续，输入密码后结束"), AutoSize = true,
                Checked = existing == null || existing.DefaultContinuous };
            form.fields.Controls.Add(continuous);
            var feedback = new Label { Text = language.Text("Clear this option to use timed sessions by default.", "取消勾选时，新会话默认到时结束。"), AutoSize = true };
            form.fields.Controls.Add(feedback);

            FlowLayoutPanel buttons;
            var save = form.AddButtons(language.Text("Save Password Settings", "保存密码设置"), language, out buttons);
            EventHandler apply = (sender, args) =>
            {
                bool disable = sender != save;
                try
                {
                    if (!disable && replacement.Text != confirmation.Text)
                        feedback.Text = language.Text("The passwords do not match.", "两次输入的密码不一致。");
                    else if (MaskPassword.Change(current == null ? "" : current.Text, replacement.Text, continuous.Checked, disable))
                        form.DialogResult = DialogResult.OK;
                    else
                        feedback.Text = language.Text("The current password is incorrect.", "当前密码错误。");
                }
                catch (ArgumentException) { feedback.Text = language.Text("Use a password with 4 to 128 characters.", "请使用 4 到 128 个字符的密码。"); }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException))
                        throw;
                    feedback.Text = language.Text("Could not save the password settings.", "密码设置保存失败。");
                }
            };
            save.Click += apply;
            if (existing != null)
            {
                var disable = new Button { Text = language.Text("Disable Password Protection", "关闭密码保护"), AutoSize = true };
                MaskUi.StyleButton(disable);
                disable.Click += apply;
                buttons.Controls.Add(disable);
            }
            form.ShowDialog(owner);
        }
    }
}

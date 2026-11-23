using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MaskPanel : Form
{
    private readonly Label status = new Label();
    private readonly Label feedback = new Label();
    private readonly ComboBox duration = new ComboBox();
    private readonly Button start = new Button();
    private readonly Button stop = new Button();
    private readonly Button settings = new Button();
    private readonly Label shortcut = new Label();
    private readonly Timer refresh = new Timer { Interval = 1000 };
    private readonly int[] durations = { 300, MaskControl.DefaultSeconds, 1800 };
    private bool changing;
    private Task<MaskStatus> pendingStatus;
    private MaskLanguage language;

    internal static void Open()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var panel = new MaskPanel())
            Application.Run(panel);
    }

    private MaskPanel()
    {
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7, 15);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(460, 235);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        status.Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(status, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = false };
        duration.DropDownStyle = ComboBoxStyle.DropDownList;
        duration.Width = 172;
        duration.Margin = new Padding(0, 8, 10, 0);
        start.Enabled = stop.Enabled = false;
        start.Size = stop.Size = new Size(104, 36);
        actions.Controls.AddRange(new Control[] { duration, start, stop });
        layout.Controls.Add(actions, 0, 1);

        feedback.Dock = DockStyle.Fill;
        layout.Controls.Add(feedback, 0, 2);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        settings.AutoSize = true;
        settings.Enabled = false;
        shortcut.AutoSize = true;
        shortcut.Margin = new Padding(8, 9, 0, 0);
        bottom.Controls.AddRange(new Control[] { settings, shortcut });
        layout.Controls.Add(bottom, 0, 3);
        Controls.Add(layout);
        ApplyLanguage(MaskLanguage.Load());

        start.Click += async (sender, args) => await UpdateStatus("Start");
        stop.Click += async (sender, args) => await UpdateStatus("Stop");
        settings.Click += async (sender, args) =>
        {
            refresh.Stop();
            try
            {
                MaskSettings.Open(this, language, ApplyLanguage);
                ConfigureDurations();
            }
            finally { refresh.Start(); }
            await UpdateStatus("Status");
        };
        refresh.Tick += async (sender, args) => await UpdateStatus("Status");
        Shown += async (sender, args) => { refresh.Start(); await UpdateStatus("Status"); };
        FormClosed += (sender, args) => refresh.Dispose();
    }

    private void ApplyLanguage(MaskLanguage selected)
    {
        language = selected;
        Text = language.Text("PrivacyMask", "PrivacyMask 屏幕遮罩");
        duration.AccessibleName = language.Text("Mask duration", "遮罩时长");
        start.Text = language.Text("Start Mask", "开始遮罩");
        stop.Text = language.Text("Stop Mask", "停止遮罩");
        settings.Text = language.Text("Settings…", "设置…");
        shortcut.Text = language.Text("Unlock shortcut", "解除快捷键") + "  Ctrl + Alt + Shift + Esc";
        status.Text = language.Text("Checking mask status…", "正在确认遮罩状态…");
        feedback.Text = "";
        ConfigureDurations();
    }

    private void ConfigureDurations()
    {
        duration.Items.Clear();
        foreach (int seconds in durations)
            duration.Items.Add(string.Format(language.Text("{0} minutes", "{0} 分钟，自动结束"), seconds / 60));
        duration.SelectedIndex = 1;
        try
        {
            var password = MaskPassword.Load();
            if (password != null)
            {
                duration.Items.Add(language.Text("Until unlocked", "持续，输入密码后结束"));
                if (password.DefaultContinuous)
                    duration.SelectedIndex = durations.Length;
            }
        }
        catch (Exception ex)
        {
            if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException))
                throw;
            feedback.Text = language.Text("Could not read password settings.", "无法读取密码设置。");
        }
    }

    private async Task UpdateStatus(string action)
    {
        bool isStatus = action == "Status";
        if (changing || (isStatus && pendingStatus != null))
            return;
        int seconds = duration.SelectedIndex == durations.Length ? 0 : durations[duration.SelectedIndex];
        if (!isStatus)
        {
            changing = true;
            start.Enabled = stop.Enabled = settings.Enabled = false;
            status.Text = action == "Start" ? language.Text("Starting mask…", "正在开启遮罩…") :
                language.Text("Stopping mask…", "正在停止遮罩…");
            feedback.Text = "";
        }

        try
        {
            if (!isStatus && pendingStatus != null)
                await pendingStatus;
            if (IsDisposed)
                return;
            var operation = Task.Run(() => MaskControl.Execute(action, seconds));
            if (isStatus)
                pendingStatus = operation;
            MaskStatus current = await operation;
            if (IsDisposed || (isStatus && changing))
                return;
            if (action == "Stop" && current.Reason == "password-required")
            {
                status.Text = language.Text("Mask active", "遮罩已开启");
                current = await Task.Run(() => MaskControl.Execute("Prompt", seconds));
                if (IsDisposed)
                    return;
            }
            if (current.State == "active")
            {
                status.Text = language.Text("Mask active · Until unlocked", "遮罩已开启 · 持续");
                if (current.ExpiresAt.HasValue)
                {
                    TimeSpan left = current.ExpiresAt.Value - DateTimeOffset.UtcNow;
                    int remaining = Math.Max(0, (int)Math.Ceiling(left.TotalSeconds));
                    status.Text = string.Format(language.Text("Mask active   {0:00}:{1:00}", "遮罩已开启   {0:00}:{1:00}"),
                        remaining / 60, remaining % 60);
                }
            }
            else
            {
                status.Text = current.State == "inactive" ? language.Text("Mask inactive", "遮罩未开启") :
                    language.Text("Mask status unavailable", "无法确认遮罩状态");
            }
            if (!isStatus || current.State != "inactive")
                feedback.Text = current.PasswordProtected ? language.Text("A password is required to stop the mask.", "手动停止需要密码。") :
                    Explain(current.Reason);
            duration.Enabled = start.Enabled = current.State == "inactive";
            settings.Enabled = current.State == "inactive";
            stop.Enabled = current.State != "inactive" && current.Reason != "desktop-unavailable" && current.Reason != "legacy-overlay";
        }
        finally
        {
            if (isStatus)
                pendingStatus = null;
            else
                changing = false;
        }
    }

    private string Explain(string reason)
    {
        switch (reason)
        {
            case null: return "";
            case "desktop-unavailable": return language.Text("Use PrivacyMask on the unlocked physical Windows desktop.", "请在 Windows 当前解锁的物理桌面中运行。");
            case "hotkey-conflict": return language.Text("The unlock shortcut is already in use. Free it and try again.", "退出快捷键已被占用。释放快捷键后重试。");
            case "legacy-overlay": return language.Text("An older mask is running. Use the unlock shortcut first.", "检测到旧版本遮罩。请先使用紧急退出键。");
            case "capture-control": return language.Text("A capture test is running.", "当前正在运行截图对照测试。");
            case "overlay-unverified": return language.Text("Checking whether the mask still covers other windows.", "正在重新确认遮罩是否盖住其他窗口。");
            case "password-not-set": return language.Text("Set a password before selecting Until unlocked.", "请先设置密码，再选择持续遮罩。");
            case "password-settings-invalid": return language.Text("Could not read the password settings. Check the local app data folder.", "密码设置无法读取，请检查本机应用数据目录。");
            default: return language.Text("Check the local PrivacyMaskPoC logs before continuing.", "请查看本机 PrivacyMaskPoC 日志，确认原因后再继续。");
        }
    }
}

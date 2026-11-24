using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MaskPanel : Form
{
    private readonly Label status = MaskUi.Title();
    private readonly Label feedback = MaskUi.Caption();
    private readonly Label scopeLabel = MaskUi.Caption();
    private readonly Label durationLabel = MaskUi.Caption();
    private readonly Label productDescription = MaskUi.Caption();
    private readonly TableLayoutPanel statusCard = new TableLayoutPanel();
    private readonly Panel statusMark = new Panel();
    private readonly ComboBox duration = new ComboBox();
    private readonly ComboBox display = new ComboBox();
    private readonly Button start = new Button();
    private readonly Button stop = new Button();
    private readonly Button settings = new Button();
    private readonly Button shortcuts = new Button();
    private readonly Label shortcut = MaskUi.Caption();
    private readonly Timer refresh = new Timer { Interval = 1000 };
    private readonly int[] durations = { 300, MaskControl.DefaultSeconds, 1800 };
    private bool changing;
    private string previousState;
    private Task<MaskStatus> pendingStatus;
    private MaskLanguage language;

    internal bool IsChanging { get { return changing; } }

    internal MaskPanel(MaskLanguage selected, Action<MaskLanguage> languageChanged, Action configureShortcuts)
    {
        MaskUi.StyleWindow(this);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(540, 393);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 21, 24, 21), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 113));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        var mark = new PictureBox { Dock = DockStyle.Fill, Image = MaskUi.BrandMark, SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 12, 0), TabStop = false };
        brand.Controls.Add(mark, 0, 0);
        brand.SetRowSpan(mark, 2);
        var product = MaskUi.Title();
        product.Text = "PrivacyMask";
        brand.Controls.Add(product, 1, 0);
        brand.Controls.Add(productDescription, 1, 1);
        layout.Controls.Add(brand, 0, 0);

        statusCard.Dock = DockStyle.Fill;
        statusCard.Margin = new Padding(0, 0, 0, 15);
        statusCard.ColumnCount = 2;
        statusCard.RowCount = 2;
        statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 4));
        statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        statusCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusMark.Dock = DockStyle.Fill;
        statusMark.Margin = Padding.Empty;
        statusCard.Controls.Add(statusMark, 0, 0);
        statusCard.SetRowSpan(statusMark, 2);
        status.Margin = new Padding(15, 1, 15, 0);
        feedback.Margin = new Padding(15, 0, 15, 6);
        statusCard.Controls.Add(status, 1, 0);
        statusCard.Controls.Add(feedback, 1, 1);
        layout.Controls.Add(statusCard, 0, 1);

        var selection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        selection.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        selection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        scopeLabel.Font = durationLabel.Font = MaskUi.Subheading;
        selection.Controls.Add(scopeLabel, 0, 0);
        selection.Controls.Add(durationLabel, 1, 0);
        MaskUi.StyleSelector(display);
        MaskUi.StyleSelector(duration);
        display.DisplayMember = "Value";
        display.Dock = duration.Dock = DockStyle.Top;
        display.Margin = new Padding(0, 0, 12, 0);
        duration.Margin = Padding.Empty;
        display.Enabled = false;
        display.SelectedIndexChanged += (sender, args) => start.Enabled = display.Enabled && display.SelectedIndex >= 0;
        selection.Controls.Add(display, 0, 1);
        selection.Controls.Add(duration, 1, 1);
        layout.Controls.Add(selection, 0, 2);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        MaskUi.StyleButton(start, primary: true);
        MaskUi.StyleButton(stop);
        start.Enabled = stop.Enabled = false;
        start.Dock = stop.Dock = DockStyle.Fill;
        start.Margin = new Padding(0, 0, 12, 14);
        stop.Margin = new Padding(0, 0, 0, 14);
        actions.Controls.Add(start, 0, 0);
        actions.Controls.Add(stop, 1, 0);
        layout.Controls.Add(actions, 0, 3);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        MaskUi.StyleButton(settings);
        MaskUi.StyleButton(shortcuts);
        settings.AutoSize = true;
        settings.Enabled = false;
        shortcuts.AutoSize = true;
        shortcut.AutoSize = true;
        shortcut.Dock = DockStyle.None;
        shortcut.Margin = new Padding(10, 11, 0, 0);
        bottom.Controls.AddRange(new Control[] { settings, shortcuts, shortcut });
        layout.Controls.Add(bottom, 0, 4);
        Controls.Add(layout);
        ApplyLanguage(selected);

        start.Click += async (sender, args) => await UpdateStatus("Start");
        stop.Click += async (sender, args) => await UpdateStatus("Stop");
        settings.Click += async (sender, args) =>
        {
            refresh.Stop();
            try
            {
                MaskSettings.Open(this, language, updated =>
                {
                    ApplyLanguage(updated);
                    languageChanged(updated);
                });
                ConfigureDurations();
            }
            finally { if (Visible) refresh.Start(); }
            await UpdateStatus("Status");
        };
        shortcuts.Click += (sender, args) => configureShortcuts();
        refresh.Tick += async (sender, args) => await UpdateStatus("Status");
        VisibleChanged += async (sender, args) =>
        {
            if (Visible)
            {
                refresh.Start();
                await UpdateStatus("Status");
            }
            else
                refresh.Stop();
        };
        FormClosing += (sender, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                Hide();
            }
        };
    }

    private void ApplyLanguage(MaskLanguage selected)
    {
        language = selected;
        Text = language.Text("PrivacyMask", "PrivacyMask 屏幕遮罩");
        productDescription.Text = language.Text("Screen masking", "屏幕遮罩");
        display.AccessibleName = scopeLabel.Text = language.Text("Display to mask", "遮罩范围");
        duration.AccessibleName = durationLabel.Text = language.Text("End mode", "结束方式");
        start.Text = language.Text("Start Mask", "开始遮罩");
        stop.Text = language.Text("Stop Mask", "停止遮罩");
        settings.Text = language.Text("Settings…", "设置…");
        shortcuts.Text = language.Text("Shortcuts…", "快捷键…");
        shortcut.Text = language.Text("Unlock", "解除遮罩") + "   Ctrl+Alt+Shift+Esc";
        status.Text = language.Text("Checking mask status…", "正在确认遮罩状态…");
        feedback.Text = "";
        SetStatusColor("checking");
        ConfigureDisplays();
        ConfigureDurations();
    }

    protected override void WndProc(ref Message message)
    {
        const int displayChange = 0x007E;
        base.WndProc(ref message);
        if (message.Msg == displayChange && IsHandleCreated && !IsDisposed)
            BeginInvoke(new Action(() => ConfigureDisplays()));
    }

    private int? SelectedDisplay
    {
        get { return display.SelectedIndex < 0 ? (int?)null : ((KeyValuePair<int, string>)display.SelectedItem).Key; }
    }

    internal static string DisplayName(MaskLanguage language, int number)
    {
        return number == 0 ? language.Text("All displays", "所有显示器") :
            string.Format(language.Text("Display {0}", "显示器 {0}"), number);
    }

    private void ConfigureDisplays(int? activeDisplay = null)
    {
        int? selected = activeDisplay ?? (display.Items.Count == 0 ? 0 : SelectedDisplay);
        display.Items.Clear();
        display.Items.Add(new KeyValuePair<int, string>(0, DisplayName(language, 0)));
        int selectedIndex = selected == 0 ? 0 : -1;
        foreach (Screen screen in Screen.AllScreens)
        {
            int number = PrivacyMask.GetDisplayNumber(screen);
            if (number == 0)
                continue;
            string name = string.Format("{0} · {1} × {2}", DisplayName(language, number), screen.Bounds.Width, screen.Bounds.Height);
            if (screen.Primary)
                name += language.Text(" (primary)", "（主屏）");
            int index = display.Items.Add(new KeyValuePair<int, string>(number, name));
            if (number == selected)
                selectedIndex = index;
        }
        display.SelectedIndex = selectedIndex;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            refresh.Dispose();
        base.Dispose(disposing);
    }

    internal async Task<string> UpdateStatus(string action, int? targetDisplay = null)
    {
        bool isStatus = action == "Status";
        if (changing || (isStatus && pendingStatus != null))
            return null;
        int? selectedDisplay = targetDisplay ?? SelectedDisplay;
        if (action == "Start" && !selectedDisplay.HasValue)
            return null;
        int seconds = duration.SelectedIndex == durations.Length ? 0 : durations[duration.SelectedIndex];
        if (!isStatus)
        {
            changing = true;
            display.Enabled = start.Enabled = stop.Enabled = settings.Enabled = false;
            status.Text = action != "Stop" ? language.Text("Starting mask…", "正在开启遮罩…") :
                language.Text("Stopping mask…", "正在停止遮罩…");
            feedback.Text = "";
            SetStatusColor("checking");
        }

        try
        {
            if (!isStatus && pendingStatus != null)
                await pendingStatus;
            if (IsDisposed)
                return null;
            var operation = Task.Run(() => MaskControl.Execute(action, seconds, displayNumber: selectedDisplay.GetValueOrDefault()));
            if (isStatus)
                pendingStatus = operation;
            MaskStatus current = await operation;
            if (IsDisposed || (isStatus && changing))
                return null;
            if (action == "Stop" && current.Reason == "password-required")
            {
                status.Text = language.Text("Mask active", "遮罩已开启");
                current = await Task.Run(() => MaskControl.Execute("Prompt", seconds));
                if (IsDisposed)
                    return null;
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
            if (current.State == "active")
            {
                if (SelectedDisplay != current.DisplayNumber)
                    ConfigureDisplays(current.DisplayNumber);
                feedback.Text = string.Format(language.Text("Covered display: {0}", "遮罩范围为{0}"), DisplayName(language, current.DisplayNumber));
                if (current.PasswordProtected)
                    feedback.Text += Environment.NewLine + language.Text("A password is required to stop the mask.", "手动停止需要密码。");
            }
            else if (!isStatus || current.State != "inactive" || previousState == "active")
                feedback.Text = Explain(current.Reason);
            if (current.State == "inactive" && feedback.Text.Length == 0)
                feedback.Text = language.Text("Close this window to keep PrivacyMask in the tray.", "关闭面板后，PrivacyMask 会留在系统托盘。");
            SetStatusColor(current.State);
            previousState = current.State;
            if (current.Reason == "display-unavailable")
                ConfigureDisplays();
            display.Enabled = duration.Enabled = current.State == "inactive";
            start.Enabled = display.Enabled && display.SelectedIndex >= 0;
            settings.Enabled = current.State == "inactive";
            stop.Enabled = current.State != "inactive" && current.Reason != "desktop-unavailable" && current.Reason != "legacy-overlay";
            return current.State == "unavailable" ? Explain(current.Reason) : null;
        }
        finally
        {
            if (isStatus)
                pendingStatus = null;
            else
                changing = false;
        }
    }

    private void SetStatusColor(string state)
    {
        statusCard.BackColor = state == "active" ? MaskUi.ActiveFill : MaskUi.Surface;
        status.ForeColor = state == "active" ? MaskUi.ActiveInk : state == "unavailable" ? MaskUi.WarningInk : MaskUi.Ink;
        statusMark.BackColor = state == "active" ? MaskUi.ActiveInk : state == "unavailable" ? MaskUi.WarningInk : MaskUi.Accent;
    }

    private string Explain(string reason)
    {
        switch (reason)
        {
            case null: return "";
            case "desktop-unavailable": return language.Text("Use PrivacyMask on the unlocked physical Windows desktop.", "请在 Windows 当前解锁的物理桌面中运行。");
            case "display-unavailable": return language.Text("That display is no longer available. Select a connected display.", "所选显示器已不可用。请选择已连接的显示器。");
            case "mask-scope-mismatch": return language.Text("A mask is already active on a different display selection.", "已有遮罩运行，范围与本次选择不同。");
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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class MaskTray : ApplicationContext
{
    private readonly MaskPanel panel;
    private readonly TrayWindow window;
    private readonly NotifyIcon icon = new NotifyIcon();
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly ToolStripMenuItem open = new ToolStripMenuItem();
    private readonly ToolStripMenuItem stop = new ToolStripMenuItem();
    private readonly ToolStripMenuItem configure = new ToolStripMenuItem();
    private readonly ToolStripMenuItem exit = new ToolStripMenuItem();
    private readonly ToolStripMenuItem[] start = new ToolStripMenuItem[MaskHotkeys.ScopeCount];
    private readonly HashSet<int> registered = new HashSet<int>();
    private int[] bindings = MaskHotkeys.Load();
    private MaskLanguage language = MaskLanguage.Load();
    private MaskShortcutDialog shortcutDialog;

    internal static void Run(bool showPanel)
    {
        using (var guard = MaskControl.OpenMutex(@"Local\" + MaskControl.TrayName))
        {
            var wait = Stopwatch.StartNew();
            while (!MaskControl.TryAcquire(guard))
            {
                if (!showPanel || TrayWindow.RequestOpen())
                    return;
                if (wait.ElapsedMilliseconds >= 3000)
                    throw new InvalidOperationException(MaskLanguage.Load().Text(
                        "The running PrivacyMask tray did not respond.", "已运行的 PrivacyMask 托盘没有响应。"));
                Thread.Sleep(40);
            }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var tray = new MaskTray())
                {
                    if (showPanel)
                    {
                        tray.ShowPanel();
                        string warning = tray.RegistrationWarning();
                        if (warning != null)
                            tray.Notify(warning);
                    }
                    Application.Run(tray);
                }
            }
            finally { guard.ReleaseMutex(); }
        }
    }

    private MaskTray()
    {
        panel = new MaskPanel(language, ApplyLanguage, ConfigureShortcuts);
        panel.FormClosed += (sender, args) => ExitThread();
        window = new TrayWindow(ShowPanel, OnHotkey);
        foreach (int binding in bindings)
            if (binding != 0 && MaskHotkeys.Register(window.Handle, binding))
                registered.Add(binding);

        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        for (int scope = 0; scope < start.Length; scope++)
        {
            int target = scope;
            start[scope] = new ToolStripMenuItem();
            start[scope].Click += (sender, args) => StartMask(target);
            menu.Items.Add(start[scope]);
        }
        menu.Items.Add(stop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(configure);
        menu.Items.Add(exit);
        menu.Opening += (sender, args) =>
        {
            foreach (var item in start)
                item.Enabled = !panel.IsChanging;
            stop.Enabled = exit.Enabled = !panel.IsChanging;
        };

        open.Click += (sender, args) => ShowPanel();
        stop.Click += async (sender, args) =>
        {
            string error = await panel.UpdateStatus("Stop");
            if (error != null)
                Notify(error);
        };
        configure.Click += (sender, args) => ConfigureShortcuts();
        exit.Click += (sender, args) =>
        {
            if (!panel.IsChanging)
                ExitThread();
        };

        ApplyLanguage(language);
        icon.Icon = MaskUi.AppIcon;
        icon.ContextMenuStrip = menu;
        icon.MouseDoubleClick += (sender, args) =>
        {
            if (args.Button == MouseButtons.Left)
                ShowPanel();
        };
        icon.Visible = true;
    }

    private void ShowPanel()
    {
        if (shortcutDialog != null)
        {
            shortcutDialog.Activate();
            return;
        }
        panel.Show();
        if (panel.WindowState == FormWindowState.Minimized)
            panel.WindowState = FormWindowState.Normal;
        panel.Activate();
    }

    private void ConfigureShortcuts()
    {
        ShowPanel();
        if (shortcutDialog != null)
            return;
        using (var dialog = new MaskShortcutDialog(language, bindings, RegistrationWarning(), ApplyBindings))
        {
            shortcutDialog = dialog;
            try { dialog.ShowDialog(panel); }
            finally { shortcutDialog = null; }
        }
    }

    private string ApplyBindings(int[] proposed)
    {
        var wanted = new HashSet<int>();
        foreach (int binding in proposed)
        {
            if (!MaskHotkeys.IsValid(binding))
                return language.Text("Each enabled shortcut needs Ctrl, Alt, or Win.", "已启用的快捷键至少需要 Ctrl、Alt 或 Win 中的一个。");
            if (binding != 0 && !wanted.Add(binding))
                return language.Text("Choose a different shortcut for each display scope.", "请为每种遮罩范围设置不同的快捷键。");
        }

        // Keep existing keys live until every new registration and the save have succeeded.
        var added = new List<int>();
        try
        {
            foreach (int binding in proposed)
            {
                if (binding == 0 || registered.Contains(binding))
                    continue;
                if (!MaskHotkeys.Register(window.Handle, binding))
                    return string.Format(language.Text("{0} is unavailable. Choose another shortcut.", "{0} 无法注册，请换一个快捷键。"),
                        MaskHotkeys.Describe(binding));
                added.Add(binding);
            }
            try { MaskHotkeys.Save(proposed); }
            catch (IOException) { return SaveError(); }
            catch (UnauthorizedAccessException) { return SaveError(); }

            foreach (int binding in registered)
                if (!wanted.Contains(binding))
                    MaskHotkeys.UnregisterHotKey(window.Handle, binding);
            registered.Clear();
            registered.UnionWith(wanted);
            bindings = proposed;
            added.Clear();
            ApplyLanguage(language);
            return null;
        }
        finally
        {
            foreach (int binding in added)
                MaskHotkeys.UnregisterHotKey(window.Handle, binding);
        }
    }

    private string SaveError()
    {
        return language.Text("Could not save shortcuts. Check the local app data folder.", "无法保存快捷键，请检查本机应用数据目录。");
    }

    private string RegistrationWarning()
    {
        var unavailable = new List<string>();
        foreach (int binding in bindings)
            if (binding != 0 && !registered.Contains(binding))
                unavailable.Add(MaskHotkeys.Describe(binding));
        return unavailable.Count == 0 ? null : string.Format(language.Text(
            "Unavailable shortcuts: {0}. Change them or free the keys and save again.",
            "这些快捷键无法注册：{0}。请更换，或释放按键后重新保存。"), string.Join(", ", unavailable));
    }

    private void ApplyLanguage(MaskLanguage selected)
    {
        language = selected;
        open.Text = language.Text("Open PrivacyMask", "打开 PrivacyMask");
        stop.Text = language.Text("Stop or Unlock Mask", "停止或解除遮罩");
        configure.Text = language.Text("Global Shortcuts…", "全局快捷键…");
        exit.Text = language.Text("Exit PrivacyMask", "退出 PrivacyMask");
        for (int scope = 0; scope < start.Length; scope++)
        {
            start[scope].Text = MaskPanel.DisplayName(language, scope);
            int binding = bindings[scope];
            start[scope].ShortcutKeyDisplayString = registered.Contains(binding) ? MaskHotkeys.Describe(binding) : "";
        }
        icon.Text = RegistrationWarning() == null ? "PrivacyMask" :
            language.Text("PrivacyMask · Shortcuts unavailable", "PrivacyMask · 部分快捷键不可用");
    }

    private void OnHotkey(int binding)
    {
        int scope = Array.IndexOf(bindings, binding);
        if (scope >= 0 && registered.Contains(binding))
            StartMask(scope);
    }

    private async void StartMask(int scope)
    {
        string error = await panel.UpdateStatus("Default", scope);
        if (error != null)
            Notify(error);
    }

    private void Notify(string message)
    {
        icon.ShowBalloonTip(4500, "PrivacyMask", message, ToolTipIcon.Warning);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            icon.Dispose();
            menu.Dispose();
            foreach (int binding in registered)
                MaskHotkeys.UnregisterHotKey(window.Handle, binding);
            window.DestroyHandle();
            panel.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class TrayWindow : NativeWindow
    {
        private const int WmHotkey = 0x0312;
        private const int WmOpenPanel = 0x8001;
        private readonly Action openPanel;
        private readonly Action<int> hotkey;

        internal TrayWindow(Action openPanel, Action<int> hotkey)
        {
            this.openPanel = openPanel;
            this.hotkey = hotkey;
            CreateHandle(new CreateParams { Caption = MaskControl.TrayName });
        }

        internal static bool RequestOpen()
        {
            IntPtr hwnd = FindWindow(null, MaskControl.TrayName);
            if (hwnd == IntPtr.Zero)
                return false;
            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            MaskControl.AllowSetForegroundWindow(processId);
            if (PostMessage(hwnd, WmOpenPanel, IntPtr.Zero, IntPtr.Zero))
                return true;
            const int invalidWindowHandle = 1400;
            int error = Marshal.GetLastWin32Error();
            if (error == invalidWindowHandle)
                return false;
            throw new Win32Exception(error);
        }

        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case WmOpenPanel: openPanel(); return;
                case WmHotkey: hotkey(message.WParam.ToInt32()); return;
                default: base.WndProc(ref message); return;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }
}

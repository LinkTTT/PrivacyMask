using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class PrivacyMask
{
    private const uint WdaNone = 0;
    private const uint WdaExcludeFromCapture = 0x11;
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint LwaAlpha = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmPaint = 0x000F;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmTimer = 0x0113;
    private const uint WmHotKey = 0x0312;
    private const uint WmSessionChange = 0x02B1;
    private const uint WmCheckOverlay = 0x8001;
    private const uint WmStopOverlay = 0x8002;
    private const uint WmControlFailed = 0x8003;
    private const uint WmOpenUnlock = 0x8004;
    private const uint WmRefreshCover = 0x8005;
    private const uint EventSystemForeground = 0x0003;
    private const uint GwPreviousWindow = 3;
    private const uint DwmCloaked = 14;
    private const int HotKeyId = 1;
    private const int GwlExStyle = -20;
    private const uint VkEscape = 0x1B;
    private const int MaNoActivate = 3, BlackBrush = 4;
    private const int SmXVirtualScreen = 76, SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;
    private static readonly UIntPtr TimerId = new UIntPtr(1);
    private static readonly IntPtr Topmost = new IntPtr(-1);
    private static readonly WindowProc Proc = HandleWindowMessage;
    private static StreamWriter log;
    private static string exitReason = "window-closed";
    private static DateTimeOffset? expiresAt;
    private static Stopwatch elapsed;
    private static int durationSeconds;
    private static int displayNumber;
    private static uint affinity;
    private static bool ending;
    private static MaskAppearance appearance;
    private static Rectangle[] displays;
    private static MaskPassword password;
    private static int stopAuthorized;
    private static int unlockOpen;
    private static IntPtr unlockWindow;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--control")
            return MaskControl.RunCommand(args);

        bool background = args.Length > 0 && args[0] == "--worker";
        bool trayOnly = args.Length == 1 && args[0] == MaskStartup.TrayArgument;
        string logPath = null;
        try
        {
            if (args.Length == 0 || trayOnly)
            {
                Require(SetProcessDpiAwarenessContext(new IntPtr(-4)), "SetProcessDpiAwarenessContext");
                MaskTray.Run(showPanel: args.Length == 0);
                return 0;
            }

            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrivacyMaskPoC");
            Directory.CreateDirectory(directory);
            int pid = Process.GetCurrentProcess().Id;
            logPath = Path.Combine(directory, "PrivacyMask-" + pid + ".log");
            using (log = new StreamWriter(logPath, false) { AutoFlush = true })
            {
                try
                {
                    using (var guard = MaskControl.OpenWorkerMutex())
                    {
                        if (!MaskControl.TryAcquire(guard))
                        {
                            Log("EXIT reason=already-running");
                            return 2;
                        }
                        try
                        {
                            bool recovered;
                            using (var current = MaskAppearance.Load(out recovered))
                            {
                                appearance = current;
                                if (recovered)
                                    Log("APPEARANCE fallback=black");
                                Run(args, background ? 1 : 0);
                            }
                        }
                        finally { guard.ReleaseMutex(); }
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    Log("ERROR " + ex.Message);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            if (!background && !trayOnly)
            {
                MaskLanguage language = MaskLanguage.Load();
                MessageBox(IntPtr.Zero, ex.Message + "\n\n" + language.Text("Log: ", "日志：") +
                    (logPath ?? language.Text("unavailable", "不可用")), "PrivacyMask", 0x10);
            }
            return 1;
        }
    }

    private static void Run(string[] args, int firstArgument)
    {
        const string usage = "Usage: PrivacyMask.exe [--seconds 1..3600 [--capture-control] | --until-unlocked] [--display number]";
        int seconds = 0;
        bool captureControl = false;
        bool continuous = false;
        for (int i = firstArgument; i < args.Length; i++)
        {
            if (args[i] == "--seconds" && seconds == 0 && i + 1 < args.Length &&
                int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) &&
                seconds >= 1 && seconds <= 3600)
                continue;
            if (args[i] == "--capture-control" && !captureControl)
            {
                captureControl = true;
                continue;
            }
            if (args[i] == "--until-unlocked" && !continuous)
            {
                continuous = true;
                continue;
            }
            if (args[i] == "--display" && displayNumber == 0 && i + 1 < args.Length &&
                int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out displayNumber) && displayNumber > 0)
                continue;
            throw new ArgumentException(usage);
        }
        if (continuous ? seconds != 0 || captureControl : seconds == 0)
            throw new ArgumentException(usage);
        password = MaskPassword.Load();
        if (continuous && password == null)
            throw new InvalidOperationException("password-not-set");

        var version = new OsVersion { Size = (uint)Marshal.SizeOf(typeof(OsVersion)) };
        if (RtlGetVersion(ref version) != 0 || version.Major < 10 || version.Build < 19041)
            throw new InvalidOperationException("Windows 10 version 2004 (build 19041) or later is required.");
        Log(string.Format("START Windows {0}.{1}.{2}; timeoutSeconds={3}; captureControl={4}; display={5}",
            version.Major, version.Minor, version.Build, seconds, captureControl, displayNumber));
        Require(SetProcessDpiAwarenessContext(new IntPtr(-4)), "SetProcessDpiAwarenessContext");
        if (!MaskControl.IsInteractiveConsole())
            throw new InvalidOperationException("desktop-unavailable: an unlocked physical Windows console is required.");

        Screen[] screens = Screen.AllScreens;
        Rectangle area = GetMaskBounds(screens);
        if (area.Width <= 0 || area.Height <= 0)
            throw new InvalidOperationException(displayNumber == 0 ? "No virtual desktop is available." : "display-unavailable");
        int x = area.X, y = area.Y, width = area.Width, height = area.Height;
        if (displayNumber != 0)
            screens = Array.FindAll(screens, screen => GetDisplayNumber(screen) == displayNumber);
        displays = new Rectangle[screens.Length];
        for (int i = 0; i < screens.Length; i++)
        {
            Rectangle bounds = screens[i].Bounds;
            bounds.Offset(-x, -y);
            displays[i] = bounds;
        }

        IntPtr instance = Marshal.GetHINSTANCE(typeof(PrivacyMask).Module);
        const string className = "PrivacyMaskPoCWindow";
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf(typeof(WindowClass)),
            Proc = Proc,
            Instance = instance,
            Background = GetStockObject(BlackBrush),
            ClassName = className
        };
        Require(RegisterClassEx(ref windowClass) != 0, "RegisterClassEx");
        // A background launch can fail to promote a normal window even if SetWindowPos succeeds.
        IntPtr hwnd = CreateWindowEx(WsExTopmost | WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow,
            className, "Privacy Mask PoC", WsPopup, x, y, width, height,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        Require(hwnd != IntPtr.Zero, "CreateWindowEx");

        bool hotKeyRegistered = false;
        bool sessionRegistered = false;
        IntPtr foregroundHook = IntPtr.Zero;
        WinEvent foreground = (hook, kind, window, objectId, childId, thread, time) =>
        {
            if (!ending)
                RestoreCoverOrder(hwnd);
        };
        try
        {
            CheckTopmost(hwnd);
            Require(SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha), "SetLayeredWindowAttributes");
            uint requested = captureControl ? WdaNone : WdaExcludeFromCapture;
            Require(SetWindowDisplayAffinity(hwnd, requested), "SetWindowDisplayAffinity");
            // The readback also requires desktop composition for this layered window.
            uint actual;
            Require(GetWindowDisplayAffinity(hwnd, out actual), "GetWindowDisplayAffinity");
            if (actual != requested)
                throw new InvalidOperationException(string.Format("Display affinity 0x{0:X} did not match 0x{1:X}.", actual, requested));
            affinity = actual;

            hotKeyRegistered = MaskHotkeys.RegisterHotKey(IntPtr.Zero, HotKeyId,
                MaskHotkeys.ModAlt | MaskHotkeys.ModControl | MaskHotkeys.ModShift | MaskHotkeys.ModNoRepeat, VkEscape);
            Require(hotKeyRegistered, "RegisterHotKey (Ctrl+Alt+Shift+Esc)");
            sessionRegistered = WTSRegisterSessionNotification(hwnd, 0);
            Require(sessionRegistered, "WTSRegisterSessionNotification");
            if (!MaskControl.IsInteractiveConsole())
                throw new InvalidOperationException("desktop-unavailable: the console changed during startup.");

            durationSeconds = seconds;
            if (seconds > 0)
            {
                expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
                elapsed = Stopwatch.StartNew();
            }
            Require(SetTimer(hwnd, TimerId, 100, IntPtr.Zero) != UIntPtr.Zero, "SetTimer");

            // Do not show any overlay until capture affinity and the exit shortcut are available.
            Require(SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate | SwpShowWindow), "SetWindowPos");
            CheckTopmost(hwnd);
            if (!IsWindowVisible(hwnd))
                throw new InvalidOperationException("The overlay window did not become visible.");
            if (!RestoreCoverOrder(hwnd))
                throw new InvalidOperationException("Windows did not place the overlay above other desktop windows.");
            foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, foreground, 0, 0, 0);
            UpdateWindow(hwnd);
            Log(string.Format("READY hwnd=0x{0:X} affinity=0x{1:X2} bounds={2},{3},{4},{5} topmost=True hotkey=Ctrl+Alt+Shift+Esc",
                hwnd.ToInt64(), actual, x, y, width, height));
            MaskControl.Serve(() => CheckOverlay(hwnd, requested), password,
                () => AuthorizeStop(hwnd),
                () => PostMessage(hwnd, WmOpenUnlock, UIntPtr.Zero, IntPtr.Zero),
                () => PostMessage(hwnd, WmControlFailed, UIntPtr.Zero, IntPtr.Zero));

            Message message;
            int status;
            while ((status = GetMessage(out message, IntPtr.Zero, 0, 0)) > 0)
            {
                if (message.Id == WmHotKey && message.WParam == new UIntPtr(HotKeyId))
                {
                    if (password == null)
                    {
                        exitReason = "hotkey";
                        break;
                    }
                    OpenUnlockPrompt(hwnd);
                    continue;
                }
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            Require(status >= 0, "GetMessage");
        }
        finally
        {
            if (foregroundHook != IntPtr.Zero)
                UnhookWinEvent(foregroundHook);
            GC.KeepAlive(foreground);
            if (sessionRegistered && IsWindow(hwnd))
                WTSUnRegisterSessionNotification(hwnd);
            if (IsWindow(hwnd))
                DestroyWindow(hwnd);
            if (hotKeyRegistered)
                MaskHotkeys.UnregisterHotKey(IntPtr.Zero, HotKeyId);
        }
        Log("EXIT reason=" + exitReason);
    }

    private static IntPtr HandleWindowMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmPaint:
                if (appearance.Mode == MaskAppearanceMode.Black)
                    break;
                PaintOverlay(hwnd);
                return IntPtr.Zero;
            case WmMouseActivate:
                return new IntPtr(MaNoActivate);
            case WmTimer:
                if (wParam == TimerId && !ending)
                {
                    if (HasExpired())
                        return EndMask("timeout");
                    RestoreCoverOrder(hwnd);
                }
                break;
            case WmSessionChange:
                switch (wParam.ToUInt64())
                {
                    case 2: // Console disconnect
                    case 4: // Remote disconnect
                    case 6: // Logoff
                        return EndMask("session-disconnect");
                    case 7: // Session lock
                        return EndMask("session-lock");
                }
                break;
            case WmDpiChanged:
            case WmDisplayChange:
                // A changed display layout invalidates the tested coverage. End the session.
                return EndMask("display-change");
            case WmStopOverlay:
                return Interlocked.Exchange(ref stopAuthorized, 0) == 1
                    ? EndMask("controller") : IntPtr.Zero;
            case WmControlFailed:
                if (password == null)
                    return EndMask("control-failure");
                Log("ERROR control-failure; unlock via hotkey");
                return IntPtr.Zero;
            case WmCheckOverlay:
                return IsOverlayHealthy(hwnd) ? new IntPtr(1) : IntPtr.Zero;
            case WmOpenUnlock:
                if (!ending && password != null)
                    OpenUnlockPrompt(hwnd);
                return IntPtr.Zero;
            case WmRefreshCover:
                if (!ending)
                    RestoreCoverOrder(hwnd);
                return IntPtr.Zero;
            case WmClose:
                if (password != null)
                {
                    OpenUnlockPrompt(hwnd);
                    return IntPtr.Zero;
                }
                return EndMask("window-closed");
            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void PaintOverlay(IntPtr hwnd)
    {
        PaintStruct paint;
        IntPtr dc = BeginPaint(hwnd, out paint);
        try
        {
            if (dc == IntPtr.Zero)
                return;
            using (var graphics = Graphics.FromHdc(dc))
            {
                foreach (Rectangle display in displays)
                {
                    if (display.IntersectsWith(Rectangle.FromLTRB(paint.Paint.Left, paint.Paint.Top, paint.Paint.Right, paint.Paint.Bottom)))
                        appearance.Draw(graphics, display);
                }
            }
        }
        catch (Exception ex)
        {
            if (!(ex is ArgumentException || ex is ExternalException || ex is OutOfMemoryException))
                throw;
            FillRect(dc, ref paint.Paint, GetStockObject(BlackBrush));
            Log("APPEARANCE paint-fallback=black");
        }
        finally { EndPaint(hwnd, ref paint); }
    }

    private static void AuthorizeStop(IntPtr hwnd)
    {
        Interlocked.Exchange(ref stopAuthorized, 1);
        PostMessage(hwnd, WmStopOverlay, UIntPtr.Zero, IntPtr.Zero);
    }

    private static void OpenUnlockPrompt(IntPtr hwnd)
    {
        if (Interlocked.Exchange(ref unlockOpen, 1) != 0)
        {
            IntPtr existing = Interlocked.CompareExchange(ref unlockWindow, IntPtr.Zero, IntPtr.Zero);
            if (existing != IntPtr.Zero)
                PostMessage(existing, PasswordDialog.ActivateMessage, UIntPtr.Zero, IntPtr.Zero);
            return;
        }
        Log("UNLOCK requested");
        var prompt = new Thread(() =>
        {
            try
            {
                PasswordDialog.Unlock(value =>
                {
                    if (!password.Verify(value))
                        return "password-invalid";
                    AuthorizeStop(hwnd);
                    return null;
                }, window =>
                {
                    Interlocked.Exchange(ref unlockWindow, window);
                    PostMessage(hwnd, WmRefreshCover, UIntPtr.Zero, IntPtr.Zero);
                });
            }
            finally
            {
                Interlocked.Exchange(ref unlockWindow, IntPtr.Zero);
                Interlocked.Exchange(ref unlockOpen, 0);
                PostMessage(hwnd, WmRefreshCover, UIntPtr.Zero, IntPtr.Zero);
            }
        });
        prompt.SetApartmentState(ApartmentState.STA);
        prompt.IsBackground = true;
        prompt.Start();
    }

    private static IntPtr EndMask(string reason)
    {
        if (!ending)
        {
            ending = true;
            exitReason = reason;
            PostQuitMessage(0);
        }
        return IntPtr.Zero;
    }

    private static MaskStatus CheckOverlay(IntPtr hwnd, uint requested)
    {
        UIntPtr result;
        if (SendMessageTimeout(hwnd, WmCheckOverlay, UIntPtr.Zero, IntPtr.Zero, 0x22, 350, out result) == IntPtr.Zero ||
            result == UIntPtr.Zero)
            return MaskStatus.Unavailable("overlay-unverified");
        if (requested == WdaNone)
            return MaskStatus.Unavailable("capture-control");
        return new MaskStatus("active", expiresAt, null, password != null, displayNumber);
    }

    private static bool HasExpired()
    {
        return elapsed != null && elapsed.Elapsed.TotalSeconds >= durationSeconds;
    }

    private static bool IsOverlayHealthy(IntPtr hwnd)
    {
        uint actual;
        Rect rect;
        if (ending || HasExpired() || !MaskControl.IsInteractiveConsole() || !IsWindowVisible(hwnd) ||
            !RestoreCoverOrder(hwnd) ||
            !GetWindowDisplayAffinity(hwnd, out actual) || actual != affinity || !GetWindowRect(hwnd, out rect))
            return false;

        Rectangle area = GetMaskBounds(displayNumber == 0 ? null : Screen.AllScreens);
        return area.Width > 0 && area.Height > 0 && rect.Left == area.Left && rect.Top == area.Top &&
            rect.Right == area.Right && rect.Bottom == area.Bottom;
    }

    internal static int GetDisplayNumber(Screen screen)
    {
        const string prefix = @"\\.\DISPLAY";
        int number;
        return screen.DeviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(screen.DeviceName.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
            number > 0 ? number : 0;
    }

    private static Rectangle GetMaskBounds(Screen[] screens)
    {
        if (displayNumber == 0)
            return new Rectangle(GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
                GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));
        Screen selected = Array.Find(screens, screen => GetDisplayNumber(screen) == displayNumber);
        return selected == null ? Rectangle.Empty : selected.Bounds;
    }

    private static bool RestoreCoverOrder(IntPtr hwnd)
    {
        IntPtr prompt = Interlocked.CompareExchange(ref unlockWindow, IntPtr.Zero, IntPtr.Zero);
        if (!IsWindowVisible(prompt))
            prompt = IntPtr.Zero;
        Rect bounds;
        if (!GetWindowRect(hwnd, out bounds))
            return false;
        if (HasCoverOrder(hwnd, prompt, bounds))
            return true;

        const uint keepPosition = SwpNoMove | SwpNoSize | SwpNoActivate;
        if (prompt != IntPtr.Zero)
            PostMessage(prompt, PasswordDialog.RaiseMessage, UIntPtr.Zero, IntPtr.Zero);
        if (!SetWindowPos(hwnd, prompt == IntPtr.Zero ? Topmost : prompt, 0, 0, 0, 0, keepPosition | SwpShowWindow))
            return false;
        return HasCoverOrder(hwnd, prompt, bounds);
    }

    internal static void RaiseUnlockWindow(IntPtr hwnd)
    {
        SetWindowPos(hwnd, Topmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private static bool HasCoverOrder(IntPtr hwnd, IntPtr prompt, Rect bounds)
    {
        if (!IsWindowVisible(hwnd) || (GetWindowLong(hwnd, GwlExStyle) & WsExTopmost) == 0)
            return false;

        IntPtr window = GetWindow(hwnd, GwPreviousWindow);
        bool promptAbove = prompt == IntPtr.Zero;
        // The z-order can change while walking it. Bound the walk and recheck on the next timer tick.
        for (int i = 0; window != IntPtr.Zero && i < 256; i++, window = GetWindow(window, GwPreviousWindow))
        {
            Rect candidate;
            int cloaked;
            if (window == prompt)
                promptAbove = true;
            else if (IsWindowVisible(window) && GetWindowRect(window, out candidate) &&
                candidate.Left < bounds.Right && candidate.Right > bounds.Left && candidate.Top < bounds.Bottom && candidate.Bottom > bounds.Top &&
                (DwmGetWindowAttribute(window, DwmCloaked, out cloaked, sizeof(int)) != 0 || cloaked == 0))
                return false;
        }
        return window == IntPtr.Zero && promptAbove;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Dc;
        public int Erase;
        public Rect Paint;
        public int Restore, IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    private static void Log(string message)
    {
        log.WriteLine("{0:O} {1}", DateTimeOffset.Now, message);
    }

    private static void CheckTopmost(IntPtr hwnd)
    {
        if ((GetWindowLong(hwnd, GwlExStyle) & WsExTopmost) == 0)
            throw new InvalidOperationException("Windows did not give the overlay the topmost window style.");
    }

    private static void Require(bool ok, string operation)
    {
        if (!ok)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, operation + " failed (Win32 " + error + "): " + new Win32Exception(error).Message);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersion
    {
        public uint Size, Major, Minor, Build, Platform;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public WindowProc Proc;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string MenuName, ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Hwnd;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    private delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref OsVersion version);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int objectId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y,
        int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint milliseconds, IntPtr callback);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback,
        uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

internal sealed class MaskStatus
{
    internal readonly string State;
    internal readonly DateTimeOffset? ExpiresAt;
    internal readonly string Reason;
    internal readonly bool PasswordProtected;

    internal MaskStatus(string state, DateTimeOffset? expiresAt, string reason, bool passwordProtected = false)
    {
        State = state;
        ExpiresAt = expiresAt;
        Reason = reason;
        PasswordProtected = passwordProtected;
    }

    internal static MaskStatus Unavailable(string reason, bool passwordProtected = false)
    {
        return new MaskStatus("unavailable", null, reason, passwordProtected);
    }
}

internal static class MaskControl
{
    internal const int DefaultSeconds = 600;
    private const byte StatusRequest = 1, StopRequest = 2, Acknowledge = 3;
    private const byte PasswordStopRequest = 4;
    private const byte UnlockPromptRequest = 5;
    private const int RequestTimeoutMs = 650;
    private const byte ActiveReply = 1, TestReply = 2, UnverifiedReply = 3;
    private const byte ProtectedReply = 4, PasswordRequiredReply = 5, PasswordInvalidReply = 6, StoppingReply = 7;
    private const uint PipeAccessDuplex = 3, FileFlagOverlapped = 0x40000000, PipeRejectRemoteClients = 8;
    private const int DesktopReadObjects = 1, UserObjectName = 2;
    private static readonly SecurityIdentifier UserSid = CurrentUserSid();
    private static readonly int SessionId = GetSessionId();
    private static readonly string PipeName = "PrivacyMask-" + UserSid.Value + "-" + SessionId;
    private static readonly string MutexName = @"Local\" + PipeName;
    private static readonly MaskStatus Inactive = new MaskStatus("inactive", null, null);

    internal static int RunCommand(string[] args)
    {
        int seconds;
        MaskStatus status;
        if (args.Length != 3 || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) ||
            seconds < 0 || seconds > 3600)
            status = MaskStatus.Unavailable("invalid-arguments");
        else
            status = Execute(args[1], seconds);

        var json = new JavaScriptSerializer().Serialize(new
        {
            state = status.State,
            expiresAt = status.ExpiresAt.HasValue ? status.ExpiresAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) : null,
            reason = status.Reason,
            passwordProtected = status.PasswordProtected
        });
        Console.Out.WriteLine(json);
        return status.State == "unavailable" ? 1 : 0;
    }

    internal static MaskStatus Execute(string action, int seconds, string password = null)
    {
        try
        {
            if (!IsInteractiveConsole())
                return MaskStatus.Unavailable("desktop-unavailable");

            switch (action)
            {
                case "Start": return Start(seconds);
                case "Default":
                    var protection = MaskPassword.Load();
                    return Start(protection != null && protection.DefaultContinuous ? 0 : DefaultSeconds);
                case "Stop": return Stop(password);
                case "Status": return Query();
                case "Prompt": return Query(UnlockPromptRequest);
                default: return MaskStatus.Unavailable("invalid-arguments");
            }
        }
        catch (UnauthorizedAccessException) { return MaskStatus.Unavailable("access-denied"); }
        catch (Win32Exception) { return MaskStatus.Unavailable("control-failure"); }
        catch (IOException) { return MaskStatus.Unavailable("control-failure"); }
        catch (InvalidDataException) { return MaskStatus.Unavailable("password-settings-invalid"); }
    }

    private static MaskStatus Start(int seconds)
    {
        MaskStatus current = Query();
        if (current.Reason == "worker-unresponsive")
            current = WaitForWorker();
        if (current.State != "inactive")
            return current;

        string arguments = seconds == 0 ? "--worker --until-unlocked" :
            "--worker --seconds " + seconds.ToString(CultureInfo.InvariantCulture);
        using (var worker = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, arguments)
            { UseShellExecute = false, CreateNoWindow = true }))
        {
            var wait = Stopwatch.StartNew();
            do
            {
                current = Query();
                if (current.State == "active")
                    return current;
                if (worker.HasExited)
                {
                    if (worker.ExitCode != 2)
                        return StartupError(worker.Id);
                    current = WaitForWorker();
                    return current.State == "inactive" ? MaskStatus.Unavailable("startup-failed") : current;
                }
                Thread.Sleep(60);
            } while (wait.ElapsedMilliseconds < 4800);

            // Only cancel the worker spawned by this request. A concurrent request may own the mask.
            try { worker.Kill(); }
            catch (InvalidOperationException) { }
            return MaskStatus.Unavailable("startup-timeout");
        }
    }

    private static MaskStatus StartupError(int pid)
    {
        try
        {
            string logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrivacyMaskPoC", "PrivacyMask-" + pid + ".log");
            foreach (string line in File.ReadLines(logFile))
            {
                if (line.Contains(" ERROR RegisterHotKey") && line.Contains("Win32 1409"))
                    return MaskStatus.Unavailable("hotkey-conflict");
                if (line.Contains(" ERROR desktop-unavailable:"))
                    return MaskStatus.Unavailable("desktop-unavailable");
                if (line.Contains(" ERROR password-not-set"))
                    return MaskStatus.Unavailable("password-not-set");
                if (line.Contains(" ERROR password-settings-invalid"))
                    return MaskStatus.Unavailable("password-settings-invalid");
            }
        }
        catch (IOException) { }
        return MaskStatus.Unavailable("startup-failed");
    }

    private static MaskStatus Stop(string password)
    {
        var wait = Stopwatch.StartNew();
        MaskStatus current;
        bool currentRequest = true;
        do
        {
            current = Query(currentRequest ? StopRequest : StatusRequest, password);
            if (current.State == "inactive" || current.Reason == "legacy-overlay" ||
                current.Reason == "password-required" || current.Reason == "password-invalid")
                return current;
            if (current.Reason == "stop-pending")
                currentRequest = false;
            Thread.Sleep(60);
        } while (wait.ElapsedMilliseconds < 2500);
        return MaskStatus.Unavailable("stop-unverified");
    }

    private static MaskStatus WaitForWorker()
    {
        var wait = Stopwatch.StartNew();
        MaskStatus current;
        do
        {
            current = Query();
            if (current.Reason != "worker-unresponsive")
                return current;
            Thread.Sleep(60);
        } while (wait.ElapsedMilliseconds < 3200);
        return current;
    }

    private static MaskStatus Query(byte request = StatusRequest, string password = null)
    {
        Mutex guard;
        if (!Mutex.TryOpenExisting(MutexName, out guard))
        {
            return FindWindow("PrivacyMaskPoCWindow", null) == IntPtr.Zero
                ? Inactive : MaskStatus.Unavailable("legacy-overlay");
        }
        guard.Dispose();

        try
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification))
            {
                pipe.Connect(220);
                uint serverProcess;
                if (request == UnlockPromptRequest && GetNamedPipeServerProcessId(pipe.SafePipeHandle, out serverProcess))
                    AllowSetForegroundWindow(serverProcess);
                if (request == StopRequest && password != null)
                {
                    byte[] secret = Encoding.UTF8.GetBytes(password);
                    try
                    {
                        if (secret.Length == 0 || secret.Length > MaskPassword.LengthLimit * 4)
                            return MaskStatus.Unavailable("password-invalid");
                        pipe.WriteByte(PasswordStopRequest);
                        byte[] length = BitConverter.GetBytes((ushort)secret.Length);
                        pipe.Write(length, 0, length.Length);
                        pipe.Write(secret, 0, secret.Length);
                    }
                    finally { Array.Clear(secret, 0, secret.Length); }
                }
                else
                    pipe.WriteByte(request);
                var response = new byte[9];
                if (!ReadExactly(pipe, response, request == StopRequest && password != null ? 4000 : RequestTimeoutMs))
                    return MaskStatus.Unavailable("worker-unresponsive");
                pipe.WriteByte(Acknowledge);
                if (response[0] == ActiveReply || response[0] == ProtectedReply)
                {
                    long ticks = BitConverter.ToInt64(response, 1);
                    DateTimeOffset? deadline = ticks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(ticks, TimeSpan.Zero);
                    return new MaskStatus("active", deadline, null, response[0] == ProtectedReply);
                }
                if (response[0] == PasswordRequiredReply)
                    return MaskStatus.Unavailable("password-required", true);
                if (response[0] == PasswordInvalidReply)
                    return MaskStatus.Unavailable("password-invalid", true);
                if (response[0] == StoppingReply)
                    return MaskStatus.Unavailable("stop-pending");
                return MaskStatus.Unavailable(response[0] == TestReply ? "capture-control" : "overlay-unverified");
            }
        }
        catch (TimeoutException) { return MaskStatus.Unavailable("worker-unresponsive"); }
        catch (IOException) { return MaskStatus.Unavailable("worker-unresponsive"); }
    }

    internal static Mutex OpenWorkerMutex()
    {
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new MutexAccessRule(UserSid, MutexRights.FullControl, AccessControlType.Allow));
        bool created;
        return new Mutex(false, MutexName, out created, security);
    }

    internal static bool TryAcquire(Mutex guard)
    {
        try { return guard.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    internal static void Serve(Func<MaskStatus> status, MaskPassword password, Action stop, Action unlock, Action failed)
    {
        var first = OpenPipe();
        var server = new Thread(() =>
        {
            var pipe = first;
            try
            {
                while (true)
                {
                    using (pipe)
                    {
                        pipe.WaitForConnection();
                        if (Reply(pipe, status, password, stop, unlock))
                            return;
                    }
                    pipe = OpenPipe();
                }
            }
            catch (IOException) { failed(); }
            catch (Win32Exception) { failed(); }
        });
        server.IsBackground = true;
        server.Start();
    }

    private static bool Reply(NamedPipeServerStream pipe, Func<MaskStatus> status, MaskPassword password, Action stop, Action unlock)
    {
        var request = new byte[1];
        bool stopRequested = false;
        try
        {
            if (!ReadExactly(pipe, request, 500))
                return false;
            var response = new byte[9];
            switch (request[0])
            {
                case StatusRequest:
                case UnlockPromptRequest:
                    MaskStatus current = status();
                    if (request[0] == UnlockPromptRequest && password != null)
                        unlock();
                    response[0] = current.State == "active" ? (current.PasswordProtected ? ProtectedReply : ActiveReply) :
                        current.Reason == "capture-control" ? TestReply : UnverifiedReply;
                    if (current.ExpiresAt.HasValue)
                        BitConverter.GetBytes(current.ExpiresAt.Value.UtcTicks).CopyTo(response, 1);
                    break;
                case StopRequest:
                    stopRequested = password == null;
                    response[0] = stopRequested ? StoppingReply : PasswordRequiredReply;
                    break;
                case PasswordStopRequest:
                    stopRequested = ReadPassword(pipe, password);
                    response[0] = stopRequested ? StoppingReply : PasswordInvalidReply;
                    break;
                default:
                    return false;
            }
            pipe.Write(response, 0, response.Length);
            // Give the client a bounded chance to read the reply before closing the pipe.
            ReadExactly(pipe, request, 500);
        }
        catch (IOException) { }
        finally
        {
            if (stopRequested)
                stop();
        }
        return stopRequested;
    }

    private static bool ReadPassword(NamedPipeServerStream pipe, MaskPassword password)
    {
        byte[] size = new byte[2];
        if (!ReadExactly(pipe, size, 500))
            return false;
        int length = BitConverter.ToUInt16(size, 0);
        if (length == 0 || length > MaskPassword.LengthLimit * 4)
            return false;
        var bytes = new byte[length];
        try
        {
            return ReadExactly(pipe, bytes, 500) && (password == null || password.Verify(Encoding.UTF8.GetString(bytes)));
        }
        finally { Array.Clear(bytes, 0, bytes.Length); }
    }

    private static bool ReadExactly(PipeStream pipe, byte[] buffer, int timeoutMs)
    {
        var wait = Stopwatch.StartNew();
        int offset = 0;
        while (offset < buffer.Length)
        {
            Task<int> read = pipe.ReadAsync(buffer, offset, buffer.Length - offset);
            if (!read.IsCompleted && Task.WhenAny(read, Task.Delay(Math.Max(0, timeoutMs - (int)wait.ElapsedMilliseconds)))
                .GetAwaiter().GetResult() != read)
                return false;
            int count = read.GetAwaiter().GetResult();
            if (count == 0)
                return false;
            offset += count;
        }
        return true;
    }

    private static NamedPipeServerStream OpenPipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(UserSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        var descriptor = security.GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)),
                Descriptor = pinned.AddrOfPinnedObject() };
            SafePipeHandle handle = CreateNamedPipe(@"\\.\pipe\" + PipeName, PipeAccessDuplex | FileFlagOverlapped,
                PipeRejectRemoteClients, 1, 128, 128, 0, ref attributes);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "CreateNamedPipe failed (Win32 " + error + ").");
            }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { pinned.Free(); }
    }

    internal static bool IsInteractiveConsole()
    {
        if (WTSGetActiveConsoleSessionId() != (uint)SessionId ||
            ObjectName(GetProcessWindowStation()) != "WinSta0" || ObjectName(GetThreadDesktop(GetCurrentThreadId())) != "Default")
            return false;

        IntPtr input = OpenInputDesktop(0, false, DesktopReadObjects);
        if (input == IntPtr.Zero)
            return false;
        try { return ObjectName(input) == "Default"; }
        finally { CloseDesktop(input); }
    }

    private static string ObjectName(IntPtr handle)
    {
        var name = new StringBuilder(128);
        int required;
        return GetUserObjectInformation(handle, UserObjectName, name, name.Capacity * 2, out required) ? name.ToString() : null;
    }

    private static SecurityIdentifier CurrentUserSid()
    {
        using (var identity = WindowsIdentity.GetCurrent())
            return identity.User;
    }

    private static int GetSessionId()
    {
        using (var process = Process.GetCurrentProcess())
            return process.SessionId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances,
        uint outBufferSize, uint inBufferSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int size, out int required);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
}

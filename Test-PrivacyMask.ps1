param([string]$Exe, [switch]$IncludeControl, [switch]$IncludeSettings, [switch]$CoverRegression)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('Exe')) { $Exe = Join-Path $PSScriptRoot 'PrivacyMask.exe' }
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this script in Windows PowerShell with -STA on an unlocked desktop.'
}
$Exe = (Resolve-Path -LiteralPath $Exe).ProviderPath

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class MaskProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mouse { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Key { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public Mouse Mouse;
        [FieldOffset(0)] public Key Key;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputData Data; }

    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
    public static IntPtr FindTitledWindow(string title) { return FindWindow(null, title); }
    private delegate bool EnumWindow(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);

    public static IntPtr FindVisibleWindow(int processId, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, parameter) =>
        {
            int owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != processId || !IsWindowVisible(window)) return true;
            var text = new StringBuilder(128);
            GetWindowText(window, text, text.Capacity);
            if (text.ToString() != title) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);

    public static string Sample(int x, int y)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new InvalidOperationException("GetDC failed.");
        IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, original = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, 1, 1);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new InvalidOperationException("Capture allocation failed.");
            original = SelectObject(memory, bitmap);
            if (original == IntPtr.Zero) throw new InvalidOperationException("SelectObject failed.");
            const uint SrccopyWithCaptureblt = 0x40CC0020;
            if (!BitBlt(memory, 0, 0, 1, 1, screen, x, y, SrccopyWithCaptureblt)) throw new Win32Exception();
            uint rgb = GetPixel(memory, 0, 0);
            if (rgb == 0xFFFFFFFF) throw new InvalidOperationException("GetPixel failed.");
            return String.Format("#{0:X2}{1:X2}{2:X2}", rgb & 255, (rgb >> 8) & 255, (rgb >> 16) & 255);
        }
        finally
        {
            if (original != IntPtr.Zero) SelectObject(memory, original);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static void Click(int x, int y)
    {
        if (!SetCursorPos(x, y)) throw new Win32Exception();
        Send(new Input { Data = new InputData { Mouse = new Mouse { Flags = 2 } } },
             new Input { Data = new InputData { Mouse = new Mouse { Flags = 4 } } });
    }

    public static void TypeX() { Send(KeyStroke(0, 'x', 4), KeyStroke(0, 'x', 6)); }

    public static void ExitHotkey()
    {
        Send(KeyStroke(0x11, '\0', 0), KeyStroke(0x12, '\0', 0), KeyStroke(0x10, '\0', 0), KeyStroke(0x1B, '\0', 0),
             KeyStroke(0x1B, '\0', 2), KeyStroke(0x10, '\0', 2), KeyStroke(0x12, '\0', 2), KeyStroke(0x11, '\0', 2));
    }

    public static void NextTab()
    {
        Send(KeyStroke(0x11, '\0', 0), KeyStroke(0x09, '\0', 0),
             KeyStroke(0x09, '\0', 2), KeyStroke(0x11, '\0', 2));
    }

    private static Input KeyStroke(ushort vk, char scan, uint flags)
    {
        return new Input { Type = 1, Data = new InputData { Key = new Key { Vk = vk, Scan = scan, Flags = flags } } };
    }

    private static void Send(params Input[] input)
    {
        if (SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(Input))) != input.Length)
            throw new Win32Exception();
    }
}
'@

function Wait-Ui([int]$Milliseconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 15
    } while ($clock.ElapsedMilliseconds -lt $Milliseconds)
}

function Assert-Probe([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Wait-Ready($Run, [string]$Affinity) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        Wait-Ui 50
        if (Test-Path -LiteralPath $Run.Log) {
            $content = Get-Content -LiteralPath $Run.Log -Raw
            if ($content -match '(?m)ERROR (.+)$') { throw $Matches[1] }
            if ($content -match 'READY hwnd=0x([0-9A-F]+) affinity=(0x[0-9A-F]+) bounds=([-0-9,]+) topmost=True') {
                Assert-Probe ($Matches[2] -eq $Affinity -and -not $Run.Process.HasExited) "Mask failed to stay ready with affinity $Affinity. $content"
                Write-Host "READY $Affinity bounds=$($Matches[3]) log=$($Run.Log)"
                return [IntPtr][Convert]::ToInt64($Matches[1], 16)
            }
        }
    } while ($clock.ElapsedMilliseconds -lt 4000 -and -not $Run.Process.HasExited)
    throw "No READY from the mask. See $($Run.Log)."
}

function Start-Mask([string[]]$Arguments) {
    $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $runs.Add($process)
    return @{ Process = $process; Log = Join-Path $env:LOCALAPPDATA "PrivacyMaskPoC\PrivacyMask-$($process.Id).log" }
}

function Assert-Exit($Run, [string]$Reason, [int]$TimeoutMs) {
    Assert-Probe ($Run.Process.WaitForExit($TimeoutMs)) "Mask did not exit after $Reason."
    $content = Get-Content -LiteralPath $Run.Log -Raw
    Assert-Probe ($Run.Process.ExitCode -eq 0 -and $content.Contains("EXIT reason=$Reason")) "Unexpected exit. $content"
    Write-Host "PASS exit=$Reason"
}

function Assert-Capture([string]$Phase, [bool]$ExpectBlack) {
    foreach ($fixture in $fixtures) {
        $sample = $fixture.Form.PointToScreen([Drawing.Point]::new(20, 20))
        $pixel = [MaskProbe]::Sample($sample.X, $sample.Y)
        $expected = if ($ExpectBlack) { '#000000' } else { $fixture.Color }
        Assert-Probe ($pixel -eq $expected) "$Phase $($fixture.Screen.DeviceName) at $sample expected $expected, got $pixel."
        Write-Host "PASS $Phase $($fixture.Screen.DeviceName) at $sample -> $pixel"
    }
}

function Invoke-MaskCommand([string]$Action, [int]$Seconds = 600, [int]$ExpectedExitCode = 0) {
    $arguments = @{ Exe = $Exe }
    if ($PSBoundParameters.ContainsKey('Seconds')) { $arguments.Seconds = $Seconds }
    $json = & (Join-Path $PSScriptRoot 'Invoke-PrivacyMask.ps1') $Action @arguments
    Assert-Probe ($ExpectedExitCode -eq -1 -or $LASTEXITCODE -eq $ExpectedExitCode) "$Action returned unexpected exit code $LASTEXITCODE. $json"
    return $json | ConvertFrom-Json
}

function Test-Controller {
    Assert-Probe ((Invoke-MaskCommand Status).state -eq 'inactive') 'Controller did not start inactive.'
    try {
        $started = Invoke-MaskCommand Start 15
        $status = Invoke-MaskCommand Status
        $repeated = Invoke-MaskCommand Start 30
        Assert-Probe ($started.state -eq 'active' -and $null -ne $started.expiresAt -and
            $status.state -eq 'active' -and $status.expiresAt -eq $started.expiresAt -and
            $repeated.state -eq 'active' -and $repeated.expiresAt -eq $started.expiresAt) 'Readiness or duplicate Start changed the session.'
        Assert-Probe ((Invoke-MaskCommand Stop).state -eq 'inactive') 'Stop did not confirm cleanup.'
        Assert-Probe ((Invoke-MaskCommand Stop).state -eq 'inactive') 'Repeated Stop did not return inactive.'
        Write-Host 'PASS controller readiness, repeated Start deadline and repeated Stop'

        Assert-Probe ((Invoke-MaskCommand Start 2).state -eq 'active') 'The short controller session did not start.'
        $clock = [Diagnostics.Stopwatch]::StartNew()
        do {
            Wait-Ui 100
            $result = Invoke-MaskCommand Status -ExpectedExitCode -1
        } while ($result.state -ne 'inactive' -and $clock.ElapsedMilliseconds -lt 5000)
        Assert-Probe ($result.state -eq 'inactive') 'The worker outlived its controller and deadline.'
        Write-Host 'PASS controller exit leaves a worker that stops at its deadline'

        Assert-Probe ([MaskProbe]::RegisterHotKey([IntPtr]::Zero, 21, 0x4007, 0x1B)) 'Could not reserve the exit hotkey for the conflict test.'
        try {
            $failure = Invoke-MaskCommand Start 10 1
            Assert-Probe ($failure.state -eq 'unavailable' -and $failure.reason -eq 'hotkey-conflict') 'The hotkey conflict was not reported.'
            Assert-Probe ((Invoke-MaskCommand Status).state -eq 'inactive') 'A mask remained after the hotkey conflict.'
            Assert-Capture 'hotkey-conflict' $false
        }
        finally { $null = [MaskProbe]::UnregisterHotKey([IntPtr]::Zero, 21) }

        $sessionId = (Get-Process -Id $PID).SessionId
        foreach ($event in @(
            @{ Message = 0x7E; Detail = 0; Reason = 'display-change' },
            @{ Message = 0x2B1; Detail = 7; Reason = 'session-lock' },
            @{ Message = 0x2B1; Detail = 2; Reason = 'session-disconnect' }
        )) {
            $run = Start-Mask @('--seconds', '20')
            $hwnd = Wait-Ready $run '0x11'
            Assert-Probe ([MaskProbe]::PostMessage($hwnd, $event.Message, [UIntPtr]::new([uint32]$event.Detail),
                [IntPtr]::new($sessionId))) "Could not inject $($event.Reason) into the test window."
            Assert-Exit $run $event.Reason 4000
        }
        Write-Host 'PASS synthetic window notifications for display change, session lock and session disconnect'
    }
    finally {
        $null = & (Join-Path $PSScriptRoot 'Invoke-PrivacyMask.ps1') Stop -Exe $Exe
    }
}

function Test-Settings {
    $data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PrivacyMaskPoC'
    $appearancePath = Join-Path $data 'appearance.dat'
    $passwordPath = Join-Path $data 'password.dat'
    Assert-Probe (-not (Test-Path $appearancePath) -and -not (Test-Path $passwordPath)) 'Run settings tests in a Windows user profile without PrivacyMask settings.'

    Add-Type -AssemblyName System.Drawing
    $flags = [Reflection.BindingFlags]'NonPublic, Static'
    $instance = [Reflection.BindingFlags]'NonPublic, Instance'
    $assembly = [Reflection.Assembly]::LoadFrom($Exe)
    $changePassword = $assembly.GetType('MaskPassword').GetMethod('Change', $flags)
    $control = $assembly.GetType('MaskControl').GetMethod('Execute', $flags)
    $appearanceType = $assembly.GetType('MaskAppearance')
    $modeField = $appearanceType.GetField('Mode', $instance)
    $modeType = $assembly.GetType('MaskAppearanceMode')
    $appearance = [Activator]::CreateInstance($appearanceType, $true)
    $saveAppearance = $appearanceType.GetMethod('Save', $instance)
    $fixturePath = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'MaskFixture-' + [Guid]::NewGuid().ToString('N') + '.png')
    $secret = [Guid]::NewGuid().ToString('N')
    $settingsCreated = $false
    try {
        $bitmap = [Drawing.Bitmap]::new(128, 96)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.Clear([Drawing.Color]::FromArgb(22, 101, 195)) }
            finally { $graphics.Dispose() }
            $bitmap.Save($fixturePath, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bitmap.Dispose() }

        $appearanceType.GetMethod('Import', $instance).Invoke($appearance, @($fixturePath))
        $modeField.SetValue($appearance, [Enum]::Parse($modeType, 'Picture'))
        $saveAppearance.Invoke($appearance, @())
        Remove-Item -LiteralPath $fixturePath

        $picture = Start-Mask @('--seconds','4','--capture-control')
        $null = Wait-Ready $picture '0x00'
        Wait-Ui 180
        foreach ($fixture in $fixtures) {
            $bounds = $fixture.Screen.Bounds
            $sample = [MaskProbe]::Sample($bounds.X + [int]($bounds.Width / 2), $bounds.Y + [int]($bounds.Height / 2))
            Assert-Probe ($sample -eq '#1665C3') "Expected saved picture on $($fixture.Screen.DeviceName), saw $sample."
        }
        Assert-Exit $picture 'timeout' 5000
        Write-Host 'PASS saved image appears on both displays after source file deletion'
        $picture = Start-Mask @('--seconds','8')
        $null = Wait-Ready $picture '0x11'
        Wait-Ui 180
        Assert-Capture 'custom-picture-excluded' $false
        [MaskProbe]::ExitHotkey()
        Assert-Exit $picture 'hotkey' 4000

        $modeField.SetValue($appearance, [Enum]::Parse($modeType, 'Text'))
        $appearanceType.GetField('Text', $instance).SetValue($appearance, ([string][char]0x2588) * 4)
        $saveAppearance.Invoke($appearance, @())
        $text = Start-Mask @('--seconds','4','--capture-control')
        $null = Wait-Ready $text '0x00'
        Wait-Ui 180
        foreach ($fixture in $fixtures) {
            $bounds = $fixture.Screen.Bounds
            $x = $bounds.X + [int]($bounds.Width / 2)
            $y = $bounds.Y + [int]($bounds.Height / 2)
            $pixels = @(11, 23, 39 | ForEach-Object { [MaskProbe]::Sample($x + $_, $y) })
            Assert-Probe (@($pixels | Where-Object { $_ -ne '#000000' }).Count -gt 0) "No large text near the center of $($fixture.Screen.DeviceName)."
        }
        Assert-Exit $text 'timeout' 5000
        Write-Host 'PASS saved large text appears in the center of both displays'

        Assert-Probe ($changePassword.Invoke($null, @('', $secret, $true, $false))) 'Could not enable the fixture password.'
        $settingsCreated = $true
        $settingsBytes = [IO.File]::ReadAllBytes($passwordPath)
        Assert-Probe (-not [Text.Encoding]::UTF8.GetString($settingsBytes).Contains($secret)) 'Password appeared in its settings file.'
        Assert-Probe (-not $changePassword.Invoke($null, @('incorrect', '', $false, $true))) 'Wrong password could disable protection.'

        $default = Invoke-MaskCommand Start
        Assert-Probe ($default.state -eq 'active' -and $default.passwordProtected -and $null -eq $default.expiresAt) 'Saved default did not start a continuous protected session.'
        Assert-Capture 'custom-text-excluded' $false
        $blocked = Invoke-MaskCommand Stop -ExpectedExitCode 1
        Assert-Probe ($blocked.reason -eq 'password-required' -and $blocked.passwordProtected) 'Unauthenticated Stop did not request the password.'
        [MaskProbe]::ExitHotkey()
        $promptTitle = -join (0x89E3, 0x9664, 0x906E, 0x7F69 | ForEach-Object { [char]$_ })
        $wait = [Diagnostics.Stopwatch]::StartNew()
        do {
            Wait-Ui 100
            $prompt = [MaskProbe]::FindTitledWindow($promptTitle)
        } while ($prompt -eq [IntPtr]::Zero -and $wait.ElapsedMilliseconds -lt 3000)
        Assert-Probe ($prompt -ne [IntPtr]::Zero -and [MaskProbe]::IsWindowVisible($prompt) -and
            (Invoke-MaskCommand Status).state -eq 'active') 'The protected hotkey did not open the prompt and keep the mask active.'
        $wrong = $control.Invoke($null, @('Stop', 600, 'incorrect'))
        $stateType = $wrong.GetType()
        $state = $stateType.GetField('State', $instance)
        $reason = $stateType.GetField('Reason', $instance)
        Assert-Probe ($reason.GetValue($wrong) -eq 'password-invalid' -and (Invoke-MaskCommand Status).state -eq 'active') 'Wrong password removed the mask.'
        Assert-Probe ($state.GetValue($control.Invoke($null, @('Stop', 600, $secret))) -eq 'inactive') 'Correct password did not stop the mask.'
        Write-Host 'PASS continuous default, protected hotkey prompt, password-required, wrong-password and correct-password behavior'

        $timed = Invoke-MaskCommand Start 3
        Assert-Probe ($timed.state -eq 'active' -and $timed.passwordProtected -and $null -ne $timed.expiresAt) 'Explicit duration did not override the saved default.'
        Wait-Ui 3500
        Assert-Probe ((Invoke-MaskCommand Status).state -eq 'inactive') 'Timed protected session did not end automatically.'
        Write-Host 'PASS Agent-supplied duration overrides the continuous default and ends automatically'

        Assert-Probe ($changePassword.Invoke($null, @($secret, '', $false, $false))) 'Could not change the default with the current password.'
        $json = & (Join-Path $PSScriptRoot 'Invoke-PrivacyMask.ps1') Start -UntilUnlocked -Exe $Exe
        $continuous = $json | ConvertFrom-Json
        Assert-Probe ($LASTEXITCODE -eq 0 -and $continuous.passwordProtected -and $null -eq $continuous.expiresAt) 'Explicit continuous start did not override the timed default.'
        Assert-Probe ($state.GetValue($control.Invoke($null, @('Stop', 600, $secret))) -eq 'inactive') 'Could not stop explicitly continuous session.'
        Write-Host 'PASS Agent-supplied UntilUnlocked overrides the timed default'
    }
    finally {
        if ($settingsCreated) {
            $null = $control.Invoke($null, @('Stop', 600, $secret))
            $null = $changePassword.Invoke($null, @($secret, '', $false, $true))
        }
        $appearance.Dispose()
        if (Test-Path -LiteralPath $appearancePath) { Remove-Item -LiteralPath $appearancePath }
        if (Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath }
    }
}

function Test-Above([IntPtr]$Upper, [IntPtr]$Lower) {
    if ($Upper -eq [IntPtr]::Zero) { return $false }
    for ($i = 0; $i -lt 256 -and $Lower -ne [IntPtr]::Zero; $i++) {
        $Lower = [MaskProbe]::GetWindow($Lower, 3)
        if ($Lower -eq $Upper) { return $true }
    }
    return $false
}

function Test-SetupOrder($Fixture) {
    $form = $Fixture.Form
    $tabs = [Windows.Forms.TabControl]::new()
    $tabs.SetBounds(35, 18, 238, 102)
    $tabs.TabPages.Add('First')
    $tabs.TabPages.Add('Promote window')
    $tabs.add_SelectedIndexChanged({
        param($sender, $eventArgs)
        if ($sender.SelectedIndex -eq 1) { $sender.FindForm().TopMost = $true }
    })
    $form.Controls.Add($tabs)
    $tabs.BringToFront()
    $form.TopMost = $false

    $start = $tabs.PointToScreen([Drawing.Point]::new(28, 10))
    [MaskProbe]::Click($start.X, $start.Y)
    Wait-Ui 180
    Assert-Probe ($tabs.Focused -and [MaskProbe]::GetForegroundWindow() -eq $form.Handle) 'The tab fixture did not get focus.'

    $run = Start-Mask @('--seconds', '14')
    $mask = Wait-Ready $run '0x11'
    [MaskProbe]::NextTab()
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do { Wait-Ui 70 } until (($tabs.SelectedIndex -eq 1 -and (Test-Above $mask $form.Handle)) -or $wait.ElapsedMilliseconds -ge 1600)
    Assert-Probe ($tabs.SelectedIndex -eq 1 -and $form.TopMost -and [MaskProbe]::GetForegroundWindow() -eq $form.Handle) 'Ctrl+Tab did not change the tab with focus intact.'
    Assert-Probe (Test-Above $mask $form.Handle) 'A newly promoted tab window remained above the mask.'
    Assert-Capture 'after-tab-switch' $false
    Write-Host 'PASS Ctrl+Tab switches tabs while the mask covers the promoted window and focus remains intact'

    $status = Invoke-MaskCommand Status
    if ($status.passwordProtected) {
        [MaskProbe]::ExitHotkey()
        $title = -join (0x89E3, 0x9664, 0x906E, 0x7F69 | ForEach-Object { [char]$_ })
        $wait = [Diagnostics.Stopwatch]::StartNew()
        do {
            Wait-Ui 80
            $prompt = [MaskProbe]::FindVisibleWindow($run.Process.Id, $title)
        } until (($prompt -ne [IntPtr]::Zero -and [MaskProbe]::IsWindowVisible($prompt) -and
            (Test-Above $prompt $mask)) -or $wait.ElapsedMilliseconds -ge 2500)
        $affinity = [uint32]255
        $visible = [MaskProbe]::IsWindowVisible($prompt)
        $above = Test-Above $prompt $mask
        $affinityRead = [MaskProbe]::GetWindowDisplayAffinity($prompt, [ref]$affinity)
        $details = "Visible=$visible, above=$above, affinityRead=$affinityRead, affinity=$affinity."
        Assert-Probe ($prompt -ne [IntPtr]::Zero -and $visible -and $above -and (-not $affinityRead -or $affinity -eq 0)) "Unlock prompt did not remain visible and eligible for capture. $details"

        $form.TopMost = $false
        $form.TopMost = $true
        $wait = [Diagnostics.Stopwatch]::StartNew()
        do { Wait-Ui 70 } until (((Test-Above $prompt $mask) -and (Test-Above $mask $form.Handle)) -or $wait.ElapsedMilliseconds -ge 1600)
        Assert-Probe ((Test-Above $prompt $mask) -and (Test-Above $mask $form.Handle)) 'Window promotion disrupted the prompt and mask order.'
        Assert-Probe ((Invoke-MaskCommand Status).state -eq 'active') 'Opening the unlock prompt stopped the mask.'
        Write-Host 'PASS unlock prompt is eligible for capture and stays above the mask through a competing window promotion'
    }

    Assert-Exit $run 'timeout' 16000
}

$runs = [Collections.Generic.List[Diagnostics.Process]]::new()
$fixtures = [Collections.Generic.List[object]]::new()
$previousDpi = [MaskProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
Assert-Probe ($previousDpi -ne [IntPtr]::Zero) 'Could not set per-monitor DPI awareness for the probe.'
$originalCursor = $null
try {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $originalCursor = [Windows.Forms.Cursor]::Position
    $screens = @([Windows.Forms.Screen]::AllScreens | Sort-Object -Property Primary -Descending)
    foreach ($screen in $screens) {
        $form = [Windows.Forms.Form]::new()
        $form.Text = 'Privacy Mask Test Fixture'
        $form.TopMost = $true
        $form.StartPosition = 'Manual'
        $form.AutoScaleMode = 'None'
        $form.ClientSize = [Drawing.Size]::new(290, 130)
        $bounds = $screen.WorkingArea
        $form.Location = [Drawing.Point]::new($bounds.Left + [int](($bounds.Width - 290) / 2), $bounds.Top + [int](($bounds.Height - 130) / 2))
        $form.BackColor = if ($screen.Primary) { [Drawing.Color]::Magenta } else { [Drawing.Color]::Lime }
        $button = [Windows.Forms.Button]::new()
        $button.Text = 'Click test'
        $button.Location = [Drawing.Point]::new(30, 48)
        $button.Tag = 0
        $button.Add_Click({ param($sender, $eventArgs) $sender.Tag = [int]$sender.Tag + 1 })
        $edit = [Windows.Forms.TextBox]::new()
        $edit.Location = [Drawing.Point]::new(115, 50)
        $form.Controls.AddRange(@($button, $edit))
        $color = '#{0:X2}{1:X2}{2:X2}' -f $form.BackColor.R, $form.BackColor.G, $form.BackColor.B
        $fixtures.Add(@{ Form = $form; Button = $button; Edit = $edit; Screen = $screen; Color = $color })
        $form.Show()
    }

    $primary = $fixtures[0]
    Wait-Ui 250
    Assert-Capture 'baseline' $false
    if ($CoverRegression) {
        Test-SetupOrder $primary
        return
    }
    $sample = $primary.Form.PointToScreen([Drawing.Point]::new(20, 20))
    [MaskProbe]::Click($sample.X, $sample.Y)
    Wait-Ui 150
    Assert-Probe ([MaskProbe]::GetForegroundWindow() -eq $primary.Form.Handle) 'The test fixture could not become foreground. Run the test from the foreground PowerShell window.'
    foreach ($fixture in $fixtures) { $fixture.Form.TopMost = $false }
    Wait-Ui 150
    Assert-Capture 'baseline-normal' $false

    $control = Start-Mask @('--seconds', '3', '--capture-control')
    $null = Wait-Ready $control '0x00'
    Wait-Ui 250
    Assert-Capture 'control' $true
    if ($IncludeControl) {
        $testStatus = Invoke-MaskCommand Status -ExpectedExitCode 1
        Assert-Probe ($testStatus.state -eq 'unavailable' -and $testStatus.reason -eq 'capture-control') 'The controller treated a capture test as a privacy session.'
    }
    Assert-Exit $control 'timeout' 6000
    Wait-Ui 250

    Assert-Capture 'before-exclusion' $false
    [MaskProbe]::Click($sample.X, $sample.Y)
    Wait-Ui 150
    $foreground = [MaskProbe]::GetForegroundWindow()
    Assert-Probe ($foreground -eq $primary.Form.Handle) 'The test fixture did not regain foreground before the exclusion test.'
    $active = Start-Mask @('--seconds', '20')
    $hwnd = Wait-Ready $active '0x11'
    Wait-Ui 250
    $rect = [MaskProbe+Rect]::new()
    Assert-Probe ([MaskProbe]::GetWindowRect($hwnd, [ref]$rect) -and [MaskProbe]::IsWindowVisible($hwnd)) 'The mask window is not visible or its rectangle is unavailable.'
    $flags = 0x080800A8
    Assert-Probe (([MaskProbe]::GetWindowLong($hwnd, -20) -band $flags) -eq $flags) 'Required window styles are missing.'
    foreach ($screen in $screens) {
        $bounds = $screen.Bounds
        Assert-Probe ($rect.Left -le $bounds.Left -and $rect.Top -le $bounds.Top -and $rect.Right -ge $bounds.Right -and $rect.Bottom -ge $bounds.Bottom) "Mask rectangle does not cover $($screen.DeviceName) $bounds."
        Write-Host "PASS geometry $($screen.DeviceName) $bounds"
    }
    $foregroundAfter = [MaskProbe]::GetForegroundWindow()
    Assert-Probe ($foregroundAfter -eq $foreground) "Starting the mask changed the foreground window ($foreground to $foregroundAfter, mask $hwnd)."
    Write-Host 'PASS focus unchanged at launch'
    Assert-Capture 'excluded' $false

    $targets = @($fixtures | Sort-Object -Property { $_.Screen.Primary })
    foreach ($fixture in $targets) {
        $point = $fixture.Button.PointToScreen([Drawing.Point]::new(15, 10))
        [MaskProbe]::Click($point.X, $point.Y)
        Wait-Ui 150
        Assert-Probe ([int]$fixture.Button.Tag -eq 1 -and [MaskProbe]::GetForegroundWindow() -eq $fixture.Form.Handle) "Mouse did not reach or activate $($fixture.Screen.DeviceName) fixture."
        $point = $fixture.Edit.PointToScreen([Drawing.Point]::new(15, 10))
        [MaskProbe]::Click($point.X, $point.Y)
        Wait-Ui 100
        Assert-Probe ($fixture.Edit.Focused -and [MaskProbe]::GetForegroundWindow() -eq $fixture.Form.Handle) 'Test textbox did not have focus. No text was injected.'
        [MaskProbe]::TypeX()
        Wait-Ui 150
        Assert-Probe ($fixture.Edit.Text -ceq 'x') 'Keyboard input did not reach the fixture.'
        Write-Host "PASS click, activation and typing $($fixture.Screen.DeviceName)"
    }

    [MaskProbe]::ExitHotkey()
    Assert-Exit $active 'hotkey' 4000
    Wait-Ui 250
    Assert-Capture 'restored' $false
    Write-Host 'PASS all local GDI and input checks'
    if ($IncludeControl) { Test-Controller }
    if ($IncludeSettings) { Test-Settings }
}
finally {
    foreach ($process in $runs) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    foreach ($fixture in $fixtures) { $fixture.Form.Dispose() }
    if ($null -ne $originalCursor) { [Windows.Forms.Cursor]::Position = $originalCursor }
    $null = [MaskProbe]::SetThreadDpiAwarenessContext($previousDpi)
}

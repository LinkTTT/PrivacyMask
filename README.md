# PrivacyMask

**English** · [简体中文](README.zh-CN.md)

PrivacyMask covers the physical displays of an unlocked Windows PC during remote computer use. Compatible capture tools can still see the apps underneath, and mouse and keyboard input continue to reach them. Use a black screen, a custom image, or large text, with optional password protection for manual dismissal.

## Quick start

Use Windows 10 version 2004 (build 19041) or later. The application runs in the active, unlocked physical desktop session using the built-in .NET Framework. It needs no installation or administrator privileges.

1. Download and run [the Windows preview](preview/PrivacyMask.exe). The control panel currently uses Simplified Chinese.
2. Choose a duration and click **Start Mask (开始遮罩)**. Wait until the panel shows **Mask active (遮罩已开启)** before opening the target app.
3. Close the panel if you like. To end the session, reopen it and click **Stop Mask (停止遮罩)**, or press `Ctrl+Alt+Shift+Esc`.

### Appearance and password

- In **Settings (设置…)**, choose a black screen, a custom image, or large text. PNG, JPG, and BMP images are saved locally and centered on each screen with their aspect ratio preserved. Large text supports line breaks and scales to each screen. Changes apply the next time you start the mask.
- Under **Password and End Mode (密码与结束方式…)**, set a password to require it for manual dismissal. The **Unlock Mask (解除遮罩)** dialog appears above the mask and can be selected from a compatible remote client's window list. Its input is masked. Changing or disabling protection requires the current password. PrivacyMask stores a salted hash locally.
- Choose a **timed session** (5, 10, or 30 minutes, with 10 minutes as the default) or, with a password set, **stay active until unlocked**. A password-protected timed session still ends automatically at its deadline. Ending it early requires the password.
- Windows remains unlocked and people at the keyboard can still operate it. Locking Windows, disconnecting the Windows session, or changing the display layout or DPI ends the mask. If you forget the password, press `Ctrl+Alt+Del` to reach Task Manager and end `PrivacyMask.exe`.

## Agent commands

Run these commands on the target Windows PC, in the same Windows user account and physical desktop session. Keep [the script](preview/Invoke-PrivacyMask.ps1) beside the preview executable when copying them elsewhere.

```powershell
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Start -Seconds 600
# With a password configured, start without a timeout when the user requests it.
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Start -UntilUnlocked
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Status
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Stop
```

- `Start` uses the saved end mode if you omit both `-Seconds` and `-UntilUnlocked`. Repeated `Start` calls return the existing session and preserve its deadline.
- Each command prints one JSON object. `state` is `active`, `inactive`, or `unavailable`. `expiresAt` is a UTC timestamp for a timed session and `null` for a continuous session. `passwordProtected` tells you whether manual dismissal requires a password. On a protected session, `Stop` returns `password-required`. The user can unlock it through the panel or the hotkey. An unavailable result returns exit code `1` and a `reason`.
- Wait for `active` or the corresponding panel state before opening the target app. If the state is unclear, capture turns black, or the connection drops, stop and report what happened. Check the mask again after reconnecting.
- If the exit hotkey is already registered by another app, the mask does not start. If a remote client or agent disconnects, a timed session still expires and a continuous session can still be unlocked on Windows.
- Logs are stored at `%LOCALAPPDATA%\PrivacyMaskPoC\PrivacyMask-<PID>.log`. They record Windows version, window bounds, display affinity, startup errors, and exit reasons. The panel and commands communicate through a local named pipe restricted to the current user.

## Capture and display behavior

PrivacyMask uses a click-through, non-activating Win32 overlay with `WDA_EXCLUDEFROMCAPTURE` (`0x11`). Windows' Desktop Window Manager applies this setting to compatible capture paths. The app checks the affinity and verifies that the exit hotkey is registered before showing the overlay. [Microsoft Learn: SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)

The app checks window order when the foreground window changes and periodically checks for other windows that promote themselves to the top. It restores the mask above these windows while keeping its own unlock dialog visible. Some system UI, notifications, and the pointer can still appear. Test your exact remote client and capture path before using the mask with sensitive material. [Microsoft Learn: SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

The [validation log](Validation.txt) records dual-monitor geometry, GDI capture and input checks, local Computer Use tests, and the preview binary's SHA-256. Desktop GDI tests yielded the same fixture colors with capture exclusion enabled as with the mask off. Mobile Dots and other remote clients still need independent end-to-end validation.

## Build and test

Open Windows PowerShell on an unlocked desktop and run the following from the repository root. The tests open temporary windows, move the pointer, and briefly show the mask. Close any running mask before starting and leave the keyboard and pointer idle while the tests run.

```powershell
$staging = Join-Path $env:TEMP ('PrivacyMask-qa-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$exe = Join-Path $staging 'PrivacyMask.exe'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-PrivacyMask.ps1 -OutputPath $exe
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Test-PrivacyMask.ps1 -Exe $exe -IncludeControl
```

- The build uses the built-in .NET Framework C# compiler and produces one AnyCPU executable with no NuGet dependencies. Omit `-OutputPath` to build `PrivacyMask.exe` in the repository root.
- Add `-IncludeSettings` in a test account without saved PrivacyMask appearance or password settings. This tests custom images, large text, and password end modes, then removes its temporary settings.
- Run `-CoverRegression` on its own to test tab switching with `Ctrl+Tab` and a competing topmost window. If the account already has password protection enabled, this also checks that the unlock dialog stays visible. No password entry is needed for the timed test.
- To start a short diagnostic session directly, run `Start-Process .\preview\PrivacyMask.exe -ArgumentList '--seconds','20'`. The `--capture-control` test switch sets display affinity to `0x00`, making the mask appear in a supported capture. `Status` reports `unavailable` with reason `capture-control` in that mode.

### Verify a remote client

Start with Calculator and have someone watch each physical display. Ask the agent to start the mask, confirm that it is active, open Calculator, click `2`, and type `7` on the number row. The remote client should show `27` while the observer sees the displays covered. End the mask, confirm that the displays recover, and record the client, capture path, and result.

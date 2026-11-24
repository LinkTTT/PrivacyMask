# PrivacyMask

**English** · [简体中文](README.zh-CN.md)

PrivacyMask covers the physical displays of an unlocked Windows PC during remote computer use. Compatible capture tools can still see the apps underneath, and mouse and keyboard input continue to reach them. Use a black screen, a custom image, or large text, with optional password protection for manual dismissal.

## Quick start

Use Windows 10 version 2004 (build 19041) or later. The application runs in the active, unlocked physical desktop session using the built-in .NET Framework. It needs no installation or administrator privileges.

1. Download and run [the Windows preview](preview/PrivacyMask.exe). English is the default. To switch languages, use **Settings → Language** and select **简体中文**. The app remembers your choice.
2. Choose **All displays** or one display, then pick a duration and click **Start Mask**. Wait until the panel shows **Mask active** and confirms the covered display before opening the target app. For a secondary display, use Windows' **Extend** mode and keep the target app on that display.
3. Close the panel to leave PrivacyMask in the Windows notification area. To end the session, click **Stop Mask** or press `Ctrl+Alt+Shift+Esc`.

### Tray and global shortcuts

| Cover | Default shortcut |
| --- | --- |
| All displays | `Ctrl+Alt+Shift+0` |
| Display 1 only | `Ctrl+Alt+Shift+1` |
| Display 2 only | `Ctrl+Alt+Shift+2` |

- Use **Shortcuts…** in the panel or **Global Shortcuts…** in the tray menu to change or disable each shortcut. Choose a letter, a number from the top row, or F1–F11, with at least one of Ctrl, Alt, or Win. Changes take effect when saved.
- Shortcuts use the saved appearance and end mode, including password protection. Display numbers match the panel. A shortcut for a disconnected display leaves the mask off and reports that the display is unavailable. End an active session before starting one for a different scope.
- Double-click the tray icon to reopen the panel, or right-click to start a mask, stop or unlock it, or quit PrivacyMask. The icon may be under Windows' hidden-icons arrow. To start with the panel hidden, run `PrivacyMask.exe --tray`. Quitting the tray disables the start shortcuts. An active mask continues under its timer or password and can still be unlocked with `Ctrl+Alt+Shift+Esc`.
- In **Settings**, enable **Start when I sign in to Windows** to launch straight into the tray for your Windows account. The setting saves immediately. If you move the EXE, enable the option from its new location to update the path.

### Appearance and password

- In **Settings**, choose a black screen, a custom image, or large text. PNG, JPG, and BMP images are saved locally and centered on each screen with their aspect ratio preserved. Large text supports line breaks and scales to each screen. Changes apply the next time you start the mask.
- Under **Password and End Mode**, set a password to require it for manual dismissal. The **Unlock Mask** dialog appears above the mask and can be selected from a compatible remote client's window list. Its input is masked. Changing or disabling protection requires the current password. PrivacyMask stores a salted hash locally.
- Choose a **timed session** (5, 10, or 30 minutes, with 10 minutes as the default) or, with a password set, **stay active until unlocked**. A password-protected timed session still ends automatically at its deadline. Ending it early requires the password.
- Windows remains unlocked and people at the keyboard can still operate it. Locking Windows, disconnecting the Windows session, or changing the display layout or DPI ends the mask. If you forget the password, press `Ctrl+Alt+Del` to reach Task Manager and end `PrivacyMask.exe`.

## Agent commands

Run these commands on the target Windows PC, in the same Windows user account and physical desktop session. Keep [the script](preview/Invoke-PrivacyMask.ps1) beside the preview executable when copying them elsewhere.

```powershell
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Start -Seconds 600
# Use the display number shown in the PrivacyMask panel.
# Run this instead of the command above to cover only Display 2.
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Start -Seconds 600 -Display 2
# With a password configured, start without a timeout when the user requests it.
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Start -UntilUnlocked
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Status
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Stop
```

- `Start` uses the saved end mode if you omit both `-Seconds` and `-UntilUnlocked`. Omitting `-Display` selects all displays. Use the number shown in PrivacyMask for a single display, since Windows display settings can use different numbering. A repeated `Start` for the same scope preserves the deadline. A request for a different scope returns `mask-scope-mismatch`. A disconnected or unknown display returns `display-unavailable`.
- Each command prints one JSON object. `state` is `active`, `inactive`, or `unavailable`. `expiresAt` is a UTC timestamp for a timed session and `null` for a continuous session. `passwordProtected` tells you whether manual dismissal requires a password. On a protected session, `Stop` returns `password-required`. The user can unlock it through the panel or the hotkey. An unavailable result returns exit code `1` and a `reason`.
- In an `active` reply, `display` is the covered display number or `null` for all displays. Wait for `active` with the expected scope before opening the target app. If the state is unclear, capture turns black, or the connection drops, stop and report what happened. Check the mask again after reconnecting.
- If the exit hotkey is already registered by another app, the mask does not start. If a remote client or agent disconnects, a timed session still expires and a continuous session can still be unlocked on Windows.
- Logs are stored at `%LOCALAPPDATA%\PrivacyMaskPoC\PrivacyMask-<PID>.log`. They record Windows version, window bounds, display affinity, startup errors, and exit reasons. The panel and commands communicate through a local named pipe restricted to the current user.

## Capture and display behavior

PrivacyMask uses a click-through, non-activating Win32 overlay with `WDA_EXCLUDEFROMCAPTURE` (`0x11`). Windows' Desktop Window Manager applies this setting to compatible capture paths. The app checks the affinity and verifies that the exit hotkey is registered before showing the overlay. [Microsoft Learn: SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)

The app checks window order when the foreground window changes and periodically checks for other windows that promote themselves to the top. It restores the mask above these windows while keeping its own unlock dialog visible. Some system UI, notifications, and the pointer can still appear. Test your exact remote client and capture path before using the mask with sensitive material. [Microsoft Learn: SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

Single-display coverage leaves the other displays visible. Windows Computer Use still uses the foreground pointer and keyboard shared by the Windows session. [ChatGPT Learn: Windows foreground use](https://learn.chatgpt.com/docs/computer-use#windows-foreground-use)

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
- Run `-DisplayRegression` on its own with two extended displays to check display bounds, capture exclusion, input, and how the controller reports the scope. The test uses the current appearance and password settings without changing them.
- Run `-TrayRegression` on its own with the tray closed, all three shortcuts enabled, and displays 1 and 2 connected to exercise all scopes. This tests the assigned shortcuts, close-to-tray, reopening in the existing process, and the unlock key.
- To start a short diagnostic session directly, run `Start-Process .\preview\PrivacyMask.exe -ArgumentList '--seconds','20'`. The `--capture-control` test switch sets display affinity to `0x00`, making the mask appear in a supported capture. `Status` reports `unavailable` with reason `capture-control` in that mode.

### Verify a remote client

Start with Calculator and have someone watch each physical display. Ask the agent to start the mask, confirm that it is active, open Calculator, click `2`, and type `7` on the number row. The remote client should show `27` while the observer sees the displays covered. End the mask, confirm that the displays recover, and record the client, capture path, and result.

## Roadmap

- Prototype an optional agent tool for window-specific screenshots and targeted input. Start with a separate browser window and a small set of tested desktop controls. Validate that an agent can complete a workflow on the secondary display while someone types continuously in an IDE on the primary display, with no focus changes, pointer movement, or keystrokes crossing between the two. This would use a dedicated agent tool. The built-in Windows Computer Use path continues to use foreground input.

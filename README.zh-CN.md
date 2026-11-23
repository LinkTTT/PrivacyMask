# PrivacyMask · Windows 屏幕遮罩

[English](README.md) · **简体中文**

`PrivacyMask.exe` 在当前 Windows 桌面上显示不透明的遮罩，支持纯黑、自定义图片和大字。遮罩覆盖所有显示器，保持置顶，不夺取焦点，鼠标和键盘输入仍可到达下方应用。遮罩窗口可从受支持的截图中排除。

## 项目文件

| 文件 | 用途 |
| --- | --- |
| [PrivacyMask.cs](PrivacyMask.cs) | Win32 遮罩、置顶、截图排除与退出 |
| [MaskPanel.cs](MaskPanel.cs) · [MaskControl.cs](MaskControl.cs) | 图形面板、会话控制和状态查询 |
| [MaskSettings.cs](MaskSettings.cs) | 外观、密码和默认结束方式设置 |
| [preview/PrivacyMask.exe](preview/PrivacyMask.exe) | 可直接运行的 Windows 预览版，配套命令脚本位于同一目录 |
| [Invoke-PrivacyMask.ps1](Invoke-PrivacyMask.ps1) | 在目标 Windows 上启动、查询或停止遮罩 |
| [Build-PrivacyMask.ps1](Build-PrivacyMask.ps1) | 从源码生成单个 EXE |
| [Test-PrivacyMask.ps1](Test-PrivacyMask.ps1) | 自动验证截图排除、焦点、输入、覆盖范围及退出 |
| [Validation.txt](Validation.txt) | 实测记录，包含旧版露出问题、修复和迁移后复测 |

## 运行和退出

在目标 Windows 10 2004（内部版本 19041）或更新版本的**当前解锁物理桌面**使用。双击预览版 EXE，选择结束方式，点击「开始遮罩」并确认显示「遮罩已开启」。可以关闭面板后继续操作，再打开同一个 EXE 停止遮罩。程序使用系统自带的 .NET Framework，无需安装或管理员权限。

### 外观和密码

- 打开「设置…」，选择纯黑、自定义图片或大字提示，再点击「保存外观」。图片支持 PNG、JPG 和 BMP，会复制到本机设置中并按原比例居中显示。大字支持换行，会自动适应每块屏幕。外观在下一次开启时生效。
- 在「密码与结束方式…」中设置密码。之后点击「停止遮罩」或按 `Ctrl+Alt+Shift+Esc`，会在遮罩上方打开独立的「解除遮罩」密码窗口。远程操作时也可以在窗口列表里选择它，输入内容会显示为圆点。修改或关闭密码保护也要输入当前密码。密码设置只保存加盐哈希。
- 密码启用后，新会话默认可以设为持续到输入密码，或到时自动结束。每次启动都可以改选。计时模式提供 5、10 或 30 分钟，默认 10 分钟。选择计时模式时，**提前停止需要密码，到时自动结束**。
- 密码保护的是遮罩的手动解除。Windows 仍可操作，锁屏、Windows 会话断开、显示布局或 DPI 变化仍会结束遮罩。忘记密码时可以通过 `Ctrl+Alt+Del` 打开任务管理器结束 `PrivacyMask.exe`。

### Agent 命令

有终端的 Agent 可在同一 Windows 用户和物理桌面会话中执行以下命令。示例从项目根目录运行，也可以把配套脚本和 EXE 一起放在自己的目录里使用。

```powershell
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Start -Seconds 600
# 已设置密码时，可按用户指示选择持续遮罩
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Start -UntilUnlocked
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Status
powershell.exe -NoProfile -File .\preview\Invoke-PrivacyMask.ps1 Stop
```

- `Start` 可按任务选择 `-Seconds` 或 `-UntilUnlocked`。省略两者使用用户保存的默认结束方式。重复 `Start` 返回当前会话，保持原结束方式。
- 每条命令返回一行 JSON。`state` 为 `active`、`inactive` 或 `unavailable`。计时会话的 `expiresAt` 为 UTC 到期时间，持续会话为 `null`。`passwordProtected` 表示是否需要密码。受保护时，脚本 `Stop` 返回 `password-required`，由用户在面板或快捷键弹窗输入密码。状态无法确认时返回退出码 `1`，`reason` 给出原因。
- 自动化应等到 `active` 或面板显示「遮罩已开启」才打开目标应用。遇到 `inactive`、`unavailable`、截图变黑或连接中断时，停下并报告状态。恢复连接后先重新确认遮罩。
- 退出键冲突时遮罩不会显示。手机或 Agent 断线时，计时会话仍会到时结束，持续会话仍可在 Windows 上用密码解除。
- 旧测试可继续用 `Start-Process .\preview\PrivacyMask.exe -ArgumentList '--seconds','20'` 直接启动。仅测试时可加 `--capture-control`，此时 `0x00` 让遮罩进入支持采集分层窗口的截图，`Status` 返回 `unavailable` 和 `capture-control`。
- 日志位于 `%LOCALAPPDATA%\PrivacyMaskPoC\PrivacyMask-<PID>.log`，只记录系统版本、窗口坐标、截图排除值、启动错误和退出原因。命令与面板通过仅允许当前本机用户连接的命名管道通信。

## Windows 机制和覆盖范围

`SetWindowDisplayAffinity(hwnd, 0x11)` 仅适用于调用进程自己的顶层窗口，并依赖桌面窗口管理器（DWM）合成。Windows 10 2004 起，支持该标记的采集路径会从结果中省略整个窗口。更旧的系统会按 `WDA_MONITOR` 处理，原型在这些版本上拒绝启动。启动时会读回 `0x11`，并在确认退出快捷键已注册后才显示遮罩。[Microsoft Learn：SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) · [GetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowdisplayaffinity)

遮罩是 `WS_EX_LAYERED | WS_EX_TRANSPARENT` 的分层窗口。这个组合让鼠标事件由下方窗口处理。程序用 `WS_EX_NOACTIVATE` 和 `SWP_NOACTIVATE` 保持原有前台窗口，创建时设置 `WS_EX_TOPMOST`。前台窗口变化时会检查实际窗口顺序，也会定期复查应用在切换标签页后重新置顶的情况。遮罩回到其他应用窗口上方时，保留「解除遮罩」窗口在自己的上方。系统界面、通知和鼠标指针仍可能显示。 [Microsoft Learn：分层窗口](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows) · [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

原型只提供视觉遮挡。Windows 仍然解锁，现场人员仍可操作，程序退出或系统切换安全桌面时也会离开遮罩覆盖范围。截图排除的效果取决于采集路径。OpenAI 的公开说明要求 Windows Computer Use 使用活动且解锁的桌面。[ChatGPT Learn：Computer Use](https://learn.chatgpt.com/docs/computer-use#windows-foreground-use)

## 本机验证结果

2026-11-23 在 Windows `10.0.26300` 的交互桌面执行。显示器分别是 `1920×1200`，起点 `(0, 0)`，以及 `1920×1080`，起点 `(1920, −733)`。遮罩的窗口范围为 `(0, −733, 3840, 1933)`，包含两块显示器。新版预览已通过同一套本机双屏测试，并通过从本机 Computer Use 点击面板、操作计算器和停止遮罩的回归。现场观察记录来自保留版，详见 [Validation.txt](Validation.txt)。

| 测试 | 主屏采样 | 副屏采样 |
| --- | --- | --- |
| 无遮罩 | `#FF00FF` | `#00FF00` |
| 同一 EXE，普通黑色遮罩 `0x00` | `#000000` | `#000000` |
| 同一 EXE，截图排除 `0x11` | `#FF00FF` | `#00FF00` |
| 快捷键退出后 | `#FF00FF` | `#00FF00` |

采样使用 GDI `BitBlt(SRCCOPY | CAPTUREBLT)`。脚本从后台启动修正版，实测了遮罩可见、置顶和覆盖范围、启动前后前台窗口一致、点击切换两块屏幕上的测试窗口、键入 `x`、快捷键退出和定时退出。初版的额外测试也确认快捷键冲突返回 Win32 `1409`，第二个实例没有显示遮罩。[Microsoft Learn：BitBlt](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt) · [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)

复测时，窗口顺序记录显示旧版遮罩未置顶且低于计算器。修正版在原生 Computer Use 激活计算器、按截图坐标点击和键入期间都位于计算器上方，截图仍返回计算器画面。后续 30 秒复测中，原生 Computer Use 先点 `7` 再点 `6`，截图读数从 `2` 变为 `27` 再变为 `276`。用户现场确认两块物理屏幕都是黑色、看不到计算器，ChatGPT 的 Computer Use 界面仍可见。遮罩到时自动退出。手机 Dots 发起的端到端流程仍未验证。

## 复现和构建

在**解锁的桌面**打开 Windows PowerShell，并切换到项目目录。自动验证会打开临时测试窗口、短暂显示遮罩并移动指针，结束后会关闭测试窗口并恢复指针位置。先退出已运行的遮罩，验证期间暂时不要操作鼠标和键盘。

```powershell
$staging = Join-Path $env:TEMP ('PrivacyMask-qa-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$exe = Join-Path $staging 'PrivacyMask.exe'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-PrivacyMask.ps1 -OutputPath $exe
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Test-PrivacyMask.ps1 -Exe $exe -IncludeControl
```

在尚未保存 PrivacyMask 外观和密码设置的测试账户中，可附加 `-IncludeSettings`，复测自定义图片、大字和密码结束方式。测试结束后会清理临时设置。

可单独使用 `-CoverRegression`，复测 `Ctrl+Tab` 导致测试窗口重新置顶的情况。当前账户已开启密码时，这个用例也会检查解除弹窗，等遮罩计时结束，无需输入或修改现有密码。

构建脚本用 Windows 自带的 .NET Framework C# 编译器生成单个 AnyCPU EXE，无 NuGet 依赖。`-OutputPath` 将构建产物写到指定位置，省略该参数时会生成项目根目录下的 `PrivacyMask.exe`。预览版的校验值见 [验证记录](Validation.txt)。

## 后续开发

外观绘制在同一个遮罩窗口上，截图排除设置适用于整个窗口。后续修改外观时，仍需确认原生 Computer Use 截图和输入正常，以及各显示器的实际遮挡效果。

每次改动后运行自动验证，并用计算器进行一次限时原生 Computer Use 点击复测。修改外观后的验收还需要现场看两块物理屏幕，确认下方应用在操作期间没有露出。

## 手机 Dots、非 Windows Codex 和其他 Agent 验收

第一次用无敏感内容的计算器验收。在目标 Windows 的文件资源管理器预先打开 `preview` 文件夹，对每种 Agent 和采集路径分别验收。可以复制以下任务指令：

> 在 Windows 目标机打开 `preview\PrivacyMask.exe`，点击「开始遮罩」，等到面板显示「遮罩已开启」再打开计算器。如果无法确认状态，请停在打开计算器之前并告诉我原因。打开计算器后点击 `2`，用数字行键入 `7`，核对读数是否为 `27`。最后回到面板点击「停止遮罩」，确认显示「遮罩未开启」。

由现场观察者确认每块物理屏幕在操作期间都遮住计算器，并在停止后恢复。支持在目标桌面执行命令的 Agent 也可用 `Start → Status → Stop` 重复同一测试。记录客户端、Agent、采集路径和截图读数。适用性以该组合的验收结果为准。

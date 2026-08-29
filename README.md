# AI退烧贴 / AI Cooling Patch

[中文](#中文) · [English](#english)

<a id="中文"></a>
## 中文

**纵然 AI 风姿千千万，休要给我一双熊猫眼。**

AI退烧贴（项目代号 LockPC）是一款 Windows 10/11 专注与睡眠保护工具，通过专注计划、强制离屏休息和定时睡眠保护，减少长时间使用电脑与 AI 对休息、睡眠的影响。不戒 AI，只退烧。

### 1.0 已实现功能

- 专注计划：支持 15/25/30/45/50/60 分钟专注，专注期间电脑正常使用。
- 强制休息：每轮专注结束后覆盖全部显示器，并阻断普通鼠标键盘操作。
- 自动循环：休息结束后自动进入下一轮；默认 4 轮、每轮休息 10 分钟。
- 睡眠保护：支持开始/结束时间、跨零点和按星期生效。
- 睡前提醒：开始前 30 秒置顶提醒；每晚可延迟一次 5/10/15/30 分钟。
- 异常恢复：运行状态原子化保存；程序重启后恢复未完成阶段或睡眠保护。
- 多显示器：每块显示器创建独立置顶覆盖层，热插拔后自动重建。
- 系统托盘与开机启动：关闭主窗口后继续运行，并支持当前用户登录时启动。
- 安全演示：提供 10 秒休息模式、提前撕贴和睡眠保护状态演示。
- 有理由中断：结束专注计划或提前撕贴均需填写至少 5 个字的理由。
- 撕贴反馈：确认提前撕贴后播放撕起飞离动效和可关闭的短音效。
- 数据分析：汇总最近 7 天的完成专注、完整休息率、中断时段、睡眠保护和延迟次数。
- 关于与更新：位于“设置 → 关于与更新”，提供版本记录和 GitHub 更新入口。

### 获取与运行

从 [GitHub Releases](https://github.com/weichi-ai/ai-cooling-patch/releases) 下载 `AI-Cooling-Patch-v1.0.0-win-x64-self-contained.zip`，完整解压后运行 `LockPC.App.exe`。

自包含版本无需安装 .NET。请勿只复制 exe，运行库必须与主程序保持在同一目录。首次体验建议打开“专注” → “试贴休息模式（10 秒）”。

### 隐私说明

应用不读取应用名称、网站、窗口标题、键盘输入或屏幕内容。数据分析只使用本机保存的计划、休息、撕贴和睡眠事件。

### 安全边界

当前版本是应用级锁定，不是 Windows 登录锁屏。它无法阻止：

- `Ctrl+Alt+Delete` 安全桌面；
- 管理员结束进程、卸载或修改本机文件；
- 安全模式、断电、重装系统等拥有物理控制权的绕过方式。

因此，当前版本不能宣称对本机管理员“绝对不可破解”。

### 开发构建

请先安装 .NET 8 SDK，然后运行：

```powershell
dotnet build .\LockPC.sln
```

设置和运行状态默认保存在 `%LOCALAPPDATA%\LockPC`。开发测试可通过 `LOCKPC_DATA_DIR` 环境变量指定其他目录。

---

<a id="english"></a>
## English

**AI is endlessly tempting. Your sleep still matters.**

AI Cooling Patch (project codename: LockPC) is a focus and sleep-protection utility for Windows 10/11. It combines timed focus plans, enforced off-screen breaks, and scheduled sleep protection to reduce the impact of prolonged computer and AI use on rest and sleep. Keep the AI—cool the fever.

### What’s included in 1.0

- Focus plans: 15/25/30/45/50/60-minute sessions while the computer remains usable.
- Enforced breaks: covers every display and blocks ordinary mouse and keyboard input after each focus round.
- Automatic cycles: starts the next focus round when a break ends; the default is four rounds with 10-minute breaks.
- Sleep protection: configurable start/end times, overnight schedules, and selected weekdays.
- Bedtime warning: a topmost warning 30 seconds before protection starts, with one 5/10/15/30-minute delay per night.
- Crash and restart recovery: runtime state is saved atomically and unfinished phases are restored after restart.
- Multi-monitor support: creates a topmost overlay on every display and rebuilds overlays after display changes.
- System tray and startup: keeps running after the main window closes and can start at user sign-in.
- Safe demos: includes a 10-second break preview, an early-peel demonstration, and a sleep-protection preview.
- Reasoned interruptions: ending a focus plan or peeling early requires a reason of at least five characters.
- Peel feedback: plays a peel-and-fly animation and an optional short sound after confirmation.
- Local analytics: summarizes the last seven days of completed focus time, full-break rate, interruption periods, sleep protection, and delays.
- About and updates: available under Settings → About & Updates, with version history and GitHub update access.

### Download and run

Download `AI-Cooling-Patch-v1.0.0-win-x64-self-contained.zip` from [GitHub Releases](https://github.com/weichi-ai/ai-cooling-patch/releases), extract the entire archive, then run `LockPC.App.exe`.

The self-contained build does not require .NET to be installed. Do not copy the exe by itself—the accompanying runtime files must remain in the same directory. For a first look, open Focus → “10-second break preview.”

### Privacy

The app does not read application names, websites, window titles, keyboard input, or screen contents. Analytics use only locally stored focus-plan, break, peel, and sleep events.

### Security boundary

This version provides application-level enforcement; it is not the Windows sign-in lock screen. It cannot prevent:

- the `Ctrl+Alt+Delete` secure desktop;
- an administrator from terminating the process, uninstalling the app, or modifying local files;
- Safe Mode, power loss, OS reinstallation, or other bypasses available to someone with physical control.

It therefore must not be described as absolutely tamper-proof against a local administrator.

### Build from source

Install the .NET 8 SDK, then run:

```powershell
dotnet build .\LockPC.sln
```

Settings and runtime state are stored in `%LOCALAPPDATA%\LockPC` by default. Set `LOCKPC_DATA_DIR` to use a different directory during development or testing.

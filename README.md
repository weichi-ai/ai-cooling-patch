# AI退烧贴 / AI Cooling Patch

[中文](#中文) · [English](#english)

<a id="中文"></a>
## 中文

### AI虽顶，别太上头

**纵然 AI 风姿千千万，休要给我一双熊猫眼。**

给被 AI 勾了魂、忘记休息、舍不得睡的赛博人，一张退烧贴。

用一张数字退烧贴，管住连续专注、强制离屏与夜间睡眠。

AI退烧贴（项目代号 LockPC）是一款 Windows 10/11 专注与睡眠保护工具，通过专注计划、强制离屏休息和定时睡眠保护，减少长时间使用电脑与 AI 对休息、睡眠的影响。不戒 AI，只退烧。

### v1.1.3 更新

- 活动历史从 JSON 迁移为应用内嵌 SQLite，取消 10,000 条上限并避免每次记录都重写整个历史文件。
- 首次启动自动导入原有 `activity-events.json`，迁移幂等，并保留 `activity-events.v1.1.2.json.bak` 备份。
- 历史记录默认显示最近 7 天，支持最近 15 天、最近 30 天和全部记录筛选，每页 50 条。
- 专注轮数补齐为 1–12 轮，开始前使用新的计划摘要确认弹窗。
- 系统托盘显示当前轮次和进入离屏休息的剩余时间；空闲状态使用两行提示。
- 修复高 DPI 和小屏幕下窗口超出工作区的问题，并改善提前撕贴按钮与休息文案。

### v1.1.3 已实现功能

- 专注计划：支持 15/25/30/45/50/60 分钟专注，专注期间电脑正常使用。
- 强制休息：每轮专注结束后覆盖全部显示器，并阻断普通鼠标键盘操作。
- 自动循环：休息结束后自动进入下一轮；默认 4 轮、每轮休息 10 分钟。
- 睡眠保护：支持开始/结束时间、跨零点和按星期生效。
- 睡前提醒：开始前 30 秒置顶提醒；每晚可延迟一次 5/10/15/30 分钟。
- 锁屏前过渡：专注退烧和睡眠保护开始前显示 15 秒动态倒计时，期间电脑仍可操作。
- 异常恢复：运行状态原子化保存；程序重启后恢复未完成阶段或睡眠保护。
- 多显示器：每块显示器创建独立置顶覆盖层，热插拔后自动重建。
- 系统托盘与开机启动：关闭主窗口后继续运行，并支持当前用户登录时启动。
- 安全演示：休息与睡眠演示均完整展示 15 秒可操作过渡、10 秒保护界面和完成庆祝；演示不会触发 Windows 锁屏。
- 有理由中断：结束专注计划或提前撕贴均需填写至少 5 个字的理由。
- 撕贴反馈：确认提前撕贴后播放撕起飞离动效和可关闭的短音效。
- 数据分析：七日指标汇总保持不变；活动历史在本机 SQLite 中全量保存，支持范围筛选和 50 条分页。
- 关于与更新：位于“设置 → 关于与更新”，提供版本记录和 GitHub 更新入口。

### 获取与运行

从 [GitHub Release v1.1.3](https://github.com/weichi-ai/ai-cooling-patch/releases/tag/v1.1.3) 下载 [`AI-Cooling-Patch-v1.1.3-win-x64-self-contained.zip`](https://github.com/weichi-ai/ai-cooling-patch/releases/download/v1.1.3/AI-Cooling-Patch-v1.1.3-win-x64-self-contained.zip)，完整解压后运行 `LockPC.App.exe`。

自包含版本无需安装 .NET。请勿只复制 exe，运行库必须与主程序保持在同一目录。首次体验建议打开“专注” → “试贴休息模式（10 秒）”。

### 隐私说明

应用不读取应用名称、网站、窗口标题、键盘输入或屏幕内容。数据分析只使用本机保存的计划、休息、撕贴和睡眠事件。

### 安全边界

睡眠保护开始时会调用 Windows 登录锁屏，用户重新登录后由应用级多屏覆盖继续保护。它仍无法阻止：

- `Ctrl+Alt+Delete` 安全桌面；
- 管理员结束进程、卸载或修改本机文件；
- 安全模式、断电、重装系统等拥有物理控制权的绕过方式。

因此，当前版本不能宣称对本机管理员“绝对不可破解”。

### 开发构建

请先安装 .NET 8 SDK，然后运行：

```powershell
dotnet build .\LockPC.sln
```

设置、运行状态和 SQLite 活动历史默认保存在 `%LOCALAPPDATA%\LockPC`。开发测试可通过 `LOCKPC_DATA_DIR` 环境变量指定其他目录。

---

<a id="english"></a>
## English

### AI is great. Don’t get carried away.

**AI is endlessly tempting. Your sleep still matters.**

For cyber humans captivated by AI—forgetting to rest and reluctant to sleep—here is a cooling patch.

One digital cooling patch for continuous focus, enforced off-screen breaks, and nighttime sleep protection.

AI Cooling Patch (project codename: LockPC) is a focus and sleep-protection utility for Windows 10/11. It combines timed focus plans, enforced off-screen breaks, and scheduled sleep protection to reduce the impact of prolonged computer and AI use on rest and sleep. Keep the AI—cool the fever.

### What changed in v1.1.3

- Moves activity history from JSON to embedded SQLite, removes the 10,000-event cap, and avoids rewriting the entire history file for every event.
- Imports the existing `activity-events.json` once, idempotently, while preserving an `activity-events.v1.1.2.json.bak` backup.
- Shows the last 7 days by default, with 15-day, 30-day, and all-history filters and 50-row pages.
- Completes the 1–12 focus-round choices and replaces the legacy start warning with a plan-summary confirmation dialog.
- Adds live round and break countdown text to the system tray, plus a two-line idle status.
- Fixes high-DPI/small-screen window placement and improves early-peel button readability and break wording.

### What’s included in v1.1.3

- Focus plans: 15/25/30/45/50/60-minute sessions while the computer remains usable.
- Enforced breaks: covers every display and blocks ordinary mouse and keyboard input after each focus round.
- Automatic cycles: starts the next focus round when a break ends; the default is four rounds with 10-minute breaks.
- Sleep protection: configurable start/end times, overnight schedules, and selected weekdays.
- Bedtime warning: a topmost warning 30 seconds before protection starts, with one 5/10/15/30-minute delay per night.
- Pre-lock transition: a 15-second animated countdown before focus breaks and sleep protection while the computer remains usable.
- Crash and restart recovery: runtime state is saved atomically and unfinished phases are restored after restart.
- Multi-monitor support: creates a topmost overlay on every display and rebuilds overlays after display changes.
- System tray and startup: keeps running after the main window closes and can start at user sign-in.
- Safe demos: break and sleep previews both show the full 15-second usable transition, 10-second protection state, and completion celebration without invoking Windows lock.
- Reasoned interruptions: ending a focus plan or peeling early requires a reason of at least five characters.
- Peel feedback: plays a peel-and-fly animation and an optional short sound after confirmation.
- Local analytics: keeps seven-day summary metrics while storing complete activity history in local SQLite with filters and 50-row pagination.
- About and updates: available under Settings → About & Updates, with version history and GitHub update access.

### Download and run

Download [`AI-Cooling-Patch-v1.1.3-win-x64-self-contained.zip`](https://github.com/weichi-ai/ai-cooling-patch/releases/download/v1.1.3/AI-Cooling-Patch-v1.1.3-win-x64-self-contained.zip) from [GitHub Release v1.1.3](https://github.com/weichi-ai/ai-cooling-patch/releases/tag/v1.1.3), extract the entire archive, then run `LockPC.App.exe`.

The self-contained build does not require .NET to be installed. Do not copy the exe by itself—the accompanying runtime files must remain in the same directory. For a first look, open Focus → “10-second break preview.”

### Privacy

The app does not read application names, websites, window titles, keyboard input, or screen contents. Analytics use only locally stored focus-plan, break, peel, and sleep events.

### Security boundary

Sleep protection invokes the Windows sign-in lock and continues with an application-level multi-display overlay after sign-in. It still cannot prevent:

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

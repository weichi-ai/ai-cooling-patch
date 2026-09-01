# AI退烧贴 v1.1.3 / AI Cooling Patch v1.1.3

[中文](#中文) · [English](#english)

<a id="中文"></a>
## 中文

v1.1.3 完成了本地历史数据升级，并集中改善专注计划、系统托盘和小屏幕使用体验。

### 主要更新

- 活动历史从 JSON 迁移到应用内嵌 SQLite，全量保存记录，不再限制为 10,000 条。
- 首次启动会自动、幂等地导入原有 `activity-events.json`，并保留 `activity-events.v1.1.2.json.bak` 备份。
- 历史记录默认显示最近 7 天，可切换最近 15 天、最近 30 天或全部记录；每页显示 50 条。
- 专注轮数补齐为 1–12 轮，并使用新的计划摘要弹窗确认开始。
- 系统托盘以两行文字显示当前轮次和距离离屏休息的剩余时间；无计划时显示空闲提示。
- 改善高 DPI 和小屏幕下的窗口尺寸与定位，避免窗口超出工作区且无法操作。
- 修复提前撕贴理由不足时禁用按钮文字难以辨认的问题。
- 离屏休息文案调整为“让大脑和眼睛放松一下”。

### 升级说明

- SQLite 为程序内嵌组件，无需安装数据库、驱动或额外服务。
- 数据仍只保存在本机 `%LOCALAPPDATA%\LockPC`。
- 自包含压缩包无需安装 .NET；请完整解压后运行 `LockPC.App.exe`，不要只复制 exe。

### 下载

- Windows 10/11 x64：`AI-Cooling-Patch-v1.1.3-win-x64-self-contained.zip`
- SHA-256：`9C70D4CEB471CAB9E8E386F4529772B9943E7E1E591AEC7621C1C02DBB5D1E44`

本版本已通过 11 项计划、托盘与 SQLite 自动化场景检查。

---

<a id="english"></a>
## English

v1.1.3 upgrades local history storage and improves focus plans, tray status, and usability on smaller or high-DPI displays.

### Highlights

- Migrates activity history from JSON to embedded SQLite and stores the complete history without the previous 10,000-event cap.
- Imports an existing `activity-events.json` automatically and idempotently on first launch, while preserving an `activity-events.v1.1.2.json.bak` backup.
- Shows the last 7 days by default, with 15-day, 30-day, and all-history filters; each page contains 50 rows.
- Completes the 1–12 focus-round options and introduces a redesigned plan-summary confirmation dialog.
- Shows the current round and live time remaining until the off-screen break in a two-line system-tray status; idle mode has its own clear status.
- Improves window sizing and placement on high-DPI and smaller displays so dialogs remain within the usable work area.
- Fixes low-contrast text on the disabled early-peel button when the reason is too short.
- Updates the break message to remind users to relax both their brain and eyes.

### Upgrade notes

- SQLite is embedded in the application; no database server, driver, or separate installation is required.
- Data remains local under `%LOCALAPPDATA%\LockPC`.
- The self-contained archive does not require .NET. Extract the complete archive before running `LockPC.App.exe`; do not copy the exe alone.

### Download

- Windows 10/11 x64: `AI-Cooling-Patch-v1.1.3-win-x64-self-contained.zip`
- SHA-256: `9C70D4CEB471CAB9E8E386F4529772B9943E7E1E591AEC7621C1C02DBB5D1E44`

This release passed all 11 automated schedule, tray, and SQLite scenarios.

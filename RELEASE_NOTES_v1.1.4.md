# AI退烧贴 v1.1.4 / AI Cooling Patch v1.1.4

[中文](#中文) · [English](#english)

<a id="中文"></a>
## 中文

v1.1.4 重新设计了睡眠保护开始前 30 秒的提醒窗口，让锁定时间、解除时间和延迟选项更容易快速确认。

### 主要更新

- 使用圆角卡片、自定义标题栏和统一的深蓝、薄荷绿视觉重做睡前提醒。
- 将倒计时集中到深色主卡片，新增动态进度条，并单独显示自动解除时间。
- 重新设计 5/10/15/30 分钟延迟按钮，补齐悬停、按下和禁用状态。
- 保留“今晚还可以申请最后一次延迟，最长 30 分钟。”提示，以及每晚仅可延迟一次的原有规则。
- 更新保存提示，引导用户保存正在处理的文件并退出运行中的闲置应用。
- 数据分析记录睡眠保护结果；使用延迟时显示“延迟 X 分钟后睡眠”，未延迟时显示“按时睡眠”。
- 无边框窗口仍支持拖动、关闭、置顶显示和倒计时结束后自动关闭。

### 升级说明

- 本次更新不改变睡眠计划、延迟次数或锁屏逻辑；只扩展本地历史记录的结果展示。
- 自包含压缩包无需安装 .NET；请完整解压后运行 `LockPC.App.exe`，不要只复制 exe。

### 下载

- Windows 10/11 x64：`AI-Cooling-Patch-v1.1.4-win-x64-self-contained.zip`
- SHA-256：`AD566E43A01BE8078A16A6474AE88EBC68E45E87EE1D7830F6CC85C31D12F246`

本版本通过 12 项自动化场景检查，并完成睡前提醒窗口的独立编译与实际渲染验证。

---

<a id="english"></a>
## English

v1.1.4 redesigns the 30-second bedtime warning so the lock countdown, automatic unlock time, and delay choices are easier to scan.

### Highlights

- Rebuilds the bedtime warning with a rounded card, custom title bar, and a consistent navy-and-mint visual system.
- Places the countdown in a focused dark panel, adds a live progress bar, and shows the automatic unlock time separately.
- Restyles the 5/10/15/30-minute delay buttons with hover, pressed, and disabled states.
- Preserves the final-delay notice and the existing rule of one delay per night, up to 30 minutes.
- Updates the reminder copy to prompt users to save active files and close idle applications that are still running.
- Records sleep-protection outcomes in analytics; completion shows “Slept after an X-minute delay,” or “Slept on schedule” when no delay was used.
- Keeps window dragging, closing, topmost display, and automatic dismissal when the countdown ends.

### Upgrade notes

- Sleep schedules, delay limits, and locking behavior are unchanged; only the local-history result display is extended.
- The self-contained archive does not require .NET. Extract the complete archive before running `LockPC.App.exe`; do not copy the exe alone.

### Download

- Windows 10/11 x64: `AI-Cooling-Patch-v1.1.4-win-x64-self-contained.zip`
- SHA-256: `AD566E43A01BE8078A16A6474AE88EBC68E45E87EE1D7830F6CC85C31D12F246`

This release passes all 12 automated scenarios and adds an isolated compile and rendered-output check for the bedtime warning.

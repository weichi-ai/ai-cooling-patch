using System.Reflection;

namespace LockPC.App.Core;

public sealed record ProductReleaseNote(string Version, string Date, string Summary);

public static class AppMetadata
{
    public const string ProductName = "AI退烧贴";
    public const string PrimarySlogan = "纵然 AI 风姿千千万，休要给我一双熊猫眼。";
    public const string ProductIntroduction = "给被 AI 勾了魂、忘记休息、舍不得睡的赛博人，一张退烧贴。";
    public const int LockTransitionSeconds = 15;
    public const int CelebrationSeconds = 5;

    // 开发和测试时可以通过 LOCKPC_GITHUB_REPOSITORY_URL 环境变量覆盖。
    public const string GitHubRepositoryUrl = "https://github.com/weichi-ai/ai-cooling-patch";

    public static string EffectiveGitHubRepositoryUrl =>
        Environment.GetEnvironmentVariable("LOCKPC_GITHUB_REPOSITORY_URL")?.Trim()
        ?? GitHubRepositoryUrl;

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string CurrentVersionText =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

    public static IReadOnlyList<ProductReleaseNote> ReleaseNotes { get; } =
    [
        new("1.1.4", "2026-09-04", "重新设计睡前 30 秒提醒窗口，并在数据分析中记录延迟分钟与按时睡眠结果。"),
        new("1.1.3", "2026-09-01", "活动历史迁移至内嵌 SQLite，支持全量保存、范围筛选和每页 50 条；同时改进专注确认、托盘倒计时与高 DPI 窗口适配。"),
        new("1.1.2", "2026-09-01", "修复睡眠保护延迟跨零点后串到下一晚的问题，并自动清理 v1.1.1 异常残留状态。"),
        new("1.1.1", "2026-08-31", "睡眠演示补全倒计时；修正单次延迟交互；两种模式完成后新增满屏撒花和音效；睡眠保护开始时自动锁定 Windows。"),
        new("1.1.0", "2026-08-29", "专注退烧与睡眠保护锁屏前新增可继续操作电脑的 15 秒倒计时提示。"),
        new("1.0.1", "2026-08-29", "统一专注计划相关状态、标题、按钮及确认提示文案。"),
        new("1.0.0", "2026-08-29", "全新退烧贴 UI；新增本地数据分析、专注取消、提前撕贴动效音效与睡眠保护演示。"),
        new("0.6.0", "2026-08-28", "启用 AI退烧贴品牌视觉与文案；新增关于页、版本历史和 GitHub Releases 更新检查。"),
        new("0.5.0", "2026-08-28", "新增 15、25 分钟专注选项；专注取消和休息提前结束均需填写理由并保存在本机。"),
        new("0.4.0", "2026-08-28", "完成番茄专注、强制离屏休息、睡眠保护、多显示器覆盖、异常恢复和系统托盘。")
    ];
}

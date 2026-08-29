using System.Reflection;

namespace LockPC.App.Core;

public sealed record ProductReleaseNote(string Version, string Date, string Summary);

public static class AppMetadata
{
    public const string ProductName = "AI退烧贴";
    public const string PrimarySlogan = "纵然 AI 风姿千千万，休要给我一双熊猫眼。";
    public const string ProductIntroduction = "给被 AI 勾了魂、忘记休息、舍不得睡的赛博人，一张退烧贴。";

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
        new("1.0.0", "2026-08-29", "全新退烧贴 UI；新增本地数据分析、专注取消、提前撕贴动效音效与睡眠保护演示。"),
        new("0.6.0", "2026-08-28", "启用 AI退烧贴品牌视觉与文案；新增关于页、版本历史和 GitHub Releases 更新检查。"),
        new("0.5.0", "2026-08-28", "新增 15、25 分钟专注选项；专注取消和休息提前结束均需填写理由并保存在本机。"),
        new("0.4.0", "2026-08-28", "完成番茄专注、强制离屏休息、睡眠保护、多显示器覆盖、异常恢复和系统托盘。")
    ];
}

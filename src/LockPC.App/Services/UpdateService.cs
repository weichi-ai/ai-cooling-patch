using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LockPC.App.Core;

namespace LockPC.App.Services;

public enum UpdateCheckStatus
{
    NotConfigured,
    Latest,
    UpdateAvailable,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestVersion = null,
    string? ReleaseName = null,
    string? ReleaseUrl = null,
    DateTimeOffset? PublishedAt = null,
    string? Message = null);

public sealed class UpdateService
{
    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = AppMetadata.CurrentVersionText;
        if (!TryGetRepository(AppMetadata.EffectiveGitHubRepositoryUrl, out var owner, out var repository))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.NotConfigured,
                currentVersion,
                Message: "尚未配置 GitHub 仓库地址。配置后即可自动检查公开 Releases。");
        }

        var endpoint = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/latest";
        try
        {
            using var response = await Client.GetAsync(endpoint, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Failed,
                    currentVersion,
                    Message: "该仓库暂时没有可用的公开 Release。");
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(release.HtmlUrl))
                throw new InvalidDataException("GitHub Release 返回内容不完整。");

            if (!TryParseVersion(release.TagName, out var latestVersion))
                throw new InvalidDataException($"无法识别 Release 版本号：{release.TagName}");

            var status = latestVersion > AppMetadata.CurrentVersion
                ? UpdateCheckStatus.UpdateAvailable
                : UpdateCheckStatus.Latest;

            return new UpdateCheckResult(
                status,
                currentVersion,
                NormalizeVersionText(latestVersion),
                string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                release.HtmlUrl,
                release.PublishedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, currentVersion, Message: "检查更新超时，请稍后重试。");
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, currentVersion, Message: "暂时无法连接 GitHub，请检查网络后重试。");
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, currentVersion, Message: exception.Message);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Cooling-Patch-Update-Checker");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static bool TryGetRepository(string repositoryUrl, out string owner, out string repository)
    {
        owner = string.Empty;
        repository = string.Empty;
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        owner = segments[0];
        repository = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];
        return owner.Length > 0 && repository.Length > 0;
    }

    internal static bool TryParseVersion(string tag, out Version version)
    {
        var normalized = tag.Trim().TrimStart('v', 'V');
        var prereleaseIndex = normalized.IndexOfAny(['-', '+']);
        if (prereleaseIndex >= 0)
            normalized = normalized[..prereleaseIndex];
        return Version.TryParse(normalized, out version!);
    }

    private static string NormalizeVersionText(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }
    }
}

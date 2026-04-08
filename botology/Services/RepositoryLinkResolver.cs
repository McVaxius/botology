using System;
using botology.Models;

namespace botology.Services;

public static class RepositoryLinkResolver
{
    private const string PlaceholderOwner = "OWNER";
    private const string DefaultBranch = "main";

    public static string? ResolveRepoUrl(PluginAssessmentRow row)
        => FirstNonEmpty(row.Entry.RepoUrl, row.RuntimeState?.RepoUrl);

    public static string ResolveRepoJsonUrl(PluginAssessmentRow row)
    {
        if (TryResolveConcreteRepoJsonUrl(row, out var concreteRepoJsonUrl))
            return concreteRepoJsonUrl;

        var repoName = SanitizeRepoName(row.RuntimeState?.InternalName ?? row.Entry.Id);
        return $"https://raw.githubusercontent.com/{PlaceholderOwner}/{repoName}/refs/heads/{DefaultBranch}/repo.json";
    }

    public static bool HasConcreteRepoJsonUrl(PluginAssessmentRow row)
        => TryResolveConcreteRepoJsonUrl(row, out _);

    private static bool TryResolveConcreteRepoJsonUrl(PluginAssessmentRow row, out string repoJsonUrl)
    {
        repoJsonUrl = FirstNonEmpty(row.Entry.RepoJsonUrl, row.RuntimeState?.RepoJsonUrl) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(repoJsonUrl))
            return true;

        return TryBuildGitHubRepoJsonUrl(FirstNonEmpty(row.Entry.RepoUrl, row.RuntimeState?.RepoUrl), out repoJsonUrl);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string SanitizeRepoName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "REPO";

        return value.Trim().Replace(' ', '-');
    }

    private static bool TryBuildGitHubRepoJsonUrl(string? repoUrl, out string repoJsonUrl)
    {
        repoJsonUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(repoUrl))
            return false;

        if (repoUrl.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            repoJsonUrl = repoUrl;
            return true;
        }

        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var repoUri) ||
            !repoUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = repoUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        var owner = segments[0];
        var repo = segments[1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];

        repoJsonUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/refs/heads/{DefaultBranch}/repo.json";
        return true;
    }
}

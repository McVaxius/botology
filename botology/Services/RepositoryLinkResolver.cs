using System;
using System.Collections.Generic;
using System.Linq;
using botology.Models;

namespace botology.Services;

public static class RepositoryLinkResolver
{
    private const string PlaceholderOwner = "OWNER";
    private const string DefaultBranch = "main";

    public static string? ResolveRepoUrl(PluginAssessmentRow row)
        => FirstNonEmpty(row.Entry.RepoUrl, row.RuntimeState?.RepoUrl);

    public static IReadOnlyList<string> ResolveRepoJsonUrls(PluginAssessmentRow row)
    {
        if (TryResolveConcreteRepoJsonUrls(row, out var repoJsonUrls))
            return repoJsonUrls;

        return [BuildPlaceholderRepoJsonUrl(row)];
    }

    public static string ResolveRepoJsonUrl(PluginAssessmentRow row)
    {
        var repoJsonUrls = ResolveRepoJsonUrls(row);
        return repoJsonUrls.FirstOrDefault() ?? BuildPlaceholderRepoJsonUrl(row);
    }

    public static bool HasConcreteRepoJsonUrl(PluginAssessmentRow row)
        => TryResolveConcreteRepoJsonUrls(row, out _);

    private static bool TryResolveConcreteRepoJsonUrls(PluginAssessmentRow row, out string[] repoJsonUrls)
    {
        var resolvedUrls = new List<string>();
        AddIfUnique(resolvedUrls, row.Entry.RepoJsonUrls);
        AddIfUnique(resolvedUrls, row.Entry.RepoJsonUrl);
        AddIfUnique(resolvedUrls, row.RuntimeState?.RepoJsonUrl);

        if (resolvedUrls.Count > 0)
        {
            repoJsonUrls = resolvedUrls.ToArray();
            return true;
        }

        if (TryBuildGitHubRepoJsonUrl(FirstNonEmpty(row.Entry.RepoUrl, row.RuntimeState?.RepoUrl), out var repoJsonUrl))
        {
            repoJsonUrls = [repoJsonUrl];
            return true;
        }

        repoJsonUrls = [];
        return false;
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

    private static string BuildPlaceholderRepoJsonUrl(PluginAssessmentRow row)
    {
        var repoName = SanitizeRepoName(row.RuntimeState?.InternalName ?? row.Entry.Id);
        return $"https://raw.githubusercontent.com/{PlaceholderOwner}/{repoName}/refs/heads/{DefaultBranch}/repo.json";
    }

    private static void AddIfUnique(List<string> values, IEnumerable<string>? candidates)
    {
        if (candidates is null)
            return;

        foreach (var candidate in candidates)
            AddIfUnique(values, candidate);
    }

    private static void AddIfUnique(List<string> values, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return;

        var trimmed = candidate.Trim();
        if (!values.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            values.Add(trimmed);
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

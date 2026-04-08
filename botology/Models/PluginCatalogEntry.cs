namespace botology.Models;

public sealed record PluginCatalogEntry(
    string Id,
    string Category,
    string DisplayName,
    string[] MatchTokens,
    string Notes,
    string? RepoUrl = null,
    string? RepoJsonUrl = null,
    string? RuleType = null,
    string[]? RelatedIds = null,
    string? Description = null,
    string[]? RepoJsonUrls = null);

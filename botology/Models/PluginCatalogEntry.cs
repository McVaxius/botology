namespace botology.Models;

public sealed record PluginCatalogEntry(
    string Id,
    string Category,
    string DisplayName,
    string[] MatchTokens,
    string Notes,
    string? RepoUrl = null,
    string? RepoJsonUrl = null,
    string? Description = null,
    string[]? RepoJsonUrls = null,
    string[]? GreenIds = null,
    string[]? YellowIds = null,
    string[]? RedIds = null,
    CatalogEntrySourceKind SourceKind = CatalogEntrySourceKind.Master,
    bool HasLocalChanges = false,
    bool IsAiAttributed = false)
{
    public string SourceLabel => SourceKind switch
    {
        CatalogEntrySourceKind.LocalOverride => "local override",
        CatalogEntrySourceKind.LocalOnly => "local only",
        CatalogEntrySourceKind.HiddenMaster => "hidden master",
        _ => "master",
    };
}

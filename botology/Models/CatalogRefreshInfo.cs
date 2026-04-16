using System;

namespace botology.Models;

public sealed record CatalogRefreshInfo(
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? LastUpdatedUtc,
    string? ContentFingerprint,
    string? SourceUrl,
    bool RefreshInProgress);

public sealed record MasterCatalogRefreshResult(
    bool Attempted,
    bool Changed,
    string Message);

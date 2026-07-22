using System;
using System.Collections.Generic;

namespace botology.Models;

public sealed class CatalogChangelog
{
    public int SchemaVersion { get; init; } = 1;

    public List<CatalogRelease> Releases { get; init; } = [];
}

public sealed class CatalogRelease
{
    public string Id { get; init; } = string.Empty;

    public DateTimeOffset PublishedUtc { get; init; }

    public string Title { get; init; } = string.Empty;

    public string[] AffectedPluginIds { get; init; } = [];

    public CatalogReleaseSection[] Sections { get; init; } = [];
}

public sealed class CatalogReleaseSection
{
    public string Heading { get; init; } = string.Empty;

    public string[] Items { get; init; } = [];
}

public sealed record CatalogChangelogRefreshResult(
    bool HasValidRemoteData,
    string Message);

public sealed record CatalogNotificationEvaluation(
    string NewestReleaseId,
    bool HasNewRelease,
    IReadOnlyList<string> InstalledPluginNames);

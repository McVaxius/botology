using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using botology.Models;

namespace botology.Services;

public static class RepositoryLinkCatalog
{
    private const string FileName = "plugin-repository-links.json";
    private static readonly StringComparer LinkComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static IReadOnlyList<RepositoryLinkEntry> cachedEntries = Array.Empty<RepositoryLinkEntry>();
    private static IReadOnlyDictionary<string, (string? RepoUrl, string? RepoJsonUrl)> cachedLinks =
        new Dictionary<string, (string? RepoUrl, string? RepoJsonUrl)>(LinkComparer);

    private static bool manifestLoaded;

    public static IReadOnlyDictionary<string, (string? RepoUrl, string? RepoJsonUrl)> GetLinks()
    {
        EnsureManifestLoaded();
        return cachedLinks;
    }

    public static IReadOnlyList<PluginCatalogEntry> GetCatalogEntries(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureManifestLoaded();
        if (cachedEntries.Count == 0)
            return fallbackEntries;

        var fallbackById = fallbackEntries.ToDictionary(entry => entry.Id, LinkComparer);
        var mergedEntries = new List<PluginCatalogEntry>(Math.Max(cachedEntries.Count, fallbackEntries.Count));
        var seenIds = new HashSet<string>(LinkComparer);

        foreach (var manifestEntry in cachedEntries)
        {
            var id = manifestEntry.Id.Trim();
            fallbackById.TryGetValue(id, out var fallbackEntry);

            var matchTokens = manifestEntry.MatchTokens
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(token => token.Trim())
                .ToArray();
            if (matchTokens.Length == 0)
                matchTokens = fallbackEntry?.MatchTokens ?? [id];

            var relatedIds = manifestEntry.RelatedIds
                .Where(relatedId => !string.IsNullOrWhiteSpace(relatedId))
                .Select(relatedId => relatedId.Trim())
                .ToArray();
            if (relatedIds.Length == 0 && fallbackEntry?.RelatedIds is { Length: > 0 } fallbackRelatedIds)
                relatedIds = fallbackRelatedIds;

            mergedEntries.Add(new PluginCatalogEntry(
                id,
                FirstNonEmpty(manifestEntry.Category, fallbackEntry?.Category) ?? "Uncategorized",
                FirstNonEmpty(manifestEntry.DisplayName, fallbackEntry?.DisplayName) ?? id,
                matchTokens,
                FirstNonEmpty(manifestEntry.Notes, fallbackEntry?.Notes) ?? "No notes configured.",
                FirstNonEmpty(manifestEntry.RepoUrl, fallbackEntry?.RepoUrl),
                FirstNonEmpty(manifestEntry.RepoJsonUrl, fallbackEntry?.RepoJsonUrl),
                FirstNonEmpty(manifestEntry.RuleType, fallbackEntry?.RuleType),
                relatedIds.Length > 0 ? relatedIds : null,
                FirstNonEmpty(manifestEntry.Description, fallbackEntry?.Description)));
            seenIds.Add(id);
        }

        foreach (var fallbackEntry in fallbackEntries)
        {
            if (seenIds.Add(fallbackEntry.Id))
                mergedEntries.Add(fallbackEntry);
        }

        return mergedEntries;
    }

    public static void Reload()
    {
        cachedEntries = Array.Empty<RepositoryLinkEntry>();
        cachedLinks = new Dictionary<string, (string? RepoUrl, string? RepoJsonUrl)>(LinkComparer);
        manifestLoaded = false;
        _ = GetLinks();
    }

    private static void EnsureManifestLoaded()
    {
        if (manifestLoaded)
            return;

        var path = GetManifestPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            cachedEntries = Array.Empty<RepositoryLinkEntry>();
            cachedLinks = new Dictionary<string, (string? RepoUrl, string? RepoJsonUrl)>(LinkComparer);
            manifestLoaded = true;
            return;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<RepositoryLinkManifest>(File.ReadAllText(path), JsonOptions);
            cachedEntries = (manifest?.Plugins ?? [])
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .ToArray();
            cachedLinks = cachedEntries
                .GroupBy(entry => entry.Id.Trim(), LinkComparer)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var entry = group.Last();
                        return (Normalize(entry.RepoUrl), Normalize(entry.RepoJsonUrl));
                    },
                    LinkComparer);
            manifestLoaded = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[botology] Failed to load packaged repository link manifest.");
            cachedEntries = Array.Empty<RepositoryLinkEntry>();
            cachedLinks = new Dictionary<string, (string? RepoUrl, string? RepoJsonUrl)>(LinkComparer);
            manifestLoaded = true;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? GetManifestPath()
    {
        var assemblyDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName;
        return string.IsNullOrWhiteSpace(assemblyDirectory)
            ? null
            : Path.Combine(assemblyDirectory, FileName);
    }

    private sealed class RepositoryLinkManifest
    {
        public List<RepositoryLinkEntry> Plugins { get; init; } = [];
    }

    private sealed class RepositoryLinkEntry
    {
        public string Id { get; init; } = string.Empty;

        public string? Category { get; init; }

        public string? DisplayName { get; init; }

        public string[] MatchTokens { get; init; } = [];

        public string? Notes { get; init; }

        public string? RepoUrl { get; init; }

        public string? RepoJsonUrl { get; init; }

        public string? RuleType { get; init; }

        public string[] RelatedIds { get; init; } = [];

        public string? Description { get; init; }
    }
}

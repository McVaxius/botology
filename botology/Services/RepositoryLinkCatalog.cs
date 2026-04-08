using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace botology.Services;

public static class RepositoryLinkCatalog
{
    private const string FileName = "plugin-repository-links.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static IReadOnlyDictionary<string, (string? RepoUrl, string? RepoJsonUrl)> cachedLinks =
        new Dictionary<string, (string? RepoUrl, string? RepoJsonUrl)>(StringComparer.OrdinalIgnoreCase);

    private static string cachedPath = string.Empty;
    private static DateTime cachedLastWriteUtc = DateTime.MinValue;

    public static IReadOnlyDictionary<string, (string? RepoUrl, string? RepoJsonUrl)> GetLinks()
    {
        var path = GetManifestPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            cachedLinks = new Dictionary<string, (string? RepoUrl, string? RepoJsonUrl)>(StringComparer.OrdinalIgnoreCase);
            cachedPath = path ?? string.Empty;
            cachedLastWriteUtc = DateTime.MinValue;
            return cachedLinks;
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        if (string.Equals(path, cachedPath, StringComparison.OrdinalIgnoreCase) &&
            lastWriteUtc == cachedLastWriteUtc)
            return cachedLinks;

        try
        {
            var manifest = JsonSerializer.Deserialize<RepositoryLinkManifest>(File.ReadAllText(path), JsonOptions);
            cachedLinks = (manifest?.Plugins ?? [])
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .GroupBy(entry => entry.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var entry = group.Last();
                        return (Normalize(entry.RepoUrl), Normalize(entry.RepoJsonUrl));
                    },
                    StringComparer.OrdinalIgnoreCase);
            cachedPath = path;
            cachedLastWriteUtc = lastWriteUtc;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[botology] Failed to load packaged repository link manifest.");
            cachedLinks = new Dictionary<string, (string? RepoUrl, string? RepoJsonUrl)>(StringComparer.OrdinalIgnoreCase);
            cachedPath = path;
            cachedLastWriteUtc = lastWriteUtc;
        }

        return cachedLinks;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? GetManifestPath()
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

        public string? RepoUrl { get; init; }

        public string? RepoJsonUrl { get; init; }
    }
}

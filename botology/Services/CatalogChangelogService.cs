using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using botology.Models;

namespace botology.Services;

public sealed class CatalogChangelogService : IDisposable
{
    private const string CacheFileName = "botology-catalog-changelog.json";
    private const string RemoteSourceUrl = "https://raw.githubusercontent.com/McVaxius/botologyupdates/refs/heads/main/changelog.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    private readonly object gate = new();
    private readonly string cachePath;
    private readonly HttpClient httpClient;
    private readonly Action<Exception?, string> warningLogger;
    private CatalogChangelog cachedChangelog = new();
    private int refreshInProgress;

    public CatalogChangelogService(string configurationDirectory, IPluginLog log)
    {
        cachePath = Path.Combine(configurationDirectory, CacheFileName);
        httpClient = BuildHttpClient();
        warningLogger = (exception, message) =>
        {
            if (exception == null)
                log.Warning(message);
            else
                log.Warning(exception, message);
        };
        LoadCache();
    }

    internal CatalogChangelogService(
        string configurationDirectory,
        HttpClient httpClient,
        Action<Exception?, string> warningLogger)
    {
        cachePath = Path.Combine(configurationDirectory, CacheFileName);
        this.httpClient = httpClient;
        this.warningLogger = warningLogger;
        LoadCache();
    }

    public IReadOnlyList<CatalogRelease> GetReleases()
    {
        lock (gate)
            return cachedChangelog.Releases.ToArray();
    }

    public async Task<CatalogChangelogRefreshResult> RefreshAsync()
    {
        if (Interlocked.Exchange(ref refreshInProgress, 1) != 0)
            return new CatalogChangelogRefreshResult(false, "Catalog changelog refresh is already running.");

        try
        {
            using var response = await httpClient.GetAsync(RemoteSourceUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var message = $"Catalog changelog request failed: {(int)response.StatusCode} {response.ReasonPhrase}";
                Warn($"[botology] {message} Keeping the last valid cache.");
                return new CatalogChangelogRefreshResult(false, message);
            }

            var rawJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!TryParseAndValidate(rawJson, out var changelog, out var validationError))
            {
                var message = $"Catalog changelog data was invalid: {validationError}";
                Warn($"[botology] {message} Keeping the last valid cache.");
                return new CatalogChangelogRefreshResult(false, message);
            }

            try
            {
                SaveCache(changelog);
            }
            catch (Exception ex)
            {
                Warn("[botology] Catalog changelog was valid but could not be written to cache.", ex);
            }

            lock (gate)
                cachedChangelog = changelog;

            return new CatalogChangelogRefreshResult(true, "Catalog changelog refreshed.");
        }
        catch (Exception ex)
        {
            Warn("[botology] Catalog changelog refresh failed; keeping the last valid cache.", ex);
            return new CatalogChangelogRefreshResult(false, $"Catalog changelog refresh failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref refreshInProgress, 0);
        }
    }

    public static CatalogNotificationEvaluation EvaluateNotifications(
        IReadOnlyList<CatalogRelease> releases,
        string? lastProcessedReleaseId,
        IReadOnlyList<PluginCatalogEntry> masterEntries,
        PluginSnapshot installedSnapshot)
    {
        if (releases.Count == 0)
            return new CatalogNotificationEvaluation(string.Empty, false, Array.Empty<string>());

        var newestReleaseId = releases[0].Id;
        IReadOnlyList<CatalogRelease> releasesToEvaluate;
        if (string.IsNullOrWhiteSpace(lastProcessedReleaseId))
        {
            releasesToEvaluate = [releases[0]];
        }
        else
        {
            var processedIndex = -1;
            for (var index = 0; index < releases.Count; index++)
            {
                if (releases[index].Id.Equals(lastProcessedReleaseId, StringComparison.OrdinalIgnoreCase))
                {
                    processedIndex = index;
                    break;
                }
            }

            releasesToEvaluate = processedIndex switch
            {
                0 => Array.Empty<CatalogRelease>(),
                > 0 => releases.Take(processedIndex).ToArray(),
                _ => [releases[0]],
            };
        }

        if (releasesToEvaluate.Count == 0)
            return new CatalogNotificationEvaluation(newestReleaseId, false, Array.Empty<string>());

        var masterById = masterEntries
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var installedNames = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var release in releasesToEvaluate)
        {
            foreach (var affectedId in release.AffectedPluginIds)
            {
                if (!seenIds.Add(affectedId) || !masterById.TryGetValue(affectedId, out var entry))
                    continue;

                var candidates = new[] { entry.Id, entry.DisplayName }
                    .Concat(entry.MatchTokens)
                    .ToArray();
                if (installedSnapshot.IsInstalled(candidates))
                    installedNames.Add(entry.DisplayName);
            }
        }

        return new CatalogNotificationEvaluation(newestReleaseId, true, installedNames);
    }

    public static string BuildNotificationMessage(IReadOnlyList<string> installedPluginNames)
    {
        var displayedNames = installedPluginNames.Take(3).ToArray();
        var remainder = installedPluginNames.Count - displayedNames.Length;
        var suffix = remainder > 0 ? $" (+{remainder} more)" : string.Empty;
        return $"Catalog update affects installed plugins: {string.Join(", ", displayedNames)}{suffix}. Open Patch Notes in Botology.";
    }

    public void Dispose()
        => httpClient.Dispose();

    private void LoadCache()
    {
        if (!File.Exists(cachePath))
            return;

        try
        {
            var rawJson = File.ReadAllText(cachePath);
            if (!TryParseAndValidate(rawJson, out var changelog, out var validationError))
            {
                Warn($"[botology] Cached catalog changelog is invalid: {validationError}");
                return;
            }

            lock (gate)
                cachedChangelog = changelog;
        }
        catch (Exception ex)
        {
            Warn("[botology] Failed to load cached catalog changelog.", ex);
        }
    }

    private void SaveCache(CatalogChangelog changelog)
    {
        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(changelog, JsonOptions));
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static HttpClient BuildHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("botology", "1.0"));
        return client;
    }

    private void Warn(string message, Exception? exception = null)
        => warningLogger(exception, message);

    private static bool TryParseAndValidate(
        string rawJson,
        out CatalogChangelog changelog,
        out string validationError)
    {
        changelog = new CatalogChangelog();
        validationError = string.Empty;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            validationError = "response was empty";
            return false;
        }

        CatalogChangelog? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CatalogChangelog>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            validationError = ex.Message;
            return false;
        }

        if (parsed == null || parsed.SchemaVersion != 1)
        {
            validationError = "SchemaVersion must be 1";
            return false;
        }

        if (parsed.Releases.Count == 0)
        {
            validationError = "Releases must contain at least one release";
            return false;
        }

        var normalizedReleases = new List<CatalogRelease>(parsed.Releases.Count);
        var releaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset? previousPublishedUtc = null;
        foreach (var release in parsed.Releases)
        {
            var releaseId = release.Id.Trim();
            if (string.IsNullOrWhiteSpace(releaseId) || !releaseIds.Add(releaseId))
            {
                validationError = "release IDs must be non-empty and unique";
                return false;
            }

            if (release.PublishedUtc == default || release.PublishedUtc.Offset != TimeSpan.Zero)
            {
                validationError = $"release '{releaseId}' must have an ISO UTC PublishedUtc value";
                return false;
            }

            if (previousPublishedUtc is not null && release.PublishedUtc > previousPublishedUtc)
            {
                validationError = "Releases must be newest-first";
                return false;
            }

            previousPublishedUtc = release.PublishedUtc;
            var title = release.Title.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                validationError = $"release '{releaseId}' has an empty Title";
                return false;
            }

            var affectedIds = release.AffectedPluginIds
                .Select(id => id?.Trim() ?? string.Empty)
                .ToArray();
            if (affectedIds.Any(string.IsNullOrWhiteSpace) ||
                affectedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != affectedIds.Length)
            {
                validationError = $"release '{releaseId}' has empty or duplicate AffectedPluginIds";
                return false;
            }

            if (release.Sections.Length == 0)
            {
                validationError = $"release '{releaseId}' must have at least one section";
                return false;
            }

            var sections = new List<CatalogReleaseSection>(release.Sections.Length);
            foreach (var section in release.Sections)
            {
                var heading = section.Heading.Trim();
                var items = section.Items.Select(item => item?.Trim() ?? string.Empty).ToArray();
                if (string.IsNullOrWhiteSpace(heading) || items.Length == 0 || items.Any(string.IsNullOrWhiteSpace))
                {
                    validationError = $"release '{releaseId}' contains an invalid section";
                    return false;
                }

                sections.Add(new CatalogReleaseSection { Heading = heading, Items = items });
            }

            normalizedReleases.Add(new CatalogRelease
            {
                Id = releaseId,
                PublishedUtc = release.PublishedUtc,
                Title = title,
                AffectedPluginIds = affectedIds,
                Sections = sections.ToArray(),
            });
        }

        changelog = new CatalogChangelog { SchemaVersion = 1, Releases = normalizedReleases };
        return true;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using botology.Models;

namespace botology.Services;

public static class RepositoryLinkCatalog
{
    private const string LegacyFileName = "plugin-repository-links.json";
    private const string MasterCacheFileName = "botology-master-catalog.json";
    private const string OverlayFileName = "botology-local-overlay.json";
    private const string RefreshStateFileName = "botology-master-catalog-state.json";
    private const string RemoteSourceUrl = "https://raw.githubusercontent.com/McVaxius/botologyupdates/refs/heads/main/plugin-repository-links.json";

    private static readonly string[] KnownRotationIds =
    {
        "bossmod",
        "bossmod_reborn",
        "wrath",
        "rotation_solver_reborn",
        "ultimate_combo",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    private static readonly object gate = new();
    private static readonly HttpClient httpClient = BuildHttpClient();

    private static IReadOnlyList<PluginCatalogEntry> cachedMasterEntries = Array.Empty<PluginCatalogEntry>();
    private static IReadOnlyDictionary<string, PluginCatalogEntry> cachedOverlayEntries =
        new Dictionary<string, PluginCatalogEntry>(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyList<PluginCatalogEntry> effectiveEntries = Array.Empty<PluginCatalogEntry>();
    private static CatalogRefreshState refreshState = new();
    private static bool manifestLoaded;
    private static bool refreshInProgress;

    public static IReadOnlyList<PluginCatalogEntry> GetCatalogEntries(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);
        lock (gate)
            return effectiveEntries;
    }

    public static IReadOnlyList<PluginCatalogEntry> GetMasterEntries(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);
        lock (gate)
            return cachedMasterEntries;
    }

    public static IReadOnlyList<PluginCatalogEntry> GetOverlayEntries(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);
        lock (gate)
            return cachedOverlayEntries.Values.OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    public static PluginCatalogEntry? GetMasterEntry(string id, IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);
        lock (gate)
            return cachedMasterEntries.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public static PluginCatalogEntry? GetOverlayEntry(string id, IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);
        lock (gate)
            return cachedOverlayEntries.TryGetValue(id, out var entry) ? entry : null;
    }

    public static IReadOnlyList<string> GetKnownIds(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);
        lock (gate)
        {
            return effectiveEntries
                .Select(entry => entry.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static CatalogRefreshInfo GetRefreshInfo()
    {
        EnsureRefreshStateLoaded();
        lock (gate)
        {
            return new CatalogRefreshInfo(
                refreshState.LastCheckedUtc,
                refreshState.LastUpdatedUtc,
                refreshState.ContentFingerprint,
                refreshState.SourceUrl,
                refreshInProgress);
        }
    }

    public static DateTimeOffset GetNextAutomaticCheckUtc(TimeSpan interval)
    {
        EnsureRefreshStateLoaded();
        lock (gate)
        {
            return (refreshState.LastCheckedUtc ?? DateTimeOffset.MinValue) + interval;
        }
    }

    public static string GetCatalogDirectory()
        => Plugin.PluginInterface.GetPluginConfigDirectory();

    public static string GetOverlayPath()
        => Path.Combine(GetCatalogDirectory(), OverlayFileName);

    public static void Reload()
    {
        lock (gate)
        {
            cachedMasterEntries = Array.Empty<PluginCatalogEntry>();
            cachedOverlayEntries = new Dictionary<string, PluginCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            effectiveEntries = Array.Empty<PluginCatalogEntry>();
            manifestLoaded = false;
        }
    }

    public static void SaveOverlayEntry(PluginCatalogEntry entry, IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);

        lock (gate)
        {
            var overlayEntries = cachedOverlayEntries.Values
                .ToDictionary(existing => existing.Id, existing => StripRuntimeMetadata(existing), StringComparer.OrdinalIgnoreCase);
            overlayEntries[entry.Id] = StripRuntimeMetadata(entry);

            SaveOverlayManifestLocked(overlayEntries.Values);
            cachedOverlayEntries = overlayEntries;
            effectiveEntries = MergeEntriesLocked(cachedMasterEntries, cachedOverlayEntries);
        }
    }

    public static bool RemoveOverlayEntry(string id, IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);

        lock (gate)
        {
            var overlayEntries = cachedOverlayEntries.Values
                .ToDictionary(existing => existing.Id, existing => StripRuntimeMetadata(existing), StringComparer.OrdinalIgnoreCase);
            if (!overlayEntries.Remove(id))
                return false;

            SaveOverlayManifestLocked(overlayEntries.Values);
            cachedOverlayEntries = overlayEntries;
            effectiveEntries = MergeEntriesLocked(cachedMasterEntries, cachedOverlayEntries);
            return true;
        }
    }

    public static int ClearOverlayEntries(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        EnsureCatalogLoaded(fallbackEntries);

        lock (gate)
        {
            var removedCount = cachedOverlayEntries.Count;
            if (removedCount == 0)
                return 0;

            cachedOverlayEntries = new Dictionary<string, PluginCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            SaveOverlayManifestLocked(Array.Empty<PluginCatalogEntry>());
            effectiveEntries = MergeEntriesLocked(cachedMasterEntries, cachedOverlayEntries);
            return removedCount;
        }
    }

    public static async Task<MasterCatalogRefreshResult> RefreshMasterCatalogAsync(
        IReadOnlyList<PluginCatalogEntry> fallbackEntries,
        bool force = false)
    {
        EnsureCatalogLoaded(fallbackEntries);

        lock (gate)
        {
            if (refreshInProgress)
                return new MasterCatalogRefreshResult(false, false, "Master catalog refresh is already running.");

            refreshInProgress = true;
        }

        try
        {
            using var response = await httpClient.GetAsync(RemoteSourceUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                UpdateRefreshStateOnly(DateTimeOffset.UtcNow, refreshState.LastUpdatedUtc, refreshState.ContentFingerprint, refreshState.SourceUrl);
                return new MasterCatalogRefreshResult(true, false, $"Master catalog request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var rawJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var parsedEntries = ParseCatalogEntries(rawJson, fallbackEntries);
            if (parsedEntries.Count == 0)
            {
                UpdateRefreshStateOnly(DateTimeOffset.UtcNow, refreshState.LastUpdatedUtc, refreshState.ContentFingerprint, refreshState.SourceUrl);
                return new MasterCatalogRefreshResult(true, false, "Master catalog fetch returned no plugin rows.");
            }

            var fingerprint = ComputeFingerprint(rawJson);
            var now = DateTimeOffset.UtcNow;
            bool changed;

            lock (gate)
            {
                changed = force ||
                    !File.Exists(GetMasterCachePath()) ||
                    !string.Equals(refreshState.ContentFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);

                if (changed)
                {
                    SaveMasterManifestLocked(parsedEntries, RemoteSourceUrl);
                    cachedMasterEntries = parsedEntries;

                    var prunedOverlay = PruneOverlayAgainstMasterLocked(cachedOverlayEntries, cachedMasterEntries);
                    if (prunedOverlay.Count != cachedOverlayEntries.Count)
                    {
                        cachedOverlayEntries = prunedOverlay;
                        SaveOverlayManifestLocked(cachedOverlayEntries.Values);
                    }

                    effectiveEntries = MergeEntriesLocked(cachedMasterEntries, cachedOverlayEntries);
                }

                refreshState = refreshState with
                {
                    SourceUrl = RemoteSourceUrl,
                    LastCheckedUtc = now,
                    LastUpdatedUtc = changed ? now : refreshState.LastUpdatedUtc,
                    ContentFingerprint = changed ? fingerprint : refreshState.ContentFingerprint,
                };
                SaveRefreshStateLocked();
            }

            return changed
                ? new MasterCatalogRefreshResult(true, true, "Master catalog changed and was refreshed.")
                : new MasterCatalogRefreshResult(true, false, "Master catalog checked; no content changes were found.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[botology] Failed to refresh master catalog.");
            UpdateRefreshStateOnly(DateTimeOffset.UtcNow, refreshState.LastUpdatedUtc, refreshState.ContentFingerprint, refreshState.SourceUrl);
            return new MasterCatalogRefreshResult(true, false, $"Master catalog refresh failed: {ex.Message}");
        }
        finally
        {
            lock (gate)
                refreshInProgress = false;
        }
    }

    private static void EnsureCatalogLoaded(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        lock (gate)
        {
            if (manifestLoaded)
                return;

            EnsureRefreshStateLoadedLocked();
            MigrateIfNeededLocked(fallbackEntries);

            cachedMasterEntries = LoadMasterEntriesLocked(fallbackEntries);
            if (cachedMasterEntries.Count == 0)
                cachedMasterEntries = fallbackEntries.Select(entry => entry with
                {
                    SourceKind = CatalogEntrySourceKind.Master,
                    HasLocalChanges = false,
                }).ToArray();

            cachedOverlayEntries = LoadOverlayEntriesLocked(fallbackEntries)
                .ToDictionary(entry => entry.Id, entry => StripRuntimeMetadata(entry), StringComparer.OrdinalIgnoreCase);

            effectiveEntries = MergeEntriesLocked(cachedMasterEntries, cachedOverlayEntries);
            manifestLoaded = true;
        }
    }

    private static void EnsureRefreshStateLoaded()
    {
        lock (gate)
            EnsureRefreshStateLoadedLocked();
    }

    private static void EnsureRefreshStateLoadedLocked()
    {
        if (refreshState.Loaded)
            return;

        var path = GetRefreshStatePath();
        if (!File.Exists(path))
        {
            refreshState = new CatalogRefreshState { Loaded = true, SourceUrl = RemoteSourceUrl };
            return;
        }

        try
        {
            var loadedState = JsonSerializer.Deserialize<CatalogRefreshState>(File.ReadAllText(path), JsonOptions);
            refreshState = loadedState == null
                ? new CatalogRefreshState { Loaded = true, SourceUrl = RemoteSourceUrl }
                : loadedState with { Loaded = true, SourceUrl = string.IsNullOrWhiteSpace(loadedState.SourceUrl) ? RemoteSourceUrl : loadedState.SourceUrl };
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[botology] Failed to load master catalog refresh state.");
            refreshState = new CatalogRefreshState { Loaded = true, SourceUrl = RemoteSourceUrl };
        }
    }

    private static void MigrateIfNeededLocked(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        if (File.Exists(GetMasterCachePath()) || File.Exists(GetOverlayPath()))
            return;

        var fallbackMasterEntries = fallbackEntries.Select(entry => entry with
        {
            SourceKind = CatalogEntrySourceKind.Master,
            HasLocalChanges = false,
        }).ToArray();

        var legacyEntries = LoadLegacyEntriesFromAssemblyLocked(fallbackEntries);
        var remoteEntries = TryFetchRemoteEntriesForMigrationLocked(fallbackEntries);
        var chosenMasterEntries = remoteEntries.Count > 0
            ? remoteEntries
            : legacyEntries.Count > 0
                ? legacyEntries.Select(entry => entry with
                {
                    SourceKind = CatalogEntrySourceKind.Master,
                    HasLocalChanges = false,
                }).ToArray()
                : fallbackMasterEntries;

        SaveMasterManifestLocked(chosenMasterEntries, RemoteSourceUrl);

        var overlayEntries = BuildOverlayEntriesLocked(legacyEntries, chosenMasterEntries);
        SaveOverlayManifestLocked(overlayEntries);

        var now = DateTimeOffset.UtcNow;
        refreshState = refreshState with
        {
            Loaded = true,
            SourceUrl = RemoteSourceUrl,
            LastCheckedUtc = remoteEntries.Count > 0 ? now : refreshState.LastCheckedUtc,
            LastUpdatedUtc = now,
            ContentFingerprint = ComputeFingerprint(JsonSerializer.Serialize(new CatalogManifest { Plugins = chosenMasterEntries.Select(ToStoredEntry).ToList() }, JsonOptions)),
        };
        SaveRefreshStateLocked();
    }

    private static IReadOnlyList<PluginCatalogEntry> LoadLegacyEntriesFromAssemblyLocked(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        var legacyPath = GetLegacyManifestPath();
        if (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath))
            return Array.Empty<PluginCatalogEntry>();

        try
        {
            return ParseCatalogEntries(File.ReadAllText(legacyPath), fallbackEntries);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[botology] Failed to load legacy bundled catalog.");
            return Array.Empty<PluginCatalogEntry>();
        }
    }

    private static IReadOnlyList<PluginCatalogEntry> TryFetchRemoteEntriesForMigrationLocked(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        try
        {
            var rawJson = httpClient.GetStringAsync(RemoteSourceUrl).GetAwaiter().GetResult();
            return ParseCatalogEntries(rawJson, fallbackEntries);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[botology] Initial remote master fetch failed during migration.");
            return Array.Empty<PluginCatalogEntry>();
        }
    }

    private static IReadOnlyList<PluginCatalogEntry> BuildOverlayEntriesLocked(
        IReadOnlyList<PluginCatalogEntry> legacyEntries,
        IReadOnlyList<PluginCatalogEntry> masterEntries)
    {
        if (legacyEntries.Count == 0)
            return Array.Empty<PluginCatalogEntry>();

        var masterById = masterEntries.ToDictionary(entry => entry.Id, entry => StripRuntimeMetadata(entry), StringComparer.OrdinalIgnoreCase);
        var overlayEntries = new List<PluginCatalogEntry>();

        foreach (var legacyEntry in legacyEntries)
        {
            var strippedLegacyEntry = StripRuntimeMetadata(legacyEntry);
            if (!masterById.TryGetValue(strippedLegacyEntry.Id, out var masterEntry) || !EntriesEquivalent(strippedLegacyEntry, masterEntry))
                overlayEntries.Add(strippedLegacyEntry);
        }

        return overlayEntries;
    }

    private static IReadOnlyList<PluginCatalogEntry> LoadMasterEntriesLocked(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        var path = GetMasterCachePath();
        if (!File.Exists(path))
            return Array.Empty<PluginCatalogEntry>();

        try
        {
            var manifest = JsonSerializer.Deserialize<CatalogManifest>(File.ReadAllText(path), JsonOptions);
            return ConvertStoredEntries(GetStoredEntries(manifest), fallbackEntries, CatalogEntrySourceKind.Master, hasLocalChanges: false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[botology] Failed to load master catalog cache.");
            return Array.Empty<PluginCatalogEntry>();
        }
    }

    private static IReadOnlyList<PluginCatalogEntry> LoadOverlayEntriesLocked(IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        var path = GetOverlayPath();
        if (!File.Exists(path))
            return Array.Empty<PluginCatalogEntry>();

        try
        {
            var manifest = JsonSerializer.Deserialize<CatalogManifest>(File.ReadAllText(path), JsonOptions);
            return ConvertStoredEntries(GetStoredEntries(manifest), fallbackEntries, CatalogEntrySourceKind.LocalOnly, hasLocalChanges: true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[botology] Failed to load local overlay catalog.");
            return Array.Empty<PluginCatalogEntry>();
        }
    }

    private static IReadOnlyList<StoredCatalogEntry> GetStoredEntries(CatalogManifest? manifest)
    {
        if (manifest == null)
            return Array.Empty<StoredCatalogEntry>();

        return manifest.Entries.Count > 0
            ? manifest.Entries
            : manifest.Plugins;
    }

    private static IReadOnlyList<PluginCatalogEntry> ParseCatalogEntries(string rawJson, IReadOnlyList<PluginCatalogEntry> fallbackEntries)
    {
        var manifest = JsonSerializer.Deserialize<CatalogManifest>(rawJson, JsonOptions);
        return ConvertStoredEntries(GetStoredEntries(manifest), fallbackEntries, CatalogEntrySourceKind.Master, hasLocalChanges: false);
    }

    private static IReadOnlyList<PluginCatalogEntry> ConvertStoredEntries(
        IReadOnlyList<StoredCatalogEntry> storedEntries,
        IReadOnlyList<PluginCatalogEntry> fallbackEntries,
        CatalogEntrySourceKind sourceKind,
        bool hasLocalChanges)
    {
        if (storedEntries.Count == 0)
            return Array.Empty<PluginCatalogEntry>();

        var rotationIds = DetectRotationIds(storedEntries);
        var entries = new List<PluginCatalogEntry>(storedEntries.Count);

        foreach (var storedEntry in storedEntries)
        {
            var id = NormalizeText(storedEntry.Id);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var fallbackEntry = fallbackEntries.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var greenIds = NormalizeIdList(storedEntry.Green);
            var yellowIds = NormalizeIdList(storedEntry.Yellow);
            var redIds = NormalizeIdList(storedEntry.Red);

            if (greenIds.Length == 0 && yellowIds.Length == 0 && redIds.Length == 0)
                ApplyLegacyRuleConversion(storedEntry, rotationIds, ref greenIds, ref yellowIds, ref redIds);

            var matchTokens = NormalizeTextList(storedEntry.MatchTokens);
            if (matchTokens.Length == 0)
                matchTokens = fallbackEntry?.MatchTokens ?? [id];

            var repoJsonUrls = NormalizeTextList(storedEntry.RepoJsonUrls);
            if (repoJsonUrls.Length == 0 && fallbackEntry?.RepoJsonUrls is { Length: > 0 } fallbackRepoJsonUrls)
                repoJsonUrls = fallbackRepoJsonUrls;

            entries.Add(new PluginCatalogEntry(
                id,
                FirstNonEmpty(storedEntry.Category, fallbackEntry?.Category) ?? "Uncategorized",
                FirstNonEmpty(storedEntry.DisplayName, fallbackEntry?.DisplayName) ?? id,
                matchTokens,
                FirstNonEmpty(storedEntry.Notes, fallbackEntry?.Notes) ?? "No notes configured.",
                FirstNonEmpty(storedEntry.RepoUrl, fallbackEntry?.RepoUrl),
                FirstNonEmpty(storedEntry.RepoJsonUrl, fallbackEntry?.RepoJsonUrl),
                FirstNonEmpty(storedEntry.Description, fallbackEntry?.Description),
                repoJsonUrls.Length > 0 ? repoJsonUrls : null,
                greenIds.Length > 0 ? greenIds : null,
                yellowIds.Length > 0 ? yellowIds : null,
                redIds.Length > 0 ? redIds : null,
                sourceKind,
                hasLocalChanges));
        }

        return entries
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> DetectRotationIds(IEnumerable<StoredCatalogEntry> storedEntries)
    {
        var ids = new HashSet<string>(KnownRotationIds, StringComparer.OrdinalIgnoreCase);
        foreach (var storedEntry in storedEntries)
        {
            if (string.Equals(storedEntry.RuleType, "rotation_conflict", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(storedEntry.RuleType, "bossmod_pair", StringComparison.OrdinalIgnoreCase))
            {
                var id = NormalizeText(storedEntry.Id);
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
        }

        return ids;
    }

    private static void ApplyLegacyRuleConversion(
        StoredCatalogEntry storedEntry,
        HashSet<string> rotationIds,
        ref string[] greenIds,
        ref string[] yellowIds,
        ref string[] redIds)
    {
        var relatedIds = NormalizeIdList(storedEntry.RelatedIds);
        switch (NormalizeText(storedEntry.RuleType))
        {
            case "autoduty_conflict":
                redIds = ["autoduty"];
                break;

            case "paired_conflict_yellow":
                yellowIds = relatedIds;
                break;

            case "paired_conflict_red":
            case "direct_conflict_red":
                redIds = relatedIds;
                break;

            case "rotation_conflict":
                yellowIds = rotationIds
                    .Where(id => !id.Equals(storedEntry.Id, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                break;

            case "bossmod_pair":
                redIds = relatedIds;
                var explicitRedIds = redIds;
                yellowIds = rotationIds
                    .Where(id => !id.Equals(storedEntry.Id, StringComparison.OrdinalIgnoreCase))
                    .Where(id => explicitRedIds.All(redId => !redId.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                break;

            case "loaded_warning_yellow":
                yellowIds = [NormalizeText(storedEntry.Id)];
                break;
        }
    }

    private static IReadOnlyList<PluginCatalogEntry> MergeEntriesLocked(
        IReadOnlyList<PluginCatalogEntry> masterEntries,
        IReadOnlyDictionary<string, PluginCatalogEntry> overlayEntries)
    {
        var mergedEntries = new List<PluginCatalogEntry>(Math.Max(masterEntries.Count, overlayEntries.Count));
        var masterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var masterEntry in masterEntries)
        {
            if (overlayEntries.TryGetValue(masterEntry.Id, out var overlayEntry))
            {
                mergedEntries.Add(StripRuntimeMetadata(overlayEntry) with
                {
                    SourceKind = CatalogEntrySourceKind.LocalOverride,
                    HasLocalChanges = true,
                });
            }
            else
            {
                mergedEntries.Add(masterEntry with
                {
                    SourceKind = CatalogEntrySourceKind.Master,
                    HasLocalChanges = false,
                });
            }

            masterIds.Add(masterEntry.Id);
        }

        foreach (var overlayEntry in overlayEntries.Values.Where(entry => !masterIds.Contains(entry.Id)))
        {
            mergedEntries.Add(StripRuntimeMetadata(overlayEntry) with
            {
                SourceKind = CatalogEntrySourceKind.LocalOnly,
                HasLocalChanges = true,
            });
        }

        return mergedEntries
            .OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, PluginCatalogEntry> PruneOverlayAgainstMasterLocked(
        IReadOnlyDictionary<string, PluginCatalogEntry> overlayEntries,
        IReadOnlyList<PluginCatalogEntry> masterEntries)
    {
        var masterById = masterEntries.ToDictionary(entry => entry.Id, entry => StripRuntimeMetadata(entry), StringComparer.OrdinalIgnoreCase);
        var prunedEntries = new Dictionary<string, PluginCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var overlayEntry in overlayEntries.Values)
        {
            var strippedOverlay = StripRuntimeMetadata(overlayEntry);
            if (!masterById.TryGetValue(strippedOverlay.Id, out var masterEntry) || !EntriesEquivalent(strippedOverlay, masterEntry))
                prunedEntries[strippedOverlay.Id] = strippedOverlay;
        }

        return prunedEntries;
    }

    private static void SaveMasterManifestLocked(IEnumerable<PluginCatalogEntry> entries, string sourceUrl)
    {
        EnsureCatalogDirectoryExistsLocked();
        var manifest = new CatalogManifest
        {
            SchemaVersion = 3,
            SourceUrl = sourceUrl,
            Plugins = entries.Select(ToStoredEntry).OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        File.WriteAllText(GetMasterCachePath(), JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void SaveOverlayManifestLocked(IEnumerable<PluginCatalogEntry> entries)
    {
        EnsureCatalogDirectoryExistsLocked();
        var manifest = new CatalogManifest
        {
            SchemaVersion = 3,
            Entries = entries.Select(ToStoredEntry).OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        File.WriteAllText(GetOverlayPath(), JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void SaveRefreshStateLocked()
    {
        EnsureCatalogDirectoryExistsLocked();
        File.WriteAllText(GetRefreshStatePath(), JsonSerializer.Serialize(refreshState, JsonOptions));
    }

    private static void EnsureCatalogDirectoryExistsLocked()
    {
        var directory = GetCatalogDirectory();
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private static void UpdateRefreshStateOnly(
        DateTimeOffset? lastCheckedUtc,
        DateTimeOffset? lastUpdatedUtc,
        string? contentFingerprint,
        string? sourceUrl)
    {
        lock (gate)
        {
            refreshState = refreshState with
            {
                Loaded = true,
                LastCheckedUtc = lastCheckedUtc,
                LastUpdatedUtc = lastUpdatedUtc,
                ContentFingerprint = contentFingerprint,
                SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? RemoteSourceUrl : sourceUrl,
            };
            SaveRefreshStateLocked();
        }
    }

    private static PluginCatalogEntry StripRuntimeMetadata(PluginCatalogEntry entry)
        => entry with
        {
            SourceKind = CatalogEntrySourceKind.Master,
            HasLocalChanges = false,
        };

    private static bool EntriesEquivalent(PluginCatalogEntry left, PluginCatalogEntry right)
        => string.Equals(NormalizeText(left.Id), NormalizeText(right.Id), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.Category), NormalizeText(right.Category), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.DisplayName), NormalizeText(right.DisplayName), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.Notes), NormalizeText(right.Notes), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.RepoUrl), NormalizeText(right.RepoUrl), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.RepoJsonUrl), NormalizeText(right.RepoJsonUrl), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.Description), NormalizeText(right.Description), StringComparison.OrdinalIgnoreCase) &&
           ListsEqual(left.MatchTokens, right.MatchTokens) &&
           ListsEqual(left.RepoJsonUrls, right.RepoJsonUrls) &&
           ListsEqual(left.GreenIds, right.GreenIds) &&
           ListsEqual(left.YellowIds, right.YellowIds) &&
           ListsEqual(left.RedIds, right.RedIds);

    private static bool ListsEqual(IEnumerable<string>? left, IEnumerable<string>? right)
        => NormalizeTextList(left).SequenceEqual(NormalizeTextList(right), StringComparer.OrdinalIgnoreCase);

    private static StoredCatalogEntry ToStoredEntry(PluginCatalogEntry entry)
        => new()
        {
            Id = entry.Id,
            Category = entry.Category,
            DisplayName = entry.DisplayName,
            MatchTokens = NormalizeTextList(entry.MatchTokens),
            Notes = entry.Notes,
            RepoUrl = entry.RepoUrl,
            RepoJsonUrl = entry.RepoJsonUrl,
            Description = entry.Description,
            RepoJsonUrls = NormalizeTextList(entry.RepoJsonUrls),
            Green = NormalizeIdList(entry.GreenIds),
            Yellow = NormalizeIdList(entry.YellowIds),
            Red = NormalizeIdList(entry.RedIds),
        };

    private static string[] NormalizeTextList(IEnumerable<string>? values)
        => values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static string[] NormalizeIdList(IEnumerable<string>? values)
        => NormalizeTextList(values)
            .Select(NormalizeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string ComputeFingerprint(string rawJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawJson));
        return Convert.ToHexString(bytes);
    }

    private static string? GetLegacyManifestPath()
    {
        var assemblyDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName;
        return string.IsNullOrWhiteSpace(assemblyDirectory)
            ? null
            : Path.Combine(assemblyDirectory, LegacyFileName);
    }

    private static string GetMasterCachePath()
        => Path.Combine(GetCatalogDirectory(), MasterCacheFileName);

    private static string GetRefreshStatePath()
        => Path.Combine(GetCatalogDirectory(), RefreshStateFileName);

    private static HttpClient BuildHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("botology", "1.0"));
        return client;
    }

    private sealed class CatalogManifest
    {
        public int SchemaVersion { get; init; } = 3;

        public string? SourceUrl { get; init; }

        public List<StoredCatalogEntry> Plugins { get; init; } = [];

        public List<StoredCatalogEntry> Entries { get; init; } = [];
    }

    private sealed class StoredCatalogEntry
    {
        public string Id { get; init; } = string.Empty;

        public string? Category { get; init; }

        public string? DisplayName { get; init; }

        public string[] MatchTokens { get; init; } = [];

        public string? Notes { get; init; }

        public string? RepoUrl { get; init; }

        public string? RepoJsonUrl { get; init; }

        public string[] RepoJsonUrls { get; init; } = [];

        public string? Description { get; init; }

        public string[] Green { get; init; } = [];

        public string[] Yellow { get; init; } = [];

        public string[] Red { get; init; } = [];

        public string? RuleType { get; init; }

        public string[] RelatedIds { get; init; } = [];
    }

    private sealed record CatalogRefreshState
    {
        public int SchemaVersion { get; init; } = 1;

        public bool Loaded { get; init; }

        public string? SourceUrl { get; init; }

        public DateTimeOffset? LastCheckedUtc { get; init; }

        public DateTimeOffset? LastUpdatedUtc { get; init; }

        public string? ContentFingerprint { get; init; }
    }
}

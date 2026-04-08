using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using botology.Models;

namespace botology.Services;

public sealed class RepositoryMetadataService : IDisposable
{
    private const string CacheFileName = "plugin-repository-metadata-cache.json";
    private const double CacheLifetimeHours = 24.0;
    private const double RefreshQueueThrottleSeconds = 5.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    private readonly IPluginLog log;
    private readonly HttpClient httpClient;
    private readonly object gate = new();
    private readonly Dictionary<string, CachedMetadataEntry> cache = new(StringComparer.OrdinalIgnoreCase);

    private bool cacheLoaded;
    private bool refreshInProgress;
    private DateTime lastRefreshQueuedUtc = DateTime.MinValue;

    public RepositoryMetadataService(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("botology", "1.0"));
        EnsureCacheLoaded();
    }

    public void Dispose()
        => httpClient.Dispose();

    public PluginRepositoryMetadata? GetCachedMetadata(PluginAssessmentRow row)
    {
        EnsureCacheLoaded();

        lock (gate)
        {
            return cache.TryGetValue(row.Entry.Id, out var entry)
                ? entry.ToMetadata()
                : null;
        }
    }

    public void InvalidateRefreshThrottle()
    {
        lock (gate)
            lastRefreshQueuedUtc = DateTime.MinValue;
    }

    public void QueueRefresh(IEnumerable<PluginAssessmentRow> rows)
    {
        EnsureCacheLoaded();

        List<MetadataRefreshRequest> requests;
        lock (gate)
        {
            if (refreshInProgress)
                return;

            if ((DateTime.UtcNow - lastRefreshQueuedUtc).TotalSeconds < RefreshQueueThrottleSeconds)
                return;

            requests = rows
                .Select(CreateRefreshRequest)
                .Where(request => request != null)
                .Cast<MetadataRefreshRequest>()
                .Where(NeedsRefresh)
                .GroupBy(request => request.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (requests.Count == 0)
                return;

            refreshInProgress = true;
            lastRefreshQueuedUtc = DateTime.UtcNow;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync(requests).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "[botology] Repository metadata refresh failed.");
            }
            finally
            {
                lock (gate)
                    refreshInProgress = false;
            }
        });
    }

    private async Task RefreshAsync(IReadOnlyList<MetadataRefreshRequest> requests)
    {
        var repoFeedCache = new Dictionary<string, IReadOnlyList<RemoteManifest>>(StringComparer.OrdinalIgnoreCase);
        var githubCache = new Dictionary<string, GitHubMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in requests)
        {
            PluginRepositoryMetadata metadata;
            try
            {
                metadata = await FetchMetadataAsync(request, repoFeedCache, githubCache).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"[botology] Failed to fetch metadata for {request.DisplayName}.");
                metadata = new PluginRepositoryMetadata();
            }

            lock (gate)
            {
                cache[request.Id] = new CachedMetadataEntry
                {
                    Id = request.Id,
                    RepoUrl = request.RepoUrl,
                    RepoJsonUrl = BuildRepoJsonSignature(request.RepoJsonUrls),
                    FetchedAtUtc = DateTime.UtcNow,
                    Description = metadata.Description,
                    Author = metadata.Author,
                    DalamudApiLevel = metadata.DalamudApiLevel,
                    Downloads = metadata.Downloads,
                    LastUpdateUtc = metadata.LastUpdateUtc,
                };
            }
        }

        SaveCache();
    }

    private async Task<PluginRepositoryMetadata> FetchMetadataAsync(
        MetadataRefreshRequest request,
        IDictionary<string, IReadOnlyList<RemoteManifest>> repoFeedCache,
        IDictionary<string, GitHubMetadata> githubCache)
    {
        string? description = null;
        string? author = null;
        int? dalamudApiLevel = null;
        long? downloads = null;
        DateTimeOffset? lastUpdateUtc = null;
        string? resolvedRepoUrl = request.RepoUrl;

        foreach (var candidateRepoJsonUrl in request.RepoJsonUrls)
        {
            if (!TryGetConcreteUrl(candidateRepoJsonUrl, out var repoJsonUrl))
                continue;

            var manifests = await GetRemoteManifestsAsync(repoJsonUrl, repoFeedCache).ConfigureAwait(false);
            var manifest = FindBestManifest(manifests, request);
            if (manifest == null)
                continue;

            description = FirstNonEmpty(description, manifest.Description);
            author = FirstNonEmpty(author, manifest.Author);
            dalamudApiLevel ??= manifest.DalamudApiLevel;
            downloads ??= manifest.Downloads;
            lastUpdateUtc ??= manifest.LastUpdateUtc;
            resolvedRepoUrl = FirstNonEmpty(resolvedRepoUrl, manifest.RepoUrl);
        }

        if (TryParseGitHubRepository(FirstNonEmpty(resolvedRepoUrl, request.RepoUrl), out var owner, out var repo))
        {
            var githubMetadata = await GetGitHubMetadataAsync(owner, repo, githubCache).ConfigureAwait(false);
            description = FirstNonEmpty(description, githubMetadata.Description);
            author = FirstNonEmpty(author, githubMetadata.Author);
            downloads ??= githubMetadata.Downloads;
            lastUpdateUtc ??= githubMetadata.LastUpdateUtc;
        }

        return new PluginRepositoryMetadata(description, author, dalamudApiLevel, downloads, lastUpdateUtc);
    }

    private async Task<IReadOnlyList<RemoteManifest>> GetRemoteManifestsAsync(
        string repoJsonUrl,
        IDictionary<string, IReadOnlyList<RemoteManifest>> repoFeedCache)
    {
        if (repoFeedCache.TryGetValue(repoJsonUrl, out var cachedManifests))
            return cachedManifests;

        using var response = await httpClient.GetAsync(repoJsonUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

        var manifests = EnumerateManifestCandidates(document.RootElement)
            .Select(ParseRemoteManifest)
            .Where(manifest => manifest != null)
            .Cast<RemoteManifest>()
            .ToArray();

        repoFeedCache[repoJsonUrl] = manifests;
        return manifests;
    }

    private static IEnumerable<JsonElement> EnumerateManifestCandidates(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
                yield return element;

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("plugins", out var plugins) &&
            plugins.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in plugins.EnumerateArray())
                yield return element;

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object)
            yield return root;
    }

    private static RemoteManifest? ParseRemoteManifest(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return new RemoteManifest
        {
            InternalName = GetString(element, "InternalName"),
            Name = GetString(element, "Name"),
            Description = FirstNonEmpty(GetString(element, "Description"), GetString(element, "Punchline")),
            Author = GetString(element, "Author"),
            DalamudApiLevel = GetInt32(element, "DalamudApiLevel"),
            Downloads = GetInt64(element, "DownloadCount"),
            LastUpdateUtc = GetUnixTime(element, "LastUpdate"),
            RepoUrl = FirstNonEmpty(GetString(element, "RepoUrl"), GetString(element, "SourceRepoUrl")),
        };
    }

    private static RemoteManifest? FindBestManifest(IEnumerable<RemoteManifest> manifests, MetadataRefreshRequest request)
    {
        var manifestList = manifests.ToList();
        if (manifestList.Count == 0)
            return null;

        if (manifestList.Count == 1)
            return manifestList[0];

        var normalizedTokens = request.MatchTokens
            .Select(PluginSnapshot.Normalize)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
        var normalizedInternalName = PluginSnapshot.Normalize(request.InternalName);
        var normalizedRepoUrl = NormalizeText(request.RepoUrl);
        var normalizedDisplayName = PluginSnapshot.Normalize(request.DisplayName);

        RemoteManifest? bestManifest = null;
        var bestScore = int.MinValue;

        foreach (var manifest in manifestList)
        {
            var score = 0;
            var manifestInternalName = PluginSnapshot.Normalize(manifest.InternalName);
            var manifestName = PluginSnapshot.Normalize(manifest.Name);
            var manifestRepoUrl = NormalizeText(manifest.RepoUrl);

            if (!string.IsNullOrWhiteSpace(normalizedInternalName) &&
                manifestInternalName.Equals(normalizedInternalName, StringComparison.Ordinal))
                score += 200;

            if (!string.IsNullOrWhiteSpace(normalizedDisplayName) &&
                manifestName.Equals(normalizedDisplayName, StringComparison.Ordinal))
                score += 120;

            if (normalizedTokens.Contains(manifestInternalName, StringComparer.Ordinal) ||
                normalizedTokens.Contains(manifestName, StringComparer.Ordinal))
                score += 160;

            if (!string.IsNullOrWhiteSpace(normalizedRepoUrl) &&
                manifestRepoUrl.Equals(normalizedRepoUrl, StringComparison.OrdinalIgnoreCase))
                score += 100;

            if (score > bestScore)
            {
                bestScore = score;
                bestManifest = manifest;
            }
        }

        return bestScore > 0 ? bestManifest : null;
    }

    private async Task<GitHubMetadata> GetGitHubMetadataAsync(
        string owner,
        string repo,
        IDictionary<string, GitHubMetadata> githubCache)
    {
        var cacheKey = $"{owner}/{repo}";
        if (githubCache.TryGetValue(cacheKey, out var cachedMetadata))
            return cachedMetadata;

        string? description = null;
        string? author = owner;
        DateTimeOffset? lastUpdateUtc = null;
        long? downloads = null;

        using (var repoResponse = await httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}").ConfigureAwait(false))
        {
            if (repoResponse.IsSuccessStatusCode)
            {
                await using var repoStream = await repoResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var repoDocument = await JsonDocument.ParseAsync(repoStream).ConfigureAwait(false);
                var root = repoDocument.RootElement;
                description = GetString(root, "description");
                author = FirstNonEmpty(author, root.TryGetProperty("owner", out var ownerElement) ? GetString(ownerElement, "login") : null);
                lastUpdateUtc = GetDateTimeOffset(root, "pushed_at");
            }
        }

        using (var releasesResponse = await httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases").ConfigureAwait(false))
        {
            if (releasesResponse.IsSuccessStatusCode)
            {
                await using var releaseStream = await releasesResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var releaseDocument = await JsonDocument.ParseAsync(releaseStream).ConfigureAwait(false);
                if (releaseDocument.RootElement.ValueKind == JsonValueKind.Array)
                {
                    long totalDownloads = 0;
                    foreach (var release in releaseDocument.RootElement.EnumerateArray())
                    {
                        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var asset in assets.EnumerateArray())
                            totalDownloads += GetInt64(asset, "download_count") ?? 0;
                    }

                    downloads = totalDownloads;
                }
            }
        }

        var metadata = new GitHubMetadata(description, author, downloads, lastUpdateUtc);
        githubCache[cacheKey] = metadata;
        return metadata;
    }

    private MetadataRefreshRequest? CreateRefreshRequest(PluginAssessmentRow row)
    {
        var repoUrl = RepositoryLinkResolver.ResolveRepoUrl(row);
        var repoJsonUrls = RepositoryLinkResolver.ResolveRepoJsonUrls(row)
            .Where(url => TryGetConcreteUrl(url, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!TryGetConcreteUrl(repoUrl, out _) && repoJsonUrls.Length == 0)
            return null;

        var matchTokens = row.Entry.MatchTokens
            .Concat(new[]
            {
                row.RuntimeState?.InternalName ?? string.Empty,
                row.RuntimeState?.Name ?? string.Empty,
            })
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MetadataRefreshRequest(
            row.Entry.Id,
            row.Entry.DisplayName,
            row.RuntimeState?.InternalName,
            repoUrl,
            repoJsonUrls,
            matchTokens);
    }

    private bool NeedsRefresh(MetadataRefreshRequest request)
    {
        if (!cache.TryGetValue(request.Id, out var cachedEntry))
            return true;

        if (!string.Equals(cachedEntry.RepoUrl, request.RepoUrl, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(cachedEntry.RepoJsonUrl, BuildRepoJsonSignature(request.RepoJsonUrls), StringComparison.OrdinalIgnoreCase))
            return true;

        return (DateTime.UtcNow - cachedEntry.FetchedAtUtc).TotalHours >= CacheLifetimeHours;
    }

    private void EnsureCacheLoaded()
    {
        if (cacheLoaded)
            return;

        lock (gate)
        {
            if (cacheLoaded)
                return;

            try
            {
                var path = GetCachePath();
                if (File.Exists(path))
                {
                    var manifest = JsonSerializer.Deserialize<CachedMetadataManifest>(File.ReadAllText(path), JsonOptions);
                    foreach (var entry in manifest?.Entries ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Id))
                            cache[entry.Id] = entry;
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, "[botology] Failed to load repository metadata cache.");
                cache.Clear();
            }
            finally
            {
                cacheLoaded = true;
            }
        }
    }

    private void SaveCache()
    {
        try
        {
            var path = GetCachePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            CachedMetadataEntry[] entries;
            lock (gate)
                entries = cache.Values.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase).ToArray();

            var manifest = new CachedMetadataManifest { Entries = entries };
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Failed to save repository metadata cache.");
        }
    }

    private static bool TryGetConcreteUrl(string? value, out string url)
    {
        url = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        return !url.Contains("/OWNER/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseGitHubRepository(string? repoUrl, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;

        if (string.IsNullOrWhiteSpace(repoUrl) ||
            !Uri.TryCreate(repoUrl, UriKind.Absolute, out var repoUri) ||
            !repoUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = repoUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        owner = segments[0];
        repo = segments[1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];

        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.Number when property.TryGetInt64(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static long? GetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static DateTimeOffset? GetUnixTime(JsonElement element, string propertyName)
    {
        var rawValue = GetInt64(element, propertyName);
        return rawValue.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(rawValue.Value)
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        var rawValue = GetString(element, propertyName);
        return DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
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

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string BuildRepoJsonSignature(IEnumerable<string> repoJsonUrls)
        => string.Join("\n", repoJsonUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim()));

    private static string GetCachePath()
        => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), CacheFileName);

    private sealed record MetadataRefreshRequest(
        string Id,
        string DisplayName,
        string? InternalName,
        string? RepoUrl,
        string[] RepoJsonUrls,
        string[] MatchTokens);

    private sealed record RemoteManifest
    {
        public string? InternalName { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Author { get; init; }
        public int? DalamudApiLevel { get; init; }
        public long? Downloads { get; init; }
        public DateTimeOffset? LastUpdateUtc { get; init; }
        public string? RepoUrl { get; init; }
    }

    private sealed record GitHubMetadata(
        string? Description,
        string? Author,
        long? Downloads,
        DateTimeOffset? LastUpdateUtc);

    private sealed class CachedMetadataManifest
    {
        public CachedMetadataEntry[] Entries { get; init; } = [];
    }

    private sealed class CachedMetadataEntry
    {
        public string Id { get; init; } = string.Empty;
        public string? RepoUrl { get; init; }
        public string? RepoJsonUrl { get; init; }
        public DateTime FetchedAtUtc { get; init; }
        public string? Description { get; init; }
        public string? Author { get; init; }
        public int? DalamudApiLevel { get; init; }
        public long? Downloads { get; init; }
        public DateTimeOffset? LastUpdateUtc { get; init; }

        public PluginRepositoryMetadata ToMetadata()
            => new(Description, Author, DalamudApiLevel, Downloads, LastUpdateUtc);
    }
}

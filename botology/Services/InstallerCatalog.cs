using System.Text.Json;
using botology.Models;

namespace botology.Services;

// Source identity is retained throughout discovery, installation and pin filtering.
public static class InstallerCatalog
{
    public const string OfficialRepository = "https://kamori.goats.dev/Plugin/PluginMaster";

    public static string? NormalizeRepository(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        // Uri normalizes scheme/host, but a feed's path and query can be case sensitive.
        return uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
    }

    public static string PluginIdentity(string repository, string internalName)
        => (NormalizeRepository(repository) ?? repository) + "\n" + internalName.Trim().ToUpperInvariant();

    public static string RepositoryName(string url)
        => url == OfficialRepository ? "Dalamud" : Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : "Unknown source";

    public static IReadOnlyList<string> ParseAuthors(string? value)
        => (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static InstallerCatalogSnapshot Merge(
        InstallerHostSnapshot host,
        IEnumerable<InstallerManifest> discovered,
        IEnumerable<PluginCatalogEntry> catalog,
        IReadOnlyList<PluginAssessmentRow> assessments)
    {
        var repositories = new Dictionary<string, InstallerRepository>(StringComparer.Ordinal);
        foreach (var repo in host.Repositories)
            if (NormalizeRepository(repo.Url) is { } url) repositories[url] = repo with { Url = url };
        foreach (var entry in catalog)
            foreach (var candidate in (entry.RepoJsonUrls ?? []).Append(entry.RepoJsonUrl ?? ""))
                if (NormalizeRepository(candidate) is { } url)
                    repositories.TryAdd(url, new(url, RepositoryName(url), false, false, url == OfficialRepository));

        var manifests = new Dictionary<string, InstallerManifest>(StringComparer.Ordinal);
        foreach (var manifest in discovered.Concat(host.Manifests))
        {
            if (string.IsNullOrWhiteSpace(manifest.InternalName) || NormalizeRepository(manifest.RepositoryUrl) is not { } url)
                continue;
            var normalized = manifest with { RepositoryUrl = url };
            repositories.TryAdd(url, new(url, RepositoryName(url), false, false, url == OfficialRepository));
            manifests[normalized.Identity] = normalized; // Current host data supersedes discovery metadata.
        }
        var installedByIdentity = host.Installed.GroupBy(p => PluginIdentity(p.RepositoryUrl, p.Runtime.InternalName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Runtime.IsLoaded).First(), StringComparer.Ordinal);
        foreach (var installed in host.Installed)
        {
            var source = NormalizeRepository(installed.RepositoryUrl) ?? installed.RepositoryUrl;
            var key = PluginIdentity(source, installed.Runtime.InternalName);
            repositories.TryAdd(source, new(source, installed.IsDevelopment ? "Development" : RepositoryName(source), false, false, false));
            manifests.TryAdd(key, new(installed.Runtime.InternalName, installed.Runtime.DisplayName, source,
                Version: installed.Runtime.Version.ToString(), ProjectUrl: installed.Runtime.RepoUrl));
        }
        var hostIds = host.Manifests.Select(m => m.Identity).ToHashSet(StringComparer.Ordinal);
        var installedNames = host.Installed.Select(p => p.Runtime.InternalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = manifests.Values.Select(manifest =>
        {
            installedByIdentity.TryGetValue(manifest.Identity, out var installed);
            var normalizedName = PluginSnapshot.Normalize(manifest.InternalName);
            var assessment = assessments.FirstOrDefault(row => row.Entry.MatchTokens.Any(t => PluginSnapshot.Normalize(t) == normalizedName));
            // An assessment can carry live installed state. Never attach another source's runtime to this entry.
            if (assessment is not null && installed is null) assessment = assessment with { RuntimeState = null };
            return new InstallerEntry(manifest, repositories[NormalizeRepository(manifest.RepositoryUrl) ?? manifest.RepositoryUrl],
                installed, assessment, installed is null && installedNames.Contains(manifest.InternalName), hostIds.Contains(manifest.Identity), host.Ready, host.SafeMode);
        }).OrderBy(e => e.Manifest.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Identity, StringComparer.Ordinal).ToArray();
        return new(repositories.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Url, StringComparer.Ordinal).ToArray(),
            rows, host.Ready, host.SafeMode, host.Error, assessments.SelectMany(row=>ParseAuthors(row.Metadata?.Author)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static IReadOnlyList<InstallerRepository> VisiblePinnedRepositories(Configuration configuration, InstallerCatalogSnapshot snapshot)
    {
        var pins = (configuration.PinnedRepositories ?? []).Select(NormalizeRepository)
            .Where(p => p is not null).ToHashSet(StringComparer.Ordinal);
        return snapshot.Repositories.Where(r => NormalizeRepository(r.Url) is { } id && pins.Contains(id)).ToArray();
    }

    public static IReadOnlyList<string> VisiblePinnedAuthors(Configuration configuration, InstallerCatalogSnapshot snapshot)
    {
        var available = snapshot.Entries.SelectMany(e => e.Manifest.Authors).Concat(snapshot.KnownAuthors??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (configuration.PinnedAuthors ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && available.Contains(p.Trim()))
            .Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool ToggleRepositoryPin(Configuration configuration, string repository)
    {
        if (NormalizeRepository(repository) is not { } id) return false;
        configuration.PinnedRepositories ??= [];
        var removed = configuration.PinnedRepositories.RemoveAll(p => NormalizeRepository(p) == id) > 0;
        if (!removed) configuration.PinnedRepositories.Add(id);
        return !removed;
    }

    public static bool ToggleAuthorPin(Configuration configuration, string author)
    {
        author = author.Trim();
        if (author.Length == 0) return false;
        configuration.PinnedAuthors ??= [];
        var removed = configuration.PinnedAuthors.RemoveAll(p => string.Equals(p?.Trim(), author, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed) configuration.PinnedAuthors.Add(author);
        return !removed;
    }

    public static IReadOnlyList<InstallerManifest> ParseFeed(string json, string repository)
    {
        if (NormalizeRepository(repository) is not { } source) return [];
        using var document = JsonDocument.Parse(json, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && TryProperty(root, "plugins", out var plugins)) root = plugins;
        if (root.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)
            || root.ValueKind == JsonValueKind.Object && string.IsNullOrWhiteSpace(Text(root, "InternalName")))
            throw new System.IO.InvalidDataException("Invalid plugin feed.");
        var candidates = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : [root];
        return candidates.Where(e => e.ValueKind == JsonValueKind.Object).Select(e => ParseManifest(e, source))
            .Where(m => m is not null).Cast<InstallerManifest>().DistinctBy(m => m.Identity).ToArray();
    }

    private static InstallerManifest? ParseManifest(JsonElement e, string source)
    {
        var internalName = Text(e, "InternalName");
        if (string.IsNullOrWhiteSpace(internalName)) return null;
        return new(internalName, Text(e, "Name", internalName), source, Text(e, "Author"),
            Text(e, "Description", Text(e, "Punchline")), Text(e, "AssemblyVersion"),
            TryProperty(e, "DalamudApiLevel", out var api) && api.ValueKind == JsonValueKind.Number && api.TryGetInt32(out var level) ? level : 0,
            Text(e, "RepoUrl"), Text(e, "IconUrl"), Strings(e, "ImageUrls"), Strings(e, "Tags"),
            Flag(e, "IsTestingExclusive"), Flag(e, "IsHide"), Text(e, "Changelog"), Text(e, "Dip17Channel", Text(e, "_Dip17Channel")));
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        value = default;
        return false;
    }
    private static string Text(JsonElement e, string name, string fallback = "")
        => TryProperty(e, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static bool Flag(JsonElement e, string name)
        => TryProperty(e, name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string[] Strings(JsonElement e, string name)
        => TryProperty(e, name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
}

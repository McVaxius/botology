using System.Text.Json;
using botology.Models;

namespace botology.Services;

// Source identity is retained throughout discovery, installation and pin filtering.
public static class InstallerCatalog
{
    public const string OfficialRepository = "https://kamori.goats.dev/Plugin/PluginMaster";
    public const string DefaultPinnedRepository = "https://aethertek.io/x.json";

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
    {
        if(url==OfficialRepository) return "Dalamud";
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Host.Length==0) return "Unknown source";
        var parts=uri.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        return (uri.Host is "raw.githubusercontent.com" or "github.com" or "www.github.com") && parts.Length>=2
            ? Uri.UnescapeDataString(parts[0]+"/"+parts[1]) : uri.Host;
    }

    public static IReadOnlyList<string> ParseAuthors(string? value)
        => (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static InstallerCatalogSnapshot Merge(
        InstallerHostSnapshot host,
        IEnumerable<InstallerManifest> discovered,
        IEnumerable<PluginCatalogEntry> catalog,
        IReadOnlyList<PluginAssessmentRow> assessments)
    {
        var catalogEntries=catalog.ToArray();
        // A feed URL repeats for every manifest. Resolve it once in this merge, not once per plugin.
        var sourceUrls = new Dictionary<string,string?>(StringComparer.Ordinal);
        string? Source(string? value)
        {
            if(value is null) return null;
            if(!sourceUrls.TryGetValue(value,out var url)) sourceUrls[value]=url=NormalizeRepository(value);
            return url;
        }
        static string Key(string source,string name)=>source+"\n"+name.Trim().ToUpperInvariant();
        var repositories = new Dictionary<string, InstallerRepository>(StringComparer.Ordinal);
        void AddSource(string url)
        {
            if(!repositories.ContainsKey(url))
                repositories.Add(url,new(url,RepositoryName(url),false,false,url==OfficialRepository));
        }
        foreach (var repo in host.Repositories)
            if (Source(repo.Url) is { } url) repositories[url] = repo with { Url = url, Name=repo.IsOfficial?"Dalamud":repo.Name };
        AddSource(DefaultPinnedRepository);
        foreach (var entry in catalogEntries)
            foreach (var candidate in (entry.RepoJsonUrls ?? []).Append(entry.RepoJsonUrl ?? ""))
                if (Source(candidate) is { } url) AddSource(url);

        var manifests = new Dictionary<string, InstallerManifest>(StringComparer.Ordinal);
        var hostIds = new HashSet<string>(StringComparer.Ordinal);
        void AddManifest(InstallerManifest manifest,bool fromHost)
        {
            if (string.IsNullOrWhiteSpace(manifest.InternalName) || Source(manifest.RepositoryUrl) is not { } url) return;
            var key=Key(url,manifest.InternalName);
            AddSource(url);
            manifests[key] = url==manifest.RepositoryUrl?manifest:manifest with { RepositoryUrl = url };
            if(fromHost) hostIds.Add(key);
        }
        foreach(var manifest in discovered) AddManifest(manifest,false);
        foreach(var manifest in host.Manifests) AddManifest(manifest,true); // Current host data supersedes discovery metadata.
        var installedByIdentity = new Dictionary<string,InstallerInstalledPlugin>(StringComparer.Ordinal);
        foreach (var installed in host.Installed)
        {
            var source = Source(installed.RepositoryUrl) ?? installed.RepositoryUrl;
            var key = Key(source, installed.Runtime.InternalName);
            if(!installedByIdentity.TryGetValue(key,out var prior) || installed.Runtime.IsLoaded && !prior.Runtime.IsLoaded)
                installedByIdentity[key]=installed;
            if(!repositories.ContainsKey(source)) repositories.Add(source,new(source,installed.IsDevelopment?"Development":RepositoryName(source),false,false,false));
            manifests.TryAdd(key, new(installed.Runtime.InternalName, installed.Runtime.DisplayName, source,
                Version: installed.Runtime.Version.ToString(), ProjectUrl: installed.Runtime.RepoUrl));
        }
        foreach(var group in repositories.Values.GroupBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).ToArray())
        {
            var variants=group.Where(r=>NormalizeRepository(r.Url) is not null).Select(r=>
            {
                var uri=new Uri(r.Url);
                var parts=uri.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
                var github=uri.Host is "raw.githubusercontent.com" or "github.com" or "www.github.com";
                if(github) parts=parts.Skip(2).ToArray();
                var reference=parts.AsEnumerable();
                if(parts.Length>2 && parts[0]=="refs" && parts[1] is "heads" or "tags") reference=reference.Skip(2);
                else if(parts.Length>1 && parts[0] is "raw" or "blob") reference=reference.Skip(1);
                var hint=github?(reference.FirstOrDefault()??"")+uri.Query:string.Join('/',parts)+uri.Query;
                return (Repo:r,Parts:(string.Join('/',parts)+uri.Query).Split('/'),Hint:hint);
            }).ToArray();
            if(variants.Length<2) continue;
            var first=0;
            while(variants.All(v=>v.Parts.Length>first+1) && variants.Select(v=>v.Parts[first]).Distinct(StringComparer.Ordinal).Count()==1) first++;
            var tail=0;
            while(variants.All(v=>v.Parts.Length-first-tail>1) && variants.Select(v=>v.Parts[^(tail+1)]).Distinct(StringComparer.Ordinal).Count()==1) tail++;
            foreach(var variant in variants)
            {
                var detail=variants.Count(v=>v.Hint==variant.Hint)==1?variant.Hint:
                    string.Join('/',variant.Parts.Skip(first).Take(variant.Parts.Length-first-tail));
                repositories[variant.Repo.Url]=variant.Repo with { Name=variant.Repo.Name+" · "+detail };
            }
        }
        var installedNames = host.Installed.Select(p => p.Runtime.InternalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assessmentsByName = new Dictionary<string,PluginAssessmentRow>(StringComparer.Ordinal);
        foreach(var assessment in assessments)
            foreach(var token in assessment.Entry.MatchTokens)
                assessmentsByName.TryAdd(PluginSnapshot.Normalize(token),assessment);
        var catalogByName=new Dictionary<string,PluginCatalogEntry>(StringComparer.Ordinal);
        foreach(var entry in catalogEntries)
            foreach(var token in entry.MatchTokens) catalogByName.TryAdd(PluginSnapshot.Normalize(token),entry);
        var tagSet=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] ManifestTags(InstallerManifest manifest)
        {
            tagSet.Clear();
            foreach(var tag in manifest.CategoryTags??[])
                if(!string.IsNullOrWhiteSpace(tag)) tagSet.Add(tag.Trim());
            foreach(var tag in manifest.Tags??[])
                if(!string.IsNullOrWhiteSpace(tag)) tagSet.Add(tag.Trim());
            return tagSet.ToArray();
        }
        var rows = manifests.Select(pair =>
        {
            var manifest=pair.Value;
            installedByIdentity.TryGetValue(pair.Key, out var installed);
            var normalizedName = PluginSnapshot.Normalize(manifest.InternalName);
            assessmentsByName.TryGetValue(normalizedName,out var assessment);
            catalogByName.TryGetValue(normalizedName,out var categoryEntry);
            var tags=ManifestTags(manifest);
            var category=(assessment?.Entry.Category??categoryEntry?.Category)?.Trim();
            // An assessment can carry live installed state. Never attach another source's runtime to this entry.
            if (assessment?.RuntimeState is not null && installed is null) assessment = assessment with { RuntimeState = null };
            return (pair.Key,Entry:new InstallerEntry(manifest, repositories[manifest.RepositoryUrl],
                installed, assessment, installed is null && installedNames.Contains(manifest.InternalName), hostIds.Contains(pair.Key), host.Ready, host.SafeMode,tags,category));
        }).OrderBy(e => e.Entry.Manifest.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Key, StringComparer.Ordinal).Select(e=>e.Entry).ToArray();
        return new(repositories.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Url, StringComparer.Ordinal).ToArray(),
            rows, host.Ready, host.SafeMode, host.Error, assessments.SelectMany(row=>ParseAuthors(row.Metadata?.Author)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NormalizeLabels(rows.Where(row=>row.Repository.Url==OfficialRepository).SelectMany(row=>row.ManifestTags??[])),
            NormalizeLabels(catalogEntries.Select(entry=>entry.Category).Concat(assessments.Select(row=>row.Entry.Category))),
            NormalizeLabels(rows.Where(row=>row.Repository.Url!=OfficialRepository).SelectMany(row=>row.ManifestTags??[])));
    }

    private static string[] NormalizeLabels(IEnumerable<string> values)=>values.Where(value=>!string.IsNullOrWhiteSpace(value))
        .Select(value=>value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<InstallerRepository> VisiblePinnedRepositories(Configuration configuration, InstallerCatalogSnapshot snapshot)
    {
        var pins = (configuration.PinnedRepositories ?? []).Select(NormalizeRepository)
            .Where(p => p is not null).ToHashSet(StringComparer.Ordinal);
        return snapshot.Repositories.Where(r => NormalizeRepository(r.Url) is { } id && pins.Contains(id)).ToArray();
    }

    public static IReadOnlyList<string> VisiblePinnedAuthors(Configuration configuration, InstallerCatalogSnapshot snapshot)
    {
        var pins=(configuration.PinnedAuthors??[]).Where(p=>!string.IsNullOrWhiteSpace(p)).Select(p=>p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(pins.Length==0) return pins;
        var missing=new HashSet<string>(pins,StringComparer.OrdinalIgnoreCase);
        foreach(var author in snapshot.KnownAuthors??[]) missing.Remove(author);
        var lookup=missing.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach(var entry in snapshot.Entries)
        {
            if(missing.Count==0) break;
            var authors=entry.Manifest.Author.AsSpan();
            foreach(var range in authors.SplitAny(",;")) lookup.Remove(authors[range].Trim());
        }
        return pins.Where(pin=>!missing.Contains(pin)).ToArray();
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
            Flag(e, "IsTestingExclusive"), Flag(e, "IsHide"), Text(e, "Changelog"), Text(e, "Dip17Channel", Text(e, "_Dip17Channel")),Strings(e,"CategoryTags"));
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

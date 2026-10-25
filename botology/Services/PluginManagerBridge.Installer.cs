using System.Collections;
using System.Reflection;
using botology.Models;

namespace botology.Services;

public sealed partial class PluginManagerBridge
{
    public InstallerHostSnapshot CaptureInstallerHost()
    {
        try
        {
            var manager = GetPluginManager();
            var configuration = manager.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager)
                ?? throw new NotSupportedException("The current host repository configuration is unavailable.");
            return CaptureInstallerHost(manager, configuration, CaptureSnapshot());
        }
        catch (Exception ex)
        {
            return InstallerHostSnapshot.Empty with { Error = ex.GetBaseException().Message };
        }
    }

    internal static InstallerHostSnapshot CaptureInstallerHost(object manager, object configuration, PluginSnapshot installed)
    {
        var sourceUrls=new Dictionary<object,string?>(ReferenceEqualityComparer.Instance);
        string? Source(object? repo)
        {
            if(repo is null) return null;
            if(!sourceUrls.TryGetValue(repo,out var url)) sourceUrls[repo]=url=InstallerCatalog.NormalizeRepository(ReadString(repo,"PluginMasterUrl"));
            return url;
        }
        var repos = new Dictionary<string, InstallerRepository>(StringComparer.Ordinal);
        foreach (var repo in GetEnumerableProperty(manager, "Repos"))
        {
            if (Source(repo) is not { } url) continue;
            repos[url] = new(url, InstallerCatalog.RepositoryName(url), true, ReadBool(repo, "IsEnabled"),
                !ReadBool(repo, "IsThirdParty"), ReadString(repo, "State"));
        }
        foreach (var setting in GetEnumerableProperty(configuration, "ThirdRepoList"))
        {
            if (InstallerCatalog.NormalizeRepository(ReadString(setting, "Url")) is not { } url) continue;
            if (!repos.ContainsKey(url)) repos[url] = new(url, InstallerCatalog.RepositoryName(url), true,
                ReadBool(setting, "IsEnabled"), false, "");
        }
        var manifests = new List<InstallerManifest>();
        foreach (var manifest in GetEnumerableProperty(manager, "AvailablePlugins"))
        {
            if (Source(Read(manifest, "SourceRepo")) is not { } source) continue;
            var name = ReadString(manifest, "InternalName");
            if (name.Length == 0) continue;
            manifests.Add(new(name, ReadString(manifest, "Name"), source,
                ReadString(manifest, "Author"), ReadString(manifest, "Description"), ReadString(manifest, "AssemblyVersion"),
                Read(manifest, "DalamudApiLevel") is int api ? api : 0, ReadString(manifest, "RepoUrl"),
                ReadString(manifest, "IconUrl"), ReadStrings(manifest, "ImageUrls"), ReadStrings(manifest, "Tags"),
                ReadBool(manifest, "IsTestingExclusive"), ReadBool(manifest, "IsHide"), ReadString(manifest, "Changelog"), ReadString(manifest, "Dip17Channel"),ReadStrings(manifest,"CategoryTags")));
        }
        var updates = GetEnumerableProperty(manager, "UpdatablePlugins");
        var locals = installed.Plugins.Select(runtime =>
        {
            var local = runtime.LocalPluginHandle;
            var manifest = Read(local, "Manifest");
            var dev = ReadBool(local, "IsDev");
            var thirdParty = ReadBool(local, "IsThirdParty");
            // InstalledFromUrl belongs to the local manifest, not the project's homepage.
            var source = dev ? "development:" + runtime.InternalName
                : !thirdParty ? InstallerCatalog.OfficialRepository
                : InstallerCatalog.NormalizeRepository(manifest is null ? null : ReadString(manifest, "InstalledFromUrl"))
                    ?? "unknown:" + runtime.InternalName;
            var update = updates.SingleOrDefault(candidate => ReferenceEquals(Read(candidate, "InstalledPlugin"), local)
                && Read(candidate, "UpdateManifest") is { } remote && ManifestSource(remote) == source);
            return new InstallerInstalledPlugin(runtime, source, ReadString(local, "State"), dev,
                ReadBool(local, "IsTesting"), ReadBool(local, "IsBanned"), ReadBool(local, "IsOutdated"),
                manifest is not null && ReadBool(manifest, "ScheduledForDeletion"))
            { UpdateApiLevel = update is null ? null : InstallerUpdateApiLevel(update) };
        }).ToArray();
        return new(repos.Values.ToArray(), manifests, locals,
            ReadBool(manager, "PluginsReady") && ReadBool(manager, "ReposReady"), ReadBool(manager, "SafeMode"));
    }

    private static object? Read(object instance, string property)
        => GetPropertyValue(instance, instance.GetType(), property);
    private static string ReadString(object instance, string property) => Read(instance, property)?.ToString() ?? "";
    private static bool ReadBool(object instance, string property) => Read(instance, property) is true;
    private static string[] ReadStrings(object instance, string property)
        => Read(instance, property) is IEnumerable values ? values.Cast<object?>().OfType<string>().ToArray() : [];
}

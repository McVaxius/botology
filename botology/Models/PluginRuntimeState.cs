using System;
using System.Collections.Generic;

namespace botology.Models;

public sealed class PluginRuntimeState
{
    private readonly HashSet<string> normalizedKeys;

    public PluginRuntimeState(
        string internalName,
        string name,
        Version version,
        bool isLoaded,
        bool hasUpdate,
        string? repoUrl,
        string? repoJsonUrl,
        object localPluginHandle,
        object? pluginInstance,
        object? configurationHandle,
        bool? dtrBarEnabled)
    {
        InternalName = internalName;
        Name = name;
        Version = version;
        IsLoaded = isLoaded;
        HasUpdate = hasUpdate;
        RepoUrl = repoUrl;
        RepoJsonUrl = repoJsonUrl;
        LocalPluginHandle = localPluginHandle;
        PluginInstance = pluginInstance;
        ConfigurationHandle = configurationHandle;
        DtrBarEnabled = dtrBarEnabled;

        normalizedKeys = new HashSet<string>(StringComparer.Ordinal);
        AddKey(internalName);
        AddKey(name);
    }

    public string InternalName { get; }

    public string Name { get; }

    public Version Version { get; }

    public bool IsLoaded { get; }

    public bool HasUpdate { get; }

    public string? RepoUrl { get; }

    public string? RepoJsonUrl { get; }

    public object LocalPluginHandle { get; }

    public object? PluginInstance { get; }

    public object? ConfigurationHandle { get; }

    public bool? DtrBarEnabled { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? InternalName : Name;

    public bool CanToggleEnabled => LocalPluginHandle != null;

    public bool CanToggleDtr => ConfigurationHandle != null && DtrBarEnabled.HasValue;

    public bool MatchesAny(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (normalizedKeys.Contains(PluginSnapshot.Normalize(candidate)))
                return true;
        }

        return false;
    }

    private void AddKey(string value)
    {
        var normalized = PluginSnapshot.Normalize(value);
        if (!string.IsNullOrEmpty(normalized))
            normalizedKeys.Add(normalized);
    }
}

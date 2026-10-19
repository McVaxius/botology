using System;
using System.Collections.Generic;
using System.Linq;

namespace botology.Models;

public sealed class PluginSnapshot
{
    public static PluginSnapshot Empty { get; } = new(Array.Empty<PluginRuntimeState>());

    public PluginSnapshot(IReadOnlyList<PluginRuntimeState> plugins)
    {
        Plugins = plugins;
    }

    public IReadOnlyList<PluginRuntimeState> Plugins { get; }

    public PluginRuntimeState? FindBestMatch(params string[] candidates)
    {
        if (candidates.Length == 0)
            return null;

        var normalized = candidates
            .Select(Normalize)
            .Where(candidate => !string.IsNullOrEmpty(candidate))
            .ToArray();

        if(normalized.Length==0) return null;
        PluginRuntimeState? best=null;
        for(var index=0;index<Plugins.Count;index++)
        {
            var plugin=Plugins[index];
            if(!plugin.MatchesNormalized(normalized)) continue;
            if(best is null || CompareMatches(plugin,best)<0)
                best=plugin;
        }
        return best;
    }

    internal PluginRuntimeState?[] FindBestMatches(IEnumerable<string[]> candidateGroups)
    {
        // All assessment rows share this capture. Build one local index instead of rescanning every plugin per row.
        var matches=new Dictionary<string,(PluginRuntimeState Plugin,int Order)>(StringComparer.Ordinal);
        void Add(string value,PluginRuntimeState plugin,int order)
        {
            var key=Normalize(value);
            if(key.Length>0 && (!matches.TryGetValue(key,out var previous) || CompareMatches(plugin,previous.Plugin)<0)) matches[key]=(plugin,order);
        }
        for(var index=0;index<Plugins.Count;index++)
        {
            var plugin=Plugins[index];Add(plugin.InternalName,plugin,index);Add(plugin.Name,plugin,index);
        }
        return candidateGroups.Select(candidates=>
        {
            PluginRuntimeState? best=null;var bestOrder=int.MaxValue;
            foreach(var candidate in candidates)
                if(matches.TryGetValue(Normalize(candidate),out var match))
                {
                    var comparison=best is null?-1:CompareMatches(match.Plugin,best);
                    if(comparison<0 || comparison==0 && match.Order<bestOrder) { best=match.Plugin;bestOrder=match.Order; }
                }
            return best;
        }).ToArray();
    }

    private static int CompareMatches(PluginRuntimeState left,PluginRuntimeState right)=>left.IsLoaded==right.IsLoaded
        ?StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName,right.DisplayName):left.IsLoaded?-1:1;

    public bool IsInstalled(params string[] candidates)
        => FindBestMatch(candidates) != null;

    public bool IsLoaded(params string[] candidates)
        => FindBestMatch(candidates)?.IsLoaded == true;

    public bool HasUpdate(params string[] candidates)
        => FindBestMatch(candidates)?.HasUpdate == true;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                buffer[index++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..index]);
    }
}

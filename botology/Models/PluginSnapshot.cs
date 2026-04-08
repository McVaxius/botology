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

        return Plugins
            .Where(plugin => plugin.MatchesAny(normalized))
            .OrderByDescending(plugin => plugin.IsLoaded)
            .ThenBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

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

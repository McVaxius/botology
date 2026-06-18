using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using botology.Models;

namespace botology.Services;

public sealed class DtrVisibilityBridge
{
    private const string DalamudConfigurationTypeName = "Dalamud.Configuration.Internal.DalamudConfiguration";
    private const string DalamudInterfaceTypeName = "Dalamud.Interface.Internal.DalamudInterface";
    private const string DtrBarTypeName = "Dalamud.Game.Gui.Dtr.DtrBar";
    private const string SettingsOpenKindTypeName = "Dalamud.Interface.SettingsOpenKind";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IDtrBar dtrBar;
    private readonly IPluginLog log;

    private object? dalamudConfiguration;
    private object? rawDtrBar;
    private object? dalamudInterface;

    public DtrVisibilityBridge(IDalamudPluginInterface pluginInterface, IDtrBar dtrBar, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.dtrBar = dtrBar;
        this.log = log;
    }

    public IReadOnlyList<DtrEntrySnapshot> CaptureEntries()
    {
        try
        {
            return dtrBar.Entries
                .Select(CreateSnapshot)
                .ToArray();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Failed to capture DTR entries.");
            return Array.Empty<DtrEntrySnapshot>();
        }
    }

    public DtrEntrySnapshot? FindBestMatch(PluginRuntimeState runtimeState, PluginCatalogEntry entry)
    {
        var entries = CaptureEntries();
        if (entries.Count == 0)
            return null;

        var candidates = BuildNormalizedMatchCandidates(runtimeState, entry);

        var referenceMatches = entries
            .Where(dtrEntry => ReferenceEquals(dtrEntry.OwnerPluginHandle, runtimeState.LocalPluginHandle))
            .ToArray();
        if (referenceMatches.Length > 0)
            return SelectBestOwnerMatch(referenceMatches, candidates);

        var runtimeInternalName = PluginSnapshot.Normalize(runtimeState.InternalName);
        if (!string.IsNullOrEmpty(runtimeInternalName))
        {
            var internalNameMatches = entries
                .Where(dtrEntry =>
                    NormalizedEquals(dtrEntry.OwnerInternalName, runtimeInternalName) ||
                    NormalizedEquals(dtrEntry.OwnerManifestInternalName, runtimeInternalName))
                .ToArray();
            if (internalNameMatches.Length > 0)
                return SelectBestOwnerMatch(internalNameMatches, candidates);
        }

        var ownerNameMatches = entries
            .Where(dtrEntry =>
                MatchesAnyNormalizedCandidate(dtrEntry.OwnerName, candidates) ||
                MatchesAnyNormalizedCandidate(dtrEntry.OwnerManifestName, candidates))
            .ToArray();
        if (ownerNameMatches.Length > 0)
            return SelectBestOwnerMatch(ownerNameMatches, candidates);

        return FindTitleMatch(
            entries.Where(dtrEntry => !dtrEntry.HasOwnerMetadata),
            candidates);
    }

    private DtrEntrySnapshot CreateSnapshot(IReadOnlyDtrBarEntry entry, int index)
    {
        var ownerPlugin = GetOwnerPlugin(entry);
        var ownerType = ownerPlugin?.GetType();
        var ownerManifest = GetPropertyValue(ownerPlugin, ownerType, "Manifest");
        var ownerManifestType = ownerManifest?.GetType();

        return new DtrEntrySnapshot(
            entry.Title,
            entry.Text?.ToString() ?? string.Empty,
            entry.Tooltip?.ToString() ?? string.Empty,
            entry.Shown,
            entry.UserHidden,
            entry.HasClickAction,
            index,
            GetStringProperty(ownerPlugin, ownerType, "InternalName"),
            GetStringProperty(ownerPlugin, ownerType, "Name"),
            GetStringProperty(ownerManifest, ownerManifestType, "InternalName"),
            GetStringProperty(ownerManifest, ownerManifestType, "Name"))
        {
            OwnerPluginHandle = ownerPlugin,
        };
    }

    private static HashSet<string> BuildNormalizedMatchCandidates(PluginRuntimeState runtimeState, PluginCatalogEntry entry)
    {
        return new[]
            {
                runtimeState.DisplayName,
                runtimeState.Name,
                runtimeState.InternalName,
                entry.DisplayName,
            }
            .Concat(entry.MatchTokens)
            .Select(PluginSnapshot.Normalize)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static DtrEntrySnapshot? SelectBestOwnerMatch(
        IEnumerable<DtrEntrySnapshot> matches,
        ISet<string> titleCandidates)
    {
        var orderedMatches = matches
            .OrderBy(dtrEntry => dtrEntry.Order)
            .ToArray();
        if (orderedMatches.Length == 0)
            return null;

        var titleMatch = FindTitleMatch(orderedMatches, titleCandidates);
        return (titleMatch ?? orderedMatches[0]) with { IsOwnerMatched = true };
    }

    private static DtrEntrySnapshot? FindTitleMatch(
        IEnumerable<DtrEntrySnapshot> entries,
        ISet<string> candidates)
    {
        if (candidates.Count == 0)
            return null;

        return entries
            .OrderBy(dtrEntry => dtrEntry.Order)
            .FirstOrDefault(dtrEntry => candidates.Contains(PluginSnapshot.Normalize(dtrEntry.Title)));
    }

    private static bool MatchesAnyNormalizedCandidate(string? value, ISet<string> candidates)
    {
        var normalized = PluginSnapshot.Normalize(value);
        return !string.IsNullOrEmpty(normalized) && candidates.Contains(normalized);
    }

    private static bool NormalizedEquals(string? value, string normalizedExpected)
    {
        var normalized = PluginSnapshot.Normalize(value);
        return !string.IsNullOrEmpty(normalized) && normalized.Equals(normalizedExpected, StringComparison.Ordinal);
    }

    public bool TrySetUserVisible(string title, bool visible, out string message)
    {
        try
        {
            var entries = CaptureEntries();
            if (!TryResolveLiveTitle(title, entries, out var resolvedTitle, out message))
                return false;

            var configuration = GetDalamudConfiguration();
            var ignore = GetMutableStringList(configuration, "DtrIgnore");
            ignore.RemoveAll(existing => existing.Equals(resolvedTitle, StringComparison.OrdinalIgnoreCase));
            if (!visible)
                ignore.Add(resolvedTitle);

            TryMakeDirty(resolvedTitle);
            ApplySort();
            QueueSave(configuration);

            var entry = entries.FirstOrDefault(candidate => candidate.Title.Equals(resolvedTitle, StringComparison.Ordinal));
            if (visible && entry?.PluginShown == false)
            {
                message = $"{resolvedTitle} is no longer hidden by Dalamud, but the plugin currently sets Shown=false.";
                return true;
            }

            message = $"{resolvedTitle} DTR {(visible ? "shown" : "hidden")} globally.";
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Failed to update global DTR visibility.");
            message = $"Could not update global DTR visibility: {ex.Message}";
            return false;
        }
    }

    public bool TryToggleUserVisible(string title, out string message)
    {
        var entries = CaptureEntries();
        if (!TryResolveLiveTitle(title, entries, out var resolvedTitle, out message))
            return false;

        var entry = entries.First(candidate => candidate.Title.Equals(resolvedTitle, StringComparison.Ordinal));
        return TrySetUserVisible(entry.Title, entry.UserHidden, out message);
    }

    public bool TryMove(string title, int delta, out string message)
    {
        try
        {
            if (delta == 0)
            {
                message = "No DTR move requested.";
                return true;
            }

            var entries = CaptureEntries();
            if (!TryResolveLiveTitle(title, entries, out var resolvedTitle, out message))
                return false;

            var liveTitles = entries.Select(entry => entry.Title).ToArray();
            var liveTitleSet = new HashSet<string>(liveTitles, StringComparer.OrdinalIgnoreCase);
            var configuration = GetDalamudConfiguration();
            var order = GetMutableStringList(configuration, "DtrOrder");
            foreach (var liveTitle in liveTitles)
            {
                if (!order.Contains(liveTitle, StringComparer.OrdinalIgnoreCase))
                    order.Add(liveTitle);
            }

            var orderedLiveTitles = order
                .Where(titleInOrder => liveTitleSet.Contains(titleInOrder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var liveTitle in liveTitles)
            {
                if (!orderedLiveTitles.Contains(liveTitle, StringComparer.OrdinalIgnoreCase))
                    orderedLiveTitles.Add(liveTitle);
            }

            var index = orderedLiveTitles.FindIndex(candidate => candidate.Equals(resolvedTitle, StringComparison.OrdinalIgnoreCase));
            var targetIndex = index + delta;
            if (index < 0 || targetIndex < 0 || targetIndex >= orderedLiveTitles.Count)
            {
                message = $"{resolvedTitle} cannot move {(delta < 0 ? "up" : "down")}.";
                return false;
            }

            (orderedLiveTitles[index], orderedLiveTitles[targetIndex]) = (orderedLiveTitles[targetIndex], orderedLiveTitles[index]);

            var staleTitles = order
                .Where(titleInOrder => !liveTitleSet.Contains(titleInOrder))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            SetStringList(configuration, "DtrOrder", orderedLiveTitles.Concat(staleTitles).ToList());

            TryMakeDirty(resolvedTitle);
            ApplySort();
            QueueSave(configuration);
            message = $"{resolvedTitle} moved {(delta < 0 ? "up" : "down")}.";
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Failed to update global DTR order.");
            message = $"Could not update global DTR order: {ex.Message}";
            return false;
        }
    }

    public bool TryOpenServerInfoBarSettings(string? searchText, out string error)
    {
        error = string.Empty;

        try
        {
            var assembly = GetDalamudAssembly();
            var settingsOpenKindType = assembly.GetType(SettingsOpenKindTypeName, throwOnError: true)!;
            var serverInfoBarKind = Enum.Parse(settingsOpenKindType, "ServerInfoBar");

            if (TryOpenViaPluginInterface(settingsOpenKindType, serverInfoBarKind, searchText, out error))
                return true;

            return TryOpenViaInternalInterface(settingsOpenKindType, serverInfoBarKind, searchText, out error);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Failed to open XLSettings Server Info Bar tab.");
            error = ex.Message;
            return false;
        }
    }

    private bool TryOpenViaPluginInterface(
        Type settingsOpenKindType,
        object serverInfoBarKind,
        string? searchText,
        out string error)
    {
        error = string.Empty;

        try
        {
            var method = FindCompatibleMethod(
                pluginInterface.GetType(),
                "OpenDalamudSettingsTo",
                settingsOpenKindType,
                allowSearchText: true);
            if (method == null)
            {
                error = "Dalamud OpenDalamudSettingsTo method was unavailable.";
                return false;
            }

            var result = InvokeSettingsOpenMethod(method, pluginInterface, serverInfoBarKind, searchText);
            if (result is bool opened)
            {
                if (!opened)
                    error = "Dalamud OpenDalamudSettingsTo returned false.";
                return opened;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryOpenViaInternalInterface(
        Type settingsOpenKindType,
        object serverInfoBarKind,
        string? searchText,
        out string error)
    {
        error = string.Empty;

        var interfaceService = GetDalamudInterface();
        var method = FindCompatibleMethod(
            interfaceService.GetType(),
            "OpenSettingsTo",
            settingsOpenKindType,
            allowSearchText: true);
        if (method == null)
        {
            error = "Dalamud OpenSettingsTo method was unavailable.";
            return false;
        }

        InvokeSettingsOpenMethod(method, interfaceService, serverInfoBarKind, searchText);
        return true;
    }

    private static MethodInfo? FindCompatibleMethod(
        Type type,
        string methodName,
        Type settingsOpenKindType,
        bool allowSearchText)
    {
        return GetCandidateMethods(type)
            .Where(method => method.Name.Equals(methodName, StringComparison.Ordinal))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 1)
                    return ParametersMatchSettingsKind(parameters[0].ParameterType, settingsOpenKindType);

                return allowSearchText &&
                       parameters.Length == 2 &&
                       ParametersMatchSettingsKind(parameters[0].ParameterType, settingsOpenKindType) &&
                       parameters[1].ParameterType == typeof(string);
            })
            .OrderByDescending(method => method.GetParameters().Length)
            .FirstOrDefault();
    }

    private static IEnumerable<MethodInfo> GetCandidateMethods(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            yield return method;

        foreach (var interfaceType in type.GetInterfaces())
        {
            foreach (var method in interfaceType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                yield return method;
        }
    }

    private static bool ParametersMatchSettingsKind(Type parameterType, Type settingsOpenKindType)
        => parameterType == settingsOpenKindType ||
           parameterType.IsAssignableFrom(settingsOpenKindType) ||
           settingsOpenKindType.IsAssignableFrom(parameterType);

    private static object? InvokeSettingsOpenMethod(
        MethodInfo method,
        object target,
        object serverInfoBarKind,
        string? searchText)
    {
        var parameters = method.GetParameters();
        return parameters.Length == 2
            ? method.Invoke(target, new object?[] { serverInfoBarKind, searchText })
            : method.Invoke(target, new[] { serverInfoBarKind });
    }

    private bool TryResolveLiveTitle(
        string title,
        IReadOnlyList<DtrEntrySnapshot> entries,
        out string resolvedTitle,
        out string error)
    {
        resolvedTitle = string.Empty;
        error = string.Empty;

        var trimmed = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "DTR title is required.";
            return false;
        }

        var exact = entries
            .Where(entry => entry.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exact.Length == 1)
        {
            resolvedTitle = exact[0].Title;
            return true;
        }

        var normalized = PluginSnapshot.Normalize(trimmed);
        var normalizedMatches = entries
            .Where(entry => PluginSnapshot.Normalize(entry.Title).Equals(normalized, StringComparison.Ordinal))
            .ToArray();
        if (normalizedMatches.Length == 1)
        {
            resolvedTitle = normalizedMatches[0].Title;
            return true;
        }

        var containsMatches = entries
            .Where(entry => PluginSnapshot.Normalize(entry.Title).Contains(normalized, StringComparison.Ordinal))
            .ToArray();
        if (containsMatches.Length == 1)
        {
            resolvedTitle = containsMatches[0].Title;
            return true;
        }

        error = containsMatches.Length > 1
            ? $"DTR title matched multiple live entries: {string.Join(", ", containsMatches.Select(entry => entry.Title))}."
            : $"No live DTR entry matched '{title}'.";
        return false;
    }

    private static object? GetOwnerPlugin(IReadOnlyDtrBarEntry entry)
        => GetPropertyValue(entry, entry.GetType(), "OwnerPlugin");

    private static object? GetPropertyValue(object? target, Type? type, string propertyName)
        => target == null
            ? null
            : FindProperty(type ?? target.GetType(), propertyName)?.GetValue(target);

    private static string? GetStringProperty(object? target, Type? type, string propertyName)
        => GetPropertyValue(target, type, propertyName)?.ToString();

    private static PropertyInfo? FindProperty(Type? type, string propertyName)
    {
        for (var currentType = type; currentType != null; currentType = currentType.BaseType)
        {
            var property = currentType.GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (property != null)
                return property;
        }

        return null;
    }

    private object GetDalamudConfiguration()
        => dalamudConfiguration ??= GetDalamudService(GetDalamudAssembly().GetType(DalamudConfigurationTypeName, throwOnError: true)!);

    private object GetRawDtrBar()
    {
        if (rawDtrBar != null)
            return rawDtrBar;

        var assembly = GetDalamudAssembly();
        var dtrBarType = assembly.GetType(DtrBarTypeName, throwOnError: false);
        if (dtrBarType != null)
        {
            try
            {
                rawDtrBar = GetDalamudService(dtrBarType);
            }
            catch (Exception ex)
            {
                log.Debug(ex, "[botology] Failed to resolve raw DtrBar service directly.");
            }
        }

        rawDtrBar ??= dtrBar
            .GetType()
            .GetField("dtrBarService", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(dtrBar);

        return rawDtrBar ?? throw new InvalidOperationException("Could not resolve Dalamud DtrBar service.");
    }

    private object GetDalamudInterface()
        => dalamudInterface ??= GetDalamudService(GetDalamudAssembly().GetType(DalamudInterfaceTypeName, throwOnError: true)!);

    private Assembly GetDalamudAssembly()
        => pluginInterface.GetType().Assembly;

    private object GetDalamudService(Type serviceType)
    {
        var serviceGeneric = GetDalamudAssembly()
            .GetType("Dalamud.Service`1", throwOnError: true)!
            .MakeGenericType(serviceType);
        return serviceGeneric
            .GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, Array.Empty<object>())!;
    }

    private static List<string> GetMutableStringList(object configuration, string propertyName)
    {
        var property = GetConfigurationProperty(configuration, propertyName);
        if (property.GetValue(configuration) is List<string> list)
            return list;

        list = new List<string>();
        property.SetValue(configuration, list);
        return list;
    }

    private static void SetStringList(object configuration, string propertyName, List<string> value)
        => GetConfigurationProperty(configuration, propertyName).SetValue(configuration, value);

    private static PropertyInfo GetConfigurationProperty(object configuration, string propertyName)
        => configuration.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
           ?? throw new MissingMemberException(configuration.GetType().FullName, propertyName);

    private static void QueueSave(object configuration)
        => configuration
            .GetType()
            .GetMethod("QueueSave", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(configuration, null);

    private void ApplySort()
        => GetRawDtrBar()
            .GetType()
            .GetMethod("ApplySort", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(GetRawDtrBar(), null);

    private void TryMakeDirty(string title)
        => GetRawDtrBar()
            .GetType()
            .GetMethod("MakeDirty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(GetRawDtrBar(), new object[] { title });
}

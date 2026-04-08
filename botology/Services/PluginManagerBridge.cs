using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using botology.Models;

namespace botology.Services;

public sealed class PluginManagerBridge
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    public PluginManagerBridge(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.log = log;
    }

    public PluginSnapshot CaptureSnapshot()
    {
        try
        {
            var pluginManager = GetPluginManager();
            var installedPlugins = GetEnumerableProperty(pluginManager, "InstalledPlugins");
            var updatableInternalNames = GetUpdatableInternalNames(pluginManager);
            var runtimeStates = new List<PluginRuntimeState>();

            foreach (var localPlugin in installedPlugins)
            {
                var type = GetLocalPluginType(localPlugin);
                var manifest = GetPropertyValue(localPlugin, type, "Manifest");
                var internalName = GetStringProperty(manifest, manifest?.GetType(), "InternalName");
                if (string.IsNullOrWhiteSpace(internalName))
                    continue;

                var name = GetStringProperty(localPlugin, type, "Name") ?? internalName;
                var version = GetVersion(localPlugin, type, manifest);
                var isLoaded = GetBoolProperty(localPlugin, type, "IsLoaded");
                var repoUrl = GetStringProperty(manifest, manifest?.GetType(), "RepoUrl");
                var repoJsonUrl =
                    GetFirstStringProperty(manifest, manifest?.GetType(), "RepoJsonUrl", "SourceRepo", "OriginRepo") ??
                    GetFirstStringProperty(localPlugin, type, "RepoJsonUrl", "ManifestUrl", "SourceRepo", "OriginRepo", "InstalledFromUrl");
                var instance = GetFieldValue(localPlugin, type, "instance");
                var configuration = GetPropertyValue(instance, instance?.GetType(), "Configuration");
                var dtrBarEnabled = GetNullableBoolProperty(configuration, configuration?.GetType(), "DtrBarEnabled");

                runtimeStates.Add(new PluginRuntimeState(
                    internalName,
                    name,
                    version,
                    isLoaded,
                    updatableInternalNames.Contains(PluginSnapshot.Normalize(internalName)),
                    repoUrl,
                    repoJsonUrl,
                    localPlugin,
                    instance,
                    configuration,
                    dtrBarEnabled));
            }

            return new PluginSnapshot(runtimeStates);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Failed to capture plugin snapshot.");
            return PluginSnapshot.Empty;
        }
    }

    public bool TrySetPluginEnabled(PluginRuntimeState runtimeState, bool enabled, out string error)
    {
        error = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(runtimeState.InternalName))
            {
                error = $"Could not toggle {runtimeState.DisplayName}: missing plugin shortname.";
                return false;
            }

            if (enabled == runtimeState.IsLoaded)
                return true;

            var command = enabled
                ? $"/xlenableplugin {runtimeState.InternalName}"
                : $"/xldisableplugin {runtimeState.InternalName}";

            if (!commandManager.ProcessCommand(command))
            {
                error = $"Could not toggle {runtimeState.DisplayName}: launcher command failed for {runtimeState.InternalName}.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Failed to toggle plugin state.");
            error = $"Could not toggle {runtimeState.DisplayName}: {ex.Message}";
            return false;
        }
    }

    public bool TrySetDtrBarEnabled(PluginRuntimeState runtimeState, bool enabled, out string error)
    {
        error = string.Empty;

        try
        {
            if (runtimeState.ConfigurationHandle == null)
            {
                error = $"Could not toggle DTR for {runtimeState.DisplayName}: no live configuration object was found.";
                return false;
            }

            var configurationType = runtimeState.ConfigurationHandle.GetType();
            var dtrProperty = configurationType.GetProperty("DtrBarEnabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (dtrProperty == null || dtrProperty.PropertyType != typeof(bool) || !dtrProperty.CanWrite)
            {
                error = $"Could not toggle DTR for {runtimeState.DisplayName}: DtrBarEnabled was unavailable.";
                return false;
            }

            dtrProperty.SetValue(runtimeState.ConfigurationHandle, enabled);
            configurationType.GetMethod("Save", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(runtimeState.ConfigurationHandle, null);
            runtimeState.PluginInstance?.GetType().GetMethod("UpdateDtrBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(runtimeState.PluginInstance, null);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Failed to toggle plugin DTR state.");
            error = $"Could not toggle DTR for {runtimeState.DisplayName}: {ex.Message}";
            return false;
        }
    }

    private object GetPluginManager()
    {
        return pluginInterface.GetType().Assembly
            .GetType("Dalamud.Service`1", true)!
            .MakeGenericType(pluginInterface.GetType().Assembly.GetType("Dalamud.Plugin.Internal.PluginManager", true)!)
            .GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, Array.Empty<object>())!;
    }

    private static IReadOnlyList<object> GetEnumerableProperty(object target, string propertyName)
    {
        var enumerable = GetPropertyValue(target, target.GetType(), propertyName) as IEnumerable;
        if (enumerable == null)
            return Array.Empty<object>();

        return enumerable.Cast<object>().ToArray();
    }

    private static HashSet<string> GetUpdatableInternalNames(object pluginManager)
    {
        var updates = GetEnumerableProperty(pluginManager, "UpdatablePlugins");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var update in updates)
        {
            var installedPlugin = GetPropertyValue(update, update.GetType(), "InstalledPlugin");
            var installedType = installedPlugin == null ? null : GetLocalPluginType(installedPlugin);
            var manifest = GetPropertyValue(installedPlugin, installedType, "Manifest");
            var internalName = GetStringProperty(manifest, manifest?.GetType(), "InternalName");
            if (!string.IsNullOrWhiteSpace(internalName))
                names.Add(PluginSnapshot.Normalize(internalName));
        }

        return names;
    }

    private static Type GetLocalPluginType(object localPlugin)
        => localPlugin.GetType().Name == "LocalDevPlugin"
            ? localPlugin.GetType().BaseType ?? localPlugin.GetType()
            : localPlugin.GetType();

    private static object? GetFieldValue(object? target, Type? type, string fieldName)
        => target == null || type == null
            ? null
            : type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

    private static object? GetPropertyValue(object? target, Type? type, string propertyName)
        => target == null || type == null
            ? null
            : type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

    private static string? GetStringProperty(object? target, Type? type, string propertyName)
        => GetPropertyValue(target, type, propertyName)?.ToString();

    private static string? GetFirstStringProperty(object? target, Type? type, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetStringProperty(target, type, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool GetBoolProperty(object? target, Type? type, string propertyName)
        => GetPropertyValue(target, type, propertyName) as bool? ?? false;

    private static bool? GetNullableBoolProperty(object? target, Type? type, string propertyName)
        => GetPropertyValue(target, type, propertyName) as bool?;

    private static Version GetVersion(object localPlugin, Type type, object? manifest)
    {
        var effectiveVersion = GetPropertyValue(manifest, manifest?.GetType(), "EffectiveVersion") as Version;
        if (effectiveVersion != null)
            return effectiveVersion;

        var version = GetPropertyValue(localPlugin, type, "Version") as Version;
        if (version != null)
            return version;

        return GetPropertyValue(manifest, manifest?.GetType(), "AssemblyVersion") as Version ?? new Version(0, 0);
    }
}

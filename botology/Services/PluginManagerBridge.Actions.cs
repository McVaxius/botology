using System.Collections;
using System.Reflection;
using botology.Models;

namespace botology.Services;

public sealed partial class PluginManagerBridge
{
    private int installerActionRunning;
    public bool InstallerActionRunning => Volatile.Read(ref installerActionRunning) != 0;

    public async Task<InstallerOperation> ExecuteInstallerActionAsync(InstallerAction action, string repository, string internalName = "")
    {
        var identity = internalName.Length == 0 ? repository : InstallerCatalog.PluginIdentity(repository, internalName);
        try
        {
            var manager = GetPluginManager();
            var configuration = manager.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager)
                ?? throw new NotSupportedException("Host repository management is unavailable.");
            var profiles = GetHostService("Dalamud.Plugin.Internal.Profiles.ProfileManager");
            var player = action is InstallerAction.Enable or InstallerAction.Disable
                ? GetHostService("Dalamud.Game.Player.PlayerState") : null;
            return await ExecuteInstallerActionAsync(manager, configuration, profiles, action, repository, internalName,
                player is null ? 0 : Read(player, "ContentId") is ulong contentId ? contentId : 0);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[botology] Installer operation failed: {Action}", action);
            return new(identity, action, false, false, ex.GetBaseException().Message);
        }
    }

    // Offline fixtures exercise the same boundary with fabricated host objects; never resolve live services in tests.
    internal async Task<InstallerOperation> ExecuteInstallerActionAsync(
        object manager, object configuration, object profiles, InstallerAction action,
        string repository, string internalName = "", ulong contentId = 0)
    {
        var identity = internalName.Length == 0 ? repository : InstallerCatalog.PluginIdentity(repository, internalName);
        if (Interlocked.CompareExchange(ref installerActionRunning, 1, 0) != 0)
            return new(identity, action, false, false, "Another operation is in progress.");
        try { return await ExecuteHostActionAsync(manager, configuration, profiles, action, repository, internalName, contentId); }
        finally { Volatile.Write(ref installerActionRunning, 0); }
    }

    internal static async Task<InstallerOperation> ExecuteHostActionAsync(
        object manager, object configuration, object profiles, InstallerAction action,
        string repository, string internalName = "", ulong contentId = 0)
    {
        var source = InstallerCatalog.NormalizeRepository(repository) ?? repository;
        var identity = internalName.Length == 0 ? source : InstallerCatalog.PluginIdentity(source, internalName);
        InstallerOperation Done(string message) => new(identity, action, false, true, message);
        if (action == InstallerAction.Refresh)
        {
            await InvokeHostAsync(manager, "ReloadAllReposAsync");
            return Done("Repositories refreshed");
        }
        if (action is InstallerAction.AddRepository or InstallerAction.EnableRepository or InstallerAction.DisableRepository or InstallerAction.RemoveRepository)
        {
            if (InstallerCatalog.NormalizeRepository(repository) is null || source == InstallerCatalog.OfficialRepository)
                throw new InvalidOperationException("This repository cannot be changed.");
            var settings = Read(configuration, "ThirdRepoList") as IList
                ?? throw new NotSupportedException("Host repository management is unavailable.");
            var existing = settings.Cast<object>().FirstOrDefault(s => InstallerCatalog.NormalizeRepository(ReadString(s, "Url")) == source);
            if (action == InstallerAction.AddRepository)
            {
                if (existing is not null) throw new InvalidOperationException("Repository already added.");
                var type = settings.GetType().GetGenericArguments().Single();
                var setting = Activator.CreateInstance(type, nonPublic: true)!;
                type.GetProperty("Url")!.SetValue(setting, source);
                type.GetProperty("IsEnabled")!.SetValue(setting, true);
                settings.Add(setting);
            }
            else
            {
                if (existing is null) throw new InvalidOperationException("Repository is no longer added.");
                if (action == InstallerAction.RemoveRepository) settings.Remove(existing);
                else existing.GetType().GetProperty("IsEnabled")!.SetValue(existing, action == InstallerAction.EnableRepository);
            }
            InvokeHost(configuration, "QueueSave");
            await InvokeHostAsync(manager, "SetPluginReposFromConfigAsync", true);
            if (action is InstallerAction.AddRepository or InstallerAction.EnableRepository)
            {
                var loaded = GetEnumerableProperty(manager, "Repos").SingleOrDefault(r =>
                    InstallerCatalog.NormalizeRepository(ReadString(r, "PluginMasterUrl")) == source);
                if (loaded is null || ReadString(loaded, "State") != "Success")
                    throw new InvalidOperationException("Repository saved, but its manifest could not be loaded.");
            }
            return Done(action switch
            {
                InstallerAction.AddRepository => "Repository added",
                InstallerAction.EnableRepository => "Repository enabled",
                InstallerAction.DisableRepository => "Repository disabled",
                _ => "Repository removed",
            });
        }

        if (!ReadBool(manager, "PluginsReady") || !ReadBool(manager, "ReposReady") || ReadBool(profiles, "IsBusy"))
            throw new InvalidOperationException("The host is busy. Wait for its current operation to finish.");
        var installed = GetEnumerableProperty(manager, "InstalledPlugins");
        if (action == InstallerAction.UpdateAll)
        {
            if (ReadBool(manager, "SafeMode")) throw new InvalidOperationException("This operation is unavailable in safe mode.");
            // Select from the current native host, independently of the visible page's filters.
            var updates = installed.Select(plugin => (Local: plugin, Source: InstalledSource(plugin), Name: ReadString(plugin, "InternalName")))
                .Where(item => FindInstallerUpdate(manager, item.Local, item.Source) is not null).ToArray();
            if (updates.Length == 0) return Done("No eligible updates.");
            var completed = 0;
            foreach (var update in updates)
            {
                try
                {
                    // Keep the outer operation gate while each native update is awaited, and recheck every gate at dispatch.
                    await ExecuteHostActionAsync(manager, configuration, profiles, InstallerAction.Update, update.Source, update.Name);
                    completed++;
                }
                catch (Exception ex)
                {
                    return new(identity, action, false, false, "Update all stopped after {0} of {1} updates: {2}. {3}")
                    { MessageArguments = [completed, updates.Length, update.Name + " (" + update.Source + ")", ex.GetBaseException().Message] };
                }
            }
            return new(identity, action, false, true, "Completed {0} updates.") { MessageArguments = [completed] };
        }
        var local = installed.FirstOrDefault(p => string.Equals(ReadString(p, "InternalName"), internalName, StringComparison.OrdinalIgnoreCase)
            && InstalledSource(p) == source);
        if (action == InstallerAction.Install)
        {
            if (ReadBool(manager, "SafeMode")) throw new InvalidOperationException("This operation is unavailable in safe mode.");
            if (installed.Any(p => string.Equals(ReadString(p, "InternalName"), internalName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This plugin name is already installed. Its source will not be replaced.");
            var remote = GetEnumerableProperty(manager, "AvailablePlugins").SingleOrDefault(m =>
                string.Equals(ReadString(m, "InternalName"), internalName, StringComparison.OrdinalIgnoreCase) && ManifestSource(m) == source)
                ?? throw new InvalidOperationException("The selected plugin is not available from this added repository.");
            if (Read(remote, "DalamudApiLevel") is not int api || api != PluginAssessmentRow.CurrentDalamudApiLevel
                || ReadBool(remote, "IsTestingExclusive") || ReadBool(remote, "IsHide"))
                throw new InvalidOperationException("This plugin is not available for stable installation on this API.");
            RequireEnabledRepository(manager, source);
            var method = HostMethod(manager, "InstallPluginAsync", 3);
            var reason = Enum.Parse(method.GetParameters()[2].ParameterType, "Installer");
            var result = await AwaitHost(method.Invoke(manager, [remote, false, reason]));
            if (result is null || ReadString(result, "State") is not ("Loaded" or "Unloaded") || InstalledSource(result) != source
                || !GetEnumerableProperty(manager, "InstalledPlugins").Any(p => ReferenceEquals(p, result)))
                throw new InvalidOperationException("The host did not complete installation successfully.");
            return Done("Installed");
        }
        if (local is null) throw new InvalidOperationException("The installed plugin no longer matches this source.");
        if (action == InstallerAction.Update)
        {
            var update = FindInstallerUpdate(manager, local, source)
                ?? throw new InvalidOperationException("No eligible update is available from the installed source.");
            var result = await InvokeHostAsync(manager, "UpdateSinglePluginAsync", update, true, false);
            var status = result is null ? "" : ReadString(result, "Status");
            if (status is not ("Success" or "AlreadyUpToDate")) throw new InvalidOperationException(status.Length == 0 ? "Update failed" : status);
            return Done(status == "AlreadyUpToDate" ? "Already up to date" : "Updated");
        }
        if (action == InstallerAction.Uninstall)
        {
            if (ReadBool(local, "IsDev") || ReadBool(local, "IsLoaded") || ReadString(local, "State") is "Loading" or "Unloading")
                throw new InvalidOperationException("Disable this plugin before uninstalling it.");
            InvokeHost(local, "ScheduleDeletion", true);
            if (ReadString(local, "State") is "Unloaded" or "DependencyResolutionFailed")
                InvokeHost(manager, "RemovePlugin", local);
            var manifest = Read(local, "Manifest");
            if (manifest is null || !ReadBool(manifest, "ScheduledForDeletion"))
                throw new InvalidOperationException("The host did not schedule removal.");
            // Native cleanup owns deleting the files later. Plugin configuration is deliberately retained.
            return Done("Removal scheduled");
        }
        if (action is not (InstallerAction.Enable or InstallerAction.Disable)) throw new ArgumentOutOfRangeException(nameof(action));
        if (ReadBool(manager, "SafeMode") || ReadString(local, "State") is "Loading" or "Unloading"
            || (action == InstallerAction.Enable && (ReadBool(local, "IsBanned") || ReadBool(local, "IsOutdated"))))
            throw new InvalidOperationException("This operation is unavailable in the current host state.");
        var enabling = action == InstallerAction.Enable;
        if (ReadBool(local, "IsLoaded") == enabling) return Done(enabling ? "Enabled" : "Disabled");
        var workingId = Read(local, "EffectiveWorkingPluginId")
            ?? throw new NotSupportedException("The host plugin identity is unavailable.");
        object[] memberships;
        using ((IDisposable)InvokeHost(profiles, "GetSyncScope")!)
            memberships = GetEnumerableProperty(profiles, "Profiles").Where(p => InvokeHost(p, "WantsPlugin", workingId) is not null).ToArray();
        if (memberships.Length == 0 || (!ReadBool(local, "IsInDefaultProfile") && memberships.Length != 1))
            throw new InvalidOperationException("Manage this plugin through its collections.");
        var profile = memberships[0];
        if (memberships.Length == 1 && (!ReadBool(profile, "IsEnabled") || InvokeHost(profile, "CheckWantsActiveFromGameState", contentId) is not true))
            throw new InvalidOperationException("The plugin collection is not active.");
        if (ReadBool(local, "IsDev")) InvokeHost(local, "ReloadManifest");
        if (enabling)
        {
            await InvokeHostAsync(profile, "AddOrUpdateAsync", workingId, internalName, true, false);
            var load = HostMethod(local, "LoadAsync", 3);
            await AwaitHost(load.Invoke(local, [Enum.Parse(load.GetParameters()[0].ParameterType, "Installer"), false, CancellationToken.None]));
        }
        else
        {
            await InvokeHostAsync(local, "UnloadAsync");
            await InvokeHostAsync(profile, "AddOrUpdateAsync", workingId, internalName, false, false);
        }
        if (ReadBool(local, "IsLoaded") != enabling) throw new InvalidOperationException("The host did not reach the requested plugin state.");
        return Done(enabling ? "Enabled" : "Disabled");
    }

    private static string InstalledSource(object local)
    {
        if (ReadBool(local, "IsDev")) return "development:" + ReadString(local, "InternalName");
        if (!ReadBool(local, "IsThirdParty")) return InstallerCatalog.OfficialRepository;
        return Read(local, "Manifest") is { } manifest
            ? InstallerCatalog.NormalizeRepository(ReadString(manifest, "InstalledFromUrl")) ?? "unknown:" + ReadString(local, "InternalName")
            : "unknown:" + ReadString(local, "InternalName");
    }
    private static string? ManifestSource(object manifest)
        => Read(manifest, "SourceRepo") is { } repo ? InstallerCatalog.NormalizeRepository(ReadString(repo, "PluginMasterUrl")) : null;
    private static object? FindInstallerUpdate(object manager, object local, string source)
    {
        if (ReadBool(manager, "SafeMode") || ReadBool(local, "IsDev") || ReadBool(local, "IsBanned")) return null;
        if (!GetEnumerableProperty(manager, "Repos").Any(repo => InstallerCatalog.NormalizeRepository(ReadString(repo, "PluginMasterUrl")) == source
            && ReadBool(repo, "IsEnabled") && ReadString(repo, "State") == "Success")) return null;
        if (!GetEnumerableProperty(manager, "AvailablePlugins").Any(manifest =>
            string.Equals(ReadString(manifest, "InternalName"), ReadString(local, "InternalName"), StringComparison.OrdinalIgnoreCase)
            && ManifestSource(manifest) == source)) return null;
        // Native testing-channel selection is retained; stable-install-only flags do not apply to an installed plugin's update.
        return GetEnumerableProperty(manager, "UpdatablePlugins").SingleOrDefault(update =>
            ReferenceEquals(Read(update, "InstalledPlugin"), local) && Read(update, "UpdateManifest") is { } manifest
            && ManifestSource(manifest) == source && InstallerUpdateApiLevel(update) == PluginAssessmentRow.CurrentDalamudApiLevel);
    }
    private static int InstallerUpdateApiLevel(object update)
    {
        if (Read(update, "UpdateManifest") is not { } manifest) return 0;
        return ReadBool(update, "UseTesting") && Read(manifest, "TestingDalamudApiLevel") is int testingApi ? testingApi
            : Read(manifest, "DalamudApiLevel") is int stableApi ? stableApi : 0;
    }
    private static void RequireEnabledRepository(object manager, string source)
    {
        if (!GetEnumerableProperty(manager, "Repos").Any(r => InstallerCatalog.NormalizeRepository(ReadString(r, "PluginMasterUrl")) == source
            && ReadBool(r, "IsEnabled") && ReadString(r, "State") == "Success"))
            throw new InvalidOperationException("The repository must be added, enabled and loaded first.");
    }
    private static MethodInfo HostMethod(object target, string name, int supplied)
        => target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(m => m.Name == name && m.GetParameters().Length >= supplied
                && m.GetParameters().Skip(supplied).All(p => p.IsOptional))
            ?? throw new NotSupportedException("The required host operation is unavailable: " + name);
    private static object? InvokeHost(object target, string name, params object?[] args)
    {
        var method = HostMethod(target, name, args.Length);
        var parameters = method.GetParameters();
        var values = parameters.Select((p, i) => i < args.Length ? args[i] : p.DefaultValue).ToArray();
        return method.Invoke(target, values);
    }
    private static Task<object?> InvokeHostAsync(object target, string name, params object?[] args)
        => AwaitHost(InvokeHost(target, name, args));
    private static async Task<object?> AwaitHost(object? pending)
    {
        if (pending is not Task task) throw new NotSupportedException("The host did not return an asynchronous operation.");
        await task;
        return Read(task, "Result");
    }
}

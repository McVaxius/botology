using botology.Models;
using botology.Services;

internal static class InstallerActionTests
{
    public static async Task<IReadOnlyList<string>> CheckAsync()
    {
        var failures = new List<string>();
        void Require(bool condition, string message) { if (!condition) failures.Add("Installer action: " + message); }
        async Task Reject(Func<Task> action, string message)
        {
            try { await action(); failures.Add("Installer action: " + message); }
            catch (InvalidOperationException) { }
        }
        const string source = "https://example.invalid/feed.json";
        const string other = "https://other.invalid/feed.json";
        var configuration = new HostConfiguration();
        var manager = new HostManager(configuration);
        var profiles = new HostProfiles();
        Task<InstallerOperation> Run(InstallerAction action, string feed = source, string name = "Sample")
            => PluginManagerBridge.ExecuteHostActionAsync(manager, configuration, profiles, action, feed, name);
        var added = await Run(InstallerAction.AddRepository, name: "");
        Require(added.Succeeded && configuration.ThirdRepoList.Single().Url == source && configuration.SaveCalls == 1
            && manager.InstallCalls == 0, "Adding a repository must use host save/refresh and install nothing.");
        await Reject(() => Run(InstallerAction.AddRepository, name: ""), "Adding an existing source must not duplicate it.");
        await Reject(() => Run(InstallerAction.RemoveRepository, InstallerCatalog.OfficialRepository, ""), "Official source removal must be rejected.");

        var remote = new HostManifest { InternalName = "Sample", SourceRepo = manager.Repos.Single(r => r.PluginMasterUrl == source) };
        manager.AvailablePlugins.Add(remote);
        manager.InstallGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Run(InstallerAction.Install);
        Require(!pending.IsCompleted && manager.InstalledPlugins.Count == 0, "Dispatched install is not completed before the native task finishes.");
        manager.InstallGate.SetResult();
        var installed = await pending;
        var local = manager.InstalledPlugins.Single();
        profiles.Profiles[0].Members[local.EffectiveWorkingPluginId] = true;
        Require(installed.Succeeded && local.IsLoaded && ReferenceEquals(manager.LastInstalledManifest, remote),
            "Install must await actual host state and pass the exact selected source manifest.");
        await Reject(() => Run(InstallerAction.Install, other), "An installed internal name must not be replaced from another source.");
        Require(manager.InstallCalls == 1, "A rejected source collision must not dispatch a native install.");
        await Reject(() => Run(InstallerAction.Uninstall), "Loaded plugins must be disabled before native removal.");
        Require(!local.Manifest.ScheduledForDeletion, "Rejected uninstall must not schedule deletion.");
        var disabled = await Run(InstallerAction.Disable);
        Require(disabled.Succeeded && !local.IsLoaded && !profiles.Profiles[0].Members[local.EffectiveWorkingPluginId],
            "Disable must await unload and update the existing profile preference.");
        var enabled = await Run(InstallerAction.Enable);
        Require(enabled.Succeeded && local.IsLoaded && profiles.Profiles[0].Members[local.EffectiveWorkingPluginId],
            "Enable must update the profile and await actual loaded state.");
        local.FailLoad = true;
        await Run(InstallerAction.Disable);
        await Reject(() => Run(InstallerAction.Enable), "A completed task with failed load state must not report success.");
        local.FailLoad = false;
        local.State = "Unloaded";
        await Run(InstallerAction.Enable);

        manager.UpdatablePlugins.Add(new() { InstalledPlugin = local, UpdateManifest = new() { InternalName = "Sample", SourceRepo = new() { PluginMasterUrl = other } } });
        await Reject(() => Run(InstallerAction.Update), "An update from another source must not be selected.");
        manager.UpdatablePlugins.Add(new() { InstalledPlugin = local, UpdateManifest = remote });
        manager.UpdateResult = HostUpdateState.FailedDownload;
        await Reject(() => Run(InstallerAction.Update), "Native failed-download status must be a failed operation even when its task completes.");
        manager.UpdateResult = HostUpdateState.Success;
        Require((await Run(InstallerAction.Update)).Succeeded && manager.LastUpdate!.UpdateManifest.SourceRepo.PluginMasterUrl == source,
            "Successful update must use the installed origin and native result.");

        profiles.IsBusy = true;
        await Reject(() => Run(InstallerAction.Disable), "Busy host collections must block conflicting actions without redispatch.");
        profiles.IsBusy = false;
        await Run(InstallerAction.Disable);
        var removed = await Run(InstallerAction.Uninstall);
        Require(removed.Succeeded && removed.Message == "Removal scheduled" && local.Manifest.ScheduledForDeletion
            && !manager.InstalledPlugins.Contains(local) && manager.ConfigurationDeletes == 0,
            "Native uninstall schedules cleanup/removes the unloaded registration and retains plugin configuration.");
        var installCount = manager.InstallCalls;
        await Run(InstallerAction.DisableRepository, name: "");
        Require(configuration.ThirdRepoList.Single().IsEnabled == false && manager.InstallCalls == installCount,
            "Source disable must not install or delete plugins.");
        await Run(InstallerAction.EnableRepository, name: "");
        await Run(InstallerAction.RemoveRepository, name: "");
        Require(configuration.ThirdRepoList.Count == 0 && configuration.UnrelatedSetting == "retained",
            "Repository removal must change only the selected source setting.");
        manager.FailRepository = true;
        await Reject(() => Run(InstallerAction.AddRepository, name: ""), "A saved source with failed manifest refresh must not report fully loaded success.");
        Require(configuration.ThirdRepoList.Count == 1, "A failed refresh must not silently undo a saved source or install anything.");
        return failures;
    }

    private enum HostReason { Installer }
    private enum HostDisposal { WaitBeforeDispose }
    private enum HostUpdateState { Success, AlreadyUpToDate, FailedDownload }
    private sealed class HostRepo
    {
        public string PluginMasterUrl { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string State { get; set; } = "Success";
    }
    private sealed class HostManifest
    {
        public string InternalName { get; set; } = "";
        public HostRepo SourceRepo { get; set; } = new();
        public int DalamudApiLevel => 15;
        public bool IsTestingExclusive => false;
        public bool IsHide => false;
    }
    private sealed class HostLocalManifest
    {
        public string InstalledFromUrl { get; set; } = "";
        public bool ScheduledForDeletion { get; set; }
    }
    private sealed class HostLocal
    {
        public string InternalName { get; set; } = "";
        public HostLocalManifest Manifest { get; set; } = new();
        public string State { get; set; } = "Loaded";
        public bool IsLoaded => State == "Loaded";
        public bool IsThirdParty => Manifest.InstalledFromUrl != InstallerCatalog.OfficialRepository;
        public bool IsDev => false;
        public bool IsInDefaultProfile => true;
        public Guid EffectiveWorkingPluginId { get; } = Guid.NewGuid();
        public bool FailLoad { get; set; }
        public Task LoadAsync(HostReason reason, bool reloading = false, CancellationToken cancellationToken = default)
        { State = FailLoad ? "LoadError" : "Loaded"; return Task.CompletedTask; }
        public Task UnloadAsync(HostDisposal disposal = HostDisposal.WaitBeforeDispose)
        { State = "Unloaded"; return Task.CompletedTask; }
        public void ScheduleDeletion(bool status = true) => Manifest.ScheduledForDeletion = status;
    }
    private sealed class HostSetting { public string Url { get; set; } = ""; public bool IsEnabled { get; set; } }
    private sealed class HostConfiguration
    {
        public List<HostSetting> ThirdRepoList { get; } = [];
        public int SaveCalls { get; private set; }
        public string UnrelatedSetting => "retained";
        public void QueueSave() => SaveCalls++;
    }
    private sealed class HostUpdate { public HostLocal InstalledPlugin { get; set; } = null!; public HostManifest UpdateManifest { get; set; } = null!; }
    private sealed class HostUpdateResult { public HostUpdateState Status { get; set; } }
    private sealed class HostManager(HostConfiguration configuration)
    {
        public bool PluginsReady => true;
        public bool ReposReady => true;
        public bool SafeMode => false;
        public List<HostRepo> Repos { get; } = [];
        public List<HostManifest> AvailablePlugins { get; } = [];
        public List<HostLocal> InstalledPlugins { get; } = [];
        public List<HostUpdate> UpdatablePlugins { get; } = [];
        public TaskCompletionSource? InstallGate { get; set; }
        public HostManifest? LastInstalledManifest { get; private set; }
        public HostUpdate? LastUpdate { get; private set; }
        public HostUpdateState UpdateResult { get; set; }
        public int InstallCalls { get; private set; }
        public int ConfigurationDeletes { get; private set; }
        public bool FailRepository { get; set; }
        public Task SetPluginReposFromConfigAsync(bool notify)
        {
            Repos.Clear();
            Repos.Add(new() { PluginMasterUrl = InstallerCatalog.OfficialRepository });
            Repos.AddRange(configuration.ThirdRepoList.Where(r => r.IsEnabled).Select(r => new HostRepo { PluginMasterUrl = r.Url, State = FailRepository ? "Fail" : "Success" }));
            return Task.CompletedTask;
        }
        public Task ReloadAllReposAsync() => SetPluginReposFromConfigAsync(true);
        public async Task<HostLocal> InstallPluginAsync(HostManifest manifest, bool testing, HostReason reason)
        {
            InstallCalls++;
            LastInstalledManifest = manifest;
            if (InstallGate is not null) await InstallGate.Task;
            var local = new HostLocal { InternalName = manifest.InternalName, Manifest = new() { InstalledFromUrl = manifest.SourceRepo.PluginMasterUrl } };
            InstalledPlugins.Add(local);
            return local;
        }
        public Task<HostUpdateResult> UpdateSinglePluginAsync(HostUpdate update, bool notify, bool dryRun)
        { LastUpdate = update; return Task.FromResult(new HostUpdateResult { Status = UpdateResult }); }
        public void RemovePlugin(HostLocal local) => InstalledPlugins.Remove(local);
        public Task DeleteConfigurationAsync(HostLocal local) { ConfigurationDeletes++; return Task.CompletedTask; }
    }
    private sealed class HostProfile
    {
        public Dictionary<Guid, bool> Members { get; } = [];
        public bool IsEnabled => true;
        public bool? WantsPlugin(Guid id) => Members.TryGetValue(id, out var wanted) ? wanted : null;
        public bool CheckWantsActiveFromGameState(ulong id) => true;
        public Task AddOrUpdateAsync(Guid id, string name, bool wanted, bool apply)
        { Members[id] = wanted; return Task.CompletedTask; }
    }
    private sealed class HostProfiles
    {
        public bool IsBusy { get; set; }
        public HostProfile[] Profiles { get; } = [new()];
        public IDisposable GetSyncScope() => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}

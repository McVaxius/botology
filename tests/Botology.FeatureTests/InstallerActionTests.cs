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
        await CheckUpdateAllAsync(Require, Reject);
        await CheckUpdateChannelsAsync(Require, Reject);
        return failures;
    }

    private static async Task CheckUpdateAllAsync(Action<bool, string> require, Func<Func<Task>, string, Task> reject)
    {
        const string source = "https://batch.invalid/plugins.json";
        const string other = "https://other.invalid/plugins.json";
        var configuration = new HostConfiguration();
        var manager = new HostManager(configuration);
        var profiles = new HostProfiles();
        var bridge = new PluginManagerBridge(null!, null!, null!);
        Task<InstallerOperation> Run(InstallerAction action = InstallerAction.UpdateAll, string feed = "", string name = "")
            => bridge.ExecuteInstallerActionAsync(manager, configuration, profiles, action, feed, name);
        HostUpdate Add(string name, string feed = source)
        {
            var repo = manager.Repos.FirstOrDefault(candidate => candidate.PluginMasterUrl == feed);
            if (repo is null) { repo = new() { PluginMasterUrl = feed }; manager.Repos.Add(repo); }
            var local = new HostLocal { InternalName = name, Manifest = new() { InstalledFromUrl = feed } };
            var remote = new HostManifest { InternalName = name, SourceRepo = repo };
            var update = new HostUpdate { InstalledPlugin = local, UpdateManifest = remote };
            manager.InstalledPlugins.Add(local); manager.AvailablePlugins.Add(remote); manager.UpdatablePlugins.Add(update);
            return update;
        }
        var official = Add("Official", InstallerCatalog.OfficialRepository);
        var third = Add("Third");
        var testing = Add("Testing"); testing.UpdateManifest.IsTestingExclusive = true; testing.InstalledPlugin.IsTesting = true;
        testing.UpdateManifest.DalamudApiLevel = 14; testing.UpdateManifest.TestingDalamudApiLevel = 15; testing.UseTesting = true;
        Add("Development").InstalledPlugin.IsDev = true;
        Add("Banned").InstalledPlugin.IsBanned = true;
        Add("WrongApi").UpdateManifest.DalamudApiLevel = 14;
        Add("Disabled", "https://disabled.invalid/plugins.json").UpdateManifest.SourceRepo.IsEnabled = false;
        Add("FailedRepo", "https://failed.invalid/plugins.json").UpdateManifest.SourceRepo.State = "Fail";
        var missingRepo = Add("MissingRepo", "https://missing.invalid/plugins.json"); manager.Repos.Remove(missingRepo.UpdateManifest.SourceRepo);
        var unavailable = Add("Unavailable"); manager.AvailablePlugins.Remove(unavailable.UpdateManifest);
        var noUpdate = Add("NoUpdate"); manager.UpdatablePlugins.Remove(noUpdate);
        var wrongSource = Add("WrongSource");
        wrongSource.UpdateManifest = new() { InternalName = "WrongSource", SourceRepo = new() { PluginMasterUrl = other } };
        var wrongUpdateApi = Add("WrongUpdateApi");
        wrongUpdateApi.UpdateManifest = new() { InternalName = "WrongUpdateApi", SourceRepo = third.UpdateManifest.SourceRepo, DalamudApiLevel = 14 };

        foreach (var state in new[] { "plugins", "repositories", "profiles", "safe" })
        {
            manager.PluginsReady = state != "plugins"; manager.ReposReady = state != "repositories";
            profiles.IsBusy = state == "profiles"; manager.SafeMode = state == "safe";
            await reject(() => Run(), "Update all must reject a non-ready, busy or safe-mode host: " + state);
            require(manager.UpdateCalls.Count == 0 && !bridge.InstallerActionRunning, "Blocked batches must dispatch nothing and release the operation gate.");
        }
        manager.SafeMode = false;
        foreach (var name in new[] { "Development", "Banned", "WrongApi", "Disabled", "FailedRepo", "MissingRepo", "Unavailable", "NoUpdate", "WrongSource", "WrongUpdateApi" })
        {
            var local = manager.InstalledPlugins.Single(plugin => plugin.InternalName == name);
            await reject(() => Run(InstallerAction.Update, local.Manifest.InstalledFromUrl, name), "A direct row update must preserve native eligibility: " + name);
        }
        require(manager.UpdateCalls.Count == 0, "Rejected row updates must never reach native dispatch.");
        var eligible = new[] { official, third, testing };
        foreach (var update in eligible)
        {
            manager.UpdateGates[update.InstalledPlugin.InternalName] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.UpdateEntered[update.InstalledPlugin.InternalName] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        manager.UpdateResults["Third"] = HostUpdateState.AlreadyUpToDate;
        var pending = Run();
        require(!pending.IsCompleted && bridge.InstallerActionRunning && manager.UpdateCalls.SequenceEqual([official]),
            "Update all must await the first native lifecycle before starting the next eligible plugin.");
        var conflicting = await Run(InstallerAction.Refresh);
        require(!conflicting.Succeeded && bridge.InstallerActionRunning && manager.UpdateCalls.Count == 1,
            "The existing single-operation gate must reject another installer operation throughout a batch.");
        for (var index = 0; index < eligible.Length; index++)
        {
            var update = eligible[index];
            await manager.UpdateEntered[update.InstalledPlugin.InternalName].Task.WaitAsync(TimeSpan.FromSeconds(5));
            require(manager.UpdateCalls.SequenceEqual(eligible.Take(index + 1)) && !pending.IsCompleted && bridge.InstallerActionRunning,
                "Native updates must start in order, one at a time, while retaining the batch gate.");
            manager.UpdateGates[update.InstalledPlugin.InternalName].SetResult();
        }
        var complete = await pending;
        require(complete.Succeeded && complete.Message == "Completed {0} updates." && complete.MessageArguments!.SequenceEqual(new object[] { 3 })
            && manager.UpdateCalls.SequenceEqual(eligible) && manager.MaximumActiveUpdates == 1 && !bridge.InstallerActionRunning,
            "Only all three eligible updates, including native testing and already-current results, must complete serially.");
        manager.UpdatablePlugins.Clear();
        var empty = await Run();
        require(empty.Succeeded && empty.Message == "No eligible updates." && manager.UpdateCalls.Count == 3,
            "An empty eligible set must finish without any native update.");

        manager.InstalledPlugins.Clear(); manager.AvailablePlugins.Clear(); manager.UpdateCalls.Clear();
        manager.UpdateGates.Clear(); manager.UpdateEntered.Clear(); manager.UpdateResults.Clear();
        var first = Add("First"); var failed = Add("Failed"); Add("Remaining");
        manager.UpdateResults["Failed"] = HostUpdateState.FailedDownload;
        var partial = await Run();
        require(!partial.Succeeded && partial.MessageArguments is [1, 3, string failedIdentity, "FailedDownload"]
            && failedIdentity.Contains("Failed (" + source + ")", StringComparison.Ordinal)
            && manager.UpdateCalls.SequenceEqual([first, failed]) && !bridge.InstallerActionRunning,
            "A native failed status must stop at that plugin, report completed/selected counts and source, and never dispatch the remainder.");
        manager.UpdateResults.Clear(); manager.UpdateCalls.Clear();
        manager.AfterUpdate = update => { if (ReferenceEquals(update, failed)) throw new InvalidOperationException("Fabricated native failure"); };
        var faulted = await Run();
        require(!faulted.Succeeded && faulted.MessageArguments is [1, 3, _, "Fabricated native failure"]
            && manager.UpdateCalls.SequenceEqual([first, failed]) && !bridge.InstallerActionRunning,
            "A faulted native task must stop the batch with its completed count and release the same operation gate.");

        foreach (var change in new[] { "plugins", "repositories", "profiles", "safe", "development", "banned", "api", "update-api", "testing-api", "testing-selection", "source", "unavailable", "disabled", "failed-repository", "removed", "no-update" })
        {
            manager.PluginsReady = manager.ReposReady = true; manager.SafeMode = profiles.IsBusy = false;
            manager.InstalledPlugins.Clear(); manager.AvailablePlugins.Clear(); manager.UpdatablePlugins.Clear(); manager.Repos.Clear();
            manager.UpdateCalls.Clear(); manager.UpdateResults.Clear();
            first = Add("First"); var changed = Add("Changed"); Add("Remaining");
            if (change is "testing-api" or "testing-selection")
            { changed.UpdateManifest.DalamudApiLevel = 14; changed.UpdateManifest.TestingDalamudApiLevel = 15; changed.UseTesting = true; }
            manager.AfterUpdate = update =>
            {
                if (!ReferenceEquals(update, first)) return;
                switch (change)
                {
                    case "plugins": manager.PluginsReady = false; break;
                    case "repositories": manager.ReposReady = false; break;
                    case "profiles": profiles.IsBusy = true; break;
                    case "safe": manager.SafeMode = true; break;
                    case "development": changed.InstalledPlugin.IsDev = true; break;
                    case "banned": changed.InstalledPlugin.IsBanned = true; break;
                    case "api": changed.UpdateManifest.DalamudApiLevel = 14; break;
                    case "update-api": changed.UpdateManifest = new() { InternalName = "Changed", SourceRepo = first.UpdateManifest.SourceRepo, DalamudApiLevel = 14 }; break;
                    case "testing-api": changed.UpdateManifest.TestingDalamudApiLevel = 14; break;
                    case "testing-selection": changed.UseTesting = false; break;
                    case "source": changed.InstalledPlugin.Manifest.InstalledFromUrl = other; break;
                    case "unavailable": manager.AvailablePlugins.Remove(changed.UpdateManifest); break;
                    case "disabled": changed.UpdateManifest.SourceRepo.IsEnabled = false; break;
                    case "failed-repository": changed.UpdateManifest.SourceRepo.State = "Fail"; break;
                    case "removed": manager.InstalledPlugins.Remove(changed.InstalledPlugin); break;
                    case "no-update": manager.UpdatablePlugins.Remove(changed); break;
                }
            };
            var stopped = await Run();
            require(!stopped.Succeeded && stopped.MessageArguments![0] is 1 && manager.UpdateCalls.SequenceEqual([first]) && !bridge.InstallerActionRunning,
                "Every selected update must re-read native eligibility before actual dispatch: " + change);
        }
    }

    private static async Task CheckUpdateChannelsAsync(Action<bool, string> require, Func<Func<Task>, string, Task> reject)
    {
        const string source = InstallerCatalog.OfficialRepository;
        foreach (var channel in new (int Stable, int? Testing, bool UseTesting, bool Eligible)[]
        {
            (14,15,true,true), (15,14,true,false), (15,null,true,true), (14,null,true,false),
            (14,15,false,false), (15,14,false,true), (15,null,false,true),
        })
        {
            var configuration = new HostConfiguration();
            var manager = new HostManager(configuration);
            var profiles = new HostProfiles();
            var repo = new HostRepo { PluginMasterUrl = source };
            var local = new HostLocal { InternalName = "ChannelFixture", Manifest = new() { InstalledFromUrl = source } };
            var manifest = new HostManifest { InternalName = local.InternalName, SourceRepo = repo,
                DalamudApiLevel = channel.Stable, TestingDalamudApiLevel = channel.Testing };
            var update = new HostUpdate { InstalledPlugin = local, UpdateManifest = manifest, UseTesting = channel.UseTesting };
            manager.Repos.Add(repo); manager.InstalledPlugins.Add(local); manager.AvailablePlugins.Add(manifest); manager.UpdatablePlugins.Add(update);
            var runtime = new PluginRuntimeState(local.InternalName,local.InternalName,new(1,0),true,true,null,null,local,null,null,null);
            var captured = PluginManagerBridge.CaptureInstallerHost(manager,configuration,new([runtime]));
            var entry = InstallerCatalog.Merge(captured,[],[],[]).Entries.Single();
            var description = $"stable {channel.Stable}, testing {channel.Testing}, UseTesting {channel.UseTesting}";
            var effectiveApi = channel.UseTesting ? channel.Testing ?? channel.Stable : channel.Stable;
            require(entry.CanUpdate == channel.Eligible && entry.Installed!.UpdateApiLevel == effectiveApi
                && entry.Manifest.ApiLevel == channel.Stable && entry.Manifest.Identity == InstallerCatalog.PluginIdentity(source,local.InternalName),
                "Host capture must use the native selected update channel without changing stable manifest identity: " + description);
            require((entry with { Installed = null }).CanInstall == (channel.Stable == 15),
                "A testing update API must never widen stable Install eligibility: " + description);
            Task<InstallerOperation> Run(InstallerAction action)
                => PluginManagerBridge.ExecuteHostActionAsync(manager,configuration,profiles,action,source,local.InternalName);
            if (channel.Eligible)
            {
                require((await Run(InstallerAction.Update)).Succeeded, "Individual Update must accept the native selected-channel API: " + description);
                require((await Run(InstallerAction.UpdateAll)).Succeeded && manager.UpdateCalls.Count == 2
                    && manager.UpdateCalls.All(item => ReferenceEquals(item,update)) && update.UseTesting == channel.UseTesting
                    && ReferenceEquals(update.UpdateManifest,manifest),
                    "Batch and individual updates must pass the original native record and UseTesting choice unchanged: " + description);
            }
            else
            {
                await reject(() => Run(InstallerAction.Update), "Individual Update must reject the native selected-channel API: " + description);
                var batch = await Run(InstallerAction.UpdateAll);
                require(batch.Succeeded && batch.Message == "No eligible updates." && manager.UpdateCalls.Count == 0,
                    "Batch selection must exclude an incompatible selected channel: " + description);
            }
            if (channel.Stable != 15)
            {
                manager.InstalledPlugins.Clear();
                await reject(() => Run(InstallerAction.Install), "Testing API compatibility must not authorize native stable installation: " + description);
                require(manager.InstallCalls == 0, "An incompatible stable installation must never dispatch.");
            }
        }
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
        public int DalamudApiLevel { get; set; } = 15;
        public int? TestingDalamudApiLevel { get; set; }
        public bool IsTestingExclusive { get; set; }
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
        public bool IsDev { get; set; }
        public bool IsBanned { get; set; }
        public bool IsTesting { get; set; }
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
    private sealed class HostUpdate
    {
        public HostLocal InstalledPlugin { get; set; } = null!;
        public HostManifest UpdateManifest { get; set; } = null!;
        public bool UseTesting { get; set; }
    }
    private sealed class HostUpdateResult { public HostUpdateState Status { get; set; } }
    private sealed class HostManager(HostConfiguration configuration)
    {
        public bool PluginsReady { get; set; } = true;
        public bool ReposReady { get; set; } = true;
        public bool SafeMode { get; set; }
        public List<HostRepo> Repos { get; } = [];
        public List<HostManifest> AvailablePlugins { get; } = [];
        public List<HostLocal> InstalledPlugins { get; } = [];
        public List<HostUpdate> UpdatablePlugins { get; } = [];
        public TaskCompletionSource? InstallGate { get; set; }
        public HostManifest? LastInstalledManifest { get; private set; }
        public HostUpdate? LastUpdate { get; private set; }
        public HostUpdateState UpdateResult { get; set; }
        public List<HostUpdate> UpdateCalls { get; } = [];
        public Dictionary<string, HostUpdateState> UpdateResults { get; } = [];
        public Dictionary<string, TaskCompletionSource> UpdateGates { get; } = [];
        public Dictionary<string, TaskCompletionSource> UpdateEntered { get; } = [];
        public Action<HostUpdate>? AfterUpdate { get; set; }
        public int MaximumActiveUpdates { get; private set; }
        private int activeUpdates;
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
        public async Task<HostUpdateResult> UpdateSinglePluginAsync(HostUpdate update, bool notify, bool dryRun)
        {
            LastUpdate = update; UpdateCalls.Add(update);
            MaximumActiveUpdates = Math.Max(MaximumActiveUpdates, ++activeUpdates);
            var name = update.InstalledPlugin.InternalName;
            UpdateEntered.GetValueOrDefault(name)?.TrySetResult();
            try
            {
                if (UpdateGates.TryGetValue(name, out var gate)) await gate.Task;
                AfterUpdate?.Invoke(update);
                return new() { Status = UpdateResults.GetValueOrDefault(name, UpdateResult) };
            }
            finally { activeUpdates--; }
        }
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

using botology.Models;
using botology.Services;

namespace botology;

public sealed partial class Plugin
{
    private InstallerOperation? installerOperation;
    public InstallerOperation? InstallerOperation => Volatile.Read(ref installerOperation);
    public bool DiscoveryRunning => RepositoryMetadataService.DiscoveryRunning;
    public IReadOnlyDictionary<string, string> DiscoveryErrors => RepositoryMetadataService.DiscoveryErrors;

    public InstallerCatalogSnapshot CaptureInstaller()
    {
        var host = PluginManagerBridge.CaptureInstallerHost();
        var snapshot = InstallerCatalog.Merge(host, RepositoryMetadataService.CaptureDiscoveryManifests(), CaptureCatalogEntries(), CaptureRows());
        RepositoryMetadataService.QueueDiscovery(snapshot.Repositories.Select(r => r.Url).Append(InstallerCatalog.OfficialRepository));
        return snapshot;
    }

    public void RefreshInstaller()
    {
        var snapshot = CaptureInstaller();
        RepositoryMetadataService.QueueDiscovery(snapshot.Repositories.Select(r => r.Url).Append(InstallerCatalog.OfficialRepository), force: true);
        RefreshMasterCatalog(force: true, silent: true);
        RunInstallerAction(InstallerAction.Refresh, InstallerCatalog.OfficialRepository);
    }

    public void RunInstallerAction(InstallerAction action, string repository, string internalName = "")
    {
        if (InstallerOperation?.IsRunning == true) return;
        Volatile.Write(ref installerOperation, new(internalName.Length == 0 ? repository : InstallerCatalog.PluginIdentity(repository, internalName),
            action, true, false, "Working..."));
        _ = CompleteInstallerActionAsync(action, repository, internalName);
    }

    private async Task CompleteInstallerActionAsync(InstallerAction action, string repository, string internalName)
    {
        var result = await PluginManagerBridge.ExecuteInstallerActionAsync(action, repository, internalName);
        Volatile.Write(ref installerOperation, result);
    }

    public void ToggleRepositoryPin(string repository)
    { InstallerCatalog.ToggleRepositoryPin(Configuration, repository); Configuration.Save(); }
    public void ToggleAuthorPin(string author)
    { InstallerCatalog.ToggleAuthorPin(Configuration, author); Configuration.Save(); }
}

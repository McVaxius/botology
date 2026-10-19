namespace botology.Models;

public sealed record InstallerRepository(
    string Url, string Name, bool IsAdded, bool IsEnabled, bool IsOfficial, string Status = "")
{
    public string Identity => Services.InstallerCatalog.NormalizeRepository(Url) ?? Url;
}

public sealed record InstallerManifest(
    string InternalName,
    string Name,
    string RepositoryUrl,
    string Author = "",
    string Description = "",
    string Version = "",
    int ApiLevel = 0,
    string? ProjectUrl = null,
    string? IconUrl = null,
    string[]? ImageUrls = null,
    string[]? Tags = null,
    bool IsTestingExclusive = false,
    bool IsHidden = false,
    string? Changelog = null,
    string? Dip17Channel = null,
    string[]? CategoryTags = null)
{
    public string Identity => Services.InstallerCatalog.PluginIdentity(RepositoryUrl, InternalName);
    public IReadOnlyList<string> Authors => Services.InstallerCatalog.ParseAuthors(Author);
    // Dalamud's official distribution supplies artwork by channel, not IconUrl/ImageUrls.
    private string? OfficialImageRoot => RepositoryUrl == Services.InstallerCatalog.OfficialRepository && !string.IsNullOrWhiteSpace(Dip17Channel)
        ? "https://raw.githubusercontent.com/goatcorp/PluginDistD17/main/"+Uri.EscapeDataString(Dip17Channel)+"/"+Uri.EscapeDataString(InternalName)+"/images/" : null;
    public string? PublisherIconUrl => OfficialImageRoot is { } root ? root+"icon.png" : IconUrl;
    public string[] PublisherImageUrls => OfficialImageRoot is { } root ? Enumerable.Range(1,5).Select(i=>root+"image"+i+".png").ToArray() : ImageUrls??[];
}

public sealed record InstallerInstalledPlugin(
    PluginRuntimeState Runtime,
    string RepositoryUrl,
    string State,
    bool IsDevelopment = false,
    bool IsTesting = false,
    bool IsBanned = false,
    bool IsOutdated = false,
    bool ScheduledForDeletion = false);

public sealed record InstallerHostSnapshot(
    IReadOnlyList<InstallerRepository> Repositories,
    IReadOnlyList<InstallerManifest> Manifests,
    IReadOnlyList<InstallerInstalledPlugin> Installed,
    bool Ready,
    bool SafeMode,
    string? Error = null)
{
    public static InstallerHostSnapshot Empty { get; } = new([], [], [], false, false);
}

public sealed record InstallerEntry(
    InstallerManifest Manifest,
    InstallerRepository Repository,
    InstallerInstalledPlugin? Installed,
    PluginAssessmentRow? Compatibility,
    bool NameCollision,
    bool AvailableFromHost,
    bool HostReady = true,
    bool HostSafeMode = false,
    IReadOnlyList<string>? ManifestTags = null,
    string? BotologyCategory = null)
{
    public string Identity => Manifest.Identity;
    public bool IsInstalled => Installed is not null;
    public bool IsLoaded => Installed?.Runtime.IsLoaded == true;
    public bool HasUpdate => Installed?.Runtime.HasUpdate == true;
    public bool IsApiCompatible => Manifest.ApiLevel == PluginAssessmentRow.CurrentDalamudApiLevel;
    public string? InstallBlockReason => IsInstalled ? "Installed" : NameCollision ? "Other source installed"
        : !Repository.IsAdded ? "Repository not added" : !Repository.IsEnabled ? "Repository disabled"
        : !IsApiCompatible ? "Unavailable for this API" : Manifest.IsTestingExclusive ? "Testing only"
        : Manifest.IsHidden ? "Hidden plugin" : HostSafeMode ? "Safe mode" : !HostReady ? "Host not ready"
        : Repository.Status != "Success" ? "Source unavailable" : !AvailableFromHost ? "Source refresh required" : null;
    public bool CanInstall => InstallBlockReason is null;
    public bool CanUpdate => HostReady && !HostSafeMode && HasUpdate && Installed is { IsDevelopment: false, IsBanned: false }
        && Repository.IsAdded && Repository.IsEnabled && Repository.Status == "Success" && AvailableFromHost && IsApiCompatible;
    public bool CanUninstall => HostReady && Installed is { IsDevelopment: false } && !IsLoaded
        && Installed.State is not ("Loading" or "Unloading");
}

public sealed record InstallerCatalogSnapshot(
    IReadOnlyList<InstallerRepository> Repositories,
    IReadOnlyList<InstallerEntry> Entries,
    bool Ready,
    bool SafeMode,
    string? Error = null,
    IReadOnlyList<string>? KnownAuthors = null,
    IReadOnlyList<string>? DalamudTags = null,
    IReadOnlyList<string>? BotologyCategories = null,
    IReadOnlyList<string>? ThirdPartyTags = null)
{
    public static InstallerCatalogSnapshot Empty { get; } = new([], [], false, false);
}

public enum InstallerAction { Install, Update, Uninstall, Enable, Disable, AddRepository, EnableRepository, DisableRepository, RemoveRepository, Refresh }

public sealed record InstallerOperation(
    string Identity, InstallerAction Action, bool IsRunning, bool Succeeded, string Message);

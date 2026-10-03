using botology.Models;

namespace botology.Windows;

// The windows consume snapshots and existing actions. Offline tests supply this boundary, never Plugin().
internal interface IBotologyUi
{
    Configuration Configuration { get; }
    void DrawAppearanceSelector();
    IReadOnlyList<PluginAssessmentRow> CaptureRows();
    IReadOnlyList<DtrEntrySnapshot> CaptureDtrEntries();
    IReadOnlyList<PluginCatalogEntry> CaptureCatalogEditorEntries();
    IReadOnlyList<CatalogRelease> CaptureCatalogReleases();
    CatalogRefreshInfo GetCatalogRefreshInfo();
    PluginCatalogEntry? GetMasterCatalogEntry(string id);
    void OpenUrl(string url);
    void OpenConfigUi();
    void OpenDtrManagerUi();
    void OpenCatalogEditorUi();
    void OpenCatalogFolder();
    bool OpenCatalogScriptFolder();
    bool PrepareCatalogUploadPackage();
    void RunTextCommand(string command);
    void PrintStatus(string message);
    void RefreshMasterCatalog(bool force = false, bool silent = false);
    void RescheduleMasterCatalogCheck();
    void SetPluginEnabled(bool enabled, bool printStatus = false);
    void SetHideUninstalledPlugins(bool hide);
    void SetIgnored(string id, bool ignored);
    void ToggleTrackedPlugin(PluginRuntimeState runtimeState);
    void ToggleTrackedPluginDtr(PluginRuntimeState runtimeState, bool enabled);
    bool TryGetGlobalDtrEntry(PluginAssessmentRow row, IReadOnlyList<DtrEntrySnapshot> entries, out DtrEntrySnapshot? entry);
    void SetGlobalDtrEntryVisible(string title, bool visible);
    void MoveGlobalDtrEntry(string title, int delta);
    void OpenServerInfoBarSettings(string? searchText = null);
    bool TryGetBlockingAlert(out string message, out bool shouldOpenPopup);
    void AcknowledgeBlockingAlert();
    void UpdateDtrBar();
    void SaveCatalogEntry(PluginCatalogEntry entry);
    bool ReplaceWithMasterData(string id);
    bool HideMasterCatalogEntry(string id);
    bool RestoreMasterCatalogEntry(string id);
    int DropAllLocalCatalogChanges();
}

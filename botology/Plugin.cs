using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using botology.Models;
using botology.Services;
using botology.Windows;

namespace botology;

public sealed class Plugin : IDalamudPlugin
{
    private const string ReviewScriptResourceName = "botology.review_catalog.py";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }
    public PluginManagerBridge PluginManagerBridge { get; }
    public RepositoryMetadataService RepositoryMetadataService { get; }
    public WindowSystem WindowSystem { get; } = new(PluginInfo.InternalName);

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly CatalogEditorWindow catalogEditorWindow;
    private IDtrBarEntry? dtrEntry;
    private DateTime nextAssessmentCheckUtc = DateTime.MinValue;
    private DateTime nextMasterCatalogCheckUtc = DateTime.MinValue;
    private string lastAssessmentFingerprint = string.Empty;
    private string? pendingBlockingAlertMessage;
    private bool shouldOpenBlockingAlertPopup;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        PluginManagerBridge = new PluginManagerBridge(PluginInterface, CommandManager, Log);
        RepositoryMetadataService = new RepositoryMetadataService(Log);
        RepositoryLinkCatalog.Reload();

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        catalogEditorWindow = new CatalogEditorWindow(this);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(catalogEditorWindow);

        RegisterCommands();

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        Framework.Update += OnFrameworkUpdate;

        SetupDtrBar();
        UpdateDtrBar();
        RescheduleMasterCatalogCheck();

        if (Configuration.OpenMainWindowOnLoad)
            OpenMainUi();

        Log.Information("[botology] Plugin loaded.");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;

        UnregisterCommands();
        WindowSystem.RemoveAllWindows();
        dtrEntry?.Remove();

        RepositoryMetadataService.Dispose();
        catalogEditorWindow.Dispose();
        configWindow.Dispose();
        mainWindow.Dispose();
    }

    public void OpenMainUi()
    {
        ReloadRepositoryLinks(silent: true);
        mainWindow.IsOpen = true;
        QueueRepositoryMetadataRefresh();
    }

    public void ToggleMainUi()
    {
        if (!mainWindow.IsOpen)
        {
            OpenMainUi();
            return;
        }

        mainWindow.Toggle();
    }

    public void OpenConfigUi()
    {
        ReloadRepositoryLinks(silent: true);
        configWindow.IsOpen = true;
        QueueRepositoryMetadataRefresh();
    }

    public void OpenCatalogEditorUi()
    {
        ReloadRepositoryLinks(silent: true);
        catalogEditorWindow.IsOpen = true;
    }

    public void ToggleConfigUi() => configWindow.Toggle();

    public void SetPluginEnabled(bool enabled, bool printStatus = false)
    {
        Configuration.PluginEnabled = enabled;
        Configuration.Save();
        UpdateDtrBar();

        if (printStatus)
            PrintStatus(enabled ? "Plugin enabled." : "Plugin disabled.");
    }

    public void ResetWindowPositions()
    {
        mainWindow.QueueResetToOrigin();
        configWindow.QueueResetToOrigin();
        catalogEditorWindow.QueueResetToOrigin();
        mainWindow.IsOpen = true;
        configWindow.IsOpen = true;
        catalogEditorWindow.IsOpen = true;
        PrintStatus("Queued all Botology windows to reset to 1,1.");
    }

    public void JumpWindows()
    {
        mainWindow.QueueRandomVisibleJump();
        configWindow.QueueRandomVisibleJump();
        catalogEditorWindow.QueueRandomVisibleJump();
        mainWindow.IsOpen = true;
        configWindow.IsOpen = true;
        catalogEditorWindow.IsOpen = true;
        PrintStatus("Queued a random visible jump for the Botology windows.");
    }

    public void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[botology] Failed to open URL.");
            PrintStatus($"Could not open link: {url}");
        }
    }

    public void PrintStatus(string message) => ChatGui.Print($"[{PluginInfo.DisplayName}] {message}");

    public void RunTextCommand(string command)
    {
        if (!CommandManager.ProcessCommand(command))
            PrintStatus($"Could not run command: {command}");
    }

    public void ReloadRepositoryLinks(bool silent = false)
    {
        RepositoryLinkCatalog.Reload();
        RepositoryMetadataService.InvalidateRefreshThrottle();
        lastAssessmentFingerprint = string.Empty;
        nextAssessmentCheckUtc = DateTime.MinValue;
        QueueRepositoryMetadataRefresh();

        if (!silent)
            PrintStatus("Reloaded local master cache and overlay files.");
    }

    public IReadOnlyList<PluginAssessmentRow> CaptureRows()
    {
        var rows = CaptureBaseRows();
        return rows
            .Select(row => row with { Metadata = RepositoryMetadataService.GetCachedMetadata(row) })
            .ToArray();
    }

    public IReadOnlyList<PluginCatalogEntry> CaptureCatalogEntries()
        => BotologyCatalog.Entries;

    public IReadOnlyList<PluginCatalogEntry> CaptureMasterCatalogEntries()
        => RepositoryLinkCatalog.GetMasterEntries(BotologyCatalog.FallbackEntries);

    public IReadOnlyList<PluginCatalogEntry> CaptureOverlayCatalogEntries()
        => RepositoryLinkCatalog.GetOverlayEntries(BotologyCatalog.FallbackEntries);

    public IReadOnlyList<string> GetKnownCatalogIds()
        => RepositoryLinkCatalog.GetKnownIds(BotologyCatalog.FallbackEntries);

    public PluginCatalogEntry? GetMasterCatalogEntry(string id)
        => RepositoryLinkCatalog.GetMasterEntry(id, BotologyCatalog.FallbackEntries);

    public PluginCatalogEntry? GetOverlayCatalogEntry(string id)
        => RepositoryLinkCatalog.GetOverlayEntry(id, BotologyCatalog.FallbackEntries);

    public CatalogRefreshInfo GetCatalogRefreshInfo()
        => RepositoryLinkCatalog.GetRefreshInfo();

    public void SaveCatalogEntry(PluginCatalogEntry entry)
    {
        RepositoryLinkCatalog.SaveOverlayEntry(entry, BotologyCatalog.FallbackEntries);
        RepositoryMetadataService.InvalidateRefreshThrottle();
        lastAssessmentFingerprint = string.Empty;
        nextAssessmentCheckUtc = DateTime.MinValue;
        QueueRepositoryMetadataRefresh();
        PrintStatus($"Saved local overlay row for {entry.DisplayName}.");
    }

    public bool ReplaceWithMasterData(string id)
    {
        var removed = RepositoryLinkCatalog.RemoveOverlayEntry(id, BotologyCatalog.FallbackEntries);
        if (removed)
        {
            RepositoryMetadataService.InvalidateRefreshThrottle();
            lastAssessmentFingerprint = string.Empty;
            nextAssessmentCheckUtc = DateTime.MinValue;
            QueueRepositoryMetadataRefresh();
        }

        return removed;
    }

    public int DropAllLocalCatalogChanges()
    {
        var removedCount = RepositoryLinkCatalog.ClearOverlayEntries(BotologyCatalog.FallbackEntries);
        if (removedCount > 0)
        {
            RepositoryMetadataService.InvalidateRefreshThrottle();
            lastAssessmentFingerprint = string.Empty;
            nextAssessmentCheckUtc = DateTime.MinValue;
            QueueRepositoryMetadataRefresh();
            PrintStatus($"Dropped {removedCount} local catalog change rows.");
        }

        return removedCount;
    }

    public bool PrepareCatalogUploadPackage()
    {
        var overlayEntries = CaptureOverlayCatalogEntries();
        if (overlayEntries.Count == 0)
        {
            PrintStatus("There are no local catalog changes to prepare for interactive review.");
            return false;
        }

        try
        {
            var scriptPath = EnsureReviewScriptInstalled();
            var refreshInfo = GetCatalogRefreshInfo();
            var outputDirectory = Path.Combine(RepositoryLinkCatalog.GetCatalogDirectory(), "upload-prep", DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(outputDirectory);

            var masterPath = Path.Combine(outputDirectory, "master.json");
            var localPath = Path.Combine(outputDirectory, "local.json");
            var uploadPath = Path.Combine(outputDirectory, "plugin-repository-links.json");
            var reportPath = Path.Combine(outputDirectory, "review-report.json");

            RepositoryLinkCatalog.ExportCatalogManifest(masterPath, CaptureMasterCatalogEntries(), writeToEntriesArray: false, sourceUrl: refreshInfo.SourceUrl);
            RepositoryLinkCatalog.ExportCatalogManifest(localPath, overlayEntries, writeToEntriesArray: true);
            RepositoryLinkCatalog.ExportCatalogManifest(uploadPath, CaptureCatalogEntries(), writeToEntriesArray: false, sourceUrl: refreshInfo.SourceUrl);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k py \"{scriptPath}\" \"{masterPath}\" \"{localPath}\" --output \"{reportPath}\"",
                WorkingDirectory = outputDirectory,
                UseShellExecute = true,
            });

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{outputDirectory}\"",
                UseShellExecute = true,
            });

            PrintStatus($"Prepared local upload-prep package in {outputDirectory}. No upload was sent; use the command window to approve or deny each changed row.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[botology] Failed to prepare the catalog upload package.");
            PrintStatus($"Could not prepare the catalog upload package: {ex.Message}");
            return false;
        }
    }

    public bool OpenCatalogScriptFolder()
    {
        try
        {
            var scriptPath = EnsureReviewScriptInstalled();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{scriptPath}\"",
                UseShellExecute = true,
            });

            PrintStatus("Opened the folder containing review_catalog.py.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[botology] Failed to open the review script folder.");
            PrintStatus($"Could not open the review script folder: {ex.Message}");
            return false;
        }
    }

    public void OpenCatalogFolder()
    {
        var folder = RepositoryLinkCatalog.GetCatalogDirectory();
        if (string.IsNullOrWhiteSpace(folder))
        {
            PrintStatus("Could not locate the catalog folder.");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true,
            });

            PrintStatus("Opened the Botology catalog folder.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[botology] Failed to open catalog folder.");
            PrintStatus("Could not open the Botology catalog folder.");
        }
    }

    private string EnsureReviewScriptInstalled()
    {
        var toolsDirectory = Path.Combine(RepositoryLinkCatalog.GetCatalogDirectory(), "tools");
        Directory.CreateDirectory(toolsDirectory);

        var scriptPath = Path.Combine(toolsDirectory, "review_catalog.py");
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ReviewScriptResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ReviewScriptResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        var scriptContents = reader.ReadToEnd();

        if (!File.Exists(scriptPath) || !string.Equals(File.ReadAllText(scriptPath), scriptContents, StringComparison.Ordinal))
            File.WriteAllText(scriptPath, scriptContents, Encoding.UTF8);

        return scriptPath;
    }

    public void RefreshMasterCatalog(bool force = false, bool silent = false)
        => _ = RefreshMasterCatalogAsync(force, silent);

    public void RescheduleMasterCatalogCheck()
    {
        if (!Configuration.EnablePeriodicMasterCatalogChecks)
        {
            nextMasterCatalogCheckUtc = DateTime.MaxValue;
            return;
        }

        var intervalMinutes = Math.Max(1, Configuration.MasterCatalogCheckIntervalMinutes);
        var nextCheck = RepositoryLinkCatalog.GetNextAutomaticCheckUtc(TimeSpan.FromMinutes(intervalMinutes)).UtcDateTime;
        nextMasterCatalogCheckUtc = nextCheck <= DateTime.UtcNow
            ? DateTime.UtcNow
            : nextCheck;
    }

    public void SetHideUninstalledPlugins(bool hide)
    {
        Configuration.HideUninstalledPlugins = hide;
        Configuration.Save();
    }

    public void SetIgnored(string id, bool ignored)
    {
        Configuration.IgnoredPluginIds.RemoveAll(existing => existing.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (ignored)
            Configuration.IgnoredPluginIds.Add(id);

        Configuration.Save();
    }

    public void ToggleTrackedPlugin(PluginRuntimeState runtimeState)
    {
        var targetState = !runtimeState.IsLoaded;
        if (!PluginManagerBridge.TrySetPluginEnabled(runtimeState, targetState, out var error))
        {
            PrintStatus(error);
            return;
        }

        PrintStatus($"{runtimeState.DisplayName} {(targetState ? "enabled" : "disabled")}.");
        nextAssessmentCheckUtc = DateTime.MinValue;
    }

    public void ToggleTrackedPluginDtr(PluginRuntimeState runtimeState, bool enabled)
    {
        if (!PluginManagerBridge.TrySetDtrBarEnabled(runtimeState, enabled, out var error))
        {
            PrintStatus(error);
            return;
        }

        PrintStatus($"{runtimeState.DisplayName} DTR {(enabled ? "enabled" : "disabled")}.");
        nextAssessmentCheckUtc = DateTime.MinValue;
    }

    public bool TryGetBlockingAlert(out string message, out bool shouldOpenPopup)
    {
        message = pendingBlockingAlertMessage ?? string.Empty;
        shouldOpenPopup = shouldOpenBlockingAlertPopup && !string.IsNullOrWhiteSpace(pendingBlockingAlertMessage);
        shouldOpenBlockingAlertPopup = false;
        return !string.IsNullOrWhiteSpace(pendingBlockingAlertMessage);
    }

    public void AcknowledgeBlockingAlert()
        => pendingBlockingAlertMessage = null;

    public void UpdateDtrBar()
    {
        if (dtrEntry == null)
            return;

        dtrEntry.Shown = Configuration.DtrBarEnabled;
        if (!Configuration.DtrBarEnabled)
            return;

        var glyph = Configuration.PluginEnabled ? Configuration.DtrIconEnabled : Configuration.DtrIconDisabled;
        var state = Configuration.PluginEnabled ? "On" : "Off";
        dtrEntry.Text = Configuration.DtrBarMode switch
        {
            1 => new SeString(new TextPayload($"{glyph} BOT")),
            2 => new SeString(new TextPayload(glyph)),
            _ => new SeString(new TextPayload($"BOT: {state}")),
        };
        dtrEntry.Tooltip = new SeString(new TextPayload($"{PluginInfo.DisplayName} {state}. Click to open the manager."));
    }

    private async Task RefreshMasterCatalogAsync(bool force, bool silent)
    {
        var result = await RepositoryLinkCatalog.RefreshMasterCatalogAsync(BotologyCatalog.FallbackEntries, force).ConfigureAwait(false);
        RepositoryMetadataService.InvalidateRefreshThrottle();
        lastAssessmentFingerprint = string.Empty;
        nextAssessmentCheckUtc = DateTime.MinValue;
        QueueRepositoryMetadataRefresh();
        RescheduleMasterCatalogCheck();

        if (result.Changed && Configuration.ToastOnMasterCatalogChange)
            ShowOperatorToast("Botology master catalog changed and was refreshed.");

        if (!silent || result.Changed)
            PrintStatus(result.Message);
    }

    private IReadOnlyList<PluginAssessmentRow> CaptureBaseRows()
    {
        var ignoredIds = new HashSet<string>(Configuration.IgnoredPluginIds, StringComparer.OrdinalIgnoreCase);
        return BotologyCatalog.BuildRows(PluginManagerBridge.CaptureSnapshot(), ignoredIds);
    }

    private void QueueRepositoryMetadataRefresh()
        => RepositoryMetadataService.QueueRefresh(CaptureBaseRows());

    private void RegisterCommands()
    {
        var helpMessage =
            "/botology: open or toggle the main window.\n" +
            "/botology config: open settings.\n" +
            "/botology ws: reset Botology windows to 1,1.\n" +
            "/botology j: randomize Botology window positions.\n" +
            "/botology status: print the current summary to chat.\n" +
            "/botology on|off: enable or disable the manager.";

        CommandManager.AddHandler(PluginInfo.Command, new CommandInfo(OnCommand) { HelpMessage = helpMessage });
        foreach (var alias in PluginInfo.CommandAliases)
            CommandManager.AddHandler(alias, new CommandInfo(OnCommand) { HelpMessage = helpMessage });
    }

    private void UnregisterCommands()
    {
        CommandManager.RemoveHandler(PluginInfo.Command);
        foreach (var alias in PluginInfo.CommandAliases)
            CommandManager.RemoveHandler(alias);
    }

    private void OnCommand(string command, string arguments)
    {
        var trimmed = arguments.Trim();
        if (trimmed.Equals("config", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("settings", StringComparison.OrdinalIgnoreCase))
        {
            OpenConfigUi();
            return;
        }

        if (trimmed.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            SetPluginEnabled(true, printStatus: true);
            return;
        }

        if (trimmed.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            SetPluginEnabled(false, printStatus: true);
            return;
        }

        if (trimmed.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            PrintStatus(BotologyCatalog.BuildAlertSummary(CaptureRows()));
            return;
        }

        if (trimmed.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            ResetWindowPositions();
            return;
        }

        if (trimmed.Equals("j", StringComparison.OrdinalIgnoreCase))
        {
            JumpWindows();
            return;
        }

        ToggleMainUi();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        UpdateDtrBar();

        if (Configuration.EnablePeriodicMasterCatalogChecks && DateTime.UtcNow >= nextMasterCatalogCheckUtc)
        {
            nextMasterCatalogCheckUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, Configuration.MasterCatalogCheckIntervalMinutes));
            RefreshMasterCatalog(force: false, silent: true);
        }

        if (DateTime.UtcNow < nextAssessmentCheckUtc)
            return;

        nextAssessmentCheckUtc = DateTime.UtcNow.AddSeconds(2);

        if (mainWindow.IsOpen || configWindow.IsOpen || catalogEditorWindow.IsOpen)
            QueueRepositoryMetadataRefresh();

        if (!Configuration.PluginEnabled)
            return;

        var rows = CaptureRows();
        var alertRows = rows
            .Where(row => row.IsAssessable && !row.Ignored && row.Assessment.Severity != AssessmentSeverity.Green)
            .ToList();

        var fingerprint = string.Join("|", alertRows.Select(row =>
            $"{row.Entry.Id}:{row.Assessment.Severity}:{row.Assessment.Summary}"));

        if (fingerprint == lastAssessmentFingerprint)
            return;

        lastAssessmentFingerprint = fingerprint;
        if (alertRows.Count == 0)
            return;

        var message = BotologyCatalog.BuildAlertSummary(alertRows);

        if (Configuration.ToastNotifications)
            ToastGui.ShowNormal(new SeString(new TextPayload(message)));

        if (Configuration.OpenWindowOnAssessmentChange || Configuration.BlockingPopupNotifications)
            mainWindow.IsOpen = true;

        if (Configuration.BlockingPopupNotifications)
        {
            pendingBlockingAlertMessage = message;
            shouldOpenBlockingAlertPopup = true;
        }
    }

    private void SetupDtrBar()
    {
        dtrEntry = DtrBar.Get(PluginInfo.DisplayName);
        dtrEntry.OnClick = _ => OpenMainUi();
    }

    private void ShowOperatorToast(string message)
    {
        var payload = new SeString(new TextPayload(message));
        var showQuestMethod = ToastGui.GetType().GetMethod("ShowQuest", new[] { typeof(SeString) });
        if (showQuestMethod != null)
        {
            showQuestMethod.Invoke(ToastGui, new object[] { payload });
            return;
        }

        ToastGui.ShowNormal(payload);
    }
}

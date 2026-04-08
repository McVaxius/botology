using System;
using System.Collections.Generic;
using System.Linq;
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
    private IDtrBarEntry? dtrEntry;
    private DateTime nextAssessmentCheckUtc = DateTime.MinValue;
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

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        RegisterCommands();

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        Framework.Update += OnFrameworkUpdate;

        SetupDtrBar();
        UpdateDtrBar();

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
        mainWindow.IsOpen = true;
        configWindow.IsOpen = true;
        PrintStatus("Queued both Botology windows to reset to 1,1.");
    }

    public void JumpWindows()
    {
        mainWindow.QueueRandomVisibleJump();
        configWindow.QueueRandomVisibleJump();
        mainWindow.IsOpen = true;
        configWindow.IsOpen = true;
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
            PrintStatus("Reloaded repository link JSON.");
    }

    public IReadOnlyList<PluginAssessmentRow> CaptureRows()
    {
        var rows = CaptureBaseRows();
        return rows
            .Select(row => row with { Metadata = RepositoryMetadataService.GetCachedMetadata(row) })
            .ToArray();
    }

    public void OpenJsonFolder()
    {
        var manifestPath = RepositoryLinkCatalog.GetManifestPath();
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            PrintStatus("Could not locate plugin-repository-links.json.");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{manifestPath}\"",
                UseShellExecute = true,
            });

            PrintStatus("Opened the JSON folder. Share good changes on Discord or GitHub if you want them included.");
            ShowOperatorToast("Opened plugin-repository-links.json. Share useful updates on Discord or GitHub if you want them included.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[botology] Failed to open JSON folder.");
            PrintStatus("Could not open the JSON folder.");
        }
    }

    private IReadOnlyList<PluginAssessmentRow> CaptureBaseRows()
    {
        var ignoredIds = new HashSet<string>(Configuration.IgnoredPluginIds, StringComparer.OrdinalIgnoreCase);
        return BotologyCatalog.BuildRows(PluginManagerBridge.CaptureSnapshot(), ignoredIds);
    }

    private void QueueRepositoryMetadataRefresh()
        => RepositoryMetadataService.QueueRefresh(CaptureBaseRows());

    public void SetIgnored(string id, bool ignored)
    {
        Configuration.IgnoredPluginIds.RemoveAll(existing => existing.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (ignored)
            Configuration.IgnoredPluginIds.Add(id);

        Configuration.Save();
    }

    public void SetHideUninstalledPlugins(bool hide)
    {
        Configuration.HideUninstalledPlugins = hide;
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

    private void RegisterCommands()
    {
        var helpMessage =
            $"Open {PluginInfo.DisplayName}. Use {PluginInfo.Command} config for settings, {PluginInfo.Command} ws to reset window positions, or /botologist j to jump the windows.";

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

        if (DateTime.UtcNow < nextAssessmentCheckUtc)
            return;

        nextAssessmentCheckUtc = DateTime.UtcNow.AddSeconds(2);

        if (mainWindow.IsOpen || configWindow.IsOpen)
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

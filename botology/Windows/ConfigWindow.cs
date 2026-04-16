using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace botology.Windows;

public sealed class ConfigWindow : PositionedWindow, IDisposable
{
    private static readonly string[] DtrModes = { "Text only", "Icon + text", "Icon only" };
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base($"{PluginInfo.DisplayName} Settings##Config")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700f, 520f),
            MaximumSize = new Vector2(1500f, 1300f),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;

        var enabled = cfg.PluginEnabled;
        if (ImGui.Checkbox("Plugin enabled", ref enabled))
            plugin.SetPluginEnabled(enabled, printStatus: true);

        var dtr = cfg.DtrBarEnabled;
        if (ImGui.Checkbox("Show DTR bar entry", ref dtr))
        {
            cfg.DtrBarEnabled = dtr;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var mode = cfg.DtrBarMode;
        if (ImGui.Combo("DTR mode", ref mode, DtrModes, DtrModes.Length))
        {
            cfg.DtrBarMode = mode;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var onIcon = cfg.DtrIconEnabled;
        if (ImGui.InputText("DTR enabled glyph", ref onIcon, 8))
        {
            cfg.DtrIconEnabled = onIcon.Length <= 3 ? onIcon : onIcon[..3];
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var offIcon = cfg.DtrIconDisabled;
        if (ImGui.InputText("DTR disabled glyph", ref offIcon, 8))
        {
            cfg.DtrIconDisabled = offIcon.Length <= 3 ? offIcon : offIcon[..3];
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var toastNotifications = cfg.ToastNotifications;
        if (ImGui.Checkbox("Toast warnings on changes", ref toastNotifications))
        {
            cfg.ToastNotifications = toastNotifications;
            cfg.Save();
        }

        var masterToastNotifications = cfg.ToastOnMasterCatalogChange;
        if (ImGui.Checkbox("Toast when master catalog changes", ref masterToastNotifications))
        {
            cfg.ToastOnMasterCatalogChange = masterToastNotifications;
            cfg.Save();
        }

        var popupNotifications = cfg.BlockingPopupNotifications;
        if (ImGui.Checkbox("Popup box that requires OK", ref popupNotifications))
        {
            cfg.BlockingPopupNotifications = popupNotifications;
            cfg.Save();
        }

        var openWindow = cfg.OpenWindowOnAssessmentChange;
        if (ImGui.Checkbox("Open the plugin window on changes", ref openWindow))
        {
            cfg.OpenWindowOnAssessmentChange = openWindow;
            cfg.Save();
        }

        var openOnLoad = cfg.OpenMainWindowOnLoad;
        if (ImGui.Checkbox("Open main window on load", ref openOnLoad))
        {
            cfg.OpenMainWindowOnLoad = openOnLoad;
            cfg.Save();
        }

        var periodicChecks = cfg.EnablePeriodicMasterCatalogChecks;
        if (ImGui.Checkbox("Enable periodic master catalog checks", ref periodicChecks))
        {
            cfg.EnablePeriodicMasterCatalogChecks = periodicChecks;
            cfg.Save();
            plugin.RescheduleMasterCatalogCheck();
        }

        var intervalMinutes = Math.Max(1, cfg.MasterCatalogCheckIntervalMinutes);
        if (ImGui.InputInt("Master catalog check interval (minutes)", ref intervalMinutes))
        {
            cfg.MasterCatalogCheckIntervalMinutes = Math.Max(1, intervalMinutes);
            cfg.Save();
            plugin.RescheduleMasterCatalogCheck();
        }

        if (ImGui.SmallButton("Reload master now"))
            plugin.RefreshMasterCatalog(force: true, silent: false);
        ImGui.SameLine();
        if (ImGui.SmallButton("Open DATA editor"))
            plugin.OpenCatalogEditorUi();
        ImGui.SameLine();
        if (ImGui.SmallButton("Open DATA folder"))
            plugin.OpenCatalogFolder();

        ImGui.Separator();
        var refreshInfo = plugin.GetCatalogRefreshInfo();
        ImGui.TextWrapped("The grid uses live installed, enabled, update-available, and best-effort DTR detection from Dalamud. If a plugin hides its configuration or repo URL, Botology will leave that control blank instead of guessing.");
        ImGui.TextWrapped("Ignore flags remove rows from alert calculations but keep them visible in the grid as blue rows.");
        ImGui.TextWrapped($"Master source: {refreshInfo.SourceUrl ?? "Unknown"}");
        ImGui.TextWrapped($"Last master check: {(refreshInfo.LastCheckedUtc?.ToLocalTime().ToString("g") ?? "Never")}");
        ImGui.TextWrapped($"Last master update: {(refreshInfo.LastUpdatedUtc?.ToLocalTime().ToString("g") ?? "Never")}");

        FinalizePendingWindowPlacement();
    }
}

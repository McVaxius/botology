using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace botology.Windows;

public sealed class ConfigWindow : PositionedWindow, IDisposable
{
    private static readonly string[] DtrModes = { "Text only", "Icon + text", "Icon only" };
    private readonly IBotologyUi plugin;

    public ConfigWindow(Plugin plugin) : this((IBotologyUi)plugin) { }

    internal ConfigWindow(IBotologyUi plugin)
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
        UiGui.Title(PluginInfo.DisplayName+" Settings",PluginInfo.DisplayName+" "+UiText.T("Settings"));
        using var font=UiText.Font(UiFontRole.Body);
        using var controls=AethertekUI.MaterialControls.Push(BotologyPresentation.Controls(34));
        plugin.DrawAppearanceSelector();
        ImGui.Separator();
        var cfg = plugin.Configuration;

        var enabled = cfg.PluginEnabled;
        if (UiGui.Checkbox("Plugin enabled", ref enabled))
            plugin.SetPluginEnabled(enabled, printStatus: true);

        var dtr = cfg.DtrBarEnabled;
        if (UiGui.Checkbox("Show DTR bar entry", ref dtr))
        {
            cfg.DtrBarEnabled = dtr;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var mode = cfg.DtrBarMode;
        if (UiGui.Combo("DTR mode", ref mode, DtrModes, DtrModes.Length))
        {
            cfg.DtrBarMode = mode;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var onIcon = cfg.DtrIconEnabled;
        if (UiGui.InputText("DTR enabled glyph", ref onIcon, 8))
        {
            cfg.DtrIconEnabled = onIcon.Length <= 3 ? onIcon : onIcon[..3];
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var offIcon = cfg.DtrIconDisabled;
        if (UiGui.InputText("DTR disabled glyph", ref offIcon, 8))
        {
            cfg.DtrIconDisabled = offIcon.Length <= 3 ? offIcon : offIcon[..3];
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        if (UiGui.SmallButton("DTR manager"))
            plugin.OpenDtrManagerUi();
        ImGui.SameLine();
        if (UiGui.SmallButton("XLSettings Server Info Bar"))
            plugin.OpenServerInfoBarSettings();

        var toastNotifications = cfg.ToastNotifications;
        if (UiGui.Checkbox("Toast warnings on changes", ref toastNotifications))
        {
            cfg.ToastNotifications = toastNotifications;
            cfg.Save();
        }

        var masterToastNotifications = cfg.ToastOnMasterCatalogChange;
        if (UiGui.Checkbox("Toast for catalog notes affecting installed plugins.", ref masterToastNotifications))
        {
            cfg.ToastOnMasterCatalogChange = masterToastNotifications;
            cfg.Save();
        }

        var popupNotifications = cfg.BlockingPopupNotifications;
        if (UiGui.Checkbox("Popup box that requires OK", ref popupNotifications))
        {
            cfg.BlockingPopupNotifications = popupNotifications;
            cfg.Save();
        }

        var openWindow = cfg.OpenWindowOnAssessmentChange;
        if (UiGui.Checkbox("Open the plugin window on changes", ref openWindow))
        {
            cfg.OpenWindowOnAssessmentChange = openWindow;
            cfg.Save();
        }

        var openOnLoad = cfg.OpenMainWindowOnLoad;
        if (UiGui.Checkbox("Open main window on load", ref openOnLoad))
        {
            cfg.OpenMainWindowOnLoad = openOnLoad;
            cfg.Save();
        }

        var periodicChecks = cfg.EnablePeriodicMasterCatalogChecks;
        if (UiGui.Checkbox("Enable periodic master catalog checks", ref periodicChecks))
        {
            cfg.EnablePeriodicMasterCatalogChecks = periodicChecks;
            cfg.Save();
            plugin.RescheduleMasterCatalogCheck();
        }

        var intervalMinutes = Math.Max(1, cfg.MasterCatalogCheckIntervalMinutes);
        if (UiGui.InputInt("Master catalog check interval (minutes)", ref intervalMinutes))
        {
            cfg.MasterCatalogCheckIntervalMinutes = Math.Max(1, intervalMinutes);
            cfg.Save();
            plugin.RescheduleMasterCatalogCheck();
        }

        if (UiGui.SmallButton("Reload master now"))
            plugin.RefreshMasterCatalog(force: true, silent: false);
        ImGui.SameLine();
        if (UiGui.SmallButton("Open DATA editor"))
            plugin.OpenCatalogEditorUi();
        ImGui.SameLine();
        if (UiGui.SmallButton("Open DATA folder"))
            plugin.OpenCatalogFolder();

        ImGui.Separator();
        var refreshInfo = plugin.GetCatalogRefreshInfo();
        ImGui.TextWrapped(UiText.T("The grid uses live installed, enabled, update-available, and DTR detection from Dalamud. Direct plugin DTR config is used first; live DTR entries use the same Server Info Bar visibility data as XLSettings."));
        ImGui.TextWrapped(UiText.T("Ignore flags remove rows from alert calculations but keep them visible in the grid as blue rows."));
        ImGui.TextWrapped(UiText.T("Special thanks to Canto who cooked most of the initial dataset and proposed categorizations. "));
        //ImGui.TextWrapped($"Master source: {refreshInfo.SourceUrl ?? "Unknown"}");
        ImGui.TextWrapped(UiText.F("Last master check: {0}",UiText.Date(refreshInfo.LastCheckedUtc)));
        ImGui.TextWrapped(UiText.F("Last master update: {0}",UiText.Date(refreshInfo.LastUpdatedUtc)));

        FinalizePendingWindowPlacement();
    }
}

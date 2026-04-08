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

        ImGui.Separator();
        ImGui.TextWrapped("The grid uses live installed, enabled, update-available, and best-effort DTR detection from Dalamud. If a plugin hides its configuration or repo URL, Botology will leave that control blank instead of guessing.");
        ImGui.TextWrapped("Ignore flags remove rows from alert calculations but keep them visible in the grid.");

        FinalizePendingWindowPlacement();
    }
}

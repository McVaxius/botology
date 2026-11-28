using System;
using System.Linq;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using botology.Windows;

namespace botology;

public sealed partial class Plugin
{
    private MaterialGlassRenderer? backgroundGlass;
    private readonly FileDialogManager glassImagePicker = new();

    private void DrawUi()
    {
        mainWindow.ReleaseClosedMedia();
        ApplyAppearance();
        if (!WindowSystem.Windows.Any(window => window.IsOpen))
        {
            ReleaseWindowResources();
            return;
        }

        uiTextHost ??= new(TextureProvider);
        if (uiFonts is null)
        {
            var atlas = PluginInterface.UiBuilder.CreateFontAtlas(FontAtlasAutoRebuildMode.OnNewFrame, true, "Botology UI");
            uiFonts = new(atlas, uiText.GlyphRanges(), appliedLanguage, PluginInterface.AssemblyLocation.DirectoryName!);
        }

        using (MaterialTheme.Push(uiTheme, ImGuiHelpers.GlobalScale, Configuration.UiStylingEnabled ? MaterialStyleMode.ColorsOnly : MaterialStyleMode.Native))
            glassImagePicker.Draw();
        var choices = Configuration.UiColors ?? new();
        var blur = Configuration.UiStylingEnabled && choices.HasBackgroundBlur(choices.Mode);
        var image = Configuration.UiStylingEnabled && choices.Mode == MaterialColorMode.Glassmorphism &&
            choices.GlassBackground == MaterialGlassBackground.Image;
        if ((blur || image) && uiFonts.Ready)
        {
            backgroundGlass ??= new(PluginInterface, Log);
            backgroundGlass.BeginFrame(blur, image);
            backgroundGlass.ConfigureImage(TextureProvider, image, choices.GlassImagePath);
        }
        else
        {
            DisposeBackgroundGlass();
        }

        var completed = false;
        try
        {
            using var background = MaterialSkin.PushBackgroundRenderer(backgroundGlass, blur, choices.BackgroundBlurPercent / 100f);
            DrawUiContent();
            completed = true;
        }
        finally { backgroundGlass?.EndFrame(completed); }
    }

    private void ReleaseGlassResources() => backgroundGlass?.ReleaseResources();

    private void DisposeBackgroundGlass()
    {
        backgroundGlass?.Dispose();
        backgroundGlass = null;
    }

    private void ReleaseWindowResources()
    {
        if (uiFonts is null && uiTextHost is null) return;
        glassImagePicker.Reset();
        DisposeBackgroundGlass();
        uiFonts?.Dispose();
        uiFonts = null!;
        uiTextHost?.Dispose();
        uiTextHost = null;
        checkedFontGeneration = -1;
        fontIssueLogged = false;
    }

    private void ToggleUiStyling() { Configuration.UiStylingEnabled = !Configuration.UiStylingEnabled; Configuration.Save(); }
    private void ToggleWindowLock() { Configuration.UiWindowsLocked = !Configuration.UiWindowsLocked; Configuration.Save(); }

    private void PrepareWindowBackground(string name)
    {
        var owner = ImGuiP.FindWindowByName(name);
        if (backgroundGlass != null) backgroundGlass.Opacity = Configuration.UiTransparencyEnabled
            ? owner.IsNull || owner.Appearing ? Configuration.UiWindowOpacityPercent / 100f
                : windowOpacities.TryGetValue(name, out var opacity) ? opacity.CurrentOpacity : Configuration.UiWindowOpacityPercent / 100f
            : 1;
    }

    private void DrawUiWindowControls()
    {
        var styled = Configuration.UiStylingEnabled;
        if (Windows.UiGui.Checkbox("UI styling", ref styled)) { Configuration.UiStylingEnabled = styled; Configuration.Save(); }
        var locked = Configuration.UiWindowsLocked;
        if (Windows.UiGui.Checkbox("Lock window movement and resizing", ref locked)) { Configuration.UiWindowsLocked = locked; Configuration.Save(); }
    }

    private void BrowseBackgroundImage() => glassImagePicker.OpenFileDialog(
        UiText.T("Choose background image") + "###BotologyBackgroundImagePicker",
        UiText.T("Images") + "{.png,.jpg,.jpeg,.bmp,.gif,.webp,.tif,.tiff,.dds,.tga},.*",
        (accepted, path) =>
        {
            if (!accepted) return;
            colorDraft = colorDraft with { GlassImagePath = path };
            Configuration.UiColors = colorDraft;
            Configuration.Save();
        });

    private void DrawBackgroundSettings()
    {
        var choices = Configuration.UiColors ?? new();
        var blur = choices.HasBackgroundBlur(choices.Mode);
        var changed = Windows.UiGui.Checkbox("Background blur", ref blur);
        if (changed) choices = choices.WithBackgroundBlur(choices.Mode, blur);
        var frost = choices.BackgroundBlurPercent;
        MaterialText.Text(UiText.T("Blur (%)"));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(160 * MaterialTheme.Metrics.Scale, 80 * MaterialTheme.Metrics.Scale));
        using (MaterialSkin.NativeFieldStyle())
            if (ImGui.InputInt("##BackgroundFrost", ref frost)) { choices = choices with { BackgroundBlurPercent = frost }; changed = true; }
        if (changed) { Configuration.UiColors = colorDraft = choices; Configuration.Save(); }
        DrawGlassStatusAndScreenshots();
    }

    private void DrawGlassStatusAndScreenshots()
    {
        if (colorDraft.Mode == MaterialColorMode.Glassmorphism && colorDraft.GlassBackground == MaterialGlassBackground.Image)
        {
            MaterialText.TextWrapped(UiText.T(backgroundGlass?.ImageProblem ?? (backgroundGlass?.ImageAvailable == true
                ? "Image backgrounds remain visible to screenshots." : string.IsNullOrWhiteSpace(colorDraft.GlassImagePath) ? "Choose an image file." : "Loading image...")));
            return;
        }
        var paused = backgroundGlass?.ScreenshotsPaused == true;
        ImGui.BeginDisabled(!paused && backgroundGlass?.CanPauseForScreenshots != true);
        if (Windows.UiGui.Checkbox("Pause backdrop for screenshots", ref paused)) backgroundGlass?.TrySetScreenshotsPaused(paused);
        ImGui.EndDisabled();
    }
}

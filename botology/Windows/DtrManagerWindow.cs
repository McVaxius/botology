using AethertekUI;
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using botology.Models;

namespace botology.Windows;

public sealed class DtrManagerWindow : PositionedWindow, IDisposable
{
    private readonly IBotologyUi plugin;

    public DtrManagerWindow(Plugin plugin) : this((IBotologyUi)plugin) { }

    internal DtrManagerWindow(IBotologyUi plugin)
        : base($"{PluginInfo.DisplayName} DTR Manager##DtrManager")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620f, 420f),
            MaximumSize = new Vector2(1200f, 1100f),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        windowMotion.DrawChrome();
        UiGui.Title(PluginInfo.DisplayName+" DTR Manager",PluginInfo.DisplayName+" "+UiText.T("DTR manager"));
        using var font=UiText.Font(UiFontRole.Body);
        using var controls=AethertekUI.MaterialControls.Push(BotologyPresentation.Controls(34));
        if (UiGui.SmallButton("XLSettings Server Info Bar"))
            plugin.OpenServerInfoBarSettings();

        var entries = plugin.CaptureDtrEntries();
        MaterialText.Text(UiText.F("{0} live DTR entries",entries.Count));

        if (entries.Count == 0)
        {
            MaterialText.Text(UiText.T("No live DTR entries."));
            FinalizePendingWindowPlacement();
            return;
        }

        var tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

        if (ImGui.BeginTable("BotologyDtrEntries", 5, tableFlags, new Vector2(-1f, -1f)))
        {
            var scale=AethertekUI.MaterialTheme.Metrics.Scale;
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 42f*scale);
            ImGui.TableSetupColumn("DTR Entry", ImGuiTableColumnFlags.WidthStretch, 0f);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, Math.Max(115f*scale,new[]{"Visible","Hidden","Plugin hidden"}.Max(l=>MaterialText.Measure(UiText.T(l)).X)));
            ImGui.TableSetupColumn("Show", ImGuiTableColumnFlags.WidthFixed, Math.Max(70f*scale,MaterialText.Measure(UiText.T("Show")).X));
            ImGui.TableSetupColumn("Move", ImGuiTableColumnFlags.WidthFixed, Math.Max(110f*scale,MaterialText.Measure(UiText.T("Up")).X+MaterialText.Measure(UiText.T("Down")).X+4*ImGui.GetStyle().FramePadding.X+ImGui.GetStyle().ItemSpacing.X));
            UiGui.TableHeadersRow();

            foreach (var entry in entries)
            {
                ImGui.TableNextRow();
                ImGui.PushID(entry.Title);

                ImGui.TableSetColumnIndex(0);
                MaterialText.Text((entry.Order + 1).ToString(UiText.Current.Culture));

                ImGui.TableSetColumnIndex(1);
                MaterialText.Text(entry.Title);
                if (!string.IsNullOrWhiteSpace(entry.Text) && ImGui.IsItemHovered())
                    MaterialText.SetTooltip(entry.Text);

                ImGui.TableSetColumnIndex(2);
                DrawState(entry);

                ImGui.TableSetColumnIndex(3);
                var userVisible = entry.UserVisible;
                if (UiGui.Checkbox("##DtrUserVisible", ref userVisible))
                    plugin.SetGlobalDtrEntryVisible(entry.Title, userVisible);
                if (ImGui.IsItemHovered())
                    MaterialText.SetTooltip(UiText.T(entry.PluginShown
                        ? "Toggles Dalamud Server Info Bar visibility."
                        : "Plugin currently sets Shown=false; showing here only clears Dalamud hidden state."));

                ImGui.TableSetColumnIndex(4);
                ImGui.BeginDisabled(entry.Order == 0);
                if (UiGui.SmallButton("Up"))
                    plugin.MoveGlobalDtrEntry(entry.Title, -1);
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(entry.Order == entries.Count - 1);
                if (UiGui.SmallButton("Down"))
                    plugin.MoveGlobalDtrEntry(entry.Title, 1);
                ImGui.EndDisabled();

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        FinalizePendingWindowPlacement();
    }

    private static void DrawState(DtrEntrySnapshot entry)
    {
        var color = entry.EffectiveVisible
            ? new Vector4(0.45f, 0.95f, 0.45f, 1f)
            : entry.UserHidden
                ? new Vector4(1f, 0.75f, 0.35f, 1f)
                : new Vector4(0.58f, 0.58f, 0.58f, 1f);
        MaterialText.TextColored(color, UiText.T(entry.StateLabel));
    }
}

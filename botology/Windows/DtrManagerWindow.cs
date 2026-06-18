using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using botology.Models;

namespace botology.Windows;

public sealed class DtrManagerWindow : PositionedWindow, IDisposable
{
    private readonly Plugin plugin;

    public DtrManagerWindow(Plugin plugin)
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
        if (ImGui.SmallButton("XLSettings Server Info Bar"))
            plugin.OpenServerInfoBarSettings();

        var entries = plugin.CaptureDtrEntries();
        ImGui.TextUnformatted($"{entries.Count} live DTR entries");

        if (entries.Count == 0)
        {
            ImGui.TextUnformatted("No live DTR entries.");
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
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 42f);
            ImGui.TableSetupColumn("DTR Entry", ImGuiTableColumnFlags.WidthStretch, 0f);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 115f);
            ImGui.TableSetupColumn("Show", ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn("Move", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableHeadersRow();

            foreach (var entry in entries)
            {
                ImGui.TableNextRow();
                ImGui.PushID(entry.Title);

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted((entry.Order + 1).ToString());

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(entry.Title);
                if (!string.IsNullOrWhiteSpace(entry.Text) && ImGui.IsItemHovered())
                    ImGui.SetTooltip(entry.Text);

                ImGui.TableSetColumnIndex(2);
                DrawState(entry);

                ImGui.TableSetColumnIndex(3);
                var userVisible = entry.UserVisible;
                if (ImGui.Checkbox("##DtrUserVisible", ref userVisible))
                    plugin.SetGlobalDtrEntryVisible(entry.Title, userVisible);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(entry.PluginShown
                        ? "Toggles Dalamud Server Info Bar visibility."
                        : "Plugin currently sets Shown=false; showing here only clears Dalamud hidden state.");

                ImGui.TableSetColumnIndex(4);
                ImGui.BeginDisabled(entry.Order == 0);
                if (ImGui.SmallButton("Up"))
                    plugin.MoveGlobalDtrEntry(entry.Title, -1);
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(entry.Order == entries.Count - 1);
                if (ImGui.SmallButton("Down"))
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
        ImGui.TextColored(color, entry.StateLabel);
    }
}

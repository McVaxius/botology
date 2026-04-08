using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using botology.Models;
using botology.Services;

namespace botology.Windows;

public sealed class MainWindow : PositionedWindow, IDisposable
{
    private const string BlockingPopupId = "BotologyBlockingAlert";

    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base($"{PluginInfo.DisplayName}##Main")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(1180f, 620f),
            MaximumSize = new Vector2(1800f, 1400f),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var rows = plugin.CaptureRows();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        var assessedRows = rows.Where(row => row.IsAssessable && !row.Ignored).ToList();
        var greenCount = assessedRows.Count(row => row.Assessment.Severity == AssessmentSeverity.Green);
        var yellowCount = assessedRows.Count(row => row.Assessment.Severity == AssessmentSeverity.Yellow);
        var redCount = assessedRows.Count(row => row.Assessment.Severity == AssessmentSeverity.Red);

        ImGui.Text($"{PluginInfo.DisplayName} v{version}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Ko-fi"))
            plugin.OpenUrl(PluginInfo.SupportUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("Discord"))
            plugin.OpenUrl(PluginInfo.DiscordUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.OpenConfigUi();
        ImGui.SameLine();
        if (ImGui.SmallButton("Status to chat"))
            plugin.PrintStatus(BotologyCatalog.BuildAlertSummary(rows));
        ImGui.SameLine();
        if (ImGui.SmallButton("XLSETTINGS"))
            plugin.RunTextCommand("/xlsettings");
        ImGui.SameLine();
        if (ImGui.SmallButton("XLPLUGINS"))
            plugin.RunTextCommand("/xlplugins");
        ImGui.SameLine();
        if (ImGui.SmallButton("XLLOG"))
            plugin.RunTextCommand("/xllog");
        ImGui.SameLine();
        if (ImGui.SmallButton("JSON reload"))
            plugin.ReloadRepositoryLinks();

        ImGui.Separator();
        ImGui.TextWrapped(PluginInfo.Description);
        ImGui.TextWrapped(PluginInfo.DiscordFeedbackNote);
        ImGui.Text($"Green: {greenCount}  Yellow: {yellowCount}  Red: {redCount}");

        var enabled = plugin.Configuration.PluginEnabled;
        if (ImGui.Checkbox("Manager enabled", ref enabled))
            plugin.SetPluginEnabled(enabled, printStatus: true);

        ImGui.SameLine();
        var hideUninstalled = plugin.Configuration.HideUninstalledPlugins;
        if (ImGui.Checkbox("Hide uninstalled Plugins", ref hideUninstalled))
            plugin.SetHideUninstalledPlugins(hideUninstalled);

        ImGui.SameLine();
        ImGui.TextUnformatted("Use the (?) button in the last column for rule details.");

        var visibleRows = plugin.Configuration.HideUninstalledPlugins
            ? rows.Where(row => row.IsInstalled).ToList()
            : rows;

        var tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

        if (ImGui.BeginTable("BotologyGrid", 8, tableFlags, new Vector2(-1f, -1f)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Category / Plugin", ImGuiTableColumnFlags.WidthFixed, 220f);
            ImGui.TableSetupColumn("Installed?", ImGuiTableColumnFlags.WidthFixed, 75f);
            ImGui.TableSetupColumn("Update?", ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn("Repo", ImGuiTableColumnFlags.WidthFixed, 120f);
            ImGui.TableSetupColumn("Enabled?", ImGuiTableColumnFlags.WidthFixed, 130f);
            ImGui.TableSetupColumn("DTR", ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableSetupColumn("Notes / Warnings", ImGuiTableColumnFlags.WidthStretch, 0f);
            ImGui.TableSetupColumn("Ignore / ?", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableHeadersRow();

            string? currentCategory = null;
            foreach (var row in visibleRows)
            {
                if (!string.Equals(currentCategory, row.Entry.Category, System.StringComparison.Ordinal))
                {
                    currentCategory = row.Entry.Category;
                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(new Vector4(0.28f, 0.23f, 0.08f, 0.95f)));
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.55f, 1.0f), currentCategory.ToUpperInvariant());
                }

                DrawPluginRow(row);
            }

            ImGui.EndTable();
        }

        DrawBlockingAlertPopup();
        FinalizePendingWindowPlacement();
    }

    private void DrawPluginRow(PluginAssessmentRow row)
    {
        ImGui.TableNextRow();
        ImGui.PushID(row.Entry.Id);

        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(row.Entry.DisplayName);

        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(row.IsInstalled ? "Yes" : "No");

        ImGui.TableSetColumnIndex(2);
        if (row.RuntimeState?.HasUpdate == true)
            ImGui.TextColored(new Vector4(0.45f, 0.95f, 0.45f, 1f), "Yes");
        else
            ImGui.TextUnformatted("No");

        ImGui.TableSetColumnIndex(3);
        var repoUrl = RepositoryLinkResolver.ResolveRepoUrl(row);
        var repoJsonUrl = RepositoryLinkResolver.ResolveRepoJsonUrl(row);
        var hasConcreteRepoJsonUrl = RepositoryLinkResolver.HasConcreteRepoJsonUrl(row);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(repoUrl));
        if (ImGui.SmallButton("Repo") && !string.IsNullOrWhiteSpace(repoUrl))
            plugin.OpenUrl(repoUrl);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.SmallButton("[Copy]"))
        {
            ImGui.SetClipboardText(repoJsonUrl);
            plugin.PrintStatus(hasConcreteRepoJsonUrl
                ? $"Copied repo.json URL for {row.Entry.DisplayName}."
                : $"Copied placeholder repo.json URL for {row.Entry.DisplayName}; adjust owner, repo, or branch if needed.");
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(hasConcreteRepoJsonUrl ? repoJsonUrl : $"Placeholder repo.json URL:\n{repoJsonUrl}");

        ImGui.TableSetColumnIndex(4);
        if (row.RuntimeState == null)
        {
            ImGui.TextUnformatted("--");
        }
        else
        {
            var enabledColor = row.RuntimeState.IsLoaded
                ? new Vector4(0.45f, 0.95f, 0.45f, 1f)
                : new Vector4(1f, 0.75f, 0.35f, 1f);
            ImGui.TextColored(enabledColor, row.RuntimeState.IsLoaded ? "[YES]" : "[NO]");
            ImGui.SameLine();
            var buttonLabel = row.RuntimeState.IsLoaded ? "Disable" : "Enable";
            if (ImGui.SmallButton(buttonLabel))
                plugin.ToggleTrackedPlugin(row.RuntimeState);
        }

        ImGui.TableSetColumnIndex(5);
        if (row.RuntimeState?.CanToggleDtr == true && row.RuntimeState.DtrBarEnabled.HasValue)
        {
            var dtrEnabled = row.RuntimeState.DtrBarEnabled.Value;
            if (ImGui.Checkbox("##DtrEnabled", ref dtrEnabled))
                plugin.ToggleTrackedPluginDtr(row.RuntimeState, dtrEnabled);
        }
        else
        {
            ImGui.TextUnformatted("--");
        }

        ImGui.TableSetColumnIndex(6);
        if (!row.IsAssessable)
        {
            ImGui.TextUnformatted("--");
        }
        else
        {
            var color = row.Ignored
                ? new Vector4(1f, 1f, 1f, 1f)
                : row.Assessment.Severity switch
                {
                    AssessmentSeverity.Red => new Vector4(1f, 0.35f, 0.35f, 1f),
                    AssessmentSeverity.Yellow => new Vector4(1f, 0.86f, 0.35f, 1f),
                    _ => new Vector4(0.45f, 0.95f, 0.45f, 1f),
                };
            var prefix = row.Ignored ? "[Ignored] " : string.Empty;
            ImGui.TextColored(color, $"{prefix}{row.Assessment.Severity}: {row.Assessment.Summary}");
        }

        ImGui.TableSetColumnIndex(7);
        var ignored = row.Ignored;
        if (ImGui.Checkbox("##Ignore", ref ignored))
            plugin.SetIgnored(row.Entry.Id, ignored);
        ImGui.SameLine();
        if (ImGui.SmallButton("?"))
            ImGui.OpenPopup("RuleInfo");

        ImGui.SetNextWindowSize(new Vector2(520f, 0f), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("RuleInfo"))
        {
            ImGui.PushTextWrapPos(480f);
            ImGui.TextWrapped($"{row.Assessment.Severity}: {row.Assessment.Summary}");
            ImGui.Separator();
            ImGui.TextWrapped(row.Assessment.Details);
            ImGui.PopTextWrapPos();
            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    private void DrawBlockingAlertPopup()
    {
        if (plugin.TryGetBlockingAlert(out var message, out var shouldOpenPopup) && shouldOpenPopup)
            ImGui.OpenPopup(BlockingPopupId);

        if (!ImGui.BeginPopupModal(BlockingPopupId, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextWrapped(message);
        if (ImGui.Button("OK"))
        {
            plugin.AcknowledgeBlockingAlert();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}

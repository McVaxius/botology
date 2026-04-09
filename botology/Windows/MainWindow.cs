using System;
using System.Globalization;
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
    private const string JsonGuidePopupId = "BotologyJsonGuide";
    private const string ColumnSelectionPopupId = "BotologyColumnSelection";

    private readonly Plugin plugin;
    private string categoryFilterText = string.Empty;
    private string pluginNameFilterText = string.Empty;
    private string authorFilterText = string.Empty;

    private enum GridColumn
    {
        Category,
        Installed,
        Update,
        Repo,
        Enabled,
        Dtr,
        Downloads,
        LastUpdated,
        DalamudApiLevel,
        Author,
        Notes,
        Ignore,
    }

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
        var visibleColumns = GetVisibleColumns();

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
        ImGui.SameLine();
        if (ImGui.SmallButton("JSON GUIDE"))
            ImGui.OpenPopup(JsonGuidePopupId);
        ImGui.SameLine();
        if (ImGui.SmallButton("JSON FOLDER"))
            plugin.OpenJsonFolder();

        ImGui.Separator();
        ImGui.TextWrapped(PluginInfo.Description);
        ImGui.TextWrapped(PluginInfo.DiscordFeedbackNote);
        ImGui.Text($"Green: {greenCount}  Yellow: {yellowCount}  Red: {redCount}");

        var enabled = plugin.Configuration.PluginEnabled;
        if (ImGui.Checkbox("Manager enabled", ref enabled))
            plugin.SetPluginEnabled(enabled, printStatus: true);

        ImGui.SameLine();
        if (ImGui.SmallButton("COLUMN SELECTION"))
            ImGui.OpenPopup(ColumnSelectionPopupId);

        ImGui.SameLine();
        var hideUninstalled = plugin.Configuration.HideUninstalledPlugins;
        if (ImGui.Checkbox("Hide uninstalled Plugins", ref hideUninstalled))
            plugin.SetHideUninstalledPlugins(hideUninstalled);

        ImGui.SetNextItemWidth(190f);
        ImGui.InputTextWithHint("##CategoryFilter", "CATEGORY", ref categoryFilterText, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##PluginNameFilter", "PLUGIN NAME", ref pluginNameFilterText, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##AuthorFilter", "AUTHOR", ref authorFilterText, 128);

        ImGui.TextUnformatted("Use the (?) button in Category / Plugin for plugin descriptions.");
        ImGui.TextUnformatted("Use the (?) button in Ignore / ? for warning-rule details.");

        var visibleRows = plugin.Configuration.HideUninstalledPlugins
            ? rows.Where(row => row.IsInstalled).ToList()
            : rows;
        visibleRows = visibleRows
            .Where(MatchesFilters)
            .ToList();

        if (visibleRows.Count == 0)
        {
            ImGui.Separator();
            ImGui.TextWrapped("No plugins match the current category/plugin/author filters.");
            DrawColumnSelectionPopup();
            DrawJsonGuidePopup();
            DrawBlockingAlertPopup();
            FinalizePendingWindowPlacement();
            return;
        }

        var tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

        if (ImGui.BeginTable("BotologyGrid", visibleColumns.Length, tableFlags, new Vector2(-1f, -1f)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            foreach (var column in visibleColumns)
                SetupColumn(column);
            ImGui.TableHeadersRow();

            string? currentCategory = null;
            foreach (var row in visibleRows)
            {
                if (!string.Equals(currentCategory, row.Entry.Category, StringComparison.Ordinal))
                {
                    currentCategory = row.Entry.Category;
                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(new Vector4(0.28f, 0.23f, 0.08f, 0.95f)));
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.55f, 1.0f), currentCategory.ToUpperInvariant());
                }

                DrawPluginRow(row, visibleColumns);
            }

            ImGui.EndTable();
        }

        DrawColumnSelectionPopup();
        DrawJsonGuidePopup();
        DrawBlockingAlertPopup();
        FinalizePendingWindowPlacement();
    }

    private GridColumn[] GetVisibleColumns()
    {
        var cfg = plugin.Configuration;
        var columns = new System.Collections.Generic.List<GridColumn>
        {
            GridColumn.Category,
        };

        if (cfg.ShowInstalledColumn)
            columns.Add(GridColumn.Installed);
        if (cfg.ShowUpdateColumn)
            columns.Add(GridColumn.Update);
        if (cfg.ShowRepoColumn)
            columns.Add(GridColumn.Repo);

        columns.Add(GridColumn.Enabled);

        if (cfg.ShowDtrColumn)
            columns.Add(GridColumn.Dtr);
        if (cfg.ShowDownloadsColumn)
            columns.Add(GridColumn.Downloads);
        if (cfg.ShowLastUpdateColumn)
            columns.Add(GridColumn.LastUpdated);
        if (cfg.ShowDalamudApiLevelColumn)
            columns.Add(GridColumn.DalamudApiLevel);
        if (cfg.ShowAuthorColumn)
            columns.Add(GridColumn.Author);
        if (cfg.ShowNotesColumn)
            columns.Add(GridColumn.Notes);
        if (cfg.ShowIgnoreColumn)
            columns.Add(GridColumn.Ignore);

        return columns.ToArray();
    }

    private static void SetupColumn(GridColumn column)
    {
        switch (column)
        {
            case GridColumn.Category:
                ImGui.TableSetupColumn("Category / Plugin", ImGuiTableColumnFlags.WidthFixed, 220f);
                break;
            case GridColumn.Installed:
                ImGui.TableSetupColumn("Installed?", ImGuiTableColumnFlags.WidthFixed, 75f);
                break;
            case GridColumn.Update:
                ImGui.TableSetupColumn("Update?", ImGuiTableColumnFlags.WidthFixed, 70f);
                break;
            case GridColumn.Repo:
                ImGui.TableSetupColumn("Repo", ImGuiTableColumnFlags.WidthFixed, 120f);
                break;
            case GridColumn.Enabled:
                ImGui.TableSetupColumn("Enabled?", ImGuiTableColumnFlags.WidthFixed, 130f);
                break;
            case GridColumn.Dtr:
                ImGui.TableSetupColumn("DTR", ImGuiTableColumnFlags.WidthFixed, 80f);
                break;
            case GridColumn.Downloads:
                ImGui.TableSetupColumn("Downloads", ImGuiTableColumnFlags.WidthFixed, 110f);
                break;
            case GridColumn.LastUpdated:
                ImGui.TableSetupColumn("Last Update Date", ImGuiTableColumnFlags.WidthFixed, 120f);
                break;
            case GridColumn.DalamudApiLevel:
                ImGui.TableSetupColumn("DalamudApiLevel", ImGuiTableColumnFlags.WidthFixed, 120f);
                break;
            case GridColumn.Author:
                ImGui.TableSetupColumn("Author", ImGuiTableColumnFlags.WidthFixed, 150f);
                break;
            case GridColumn.Notes:
                ImGui.TableSetupColumn("Notes / Warnings", ImGuiTableColumnFlags.WidthStretch, 0f);
                break;
            case GridColumn.Ignore:
                ImGui.TableSetupColumn("Ignore / ?", ImGuiTableColumnFlags.WidthFixed, 110f);
                break;
        }
    }

    private void DrawPluginRow(PluginAssessmentRow row, GridColumn[] columns)
    {
        ImGui.TableNextRow();
        ImGui.PushID(row.Entry.Id);

        for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            ImGui.TableSetColumnIndex(columnIndex);
            switch (columns[columnIndex])
            {
                case GridColumn.Category:
                    DrawCategoryColumn(row);
                    break;
                case GridColumn.Installed:
                    ImGui.TextUnformatted(row.IsInstalled ? "Yes" : "No");
                    break;
                case GridColumn.Update:
                    if (row.RuntimeState?.HasUpdate == true)
                        ImGui.TextColored(new Vector4(0.45f, 0.95f, 0.45f, 1f), "Yes");
                    else
                        ImGui.TextUnformatted("No");
                    break;
                case GridColumn.Repo:
                    DrawRepoColumn(row);
                    break;
                case GridColumn.Enabled:
                    DrawEnabledColumn(row);
                    break;
                case GridColumn.Dtr:
                    DrawDtrColumn(row);
                    break;
                case GridColumn.Downloads:
                    DrawDownloadsColumn(row);
                    break;
                case GridColumn.LastUpdated:
                    DrawLastUpdatedColumn(row);
                    break;
                case GridColumn.DalamudApiLevel:
                    DrawDalamudApiLevelColumn(row);
                    break;
                case GridColumn.Author:
                    DrawAuthorColumn(row);
                    break;
                case GridColumn.Notes:
                    DrawNotesColumn(row);
                    break;
                case GridColumn.Ignore:
                    DrawIgnoreColumn(row);
                    break;
            }
        }

        ImGui.PopID();
    }

    private static void DrawCategoryColumn(PluginAssessmentRow row)
    {
        var color = GetAssessmentColor(row);
        if (row.IsAssessable)
            ImGui.TextColored(color, row.Entry.DisplayName);
        else
            ImGui.TextUnformatted(row.Entry.DisplayName);
        ImGui.SameLine();
        if (ImGui.SmallButton("(?)"))
            ImGui.OpenPopup("PluginDescription");

        ImGui.SetNextWindowSize(new Vector2(540f, 0f), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("PluginDescription"))
        {
            ImGui.PushTextWrapPos(500f);
            ImGui.TextWrapped(GetPluginDescription(row));
            ImGui.PopTextWrapPos();
            ImGui.EndPopup();
        }
    }

    private void DrawRepoColumn(PluginAssessmentRow row)
    {
        var repoUrl = RepositoryLinkResolver.ResolveRepoUrl(row);
        var repoJsonUrls = RepositoryLinkResolver.ResolveRepoJsonUrls(row).ToArray();
        var repoJsonUrl = repoJsonUrls.FirstOrDefault() ?? RepositoryLinkResolver.ResolveRepoJsonUrl(row);
        var hasConcreteRepoJsonUrl = RepositoryLinkResolver.HasConcreteRepoJsonUrl(row);
        var copyText = repoJsonUrls.Length > 1
            ? string.Join(Environment.NewLine, repoJsonUrls)
            : repoJsonUrl;
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(repoUrl));
        if (ImGui.SmallButton("Repo") && !string.IsNullOrWhiteSpace(repoUrl))
            plugin.OpenUrl(repoUrl);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.SmallButton("[Copy]"))
        {
            ImGui.SetClipboardText(copyText);
            plugin.PrintStatus(hasConcreteRepoJsonUrl
                ? repoJsonUrls.Length > 1
                    ? $"Copied {repoJsonUrls.Length} repo feed URLs for {row.Entry.DisplayName}."
                    : $"Copied repo.json URL for {row.Entry.DisplayName}."
                : $"Copied placeholder repo.json URL for {row.Entry.DisplayName}; adjust owner, repo, or branch if needed.");
        }

        if (ImGui.IsItemHovered())
        {
            var tooltipText = hasConcreteRepoJsonUrl
                ? repoJsonUrls.Length > 1
                    ? $"Repo feeds:\n{copyText}"
                    : repoJsonUrl
                : $"Placeholder repo.json URL:\n{repoJsonUrl}";
            ImGui.SetTooltip(tooltipText);
        }
    }

    private void DrawEnabledColumn(PluginAssessmentRow row)
    {
        if (row.RuntimeState == null)
        {
            ImGui.TextUnformatted("--");
            return;
        }

        var enabledColor = row.RuntimeState.IsLoaded
            ? new Vector4(0.45f, 0.95f, 0.45f, 1f)
            : new Vector4(1f, 0.75f, 0.35f, 1f);
        ImGui.TextColored(enabledColor, row.RuntimeState.IsLoaded ? "[YES]" : "[NO]");
        ImGui.SameLine();
        var buttonLabel = row.RuntimeState.IsLoaded ? "Disable" : "Enable";
        if (ImGui.SmallButton(buttonLabel))
            plugin.ToggleTrackedPlugin(row.RuntimeState);
    }

    private void DrawDtrColumn(PluginAssessmentRow row)
    {
        if (row.RuntimeState?.CanToggleDtr == true && row.RuntimeState.DtrBarEnabled.HasValue)
        {
            var dtrEnabled = row.RuntimeState.DtrBarEnabled.Value;
            if (ImGui.Checkbox("##DtrEnabled", ref dtrEnabled))
                plugin.ToggleTrackedPluginDtr(row.RuntimeState, dtrEnabled);
        }
        else if (row.RuntimeState != null)
        {
            if (ImGui.SmallButton("XLSET"))
                plugin.RunTextCommand("/xlsettings");
        }
        else
        {
            ImGui.TextUnformatted("--");
        }
    }

    private static void DrawDownloadsColumn(PluginAssessmentRow row)
    {
        if (row.Metadata?.Downloads is long downloads)
            ImGui.TextUnformatted(downloads.ToString("N0", CultureInfo.CurrentCulture));
        else
            ImGui.TextUnformatted("--");
    }

    private static void DrawLastUpdatedColumn(PluginAssessmentRow row)
    {
        if (row.Metadata?.LastUpdateUtc is DateTimeOffset lastUpdateUtc)
            ImGui.TextUnformatted(lastUpdateUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
        else
            ImGui.TextUnformatted("--");
    }

    private static void DrawDalamudApiLevelColumn(PluginAssessmentRow row)
    {
        if (row.Metadata?.DalamudApiLevel is int apiLevel)
            ImGui.TextUnformatted(apiLevel.ToString(CultureInfo.CurrentCulture));
        else
            ImGui.TextUnformatted("--");
    }

    private static void DrawAuthorColumn(PluginAssessmentRow row)
    {
        var author = row.Metadata?.Author;
        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(author) ? "--" : author);
    }

    private static void DrawNotesColumn(PluginAssessmentRow row)
    {
        if (!row.IsAssessable)
        {
            ImGui.TextUnformatted("--");
            return;
        }

        var color = GetAssessmentColor(row);
        var prefix = row.Ignored ? "[Ignored] " : string.Empty;
        ImGui.TextColored(color, $"{prefix}{row.Assessment.Severity}: {row.Assessment.Summary}");
    }

    private static Vector4 GetAssessmentColor(PluginAssessmentRow row)
    {
        if (row.Ignored)
            return new Vector4(1.0f, 0.84f, 0.25f, 1f);

        return row.Assessment.Severity switch
        {
            AssessmentSeverity.Red => new Vector4(1f, 0.35f, 0.35f, 1f),
            AssessmentSeverity.Yellow => new Vector4(1f, 0.86f, 0.35f, 1f),
            _ => new Vector4(0.45f, 0.95f, 0.45f, 1f),
        };
    }

    private void DrawIgnoreColumn(PluginAssessmentRow row)
    {
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
    }

    private void DrawColumnSelectionPopup()
    {
        if (!ImGui.BeginPopup(ColumnSelectionPopupId))
            return;

        DrawColumnToggle("Installed?", plugin.Configuration.ShowInstalledColumn, value => plugin.Configuration.ShowInstalledColumn = value);
        DrawColumnToggle("Update?", plugin.Configuration.ShowUpdateColumn, value => plugin.Configuration.ShowUpdateColumn = value);
        DrawColumnToggle("Repo", plugin.Configuration.ShowRepoColumn, value => plugin.Configuration.ShowRepoColumn = value);
        DrawColumnToggle("DTR", plugin.Configuration.ShowDtrColumn, value => plugin.Configuration.ShowDtrColumn = value);
        DrawColumnToggle("Downloads", plugin.Configuration.ShowDownloadsColumn, value => plugin.Configuration.ShowDownloadsColumn = value);
        DrawColumnToggle("Last Update Date", plugin.Configuration.ShowLastUpdateColumn, value => plugin.Configuration.ShowLastUpdateColumn = value);
        DrawColumnToggle("DalamudApiLevel", plugin.Configuration.ShowDalamudApiLevelColumn, value => plugin.Configuration.ShowDalamudApiLevelColumn = value);
        DrawColumnToggle("Author", plugin.Configuration.ShowAuthorColumn, value => plugin.Configuration.ShowAuthorColumn = value);
        DrawColumnToggle("Notes / Warnings", plugin.Configuration.ShowNotesColumn, value => plugin.Configuration.ShowNotesColumn = value);
        DrawColumnToggle("Ignore / ?", plugin.Configuration.ShowIgnoreColumn, value => plugin.Configuration.ShowIgnoreColumn = value);

        ImGui.Separator();
        ImGui.TextWrapped("Category / Plugin and Enabled? stay visible so the grid remains operable.");
        ImGui.EndPopup();
    }

    private void DrawJsonGuidePopup()
    {
        ImGui.SetNextWindowSize(new Vector2(760f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup(JsonGuidePopupId))
            return;

        var manifestPath = RepositoryLinkCatalog.GetManifestPath() ?? "Unknown";

        ImGui.PushTextWrapPos(720f);
        ImGui.TextWrapped("plugin-repository-links.json is a human-edited manifest. Botology only reloads it when the plugin/settings window is opened or when you click JSON reload.");
        ImGui.Separator();
        ImGui.TextWrapped($"Enabled file: {manifestPath}");
        ImGui.TextWrapped("Raw GitHub path: https://raw.githubusercontent.com/McVaxius/botology/refs/heads/main/botology/plugin-repository-links.json");
        ImGui.Separator();
        ImGui.BulletText("id: stable key used for rule matching and relatedIds references.");
        ImGui.BulletText("category: visible group heading.");
        ImGui.BulletText("displayName: row label in the manager.");
        ImGui.BulletText("matchTokens: names/internal names Botology uses to match installed plugins.");
        ImGui.BulletText("notes: long warning/help text shown in the rule popup.");
        ImGui.BulletText("description: plugin summary for the (?) button in Category / Plugin. If missing, Botology tries repo.json and otherwise falls back to placeholder description.");
        ImGui.BulletText("repoUrl: source repository used by the Repo button and GitHub metadata fallback.");
        ImGui.BulletText("repoJsonUrl: concrete feed URL used for repo.json scraping. If this is missing and repoUrl points at GitHub, Botology will try the standard raw repo.json path.");
        ImGui.BulletText("repoJsonUrls: optional array of multiple concrete feed URLs. Botology will scrape all of them, merge the best matching manifest metadata, and [Copy] will copy every listed feed.");
        ImGui.BulletText("ruleType: optional built-in rule selector.");
        ImGui.BulletText("relatedIds: optional array of one or more plugin ids used by paired/direct rules, for example [\"bossmod_reborn\"] or [\"gatherbuddy\", \"gatherbuddy_reborn\"].");
        ImGui.Separator();
        ImGui.TextWrapped("Available ruleType values:");
        ImGui.BulletText("autoduty_conflict: row goes red when AutoDuty is enabled.");
        ImGui.BulletText("paired_conflict_yellow: row goes yellow when any relatedIds entry is enabled.");
        ImGui.BulletText("paired_conflict_red: row goes red when any relatedIds entry is enabled.");
        ImGui.BulletText("direct_conflict_red: row goes red when any relatedIds entry is enabled.");
        ImGui.BulletText("rotation_conflict: row goes yellow when another tracked rotation/bossmod entry is enabled.");
        ImGui.BulletText("bossmod_pair: row goes red for direct bossmod rivals, otherwise falls back to rotation_conflict.");
        ImGui.BulletText("loaded_warning_yellow: row goes yellow when the plugin itself is enabled.");
        ImGui.Separator();
        ImGui.TextWrapped("Downloads, Last Update Date, DalamudApiLevel, Author, and description fallback are auto-scraped and cached for 24 hours. Those columns are not configured in the JSON file.");
        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
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

    private void DrawColumnToggle(string label, bool currentValue, Action<bool> apply)
    {
        var value = currentValue;
        if (ImGui.Checkbox(label, ref value))
        {
            apply(value);
            plugin.Configuration.Save();
        }
    }

    private static string GetPluginDescription(PluginAssessmentRow row)
    {
        var configuredDescription = row.Entry.Description?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredDescription) &&
            !configuredDescription.Equals("placeholder description", StringComparison.OrdinalIgnoreCase))
            return configuredDescription;

        var scrapedDescription = row.Metadata?.Description?.Trim();
        if (!string.IsNullOrWhiteSpace(scrapedDescription))
            return scrapedDescription;

        return !string.IsNullOrWhiteSpace(configuredDescription)
            ? configuredDescription
            : "placeholder description";
    }

    private bool MatchesFilters(PluginAssessmentRow row)
    {
        if (!string.IsNullOrWhiteSpace(categoryFilterText) &&
            !ContainsInvariant(row.Entry.Category, categoryFilterText))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(authorFilterText) &&
            !ContainsInvariant(row.Metadata?.Author, authorFilterText))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(pluginNameFilterText))
            return true;

        if (ContainsInvariant(row.Entry.DisplayName, pluginNameFilterText))
            return true;

        return row.Entry.MatchTokens.Any(token => ContainsInvariant(token, pluginNameFilterText));
    }

    private static bool ContainsInvariant(string? haystack, string needle)
        => !string.IsNullOrWhiteSpace(haystack) &&
           haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
}

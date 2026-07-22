using System;
using System.Collections.Generic;
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
    private const string ColumnSelectionPopupId = "BotologyColumnSelection";
    private const string PatchNotesPopupId = "BotologyPatchNotes";
    private const string AiAttributionTooltip = "Likely AI-written code based on Aetherfeed contributor and coding-pattern attribution; snapshot 2026-07-22.";

    private readonly Plugin plugin;
    private string categoryFilterText = string.Empty;
    private string pluginNameFilterText = string.Empty;
    private string authorFilterText = string.Empty;

    private enum GridColumn
    {
        Category,
        Source,
        AiAttribution,
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
            MinimumSize = new Vector2(1240f, 640f),
            MaximumSize = new Vector2(1880f, 1400f),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var rows = plugin.CaptureRows();
        var dtrEntries = plugin.CaptureDtrEntries();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        var assessedRows = rows.Where(row => row.IsAssessable && !row.Ignored).ToList();
        var greenCount = assessedRows.Count(row => row.Assessment.Severity == AssessmentSeverity.Green);
        var yellowCount = assessedRows.Count(row => row.Assessment.Severity == AssessmentSeverity.Yellow);
        var redCount = assessedRows.Count(row => row.Assessment.Severity == AssessmentSeverity.Red);
        var visibleColumns = GetVisibleColumns();
        var refreshInfo = plugin.GetCatalogRefreshInfo();

        var visibleRows = GetVisibleRows(rows, dtrEntries);

        DrawStatusRow(version, refreshInfo, greenCount, yellowCount, redCount, visibleRows.Count);
        DrawActionsRow(rows);
        DrawFilterRow();
        DrawSearchRow();
        ImGui.Separator();

        //ImGui.TextUnformatted("Use the (?) button in Category / Plugin for plugin descriptions.");
        //ImGui.TextUnformatted("Use the (?) button in Ignore / ? for the current assessment and notes.");

        visibleRows = GetVisibleRows(rows, dtrEntries);

        if (visibleRows.Count == 0)
        {
            ImGui.TextWrapped("No plugins match the current filters.");
            DrawColumnSelectionPopup();
            DrawPatchNotesPopup();
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

                DrawPluginRow(row, visibleColumns, dtrEntries);
            }

            ImGui.EndTable();
        }

        DrawColumnSelectionPopup();
        DrawPatchNotesPopup();
        DrawBlockingAlertPopup();
        FinalizePendingWindowPlacement();
    }

    private static void DrawStatusRow(
        string version,
        CatalogRefreshInfo refreshInfo,
        int greenCount,
        int yellowCount,
        int redCount,
        int visibleCount)
    {
        ImGui.TextUnformatted($"{PluginInfo.DisplayName} v{version}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"Master checked: {FormatDate(refreshInfo.LastCheckedUtc)}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"Master updated: {FormatDate(refreshInfo.LastUpdatedUtc)}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"Green: {greenCount}  Yellow: {yellowCount}  Red: {redCount}  Visible: {visibleCount}");
    }

    private void DrawActionsRow(IReadOnlyList<PluginAssessmentRow> rows)
    {
        if (ImGui.SmallButton("Ko-fi"))
            plugin.OpenUrl(PluginInfo.SupportUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("Discord"))
            plugin.OpenUrl(PluginInfo.DiscordUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("AETHERFEED"))
            plugin.OpenUrl(PluginInfo.AetherfeedUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.OpenConfigUi();
        ImGui.SameLine();
        if (ImGui.SmallButton("DTR"))
            plugin.OpenDtrManagerUi();
        ImGui.SameLine();
        if (ImGui.SmallButton("Catalog"))
            plugin.OpenCatalogEditorUi();
        ImGui.SameLine();
        if (ImGui.SmallButton("Data"))
            plugin.OpenCatalogFolder();
        ImGui.SameLine();
        if (ImGui.SmallButton("Patch Notes"))
            ImGui.OpenPopup(PatchNotesPopupId);
        ImGui.SameLine();
        if (ImGui.SmallButton("Reload"))
            plugin.RefreshMasterCatalog(force: true, silent: false);
        ImGui.SameLine();
        if (ImGui.SmallButton("Status"))
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
    }

    private void DrawFilterRow()
    {
        var enabled = plugin.Configuration.PluginEnabled;
        if (ImGui.Checkbox("Manager enabled", ref enabled))
            plugin.SetPluginEnabled(enabled, printStatus: true);

        ImGui.SameLine();
        var hideUninstalled = plugin.Configuration.HideUninstalledPlugins;
        if (ImGui.Checkbox("Hide uninstalled", ref hideUninstalled))
            plugin.SetHideUninstalledPlugins(hideUninstalled);

        ImGui.SameLine();
        DrawSavedCheckbox("Detailed Notes", plugin.Configuration.ShowDetailedNotes, value => plugin.Configuration.ShowDetailedNotes = value);

        ImGui.SameLine();
        if (ImGui.SmallButton("Columns"))
            ImGui.OpenPopup(ColumnSelectionPopupId);

        ImGui.SameLine();
        DrawSavedCheckbox("Green", plugin.Configuration.ShowOnlyGreenPlugins, value => plugin.Configuration.ShowOnlyGreenPlugins = value);
        ImGui.SameLine();
        DrawSavedCheckbox("Yellow", plugin.Configuration.ShowOnlyYellowPlugins, value => plugin.Configuration.ShowOnlyYellowPlugins = value);
        ImGui.SameLine();
        DrawSavedCheckbox("Red", plugin.Configuration.ShowOnlyRedPlugins, value => plugin.Configuration.ShowOnlyRedPlugins = value);
        ImGui.SameLine();
        DrawSavedCheckbox("Has DTR entry", plugin.Configuration.ShowOnlyPluginsWithDtrEntry, value => plugin.Configuration.ShowOnlyPluginsWithDtrEntry = value);
    }

    private void DrawSearchRow()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Category");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(190f);
        ImGui.InputTextWithHint("##CategoryFilter", "Category", ref categoryFilterText, 128);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Plugin Name");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##PluginNameFilter", "Plugin Name", ref pluginNameFilterText, 128);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Author");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##AuthorFilter", "Author", ref authorFilterText, 128);
    }

    private IReadOnlyList<PluginAssessmentRow> GetVisibleRows(
        IReadOnlyList<PluginAssessmentRow> rows,
        IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        var visibleRows = plugin.Configuration.HideUninstalledPlugins
            ? rows.Where(row => row.IsInstalled)
            : rows;

        return visibleRows
            .Where(row => MatchesFilters(row, dtrEntries))
            .ToList();
    }

    private GridColumn[] GetVisibleColumns()
    {
        var cfg = plugin.Configuration;
        var columns = new System.Collections.Generic.List<GridColumn>
        {
            GridColumn.Category,
            GridColumn.Source,
        };

        if (cfg.ShowAiColumn)
            columns.Add(GridColumn.AiAttribution);

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
                ImGui.TableSetupColumn("Category / Plugin", ImGuiTableColumnFlags.WidthFixed, 260f);
                break;
            case GridColumn.Source:
                ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 120f);
                break;
            case GridColumn.AiAttribution:
                ImGui.TableSetupColumn("AI", ImGuiTableColumnFlags.WidthFixed, 42f);
                break;
            case GridColumn.Installed:
                ImGui.TableSetupColumn("Installed", ImGuiTableColumnFlags.WidthFixed, 75f);
                break;
            case GridColumn.Update:
                ImGui.TableSetupColumn("Update", ImGuiTableColumnFlags.WidthFixed, 70f);
                break;
            case GridColumn.Repo:
                ImGui.TableSetupColumn("Repo", ImGuiTableColumnFlags.WidthFixed, 120f);
                break;
            case GridColumn.Enabled:
                ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 130f);
                break;
            case GridColumn.Dtr:
                ImGui.TableSetupColumn("DTR", ImGuiTableColumnFlags.WidthFixed, 90f);
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
                ImGui.TableSetupColumn("Ignore", ImGuiTableColumnFlags.WidthFixed, 110f);
                break;
        }
    }

    private void DrawPluginRow(
        PluginAssessmentRow row,
        GridColumn[] columns,
        IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        ImGui.TableNextRow();
        if (row.IsUnavailableForCurrentPatch)
        {
            var bg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.24f, 0.24f, 0.24f, 0.38f));
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, bg);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, bg);
        }

        ImGui.PushID(row.Entry.Id);

        for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            ImGui.TableSetColumnIndex(columnIndex);
            switch (columns[columnIndex])
            {
                case GridColumn.Category:
                    DrawCategoryColumn(row);
                    break;
                case GridColumn.Source:
                    DrawSourceColumn(row);
                    break;
                case GridColumn.AiAttribution:
                    DrawAiAttributionColumn(row);
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
                    DrawDtrColumn(row, dtrEntries);
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
                    DrawNotesColumn(row, plugin.Configuration.ShowDetailedNotes);
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
        var color = row.IsUnavailableForCurrentPatch
            ? new Vector4(0.58f, 0.58f, 0.58f, 1f)
            : GetAssessmentColor(row);
        if (row.IsAssessable || row.IsUnavailableForCurrentPatch)
            ImGui.TextColored(color, row.Entry.DisplayName);
        else
            ImGui.TextUnformatted(row.Entry.DisplayName);

        if (row.HasLocalChanges)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.25f, 1f), "[*]");
        }

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

    private static void DrawSourceColumn(PluginAssessmentRow row)
    {
        var color = row.Entry.SourceKind switch
        {
            CatalogEntrySourceKind.LocalOverride => new Vector4(1.0f, 0.84f, 0.25f, 1f),
            CatalogEntrySourceKind.LocalOnly => new Vector4(0.90f, 0.72f, 0.28f, 1f),
            _ => new Vector4(0.78f, 0.78f, 0.78f, 1f),
        };
        ImGui.TextColored(color, row.Entry.SourceLabel);
    }

    private static void DrawAiAttributionColumn(PluginAssessmentRow row)
    {
        if (row.Entry.IsAiAttributed)
            ImGui.TextColored(new Vector4(0.72f, 0.48f, 1.0f, 1f), "AI");
        else
            ImGui.TextUnformatted("--");

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(AiAttributionTooltip);
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

    private void DrawDtrColumn(PluginAssessmentRow row, IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        if (HasDirectWritableDtrEntry(row) && row.RuntimeState?.DtrBarEnabled is bool dtrValue)
        {
            var dtrEnabled = dtrValue;
            if (ImGui.Checkbox("##DtrEnabled", ref dtrEnabled))
                plugin.ToggleTrackedPluginDtr(row.RuntimeState, dtrEnabled);

            return;
        }

        if (row.RuntimeState == null)
        {
            ImGui.TextUnformatted("--");
            return;
        }

        if (plugin.TryGetGlobalDtrEntry(row, dtrEntries, out var globalDtrEntry) && globalDtrEntry != null)
        {
            var userVisible = globalDtrEntry.UserVisible;
            if (ImGui.Checkbox("##GlobalDtrEnabled", ref userVisible))
                plugin.SetGlobalDtrEntryVisible(globalDtrEntry.Title, userVisible);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(globalDtrEntry.PluginShown
                    ? $"Global DTR: {globalDtrEntry.Title} ({globalDtrEntry.StateLabel})"
                    : $"Global DTR: {globalDtrEntry.Title} ({globalDtrEntry.StateLabel}; plugin sets Shown=false)");

            return;
        }

        ImGui.TextUnformatted("--");
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

    private static void DrawNotesColumn(PluginAssessmentRow row, bool showDetailedNotes)
    {
        if (!row.IsAssessable)
        {
            if (row.IsUnavailableForCurrentPatch)
            {
                ImGui.TextColored(new Vector4(0.58f, 0.58f, 0.58f, 1f), row.Assessment.Summary);
                return;
            }

            ImGui.TextUnformatted("--");
            return;
        }

        var color = GetAssessmentColor(row);
        var label = row.Ignored ? "Blue" : row.Assessment.Severity.ToString();
        var prefix = row.Ignored ? "[Ignored] " : string.Empty;
        ImGui.TextColored(color, $"{prefix}{label}: {row.Assessment.Summary}");

        if (showDetailedNotes && !string.IsNullOrWhiteSpace(row.Assessment.Details))
            ImGui.TextWrapped(row.Assessment.Details);
    }

    private static Vector4 GetAssessmentColor(PluginAssessmentRow row)
    {
        if (row.Ignored)
            return new Vector4(0.40f, 0.70f, 1.0f, 1f);

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
            if (row.Ignored)
                ImGui.TextWrapped($"Blue: ignored row. Underlying assessment is {row.Assessment.Severity}: {row.Assessment.Summary}");
            else
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
        DrawColumnToggle("AI attribution", plugin.Configuration.ShowAiColumn, value => plugin.Configuration.ShowAiColumn = value);
        DrawColumnToggle("DTR", plugin.Configuration.ShowDtrColumn, value => plugin.Configuration.ShowDtrColumn = value);
        DrawColumnToggle("Downloads", plugin.Configuration.ShowDownloadsColumn, value => plugin.Configuration.ShowDownloadsColumn = value);
        DrawColumnToggle("Last Update Date", plugin.Configuration.ShowLastUpdateColumn, value => plugin.Configuration.ShowLastUpdateColumn = value);
        DrawColumnToggle("DalamudApiLevel", plugin.Configuration.ShowDalamudApiLevelColumn, value => plugin.Configuration.ShowDalamudApiLevelColumn = value);
        DrawColumnToggle("Author", plugin.Configuration.ShowAuthorColumn, value => plugin.Configuration.ShowAuthorColumn = value);
        DrawColumnToggle("Notes / Warnings", plugin.Configuration.ShowNotesColumn, value => plugin.Configuration.ShowNotesColumn = value);
        DrawColumnToggle("Ignore / ?", plugin.Configuration.ShowIgnoreColumn, value => plugin.Configuration.ShowIgnoreColumn = value);

        ImGui.Separator();
        ImGui.TextWrapped("Category / Plugin, Source, and Enabled stay visible so the grid remains operable.");
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

    private void DrawPatchNotesPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(760f, 560f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(PatchNotesPopupId, ImGuiWindowFlags.None))
            return;

        var releases = plugin.CaptureCatalogReleases();
        if (ImGui.BeginChild("##PatchNotesScroll", new Vector2(0f, -40f), true))
        {
            if (releases.Count == 0)
            {
                ImGui.TextWrapped("No patch notes available");
            }
            else
            {
                for (var releaseIndex = 0; releaseIndex < releases.Count; releaseIndex++)
                {
                    var release = releases[releaseIndex];
                    if (releaseIndex > 0)
                        ImGui.Separator();

                    ImGui.TextColored(new Vector4(0.72f, 0.48f, 1.0f, 1f), release.Title);
                    ImGui.TextDisabled(release.PublishedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                    foreach (var section in release.Sections)
                    {
                        ImGui.Spacing();
                        ImGui.TextUnformatted(section.Heading);
                        foreach (var item in section.Items)
                        {
                            ImGui.Bullet();
                            ImGui.SameLine();
                            ImGui.TextWrapped(item);
                        }
                    }
                }
            }
        }
        ImGui.EndChild();

        if (ImGui.Button("Close"))
            ImGui.CloseCurrentPopup();

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

    private void DrawSavedCheckbox(string label, bool currentValue, Action<bool> apply)
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

    private bool MatchesFilters(PluginAssessmentRow row, IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        if (!MatchesColorFilters(row))
            return false;

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

        if (!string.IsNullOrWhiteSpace(pluginNameFilterText) &&
            !ContainsInvariant(row.Entry.DisplayName, pluginNameFilterText) &&
            !row.Entry.MatchTokens.Any(token => ContainsInvariant(token, pluginNameFilterText)))
        {
            return false;
        }

        return !plugin.Configuration.ShowOnlyPluginsWithDtrEntry || HasDtrEntry(row, dtrEntries);
    }

    private bool MatchesColorFilters(PluginAssessmentRow row)
    {
        var cfg = plugin.Configuration;
        if (!cfg.ShowOnlyGreenPlugins && !cfg.ShowOnlyYellowPlugins && !cfg.ShowOnlyRedPlugins)
            return true;

        if (!row.IsAssessable || row.Ignored)
            return false;

        return row.Assessment.Severity switch
        {
            AssessmentSeverity.Green => cfg.ShowOnlyGreenPlugins,
            AssessmentSeverity.Yellow => cfg.ShowOnlyYellowPlugins,
            AssessmentSeverity.Red => cfg.ShowOnlyRedPlugins,
            _ => false,
        };
    }

    private bool HasDtrEntry(PluginAssessmentRow row, IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        if (HasDirectWritableDtrEntry(row))
            return true;

        return plugin.TryGetGlobalDtrEntry(row, dtrEntries, out var globalDtrEntry) && globalDtrEntry != null;
    }

    private static bool HasDirectWritableDtrEntry(PluginAssessmentRow row)
        => row.RuntimeState?.CanToggleDtr == true;

    private static bool ContainsInvariant(string? haystack, string needle)
        => !string.IsNullOrWhiteSpace(haystack) &&
           haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Never";
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using botology.Models;

namespace botology.Windows;

public sealed class CatalogEditorWindow : PositionedWindow, IDisposable
{
    private readonly Plugin plugin;
    private string searchText = string.Empty;
    private string categoryFilterText = string.Empty;
    private string relationShortnameFilterText = string.Empty;
    private string relationDisplayNameFilterText = string.Empty;
    private int sourceFilterIndex;
    private CatalogEntryDraft? draft;
    private string? selectedId;

    public CatalogEditorWindow(Plugin plugin)
        : base($"{PluginInfo.DisplayName} Catalog Editor##CatalogEditor")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(1180f, 660f),
            MaximumSize = new Vector2(1880f, 1440f),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var entries = plugin.CaptureCatalogEntries();
        var localChangeCount = entries.Count(entry => entry.HasLocalChanges);
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        var filteredEntries = entries
            .Where(MatchesFilters)
            .ToList();

        if (draft != null && !draft.IsNewLocal)
        {
            var refreshedEntry = entries.FirstOrDefault(entry => entry.Id.Equals(draft.OriginalId, StringComparison.OrdinalIgnoreCase));
            if (refreshedEntry != null && !draft.HasUnsavedChanges)
                LoadDraft(refreshedEntry, startEditable: refreshedEntry.HasLocalChanges || refreshedEntry.SourceKind == CatalogEntrySourceKind.LocalOnly);
        }

        if (draft == null && !string.IsNullOrWhiteSpace(selectedId))
        {
            var selectedEntry = entries.FirstOrDefault(entry => entry.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (selectedEntry != null)
                LoadDraft(selectedEntry, startEditable: selectedEntry.HasLocalChanges || selectedEntry.SourceKind == CatalogEntrySourceKind.LocalOnly);
        }

        ImGui.TextWrapped("Master catalog rows stay read-only until you start a local override. Local work lives in the overlay file and wins over master by plugin shortname.");
        if (ImGui.SmallButton("Reload master now"))
            plugin.RefreshMasterCatalog(force: true, silent: false);
        ImGui.SameLine();
        if (ImGui.SmallButton("OPEN DATA FOLDER"))
            plugin.OpenCatalogFolder();
        ImGui.SameLine();
        if (ImGui.SmallButton("New local entry"))
            CreateNewLocalDraft();
        ImGui.SameLine();
        ImGui.BeginDisabled(localChangeCount == 0 || !ctrlHeld);
        var dropAllClicked = ImGui.SmallButton($"Drop all local changes ({localChangeCount})");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            DrawWrappedTooltip(BuildDropAllLocalChangesTooltip(localChangeCount, ctrlHeld));
        if (dropAllClicked && plugin.DropAllLocalCatalogChanges() > 0)
        {
            draft = null;
            selectedId = null;
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(localChangeCount == 0 || !ctrlHeld);
        var prepareUploadClicked = ImGui.SmallButton("PREPARE UPLOAD");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            DrawWrappedTooltip(BuildPrepareUploadTooltip(localChangeCount, ctrlHeld));
        if (prepareUploadClicked)
            plugin.PrepareCatalogUploadPackage();
        ImGui.SameLine();
        ImGui.BeginDisabled(!ctrlHeld);
        var inspectScriptClicked = ImGui.SmallButton("INSPECT SCRIPT");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            DrawWrappedTooltip(BuildInspectScriptTooltip(ctrlHeld));
        if (inspectScriptClicked)
            plugin.OpenCatalogScriptFolder();

        ImGui.Separator();
        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##CatalogSearch", "SEARCH", ref searchText, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##CatalogCategoryFilter", "CATEGORY", ref categoryFilterText, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("##CatalogSourceFilter", SourceFilterLabel(sourceFilterIndex)))
        {
            for (var index = 0; index < SourceFilterLabels.Length; index++)
            {
                if (ImGui.Selectable(SourceFilterLabels[index], index == sourceFilterIndex))
                    sourceFilterIndex = index;
            }

            ImGui.EndCombo();
        }

        var availableHeight = ImGui.GetContentRegionAvail().Y;
        const float listWidth = 390f;

        if (ImGui.BeginChild("##CatalogList", new Vector2(listWidth, availableHeight), true))
        {
            foreach (var entry in filteredEntries)
            {
                var isSelected = draft != null && entry.Id.Equals(draft.OriginalId, StringComparison.OrdinalIgnoreCase);
                var label = entry.HasLocalChanges
                    ? $"[*] {entry.DisplayName} ({entry.SourceLabel})##{entry.Id}"
                    : $"{entry.DisplayName} ({entry.SourceLabel})##{entry.Id}";
                if (ImGui.Selectable(label, isSelected))
                    LoadDraft(entry, startEditable: entry.HasLocalChanges || entry.SourceKind == CatalogEntrySourceKind.LocalOnly);
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("##CatalogEditorPane", new Vector2(0f, availableHeight), true))
        {
            DrawEditorPane(entries);
        }
        ImGui.EndChild();
        FinalizePendingWindowPlacement();
    }

    private void DrawEditorPane(IReadOnlyList<PluginCatalogEntry> entries)
    {
        if (draft == null)
        {
            ImGui.TextWrapped("Select a catalog row on the left or create a new local entry.");
            return;
        }

        ImGui.Text($"{draft.DisplayNameOrId} [{draft.SourceLabel}]");
        ImGui.TextWrapped(draft.ExistsInMaster
            ? "This row exists in master. Override it when you want a local edit."
            : "This row exists only in the local overlay.");
        ImGui.Separator();

        if (DrawActionBar())
            return;

        ImGui.Separator();
        if (ImGui.BeginChild("##CatalogEditorScroll", new Vector2(0f, 0f), false))
            DrawEditorFields(entries);
        ImGui.EndChild();
    }

    private bool DrawActionBar()
    {
        if (draft == null)
            return false;

        if (!draft.EditingEnabled && draft.ExistsInMaster)
        {
            if (ImGui.SmallButton("Override master row"))
                draft.EditingEnabled = true;
            ImGui.SameLine();
        }

        var saveLabel = draft.IsNewLocal ? "Save new entry" : "Save local changes";
        ImGui.BeginDisabled(!draft.EditingEnabled);
        var saveClicked = ImGui.SmallButton(saveLabel);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !draft.EditingEnabled)
            DrawWrappedTooltip("Click Override master row first to create a writable local copy.");
        if (saveClicked)
            SaveDraft();

        ImGui.SameLine();

        var canDropLocalChanges = CanDropLocalChanges(draft);
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!canDropLocalChanges || !ctrlHeld);
        var dropClicked = ImGui.SmallButton("Drop local changes");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            DrawWrappedTooltip(BuildDropLocalChangesTooltip(draft, canDropLocalChanges, ctrlHeld));
        if (dropClicked)
        {
            DropLocalChanges();
            return true;
        }

        ImGui.SameLine();
        var resetLabel = draft.IsNewLocal ? "Clear draft" : "Reload from current state";
        if (ImGui.SmallButton(resetLabel))
            ReloadDraftFromCurrentState();

        return false;
    }

    private void DrawEditorFields(IReadOnlyList<PluginCatalogEntry> entries)
    {
        if (draft == null)
            return;

        ImGui.BeginDisabled(!draft.EditingEnabled);

        if (draft.IsNewLocal)
            DrawNewEntryIdentityEditor(entries, draft);
        else
            DrawReadOnlyText("Id", draft.Id);

        ImGui.InputText("Category", ref draft.Category, 128);
        ImGui.InputText("Display name", ref draft.DisplayName, 128);
        DrawNotesEditor();
        DrawMultilineText("Description", ref draft.Description, 100f);
        ImGui.InputText("Repo URL", ref draft.RepoUrl, 512);
        ImGui.InputText("Repo JSON URL", ref draft.RepoJsonUrl, 512);
        DrawRelationEditor(entries, "Green plugins", RelationTarget.Green);
        DrawRelationEditor(entries, "Yellow plugins", RelationTarget.Yellow);
        DrawRelationEditor(entries, "Red plugins", RelationTarget.Red);
        DrawRulePreview(entries);

        ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextWrapped("Rules compare against plugin shortnames, including this row if you select it. Red beats yellow, yellow beats green, and a row that lists required green plugins turns red if any of them are missing.");
        if (draft.IsNewLocal)
            ImGui.TextWrapped("New rows use the plugin shortname as the runtime match token automatically.");
        else if (!draft.EditingEnabled && draft.ExistsInMaster)
            ImGui.TextWrapped("Click Override master row to start a writable local copy of this master row.");
    }

    private void SaveDraft()
    {
        if (draft == null)
            return;

        var validationError = ValidateDraft(draft);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            plugin.PrintStatus(validationError);
            return;
        }

        var entry = draft.ToEntry();
        var masterEntry = plugin.GetMasterCatalogEntry(entry.Id);
        if (masterEntry != null && EntriesEquivalent(masterEntry, entry))
        {
            plugin.ReplaceWithMasterData(entry.Id);
            plugin.PrintStatus($"No local delta to save for {entry.DisplayName}; using master data.");
            LoadDraft(masterEntry, startEditable: false);
            return;
        }

        plugin.SaveCatalogEntry(entry);
        draft = CatalogEntryDraft.FromEntry(entry with
        {
            SourceKind = plugin.GetMasterCatalogEntry(entry.Id) != null
                ? CatalogEntrySourceKind.LocalOverride
                : CatalogEntrySourceKind.LocalOnly,
            HasLocalChanges = true,
        }, startEditable: true);
        selectedId = draft.OriginalId;
    }

    private void DropLocalChanges()
    {
        if (draft == null)
            return;

        if (draft.IsNewLocal)
        {
            plugin.PrintStatus("Discarded the new local entry draft.");
            draft = null;
            selectedId = null;
            return;
        }

        if (!draft.ExistsInMaster)
        {
            if (plugin.ReplaceWithMasterData(draft.OriginalId))
                plugin.PrintStatus($"Deleted local-only row for {draft.DisplayNameOrId}.");
            draft = null;
            selectedId = null;
            return;
        }

        if (draft.SourceKind == CatalogEntrySourceKind.LocalOverride)
        {
            if (plugin.ReplaceWithMasterData(draft.OriginalId))
                plugin.PrintStatus($"Removed local override for {draft.DisplayNameOrId}.");
            ReloadDraftFromCurrentState();
            return;
        }

        plugin.PrintStatus($"Discarded unsaved edits for {draft.DisplayNameOrId}.");
        ReloadDraftFromCurrentState();
    }

    private void ReloadDraftFromCurrentState()
    {
        if (draft == null)
            return;

        if (draft.IsNewLocal)
        {
            draft = null;
            selectedId = null;
            return;
        }

        var refreshedEntry = plugin.CaptureCatalogEntries()
            .FirstOrDefault(entry => entry.Id.Equals(draft.OriginalId, StringComparison.OrdinalIgnoreCase));
        if (refreshedEntry != null)
        {
            LoadDraft(refreshedEntry, startEditable: refreshedEntry.HasLocalChanges || refreshedEntry.SourceKind == CatalogEntrySourceKind.LocalOnly);
            return;
        }

        draft = null;
        selectedId = null;
    }

    private void LoadDraft(PluginCatalogEntry entry, bool startEditable)
    {
        draft = CatalogEntryDraft.FromEntry(entry, startEditable);
        selectedId = entry.Id;
    }

    private void CreateNewLocalDraft()
    {
        draft = CatalogEntryDraft.CreateNewLocal();
        selectedId = null;
    }

    private void DrawNewEntryIdentityEditor(IReadOnlyList<PluginCatalogEntry> entries, CatalogEntryDraft entryDraft)
    {
        ImGui.TextUnformatted("Plugin shortname");
        ImGui.InputTextWithHint("##NewEntryShortname", "The shortname shown in Dalamud", ref entryDraft.NewEntryShortname, 128);
        ImGui.TextUnformatted("Optional id suffix");
        ImGui.InputTextWithHint("##NewEntryVariantSuffix", "Leave blank unless multiple plugins share that shortname", ref entryDraft.VariantSuffix, 128);
        var effectiveId = entryDraft.GetEffectiveId();
        DrawReadOnlyText("Effective id", effectiveId);

        if (string.IsNullOrWhiteSpace(effectiveId))
            return;

        var existingEntry = entries.FirstOrDefault(entry => entry.Id.Equals(effectiveId, StringComparison.OrdinalIgnoreCase));
        if (existingEntry == null)
            return;

        var warningText = existingEntry.SourceKind == CatalogEntrySourceKind.LocalOnly
            ? "That combination of name and suffix already exists in local data. Saving will update that local row."
            : "That combination of name and suffix already exists and this will become a local override.";
        ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.35f, 1.0f), warningText);
    }

    private void DrawMultilineText(string label, ref string value, float height)
    {
        ImGui.TextUnformatted(label);
        ImGui.InputTextMultiline($"##{label.Replace(' ', '_')}", ref value, 4096, new Vector2(-1f, height));
    }

    private void DrawNotesEditor()
    {
        if (draft == null)
            return;

        ImGui.TextUnformatted("Notes");
        ImGui.TextWrapped("Notes are the operator-facing explanation shown with the current rule result. The Green / Yellow / Red plugin lists decide the color; use Notes to explain why the row should end up in that state.");
        ImGui.InputTextMultiline("##Notes", ref draft.Notes, 4096, new Vector2(-1f, 100f));
    }

    private void DrawRelationEditor(IReadOnlyList<PluginCatalogEntry> entries, string label, RelationTarget relationTarget)
    {
        if (draft == null)
            return;

        var relationEntries = GetRelationPickerEntries(entries)
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo($"##{label.Replace(' ', '_')}_Picker", "Select plugin shortname..."))
        {
            if (ImGui.IsWindowAppearing())
            {
                relationShortnameFilterText = string.Empty;
                relationDisplayNameFilterText = string.Empty;
            }

            ImGui.SetNextItemWidth(180f);
            ImGui.InputTextWithHint($"##{label.Replace(' ', '_')}_Shortname", "SHORTNAME", ref relationShortnameFilterText, 128);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180f);
            ImGui.InputTextWithHint($"##{label.Replace(' ', '_')}_DisplayName", "DISPLAY NAME", ref relationDisplayNameFilterText, 128);
            if (ImGui.BeginChild($"##{label.Replace(' ', '_')}_PickerList", new Vector2(0f, 220f), true))
            {
                foreach (var entry in relationEntries)
                {
                    if (!string.IsNullOrWhiteSpace(relationShortnameFilterText) &&
                        !entry.Id.Contains(relationShortnameFilterText, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(relationDisplayNameFilterText) &&
                        !entry.DisplayName.Contains(relationDisplayNameFilterText, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (ImGui.Selectable($"{entry.Id} - {entry.DisplayName}##{label}_{entry.Id}"))
                    {
                        AppendRelationId(relationTarget, entry.Id);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            ImGui.EndChild();
            ImGui.EndCombo();
        }

        var ids = GetRelationIds(relationTarget);
        if (ids.Length == 0)
        {
            ImGui.TextDisabled("No plugin shortnames selected.");
            return;
        }

        var height = MathF.Min(120f, 30f + (ids.Length * 24f));
        if (ImGui.BeginChild($"##{label.Replace(' ', '_')}_List", new Vector2(-1f, height), true))
        {
            foreach (var id in ids)
            {
                if (ImGui.SmallButton($"Remove##{label}_{id}"))
                    RemoveRelationId(relationTarget, id);

                ImGui.SameLine();
                var relatedEntry = relationEntries.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                var displayText = relatedEntry == null || relatedEntry.DisplayName.Equals(id, StringComparison.OrdinalIgnoreCase)
                    ? id
                    : $"{id} - {relatedEntry.DisplayName}";
                ImGui.TextUnformatted(displayText);
            }
        }
        ImGui.EndChild();
    }

    private IEnumerable<PluginCatalogEntry> GetRelationPickerEntries(IReadOnlyList<PluginCatalogEntry> entries)
    {
        if (draft == null)
            return entries;

        var currentId = draft.GetEffectiveId();
        if (string.IsNullOrWhiteSpace(currentId) ||
            entries.Any(entry => entry.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase)))
        {
            return entries;
        }

        var draftDisplayName = string.IsNullOrWhiteSpace(draft.DisplayName) ? currentId : draft.DisplayName.Trim();
        var draftCategory = string.IsNullOrWhiteSpace(draft.Category) ? "Uncategorized" : draft.Category.Trim();
        var draftNotes = string.IsNullOrWhiteSpace(draft.Notes) ? "No notes configured." : draft.Notes.Trim();

        return entries.Concat(
        [
            new PluginCatalogEntry(
                currentId,
                draftCategory,
                draftDisplayName,
                draft.GetNormalizedMatchTokens(),
                draftNotes,
                SourceKind: draft.ExistsInMaster ? CatalogEntrySourceKind.LocalOverride : CatalogEntrySourceKind.LocalOnly,
                HasLocalChanges: true),
        ]);
    }

    private void DrawRulePreview(IReadOnlyList<PluginCatalogEntry> entries)
    {
        if (draft == null)
            return;

        var relationEntries = GetRelationPickerEntries(entries).ToArray();
        var greenIds = GetRelationIds(RelationTarget.Green);
        var yellowIds = GetRelationIds(RelationTarget.Yellow);
        var redIds = GetRelationIds(RelationTarget.Red);

        ImGui.Separator();
        ImGui.TextUnformatted("Rule preview");

        if (greenIds.Length == 0 && yellowIds.Length == 0 && redIds.Length == 0)
        {
            ImGui.TextDisabled("No green / yellow / red rules configured yet.");
            return;
        }

        if (redIds.Length > 0)
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), $"Red when loaded: {FormatRelationNames(redIds, relationEntries)}");

        if (yellowIds.Length > 0)
            ImGui.TextColored(new Vector4(1f, 0.86f, 0.35f, 1f), $"Yellow when loaded: {FormatRelationNames(yellowIds, relationEntries)}");

        if (greenIds.Length > 0)
        {
            var formattedNames = FormatRelationNames(greenIds, relationEntries);
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), $"Red when required green plugins are missing: {formattedNames}");
            ImGui.TextColored(new Vector4(0.45f, 0.95f, 0.45f, 1f), $"Green when required green plugins are loaded: {formattedNames}");
        }

        var notesPreview = string.IsNullOrWhiteSpace(draft.Notes)
            ? "No notes configured."
            : draft.Notes.Trim();
        ImGui.TextWrapped($"Notes shown with this row: {notesPreview}");
    }

    private void AppendRelationId(RelationTarget relationTarget, string id)
    {
        if (draft == null)
            return;

        var values = GetRelationIds(relationTarget).ToList();
        if (!values.Contains(id, StringComparer.OrdinalIgnoreCase))
            values.Add(id);

        SetRelationIds(relationTarget, values);
    }

    private void RemoveRelationId(RelationTarget relationTarget, string id)
    {
        if (draft == null)
            return;

        var values = GetRelationIds(relationTarget)
            .Where(value => !value.Equals(id, StringComparison.OrdinalIgnoreCase));
        SetRelationIds(relationTarget, values);
    }

    private string[] GetRelationIds(RelationTarget relationTarget)
        => relationTarget switch
        {
            RelationTarget.Green => ParseListText(draft?.GreenIdsText ?? string.Empty),
            RelationTarget.Yellow => ParseListText(draft?.YellowIdsText ?? string.Empty),
            RelationTarget.Red => ParseListText(draft?.RedIdsText ?? string.Empty),
            _ => [],
        };

    private void SetRelationIds(RelationTarget relationTarget, IEnumerable<string> ids)
    {
        if (draft == null)
            return;

        var updatedText = string.Join(Environment.NewLine, ids.Distinct(StringComparer.OrdinalIgnoreCase));
        switch (relationTarget)
        {
            case RelationTarget.Green:
                draft.GreenIdsText = updatedText;
                break;
            case RelationTarget.Yellow:
                draft.YellowIdsText = updatedText;
                break;
            case RelationTarget.Red:
                draft.RedIdsText = updatedText;
                break;
        }
    }

    private bool MatchesFilters(PluginCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(searchText) &&
            !entry.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) &&
            !entry.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(categoryFilterText) &&
            !entry.Category.Contains(categoryFilterText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return sourceFilterIndex switch
        {
            1 => entry.SourceKind == CatalogEntrySourceKind.Master,
            2 => entry.SourceKind == CatalogEntrySourceKind.LocalOverride,
            3 => entry.SourceKind == CatalogEntrySourceKind.LocalOnly,
            _ => true,
        };
    }

    private static bool CanDropLocalChanges(CatalogEntryDraft entryDraft)
        => entryDraft.IsNewLocal ||
           !entryDraft.ExistsInMaster ||
           entryDraft.SourceKind == CatalogEntrySourceKind.LocalOverride ||
           entryDraft.EditingEnabled;

    private static string BuildDropLocalChangesTooltip(CatalogEntryDraft entryDraft, bool canDropLocalChanges, bool ctrlHeld)
    {
        if (!canDropLocalChanges)
            return "There are no local changes to drop for this row.";

        if (!ctrlHeld)
        {
            return entryDraft switch
            {
                { IsNewLocal: true } => "Hold CTRL to discard this new local entry draft.",
                { ExistsInMaster: false } => "Hold CTRL to delete this local-only row from the overlay.",
                { SourceKind: CatalogEntrySourceKind.LocalOverride } => "Hold CTRL to delete the local override and fall back to master data.",
                _ => "Hold CTRL to discard the current edit session for this master row.",
            };
        }

        return entryDraft switch
        {
            { IsNewLocal: true } => "CTRL is held. Clicking now discards this new local entry draft.",
            { ExistsInMaster: false } => "CTRL is held. Clicking now deletes this local-only row from the overlay.",
            { SourceKind: CatalogEntrySourceKind.LocalOverride } => "CTRL is held. Clicking now deletes the local override and restores master data.",
            _ => "CTRL is held. Clicking now discards the current edit session and reloads master data.",
        };
    }

    private static string BuildDropAllLocalChangesTooltip(int localChangeCount, bool ctrlHeld)
    {
        if (localChangeCount == 0)
            return "There are no local catalog changes to drop.";

        return ctrlHeld
            ? $"CTRL is held. Clicking now deletes all {localChangeCount} local override/local-only rows and falls back to master."
            : $"Hold CTRL to delete all {localChangeCount} local override/local-only rows and fall back to master.";
    }

    private static string BuildPrepareUploadTooltip(int localChangeCount, bool ctrlHeld)
    {
        if (localChangeCount == 0)
            return "There are no local catalog changes to prepare for upload.";

        return ctrlHeld
            ? "CTRL is held. Clicking now exports master.json, local.json, and plugin-repository-links.json into today's upload-prep folder, launches the Python review script, and opens the folder in Explorer. Use the command window to interactively approve or deny each changed local row. This does not upload anything by itself."
            : "Requires Python. Hold CTRL to export today's upload-prep folder and start the interactive approve/deny review script. This does not upload anything by itself.";
    }

    private static string BuildInspectScriptTooltip(bool ctrlHeld)
        => ctrlHeld
            ? "CTRL is held. Clicking now opens the folder containing review_catalog.py."
            : "CTRL-click to see the folder containing review_catalog.py if you are curious.";

    private static string? ValidateDraft(CatalogEntryDraft entryDraft)
    {
        if (string.IsNullOrWhiteSpace(entryDraft.GetEffectiveId()))
            return "Catalog rows need a plugin shortname before they can be saved.";

        if (string.IsNullOrWhiteSpace(entryDraft.DisplayName))
            return "Catalog rows need a display name.";

        if (entryDraft.GetNormalizedMatchTokens().Length == 0)
            return "Catalog rows need a plugin shortname before they can be saved.";

        return null;
    }

    private static void DrawReadOnlyText(string label, string value)
    {
        ImGui.TextUnformatted(label);
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(value) ? "--" : value);
    }

    private static void DrawWrappedTooltip(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(380f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static bool EntriesEquivalent(PluginCatalogEntry left, PluginCatalogEntry right)
        => string.Equals(NormalizeText(left.Id), NormalizeText(right.Id), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.Category), NormalizeText(right.Category), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.DisplayName), NormalizeText(right.DisplayName), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.Notes), NormalizeText(right.Notes), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.RepoUrl), NormalizeText(right.RepoUrl), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.RepoJsonUrl), NormalizeText(right.RepoJsonUrl), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeText(left.Description), NormalizeText(right.Description), StringComparison.OrdinalIgnoreCase) &&
           ListsEqual(left.MatchTokens, right.MatchTokens) &&
           ListsEqual(left.RepoJsonUrls, right.RepoJsonUrls) &&
           ListsEqual(left.GreenIds, right.GreenIds) &&
           ListsEqual(left.YellowIds, right.YellowIds) &&
           ListsEqual(left.RedIds, right.RedIds);

    private static bool ListsEqual(IEnumerable<string>? left, IEnumerable<string>? right)
        => ParseList(left).SequenceEqual(ParseList(right), StringComparer.OrdinalIgnoreCase);

    private static string[] ParseList(IEnumerable<string>? values)
        => values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray()
           ?? [];

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatRelationNames(IEnumerable<string> ids, IEnumerable<PluginCatalogEntry> entries)
    {
        var entryMap = entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id =>
            {
                if (!entryMap.TryGetValue(id, out var entry) ||
                    entry.DisplayName.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }

                return $"{entry.DisplayName} ({id})";
            }));
    }

    private static string[] ParseListText(string rawText)
        => rawText.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeIdFragment(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        var builder = new StringBuilder(rawText.Length);
        var lastWasUnderscore = false;
        foreach (var ch in rawText.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasUnderscore = false;
                continue;
            }

            if (ch is ' ' or '_' or '-')
            {
                if (!lastWasUnderscore && builder.Length > 0)
                {
                    builder.Append('_');
                    lastWasUnderscore = true;
                }
            }
        }

        return builder.ToString().Trim('_');
    }

    private static string SourceFilterLabel(int index)
        => index is >= 0 and < 4 ? SourceFilterLabels[index] : SourceFilterLabels[0];

    private static readonly string[] SourceFilterLabels =
    {
        "All sources",
        "Master only",
        "Local overrides",
        "Local only",
    };

    private enum RelationTarget
    {
        Green,
        Yellow,
        Red,
    }

    private sealed class CatalogEntryDraft
    {
        public string OriginalId = string.Empty;
        public string Id = string.Empty;
        public string NewEntryShortname = string.Empty;
        public string VariantSuffix = string.Empty;
        public string Category = string.Empty;
        public string DisplayName = string.Empty;
        public string MatchTokensText = string.Empty;
        public string Notes = string.Empty;
        public string RepoUrl = string.Empty;
        public string RepoJsonUrl = string.Empty;
        public string Description = string.Empty;
        public string RepoJsonUrlsText = string.Empty;
        public string GreenIdsText = string.Empty;
        public string YellowIdsText = string.Empty;
        public string RedIdsText = string.Empty;
        public CatalogEntrySourceKind SourceKind;
        public bool ExistsInMaster;
        public bool IsNewLocal;
        public bool EditingEnabled;

        public string SourceLabel => SourceKind switch
        {
            CatalogEntrySourceKind.LocalOverride => "local override",
            CatalogEntrySourceKind.LocalOnly => "local only",
            _ => "master",
        };

        public string DisplayNameOrId => string.IsNullOrWhiteSpace(DisplayName) ? GetEffectiveId() : DisplayName;

        public bool HasUnsavedChanges => EditingEnabled;

        public static CatalogEntryDraft FromEntry(PluginCatalogEntry entry, bool startEditable)
        {
            return new CatalogEntryDraft
            {
                OriginalId = entry.Id,
                Id = entry.Id,
                Category = entry.Category,
                DisplayName = entry.DisplayName,
                MatchTokensText = string.Join(Environment.NewLine, entry.MatchTokens),
                Notes = entry.Notes,
                RepoUrl = entry.RepoUrl ?? string.Empty,
                RepoJsonUrl = entry.RepoJsonUrl ?? string.Empty,
                Description = entry.Description ?? string.Empty,
                RepoJsonUrlsText = string.Join(Environment.NewLine, entry.RepoJsonUrls ?? []),
                GreenIdsText = string.Join(Environment.NewLine, entry.GreenIds ?? []),
                YellowIdsText = string.Join(Environment.NewLine, entry.YellowIds ?? []),
                RedIdsText = string.Join(Environment.NewLine, entry.RedIds ?? []),
                SourceKind = entry.SourceKind,
                ExistsInMaster = entry.SourceKind != CatalogEntrySourceKind.LocalOnly,
                IsNewLocal = false,
                EditingEnabled = startEditable,
            };
        }

        public static CatalogEntryDraft CreateNewLocal()
        {
            return new CatalogEntryDraft
            {
                OriginalId = string.Empty,
                SourceKind = CatalogEntrySourceKind.LocalOnly,
                ExistsInMaster = false,
                IsNewLocal = true,
                EditingEnabled = true,
                Notes = "No notes configured.",
            };
        }

        public string GetEffectiveId()
            => IsNewLocal
                ? NormalizeIdFragment($"{NewEntryShortname}{VariantSuffix}")
                : Id.Trim();

        public string[] GetNormalizedMatchTokens()
        {
            var matchTokens = ParseListText(MatchTokensText);
            if (matchTokens.Length > 0)
                return matchTokens;

            return IsNewLocal && !string.IsNullOrWhiteSpace(NewEntryShortname)
                ? [NewEntryShortname.Trim()]
                : [];
        }

        public PluginCatalogEntry ToEntry()
        {
            var normalizedId = GetEffectiveId();
            var greenIds = ParseListText(GreenIdsText);
            var yellowIds = ParseListText(YellowIdsText);
            var redIds = ParseListText(RedIdsText);
            var sourceKind = ExistsInMaster
                ? CatalogEntrySourceKind.LocalOverride
                : CatalogEntrySourceKind.LocalOnly;

            return new PluginCatalogEntry(
                normalizedId,
                string.IsNullOrWhiteSpace(Category) ? "Uncategorized" : Category.Trim(),
                DisplayName.Trim(),
                GetNormalizedMatchTokens(),
                string.IsNullOrWhiteSpace(Notes) ? "No notes configured." : Notes.Trim(),
                string.IsNullOrWhiteSpace(RepoUrl) ? null : RepoUrl.Trim(),
                string.IsNullOrWhiteSpace(RepoJsonUrl) ? null : RepoJsonUrl.Trim(),
                string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                ParseListText(RepoJsonUrlsText),
                greenIds.Length > 0 ? greenIds : null,
                yellowIds.Length > 0 ? yellowIds : null,
                redIds.Length > 0 ? redIds : null,
                sourceKind,
                true);
        }
    }
}

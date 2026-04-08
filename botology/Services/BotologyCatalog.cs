using System;
using System.Collections.Generic;
using System.Linq;
using botology.Models;

namespace botology.Services;

public static class BotologyCatalog
{
    private static readonly IReadOnlyList<PluginCatalogEntry> entries = new[]
    {
        new PluginCatalogEntry("lootgoblin", "DOW/DOM Content Bots", "LootGoblin", new[] { "LootGoblin" }, "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty doesn't even manage normally. This is volatile behaviour.", RuleType: "autoduty_conflict"),
        new PluginCatalogEntry("mogtome", "DOW/DOM Content Bots", "MOGTOME", new[] { "MOGTOME" }, "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty doesn't even manage normally. This is volatile behaviour.", RuleType: "autoduty_conflict"),
        new PluginCatalogEntry("aurafarmer", "DOW/DOM Content Bots", "AuraFarmer", new[] { "AuraFarmer" }, "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty doesn't even manage normally. This is volatile behaviour.", RuleType: "autoduty_conflict"),
        new PluginCatalogEntry("lanparty", "DOW/DOM Content Bots", "LANParty", new[] { "LANParty" }, "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty doesn't even manage normally. This is volatile behaviour.", RuleType: "autoduty_conflict"),
        new PluginCatalogEntry("cbt_vfate", "DOW/DOM Content Bots", "CBT (vfate activated)", new[] { "CBT" }, "If you use Twist of Fayte at the same time as CBT vfate there might be issues.", RuleType: "paired_conflict_yellow", RelatedIds: new[] { "twist_of_fayte" }),
        new PluginCatalogEntry("twist_of_fayte", "DOW/DOM Content Bots", "Twist of Fayte", new[] { "TwistOfFayte", "Twist of Fayte" }, "If you use CBT vfate at the same time as Twist of Fayte there might be issues.", RuleType: "paired_conflict_yellow", RelatedIds: new[] { "cbt_vfate" }),
        new PluginCatalogEntry("bunny_farm_eureka", "DOW/DOM Content Bots", "Bunny Farm Eureka", new[] { "BunnyFarmEureka", "Bunny Farm Eureka" }, "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty doesn't even manage normally. This is volatile behaviour.", RuleType: "autoduty_conflict"),

        new PluginCatalogEntry("accountant", "Empire Management", "Accountant", new[] { "Accountant" }, "This plugin is fine to keep if you want to use the Garden tracker. Otherwise Submarine Tracker and AutoRetainer are sufficient to cover retainer and submarine information."),
        new PluginCatalogEntry("allagan_tools", "Empire Management", "Allagan Tools", new[] { "AllaganTools", "Allagan Tools" }, "This tool offers the same data recording as XA DB and additional tools and guides but will frequently corrupt its own database and is unreliable for long term 100% data mining of a large empire of characters and FCs."),
        new PluginCatalogEntry("altoholic", "Empire Management", "Altoholic", new[] { "Altoholic" }, "You should use XA DB instead. This plugin will CTD you if you try to browse the sheets tabs. Only use this over XA DB if you like the layout."),
        new PluginCatalogEntry("autoretainer", "Empire Management", "AutoRetainer", new[] { "AutoRetainer", "Autoretainer" }, "AutoDuty should be disabled or it can cause problems with checking retainers. Solution for now is to have an \"/ad stop\". If VerMaxion is installed we are safe with AutoDuty being enabled."),
        new PluginCatalogEntry("submarine_tracker", "Empire Management", "Submarine Tracker", new[] { "SubmarineTracker", "Submarine Tracker" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. The folder can get large."),
        new PluginCatalogEntry("xa_db", "Empire Management", "XA DB", new[] { "XADB", "XA DB" }, "This tool covers all of the data of Allagan Tools and Altoholic. For now it doesn't have auto list via tooltip for hovered item, so keep Allagan Tools if you need that feature."),

        new PluginCatalogEntry("artisan", "DOH/L Content Bots", "Artisan", new[] { "Artisan" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("autohook", "DOH/L Content Bots", "AutoHook", new[] { "AutoHook", "Autohook" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("henchman", "DOH/L Content Bots", "Henchman", new[] { "Henchman" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("gatherbuddy", "DOH/L Content Bots", "GatherBuddy", new[] { "GatherBuddy" }, "Pick one GatherBuddy only.", RuleType: "paired_conflict_red", RelatedIds: new[] { "gatherbuddy_reborn" }),
        new PluginCatalogEntry("gatherbuddy_reborn", "DOH/L Content Bots", "GatherBuddyReborn", new[] { "GatherBuddyReborn" }, "Pick one GatherBuddy only.", RuleType: "paired_conflict_red", RelatedIds: new[] { "gatherbuddy" }),
        new PluginCatalogEntry("icecosmic", "DOH/L Content Bots", "ICECOSMIC", new[] { "ICECOSMIC" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("vsatisfy", "DOH/L Content Bots", "vSatisfy", new[] { "vSatisfy", "Vsatisfy" }, "It has a small problem of not auto-deactivating itself if you get stuck in an unfinished cycle, so be sure to hit stop tasks before doing something else."),

        new PluginCatalogEntry("autoduty", "Duty solving", "AutoDuty", new[] { "AutoDuty" }, "Keep this plugin disabled unless you specifically need it on. It interferes with many things without even being activated.", RuleType: "loaded_warning_yellow"),
        new PluginCatalogEntry("ai_duty_solver", "Duty solving", "AI Duty Solver", new[] { "AIDutySolver", "AI Duty Solver" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),

        new PluginCatalogEntry("bossmod", "Rotations/AI", "BossMod", new[] { "BossMod" }, "Ensure you are only using one rotation plugin at the same time. Pick one BossMod only.", RuleType: "bossmod_pair", RelatedIds: new[] { "bossmod_reborn" }),
        new PluginCatalogEntry("bossmod_reborn", "Rotations/AI", "BossModReborn", new[] { "BossModReborn" }, "Ensure you are only using one rotation plugin at the same time. Pick one BossMod only.", RuleType: "bossmod_pair", RelatedIds: new[] { "bossmod" }),
        new PluginCatalogEntry("wrath", "Rotations/AI", "Wrath", new[] { "Wrath" }, "Ensure you are only using one rotation plugin at the same time.", RuleType: "rotation_conflict"),
        new PluginCatalogEntry("rotation_solver_reborn", "Rotations/AI", "RotationSolverReborn", new[] { "RotationSolverReborn", "RotationSolver" }, "Ensure you are only using one rotation plugin at the same time.", RuleType: "rotation_conflict"),
        new PluginCatalogEntry("ultimate_combo", "Rotations/AI", "UltimateCombo", new[] { "UltimateCombo" }, "Ensure you are only using one rotation plugin at the same time.", RuleType: "rotation_conflict"),

        new PluginCatalogEntry("frenrider", "Utility Bots", "FrenRider", new[] { "FrenRider" }, "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty doesn't even manage normally. This is volatile behaviour.", RuleType: "autoduty_conflict"),
        new PluginCatalogEntry("hellofellowhumans", "Utility Bots", "HelloFellowHumans", new[] { "HelloFellowHumans" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game unless you want it to hahaha."),
        new PluginCatalogEntry("vermaxion", "Utility Bots", "VerMaxion", new[] { "VERMAXION", "VerMaxion" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("vnavmesh", "Utility Bots", "VNAVMESH", new[] { "vnavmesh", "VNAVMESH" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game.", RuleType: "direct_conflict_red", RelatedIds: new[] { "smartnav" }),
        new PluginCatalogEntry("smartnav", "Utility Bots", "SmartNav", new[] { "SmartNav" }, "Wiggly TBD plugin.", RuleType: "direct_conflict_red", RelatedIds: new[] { "vnavmesh" }),
        new PluginCatalogEntry("visland", "Utility Bots", "VISLAND", new[] { "VISLAND", "visland" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("xa_slave", "Utility Bots", "XA Slave", new[] { "XASlave", "XA Slave" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("xa_hud", "Utility Bots", "XA HUD", new[] { "XAHUD", "XA HUD" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("xa_debug", "Utility Bots", "XA Debug", new[] { "XADebug", "XA Debug" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("something_need_doing", "Utility Bots", "Something Need Doing", new[] { "SomethingNeedDoing", "SND" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game unless you want it to hahaha."),
        new PluginCatalogEntry("tracky_track", "Utility Bots", "Tracky Track", new[] { "TrackyTrack", "Tracky Track" }, "There is no reason to uninstall this plugin unless you want to save space on your HD. It does get a bit spicy in terms of storage."),

        new PluginCatalogEntry("questionable_wiggly", "Quest", "Questionable (wiggly)", new[] { "Questionable", "QuestionableWiggly" }, "These plugins have diverged already.", RuleType: "paired_conflict_red", RelatedIds: new[] { "questionable_punish" }),
        new PluginCatalogEntry("questionable_punish", "Quest", "Questionable (punish)", new[] { "QuestionablePunish", "QuestionablePunished" }, "These plugins have diverged already.", RuleType: "paired_conflict_red", RelatedIds: new[] { "questionable_wiggly" }),
    };

    public static IReadOnlyList<PluginCatalogEntry> Entries => RepositoryLinkCatalog.GetCatalogEntries(entries);

    public static IReadOnlyList<PluginAssessmentRow> BuildRows(PluginSnapshot snapshot, ISet<string> ignoredIds)
    {
        var effectiveEntries = Entries;
        var effectiveEntryMap = effectiveEntries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var rows = new List<PluginAssessmentRow>(effectiveEntries.Count);
        foreach (var entry in effectiveEntries)
        {
            var runtimeState = snapshot.FindBestMatch(entry.MatchTokens);
            var assessment = Evaluate(entry, runtimeState, snapshot, effectiveEntryMap);
            rows.Add(new PluginAssessmentRow(entry, runtimeState, assessment, ignoredIds.Contains(entry.Id)));
        }

        return rows;
    }

    public static string BuildAlertSummary(IEnumerable<PluginAssessmentRow> rows)
    {
        var actionableRows = rows
            .Where(row => row.IsAssessable && !row.Ignored && row.Assessment.Severity != AssessmentSeverity.Green)
            .ToList();

        if (actionableRows.Count == 0)
            return "All active tracked plugins are green.";

        var labels = actionableRows
            .Take(4)
            .Select(row => $"{row.Entry.DisplayName} ({row.Assessment.Severity})");
        var suffix = actionableRows.Count > 4 ? " ..." : string.Empty;
        return $"{actionableRows.Count} non-green plugin entries: {string.Join(", ", labels)}{suffix}";
    }

    private static AssessmentResult Evaluate(
        PluginCatalogEntry entry,
        PluginRuntimeState? runtimeState,
        PluginSnapshot snapshot,
        IReadOnlyDictionary<string, PluginCatalogEntry> entryMap)
    {
        if (runtimeState?.IsLoaded != true)
        {
            var summary = runtimeState == null
                ? "Not installed."
                : "Installed but disabled.";
            return Inactive(entry, summary);
        }

        if (TryEvaluateConfiguredRule(entry, snapshot, entryMap, out var configuredAssessment))
            return configuredAssessment;

        return entry.Id switch
        {
            "accountant" => IsLoaded(snapshot, "autoretainer", entryMap) || IsLoaded(snapshot, "submarine_tracker", entryMap)
                ? Yellow(entry, "AutoRetainer or Submarine Tracker already covers overlapping empire data.")
                : Green(entry, "No overlapping empire trackers detected."),

            "allagan_tools" => IsLoaded(snapshot, "xa_db", entryMap)
                ? Yellow(entry, "XA DB is loaded and overlaps the same empire data capture.")
                : Green(entry, "No XA DB overlap detected."),

            "altoholic" => IsLoaded(snapshot, "xa_db", entryMap)
                ? Yellow(entry, "Altoholic is loaded and XA DB is also present.")
                : Yellow(entry, "Altoholic is loaded; XA DB is the preferred option."),

            "autoretainer" => IsLoaded(snapshot, "autoduty", entryMap) && IsLoaded(snapshot, "vermaxion", entryMap)
                ? Green(entry, "AutoDuty is loaded, but VerMaxion is also present for the current safe workaround.")
                : IsLoaded(snapshot, "autoduty", entryMap)
                    ? Yellow(entry, "AutoDuty is loaded and may interfere with retainer checks.")
                    : Green(entry, "No AutoDuty interference detected."),

            "submarine_tracker" => IsLoaded(snapshot, "accountant", entryMap)
                ? Yellow(entry, "Accountant is loaded and overlaps part of this data surface.")
                : Green(entry, "No overlapping submarine tracker warning triggered."),

            "xa_db" => IsLoaded(snapshot, "allagan_tools", entryMap) || IsLoaded(snapshot, "altoholic", entryMap)
                ? Yellow(entry, "Allagan Tools or Altoholic is loaded alongside XA DB.")
                : Green(entry, "No overlapping empire data plugins detected."),

            _ => Green(entry, "No warning rules triggered."),
        };
    }

    private static bool TryEvaluateConfiguredRule(
        PluginCatalogEntry entry,
        PluginSnapshot snapshot,
        IReadOnlyDictionary<string, PluginCatalogEntry> entryMap,
        out AssessmentResult assessment)
    {
        switch (NormalizeRuleType(entry.RuleType))
        {
            case "autoduty_conflict":
                assessment = AutoDutyConflict(entry, snapshot, entryMap);
                return true;

            case "paired_conflict_yellow":
                assessment = RelatedConflict(entry, snapshot, entryMap, AssessmentSeverity.Yellow, "No conflicting paired plugin detected.");
                return true;

            case "paired_conflict_red":
                assessment = RelatedConflict(entry, snapshot, entryMap, AssessmentSeverity.Red, "No competing paired plugin detected.");
                return true;

            case "direct_conflict_red":
                assessment = RelatedConflict(entry, snapshot, entryMap, AssessmentSeverity.Red, "No direct conflict detected.");
                return true;

            case "rotation_conflict":
                assessment = RotationEvaluation(entry, snapshot, entryMap);
                return true;

            case "bossmod_pair":
                assessment = BossmodEvaluation(entry, snapshot, entryMap);
                return true;

            case "loaded_warning_yellow":
                assessment = Yellow(entry, $"{entry.DisplayName} is currently loaded.");
                return true;
        }

        assessment = Green(entry, "No warning rules triggered.");
        return false;
    }

    private static AssessmentResult AutoDutyConflict(
        PluginCatalogEntry entry,
        PluginSnapshot snapshot,
        IReadOnlyDictionary<string, PluginCatalogEntry> entryMap)
    {
        var loadedEntries = GetLoadedEntries(
            snapshot,
            entryMap,
            entry.RelatedIds is { Length: > 0 } ? entry.RelatedIds : new[] { "autoduty" });

        return loadedEntries.Count > 0
            ? Red(entry, $"{FormatEntryNames(loadedEntries)} {(loadedEntries.Count == 1 ? "is" : "are")} loaded.")
            : Green(entry, "No AutoDuty conflict detected.");
    }

    private static AssessmentResult RelatedConflict(
        PluginCatalogEntry entry,
        PluginSnapshot snapshot,
        IReadOnlyDictionary<string, PluginCatalogEntry> entryMap,
        AssessmentSeverity severity,
        string noConflictSummary)
    {
        var loadedEntries = GetLoadedEntries(snapshot, entryMap, entry.RelatedIds);
        if (loadedEntries.Count == 0)
            return Green(entry, noConflictSummary);

        var summary = $"{FormatEntryNames(loadedEntries)} {(loadedEntries.Count == 1 ? "is" : "are")} loaded at the same time.";
        return severity == AssessmentSeverity.Red
            ? Red(entry, summary)
            : Yellow(entry, summary);
    }

    private static AssessmentResult RotationEvaluation(
        PluginCatalogEntry entry,
        PluginSnapshot snapshot,
        IReadOnlyDictionary<string, PluginCatalogEntry> entryMap)
    {
        var otherLoaded = entryMap.Values
            .Where(candidate =>
                !candidate.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase) &&
                IsRotationEntry(candidate) &&
                snapshot.IsLoaded(candidate.MatchTokens))
            .ToList();

        return otherLoaded.Count > 0
            ? Yellow(entry, $"{FormatEntryNames(otherLoaded)} {(otherLoaded.Count == 1 ? "is" : "are")} loaded at the same time.")
            : Green(entry, "No competing rotation plugin detected.");
    }

    private static AssessmentResult BossmodEvaluation(
        PluginCatalogEntry entry,
        PluginSnapshot snapshot,
        IReadOnlyDictionary<string, PluginCatalogEntry> entryMap)
    {
        var loadedRivals = GetLoadedEntries(snapshot, entryMap, entry.RelatedIds);
        if (loadedRivals.Count > 0)
            return Red(entry, $"{FormatEntryNames(loadedRivals)} {(loadedRivals.Count == 1 ? "is" : "are")} also loaded.");

        return RotationEvaluation(entry, snapshot, entryMap);
    }

    private static bool IsLoaded(
        PluginSnapshot snapshot,
        string id,
        IReadOnlyDictionary<string, PluginCatalogEntry> entryMap)
        => entryMap.TryGetValue(id, out var entry) && snapshot.IsLoaded(entry.MatchTokens);

    private static List<PluginCatalogEntry> GetLoadedEntries(
        PluginSnapshot snapshot,
        IReadOnlyDictionary<string, PluginCatalogEntry> entryMap,
        IEnumerable<string>? ids)
    {
        var loadedEntries = new List<PluginCatalogEntry>();
        if (ids == null)
            return loadedEntries;

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seenIds.Add(id))
                continue;

            if (entryMap.TryGetValue(id, out var entry) && snapshot.IsLoaded(entry.MatchTokens))
                loadedEntries.Add(entry);
        }

        return loadedEntries;
    }

    private static bool IsRotationEntry(PluginCatalogEntry entry)
    {
        var normalizedRuleType = NormalizeRuleType(entry.RuleType);
        return normalizedRuleType is "rotation_conflict" or "bossmod_pair" ||
               entry.Id is "bossmod" or "bossmod_reborn" or "wrath" or "rotation_solver_reborn" or "ultimate_combo";
    }

    private static string NormalizeRuleType(string? ruleType)
        => string.IsNullOrWhiteSpace(ruleType) ? string.Empty : ruleType.Trim().ToLowerInvariant();

    private static string FormatEntryNames(IReadOnlyList<PluginCatalogEntry> entries)
        => string.Join(", ", entries.Select(entry => entry.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase));

    private static AssessmentResult Inactive(PluginCatalogEntry entry, string summary)
        => new(AssessmentSeverity.Green, summary, entry.Notes);

    private static AssessmentResult Green(PluginCatalogEntry entry, string summary)
        => new(AssessmentSeverity.Green, summary, entry.Notes);

    private static AssessmentResult Yellow(PluginCatalogEntry entry, string summary)
        => new(AssessmentSeverity.Yellow, summary, entry.Notes);

    private static AssessmentResult Red(PluginCatalogEntry entry, string summary)
        => new(AssessmentSeverity.Red, summary, entry.Notes);
}

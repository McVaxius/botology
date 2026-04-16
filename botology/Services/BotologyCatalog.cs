using System;
using System.Collections.Generic;
using System.Linq;
using botology.Models;

namespace botology.Services;

public static class BotologyCatalog
{
    private static readonly string[] RotationIds =
    {
        "bossmod",
        "bossmod_reborn",
        "wrath",
        "rotation_solver_reborn",
        "ultimate_combo",
    };

    private static readonly IReadOnlyList<PluginCatalogEntry> fallbackEntries = new[]
    {
        new PluginCatalogEntry("lootgoblin", "DOW/DOM Content Bots", "LootGoblin", ["LootGoblin"], "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty does not even manage normally. This is volatile behaviour.", RedIds: ["autoduty"]),
        new PluginCatalogEntry("mogtome", "DOW/DOM Content Bots", "MOGTOME", ["MOGTOME"], "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty does not even manage normally. This is volatile behaviour.", RedIds: ["autoduty"]),
        new PluginCatalogEntry("aurafarmer", "DOW/DOM Content Bots", "AuraFarmer", ["AuraFarmer"], "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty does not even manage normally. This is volatile behaviour.", RedIds: ["autoduty"]),
        new PluginCatalogEntry("lanparty", "DOW/DOM Content Bots", "LANParty", ["LANParty"], "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty does not even manage normally. This is volatile behaviour.", RedIds: ["autoduty"]),
        new PluginCatalogEntry("cbt_vfate", "DOW/DOM Content Bots", "CBT (vfate activated)", ["CBT"], "If you use Twist of Fayte at the same time as CBT vfate there might be issues.", YellowIds: ["twist_of_fayte"]),
        new PluginCatalogEntry("twist_of_fayte", "DOW/DOM Content Bots", "Twist of Fayte", ["TwistOfFayte", "Twist of Fayte"], "If you use CBT vfate at the same time as Twist of Fayte there might be issues.", YellowIds: ["cbt_vfate"]),
        new PluginCatalogEntry("bunny_farm_eureka", "DOW/DOM Content Bots", "Bunny Farm Eureka", ["BunnyFarmEureka", "Bunny Farm Eureka"], "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty does not even manage normally. This is volatile behaviour.", RedIds: ["autoduty"]),

        new PluginCatalogEntry("accountant", "Empire Management", "Accountant", ["Accountant"], "This plugin is fine to keep if you want to use the Garden tracker. Otherwise Submarine Tracker and AutoRetainer are sufficient to cover retainer and submarine information.", YellowIds: ["autoretainer", "submarine_tracker"]),
        new PluginCatalogEntry("allagan_tools", "Empire Management", "Allagan Tools", ["AllaganTools", "Allagan Tools"], "This tool offers the same data recording as XA DB and additional tools and guides but will frequently corrupt its own database and is unreliable for long term 100% data mining of a large empire of characters and FCs.", YellowIds: ["xa_db"]),
        new PluginCatalogEntry("altoholic", "Empire Management", "Altoholic", ["Altoholic"], "You should use XA DB instead. This plugin will CTD you if you try to browse the sheets tabs. Only use this over XA DB if you like the layout.", YellowIds: ["xa_db"]),
        new PluginCatalogEntry("autoretainer", "Empire Management", "AutoRetainer", ["AutoRetainer", "Autoretainer"], "AutoDuty should be disabled or it can cause problems with checking retainers. Solution for now is to have an \"/ad stop\". If VerMaxion is installed we are often safe with AutoDuty being enabled.", YellowIds: ["autoduty"], GreenIds: ["vermaxion"]),
        new PluginCatalogEntry("submarine_tracker", "Empire Management", "Submarine Tracker", ["SubmarineTracker", "Submarine Tracker"], "There is no reason to uninstall this plugin unless you want to save space on your HD. The folder can get large.", YellowIds: ["accountant"]),
        new PluginCatalogEntry("xa_db", "Empire Management", "XA DB", ["XADB", "XA DB"], "This tool covers all of the data of Allagan Tools and Altoholic. For now it does not have auto list via tooltip for hovered item, so keep Allagan Tools if you need that feature.", YellowIds: ["allagan_tools", "altoholic"]),

        new PluginCatalogEntry("artisan", "DOH/L Content Bots", "Artisan", ["Artisan"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("autohook", "DOH/L Content Bots", "AutoHook", ["AutoHook", "Autohook"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("henchman", "DOH/L Content Bots", "Henchman", ["Henchman"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("gatherbuddy", "DOH/L Content Bots", "GatherBuddy", ["GatherBuddy"], "Pick one GatherBuddy only.", RedIds: ["gatherbuddy_reborn"]),
        new PluginCatalogEntry("gatherbuddy_reborn", "DOH/L Content Bots", "GatherBuddyReborn", ["GatherBuddyReborn"], "Pick one GatherBuddy only.", RedIds: ["gatherbuddy"]),
        new PluginCatalogEntry("icecosmic", "DOH/L Content Bots", "ICECOSMIC", ["ICECOSMIC"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("vsatisfy", "DOH/L Content Bots", "vSatisfy", ["vSatisfy", "Vsatisfy"], "It has a small problem of not auto-deactivating itself if you get stuck in an unfinished cycle, so be sure to hit stop tasks before doing something else."),

        new PluginCatalogEntry("autoduty", "Duty solving", "AutoDuty", ["AutoDuty"], "Keep this plugin disabled unless you specifically need it on. It interferes with many things without even being activated.", YellowIds: ["autoduty"]),
        new PluginCatalogEntry("ai_duty_solver", "Duty solving", "AI Duty Solver", ["AIDutySolver", "AI Duty Solver"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),

        new PluginCatalogEntry("bossmod", "Rotations/AI", "BossMod", ["BossMod"], "Ensure you are only using one rotation plugin at the same time. Pick one BossMod only.", YellowIds: RotationPeers("bossmod", "bossmod_reborn"), RedIds: ["bossmod_reborn"]),
        new PluginCatalogEntry("bossmod_reborn", "Rotations/AI", "BossModReborn", ["BossModReborn"], "Ensure you are only using one rotation plugin at the same time. Pick one BossMod only.", YellowIds: RotationPeers("bossmod_reborn", "bossmod"), RedIds: ["bossmod"]),
        new PluginCatalogEntry("wrath", "Rotations/AI", "Wrath", ["Wrath"], "Ensure you are only using one rotation plugin at the same time.", YellowIds: RotationPeers("wrath")),
        new PluginCatalogEntry("rotation_solver_reborn", "Rotations/AI", "RotationSolverReborn", ["RotationSolverReborn", "RotationSolver"], "Ensure you are only using one rotation plugin at the same time.", YellowIds: RotationPeers("rotation_solver_reborn")),
        new PluginCatalogEntry("ultimate_combo", "Rotations/AI", "UltimateCombo", ["UltimateCombo"], "Ensure you are only using one rotation plugin at the same time.", YellowIds: RotationPeers("ultimate_combo")),

        new PluginCatalogEntry("frenrider", "Utility Bots", "FrenRider", ["FrenRider"], "AutoDuty will do weird things including leaving duties early, even when not activated intentionally, and in a duty that AutoDuty does not even manage normally. This is volatile behaviour.", RedIds: ["autoduty"]),
        new PluginCatalogEntry("hellofellowhumans", "Utility Bots", "HelloFellowHumans", ["HelloFellowHumans"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game unless you want it to hahaha."),
        new PluginCatalogEntry("vermaxion", "Utility Bots", "VerMaxion", ["VERMAXION", "VerMaxion"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("vnavmesh", "Utility Bots", "VNAVMESH", ["vnavmesh", "VNAVMESH"], "Pick one navigation helper only.", RedIds: ["smartnav"]),
        new PluginCatalogEntry("smartnav", "Utility Bots", "SmartNav", ["SmartNav"], "Pick one navigation helper only.", RedIds: ["vnavmesh"]),
        new PluginCatalogEntry("visland", "Utility Bots", "VISLAND", ["VISLAND", "visland"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("xa_slave", "Utility Bots", "XA Slave", ["XASlave", "XA Slave"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("xa_hud", "Utility Bots", "XA HUD", ["XAHUD", "XA HUD"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("xa_debug", "Utility Bots", "XA Debug", ["XADebug", "XA Debug"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game."),
        new PluginCatalogEntry("something_need_doing", "Utility Bots", "Something Need Doing", ["SomethingNeedDoing", "SND"], "There is no reason to uninstall this plugin unless you want to save space on your HD. Its operations are clean and non-interfering with the rest of the game unless you want it to hahaha."),
        new PluginCatalogEntry("tracky_track", "Utility Bots", "Tracky Track", ["TrackyTrack", "Tracky Track"], "There is no reason to uninstall this plugin unless you want to save space on your HD. It does get a bit spicy in terms of storage."),
        new PluginCatalogEntry("targetpyon", "Utility Bots", "TargetPyon", ["TargetPyon"], "No Botology rule is configured yet. Treat this as a tracked utility plugin entry until stronger compatibility guidance is added.", RepoUrl: "https://github.com/priprii/FFXIVPlugins", RepoJsonUrl: "https://raw.githubusercontent.com/priprii/FFXIVPlugins/refs/heads/main/repo.json"),
        new PluginCatalogEntry("pyoncam", "Utility Bots", "PyonCam", ["PyonCam"], "No Botology rule is configured yet. Treat this as a tracked utility plugin entry until stronger compatibility guidance is added.", RepoUrl: "https://github.com/priprii/FFXIVPlugins", RepoJsonUrl: "https://raw.githubusercontent.com/priprii/FFXIVPlugins/refs/heads/main/repo.json"),

        new PluginCatalogEntry("questionable_wiggly", "Quest", "Questionable (wiggly)", ["Questionable", "QuestionableWiggly"], "These plugins have diverged already.", RedIds: ["questionable_punish"]),
        new PluginCatalogEntry("questionable_punish", "Quest", "Questionable (punish)", ["QuestionablePunish", "QuestionablePunished"], "These plugins have diverged already.", RedIds: ["questionable_wiggly"]),
    };

    public static IReadOnlyList<PluginCatalogEntry> FallbackEntries => fallbackEntries;

    public static IReadOnlyList<PluginCatalogEntry> Entries => RepositoryLinkCatalog.GetCatalogEntries(fallbackEntries);

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
            return new AssessmentResult(AssessmentSeverity.Green, summary, entry.Notes);
        }

        var redMatches = GetLoadedEntries(snapshot, entryMap, entry.RedIds);
        if (redMatches.Count > 0)
            return new AssessmentResult(AssessmentSeverity.Red, $"{FormatEntryNames(redMatches)} {BeVerb(redMatches.Count)} loaded.", entry.Notes);

        var yellowMatches = GetLoadedEntries(snapshot, entryMap, entry.YellowIds);
        if (yellowMatches.Count > 0)
            return new AssessmentResult(AssessmentSeverity.Yellow, $"{FormatEntryNames(yellowMatches)} {BeVerb(yellowMatches.Count)} loaded.", entry.Notes);

        if (entry.GreenIds is { Length: > 0 } greenIds)
        {
            var missingGreenIds = greenIds
                .Where(greenId => !snapshot.IsLoaded(ResolveMatchTokens(greenId, entryMap)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingGreenIds.Length > 0)
                return new AssessmentResult(AssessmentSeverity.Red, $"Missing required green plugins: {FormatExpectedNames(missingGreenIds, entryMap)}.", entry.Notes);

            return new AssessmentResult(AssessmentSeverity.Green, "Required green plugins are loaded.", entry.Notes);
        }

        return new AssessmentResult(AssessmentSeverity.Green, "No warning rules triggered.", entry.Notes);
    }

    private static string[] RotationPeers(string selfId, params string[] explicitRedIds)
        => RotationIds
            .Where(id => !id.Equals(selfId, StringComparison.OrdinalIgnoreCase))
            .Where(id => explicitRedIds.All(redId => !redId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    private static string BeVerb(int count)
        => count == 1 ? "is" : "are";

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

    private static string[] ResolveMatchTokens(string id, IReadOnlyDictionary<string, PluginCatalogEntry> entryMap)
        => entryMap.TryGetValue(id, out var entry)
            ? entry.MatchTokens
            : [id];

    private static string FormatEntryNames(IReadOnlyList<PluginCatalogEntry> entries)
        => string.Join(", ", entries.Select(entry => entry.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string FormatExpectedNames(IEnumerable<string> ids, IReadOnlyDictionary<string, PluginCatalogEntry> entryMap)
        => string.Join(", ", ids.Select(id => entryMap.TryGetValue(id, out var entry) ? entry.DisplayName : id));
}

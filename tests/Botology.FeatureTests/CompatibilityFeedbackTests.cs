using System.Reflection;
using System.Text.Json;
using botology.Models;
using botology.Services;
using botology.Windows;

internal static class CompatibilityFeedbackTests
{
    internal static (int Checks, IReadOnlyList<string> Failures) Check(string? masterPath = null)
    {
        var checks = 0;
        var failures = new List<string>();
        void Require(bool condition, string message) { checks++; if (!condition) failures.Add(message); }
        PluginRuntimeState Runtime(string name, bool loaded, string? display = null)
            => new(name, display ?? name, new Version(1, 0), loaded, false, null, null, new object(), null, null, null);
        var evaluate = typeof(BotologyCatalog).GetMethod("Evaluate", BindingFlags.Static | BindingFlags.NonPublic)!;
        var entry = new PluginCatalogEntry("autofategrind", "Tests", "Auto FATE Grind", ["AutoFateGrind"],
            "Fixture warning explanation.", YellowIds: ["autofategrind"]);
        PluginAssessmentRow Row(PluginCatalogEntry target, PluginSnapshot snapshot, params PluginCatalogEntry[] related)
        {
            var runtime = snapshot.FindBestMatches([target.MatchTokens]).Single();
            Require(ReferenceEquals(runtime, snapshot.FindBestMatch(target.MatchTokens)), "Indexed and direct matching must agree.");
            var map = related.Append(target).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            return new(target, runtime, (AssessmentResult)evaluate.Invoke(null, [target, runtime, snapshot, map])!, false);
        }
        foreach (var (snapshot, loaded, summary) in new[]
        {
            (PluginSnapshot.Empty, false, "Not installed."),
            (new PluginSnapshot([Runtime("AutoFateGrind", false)]), false, "Installed but disabled."),
            (new PluginSnapshot([Runtime("aUtO-FaTe_GrInD", true, "Renamed fixture")]), true, "Auto FATE Grind is loaded."),
            (new PluginSnapshot([Runtime("AutoFateGrindBeta", true)]), false, "Not installed."),
            (new PluginSnapshot([Runtime("OtherFixture", true, "Auto FATE Grind")]), true, "Auto FATE Grind is loaded."),
            (new PluginSnapshot([Runtime("AutoFateGrind", false), Runtime("Auto-Fate-Grind", true)]), true, "Auto FATE Grind is loaded."),
            (new PluginSnapshot([Runtime("Auto-Fate-Grind", true), Runtime("AutoFateGrind", false)]), true, "Auto FATE Grind is loaded."),
        })
        {
            var row = Row(entry, snapshot);
            Require(row.IsLoaded == loaded && row.Assessment.Summary == summary
                && row.Assessment.Severity == (loaded ? AssessmentSeverity.Yellow : AssessmentSeverity.Green),
                "Only an exact normalized, loaded runtime identity may trigger the loaded warning.");
            Require(loaded || BotologyCatalog.BuildAlertSummary([row]) == "All active tracked plugins are green.",
                "Absent, disabled and similarly named plugins must not produce active alerts.");
        }

        var loadedRow = Row(entry, new([Runtime("AutoFateGrind", true)]));
        Require(BotologyCatalog.BuildAlertSummary([loadedRow]).Contains("Auto FATE Grind (Yellow): Auto FATE Grind is loaded.", StringComparison.Ordinal),
            "Alerts must explain the actual loaded-warning assessment.");
        var dependency = new PluginCatalogEntry("required", "Tests", "Required dependency", ["RequiredFixture"], "");
        var dependent = entry with { Id = "dependent", DisplayName = "Dependent fixture", YellowIds = null, GreenIds = [dependency.Id] };
        var missingRow = Row(dependent, new([Runtime("AutoFateGrind", true)]), dependency);
        Require(BotologyCatalog.BuildAlertSummary([missingRow]).Contains("Missing required green plugins: Required dependency.", StringComparison.Ordinal),
            "Alerts must name the actual missing dependencies.");
        Require(BotologyCatalog.BuildAlertSummary([loadedRow with { Ignored = true }, missingRow]).StartsWith("1 non-green plugin entries:", StringComparison.Ordinal),
            "Ignored rows must remain excluded from counts and alert reasons.");
        var many = Enumerable.Range(0, 6).Select(index => loadedRow with { Entry = entry with { DisplayName = "Fixture " + index } }).ToArray();
        var bounded = BotologyCatalog.BuildAlertSummary(many);
        Require(bounded.StartsWith("6 non-green plugin entries:", StringComparison.Ordinal) && bounded.Contains("Fixture 3", StringComparison.Ordinal)
            && !bounded.Contains("Fixture 4", StringComparison.Ordinal) && bounded.EndsWith(" ...", StringComparison.Ordinal),
            "Alert details must retain the existing four-row bound and total count.");

        foreach (var language in UiText.Languages)
        {
            using var text = new UiText(language.Code, _ => throw new InvalidOperationException("Alert formatting must not draw or push a font."));
            using var locale = text.Enter();
            var alert = UiText.Alert([loadedRow, missingRow]);
            Require(alert.StartsWith(UiText.F("{0} non-green plugin entries: {1}", 2, ""), StringComparison.Ordinal)
                && alert.Contains($"Auto FATE Grind ({UiText.T("Yellow")}): ", StringComparison.Ordinal),
                "Alert totals and severity must use the selected locale: " + language.Code);
            Require(alert.Contains(UiText.F("{0} is loaded.", entry.DisplayName), StringComparison.Ordinal)
                && alert.Contains(UiText.F("Missing required green plugins: {0}.", dependency.DisplayName), StringComparison.Ordinal),
                "Actual loaded and missing-dependency reasons must be localized without translating identities: " + language.Code);
            var unavailable = new AssessmentResult(AssessmentSeverity.Red, "Unavailable for current Dalamud API 15 (manifest API 14).", "")
            {
                UiSummaryKey = "Unavailable for current Dalamud API {0} (manifest API {1}).",
                UiSummaryArguments = [15, 14],
                UiUnderlyingAssessment = missingRow.Assessment,
            };
            Require(UiText.Alert([missingRow with { Assessment = unavailable }]).Contains(
                UiText.F("Missing required green plugins: {0}.", dependency.DisplayName), StringComparison.Ordinal),
                "A patch-availability warning must retain the underlying dependency reason: " + language.Code);
            Require(UiText.Alert([loadedRow with { Ignored = true }]) == UiText.T("All active tracked plugins are green."),
                "Ignored-only alerts must retain localized all-green feedback: " + language.Code);
            const string nativeName = "Fixture (Yellow)";
            var namedRow = loadedRow with
            {
                Entry = entry with { DisplayName = nativeName },
                Assessment = loadedRow.Assessment with { Summary = nativeName + " is loaded.", UiSummaryArguments = [nativeName] },
            };
            Require(UiText.Alert([namedRow]).Contains($"{nativeName} ({UiText.T("Yellow")}): {UiText.F("{0} is loaded.", nativeName)}", StringComparison.Ordinal),
                "Structured alert formatting must preserve native names containing severity words verbatim: " + language.Code);
            Require(UiText.Alert("1 non-green plugin entries: Fixture (Yellow)")
                == UiText.F("{0} non-green plugin entries: {1}", 1, $"Fixture ({UiText.T("Yellow")})"),
                "Legacy string alert callers must retain severity localization: " + language.Code);
        }

        if (masterPath is not null)
        {
            var raw = File.ReadAllText(masterPath);
            using var document = JsonDocument.Parse(raw);
            Require(document.RootElement.GetProperty("SchemaVersion").GetInt32() == 3, "The master schema version must stay 3.");
            var parse = typeof(RepositoryLinkCatalog).GetMethod("ParseCatalogEntries", BindingFlags.Static | BindingFlags.NonPublic)!;
            var imported = (IReadOnlyList<PluginCatalogEntry>)parse.Invoke(null, [raw, BotologyCatalog.FallbackEntries])!;
            var seal = imported.Single(item => item.Id == "sealbreaker");
            Require(document.RootElement.GetProperty("Plugins").EnumerateArray().Count(item => item.GetProperty("Id").GetString() == seal.Id) == 1
                && seal.MatchTokens.SequenceEqual(["SealBreaker"]), "SealBreaker must retain one stable catalog/runtime identity.");
            Require(seal.RepoUrl == "https://github.com/ofnature/SealBreaker"
                && seal.RepoJsonUrl == "https://raw.githubusercontent.com/ofnature/Daedalus/main/repo.json", "SealBreaker must use its source and shared distribution feed.");
            Require(seal.GreenIds is not null && seal.GreenIds.SequenceEqual(["lifestream"]),
                "Only the currently verified unconditional Lifestream Start gate is a mandatory plugin rule.");
            Require(!string.IsNullOrWhiteSpace(seal.Description) && seal.Notes.Contains("Charon", StringComparison.Ordinal)
                && seal.Notes.Contains("Dashboard", StringComparison.Ordinal) && seal.Notes.Contains("tickets", StringComparison.OrdinalIgnoreCase),
                "Catalog information must explain current features, mode requirements and manual setup checks.");
            var lifestream = imported.Single(item => item.Id == "lifestream");
            Require(Row(seal, new([Runtime("SealBreaker", true), Runtime("Lifestream", true)]), lifestream).Assessment.Severity == AssessmentSeverity.Green,
                "Absent alternative runner/movement plugins must not all be treated as mandatory.");
            Require(Row(seal, new([Runtime("SealBreaker", true), Runtime("Lifestream", false)]), lifestream).Assessment.Severity == AssessmentSeverity.Red,
                "A disabled unconditional Lifestream requirement must remain visible.");
            Require(!BotologyCatalog.FallbackEntries.Any(item => item.Id == seal.Id), "Do not create a duplicate compiled SealBreaker row.");
        }
        return (checks, failures);
    }
}

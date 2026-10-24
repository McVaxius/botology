using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using botology.Models;
using botology.Services;
using botology.Windows;

Console.OutputEncoding = new UTF8Encoding(false);

if (args.FirstOrDefault() == "--compatibility-feedback")
{
    var product = typeof(BotologyCatalog).Assembly;
    Console.WriteLine($"Product {product.GetName().Version}; Dalamud reference {product.GetReferencedAssemblies().Single(item => item.Name == "Dalamud").Version}.");
    var result = CompatibilityFeedbackTests.Check(args.Skip(1).FirstOrDefault());
    foreach (var failure in result.Failures) Console.Error.WriteLine("FAIL: " + failure);
    Console.WriteLine($"Compatibility feedback: {result.Checks} checks, {result.Failures.Count} failures.");
    try { AethertekUI.Tests.BotologyUiSmoke.CheckDescriptionHover(); }
    catch (Exception exception) { Console.Error.WriteLine("FAIL: " + exception.Message); return 1; }
    return result.Failures.Count == 0 ? 0 : 1;
}

if (args.FirstOrDefault() == "--installer-media") { InstallerMediaTests.Check(); await InstallerMediaTests.CheckOwnershipAsync(); return 0; }
if (args.FirstOrDefault() == "--installer-ui") { InstallerUiTests.Check(args.Skip(1).ToArray()); return 0; }
if (args.FirstOrDefault() == "--lcars")
{
    InstallerUiTests.Check(args.Skip(1).ToArray(), lcars: true);
    AethertekUI.Tests.BotologyUiSmoke.Run(verifyBaselines: false, lcars: true);
    return 0;
}
if (args.FirstOrDefault() == "--lcars-owners") { AethertekUI.Tests.BotologyUiSmoke.Run(verifyBaselines: false, lcars: true); return 0; }
if (args.FirstOrDefault() == "--installer-performance") { InstallerUiTests.CheckPerformance(); return 0; }
if (args.FirstOrDefault() == "--installer-title") { InstallerUiTests.CheckTitle(); return 0; }
if (args.FirstOrDefault() == "--installer-discovery") { await InstallerDiscoveryTests.CheckAsync(); return 0; }

if (args.FirstOrDefault() == "--installer-core")
{
    var installerFailures = InstallerFeatureTests.Check().Concat(await InstallerActionTests.CheckAsync()).ToArray();
    foreach (var failure in installerFailures) Console.Error.WriteLine("FAIL: " + failure);
    if (installerFailures.Length == 0) Console.WriteLine("Botology installer core checks passed: source identity, pins/configuration, host snapshots, repository management and awaited native lifecycle results.");
    return installerFailures.Length == 0 ? 0 : 1;
}

if (args.FirstOrDefault() == "--confirmation-popups")
{
    AethertekUI.Tests.BotologyUiSmoke.CheckConfirmationPopups(args.Skip(1).ToArray());
    Environment.SetEnvironmentVariable("BOTOLOGY_VISUAL_CASE", "en/popup-blocking,en/popup-patch");
    Environment.SetEnvironmentVariable("BOTOLOGY_HINDI_ONLY", "1");
    AethertekUI.Tests.BotologyUiSmoke.Run(verifyBaselines: false);
    return 0;
}

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

PluginRuntimeState Runtime(string internalName, string name, bool loaded)
    => new(
        internalName,
        name,
        new Version(1, 0),
        loaded,
        false,
        null,
        null,
        new object(),
        null,
        null,
        null);

CatalogRelease Release(string id, params string[] affectedIds)
    => new()
    {
        Id = id,
        PublishedUtc = DateTimeOffset.UtcNow,
        Title = id,
        AffectedPluginIds = affectedIds,
        Sections = [new CatalogReleaseSection { Heading = "Changes", Items = ["Item"] }],
    };

var affectedEntry = new PluginCatalogEntry(
    "affected",
    "Tests",
    "Affected Plugin",
    ["AffectedInternal"],
    "No notes configured.");
var unrelatedEntry = new PluginCatalogEntry(
    "unrelated",
    "Tests",
    "Unrelated Plugin",
    ["UnrelatedInternal"],
    "No notes configured.");
var newest = Release("newest", "affected");

var disabledInstalledSnapshot = new PluginSnapshot([Runtime("AffectedInternal", "Affected Plugin", loaded: false)]);
var firstEvaluation = CatalogChangelogService.EvaluateNotifications(
    [newest],
    string.Empty,
    [affectedEntry, unrelatedEntry],
    disabledInstalledSnapshot);
Check(firstEvaluation.HasNewRelease, "First valid fetch must evaluate the newest release.");
Check(firstEvaluation.InstalledPluginNames.SequenceEqual(["Affected Plugin"]),
    "An installed-but-disabled affected plugin must match.");
Check(
    CatalogChangelogService.BuildNotificationMessage(firstEvaluation.InstalledPluginNames) ==
    "Catalog update affects installed plugins: Affected Plugin. Open Patch Notes in Botology.",
    "Notification text must use the accepted installed-plugin wording.");

var repeatedEvaluation = CatalogChangelogService.EvaluateNotifications(
    [newest],
    firstEvaluation.NewestReleaseId,
    [affectedEntry],
    disabledInstalledSnapshot);
Check(!repeatedEvaluation.HasNewRelease && repeatedEvaluation.InstalledPluginNames.Count == 0,
    "A force reload must not replay an already processed release.");

var noMatchEvaluation = CatalogChangelogService.EvaluateNotifications(
    [newest],
    string.Empty,
    [affectedEntry],
    new PluginSnapshot([Runtime("UnrelatedInternal", "Unrelated Plugin", loaded: true)]));
Check(noMatchEvaluation.HasNewRelease && noMatchEvaluation.InstalledPluginNames.Count == 0,
    "A new release with no installed match must advance without a toast target.");

var multiMessage = CatalogChangelogService.BuildNotificationMessage(["A", "B", "C", "D", "E"]);
Check(multiMessage == "Catalog update affects installed plugins: A, B, C (+2 more). Open Patch Notes in Botology.",
    "Notification text must cap displayed names at three and report the remainder.");

var toStoredEntry = typeof(RepositoryLinkCatalog).GetMethod(
    "ToStoredEntry",
    BindingFlags.Static | BindingFlags.NonPublic);
Check(toStoredEntry != null, "Catalog storage conversion must remain available to the feature harness.");
if (toStoredEntry != null)
{
    var falseEntry = affectedEntry with { IsAiAttributed = false };
    var masterStored = toStoredEntry.Invoke(null, [falseEntry, false]);
    var overlayStored = toStoredEntry.Invoke(null, [falseEntry, true]);
    var trueStored = toStoredEntry.Invoke(null, [affectedEntry with { IsAiAttributed = true }, false]);
    var storedProperty = masterStored?.GetType().GetProperty("IsAiAttributed");
    Check(storedProperty?.GetValue(masterStored) == null,
        "Master/export conversion must omit false AI values.");
    Check(storedProperty?.GetValue(overlayStored) is false,
        "Overlay conversion must preserve an explicit false AI value.");
    Check(storedProperty?.GetValue(trueStored) is true,
        "Storage conversion must preserve true AI values.");
}

var compareRows = typeof(MainWindow).GetMethod("CompareRows", BindingFlags.Static | BindingFlags.NonPublic)!;
var discordEntry = affectedEntry with { DiscordUrl = "https://discord.gg/ExampleInvite" };
Check(affectedEntry.DiscordUrl == null, "Existing catalog entries must default to no Discord URL.");
foreach (var url in new[] { "https://discord.gg/ExampleInvite", "https://discord.com/invite/ExampleInvite", "https://canary.discord.com/channels/1/2" })
    Check(RepositoryLinkResolver.NormalizeDiscordUrl("  " + url + "  ") == url, "Supplied Discord URLs must retain invite case and trim surrounding whitespace.");
foreach (var url in new[] { "", "javascript:alert(1)", "file:///C:/sample", "https://discord.gg.example.com/invite", "https://example.com/discord", "https://user:password@discord.com/invite/test", "http://discord.gg/test" })
    Check(RepositoryLinkResolver.NormalizeDiscordUrl(url) == null, "Invalid or non-Discord URLs must not become link actions.");
var catalogOptions = (System.Text.Json.JsonSerializerOptions)typeof(RepositoryLinkCatalog)
    .GetField("JsonOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
var parseCatalog = typeof(RepositoryLinkCatalog).GetMethod("ParseCatalogEntries", BindingFlags.Static | BindingFlags.NonPublic)!;
var bundledCatalog = (IReadOnlyList<PluginCatalogEntry>)parseCatalog.Invoke(null,
    [File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "plugin-repository-links.json")), BotologyCatalog.FallbackEntries])!;
foreach (var (id, internalName, category) in new[]
{
    ("autohouselottery", "AutoHouseLottery", "Housing"),
    ("autoseller", "AutoSeller", "Market Bots"),
})
{
    var matches = bundledCatalog.Where(entry => entry.Id == id).ToArray();
    Check(matches.Length == 1, "Each selected catalog addition must have one stable ID.");
    if (matches.Length != 1) continue;
    var added = matches[0];
    Check(added.MatchTokens.SequenceEqual([internalName]) && added.Category == category,
        "Selected catalog entries must match their exact runtime name and functional category.");
    Check(new PluginSnapshot([Runtime(internalName, internalName, loaded: true)]).FindBestMatch(added.MatchTokens) != null,
        "Selected catalog entries must recognize the installed runtime name.");
    Check(added.GreenIds is not { Length: > 0 } && added.YellowIds is not { Length: > 0 }
        && added.RedIds is not { Length: > 0 }, "Catalog additions must not introduce compatibility rules.");
    Check(!string.IsNullOrWhiteSpace(added.Description) && !string.IsNullOrWhiteSpace(added.Notes),
        "Selected catalog entries must retain functional descriptions.");
}
var addedIds = new[] { "autohouselottery", "autoseller" };
var buildMigrationOverlay = typeof(RepositoryLinkCatalog).GetMethod("BuildOverlayEntriesLocked", BindingFlags.Static | BindingFlags.NonPublic)!;
var migrationAdditions = (IReadOnlyList<PluginCatalogEntry>)buildMigrationOverlay.Invoke(null,
    [bundledCatalog, bundledCatalog.Where(entry => !addedIds.Contains(entry.Id)).ToArray()])!;
Check(migrationAdditions.Select(entry => entry.Id).Order().SequenceEqual(addedIds),
    "Initial catalog migration must preserve exactly the selected additions when the master lacks them.");
var storedAdditions = migrationAdditions.Select(entry => toStoredEntry!.Invoke(null, [entry, true])).ToArray();
var restoredAdditions = (IReadOnlyList<PluginCatalogEntry>)parseCatalog.Invoke(null,
    [System.Text.Json.JsonSerializer.Serialize(new { Plugins = storedAdditions }, catalogOptions), Array.Empty<PluginCatalogEntry>()])!;
Check(restoredAdditions.Select(entry => entry.Id).Order().SequenceEqual(addedIds)
    && restoredAdditions.All(entry => entry.MatchTokens.SequenceEqual(bundledCatalog.Single(original => original.Id == entry.Id).MatchTokens)),
    "Catalog editor storage must round-trip the selected additions and runtime names.");
var discordStored = toStoredEntry!.Invoke(null, [discordEntry, true])!;
var discordJson = System.Text.Json.JsonSerializer.Serialize(new { SchemaVersion = 3, Plugins = new[] { discordStored } }, catalogOptions);
var restoredDiscord = (IReadOnlyList<PluginCatalogEntry>)parseCatalog.Invoke(null, [discordJson, Array.Empty<PluginCatalogEntry>()])!;
Check(restoredDiscord.Single().DiscordUrl == discordEntry.DiscordUrl, "Native catalog storage/reload must preserve the optional Discord URL.");
var compareEntries = typeof(RepositoryLinkCatalog).GetMethod("EntriesEquivalent", BindingFlags.Static | BindingFlags.NonPublic)!;
Check(!(bool)compareEntries.Invoke(null, [discordEntry, discordEntry with { DiscordUrl = "https://discord.gg/exampleinvite" }])!,
    "Case-sensitive Discord invite edits must count as catalog changes.");
var draftType = typeof(CatalogEditorWindow).GetNestedType("CatalogEntryDraft", BindingFlags.NonPublic)!;
var discordDraft = draftType.GetMethod("FromEntry")!.Invoke(null, [discordEntry, true])!;
var replacementDraft = draftType.GetMethod("CreateIdReplacement")!.Invoke(null, [discordDraft])!;
Check((string?)draftType.GetField("DiscordUrl")!.GetValue(replacementDraft) == discordEntry.DiscordUrl,
    "Replacing an entry ID must preserve the draft's Discord URL.");
var savedDraft = (PluginCatalogEntry)draftType.GetMethod("ToEntry")!.Invoke(discordDraft, null)!;
Check(savedDraft.DiscordUrl == discordEntry.DiscordUrl, "Editor save conversion must preserve the supplied Discord URL.");

var gridColumn = typeof(MainWindow).GetNestedType("GridColumn", BindingFlags.NonPublic)!;
int Compare(PluginAssessmentRow left, PluginAssessmentRow right, string column)
    => (int)compareRows.Invoke(null, [left, right, Enum.Parse(gridColumn, column)])!;
var oldVersion = new PluginRuntimeState("Old", "Old", new Version(1, 9), true, false, null, null, new object(), null, null, null);
var newVersion = new PluginRuntimeState("New", "New", new Version(1, 10), false, true, null, null, new object(), null, null, null);
var oldRow = new PluginAssessmentRow(affectedEntry, oldVersion, new(AssessmentSeverity.Green, "OK", ""), false);
var newRow = new PluginAssessmentRow(unrelatedEntry, newVersion, new(AssessmentSeverity.Red, "Warning", ""), false);
var presentationCopy=oldRow.Assessment with { UiSummaryKey="No warning rules triggered.",UiSummaryArguments=["presentation only"] };
Check(oldRow.Assessment==presentationCopy && oldRow.Assessment.GetHashCode()==presentationCopy.GetHashCode(),
    "Localized presentation metadata must not change assessment domain equality.");
Check(Compare(oldRow, newRow, "Installed") < 0, "Installed versions must sort numerically: 1.9 precedes 1.10.");
Check(Compare(oldRow with { RuntimeState = null }, newRow, "Installed") < 0, "Missing installed versions must remain sortable.");
Check(Compare(oldRow, newRow, "Update") < 0, "Update sorting must use availability from the captured runtime state.");
Check(Compare(oldRow, newRow, "Enabled") > 0, "Enabled sorting must reflect loaded state.");
Check(Compare(oldRow, newRow, "Category") < 0, "Rows in one category must sort by plugin name.");
Check(Compare(oldRow, newRow with { RuntimeState = oldVersion }, "Notes") < 0, "Assessment sorting must preserve severity order.");
Check(Compare(oldRow, oldRow with { Ignored = true }, "Ignore") < 0, "Ignore sorting must reflect the saved value.");
Check(Compare(oldRow, newRow, "Author") == 0, "Missing optional metadata must remain sortable.");

var tempDirectory = Path.Combine(Path.GetTempPath(), $"botology-feature-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDirectory);
try
{
    const string validJson = """
        {
          "SchemaVersion": 1,
          "Releases": [
            {
              "Id": "cached-release",
              "PublishedUtc": "2026-07-22T00:00:00Z",
              "Title": "Cached release",
              "AffectedPluginIds": ["affected"],
              "Sections": [{"Heading": "Changes", "Items": ["Item"]}]
            }
          ]
        }
        """;

    using (var initialClient = new HttpClient(new FixedResponseHandler(HttpStatusCode.OK, validJson)))
    using (var initialService = new CatalogChangelogService(tempDirectory, initialClient, (_, _) => { }))
    {
        var result = await initialService.RefreshAsync();
        Check(result.HasValidRemoteData && initialService.GetReleases().Count == 1,
            "A valid remote changelog must populate memory and cache.");
    }

    using (var failedClient = new HttpClient(new FixedResponseHandler(HttpStatusCode.ServiceUnavailable, string.Empty)))
    using (var failedService = new CatalogChangelogService(tempDirectory, failedClient, (_, _) => { }))
    {
        Check(failedService.GetReleases().Count == 1,
            "A new service instance must load the last valid cache.");
        var result = await failedService.RefreshAsync();
        Check(!result.HasValidRemoteData && failedService.GetReleases().Single().Id == "cached-release",
            "A fetch failure must retain the prior valid cache.");
    }

    using (var invalidClient = new HttpClient(new FixedResponseHandler(HttpStatusCode.OK, "{}")))
    using (var invalidService = new CatalogChangelogService(tempDirectory, invalidClient, (_, _) => { }))
    {
        var result = await invalidService.RefreshAsync();
        Check(!result.HasValidRemoteData && invalidService.GetReleases().Single().Id == "cached-release",
            "Invalid remote data must retain the prior valid cache and remain non-processable.");
    }
}
finally
{
    Directory.Delete(tempDirectory, recursive: true);
}

failures.AddRange(InstallerFeatureTests.Check());
failures.AddRange(CompatibilityFeedbackTests.Check().Failures);
failures.AddRange(await InstallerActionTests.CheckAsync());
if (failures.Count > 0)
{
    foreach (var failure in failures)
        Console.Error.WriteLine($"FAIL: {failure}");
    return 1;
}

await InstallerDiscoveryTests.CheckAsync();
InstallerMediaTests.Check();
await InstallerMediaTests.CheckOwnershipAsync();
InstallerUiTests.CheckTitle();
InstallerUiTests.Check();
AethertekUI.Tests.BotologyUiSmoke.CheckDescriptionHover();
AethertekUI.Tests.BotologyUiSmoke.Run();
Console.WriteLine("Botology feature tests passed: notification matching, duplicate prevention, AI storage, changelog cache fallback, grid sorting, and native main-window rendering.");
return 0;

sealed class FixedResponseHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        });
}

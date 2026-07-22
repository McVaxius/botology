using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using botology.Models;
using botology.Services;

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

if (failures.Count > 0)
{
    foreach (var failure in failures)
        Console.Error.WriteLine($"FAIL: {failure}");
    return 1;
}

Console.WriteLine("Botology feature tests passed: notification matching, duplicate prevention, and changelog cache fallback.");
return 0;

sealed class FixedResponseHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        });
}

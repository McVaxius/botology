using botology;
using botology.Models;
using botology.Services;
using botology.Windows;
using Newtonsoft.Json;

internal static class InstallerFeatureTests
{
    public static IReadOnlyList<string> Check()
    {
        var failures = new List<string>();
        void Require(bool condition, string message) { if (!condition) failures.Add("Installer: " + message); }
        const string official = InstallerCatalog.OfficialRepository;
        const string third = "https://example.invalid/Plugins/feed.json?channel=Stable";
        const string other = "https://example.invalid/plugins/feed.json?channel=Stable";
        var fresh=Configuration.FromSaved(null);
        Require(fresh.PinnedRepositories.SequenceEqual([InstallerCatalog.DefaultPinnedRepository]) && new Configuration().PinnedRepositories.Count==0,
            "Only fresh installation initialization seeds the default; ordinary deserialization defaults stay empty.");
        var freshSnapshot=InstallerCatalog.Merge(InstallerHostSnapshot.Empty,[],[],[]);
        Require(InstallerCatalog.VisiblePinnedRepositories(fresh,freshSnapshot).Single().Url==InstallerCatalog.DefaultPinnedRepository
            && freshSnapshot.Repositories.Single().IsAdded==false && freshSnapshot.Entries.Count==0,
            "The initial favorite must resolve as a known unadded source without subscribing/installing.");
        fresh.PinnedRepositories.Add(third);
        InstallerCatalog.ToggleRepositoryPin(fresh,InstallerCatalog.DefaultPinnedRepository);
        var unpinned=Configuration.FromSaved(JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(fresh)));
        Require(unpinned.PinnedRepositories.SequenceEqual([third]) && ReferenceEquals(Configuration.FromSaved(unpinned),unpinned),
            "Save/load/reload must retain removal and unrelated favorites.");
        var emptyPins=JsonConvert.DeserializeObject<Configuration>("{\"PinnedRepositories\":[],\"privateFutureField\":7}")!;
        Require(ReferenceEquals(Configuration.FromSaved(emptyPins),emptyPins) && emptyPins.PinnedRepositories.Count==0
            && emptyPins.AdditionalSettings!.ContainsKey("privateFutureField")
            && Configuration.FromSaved(JsonConvert.DeserializeObject<Configuration>("{}")!).PinnedRepositories.Count==0,
            "Existing empty and legacy saved configurations are authoritative, including unknown fields.");
        for(var refresh=0;refresh<3;refresh++)
            Require(InstallerCatalog.VisiblePinnedRepositories(Configuration.FromSaved(emptyPins),InstallerCatalog.Merge(InstallerHostSnapshot.Empty,[],[],[])).Count==0,
                "Catalog refresh/reload must not recreate a removed default favorite.");
        var categoryFeed=InstallerCatalog.ParseFeed("""
            [{"InternalName":"TagsA","Tags":["Utility"," chat ","",null,1],"CategoryTags":[" utility ","UI","Housing"]},
             {"InternalName":"TagsB","Tags":["Housing"],"CategoryTags":null}]
            """,third);
        var categories=InstallerCatalog.Merge(InstallerHostSnapshot.Empty,categoryFeed.Append(new("OfficialTags","Official tags",official,Tags:["Official only","UI"])),
            [new("tags-a"," Housing ","Tag A",["T-a-g-s-A"],""),new("not-discovered","New category","Absent",["Absent"],"")],[]);
        Require(categoryFeed[0].CategoryTags!.SequenceEqual([" utility ","UI","Housing"])
            && categories.DalamudTags!.SequenceEqual(["Official only","UI"]) && categories.ThirdPartyTags!.Count==4 && categories.ThirdPartyTags.Contains("chat")
            && !categories.DalamudTags!.Contains("chat") && !categories.ThirdPartyTags!.Contains("Official only") && categories.ThirdPartyTags.Contains("UI")
            && categories.Entries.Single(entry=>entry.Manifest.InternalName=="TagsA").ManifestTags!.Count==4
            && categories.Entries.Single(entry=>entry.Manifest.InternalName=="TagsA") is { BotologyCategory:"Housing",Compatibility:null }
            && categories.BotologyCategories!.SequenceEqual(["Housing","New category"]),
            "Category metadata must keep official/third-party vocabularies separate by feed (including unadded feeds and shared labels), normalize both manifest fields, and match effective catalog categories without fabricating assessments.");
        var officialArt=new InstallerManifest("Fixture","Fixture",official,Dip17Channel:"stable");
        Require(officialArt.PublisherIconUrl=="https://raw.githubusercontent.com/goatcorp/PluginDistD17/main/stable/Fixture/images/icon.png"
            && officialArt.PublisherImageUrls.Length==5 && officialArt.PublisherImageUrls[0].EndsWith("/image1.png",StringComparison.Ordinal),
            "Official artwork must use the host's authoritative distribution channel route.");
        Require(InstallerCatalog.ParseFeed("[{\"InternalName\":\"Fixture\",\"_Dip17Channel\":\"stable\"}]",official).Single().PublisherIconUrl==officialArt.PublisherIconUrl,
            "Feed parsing must retain the official image channel.");
        var thirdArt=officialArt with { RepositoryUrl=third,IconUrl="https://fixture.invalid/icon.png",ImageUrls=["https://fixture.invalid/screenshot.png"] };
        Require(thirdArt.PublisherIconUrl==thirdArt.IconUrl && thirdArt.PublisherImageUrls.SequenceEqual(thirdArt.ImageUrls!)
            && (thirdArt with { IconUrl=null,ImageUrls=null }).PublisherIconUrl is null,
            "Third-party artwork must retain publisher URLs and never inherit official or Botology artwork.");
        Require(InstallerCatalog.NormalizeRepository(" HTTPS://EXAMPLE.INVALID/Plugins/feed.json?channel=Stable ") == third,
            "Feed identity must normalize host/scheme without changing path/query case.");
        Require(InstallerCatalog.NormalizeRepository(other) != third, "Case-sensitive feed paths must remain distinct.");
        var feeds=new[]{"https://raw.githubusercontent.com/sample-owner/tools/main/plugins.json",
            "https://raw.githubusercontent.com/sample-owner/tools/testing/plugins.json",
            "https://raw.githubusercontent.com/sample-owner/tools/main/plugins.json?track=Preview",
            "https://raw.githubusercontent.com/sample-owner/tools/main/plugins.json?track=preview",
            "https://raw.githubusercontent.com/other-owner/tools/main/plugins.json",
            "https://raw.githubusercontent.com/sample-owner/tools/main/alternate.json",
            "https://raw.githubusercontent.com/sample-owner/tools/main/plugins.json?track=A%2FB",
            "https://raw.githubusercontent.com/sample-owner/tools/main/plugins.json?track=A/B"};
        var namedSources=InstallerCatalog.Merge(new([],[],[],true,false),feeds.Select(url=>new InstallerManifest("Same","Same",url)),[],[]);
        var feedSources=namedSources.Repositories.Where(repo=>feeds.Contains(repo.Url)).ToArray();
        Require(feedSources.Select(r=>r.Name).Distinct(StringComparer.Ordinal).Count()==feeds.Length
            && feedSources.All(r=>r.Name.Contains("/tools",StringComparison.Ordinal) && !r.Name.Contains("raw.githubusercontent",StringComparison.Ordinal)),
            "GitHub feed labels must expose owner/repository and distinguish branches, feed paths and case-sensitive query values.");
        Require(InstallerCatalog.RepositoryName(official)=="Dalamud" && InstallerCatalog.RepositoryName("https://source.invalid/feed.json")=="source.invalid"
            && InstallerCatalog.RepositoryName("https://github.com/sample-owner/tools/raw/main/plugins.json")=="sample-owner/tools",
            "Official, dedicated-host and GitHub URL labels must retain their actual source provenance.");
        Require(namedSources.Entries.Select(e=>e.Identity).Distinct(StringComparer.Ordinal).Count()==feeds.Length
            && InstallerCatalog.VisiblePinnedRepositories(new Configuration { PinnedRepositories=[feeds[0]] },namedSources).Single().Url==feeds[0],
            "Readable source labels must not merge entries, rewrite URLs or invalidate existing pins.");
        Require(InstallerCatalog.NormalizeRepository("file:///repo.json") is null
            && InstallerCatalog.NormalizeRepository("https://user:secret@example.invalid/feed") is null,
            "Non-HTTP and credential-bearing values are not repository pins.");

        var localHandle = new
        {
            IsDev = false, IsThirdParty = false, IsTesting = false, IsBanned = false, IsOutdated = false, State = "Loaded",
            Manifest = new { InstalledFromUrl = "OFFICIAL", ScheduledForDeletion = false },
        };
        var runtime = new PluginRuntimeState("Same", "Installed official", new(1, 0), true, true,
            "https://project.invalid/source", null, localHandle, null, null, null);
        var native = new InstallerInstalledPlugin(runtime, official, "Loaded");
        var hostManifest = new InstallerManifest("Same", "Official plugin", official, "Alex, Sam", ApiLevel: 15);
        var host = new InstallerHostSnapshot([new(official, "Dalamud", true, true, true, "Success")], [hostManifest], [native], true, false);
        var external = new InstallerManifest("Same", "Third-party plugin", third, "Other author", ApiLevel: 15);
        var extra = new InstallerManifest("NewPlugin", "New plugin", third, "Sam", ApiLevel: 15);
        var catalog = new[] { new PluginCatalogEntry("same", "Utility", "Same", ["Same"], "Rule", RepoJsonUrl: third) };
        var assessment = new PluginAssessmentRow(catalog[0], runtime, new(AssessmentSeverity.Yellow, "Existing warning", ""), false);
        var snapshot = InstallerCatalog.Merge(host, [external, extra, hostManifest with { Name = "Stale name" }], catalog, [assessment]);
        Require(snapshot.Entries.Count == 3, "Discovery must retain duplicate names from distinct sources and deduplicate the same source.");
        var installed = snapshot.Entries.Single(e => e.Manifest.RepositoryUrl == official);
        var collision = snapshot.Entries.Single(e => e.Manifest.RepositoryUrl == third && e.Manifest.InternalName == "Same");
        Require(installed.IsInstalled && installed.IsLoaded && installed.Manifest.Name == "Official plugin", "Authoritative host metadata/state must win for its source.");
        Require(!collision.IsInstalled && collision.NameCollision && !collision.CanInstall && collision.Compatibility?.RuntimeState is null,
            "Another source must not inherit installed state or silently replace the installed plugin.");
        Require(!snapshot.Repositories.Single(r => r.Url == third).IsAdded && !snapshot.Entries.Single(e => e.Manifest.InternalName == "NewPlugin").CanInstall,
            "Browsing an unadded feed must not subscribe or enable installation.");

        var configuration = new Configuration
        {
            PinnedRepositories = [third, "https://no-longer-known.invalid/feed.json", "invalid pin"],
            PinnedAuthors = ["sam", "Removed author", "  ", "SAM"],
        };
        Require(InstallerCatalog.VisiblePinnedRepositories(configuration, snapshot).Select(r => r.Url).SequenceEqual([third]),
            "Known unadded feeds stay pinned; invalid/unresolved pins have no visible placeholder.");
        Require(InstallerCatalog.VisiblePinnedAuthors(configuration, snapshot).SequenceEqual(["sam"]),
            "Author pins resolve individual credits case-insensitively and omit invalid/duplicate entries.");
        Require(configuration.PinnedRepositories.Count == 3 && configuration.PinnedAuthors.Count == 4,
            "Hiding invalid pins must not delete stored choices.");
        var lastKnown = InstallerCatalog.Merge(host, [], [], [assessment with { Metadata = new PluginRepositoryMetadata { Author = "Cached author" } }]);
        Require(InstallerCatalog.VisiblePinnedAuthors(new Configuration { PinnedAuthors = ["Cached author"] }, lastKnown).Count == 1,
            "Last-valid existing author metadata must resolve a pin while feed discovery is unavailable.");
        InstallerCatalog.ToggleRepositoryPin(configuration, third);
        Require(InstallerCatalog.VisiblePinnedRepositories(configuration, snapshot).Count == 0 && configuration.PinnedRepositories.Count == 2,
            "Unpin must affect only the selected source identity.");
        InstallerCatalog.ToggleAuthorPin(configuration, "SaM");
        Require(InstallerCatalog.VisiblePinnedAuthors(configuration, snapshot).Count == 0,
            "Unpin removes duplicate spellings of the selected author, not unrelated saved pins.");
        var json = JsonConvert.SerializeObject(new Configuration { PinnedRepositories = [third], PinnedAuthors = ["Sam"], UiLanguage = "fr" });
        var restored = JsonConvert.DeserializeObject<Configuration>(json)!;
        Require(restored.PinnedRepositories.SequenceEqual([third]) && restored.PinnedAuthors.SequenceEqual(["Sam"]) && restored.UiLanguage == "fr",
            "Existing configuration serialization must round-trip pins and unrelated appearance choices.");
        var legacy = JsonConvert.DeserializeObject<Configuration>("{\"UiLanguage\":\"de\",\"privateFutureField\":7}")!;
        Require(legacy.PinnedAuthors.Count == 0 && legacy.PinnedRepositories.Count == 0 && legacy.UiLanguage == "de"
            && legacy.AdditionalSettings!.ContainsKey("privateFutureField"), "Legacy/unknown settings must survive the new pin defaults.");

        const string feed = """
            {"plugins":[
              {"InternalName":"Media","Name":"Media plugin","Author":"Alex; Sam","AssemblyVersion":"1.2.3","DalamudApiLevel":15,
               "IconUrl":"https://example.invalid/icon.png","ImageUrls":["https://example.invalid/screen.png","https://example.invalid/preview.gif"],"Tags":["utility"]},
              {"InternalName":"Testing","DalamudApiLevel":15,"IsTestingExclusive":true},
              {"InternalName":"Hidden","DalamudApiLevel":15,"IsHide":true},
              {"InternalName":"BadApi","DalamudApiLevel":"not-a-number"},
              {"Name":"Missing internal name"},null
            ]}
            """;
        var parsed = InstallerCatalog.ParseFeed(feed, third);
        Require(parsed.Count == 4 && parsed[0].ImageUrls!.Length == 2 && parsed[0].Authors.SequenceEqual(["Alex", "Sam"]),
            "Feed discovery must retain publisher screenshots/GIF URLs and credits while rejecting nameless records.");
        var addedHost = host with
        {
            Repositories = [new(third, "Example", true, true, false, "Success")],
            Manifests = parsed,
            Installed = [],
        };
        var eligible = InstallerCatalog.Merge(addedHost, [], [], []);
        Require(eligible.Entries.Single(e => e.Manifest.InternalName == "Media").CanInstall,
            "An actual available, compatible host manifest from an enabled added source is installable.");
        var installable=eligible.Entries.Single(e=>e.Manifest.InternalName=="Media");
        var blocked=new[]{installable with { NameCollision=true },installable with { HostReady=false },installable with { HostSafeMode=true },
            installable with { AvailableFromHost=false },installable with { Repository=installable.Repository with { IsAdded=false } },
            installable with { Repository=installable.Repository with { IsEnabled=false } },installable with { Repository=installable.Repository with { Status="Fail" } },
            installable with { Manifest=installable.Manifest with { ApiLevel=14 } },installable with { Manifest=installable.Manifest with { IsTestingExclusive=true } },
            installable with { Manifest=installable.Manifest with { IsHidden=true } },installable with { Installed=native }};
        using(var text=new UiText("en",_=>throw new InvalidOperationException()))
        using(var locale=text.Enter())
        {
            var state=typeof(MainWindow).GetMethod("EntryState",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
            Require(blocked.All(e=>!e.CanInstall && !string.IsNullOrWhiteSpace(e.InstallBlockReason) && (string)state.Invoke(null,[e])! != "Available")
                && installable.InstallBlockReason is null && (string)state.Invoke(null,[installable])! == "Available",
                "Every install blocker must have an honest displayed reason; Available must agree with the offered eligible action.");
        }
        Require(eligible.Entries.Where(e => e.Manifest.InternalName != "Media").All(e => !e.CanInstall),
            "Testing-only, hidden and unknown-API records must not offer stable installation.");
        Require(InstallerCatalog.Merge(addedHost with { SafeMode = true }, [], [], []).Entries.All(e => !e.CanInstall)
            && InstallerCatalog.Merge(addedHost with { Ready = false }, [], [], []).Entries.All(e => !e.CanInstall),
            "Safe mode and incomplete host readiness must disable install eligibility.");
        var blockedUpdates=new[]{installed with { HostReady=false },installed with { HostSafeMode=true },installed with { Installed=null },
            installed with { AvailableFromHost=false },installed with { Repository=installed.Repository with { IsAdded=false } },
            installed with { Repository=installed.Repository with { IsEnabled=false } },installed with { Repository=installed.Repository with { Status="Fail" } },
            installed with { Manifest=installed.Manifest with { ApiLevel=14 } },installed with { Installed=native with { IsDevelopment=true } },
            installed with { Installed=native with { IsBanned=true } }};
        Require(installed.CanUpdate && blockedUpdates.All(entry=>!entry.CanUpdate)
            && (installed with { Installed=native with { IsTesting=true },Manifest=installed.Manifest with { IsTestingExclusive=true,IsHidden=true } }).CanUpdate,
            "Update eligibility must retain every host/source/API/development/banned gate while preserving native testing updates.");
        string[] updateLabels=["Update all","Updates every eligible plugin, including plugins hidden by the current filters.",
            "No eligible updates.","Completed {0} updates.","Update all stopped after {0} of {1} updates: {2}. {3}",
            "No eligible update is available from the installed source."];
        foreach(var (language,_) in UiText.Languages)
        {
            using var text=new UiText(language,_=>throw new InvalidOperationException());
            foreach(var label in updateLabels)
            {
                var translated=text.Resources.GetString(label,true);
                Require(!string.IsNullOrWhiteSpace(translated),"Manual update action/result localization must exist in "+language+": "+label);
                if(translated is null) continue;
                var expected=System.Text.RegularExpressions.Regex.Matches(label,@"\{\d+\}").Select(match=>match.Value).Order().ToArray();
                var actual=System.Text.RegularExpressions.Regex.Matches(translated,@"\{\d+\}").Select(match=>match.Value).Order().ToArray();
                Require(actual.SequenceEqual(expected),"Manual update result placeholders must retain counts, plugin and native failure in "+language+".");
                _=string.Format(text.Culture,translated,1,2,"Fixture (https://example.invalid/feed.json)","FailedDownload");
            }
        }

        var repoHandle = new { PluginMasterUrl = official, IsEnabled = true, IsThirdParty = false, State = "Success" };
        var manager = new
        {
            Repos = new[] { repoHandle },
            AvailablePlugins = new[] { new { InternalName = "Same", Name = "Native", Author = "Sam", SourceRepo = repoHandle,
                AssemblyVersion = new Version(1, 2), DalamudApiLevel = 15, Dip17Channel="stable", ImageUrls = new[] { "https://example.invalid/image.png" },Tags=new[]{"chat"},CategoryTags=new[]{"UI"} } },
            PluginsReady = true, ReposReady = true, SafeMode = false,
        };
        var captured = PluginManagerBridge.CaptureInstallerHost(manager,
            new { ThirdRepoList = new[] { new { Url = third, IsEnabled = false } } }, new([runtime]));
        Require(captured.Ready && captured.Installed.Single().RepositoryUrl == official
            && captured.Repositories.Single(r => r.Url == third) is { IsAdded: true, IsEnabled: false }
            && captured.Manifests.Single().ImageUrls!.Length == 1 && captured.Manifests.Single().Dip17Channel=="stable"
            && captured.Manifests.Single().CategoryTags!.SequenceEqual(["UI"]) && captured.Manifests.Single().Tags!.SequenceEqual(["chat"])
            && captured.Manifests.Single().PublisherIconUrl!.EndsWith("/stable/Same/images/icon.png",StringComparison.Ordinal),
            "Host capture must retain official origin, disabled-added repos and publisher media without touching the host.");
        return failures;
    }
}

using System.Numerics;
using AethertekUI;
using AethertekUI.Tests;
using botology.Models;
using botology.Services;
using botology.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

internal static class InstallerUiTests
{
    internal static unsafe void CheckPerformance()
    {
        var identityCases=InstallerCatalog.Merge(InstallerHostSnapshot.Empty,new[]{"https://sorting.invalid/a","https://sorting.invalid/aa","https://sorting.invalid/a?c=A","https://sorting.invalid/A"}
            .SelectMany(url=>new[]{"Fixture"," fixture ","İ","ı","K","σ","ς","ß","𐐨"}.Select(name=>new InstallerManifest(name,"Same title",url))),[],[]).Entries;
        var identityCompare=typeof(MainWindow).GetMethod("CompareInstallerIdentity",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        foreach(var left in identityCases) foreach(var right in identityCases)
            if(Math.Sign((int)identityCompare.Invoke(null,[left,right])!)!=Math.Sign(StringComparer.Ordinal.Compare(left.Identity,right.Identity)))
                throw new Exception("Allocation-free identity sorting changed canonical source/name ordering.");
        foreach(var count in new[]{250,5000})
        {
            const string source="https://benchmark.invalid/plugins.json";
            var manifests=Enumerable.Range(0,count).Select(i=>new InstallerManifest("Bench"+i,"Plugin "+i.ToString("00000")+(i==count/2?"\nA local multiline title outlier":""),
                i%2==0?InstallerCatalog.OfficialRepository:source,"SampleAuthor","Publisher description.","1.0",15,Tags:["Utility","Tag "+i.ToString("00000"),"Unicode हिन्दी caption that stays readable"],CategoryTags:["UI"," utility "])).ToArray();
            var assessments=Enumerable.Range(0,1000).Select(i=>new PluginAssessmentRow(new("bench-"+i,"Category "+(i%400).ToString("00000"),"Plugin "+i,
                i==1?["Bench1","LegacyAlias1","B-E-N-C-H-0"]:["Bench"+i,"LegacyAlias"+i],"Fixture notes."),null,new(AssessmentSeverity.Green,"Not installed.",""),false)).ToArray();
            var runtimeSnapshot=new PluginSnapshot(manifests.Take(250).Select((manifest,i)=>new PluginRuntimeState(manifest.InternalName,manifest.Name,
                new(1,0),i%2==0,false,null,null,new object(),null,null,null)).ToArray());
            var matchingClock=System.Diagnostics.Stopwatch.StartNew();var matchingAllocated=GC.GetAllocatedBytesForCurrentThread();
            var runtimeMatches=runtimeSnapshot.FindBestMatches(assessments.Select(row=>row.Entry.MatchTokens));
            var matches=runtimeMatches.Count(match=>match is not null);
            var matchingMilliseconds=matchingClock.Elapsed.TotalMilliseconds;var matchingBytes=GC.GetAllocatedBytesForCurrentThread()-matchingAllocated;
            Console.WriteLine($"Title assessment matching: 1000 entries/250 runtime plugins; {matchingMilliseconds:F2} ms/{matchingBytes:N0} bytes.");
            if(matches!=250) throw new Exception("Title matching workload lost normalized runtime identities.");
            if(!runtimeMatches.SequenceEqual(assessments.Select(row=>runtimeSnapshot.FindBestMatch(row.Entry.MatchTokens))))
                throw new Exception("Batched assessment matching changed individual loaded/name match precedence.");
            if(matchingBytes>2_000_000 || matchingMilliseconds>100) throw new Exception("Runtime matching repeatedly allocated or normalized candidates per plugin.");
            var alpha=runtimeSnapshot.Plugins[0];
            var disabled=new PluginRuntimeState("Bench0","A disabled",new(1,0),false,false,null,null,new object(),null,null,null);
            var zulu=new PluginRuntimeState("Bench0","Zulu",new(1,0),true,false,null,null,new object(),null,null,null);
            if(!ReferenceEquals(new PluginSnapshot([disabled,zulu,alpha]).FindBestMatch("B-E-N-C-H-0"),alpha)
                || !ReferenceEquals(new PluginSnapshot([alpha,alpha]).FindBestMatch("Plugin 00000"),alpha)
                || new PluginSnapshot([alpha]).FindBestMatch("---") is not null)
                throw new Exception("Runtime matching lost loaded/name precedence, aliases, stable ties or empty-candidate behavior.");
            var early=new PluginRuntimeState("Early","Shared",new(1,0),true,false,null,null,new object(),null,null,null);
            var late=new PluginRuntimeState("Late","sHaReD",new(1,0),true,false,null,null,new object(),null,null,null);
            var tieSnapshot=new PluginSnapshot([early,late]);
            if(!ReferenceEquals(tieSnapshot.FindBestMatches([["Late","Early"]])[0],early))
                throw new Exception("Batched runtime matching let token order override a stable plugin tie.");
            var handles=new[]{InstallerCatalog.OfficialRepository,source}.Select(url=>new { PluginMasterUrl=url,IsEnabled=true,IsThirdParty=url==source,State="Success" }).ToArray();
            var manager=new { Repos=handles,AvailablePlugins=manifests.Select((manifest,i)=>new {
                SourceRepo=handles[i%2],manifest.InternalName,manifest.Name,manifest.Author,manifest.Description,AssemblyVersion=manifest.Version,
                DalamudApiLevel=manifest.ApiLevel,RepoUrl="",IconUrl="",ImageUrls=Array.Empty<string>(),Tags=manifest.Tags,CategoryTags=manifest.CategoryTags,
                IsTestingExclusive=false,IsHide=false,Changelog="",Dip17Channel="" }).ToArray(),PluginsReady=true,ReposReady=true,SafeMode=false };
            var captureClock=System.Diagnostics.Stopwatch.StartNew();var captureAllocated=GC.GetAllocatedBytesForCurrentThread();
            var captured=PluginManagerBridge.CaptureInstallerHost(manager,new { ThirdRepoList=Array.Empty<object>() },PluginSnapshot.Empty);
            Console.WriteLine($"Host manifest capture: {count} entries; {captureClock.Elapsed.TotalMilliseconds:F2} ms/{GC.GetAllocatedBytesForCurrentThread()-captureAllocated:N0} bytes.");
            if(captured.Manifests.Count!=count || !captured.Ready) throw new Exception("Fabricated host capture dropped manifests/readiness.");
            var snapshot=new InstallerHostSnapshot([new(InstallerCatalog.OfficialRepository,"Dalamud",true,true,true,"Success")],
                manifests.Where(m=>m.RepositoryUrl==InstallerCatalog.OfficialRepository).ToArray(),[],true,false);
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var allocated=GC.GetAllocatedBytesForCurrentThread();
            var catalog=InstallerCatalog.Merge(snapshot,manifests,[],assessments);
            var mergeMilliseconds=clock.Elapsed.TotalMilliseconds;
            var mergeBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            if(mergeBytes>20_000_000 || mergeMilliseconds>500) throw new Exception("Catalog merge exceeded its bounded CPU/allocation budget.");
            if(catalog.Entries.Single(e=>e.Manifest.InternalName=="Bench0").Compatibility?.Entry.Id!="bench-0"
                || catalog.Entries.Count(e=>e.Compatibility is not null)!=Math.Min(count,1000))
                throw new Exception("Catalog indexing changed normalized-token precedence or attachment.");
            using var host=new NativeTestContext(1440,960,1,"en");
            using var icon=new BotologyOriginalIcon(host);
            using var text=new UiText("en",host.PushFont);
            var backend=new SnapshotUi(true,0x1CC9E6,"en") { Installer=catalog };
            using var main=new MainWindow(backend);
            var sortFlags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var sortRows=identityCases.Select((entry,index)=>entry with { Manifest=entry.Manifest with {
                ApiLevel=15,Version=index%3==0?"10.0":"2.0",Author=index%2==0?"Alpha":"Zulu" } }).ToArray();
            var snapshotField=typeof(MainWindow).GetField("installerSnapshot",sortFlags)!;
            var sortField=typeof(MainWindow).GetField("installerSort",sortFlags)!;
            var descendingField=typeof(MainWindow).GetField("installerSortDescending",sortFlags)!;
            var filterMethod=typeof(MainWindow).GetMethod("FilterInstallerEntries",sortFlags)!;
            snapshotField.SetValue(main,catalog with { Entries=sortRows });
            foreach(var sort in new[]{1,3,6}) foreach(var descending in new[]{false,true})
            {
                sortField.SetValue(main,sort);descendingField.SetValue(main,descending);
                var actual=(IReadOnlyList<InstallerEntry>)filterMethod.Invoke(main,null)!;
                IEnumerable<InstallerEntry> expected;
                if(sort==3)
                    expected=(descending?sortRows.OrderByDescending(entry=>Version.Parse(entry.Manifest.Version))
                        :sortRows.OrderBy(entry=>Version.Parse(entry.Manifest.Version))).ThenBy(entry=>entry.Identity,StringComparer.Ordinal);
                else
                {
                    string Key(InstallerEntry entry)=>sort==1?entry.Repository.Name:entry.Manifest.Author;
                    expected=(descending?sortRows.OrderByDescending(Key,StringComparer.OrdinalIgnoreCase)
                        :sortRows.OrderBy(Key,StringComparer.OrdinalIgnoreCase)).ThenBy(entry=>entry.Identity,StringComparer.Ordinal);
                }
                if(!actual.Select(entry=>entry.Identity).SequenceEqual(expected.Select(entry=>entry.Identity)))
                    throw new Exception("Indexed installer sorting changed numeric versions, direction or Unicode source/name tie order.");
            }
            snapshotField.SetValue(main,catalog);sortField.SetValue(main,0);descendingField.SetValue(main,false);
            var theme=BotologyPresentation.Theme(0x1CC9E6);theme.Motion.Enabled=false;
            var layoutMilliseconds=0d;
            float? requestedScroll=null;
            var reservedRows=new Dictionary<string,float>();var renderedRows=0;
            var observedHeights=new HashSet<float>();
            (Vector2 Min,Vector2 Max) authorHeader=default;
            var tableScroll=0f;var tableScrollMax=0f;
            var categoryScroll=0f;var categoryScrollMax=0f;var visibleCategories=0;
            var scrollCategories=false;var focusCategories=false;string? focusedCategory=null;
            var windowWidth=1440f;var renderedCards=0;
            main.MeasureBounds=(name,min,max)=>
            {
                if(name.StartsWith("preview-slot-",StringComparison.Ordinal)) renderedCards++;
                if(name is "installer-browse" or "installer-column-Plugin")
                {
                    layoutMilliseconds=clock.Elapsed.TotalMilliseconds;
                    if(requestedScroll is { } scroll) { ImGui.SetScrollY(scroll);requestedScroll=null; }
                    if(name=="installer-column-Plugin") { tableScroll=ImGui.GetScrollY();tableScrollMax=ImGui.GetScrollMaxY(); }
                }
                if(name=="installer-column-Author") authorHeader=(min,max);
                if(name=="category-track-Dalamud")
                {
                    if(scrollCategories) { ImGui.SetScrollX(ImGui.GetScrollMaxX());scrollCategories=false; }
                    if(focusCategories) { ImGui.SetWindowFocus();focusCategories=false; }
                    categoryScroll=ImGui.GetScrollX();categoryScrollMax=ImGui.GetScrollMaxX();
                }
                if(name.StartsWith("category-dalamud-",StringComparison.Ordinal) || name.StartsWith("category-botology-",StringComparison.Ordinal) || name.StartsWith("category-third-party-",StringComparison.Ordinal))
                { visibleCategories++;if(ImGui.IsItemFocused()) focusedCategory=name; }
                if(name.StartsWith("installer-table-reserved-",StringComparison.Ordinal)) reservedRows[name[25..]]=max.Y-min.Y;
                if(name.StartsWith("installer-table-row-",StringComparison.Ordinal))
                {
                    renderedRows++;
                    observedHeights.Add(max.Y-min.Y);
                    if(max.Y-min.Y>reservedRows[name[20..]]+.5f) throw new Exception($"A wrapped table row outgrew its reserved height: {name}, actual {max.Y-min.Y}, reserved {reservedRows[name[20..]]}.");
                }
            };
            void Draw()
            {
                reservedRows.Clear();renderedRows=0;visibleCategories=0;renderedCards=0;
                using var locale=text.Enter(); using var material=MaterialTheme.Push(theme,1,MaterialStyleMode.ColorsOnly);
                using var geometry=BotologyPresentation.PushHostGeometry(1,false);using var chrome=MaterialWindowChrome.Push();
                main.PreDraw(); ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new(windowWidth,960));
                var open=true;ImGui.Begin(main.WindowName,ref open,main.Flags|ImGuiWindowFlags.NoSavedSettings);
                main.Draw();ImGui.End();main.PostDraw();
            }
            var categoryFlags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var dalamudFilters=(System.Collections.IDictionary)typeof(MainWindow).GetField("dalamudCategoryFilters",categoryFlags)!.GetValue(main)!;
            var thirdPartyFilters=(System.Collections.IDictionary)typeof(MainWindow).GetField("thirdPartyCategoryFilters",categoryFlags)!.GetValue(main)!;
            var botologyFilters=(System.Collections.IDictionary)typeof(MainWindow).GetField("botologyCategoryFilters",categoryFlags)!.GetValue(main)!;
            var requiredMode=Enum.Parse(dalamudFilters.GetType().GenericTypeArguments[1],"Required");
            var blockedMode=Enum.Parse(dalamudFilters.GetType().GenericTypeArguments[1],"Blocked");
            foreach(var scenario in new[]{"cards","snapshot-refresh","scrolled","card-reflow","authors","source-sort","state-sort","author-sort","version-sort","categories","category-scrolled","category-selections","table","table-scrolled"})
            {
            windowWidth=scenario=="card-reflow"?1200:1440;
            if(scenario=="snapshot-refresh")
            {
                backend.Installer=catalog with { Entries=catalog.Entries.Select(entry=>entry with { Manifest=entry.Manifest with { } }).ToArray() };
                typeof(MainWindow).GetField("nextInstallerSnapshot",categoryFlags)!.SetValue(main,DateTime.MinValue);
            }
            if(scenario=="categories")
            {
                typeof(MainWindow).GetField("installerSort",categoryFlags)!.SetValue(main,0);
                typeof(MainWindow).GetField("pinsExpanded",categoryFlags)!.SetValue(main,false);
                typeof(MainWindow).GetField("categoriesExpanded",categoryFlags)!.SetValue(main,true);
                if(catalog.DalamudTags!.Count!=count/2+3 || catalog.ThirdPartyTags!.Count!=count/2+3 || catalog.BotologyCategories!.Count!=400) throw new Exception("Category vocabulary was capped or mixed official/third-party feeds.");
            }
            if(scenario=="category-scrolled") scrollCategories=true;
            if(scenario=="category-selections")
            {
                dalamudFilters["UI"]=requiredMode;
                for(var tag=0;tag<Math.Min(count,160);tag++) (tag%2==0?dalamudFilters:thirdPartyFilters)["Tag "+tag.ToString("00000")]=tag%2==0?requiredMode:blockedMode;
                for(var category=0;category<160;category++) botologyFilters["Category "+category.ToString("00000")]=requiredMode;
                typeof(MainWindow).GetMethod("RefreshSelectedCategories",categoryFlags)!.Invoke(main,null);
            }
            if(scenario.EndsWith("-sort",StringComparison.Ordinal))
                typeof(MainWindow).GetField("installerSort",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,
                    scenario switch { "source-sort"=>1,"state-sort"=>2,"author-sort"=>6,_=>3 });
            if(scenario is "scrolled" or "table-scrolled") requestedScroll=100000;
            if(scenario=="authors")
            {
                backend.Configuration.PinnedAuthors=["SampleAuthor","Missing author"];
                typeof(MainWindow).GetField("pinsExpanded",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,true);
                var filters=(System.Collections.IDictionary)typeof(MainWindow).GetField("pinnedAuthorFilters",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
                filters["SampleAuthor"]=Enum.Parse(filters.GetType().GenericTypeArguments[1],"Required");
            }
            if(scenario=="table")
            {
                typeof(MainWindow).GetMethod("ClearCategoryFilters",categoryFlags)!.Invoke(main,null);
                typeof(MainWindow).GetField("categoriesExpanded",categoryFlags)!.SetValue(main,false);
                typeof(MainWindow).GetField("installerSort",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,0);
                ((System.Collections.IDictionary)typeof(MainWindow).GetField("pinnedAuthorFilters",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!).Clear();
                typeof(MainWindow).GetField("pinsExpanded",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,false);
                typeof(MainWindow).GetField("tableView",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,true);
            }
            var firstAllocated=GC.GetAllocatedBytesForCurrentThread();clock.Restart();host.Frame(Draw);
            Console.WriteLine($"Installer first layout/reflow {scenario}: {count} entries; {clock.Elapsed.TotalMilliseconds:F2} ms/{GC.GetAllocatedBytesForCurrentThread()-firstAllocated:N0} bytes.");
            for(var warm=0;warm<3;warm++) host.Frame(Draw);
            var frames=new List<double>();var layouts=new List<double>();var allocations=new List<long>();
            for(var frame=0;frame<12;frame++)
            {
                allocated=GC.GetAllocatedBytesForCurrentThread();clock.Restart();host.Frame(Draw);
                frames.Add(clock.Elapsed.TotalMilliseconds);layouts.Add(layoutMilliseconds);
                allocations.Add(GC.GetAllocatedBytesForCurrentThread()-allocated);
            }
            frames.Sort();layouts.Sort();allocations.Sort();
            Console.WriteLine($"Installer performance {scenario}: {count} manifests/1000 assessments; merge {mergeMilliseconds:F2} ms/{mergeBytes:N0} bytes; native frame median {frames[frames.Count/2]:F2} ms, max {frames[^1]:F2} ms; layout median {layouts[layouts.Count/2]:F2} ms; median {allocations[allocations.Count/2]:N0} bytes/frame.");
            if(allocations[allocations.Count/2]>(scenario=="version-sort"?768_000:512_000) || frames[frames.Count/2]>50) throw new Exception("Installer steady frames exceeded their CPU/allocation budget.");
            if(scenario is "cards" or "snapshot-refresh" or "scrolled" or "card-reflow" or "authors" && renderedCards is <1 or >64)
                throw new Exception("Card controls were not restricted to visible rows.");
            if(scenario.StartsWith("table",StringComparison.Ordinal) && renderedRows is <1 or >64) throw new Exception("Table controls were not restricted to visible rows.");
            if(scenario.StartsWith("categor",StringComparison.Ordinal) && visibleCategories is <1 or >48) throw new Exception("Category strip submitted the complete offscreen vocabulary.");
            if(scenario=="category-scrolled" && (categoryScroll<=0 || Math.Abs(categoryScroll-categoryScrollMax)>1)) throw new Exception("Horizontal category overflow did not reach all labels.");
            if(scenario=="category-selections")
            {
                typeof(MainWindow).GetField("categoriesExpanded",categoryFlags)!.SetValue(main,false);
                for(var settle=0;settle<3;settle++) host.Frame(Draw);
                var summary=typeof(MainWindow).GetField("selectedCategories",categoryFlags)!.GetValue(main)!;
                var items=(Array)summary.GetType().GetField("Items")!.GetValue(summary)!;
                if(items.Length!=321 || dalamudFilters.Count!=81 || thirdPartyFilters.Count!=80 || botologyFilters.Count!=160 || visibleCategories>24)
                    throw new Exception("Collapsed category strip truncated many choices or drew every selected chip.");
            }
            if(count==5000 && scenario is "categories" or "category-scrolled" or "category-selections")
            {
                var output=Path.Combine(AppContext.BaseDirectory,"artifacts","installer");Directory.CreateDirectory(output);
                SoftwarePreview.Save(Path.Combine(output,"en-5000-"+scenario+".png"),host);
            }
            }
            typeof(MainWindow).GetField("categoriesExpanded",categoryFlags)!.SetValue(main,true);
            focusCategories=true;host.Frame(Draw);
            ImGui.GetIO().AddKeyEvent(ImGuiKey.End,true);host.Frame(Draw);ImGui.GetIO().AddKeyEvent(ImGuiKey.End,false);host.Frame(Draw);host.Frame(Draw);
            if(focusedCategory!="category-dalamud-utility") throw new Exception("Category keyboard End did not reach the last dynamic label: "+focusedCategory);
            ImGui.GetIO().AddKeyEvent(ImGuiKey.Home,true);host.Frame(Draw);ImGui.GetIO().AddKeyEvent(ImGuiKey.Home,false);host.Frame(Draw);host.Frame(Draw);
            if(focusedCategory!="category-dalamud-Tag 00000") throw new Exception("Category keyboard Home did not reach the first dynamic label: "+focusedCategory);
            var updated=manifests.Select((manifest,index)=>index==1?manifest with { Tags=["Refreshed label","Utility"] }:manifest).ToArray();
            backend.Installer=InstallerCatalog.Merge(snapshot,updated,[],assessments);
            typeof(MainWindow).GetField("nextInstallerSnapshot",categoryFlags)!.SetValue(main,DateTime.MinValue);host.Frame(Draw);
            typeof(MainWindow).GetField("categoryChooserSearch",categoryFlags)!.SetValue(main,"Refreshed");
            using(var locale=text.Enter()) typeof(MainWindow).GetMethod("RefreshCategoryTracks",categoryFlags)!.Invoke(main,null);
            host.Frame(Draw);
            if(visibleCategories!=1) throw new Exception("New categories were not searchable after capture refresh.");
            typeof(MainWindow).GetField("categoryChooserSearch",categoryFlags)!.SetValue(main,"");
            using(var locale=text.Enter()) typeof(MainWindow).GetMethod("RefreshCategoryTracks",categoryFlags)!.Invoke(main,null);
            typeof(MainWindow).GetField("categoriesExpanded",categoryFlags)!.SetValue(main,false);
            backend.Installer=catalog with { Entries=catalog.Entries.Select((entry,i)=>entry with {
                Manifest=entry.Manifest with { Author=i%3==0?"SampleAuthor with a long publisher credit that wraps over several lines\nSecond line":"SampleAuthor" } }).ToArray() };
            typeof(MainWindow).GetField("nextInstallerSnapshot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,DateTime.MinValue);
            requestedScroll=0;observedHeights.Clear();
            for(var frame=0;frame<3;frame++) host.Frame(Draw);
            if(observedHeights.Count<2) throw new Exception("Variable-height table fixture did not exercise wrapped and short rows.");
            var widthBefore=authorHeader.Max.X-authorHeader.Min.X;
            var divider=new Vector2(authorHeader.Max.X,(authorHeader.Min.Y+authorHeader.Max.Y)*.5f);
            ImGui.GetIO().AddMousePosEvent(divider.X,divider.Y);host.Frame(Draw);
            ImGui.GetIO().AddMouseButtonEvent(0,true);host.Frame(Draw);
            ImGui.GetIO().AddMousePosEvent(divider.X-60,divider.Y);host.Frame(Draw);
            ImGui.GetIO().AddMouseButtonEvent(0,false);host.Frame(Draw);host.Frame(Draw);
            if(authorHeader.Max.X-authorHeader.Min.X>=widthBefore-10) throw new Exception("Native table resize did not exercise new wrapped row heights.");
            var scrollExtent=tableScrollMax;
            requestedScroll=tableScrollMax;
            for(var frame=0;frame<3;frame++) host.Frame(Draw);
            if(renderedRows is <1 or >64 || tableScroll<=0 || Math.Abs(tableScroll-tableScrollMax)>1 || Math.Abs(scrollExtent-tableScrollMax)>1)
                throw new Exception($"Variable-height table lost its scroll extent or last visible rows: rows={renderedRows}, scroll={tableScroll}, max={tableScrollMax}, prior max={scrollExtent}.");
            if(backend.Actions.Count!=0) throw new Exception("Performance fixture dispatched host actions.");
        }
    }

    internal static unsafe void CheckTitle()
    {
        foreach(var separate in new[]{false,true}) foreach(var compact in new[]{false,true}) foreach(var scale in new[]{1f,1.5f})
        {
            using var host=new NativeTestContext((int)(1000*scale),(int)(760*scale),scale,"en",separate);
            using var icon=new BotologyOriginalIcon(host);
            using var text=new UiText("en",host.PushFont);
            var backend=new SnapshotUi(false,0x1CC9E6,"en") { TitlebarStatus=new("Alerts on | 12 loaded | 2 issues","12 loaded","Fixture status") };
            using var main=new MainWindow(backend);
            var appearance=AethertekUI.Dalamud.MaterialAppearanceTitleBar.Register(main,()=>new("Language","Compact","Transparency","Colour"),
                ()=>{},()=>{},()=>{},()=>{},()=>new(backend.Configuration.UiLanguageVisibleOnMainWindow,
                    backend.Configuration.UiCompactVisibleOnMainWindow,backend.Configuration.UiTransparencyVisibleOnMainWindow,true));
            var retainedButtons=main.TitleBarButtons.Take(2).ToArray();
            backend.Configuration.UiLanguageVisibleOnMainWindow=false;backend.Configuration.UiCompactVisibleOnMainWindow=true;
            backend.Configuration.UiTransparencyVisibleOnMainWindow=false;appearance.RefreshVisibility();
            if(main.TitleBarButtons.Count!=4 || retainedButtons.Any(b=>!main.TitleBarButtons.Contains(b))) throw new Exception("Saved title visibility changed retained descriptors.");
            var theme=BotologyPresentation.Theme(0x1CC9E6); theme.Density=compact?MaterialDensity.Compact:MaterialDensity.Standard; theme.Motion.Enabled=false;
            var original=new List<ImDrawVert>();
            ImGuiWindowPtr owner=default;
            bool? forcedCollapse=null;
            var open=true;
            var opacityValue=1f;
            var opacity=new MaterialWindowOpacity();
            uint ownerId=0;
            Vector2 collapsePoint=default,closePoint=default;
            void Draw()
            {
                using var locale=text.Enter(); using var palette=MaterialTheme.Push(theme,scale,MaterialStyleMode.ColorsOnly);
                using var geometry=BotologyPresentation.PushHostGeometry(scale,compact); using var chrome=MaterialWindowChrome.Push();
                using var nativeFont=host.PushNativeFont();
                ImGui.GetStyle().WindowMenuButtonPosition=ImGuiDir.Right;
                main.PreDraw();
                ImGui.SetNextWindowPos(new(8*scale)); ImGui.SetNextWindowSize(new(960*scale,720*scale));
                if(forcedCollapse.HasValue) ImGui.SetNextWindowCollapsed(forcedCollapse.Value);
                var shown=ImGui.Begin(main.WindowName,ref open,main.Flags|ImGuiWindowFlags.NoSavedSettings);
                owner=ImGuiP.GetCurrentWindow();
                if(ownerId==0) ownerId=owner.ID;
                if(owner.ID!=ownerId) throw new Exception("Title repaint changed the owner's native identity.");
                original.Clear();
                var title=ImGuiP.TitleBarRect(owner);
                collapsePoint=new(title.Max.X-1.5f*ImGui.GetFontSize(),(title.Min.Y+title.Max.Y)*.5f);
                closePoint=new(title.Max.X-.5f*ImGui.GetFontSize(),collapsePoint.Y);
                var captionMax=title.Min+new Vector2(ImGui.CalcTextSize("Botology").X+2*ImGui.GetStyle().FramePadding.X+2*scale,title.Max.Y-title.Min.Y);
                var list=owner.DrawList;
                for(var i=0;i+3<list.VtxBuffer.Size;i++)
                {
                    var a=list.VtxBuffer[i];var b=list.VtxBuffer[i+1];var c=list.VtxBuffer[i+2];var d=list.VtxBuffer[i+3];
                    if(a.Uv.X<c.Uv.X && a.Uv.Y<c.Uv.Y && a.Pos.X>=title.Min.X-1 && c.Pos.X<=captionMax.X
                        && a.Pos.Y==b.Pos.Y && b.Pos.X==c.Pos.X && c.Pos.Y==d.Pos.Y && d.Pos.X==a.Pos.X
                        && a.Pos.Y>=title.Min.Y-1 && c.Pos.Y<=title.Max.Y+1)
                    { for(var offset=0;offset<4;offset++) original.Add(list.VtxBuffer[i+offset]); i+=3; }
                }
                if(shown) main.Draw(); ImGui.End(); main.PostDraw();
                opacity.Apply(main.WindowName,opacityValue,autoFade:false);
            }
            host.Frame(Draw);host.Frame(Draw);
            var directory=Path.Combine(AppContext.BaseDirectory,"artifacts","installer","title");Directory.CreateDirectory(directory);
            SoftwarePreview.Save(Path.Combine(directory,$"en-{(compact?"compact":"regular")}-{scale:0.0}-{(separate?"private":"shared")}.png"),host);
            void VerifyTitle()
            {
                if(original.Count==0) throw new Exception("Title fixture did not observe the native caption.");
                var remaining=original.Where(vertex=>Enumerable.Range(0,owner.DrawList.VtxBuffer.Size).Any(i=>
                    owner.DrawList.VtxBuffer[i].Pos==vertex.Pos && owner.DrawList.VtxBuffer[i].Uv==vertex.Uv && (owner.DrawList.VtxBuffer[i].Col>>24)>0)).ToArray();
                if(remaining.Length!=0) throw new Exception($"Native caption survived actual MainWindow hooks: {remaining.Length}/{original.Count} vertices, privateAtlas={separate}, compact={compact}, scale={scale}.");
                var imageVertices=0;
                for(var c=0;c<owner.DrawList.CmdBuffer.Size;c++)
                {
                    var command=owner.DrawList.CmdBuffer[c];
                    if(command.TextureId!=icon.Handle) continue;
                    imageVertices+=(int)command.ElemCount;
                    for(var index=command.IdxOffset;index<command.IdxOffset+command.ElemCount;index++)
                    {
                        var vertex=owner.DrawList.VtxBuffer[(int)(owner.DrawList.IdxBuffer[(int)index]+command.VtxOffset)];
                        if(Math.Abs((vertex.Col>>24)-opacityValue*255)>1) throw new Exception("Title image opacity was applied incorrectly.");
                    }
                }
                if(imageVertices!=6) throw new Exception("Title image was missing or drawn more than once.");
            }
            VerifyTitle();
            backend.Configuration.UiLanguageVisibleOnMainWindow=backend.Configuration.UiTransparencyVisibleOnMainWindow=true;
            appearance.RefreshVisibility();host.Frame(Draw);host.Frame(Draw);VerifyTitle();
            if(main.TitleBarButtons.Count!=6) throw new Exception("Visible appearance descriptors were not restored.");
            backend.TitlebarStatus=null;host.Frame(Draw);host.Frame(Draw);VerifyTitle();
            backend.TitlebarStatus=new("हिन्दी","हिन्दी","Shaped fixture status");host.Frame(Draw);host.Frame(Draw);VerifyTitle();
            SoftwarePreview.Save(Path.Combine(directory,$"en-{(compact?"compact":"regular")}-{scale:0.0}-{(separate?"private":"shared")}-shaped.png"),host);
            forcedCollapse=true;host.Frame(Draw);host.Frame(Draw);VerifyTitle();
            if(!owner.Collapsed) throw new Exception("The actual owner's native collapse state was lost.");
            forcedCollapse=false;host.Frame(Draw);host.Frame(Draw);VerifyTitle();forcedCollapse=null;
            opacityValue=.5f;host.Frame(Draw);host.Frame(Draw);VerifyTitle();
            host.Click(collapsePoint,Draw);host.Frame(Draw);
            if(!owner.Collapsed) throw new Exception("Native title collapse action was lost.");
            host.Click(collapsePoint,Draw);host.Frame(Draw);
            if(owner.Collapsed) throw new Exception("Native title restore action was lost.");
            if(backend.Actions.Count!=0) throw new Exception("Title layout dispatched a plugin action.");
            host.Click(closePoint,Draw);
            if(open) throw new Exception("Native title close action was lost.");
        }
        Console.WriteLine("Actual MainWindow title hooks removed original caption ink in eight atlas/density/scale cases.");
    }

    internal static unsafe void Check(string[]? languages=null,bool lcars=false)
    {
        BotologyUiSmoke.CheckPreferencesAndResources();
        CheckUpdateAll(lcars);
        void SavePreview(string path,NativeTestContext context)
        {
            if(!lcars || path.EndsWith("-default.png",StringComparison.Ordinal) || path.Contains("-profile-",StringComparison.Ordinal)
                || path.Contains("failed",StringComparison.Ordinal) || path.Contains("clipped",StringComparison.Ordinal)) SoftwarePreview.Save(path,context);
        }
        var selectedLanguages=(languages is { Length:>0 }?languages:UiText.Languages.Select(l=>l.Code)).ToArray();
        var densityEvidence=new Dictionary<string,(float Media,float Body,float Font)>();
        var cases=selectedLanguages.SelectMany(language=>new[]{false,true}.Select(compact=>(language,compact,width:1440,height:960,scale:1f,accent:0x1CC9E6u,suffix:""))).ToList();
        if(selectedLanguages.Contains("en"))
        {
            foreach(var compact in new[]{false,true}) cases.Add(("en",compact,800,816,1,0x1CC9E6,"reference"));
            cases.Insert(0,("en",false,2048,1187,1,0x1CC9E6,"wide"));
            cases.Insert(1,("en",true,2048,1187,1,0x1CC9E6,"wide"));
            cases.Add(("en",false,1200,1224,1.5f,0x1CC9E6,"scaled"));
            cases.Add(("en",true,1240,1000,1,0x808080,"grey"));
            cases.Add(("en",false,1240,1000,1,0x000000,"black"));
            cases.Add(("en",true,1240,1000,1,0xFFFFFF,"white"));
        }
        if(selectedLanguages.Contains("ru")) cases.Add(("ru",true,1860,1440,1.5f,0xCF79AE,"translated-scaled-custom"));
        var checkedCases=0;
        var checkedLanguages=new HashSet<string>(StringComparer.Ordinal);
        foreach(var scenario in cases)
        {
            var (language,compact,width,height,scale,accent,suffix)=scenario;
            if(lcars && Environment.GetEnvironmentVariable("BOTOLOGY_VISUAL_CASE") is { Length:>0 } selected
                && !selected.Split(',').Contains(language+"/"+(suffix.Length==0?"installer":suffix),StringComparer.Ordinal)) continue;
            using var host=new NativeTestContext(width,height,scale,language);
            using var icon=new BotologyOriginalIcon(host);
            using var text=new UiText(language,host.PushFont);
            var backend=new SnapshotUi(false,accent,language);
            const string source="https://raw.githubusercontent.com/fixture-owner/fixture-tools/main/plugins.json";
            var manifests=Enumerable.Range(0,48).Select(i=>new InstallerManifest("Fixture"+i,"Plugin "+i.ToString("00"),i%4==2?InstallerCatalog.OfficialRepository:source,i<24?"SampleAuthor":"OtherAuthor",
                "Publisher description.",i==2?"2.0":i==6?"10.0":"1.0",15,Tags:i%3==0?["Crafting","Housing"]:["Utility"],CategoryTags:["UI"])).ToArray();
            var withMedia=suffix is "reference" or "wide" or "scaled" or "translated-scaled-custom" || language=="hi";
            const string still="https://fixture.invalid/layout.bmp",gif="https://fixture.invalid/preview.gif";
            if(withMedia)
            {
                manifests=manifests.Select(manifest=>manifest with { IconUrl="https://fixture.invalid/icon.bmp" }).ToArray();
                manifests[0]=manifests[0] with { ImageUrls=[still,gif,"https://fixture.invalid/missing.bmp"] };
                if(language=="hi") manifests[0]=manifests[0] with { Name="Plugin 00\nहिन्दी शीर्षक" };
                manifests[1]=manifests[1] with { ImageUrls=[gif] };
                manifests[2]=manifests[2] with { IconUrl="https://fixture.invalid/icon.bmp" };
                manifests[3]=manifests[3] with { ImageUrls=["https://fixture.invalid/portrait.bmp"] };
                manifests[4]=manifests[4] with { ImageUrls=["https://fixture.invalid/square.bmp"] };
                manifests[5]=manifests[5] with { ImageUrls=["https://fixture.invalid/missing.bmp"] };
                manifests[6]=manifests[6] with { ImageUrls=["https://fixture.invalid/failed.bmp"] };
                manifests[8]=manifests[8] with { ImageUrls=["https://fixture.invalid/loading.bmp"] };
                manifests[7]=manifests[7] with { IconUrl="https://fixture.invalid/portrait.bmp" };
                manifests[9]=manifests[9] with { IconUrl=null };
                manifests[40]=manifests[40] with { Name="Plugin 40 with a long title that wraps locally\n"+(language=="hi"?"हिन्दी शीर्षक":"Second title line") };
            }
            manifests=manifests.Concat(new[]{"testing/plugins.json","main/plugins.json?track=preview"}.Select(path=>
                new InstallerManifest("Fixture8","Plugin 08","https://raw.githubusercontent.com/fixture-owner/fixture-tools/"+path,"OtherAuthor","Alternate source fixture.","1.0",15))).ToArray();
            const string failedSource="https://states.invalid/failure.json",disabledSource="https://states.invalid/disabled.json";
            manifests=manifests.Concat(new[]{
                new InstallerManifest("Fixture3","State collision",InstallerCatalog.OfficialRepository,"OtherAuthor",Version:"1.0",ApiLevel:15),
                new InstallerManifest("StateTesting","State testing",InstallerCatalog.OfficialRepository,"OtherAuthor",Version:"1.0",ApiLevel:15,IsTestingExclusive:true),
                new InstallerManifest("StateApi","State API",InstallerCatalog.OfficialRepository,"OtherAuthor",Version:"1.0",ApiLevel:14),
                new InstallerManifest("StateRefresh","State refresh",InstallerCatalog.OfficialRepository,"OtherAuthor",Version:"1.0",ApiLevel:15),
                new InstallerManifest("StateReady","State ready",InstallerCatalog.OfficialRepository,"OtherAuthor",Version:"1.0",ApiLevel:15),
                new InstallerManifest("StateFailure","State failed source",failedSource,"OtherAuthor",Version:"1.0",ApiLevel:15),
                new InstallerManifest("StateDisabled","State disabled source",disabledSource,"OtherAuthor",Version:"1.0",ApiLevel:15)}).ToArray();
            var errorRuntime=new PluginRuntimeState("Fixture1","Plugin 01",new(1,0),false,false,null,null,new object(),null,null,null);
            var loaded=new PluginRuntimeState("Fixture3","Plugin 03",new(1,0),true,false,null,null,new object(),null,null,null);
            var updating=new PluginRuntimeState("Fixture4","Plugin 04",new(1,0),false,true,null,null,new object(),null,null,null);
            var outdated=new PluginRuntimeState("Fixture7","Plugin 07",new(1,0),false,false,null,null,new object(),null,null,null);
            var rule=new PluginCatalogEntry("fixture3","Utility","Plugin 03",["Fixture3"],"Fixture rule notes.");
            backend.Installer=InstallerCatalog.Merge(new([new(InstallerCatalog.OfficialRepository,"Dalamud",true,true,true,"Success"),
                    new(failedSource,"states.invalid",true,true,false,"Fail"),new(disabledSource,"states.invalid",true,false,false,"Success")],
                manifests.Where(m=>(m.RepositoryUrl==InstallerCatalog.OfficialRepository && m.InternalName!="StateRefresh") || m.RepositoryUrl==failedSource).ToArray(),
                [new(errorRuntime,source,"LoadError"),new(loaded,source,"Loaded"),new(updating,source,"Unloaded"),new(outdated,source,"Unloaded",IsOutdated:true)],true,false),manifests,
                [new("fixture0","DOH/L Content Bots","Plugin 00",["Fixture0"],""),new("fixture1","DOH/L Content Bots","Plugin 01",["Fixture1"],""),new("fixture4","Housing","Plugin 04",["Fixture4"],"")],
                [new(rule,loaded,new(AssessmentSeverity.Yellow,"Fixture warning.","Fixture details."),false)]);
            backend.Configuration.PinnedRepositories=[source,"invalid"];
            backend.Configuration.PinnedAuthors=["SampleAuthor","Invalid Author"];
            using var main=new MainWindow(backend);
            // The supplied diagnostic exceeds the normal 1880px maximum at this fixture's 100% host scale.
            // Permit that physical diagnostic size here while retaining the production Draw/PreDraw paths and packing policy.
            if(suffix=="wide" && main.SizeConstraints is { } limits)
                main.SizeConstraints=new Dalamud.Interface.Windowing.WindowSizeConstraints {
                    MinimumSize=limits.MinimumSize,MaximumSize=Vector2.Max(limits.MaximumSize,new(width,height)) };
            using var fixtureImages=withMedia?new PreviewImages(320,180):null;
            using var fixtureMedia=fixtureImages is not null?new InstallerMedia(frame=>new PreviewTexture(host,frame),new HttpClient(fixtureImages)):null;
            if(fixtureMedia is not null) typeof(MainWindow).GetField("installerMedia",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,fixtureMedia);
            var bounds=new Dictionary<string,(Vector2 Min,Vector2 Max)>();
            var scrollRequest=false;
            var requestedScroll=680f;
            var browseScroll=0f;
            var browseScrollMax=0f;
            var bodyFontSize=0f;var bodyLineHeight=0f;
            var browseClip=(Min:Vector2.Zero,Max:Vector2.Zero);
            var contentScrollRequest=false;
            var requestedContentScroll=0f;
            var contentScroll=0f;
            var contentClip=(Min:Vector2.Zero,Max:Vector2.Zero);
            var galleryClip=(Min:Vector2.Zero,Max:Vector2.Zero);
            var shapedTitleId="card-title-"+InstallerCatalog.PluginIdentity(source,"Fixture0");
            var shapedTitleRequired=0f;
            uint expectedGrid=0,actualGrid=0;
            string? clippedPin=null;
            main.MeasureBounds=(name,min,max)=>
            {
                bounds[name]=(min,max);
                if(name.StartsWith("installer-table-row-",StringComparison.Ordinal)
                    && max.Y-min.Y>bounds["installer-table-reserved-"+name[20..]].Max.Y-bounds["installer-table-reserved-"+name[20..]].Min.Y+.5f)
                    throw new Exception("Translated table row outgrew its reserved wrapped height: "+language);
                if(name=="table-header") actualGrid=ImGuiP.GetCurrentTable().ID;
                if(name=="media-play") { var clip=ImGuiP.GetCurrentWindow().InnerClipRect;galleryClip=(clip.Min,clip.Max); }
                if(language=="hi" && name==shapedTitleId)
                {
                    using var font=UiText.Font(UiFontRole.BodyStrong);
                    shapedTitleRequired=MaterialText.Measure(manifests[0].Name,false,Math.Max(1,ImGui.GetWindowWidth()-24*scale)).Y;
                }
                if(name=="installer-content")
                {
                    var clip=ImGuiP.GetCurrentWindow().InnerClipRect;contentClip=(clip.Min,clip.Max);
                    if(contentScrollRequest) { ImGui.SetScrollY(requestedContentScroll);contentScrollRequest=false; }
                    contentScroll=ImGui.GetScrollY();
                }
                if(name.StartsWith("pin-",StringComparison.Ordinal) && max.X>ImGui.GetWindowPos().X+ImGui.GetWindowContentRegionMax().X+.5f) clippedPin=name;
                if(name=="installer-browse")
                {
                    bodyFontSize=ImGui.GetFontSize();bodyLineHeight=ImGui.GetTextLineHeight();
                    var clip=ImGuiP.GetCurrentWindow().InnerClipRect;browseClip=(clip.Min,clip.Max);
                    if(scrollRequest) { ImGui.SetScrollY(requestedScroll);scrollRequest=false; }
                    browseScroll=ImGui.GetScrollY();
                    browseScrollMax=ImGui.GetScrollMaxY();
                }
            };
            var theme=BotologyPresentation.Theme(accent,new() { Mode=lcars?MaterialColorMode.Lcars:MaterialColorMode.Auto }); theme.Density=compact?MaterialDensity.Compact:MaterialDensity.Standard; theme.Motion.Enabled=false;
            void Draw()
            {
                bounds.Clear(); clippedPin=null;shapedTitleRequired=0;using var locale=text.Enter(); using var material=MaterialTheme.Push(theme,scale,MaterialStyleMode.ColorsOnly);
                using var geometry=BotologyPresentation.PushHostGeometry(scale,theme.Density==MaterialDensity.Compact);
                using var chrome=MaterialWindowChrome.Push();
                using var nativeFont=host.PushNativeFont();
                main.PreDraw();
                ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new(width,height));
                var open=true;
                ImGui.Begin("Botology##Main",ref open,main.Flags|ImGuiWindowFlags.NoSavedSettings);
                if(suffix=="wide" && Vector2.Distance(ImGui.GetWindowSize(),new(width,height))>.5f)
                    throw new Exception("Wide fixture did not render the actual supplied window dimensions.");
                expectedGrid=ImGui.GetID("BotologyGrid");
                main.Draw(); ImGui.End(); main.PostDraw();
            }
            void Settle() { for(var i=0;i<4;i++) host.Frame(Draw); }
            void Click(string id,ImGuiMouseButton button=ImGuiMouseButton.Left)
            {
                if(!bounds.TryGetValue(id,out var rect))
                {
                    SavePreview(Path.Combine(AppContext.BaseDirectory,"artifacts","installer",suffix,language+(compact?"-compact":"-regular")+"-interaction-failed.png"),host);
                    throw new Exception("Missing installer action: "+id+" in "+language+"/"+compact+"/"+suffix);
                }
                // A wholly clipped child has not submitted its buttons yet. Reveal the real
                // repository card before using the button's native item rectangle.
                if(id.StartsWith("repo-add-",StringComparison.Ordinal) && bounds.TryGetValue("repository-card-"+id[9..],out var repoCard)
                    && (repoCard.Min.Y>=contentClip.Max.Y || repoCard.Max.Y<=contentClip.Min.Y))
                {
                    requestedContentScroll=Math.Max(0,contentScroll+repoCard.Min.Y-contentClip.Min.Y+8*scale);
                    contentScrollRequest=true;Settle();
                    if(!bounds.TryGetValue(id,out rect)) throw new Exception("Revealed repository card did not submit its action: "+id);
                }
                if((id.StartsWith("repo-add-",StringComparison.Ordinal) || id.StartsWith("nav-",StringComparison.Ordinal)
                    || id.StartsWith("profile-",StringComparison.Ordinal) || id.StartsWith("media-",StringComparison.Ordinal))
                    && (rect.Min.Y<contentClip.Min.Y || rect.Max.Y>contentClip.Max.Y))
                {
                    requestedContentScroll=Math.Max(0,contentScroll+(rect.Max.Y>contentClip.Max.Y?rect.Max.Y-contentClip.Max.Y+8*scale:rect.Min.Y-contentClip.Min.Y-8*scale));
                    contentScrollRequest=true;Settle();
                    if(!bounds.TryGetValue(id,out rect) || rect.Min.Y<contentClip.Min.Y-1 || rect.Max.Y>contentClip.Max.Y+1)
                    {
                        SavePreview(Path.Combine(AppContext.BaseDirectory,"artifacts","installer",suffix,language+(compact?"-compact":"-regular")+"-scroll-failed.png"),host);
                        throw new Exception("Native content scrolling did not reveal its action: "+id+" in "+language+"/"+compact+"/"+suffix+"; item="+rect+", clip="+contentClip+", scroll="+contentScroll+", requested="+requestedContentScroll);
                    }
                }
                if(id.StartsWith("details-",StringComparison.Ordinal) && (rect.Min.Y<browseClip.Min.Y || rect.Max.Y>browseClip.Max.Y))
                {
                    requestedScroll=Math.Max(0,browseScroll+(rect.Max.Y>browseClip.Max.Y?rect.Max.Y-browseClip.Max.Y+8*scale:rect.Min.Y-browseClip.Min.Y-8*scale));
                    scrollRequest=true;Settle();
                    if(!bounds.TryGetValue(id,out rect) || rect.Min.Y<browseClip.Min.Y-1 || rect.Max.Y>browseClip.Max.Y+1)
                        throw new Exception("Native browse scrolling did not reveal its card action: "+id);
                }
                host.Click((rect.Min+rect.Max)*.5f,Draw,button);Settle();
            }
            void WaitForPreview(string url,bool full)
            {
                if(fixtureMedia is null) return;
                var deadline=DateTime.UtcNow.AddSeconds(5);
                while(fixtureMedia.Get(url,full,out var failed) is null)
                {
                    if(failed || DateTime.UtcNow>=deadline) throw new Exception("Native fixture preview did not become ready.");
                    host.Frame(Draw); Thread.Sleep(5);
                }
                host.Frame(Draw);
            }
            void CheckCardBounds()
            {
                var cards=bounds.Where(pair=>pair.Key.StartsWith("card-",StringComparison.Ordinal)
                    && backend.Installer.Entries.Any(entry=>pair.Key=="card-"+entry.Identity)).ToArray();
                var template=cards.Length>0?cards[0]:default;
                foreach(var (name,card) in cards)
                {
                    var identity=name[5..];
                    if(Vector2.Distance(card.Max-card.Min,template.Value.Max-template.Value.Min)>.5f)
                        throw new Exception("Media state or text changed an individual card's dimensions: "+language+"/"+compact+"/"+suffix);
                    foreach(var prefix in new[]{"preview-slot-","card-title-","card-state-","card-compatibility-","card-pin-"})
                        if(bounds.TryGetValue(prefix+identity,out var region) && bounds.TryGetValue(prefix+template.Key[5..],out var shared)
                            && Math.Abs((region.Min.Y-card.Min.Y)-(shared.Min.Y-template.Value.Min.Y))>.5f)
                            throw new Exception("Card regions do not align within the shared template: "+prefix+language+"/"+suffix);
                    if(bounds["card-title-"+identity].Min.Y<bounds["preview-slot-"+identity].Max.Y)
                        throw new Exception("A plugin title moved into the media band.");
                    var entry=backend.Installer.Entries.First(entry=>entry.Identity==identity);
                    if(fixtureMedia is not null && entry.Manifest.IconUrl is { } iconUrl
                        && bounds.TryGetValue("preview-icon-"+identity,out var iconInk)
                        && fixtureMedia.Get(iconUrl,false,out _) is { } iconTexture)
                    {
                        var slot=bounds["preview-slot-"+identity];var extent=slot.Max-slot.Min;
                        var fit=Math.Min(extent.X/iconTexture.Width,extent.Y/iconTexture.Height);
                        if(Vector2.Distance(iconInk.Max-iconInk.Min,new Vector2(iconTexture.Width,iconTexture.Height)*fit)>.5f
                            || Vector2.Distance((iconInk.Min+iconInk.Max)*.5f,(slot.Min+slot.Max)*.5f)>.5f)
                            throw new Exception("Plugin icon lost full aspect-fit or media prominence: "+identity);
                    }
                    foreach(var prefix in new[]{"card-title-","card-state-","card-compatibility-","preview-slot-","preview-image-","preview-icon-","preview-placeholder-","preview-label-"})
                        if(bounds.TryGetValue(prefix+identity,out var ink) && (ink.Min.X<card.Min.X-.5f || ink.Max.X>card.Max.X+.5f || ink.Min.Y<card.Min.Y-.5f || ink.Max.Y>card.Max.Y+.5f))
                            throw new Exception("Card content exceeds its measured bounds: "+prefix+language+"/"+compact+"/"+suffix);
                    if(bounds.TryGetValue("preview-label-"+identity,out var label) && (bounds.TryGetValue("preview-icon-"+identity,out var picture) || bounds.TryGetValue("preview-placeholder-"+identity,out picture))
                        && picture.Min.X<label.Max.X && picture.Max.X>label.Min.X && picture.Min.Y<label.Max.Y && picture.Max.Y>label.Min.Y)
                        throw new Exception("Missing-media caption overlaps its icon: "+language);
                }
                if(cards.Length>0)
                {
                    var first=cards.OrderBy(pair=>pair.Value.Min.Y).ThenBy(pair=>pair.Value.Min.X).First().Value;
                    var firstRow=cards.Where(pair=>Math.Abs(pair.Value.Min.Y-first.Min.Y)<1).ToArray();
                    var available=browseClip.Max.X-browseClip.Min.X;
                    if(firstRow.Length>1 && firstRow.Max(pair=>pair.Value.Max.X)-firstRow.Min(pair=>pair.Value.Min.X)>available+2*scale)
                        throw new Exception("Responsive card packing exceeded the browse viewport.");
                }
            }
            Settle();
            if(language=="en" && withMedia)
            {
                WaitForPreview(still,false);Settle();
                var identity=InstallerCatalog.PluginIdentity(source,"Fixture0");
                var card=bounds["card-"+identity];var slot=bounds["preview-slot-"+identity];var picture=bounds["preview-image-"+identity];
                var priorFont=bodyFontSize;
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var columns=(int)typeof(MainWindow).GetField("previousColumns",flags)!.GetValue(main)!;
                var priorDensity=theme.Density;
                theme.Density=priorDensity==MaterialDensity.Compact?MaterialDensity.Standard:MaterialDensity.Compact;
                Settle();CheckCardBounds();
                var toggledCard=bounds["card-"+identity];var toggledSlot=bounds["preview-slot-"+identity];var toggledImage=bounds["preview-image-"+identity];
                if((int)typeof(MainWindow).GetField("previousColumns",flags)!.GetValue(main)! != columns
                    || Math.Abs((card.Max.X-card.Min.X)-(toggledCard.Max.X-toggledCard.Min.X))>.5f
                    || Vector2.Distance(slot.Max-slot.Min,toggledSlot.Max-toggledSlot.Min)>.5f
                    || Vector2.Distance(picture.Max-picture.Min,toggledImage.Max-toggledImage.Min)>.5f
                    || Math.Abs(bodyFontSize-priorFont)>.01f)
                    throw new Exception("Density changed the default grid breakpoint, actual media geometry or body font at the same window size.");
                theme.Density=priorDensity;Settle();CheckCardBounds();
            }
            var initial=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(initial.Count!=55 || initial.Any(e=>!e.IsApiCompatible || e.Installed?.IsOutdated==true))
                throw new Exception("New installer windows must block API-unavailable entries by default.");
            if(language=="en" && bounds.TryGetValue("clear-installer-filters",out var clearDefault))
            {
                var countBounds=bounds["installer-result-count"];
                if(clearDefault.Min.Y>=countBounds.Max.Y || clearDefault.Max.Y<=countBounds.Min.Y)
                    throw new Exception("Default API block spent an extra Clear filter row instead of sharing result tools.");
            }
            var output=Path.Combine(AppContext.BaseDirectory,"artifacts","installer",suffix);Directory.CreateDirectory(output);
            if(language is "en" or "ru" or "hi") SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-default.png"),host);
            IReadOnlyList<InstallerEntry> CategoryResults()=>(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            void CheckCategoryBounds()
            {
                var surface=bounds["category-surface"];
                foreach(var id in new[]{"categories-toggle","categories-clear","category-search","category-track-Dalamud","category-track-Botology","category-track-Third-party","category-track-Selected","category-hint"})
                    if(bounds.TryGetValue(id,out var rect) && (rect.Min.Y<surface.Min.Y || rect.Max.Y>surface.Max.Y-2*scale || rect.Max.X>surface.Max.X-2*scale))
                        throw new Exception("Category content exceeds its surface: "+id+" in "+language+"/"+compact+"/"+suffix);
            }
            Click("categories-toggle");
            if(!bounds.ContainsKey("category-search") || !bounds.ContainsKey("category-track-Dalamud") || !bounds.ContainsKey("category-track-Botology") || !bounds.ContainsKey("category-track-Third-party"))
                throw new Exception("Category expansion lost a source track/chooser: "+language);
            CheckCategoryBounds();
            if(language is "en" or "ru" or "hi") SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-categories-expanded.png"),host);
            Click("category-third-party-UI");Click("category-botology-DOH/L Content Bots");
            if(CategoryResults().Count!=2 || CategoryResults().Any(entry=>entry.BotologyCategory!="DOH/L Content Bots" || entry.Repository.IsOfficial || !entry.ManifestTags!.Contains("UI")))
                throw new Exception("Category source intersection lost uninstalled catalog matches: "+language);
            if(language=="en" && !compact && suffix.Length==0)
            {
                Click("categories-clear");
                Click("category-third-party-Crafting");Click("category-third-party-Utility");
                Click("category-botology-DOH/L Content Bots");Click("category-botology-Housing");
                Click("category-third-party-Housing",ImGuiMouseButton.Right);
                if(!CategoryResults().Select(entry=>entry.Manifest.InternalName).SequenceEqual(["Fixture1","Fixture4"]))
                    throw new Exception("Required categories did not union within sources, intersect sources, or preserve blocked/source-collision precedence.");
                Click("category-third-party-Housing");
                if(CategoryResults().Count!=3) throw new Exception("Opposite category click did not switch blocked to required.");
                Click("category-third-party-Housing");
                if(CategoryResults().Count!=3) throw new Exception("Repeated category click did not clear its requirement.");
                Click("categories-clear");Click("category-dalamud-UI");
                if(CategoryResults().Count!=12 || CategoryResults().Any(entry=>!entry.Repository.IsOfficial))
                    throw new Exception("Official category requirement included third-party manifests with the same tag.");
                Click("category-third-party-UI");
                if(CategoryResults().Count!=47) throw new Exception("Requirements across the two repository rows did not include either matching feed group while preserving the default API block.");
                Click("category-third-party-UI",ImGuiMouseButton.Right);
                if(CategoryResults().Count!=12 || CategoryResults().Any(entry=>!entry.Repository.IsOfficial))
                    throw new Exception("Blocking a third-party tag excluded an official tag with the same caption.");
                Click("category-dalamud-UI",ImGuiMouseButton.Right);Click("category-third-party-UI",ImGuiMouseButton.Right);
                if(CategoryResults().Any(entry=>entry.Repository.IsOfficial && (entry.ManifestTags??[]).Contains("UI"))
                    || !CategoryResults().Any(entry=>!entry.Repository.IsOfficial && (entry.ManifestTags??[]).Contains("UI")))
                    throw new Exception("Official-only category block leaked into third-party results or repeating a third-party block did not clear it.");
                Click("categories-clear");Click("category-third-party-UI");Click("category-botology-DOH/L Content Bots");
                var beforeChooser=CategoryResults().Select(entry=>entry.Identity).ToArray();
                Click("category-search");
                foreach(var character in "Housing") ImGui.GetIO().AddInputCharacter(character);
                Settle();
                if(!bounds.ContainsKey("category-dalamud-Housing") || !bounds.ContainsKey("category-third-party-Housing") || !bounds.ContainsKey("category-botology-Housing") || bounds.ContainsKey("category-dalamud-UI") || bounds.ContainsKey("category-third-party-UI")
                    || !CategoryResults().Select(entry=>entry.Identity).SequenceEqual(beforeChooser))
                    throw new Exception("Native chooser search changed result filters or lost one source's matching label.");
                ImGui.GetIO().AddKeyEvent(ImGuiKey.ModCtrl,true);ImGui.GetIO().AddKeyEvent(ImGuiKey.A,true);host.Frame(Draw);
                ImGui.GetIO().AddKeyEvent(ImGuiKey.A,false);ImGui.GetIO().AddKeyEvent(ImGuiKey.ModCtrl,false);ImGui.GetIO().AddKeyEvent(ImGuiKey.Backspace,true);host.Frame(Draw);
                ImGui.GetIO().AddKeyEvent(ImGuiKey.Backspace,false);Settle();
                Click("clear-installer-filters");
                if(CategoryResults().Count!=57) throw new Exception("Clear filter did not clear category and status criteria together.");
                Click("filter-Not available on this API",ImGuiMouseButton.Right);
                Click("category-third-party-UI");Click("category-botology-DOH/L Content Bots");
            }
            Click("categories-toggle");
            if(CategoryResults().Count!=2 || !bounds.ContainsKey("category-track-Selected") || bounds.ContainsKey("category-search"))
                throw new Exception("Folded categories lost hidden selections/results or retained the chooser: "+language);
            CheckCategoryBounds();
            if(language is "en" or "ru" or "hi") SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-categories-selected.png"),host);
            Click("categories-clear");
            if(CategoryResults().Count!=initial.Count || backend.Actions.Count!=0) throw new Exception("Category clear altered status criteria or dispatched plugin/source work in "+language+"/"+compact+"/"+suffix+": "+CategoryResults().Count+" results, "+backend.Actions.Count+" actions.");
            Click("filter-All plugins");
            WaitForPreview(still,false); WaitForPreview(gif,false);
            if(fixtureMedia is not null)
            {
                var picture=fixtureMedia.Get(still,false,out _)!;
                var seen=false;
                var data=ImGui.GetDrawData();
                for(var listIndex=0;listIndex<data.CmdListsCount;listIndex++)
                {
                    var list=new ImDrawListPtr(data.CmdLists[listIndex]);
                    for(var c=0;c<list.CmdBuffer.Size;c++)
                    {
                        var command=list.CmdBuffer[c];if(command.TextureId!=picture.Handle) continue;
                        var vertices=Enumerable.Range((int)command.IdxOffset,(int)command.ElemCount)
                            .Select(i=>list.VtxBuffer[(int)(list.IdxBuffer[i]+command.VtxOffset)]).ToArray();
                        var span=new Vector2(vertices.Max(v=>v.Pos.X)-vertices.Min(v=>v.Pos.X),vertices.Max(v=>v.Pos.Y)-vertices.Min(v=>v.Pos.Y));
                        if(Math.Abs(span.X/span.Y-(float)picture.Width/picture.Height)>.002f
                            || vertices.Min(v=>v.Uv.X)!=0 || vertices.Max(v=>v.Uv.X)!=1 || vertices.Min(v=>v.Uv.Y)!=0 || vertices.Max(v=>v.Uv.Y)!=1)
                            throw new Exception("Card preview stretched or cropped supplied screenshot edges.");
                        seen=true;
                    }
                }
                if(!seen) throw new Exception("Aspect-fit fixture did not exercise its native screenshot.");
                var identity=InstallerCatalog.PluginIdentity(source,"Fixture0");
                if(language=="hi")
                {
                    var title=bounds["card-title-"+identity];var actual=title.Max.Y-title.Min.Y;
                    if(shapedTitleRequired<=0 || actual<shapedTitleRequired-.5f)
                    {
                        SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-shaped-title-clipped.png"),host);
                        throw new Exception($"Two-line shaped title was clipped to native line metrics: reserved={actual}, required={shapedTitleRequired}.");
                    }
                }
                var slot=bounds["preview-slot-"+identity];var card=bounds["card-"+identity];
                var mediaProportion=(slot.Max.Y-slot.Min.Y)/(card.Max.X-card.Min.X);
                if(mediaProportion<.4f || mediaProportion>.5f)
                    throw new Exception("Screenshot band differs from the approved reference proportion.");
                var densityKey=language+"/"+suffix;
                var bodyProportion=((card.Max.Y-card.Min.Y)-(slot.Max.Y-slot.Min.Y))/bodyLineHeight;
                if(compact && densityEvidence.TryGetValue(densityKey,out var regular))
                {
                    if(Math.Abs(mediaProportion-regular.Media)>.002f || Math.Abs(bodyFontSize-regular.Font)>.01f)
                        throw new Exception("Compact changed relative media sizing or requested typography.");
                    if(language=="en" && suffix=="wide" && bodyProportion>=regular.Body-.1f)
                        throw new Exception("Compact did not meaningfully tighten the card body.");
                }
                else densityEvidence[densityKey]=(mediaProportion,bodyProportion,bodyFontSize);
                var image=bounds["preview-image-"+identity];
                var expected=Math.Min((slot.Max.X-slot.Min.X)/picture.Width,(slot.Max.Y-slot.Min.Y)/picture.Height);
                if(Math.Abs(image.Max.X-image.Min.X-picture.Width*expected)>.5f || Math.Abs(image.Max.Y-image.Min.Y-picture.Height*expected)>.5f)
                    throw new Exception("Screenshot retained an avoidable inner inset.");
                if(suffix=="wide")
                {
                    WaitForPreview("https://fixture.invalid/icon.bmp",false);
                    WaitForPreview("https://fixture.invalid/portrait.bmp",false);
                    WaitForPreview("https://fixture.invalid/square.bmp",false);
                    var deadline=DateTime.UtcNow.AddSeconds(5);
                    foreach(var url in new[]{"https://fixture.invalid/missing.bmp","https://fixture.invalid/failed.bmp"})
                        while(fixtureMedia.Get(url,false,out var failed) is not null || !failed)
                        {
                            if(DateTime.UtcNow>=deadline) throw new Exception("Native failure fixture did not settle.");
                            host.Frame(Draw);Thread.Sleep(5);
                        }
                    Settle();CheckCardBounds();
                    if(!fixtureMedia.IsMissing("https://fixture.invalid/missing.bmp",false) || fixtureMedia.IsMissing("https://fixture.invalid/failed.bmp",false))
                        throw new Exception("Missing and failed previews lost their distinct states.");
                    var knownHeaders=new[]{5,6}.Select(index=>"preview-slot-"+InstallerCatalog.PluginIdentity(index==6?InstallerCatalog.OfficialRepository:source,"Fixture"+index))
                        .ToDictionary(key=>key,key=>bounds[key].Max-bounds[key].Min);
                    backend.Installer=backend.Installer with { Entries=backend.Installer.Entries.Select(entry=>entry with { Manifest=entry.Manifest with { } }).ToArray() };
                    typeof(MainWindow).GetField("nextInstallerSnapshot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,DateTime.MinValue);
                    host.Frame(Draw);
                    foreach(var (key,priorSize) in knownHeaders)
                        if(Vector2.Distance(priorSize,bounds[key].Max-bounds[key].Min)>.5f)
                            throw new Exception("An unchanged host refresh expanded a failed-preview card for one frame.");
                    foreach(var index in new[]{2,5,6})
                    {
                        var key=InstallerCatalog.PluginIdentity(index==2||index==6?InstallerCatalog.OfficialRepository:source,"Fixture"+index);
                        var header=bounds["preview-slot-"+key];
                        var title=bounds["card-title-ink-"+key];
                        if(title.Min.Y<header.Max.Y || Math.Abs((header.Max.Y-header.Min.Y)-(bounds["preview-slot-"+identity].Max.Y-bounds["preview-slot-"+identity].Min.Y))>.5f)
                            throw new Exception("Icon-only/failed card changed the common media band or title placement.");
                        if(index==2)
                        {
                            var ink=bounds["preview-icon-"+key];var side=ink.Max.X-ink.Min.X;
                            var expectedSide=Math.Min(header.Max.X-header.Min.X,header.Max.Y-header.Min.Y);
                            if(Math.Abs(side-expectedSide)>.5f || Math.Abs(side-(ink.Max.Y-ink.Min.Y))>.5f
                                || Vector2.Distance((ink.Min+ink.Max)*.5f,(header.Min+header.Max)*.5f)>.5f)
                                throw new Exception("Small real plugin icon did not fill and center within the shared media band proportionally.");
                        }
                    }
                    SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-loading.png"),host);
                }
                var anchorField=typeof(MainWindow).GetField("browseAnchor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                var anchorBefore=anchorField.GetValue(main);
                var readyDimensions=bounds.Where(pair=>pair.Key.StartsWith("card-",StringComparison.Ordinal)
                    && backend.Installer.Entries.Any(entry=>pair.Key=="card-"+entry.Identity)).ToDictionary(pair=>pair.Key,pair=>pair.Value.Max-pair.Value.Min);
                fixtureImages!.CompleteLoading();
                host.Frame(Draw);CheckCardBounds();
                foreach(var (key,size) in readyDimensions)
                    if(bounds.TryGetValue(key,out var current) && Vector2.Distance(size,current.Max-current.Min)>.5f)
                        throw new Exception("A media readiness transition resized a card.");
                if(suffix=="wide") WaitForPreview("https://fixture.invalid/loading.bmp",false);
                Settle();CheckCardBounds();
                if(!Equals(anchorBefore,anchorField.GetValue(main))) throw new Exception("Media readiness moved the logical browse anchor.");
                if(suffix=="wide")
                {
                    var steady=backend.Installer;
                    var first=bounds["card-"+identity];
                    requestedScroll=browseScrollMax;scrollRequest=true;Settle();CheckCardBounds();
                    if(browseScroll<=0 || Math.Abs(browseScroll-browseScrollMax)>1) throw new Exception("Variable card rows lost the bottom scroll extent.");
                    var outlier=InstallerCatalog.PluginIdentity(source,"Fixture40");
                    if(!bounds.ContainsKey("card-"+outlier))
                    {
                        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                        var current=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",flags)!.Invoke(main,null)!;
                        var index=Enumerable.Range(0,current.Count).Single(i=>current[i].Identity==outlier);
                        var columns=(int)typeof(MainWindow).GetField("previousColumns",flags)!.GetValue(main)!;
                        var rows=(float[])typeof(MainWindow).GetField("cardRowOffsets",flags)!.GetValue(main)!;
                        requestedScroll=rows[index/columns];scrollRequest=true;Settle();
                    }
                    var tall=bounds["card-"+outlier];
                    SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-outlier-bottom.png"),host);
                    backend.Installer=steady with { Entries=steady.Entries.Select(entry=>entry.Manifest.InternalName=="Fixture40"
                        ?entry with { Manifest=entry.Manifest with { Name="Plugin 40" } }:entry).ToArray() };
                    typeof(MainWindow).GetField("nextInstallerSnapshot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,DateTime.MinValue);
                    Settle();
                    var shortCard=bounds["card-"+outlier];
                    if(Vector2.Distance(tall.Max-tall.Min,shortCard.Max-shortCard.Min)>.5f)
                        throw new Exception("Changing a multiline title resized its card instead of retaining the shared allocation.");
                    backend.Installer=steady;
                    typeof(MainWindow).GetField("nextInstallerSnapshot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,DateTime.MinValue);
                    Settle();
                    requestedScroll=0;scrollRequest=true;Settle();CheckCardBounds();
                    var unchanged=bounds["card-"+identity];
                    if(Vector2.Distance(first.Max-first.Min,unchanged.Max-unchanged.Min)>.5f)
                        throw new Exception("A distant title outlier changed another card's dimensions or column packing.");
                    requestedScroll=browseScrollMax*.5f;scrollRequest=true;Settle();
                    var densityAnchor=anchorField.GetValue(main);
                    var priorDensity=theme.Density;
                    theme.Density=priorDensity==MaterialDensity.Compact?MaterialDensity.Standard:MaterialDensity.Compact;
                    Settle();CheckCardBounds();
                    if(!Equals(densityAnchor,anchorField.GetValue(main)))
                        throw new Exception("An in-place density toggle moved the logical browse anchor.");
                    theme.Density=priorDensity;Settle();CheckCardBounds();
                    if(!Equals(densityAnchor,anchorField.GetValue(main)))
                        throw new Exception("Restoring density moved the logical browse anchor.");
                    requestedScroll=0;scrollRequest=true;Settle();
                }
            }
            if(language=="en" && !compact && suffix.Length==0)
            {
                var pinId="card-pin-"+InstallerCatalog.PluginIdentity(InstallerCatalog.OfficialRepository,"Fixture2");
                Click(pinId);
                if(!backend.Configuration.PinnedRepositories.Contains(InstallerCatalog.OfficialRepository) || backend.Actions.Count!=0) throw new Exception("Card favourite dispatched source/plugin work.");
                Click(pinId);
                if(backend.Configuration.PinnedRepositories.Contains(InstallerCatalog.OfficialRepository)) throw new Exception("Card favourite did not toggle its selected source.");
                Click("details-2");
                if(backend.Actions.Single()!="Install:"+InstallerCatalog.OfficialRepository+":Fixture2") throw new Exception("Available card did not dispatch its exact native Install action.");
                backend.Actions.Clear();
                var unrelated=backend.Installer.Entries.First(e=>e.Manifest.InternalName=="Fixture0");
                unrelated=unrelated with { Manifest=unrelated.Manifest with { InternalName="botology" } };
                if(typeof(MainWindow).GetMethod("InstallerIcon",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[unrelated]) is not null)
                    throw new Exception("An uninstalled same-name source borrowed the running plugin's branding.");
                var sort=typeof(MainWindow).GetField("installerSort",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                sort.SetValue(main,3);
                var sorted=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(!sorted.TakeLast(2).Select(e=>e.Manifest.InternalName).SequenceEqual(["Fixture2","Fixture6"])) throw new Exception("Version sorting ordered 10.0 before 2.0.");
                sort.SetValue(main,0); Settle();
                using(var locale=text.Enter()) using(var body=host.PushFont(UiFontRole.Body))
                {
                    var fit=typeof(MainWindow).GetMethod("FitSourceLabel",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
                    var labels=backend.Installer.Repositories.Where(r=>r.Name.StartsWith("fixture-owner/",StringComparison.Ordinal))
                        .Select(r=>(string)fit.Invoke(null,[r.Name,170f])!).ToArray();
                    if(labels.Distinct(StringComparer.Ordinal).Count()!=3) throw new Exception("Card label ellipses concealed distinct GitHub sources.");
                    var compatibility=typeof(MainWindow).GetMethod("CardCompatibilityLabel",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
                    var assessed=backend.Installer.Entries.Single(e=>e.Manifest.InternalName=="Fixture3" && e.IsInstalled);
                    if(!((string)compatibility.Invoke(null,[assessed])!).Contains("Yellow",StringComparison.Ordinal)
                        || !((string)compatibility.Invoke(null,[backend.Installer.Entries.Single(e=>e.Manifest.InternalName=="Fixture0")])!).Contains("Not assessed",StringComparison.Ordinal))
                        throw new Exception("Card compatibility fabricated an assessment or hid a known warning.");
                    if(!((string)compatibility.Invoke(null,[assessed with { Compatibility=assessed.Compatibility! with { Ignored=true } }])!).Contains("Ignored",StringComparison.Ordinal))
                        throw new Exception("Card compatibility discarded the existing ignore choice.");
                }
            }
            if(language is "en" or "ru" or "ja" or "hi") SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-folded.png"),host);
            CheckCardBounds();
            foreach(var index in new[]{0,1})
                if(bounds.TryGetValue("card-text-end-"+InstallerCatalog.PluginIdentity(source,"Fixture"+index),out var content)
                    && bounds.TryGetValue("details-"+index,out var action) && action.Min.Y<content.Max.Y)
                    throw new Exception($"Card action overlaps translated text: {language}/{suffix}; action={action.Min.Y}, textEnd={content.Max.Y}.");
            if(language=="en")
            {
                using var locale=text.Enter();
                var state=typeof(MainWindow).GetMethod("EntryState",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
                var entry=backend.Installer.Entries.Single(e=>e.Manifest.InternalName=="Fixture1");
                if(!((string)state.Invoke(null,[entry])!).Contains("LoadError",StringComparison.Ordinal)) throw new Exception("A load error was shown as disabled.");
            }
            if(backend.Actions.Count!=0) throw new Exception("Browsing dispatched an action.");
            var folded=bounds["installer-search"].Min.X;
            if(language=="en")
            {
                scrollRequest=true; Settle();
                var anchor=typeof(MainWindow).GetField("browseAnchor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                var before=(string?)anchor.GetValue(main);
                Click("pins-toggle");Click("pins-toggle");
                if(before is null || !Equals(before,anchor.GetValue(main))) throw new Exception("Folding the panel lost the logical scroll anchor.");
                Click("categories-toggle");Click("categories-toggle");
                if(!Equals(before,anchor.GetValue(main))) throw new Exception("Folding categories lost the logical scroll anchor.");
                scrollRequest=true;
                requestedScroll=0;
                Settle();
            }
            Click("pins-toggle");
            if(bounds["installer-search"].Min.X<folded+100) throw new Exception("Expanded pins did not reflow results.");
            if(bounds["repo-pin-name-"+source].Max.X>bounds["repo-pin-"+source].Max.X-4*scale)
                throw new Exception("Translated rail status resized or clipped the source label: "+language);
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru" or "ja" or "hi") SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-expanded.png"),host);
            // Click the actual rail row, including the status line which used to be inert.
            scrollRequest=true; requestedScroll=680; Settle();
            var repoRow=bounds["repo-pin-"+source];
            host.Click(new((repoRow.Min.X+repoRow.Max.X)*.5f,repoRow.Min.Y+(repoRow.Max.Y-repoRow.Min.Y)*.75f),Draw); Settle();
            var railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(railEntries.Count!=36 || railEntries.Any(e=>e.Repository.Url!=source) || browseScroll>.5f || !bounds.ContainsKey("active-pin-filter"))
                throw new Exception($"Pinned repository status-line click did not filter and reveal its results: {language}; results={railEntries.Count}, scroll={browseScroll}, active={bounds.ContainsKey("active-pin-filter")}.");
            repoRow=bounds["repo-pin-"+source];
            host.Click(new((repoRow.Min.X+repoRow.Max.X)*.5f,repoRow.Min.Y+(repoRow.Max.Y-repoRow.Min.Y)*.25f),Draw); Settle();
            if(bounds.ContainsKey("active-pin-filter")) throw new Exception("Repeated repository selection did not clear its filter.");
            Click("author-pin-SampleAuthor");
            railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(railEntries.Count!=24 || railEntries.Any(e=>!e.Manifest.Authors.Contains("SampleAuthor"))) throw new Exception("Actual author rail row did not filter its credited plugins.");
            Click("author-pin-SampleAuthor");
            Click("filter-Not available on this API");
            railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(railEntries.Count!=2 || !railEntries.Any(e=>e.Manifest.InternalName=="StateApi") || !railEntries.Any(e=>e.Installed?.IsOutdated==true))
                throw new Exception("API filter must include incompatible catalog entries and installed outdated entries.");
            if(language is "en" or "ru") SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-api-filter.png"),host);
            Click("repo-pin-"+source);
            railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(railEntries.Count!=1 || railEntries[0].Manifest.InternalName!="Fixture7") throw new Exception("API and repository filters did not intersect.");
            Click("clear-pin-filter");
            if(bounds.ContainsKey("clear-installer-filters") || bounds.ContainsKey("active-pin-filter")) throw new Exception("Clear filter left hidden active criteria.");
            Click("filter-Not installed");
            railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(railEntries.Any(e=>e.IsInstalled)) throw new Exception("Not installed filter included an installed plugin.");
            Click("filter-Not installed");
            if(bounds.ContainsKey("clear-installer-filters")) throw new Exception("Selected status chip did not toggle back to All plugins.");
            Click("filter-Installed"); Click("filter-Repository not added");
            Click("filter-Not installed",ImGuiMouseButton.Right); Click("filter-Not available on this API",ImGuiMouseButton.Right);
            railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(railEntries.Count!=3) throw new Exception("Multiple required and blocked status filters did not coexist.");
            if(language is "en" or "ru") SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-require-block.png"),host);
            Click("repo-pin-"+source,ImGuiMouseButton.Right);
            railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(railEntries.Count!=0) throw new Exception("A blocked repository still contributed matching plugins.");
            Click("repo-pin-"+source);
            railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(railEntries.Count!=3) throw new Exception("Left click did not replace a blocked repository with a requirement.");
            Click("repo-pin-"+source); Click("clear-installer-filters");
            if(language=="en" && !compact && suffix.Length==0)
            {
                backend.Configuration.PinnedRepositories.Add(InstallerCatalog.OfficialRepository);
                backend.Configuration.PinnedAuthors.Add("OtherAuthor"); Settle();
                Click("repo-pin-"+source); Click("repo-pin-"+InstallerCatalog.OfficialRepository);
                railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(railEntries.Count!=53) throw new Exception("Required repositories must include either selected source.");
                Click("author-pin-SampleAuthor"); Click("author-pin-OtherAuthor");
                railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(railEntries.Count!=53) throw new Exception("Required authors must include either credited author.");
                Click("repo-pin-"+source,ImGuiMouseButton.Right); Click("author-pin-SampleAuthor",ImGuiMouseButton.Right);
                railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(railEntries.Count!=11) throw new Exception("Multiple blocked sources/authors did not override matching requirements.");
                SavePreview(Path.Combine(output,"en-multiple-pinned-filters.png"),host);
                Click("author-pin-OtherAuthor",ImGuiMouseButton.Right);
                railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(railEntries.Count!=0) throw new Exception("Opposite click did not switch a required author to blocked.");
                Click("author-pin-OtherAuthor",ImGuiMouseButton.Right);
                railEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(railEntries.Count!=11) throw new Exception("Repeated right click did not clear the blocked author.");
                Click("clear-pin-filter");
                var cleared=CategoryResults();
                var clearAnchor=typeof(MainWindow).GetField("browseAnchor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(main);
                if(browseScroll>.5f || !Equals(clearAnchor,cleared[0].Identity) || !bounds.ContainsKey("details-0"))
                    throw new Exception("Clear filter retained the old filtered anchor instead of revealing the beginning of the new results.");
                backend.Configuration.PinnedRepositories.Remove(InstallerCatalog.OfficialRepository);
                backend.Configuration.PinnedAuthors.Remove("OtherAuthor"); Settle();
            }
            if(backend.Actions.Count!=0) throw new Exception("Rail/filter clicks dispatched a repository or plugin mutation.");
            Click("pins-toggle");
            if(Math.Abs(bounds["installer-search"].Min.X-folded)>1) throw new Exception("Folded pins retained an empty sidebar.");
            Click("view-table");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SavePreview(Path.Combine(output,language+"-"+(compact?"table-compact.png":"table-regular.png")),host);
            if(language=="en" && !compact && suffix.Length==0)
            {
                Click("installer-column-Author");
                var byAuthor=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(byAuthor[0].Manifest.Author!="OtherAuthor") throw new Exception("Native author sorting lost its column identity.");
                Click("installer-column-Plugin");
            }
            Click("pins-toggle");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SavePreview(Path.Combine(output,language+"-"+(compact?"table-compact-expanded.png":"table-regular-expanded.png")),host);
            Click("pins-toggle");
            if(backend.Actions.Count!=0) throw new Exception("View switching dispatched an action.");
            Click("view-cards"); Click("details-0");
            WaitForPreview(still,true);
            if(bounds["profile-source-content"].Max.Y>bounds["profile-source-surface"].Max.Y+1) throw new Exception("The profile source panel clipped its own content: "+language);
            if(!bounds.ContainsKey("profile-pin-repo") || backend.Actions.Count!=0) throw new Exception("Unadded-source details lost interactivity or mutated the source.");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SavePreview(Path.Combine(output,language+"-"+(compact?"profile-compact.png":"profile-regular.png")),host);
            if(fixtureMedia is not null)
            {
                WaitForPreview(gif,false); Click("media-1"); WaitForPreview(gif,true); Settle();
                if(bounds.ContainsKey("media-2")) throw new Exception("A missing publisher image left a gallery placeholder.");
                Click("media-play");
                if(!fixtureMedia.IsPlaying(gif))
                {
                    SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-gallery-play-failed.png"),host);
                    throw new Exception($"The native gallery Play action did not start its selected GIF: {language}/{compact}; button={bounds["media-play"]}, gallery clip={galleryClip}, content clip={contentClip}.");
                }
                Click("media-play");
                if(fixtureMedia.IsPlaying(gif)) throw new Exception("The native gallery Stop action retained playback.");
                Click("media-0"); WaitForPreview(still,true);
            }
            Click("pins-toggle");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SavePreview(Path.Combine(output,language+"-"+(compact?"profile-compact-expanded.png":"profile-regular-expanded.png")),host);
            Click("repo-pin-"+source);
            if(!bounds.ContainsKey("installer-search") || !bounds.ContainsKey("active-pin-filter") || browseScroll>.5f)
                throw new Exception("Pinned repository did not return from the profile to visible filtered results.");
            Click("clear-pin-filter"); Click("details-0");
            Click("pins-toggle");
            Click("profile-back"); Click("nav-Pinned sources");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SavePreview(Path.Combine(output,language+"-"+(compact?"pins-compact.png":"pins-regular.png")),host);
            Click("pins-toggle");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(clippedPin is not null) throw new Exception("Pinned action exceeds its surface: "+language+"/"+suffix+"/"+clippedPin);
            if(language is "en" or "ru") SavePreview(Path.Combine(output,language+"-"+(compact?"pins-compact-expanded.png":"pins-regular-expanded.png")),host);
            Click("pins-toggle"); Click("pin-repo-view-"+source);
            var filtered=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(filtered.Count!=36 || filtered.Any(e=>e.Repository.Url!=source)) throw new Exception("A repository pin did not filter its own plugins.");
            Click("clear-pin-filter"); Click("nav-Pinned sources"); Click("pin-author-view-SampleAuthor");
            filtered=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
            if(filtered.Count!=24 || filtered.Any(e=>!e.Manifest.Authors.Contains("SampleAuthor"))) throw new Exception("An author pin did not filter its credited plugins.");
            Click("clear-pin-filter"); Click("details-0");
            Click("profile-pin-repo");
            if(backend.Configuration.PinnedRepositories.Contains(source)) throw new Exception("Native unpin did not update existing configuration.");
            Click("profile-back"); Click("nav-Pinned sources");
            if(backend.Configuration.PinnedRepositories.Count!=1 || backend.Configuration.PinnedAuthors.Count!=2) throw new Exception("Hidden invalid pins were deleted.");
            Click("nav-Compatibility");
            if(actualGrid==0 || actualGrid!=expectedGrid) throw new Exception("Installer navigation changed the retained compatibility table identity.");
            Click("nav-Repositories");
            if(backend.Actions.Count!=0) throw new Exception("Reviewing repositories dispatched an operation.");
            Click("repo-add-"+source);
            if(backend.Actions.Count!=1 || backend.Actions[0]!="AddRepository:"+source+":") throw new Exception("Explicit source add did not dispatch exactly the selected repository.");
            Click("nav-Discover");
            var stateSnapshot=backend.Installer;
            typeof(MainWindow).GetField("installerSearch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,"State ");
            for(var stateMode=0;stateMode<3;stateMode++)
            {
                backend.Installer=stateSnapshot with { Ready=stateMode!=1,SafeMode=stateMode==2,
                    Entries=stateSnapshot.Entries.Select(entry=>entry with { HostReady=stateMode!=1,HostSafeMode=stateMode==2 }).ToArray() };
                typeof(MainWindow).GetField("nextInstallerSnapshot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,DateTime.MinValue);
                Settle();
                var stateEntries=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(stateEntries.Count!=7 || stateEntries.Count(e=>e.CanInstall)!=(stateMode==0?1:0)) throw new Exception("Native mixed eligibility states did not preserve exact install guards.");
                for(var entryIndex=0;entryIndex<stateEntries.Count;entryIndex++)
                {
                    var identity=stateEntries[entryIndex].Identity;
                    if(!bounds.TryGetValue("card-"+identity,out var card)) continue;
                    var inset=bounds["card-state-"+identity].Min.X-card.Min.X;
                    var hasAction=bounds.TryGetValue("details-"+entryIndex,out var action);
                    if(bounds["card-state-"+identity].Max.X>card.Max.X-inset+.5f || bounds["card-compatibility-"+identity].Max.X>card.Max.X-inset+.5f
                        || hasAction && (bounds["card-text-end-"+identity].Min.Y>action.Min.Y || action.Max.X>card.Max.X-inset+.5f))
                    {
                        SavePreview(Path.Combine(output,language+(compact?"-compact":"-regular")+"-state-ink-failed.png"),host);
                        throw new Exception($"Translated eligibility/compatibility ink exceeds its card: {language}/{compact}/{suffix}/{stateMode}/{identity}; card={card}, inset={inset}, state={bounds["card-state-"+identity]}, compatibility={bounds["card-compatibility-"+identity]}, textEnd={bounds["card-text-end-"+identity]}, action={bounds.GetValueOrDefault("details-"+entryIndex)}.");
                    }
                }
                if(backend.Actions.Count!=1) throw new Exception("Eligibility-state browsing dispatched an operation.");
                if(language=="en") SavePreview(Path.Combine(output,$"en-states-{(compact?"compact":"regular")}-{stateMode}.png"),host);
            }
            Console.WriteLine($"Native installer case passed: {language}/{(compact?"Compact":"Regular")}/{width}x{height}/{scale:F1}/{suffix}.");
            checkedCases++;checkedLanguages.Add(language);
        }
        Console.WriteLine("Native installer checks passed in "+checkedLanguages.Count+" locales and "+checkedCases+" size/density/scale/theme cases: cards/table, folded rail reflow, clickable unadded details, pin changes and side-effect-free browsing.");
    }

    internal static unsafe void CheckUpdateAll(bool lcars)
    {
        using var host=new NativeTestContext(1200,960,1,"en");
        using var icon=new BotologyOriginalIcon(host);
        using var text=new UiText("en",host.PushFont);
        var backend=new SnapshotUi(true,0x1CC9E6,"en");
        const string source=InstallerCatalog.OfficialRepository;
        var repository=new InstallerRepository(source,"Dalamud",true,true,true,"Success");
        var entries=new[]{"Visible","Hidden"}.Select(name=>new InstallerEntry(new(name,name,source,ApiLevel:15),repository,
            new(new PluginRuntimeState(name,name,new(1,0),false,true,null,null,new object(),null,null,null),source,"Unloaded"),null,false,true)).ToArray();
        var snapshot=new InstallerCatalogSnapshot([repository],entries,true,false);
        backend.Installer=snapshot;
        using var main=new MainWindow(backend);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(MainWindow).GetField("installerPage",flags)!.SetValue(main,"Updates");
        typeof(MainWindow).GetField("installerSearch",flags)!.SetValue(main,"no visible matches");
        var bounds=new Dictionary<string,(Vector2 Min,Vector2 Max)>();
        main.MeasureBounds=(id,min,max)=>bounds[id]=(min,max);
        var theme=BotologyPresentation.Theme(0x1CC9E6,new() { Mode=lcars?MaterialColorMode.Lcars:MaterialColorMode.Auto });
        theme.Motion.Enabled=false;
        void Draw()
        {
            using var locale=text.Enter(); using var material=MaterialTheme.Push(theme,1,MaterialStyleMode.ColorsOnly);
            using var geometry=BotologyPresentation.PushHostGeometry(1,false); using var chrome=MaterialWindowChrome.Push();
            main.PreDraw(); ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new(1200,960));
            var open=true; ImGui.Begin(main.WindowName,ref open,main.Flags|ImGuiWindowFlags.NoSavedSettings);
            main.Draw(); ImGui.End(); main.PostDraw();
        }
        void Settle()
        {
            typeof(MainWindow).GetField("nextInstallerSnapshot",flags)!.SetValue(main,DateTime.MinValue);
            for(var frame=0;frame<4;frame++) { bounds.Clear();host.Frame(Draw); }
        }
        void Click()
        {
            var button=bounds["installer-update-all"];
            host.Click((button.Min+button.Max)*.5f,Draw);
            Settle();
        }
        Settle();
        if(backend.Actions.Count!=0 || !bounds.ContainsKey("installer-update-all")) throw new Exception("Opening Updates must expose Update all without dispatching it.");
        var visible=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",flags)!.Invoke(main,null)!;
        if(visible.Count!=0) throw new Exception("Update-all filter fixture must hide every eligible plugin.");
        Click();
        if(!backend.Actions.SequenceEqual(["UpdateAll::"])) throw new Exception("An explicit Update all click must dispatch one batch even when all eligible rows are filtered out.");
        backend.Actions.Clear();
        typeof(MainWindow).GetField("installerSearch",flags)!.SetValue(main,"");
        typeof(MainWindow).GetField("selectedInstaller",flags)!.SetValue(main,entries[0].Identity);Settle();
        var rowUpdate=bounds["plugin-action-Update"];
        host.Click((rowUpdate.Min+rowUpdate.Max)*.5f,Draw);Settle();
        if(!backend.Actions.SequenceEqual(["Update:"+source+":Visible"])) throw new Exception("The existing individual Update action must retain its source and plugin identity.");
        backend.Actions.Clear();
        typeof(MainWindow).GetField("selectedInstaller",flags)!.SetValue(main,null);
        typeof(MainWindow).GetField("installerSearch",flags)!.SetValue(main,"no visible matches");
        foreach(var blocked in new[]{"empty","not-ready","safe-mode","running"})
        {
            backend.Installer=snapshot with { Ready=blocked!="not-ready",SafeMode=blocked=="safe-mode",Entries=blocked=="empty"?[]:entries };
            backend.InstallerOperation=blocked=="running"?new("",InstallerAction.UpdateAll,true,false,"Working..."):null;
            Settle();Click();
            if(backend.Actions.Count!=0) throw new Exception("Update all must stay disabled for "+blocked+".");
        }
        backend.Installer=snapshot;
        backend.InstallerOperation=new("",InstallerAction.UpdateAll,false,false,
            "Update all stopped after {0} of {1} updates: {2}. {3}") { MessageArguments=[1,2,"Hidden ("+source+")","FailedDownload"] };
        Settle();
        if(backend.Actions.Count!=0) throw new Exception("Displaying a partial-update result must not dispatch more work.");
        backend.InstallerOperation=null;
        typeof(MainWindow).GetField("installerSearch",flags)!.SetValue(main,"");
        foreach(var channel in new (int Stable,int? Testing,bool UseTesting,bool Eligible)[]
        {
            (14,15,true,true),(15,14,true,false),(15,null,true,true),(14,null,true,false),
            (14,15,false,false),(15,14,false,true),(15,null,false,true),
        })
        {
            var nativeRepo=new { PluginMasterUrl=source,IsEnabled=true,State="Success",IsThirdParty=false };
            var nativeLocal=new { InternalName="ChannelFixture",IsThirdParty=false,IsDev=false,IsTesting=false,IsBanned=false,State="Unloaded",
                Manifest=new { InstalledFromUrl=source,ScheduledForDeletion=false } };
            var nativeManifest=new { InternalName=nativeLocal.InternalName,Name="Channel fixture",SourceRepo=nativeRepo,
                DalamudApiLevel=channel.Stable,TestingDalamudApiLevel=channel.Testing };
            var manager=new { Repos=new[]{nativeRepo},AvailablePlugins=new[]{nativeManifest},PluginsReady=true,ReposReady=true,SafeMode=false,
                UpdatablePlugins=new[]{new { InstalledPlugin=nativeLocal,UpdateManifest=nativeManifest,UseTesting=channel.UseTesting }} };
            var runtime=new PluginRuntimeState(nativeLocal.InternalName,nativeLocal.InternalName,new(1,0),false,true,null,null,nativeLocal,null,null,null);
            backend.Installer=InstallerCatalog.Merge(PluginManagerBridge.CaptureInstallerHost(manager,new { ThirdRepoList=Array.Empty<object>() },new([runtime])),[],[],[]);
            var entry=backend.Installer.Entries.Single();
            var description=$"stable {channel.Stable}, testing {channel.Testing}, UseTesting {channel.UseTesting}, LCARS {lcars}";
            if(entry.CanUpdate!=channel.Eligible || (entry with { Installed=null }).CanInstall!=(channel.Stable==15))
                throw new Exception("Captured update/stable Install gates disagree with native channels: "+description);
            Settle();
            visible=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",flags)!.Invoke(main,null)!;
            if(visible.Count!=(channel.Stable==15?1:0)) throw new Exception("Default API filtering must retain stable manifest semantics: "+description);
            Click();
            if(!backend.Actions.SequenceEqual(channel.Eligible?new[]{"UpdateAll::"}:Array.Empty<string>()))
                throw new Exception("Update all must use selected-channel eligibility even when the default filter hides the only testing update: "+description);
            backend.Actions.Clear();
            typeof(MainWindow).GetField("selectedInstaller",flags)!.SetValue(main,entry.Identity);Settle();
            rowUpdate=bounds["plugin-action-Update"];
            host.Click((rowUpdate.Min+rowUpdate.Max)*.5f,Draw);Settle();
            if(!backend.Actions.SequenceEqual(channel.Eligible?new[]{"Update:"+source+":"+nativeLocal.InternalName}:Array.Empty<string>()))
                throw new Exception("Individual Update must retain native selected-channel eligibility: "+description);
            backend.Actions.Clear();
            typeof(MainWindow).GetField("selectedInstaller",flags)!.SetValue(main,null);
        }
        typeof(MainWindow).GetField("installerPage",flags)!.SetValue(main,"Discover");Settle();
        if(bounds.ContainsKey("installer-update-all")) throw new Exception("Update all must be scoped to the Updates page.");
        Console.WriteLine("Native Update all interaction passed: explicit click, hidden eligible rows, host gates, selected-channel API/fallback matrix, individual Update and terminal result; LCARS "+lcars+".");
    }
    // Fabricated image data stays in this fixture and never enters publisher metadata or product fallbacks.
    private sealed class PreviewImages : HttpMessageHandler
    {
        private readonly byte[] screenshot;
        private readonly TaskCompletionSource loading=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal PreviewImages(int width,int height) { screenshot=Screenshot(width,height); }
        internal void CompleteLoading()=>loading.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri?.Host!="fixture.invalid") throw new InvalidOperationException("Unexpected fixture image host.");
            var path=request.RequestUri.AbsolutePath;
            if(path=="/missing.bmp") return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            if(path=="/failed.bmp") return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            if(path=="/loading.bmp") await loading.Task.WaitAsync(token);
            var bytes=path switch { "/icon.bmp"=>Screenshot(48,48),"/portrait.bmp"=>Screenshot(96,192),"/square.bmp"=>Screenshot(192,192),
                _=>path.EndsWith(".gif",StringComparison.Ordinal)?InstallerMediaTests.Gif():screenshot };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content=new ByteArrayContent(bytes) };
        }
        private static byte[] Screenshot(int width,int height)
        {
            using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
            writer.Write((ushort)0x4D42); writer.Write(54+width*height*4);writer.Write(0);writer.Write(54);
            writer.Write(40);writer.Write(width);writer.Write(height);writer.Write((ushort)1);writer.Write((ushort)32);
            writer.Write(0);writer.Write(width*height*4);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
            for(var y=0;y<height;y++) for(var x=0;x<width;x++)
            {
                // A fabricated dense UI screenshot, with all geometry relative to this source image.
                var top=y>height*.86f;var sidebar=x<width*.22f;
                var row=(int)(y/(height*.12f));
                var text=y%(Math.Max(1,(int)(height*.12f)))<Math.Max(1,height*.025f)
                    && x>width*(sidebar?.035f:.28f) && x<width*(sidebar?.17f:row%2==0?.86f:.69f);
                var tile=x>width*.89f && x<width*.96f && y<height*.8f && row%2==0;
                var blue=text?190:tile?110:top?65:sidebar?40:48;
                var green=text?205:tile?170:top?48:sidebar?30:36;
                var red=text?215:tile?55:top?35:sidebar?23:26;
                writer.Write((byte)blue);writer.Write((byte)green);writer.Write((byte)red);writer.Write((byte)255);
            }
            return stream.ToArray();
        }
    }
    private sealed class PreviewTexture : IDalamudTextureWrap
    {
        private readonly MaterialTextTexture texture;
        public ImTextureID Handle=>texture.Id;
        public int Width { get; }
        public int Height { get; }
        internal PreviewTexture(NativeTestContext host,PreviewBitmap frame)
        {
            Width=frame.Width;Height=frame.Height;
            var rgba=frame.Bgra.ToArray();
            for(var i=0;i<rgba.Length;i+=4) (rgba[i],rgba[i+2])=(rgba[i+2],rgba[i]);
            texture=host.RegisterBitmap(new(Width,Height,Vector2.Zero,rgba));
        }
        public void Dispose()=>texture.Dispose();
        public IDalamudTextureWrap CreateWrapSharingLowLevelResource()=>throw new NotSupportedException();
    }

}

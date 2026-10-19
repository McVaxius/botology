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
    internal static void Check(string[]? languages=null)
    {
        BotologyUiSmoke.CheckPreferencesAndResources();
        var selectedLanguages=(languages is { Length:>0 }?languages:UiText.Languages.Select(l=>l.Code)).ToArray();
        var cases=selectedLanguages.SelectMany(language=>new[]{false,true}.Select(compact=>(language,compact,width:1440,height:960,scale:1f,accent:0x1CC9E6u,suffix:""))).ToList();
        if(selectedLanguages.Contains("en"))
        {
            foreach(var compact in new[]{false,true}) cases.Add(("en",compact,800,816,1,0x1CC9E6,"reference"));
            cases.Add(("en",false,1200,1224,1.5f,0x1CC9E6,"scaled"));
            cases.Add(("en",true,1240,1000,1,0x808080,"grey"));
            cases.Add(("en",false,1240,1000,1,0x000000,"black"));
            cases.Add(("en",true,1240,1000,1,0xFFFFFF,"white"));
        }
        if(selectedLanguages.Contains("ru")) cases.Add(("ru",true,1860,1440,1.5f,0xCF79AE,"translated-scaled-custom"));
        foreach(var scenario in cases)
        {
            var (language,compact,width,height,scale,accent,suffix)=scenario;
            using var host=new NativeTestContext(width,height,scale,language);
            using var icon=new BotologyOriginalIcon(host);
            using var text=new UiText(language,host.PushFont);
            var backend=new SnapshotUi(false,accent,language);
            const string source="https://fixture.invalid/plugins.json";
            var manifests=Enumerable.Range(0,48).Select(i=>new InstallerManifest("Fixture"+i,"Plugin "+i.ToString("00"),i%4==2?InstallerCatalog.OfficialRepository:source,i<24?"SampleAuthor":"OtherAuthor",
                "Publisher description.",i==2?"2.0":i==6?"10.0":"1.0",15,Tags:["Utility"])).ToArray();
            var withMedia=suffix is "reference" or "scaled" or "translated-scaled-custom";
            const string still="https://fixture.invalid/layout.bmp",gif="https://fixture.invalid/preview.gif";
            if(withMedia)
            {
                manifests[0]=manifests[0] with { ImageUrls=[still,gif,"https://fixture.invalid/missing.bmp"] };
                manifests[1]=manifests[1] with { ImageUrls=[gif] };
            }
            var errorRuntime=new PluginRuntimeState("Fixture1","Plugin 01",new(1,0),false,false,null,null,new object(),null,null,null);
            var loaded=new PluginRuntimeState("Fixture3","Plugin 03",new(1,0),true,false,null,null,new object(),null,null,null);
            var updating=new PluginRuntimeState("Fixture4","Plugin 04",new(1,0),false,true,null,null,new object(),null,null,null);
            backend.Installer=InstallerCatalog.Merge(new([new(InstallerCatalog.OfficialRepository,"Dalamud",true,true,true,"Success")],
                manifests.Where(m=>m.RepositoryUrl==InstallerCatalog.OfficialRepository).ToArray(),
                [new(errorRuntime,source,"LoadError"),new(loaded,source,"Loaded"),new(updating,source,"Unloaded")],true,false),manifests,[],[]);
            backend.Configuration.PinnedRepositories=[source,"invalid"];
            backend.Configuration.PinnedAuthors=["SampleAuthor","Invalid Author"];
            using var main=new MainWindow(backend);
            using var fixtureMedia=withMedia?new InstallerMedia(frame=>new PreviewTexture(host,frame),new HttpClient(new PreviewImages())):null;
            if(fixtureMedia is not null) typeof(MainWindow).GetField("installerMedia",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,fixtureMedia);
            var bounds=new Dictionary<string,(Vector2 Min,Vector2 Max)>();
            var scrollRequest=false;
            var requestedScroll=680f;
            uint expectedGrid=0,actualGrid=0;
            string? clippedPin=null;
            main.MeasureBounds=(name,min,max)=>
            {
                bounds[name]=(min,max);
                if(name=="table-header") actualGrid=ImGuiP.GetCurrentTable().ID;
                if(name.StartsWith("pin-",StringComparison.Ordinal) && max.X>ImGui.GetWindowPos().X+ImGui.GetWindowContentRegionMax().X+.5f) clippedPin=name;
                if(name=="installer-browse" && scrollRequest) { ImGui.SetScrollY(requestedScroll);scrollRequest=false; }
            };
            var theme=BotologyPresentation.Theme(accent); theme.Density=compact?MaterialDensity.Compact:MaterialDensity.Standard; theme.Motion.Enabled=false;
            void Draw()
            {
                bounds.Clear(); clippedPin=null; using var locale=text.Enter(); using var material=MaterialTheme.Push(theme,scale,MaterialStyleMode.ColorsOnly);
                using var geometry=BotologyPresentation.PushHostGeometry(scale,compact);
                using var chrome=MaterialWindowChrome.Push();
                ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new(width,height));
                ImGui.Begin("Botology##Main",main.Flags|ImGuiWindowFlags.NoSavedSettings);
                expectedGrid=ImGui.GetID("BotologyGrid");
                main.Draw(); ImGui.End();
            }
            void Settle() { for(var i=0;i<4;i++) host.Frame(Draw); }
            void Click(string id) { if(!bounds.TryGetValue(id,out var rect)) throw new Exception("Missing installer action: "+id); host.Click((rect.Min+rect.Max)*.5f,Draw); Settle(); }
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
            Settle();
            WaitForPreview(still,false); WaitForPreview(gif,false);
            if(language=="en" && !compact && suffix.Length==0)
            {
                var unrelated=backend.Installer.Entries.First(e=>e.Manifest.InternalName=="Fixture0");
                unrelated=unrelated with { Manifest=unrelated.Manifest with { InternalName="botology" } };
                if(typeof(MainWindow).GetMethod("InstallerIcon",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[unrelated]) is not null)
                    throw new Exception("An uninstalled same-name source borrowed the running plugin's branding.");
                var sort=typeof(MainWindow).GetField("installerSort",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                sort.SetValue(main,3);
                var sorted=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(!sorted.TakeLast(2).Select(e=>e.Manifest.InternalName).SequenceEqual(["Fixture2","Fixture6"])) throw new Exception("Version sorting ordered 10.0 before 2.0.");
                sort.SetValue(main,0); Settle();
            }
            var output=Path.Combine(AppContext.BaseDirectory,"artifacts","installer",suffix);Directory.CreateDirectory(output);
            if(language is "en" or "ru" or "ja" or "hi") SoftwarePreview.Save(Path.Combine(output,language+(compact?"-compact":"-regular")+"-folded.png"),host);
            foreach(var label in bounds.Where(p=>p.Key.StartsWith("preview-label-",StringComparison.Ordinal)))
                if(bounds.TryGetValue(label.Key.Replace("preview-label-","preview-icon-"),out var picture) && picture.Max.Y>label.Value.Min.Y)
                    throw new Exception("Missing-media caption overlaps its icon: "+language);
            foreach(var index in new[]{0,1})
                if(bounds.TryGetValue("card-text-end-"+InstallerCatalog.PluginIdentity(source,"Fixture"+index),out var content)
                    && bounds.TryGetValue("details-"+index,out var action) && action.Min.Y<content.Max.Y)
                    throw new Exception("Card action overlaps translated text: "+language);
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
                scrollRequest=true;
                requestedScroll=0;
                Settle();
            }
            Click("pins-toggle");
            if(bounds["installer-search"].Min.X<folded+100) throw new Exception("Expanded pins did not reflow results.");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru" or "ja" or "hi") SoftwarePreview.Save(Path.Combine(output,language+(compact?"-compact":"-regular")+"-expanded.png"),host);
            Click("pins-toggle");
            if(Math.Abs(bounds["installer-search"].Min.X-folded)>1) throw new Exception("Folded pins retained an empty sidebar.");
            Click("view-table");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SoftwarePreview.Save(Path.Combine(output,language+"-"+(compact?"table-compact.png":"table-regular.png")),host);
            if(language=="en" && !compact && suffix.Length==0)
            {
                Click("installer-column-Author");
                var byAuthor=(IReadOnlyList<InstallerEntry>)typeof(MainWindow).GetMethod("FilterInstallerEntries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null)!;
                if(byAuthor[0].Manifest.Author!="OtherAuthor") throw new Exception("Native author sorting lost its column identity.");
                Click("installer-column-Plugin");
            }
            Click("pins-toggle");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SoftwarePreview.Save(Path.Combine(output,language+"-"+(compact?"table-compact-expanded.png":"table-regular-expanded.png")),host);
            Click("pins-toggle");
            if(backend.Actions.Count!=0) throw new Exception("View switching dispatched an action.");
            Click("view-cards"); Click("details-0");
            WaitForPreview(still,true);
            if(bounds["profile-source-content"].Max.Y>bounds["profile-source-surface"].Max.Y+1) throw new Exception("The profile source panel clipped its own content: "+language);
            if(!bounds.ContainsKey("profile-pin-repo") || backend.Actions.Count!=0) throw new Exception("Unadded-source details lost interactivity or mutated the source.");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SoftwarePreview.Save(Path.Combine(output,language+"-"+(compact?"profile-compact.png":"profile-regular.png")),host);
            if(fixtureMedia is not null)
            {
                WaitForPreview(gif,false); Click("media-1"); WaitForPreview(gif,true); Settle();
                if(bounds.ContainsKey("media-2")) throw new Exception("A missing publisher image left a gallery placeholder.");
                Click("media-play");
                if(!fixtureMedia.IsPlaying(gif)) throw new Exception("The native gallery Play action did not start its selected GIF.");
                Click("media-play");
                if(fixtureMedia.IsPlaying(gif)) throw new Exception("The native gallery Stop action retained playback.");
                Click("media-0"); WaitForPreview(still,true);
            }
            Click("pins-toggle");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SoftwarePreview.Save(Path.Combine(output,language+"-"+(compact?"profile-compact-expanded.png":"profile-regular-expanded.png")),host);
            Click("pins-toggle");
            Click("profile-back"); Click("nav-Pinned sources");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(language is "en" or "ru") SoftwarePreview.Save(Path.Combine(output,language+"-"+(compact?"pins-compact.png":"pins-regular.png")),host);
            Click("pins-toggle");
            ImGui.GetIO().AddMousePosEvent(-100,-100);host.Frame(Draw);
            if(clippedPin is not null) throw new Exception("Pinned action exceeds its surface: "+language+"/"+suffix+"/"+clippedPin);
            if(language is "en" or "ru") SoftwarePreview.Save(Path.Combine(output,language+"-"+(compact?"pins-compact-expanded.png":"pins-regular-expanded.png")),host);
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
        }
        Console.WriteLine("Native installer checks passed in "+selectedLanguages.Length+" locales and "+cases.Count+" size/density/scale/theme cases: cards/table, folded rail reflow, clickable unadded details, pin changes and side-effect-free browsing.");
    }
    // Fabricated image data stays in this fixture and never enters publisher metadata or product fallbacks.
    private sealed class PreviewImages : HttpMessageHandler
    {
        private readonly byte[] screenshot=Screenshot();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri?.Host!="fixture.invalid") throw new InvalidOperationException("Unexpected fixture image host.");
            if(request.RequestUri.AbsolutePath=="/missing.bmp") return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content=new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith(".gif",StringComparison.Ordinal)?InstallerMediaTests.Gif():screenshot) });
        }
        private static byte[] Screenshot()
        {
            const int width=320,height=180;
            using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
            writer.Write((ushort)0x4D42); writer.Write(54+width*height*4);writer.Write(0);writer.Write(54);
            writer.Write(40);writer.Write(width);writer.Write(height);writer.Write((ushort)1);writer.Write((ushort)32);
            writer.Write(0);writer.Write(width*height*4);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
            for(var y=0;y<height;y++) for(var x=0;x<width;x++)
            {
                var grid=x%40<2||y%30<2;
                writer.Write((byte)(grid?230:90+x/5));writer.Write((byte)(grid?205:40+y/2));
                writer.Write((byte)(grid?80:20+x/4));writer.Write((byte)255);
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

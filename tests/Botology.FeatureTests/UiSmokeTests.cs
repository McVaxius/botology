using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using botology;
using botology.Models;
using botology.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;

namespace AethertekUI.Tests;

internal static class BotologyUiSmoke
{
    private sealed record Scenario(string Name,string Window,int Width=1472,int Height=996,float Scale=1,uint Accent=0x1CC9E6,
        bool Extras=false,bool Empty=false,bool Detailed=false,string? Popup=null,bool Editable=false,bool Ignored=false,bool Unavailable=false,bool Compact=false);

    public static void Run()
    {
        CheckPreferencesAndResources();
        CheckHindiNativeControls();
        if(Environment.GetEnvironmentVariable("BOTOLOGY_HINDI_ONLY")!="1")
        {
            CheckNativeLocalizedControls();
            CheckReadableFields();
        }
        var originalCulture=CultureInfo.CurrentCulture;
        var rendered=0;
        foreach(var language in UiText.Languages.Select(l=>l.Code))
        {
            var scenarios=new List<Scenario>
            {
                new("main","main"), new("settings","settings",1000,850),
                new("compact","main",Compact:true),new("compact-settings","settings",1000,850,Compact:true),
                new("catalog-readonly","catalog",1500,1000),new("catalog-editable","catalog",1500,1000,Editable:true),
                new("dtr-populated","dtr",900,700),new("dtr-empty","dtr",900,700,Empty:true),
            };
            foreach(var popup in new[]{"columns","patch","blocking","description","rule","color","language"})
                scenarios.Add(new("popup-"+popup,"main",Width:popup=="rule"?1880:1472,Extras:popup=="rule",Popup:popup));
            if(language=="en") scenarios.AddRange([
                new("minimum","main",1240,640),new("maximum","main",1880,1400),
                new("scale-150","main",2208,1494,1.5f),new("scale-200","main",2944,1992,2),
                new("blue","main",Accent:0x4285F4),new("pink","main",Accent:0xEC5CA0),
                new("custom","main",Accent:0xC98542),new("empty","main",Empty:true),
                new("columns","main",1880,1100,Extras:true),new("detailed","main",Detailed:true),
                new("ignored","main",Ignored:true),new("unavailable","main",Detailed:true,Unavailable:true),
            ]);
            foreach(var scenario in scenarios)
            {
                if(Environment.GetEnvironmentVariable("BOTOLOGY_VISUAL_CASE") is { Length:>0 } selected && !selected.Split(',').Contains(language+"/"+scenario.Name,StringComparer.Ordinal)) continue;
                using var host=new NativeTestContext(scenario.Width,scenario.Height,scenario.Scale,language);
                var backend=new SnapshotUi(scenario.Empty,scenario.Accent,language,scenario.Ignored,scenario.Unavailable);
                backend.Configuration.UiCompact=scenario.Compact;
                backend.Configuration.ShowAiColumn=backend.Configuration.ShowRepoColumn=scenario.Extras && scenario.Popup!="rule";
                backend.Configuration.ShowIgnoreColumn=scenario.Extras;
                backend.Configuration.ShowDetailedNotes=scenario.Detailed;
                backend.BlockingPopup=scenario.Popup=="blocking";
                var main=new MainWindow(backend);
                var bounds=new Dictionary<string,(Vector2 Min,Vector2 Max)>();
                main.MeasureBounds=(name,min,max)=>bounds[name]=(min,max);
                var settings=new ConfigWindow(backend);
                var catalog=new CatalogEditorWindow(backend);
                if(backend.Entries.Length>0) catalog.DiagnosticSelect(backend.Entries[0],scenario.Editable);
                object? hindiDraft=null;
                const string hindiNotes="क्षेत्र\r\nखोजें🙂";
                const string hindiDescription="प्रार्थना\r\nअनुवाद";
                if(language=="hi" && scenario.Name=="catalog-editable")
                {
                    hindiDraft=typeof(CatalogEditorWindow).GetField("draft",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(catalog)!;
                    hindiDraft.GetType().GetField("Notes")!.SetValue(hindiDraft,hindiNotes);
                    hindiDraft.GetType().GetField("Description")!.SetValue(hindiDraft,hindiDescription);
                }
                var dtr=new DtrManagerWindow(backend);
                var text=new UiText(language,host.PushFont);
                var theme=BotologyPresentation.Theme(scenario.Accent);
                theme.Density=scenario.Compact?MaterialDensity.Compact:MaterialDensity.Standard;
                theme.Motion.Enabled=false;
                void Draw()
                {
                    bounds.Clear();
                    using var locale=text.Enter();
                    using var material=MaterialTheme.Push(theme,scenario.Scale,MaterialStyleMode.ColorsOnly);
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(16*scenario.Scale));
                    ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new(scenario.Width,scenario.Height));
                    ImGui.Begin(scenario.Window switch { "settings"=>"Botology Settings##Config","catalog"=>"Botology Catalog Editor##CatalogEditor",
                        "dtr"=>"Botology DTR Manager##DtrManager",_=>"Botology##Main" },ImGuiWindowFlags.NoSavedSettings);
                    switch(scenario.Window)
                    {
                        case "settings": settings.Draw(); break;
                        case "catalog": catalog.Draw(); break;
                        case "dtr": dtr.Draw(); break;
                        default: main.Draw(); break;
                    }
                    using(UiText.Font(UiFontRole.Body))
                    {
                    var minimumSource=new[]{"master","local override","local only","hidden master"}.Max(l=>MaterialText.Measure(UiText.T(l)).X+2*MaterialTheme.Metrics.Gap)+32*scenario.Scale;
                    bounds["source-minimum"]=(new(minimumSource,0),new(minimumSource,0));
                    foreach(var row in backend.CaptureRows())
                        if(bounds.TryGetValue("source-"+row.Entry.Id,out var badge) && badge.Max.X-badge.Min.X<MaterialText.Measure(UiText.T(row.Entry.SourceLabel)).X+2*MaterialTheme.Metrics.Gap-.5f)
                            throw new InvalidOperationException("Source badge clipped: "+language+"/"+scenario.Name+" width="+(badge.Max.X-badge.Min.X)+" required="+(MaterialText.Measure(UiText.T(row.Entry.SourceLabel)).X+2*MaterialTheme.Metrics.Gap));
                    }
                    using(UiText.Font(UiFontRole.BodyStrong))
                    foreach(var column in new[]{"Source","Installed","Update","Enabled"})
                        if(bounds.TryGetValue("column-"+column,out var cell) && cell.Max.X-cell.Min.X<MaterialText.Measure(UiText.T(column)).X+48*scenario.Scale-.5f)
                            throw new InvalidOperationException("Translated table header clipped: "+language+"/"+column);
                    if(scenario.Window=="main" && ImGui.GetCursorPosY()>scenario.Height-8*scenario.Scale)
                        throw new InvalidOperationException("Footer overflow: "+language+"/"+scenario.Name);
                    ImGui.End(); ImGui.PopStyleVar();
                }
                ImGui.GetIO().AddMousePosEvent(-1000,-1000);
                // Native child scrollbars can change available width over several frames.
                // Wait for three matching geometry samples before choosing a click position.
                string? priorGeometry=null;
                var stableFrames=0;
                for(var frame=0;frame<12 && stableFrames<3;frame++)
                {
                    host.Frame(Draw);
                    var geometry=string.Join(";",bounds.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>(pair.Key,pair.Value.Min,pair.Value.Max)));
                    stableFrames=geometry==priorGeometry?stableFrames+1:1;
                    priorGeometry=geometry;
                }
                if(stableFrames<3) throw new InvalidOperationException("Window geometry did not settle: "+language+"/"+scenario.Name);
                if(scenario.Popup is "columns" or "patch" or "description" or "rule" or "color" or "language")
                {
                    Vector2 Center(string key)=>(bounds[key].Min+bounds[key].Max)*.5f;
                    host.Click(scenario.Popup switch
                    {
                        "columns"=>Center("columns-button"),"patch"=>Center("action-3"),"description"=>Center("description-sample-0"),
                        "rule"=>Center("rule-sample-0"),"color"=>backend.AccentCenter,_=>backend.LanguageCenter,
                    },Draw);
                    ImGui.GetIO().AddMousePosEvent(-1000,-1000);
                    for(var frame=0;frame<3;frame++) host.Frame(Draw);
                }
                if(scenario.Popup is not null)
                {
                    try { CheckPopup(scenario.Popup,scenario.Width,scenario.Height,scenario.Scale); }
                    catch(Exception exception)
                    {
                        var output=Environment.GetEnvironmentVariable("AETHERTEKUI_CANDIDATE_DIR") ?? Path.Combine(AppContext.BaseDirectory,"artifacts");
                        SoftwarePreview.Save(Path.Combine(output,language+"-"+scenario.Name+"-failed.png"),host);
                        throw new InvalidOperationException(language+"/"+scenario.Name+": "+exception.Message,exception);
                    }
                }
                if(ImGui.GetDrawData().TotalVtxCount<100) throw new InvalidOperationException("No rendered window.");
                if(hindiDraft is not null && ((string)hindiDraft.GetType().GetField("Notes")!.GetValue(hindiDraft)! != hindiNotes
                    || (string)hindiDraft.GetType().GetField("Description")!.GetValue(hindiDraft)! != hindiDescription))
                    throw new InvalidOperationException("Actual catalog multiline editors changed retained Hindi/CRLF/Unicode data.");
                var name=language+"-"+scenario.Name;
                if(language=="en" && scenario.Name=="main") CheckDesign(host,bounds);
                SoftwarePreview.Verify(name,host,Path.Combine(AppContext.BaseDirectory,"Baselines"),Path.Combine(AppContext.BaseDirectory,"artifacts"));
                rendered++;
                if(Environment.GetEnvironmentVariable("AETHERTEKUI_PREVIEW_DIR") is { Length:>0 } previews)
                    SoftwarePreview.Save(Path.Combine(previews,name+".png"),host);
                if(language=="en" && scenario.Name=="main") CheckNativeGrid(host,Draw,bounds,main,backend);
                if(backend.Actions.Count>0) throw new InvalidOperationException("A diagnostic render invoked an action: "+string.Join(",",backend.Actions));
            }
        }
        if(CultureInfo.CurrentCulture!=originalCulture) throw new InvalidOperationException("UI changed process culture.");
        Console.WriteLine($"Botology: {rendered} deterministic font-checked window/state renders.");
    }

    private static unsafe void CheckPopup(string name,int width,int height,float scale)
    {
        var stack=ImGui.GetCurrentContext().OpenPopupStack;
        if(stack.Size!=1 || stack[0].Window==null) throw new InvalidOperationException("Expected rendered popup: "+name);
        var popup=new ImGuiWindowPtr(stack[0].Window);
        var minimumWidth=name is "columns" or "blocking" or "description" or "rule"?320:180;
        if(!popup.Active || popup.Size.X<minimumWidth*scale || popup.Size.Y<40*scale ||
            popup.Pos.X<0 || popup.Pos.Y<0 || popup.Pos.X+popup.Size.X>width || popup.Pos.Y+popup.Size.Y>height)
            throw new InvalidOperationException("Collapsed, missing or overflowing popup: "+name);
    }

    private static void CheckNativeGrid(NativeTestContext host,Action draw,Dictionary<string,(Vector2 Min,Vector2 Max)> bounds,MainWindow main,SnapshotUi backend)
    {
        host.Click(bounds["search"].Min+new Vector2(80,19),draw);
        foreach(var c in "Sample B") ImGui.GetIO().AddInputCharacter(c);
        host.Frame(draw);
        if(!bounds.Keys.Where(k=>k.StartsWith("row-",StringComparison.Ordinal)).SequenceEqual(["row-sample-1"])) throw new InvalidOperationException("Native search no longer filters captured rows.");
        host.Click(new(bounds["search"].Max.X-19,(bounds["search"].Min.Y+bounds["search"].Max.Y)*.5f),draw);
        if(bounds.Keys.Count(k=>k.StartsWith("row-",StringComparison.Ordinal))!=3) throw new InvalidOperationException("Native clear did not restore filtered rows.");
        var headerY=(bounds["table-header"].Min.Y+bounds["table-header"].Max.Y)*.5f;
        var updateX=(bounds["column-Update"].Min.X+bounds["column-Update"].Max.X)*.5f;
        host.Click(new(updateX,headerY),draw);
        if(typeof(MainWindow).GetField("sortColumn",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!.ToString()!="Update") throw new InvalidOperationException("Native header sorting lost its column identity.");
        host.Click(new(updateX,headerY),draw);
        if(bounds.Keys.First(k=>k.StartsWith("row-",StringComparison.Ordinal))!="row-sample-1") throw new InvalidOperationException("Native descending update sort did not use captured availability.");
        var column=bounds["column-Category"];
        var width=column.Max.X-column.Min.X;
        ImGui.GetIO().AddMousePosEvent(column.Max.X,headerY);host.Frame(draw);
        ImGui.GetIO().AddMouseButtonEvent(0,true);host.Frame(draw);
        ImGui.GetIO().AddMousePosEvent(column.Max.X+40,headerY);host.Frame(draw);
        ImGui.GetIO().AddMouseButtonEvent(0,false);host.Frame(draw);
        var resized=bounds["column-Category"].Max.X-bounds["column-Category"].Min.X;
        if(resized<width+35) throw new InvalidOperationException("Native column resizing did not retain user widths.");
        backend.Configuration.ShowRepoColumn=true;host.Frame(draw);host.Frame(draw);
        backend.Configuration.ShowRepoColumn=false;host.Frame(draw);host.Frame(draw);
        if(Math.Abs(bounds["column-Category"].Max.X-bounds["column-Category"].Min.X-resized)>2) throw new InvalidOperationException("Optional columns reset a saved column width.");
        Console.WriteLine("Native filtering/clear, stable header sorting, resized widths and optional-column preservation passed.");
    }

    private static void CheckPreferencesAndResources()
    {
        var configuration=Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>("{\"Version\":1,\"PluginEnabled\":false,\"ShowAuthorColumn\":true,\"IgnoredPluginIds\":[\"fixture\"]}")!;
        if(configuration.UiLanguage!="en" || configuration.UiAccentRgb!=0x1CC9E6 || configuration.Version!=1 || configuration.PluginEnabled || !configuration.ShowAuthorColumn || configuration.IgnoredPluginIds.Single()!="fixture")
            throw new InvalidOperationException("Existing configuration defaults were reset.");
        var property=typeof(Plugin).GetProperty("PluginInterface",BindingFlags.Static|BindingFlags.NonPublic)!;
        var previous=property.GetValue(null);
        var save=DispatchProxy.Create<IDalamudPluginInterface,ConfigurationSaveProxy>();
        try
        {
            property.SetValue(null,save);
            foreach(var language in UiText.Languages)
            {
                configuration.UiLanguage=language.Code;configuration.UiAccentRgb=0xC98542;configuration.Save();
                var saved=Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(((ConfigurationSaveProxy)save).Json!)!;
                if(saved.UiLanguage!=language.Code || saved.UiAccentRgb!=0xC98542 || saved.Version!=1 || saved.PluginEnabled || !saved.ShowAuthorColumn || saved.IgnoredPluginIds.Single()!="fixture")
                    throw new InvalidOperationException("UI preferences did not use the existing save path: "+language.Code);
            }
        }
        finally { property.SetValue(null,previous); }
        using var english=new UiText("en",_=>throw new InvalidOperationException());
        var keys=english.Resources.Cast<DictionaryEntry>().ToDictionary(e=>(string)e.Key,e=>(string)e.Value!);
        string Fields(string value)=>string.Join(",",System.Text.RegularExpressions.Regex.Matches(value,@"(?<!\{)\{(\d+)(?:[^{}]*)\}").Select(m=>m.Groups[1].Value).Distinct().Order());
        foreach(var language in UiText.Languages)
        {
            using var text=new UiText(language.Code,_=>throw new InvalidOperationException());
            var translated=text.Resources.Cast<DictionaryEntry>().ToDictionary(e=>(string)e.Key,e=>(string)e.Value!);
            if(!keys.Keys.Order().SequenceEqual(translated.Keys.Order())) throw new InvalidOperationException("Resource key mismatch: "+language.Code);
            foreach(var key in keys.Keys)
            {
                if(string.IsNullOrWhiteSpace(translated[key]) || Fields(keys[key])!=Fields(translated[key])) throw new InvalidOperationException("Resource placeholders: "+language.Code+"/"+key);
                System.Text.CompositeFormat.Parse(translated[key]);
            }
        }
        Console.WriteLine($"Existing configuration defaults/save path and all {UiText.Languages.Length} keyed resource/placeholder sets passed.");
    }

    private static unsafe void CheckNativeLocalizedControls()
    {
        using var host=new NativeTestContext(900,600,1,"de");
        using var text=new UiText("de",host.PushFont);
        var theme=BotologyPresentation.Theme(0x1CC9E6);
        var value=false;var disabled=false;var clicked=false;Vector2 checkbox=default,button=default;
        void Draw()
        {
            using var locale=text.Enter();using var font=UiText.Font(UiFontRole.Body);
            using var material=MaterialTheme.Push(theme);
            ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new(900,600));
            ImGui.Begin("Localized controls",ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoTitleBar);
            var expected=ImGui.GetID("Show DTR bar entry");
            UiGui.Checkbox("Show DTR bar entry",ref value);
            if(ImGui.GetCurrentContext().LastItemData.ID!=expected) throw new InvalidOperationException("Translated checkbox changed its ID.");
            var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
            checkbox=new(max.X-3,(min.Y+max.Y)*.5f);
            if(max.X-min.X<ImGui.GetFrameHeight()+MaterialText.Measure(UiText.T("Show DTR bar entry")).X) throw new InvalidOperationException("Translated checkbox hit area is too short.");
            ImGui.BeginDisabled(disabled);
            expected=ImGui.GetID("Reload master now");
            clicked|=UiGui.Button("Reload master now");
            if(ImGui.GetCurrentContext().LastItemData.ID!=expected) throw new InvalidOperationException("Translated button changed its ID.");
            button=(ImGui.GetItemRectMin()+ImGui.GetItemRectMax())*.5f;
            ImGui.EndDisabled();
            expected=ImGui.GetID("All sources");UiGui.Selectable("All sources",false);
            if(ImGui.GetCurrentContext().LastItemData.ID!=expected) throw new InvalidOperationException("Translated source changed its ID.");
            ImGui.End();
        }
        host.Frame(Draw);host.Frame(Draw);host.Click(checkbox,Draw);
        if(!value) throw new InvalidOperationException("Translated checkbox did not activate across its label.");
        disabled=true;host.Click(button,Draw);
        if(clicked) throw new InvalidOperationException("Disabled translated button activated.");
        disabled=false;host.Click(button,Draw);
        if(!clicked) throw new InvalidOperationException("Translated native button did not activate.");
        Console.WriteLine("Localized native IDs, translated hit areas, and disabled interactions passed.");
    }

    private static void CheckReadableFields()
    {
        var checkedFields=0;
        foreach(var language in UiText.Languages.Select(l=>l.Code))
        {
            using var host=new NativeTestContext(1400,950,1,language);
            using var text=new UiText(language,host.PushFont);
            foreach(var compact in new[]{false,true})
            foreach(var scale in new[]{1f,1.25f,1.5f})
            {
                ImGui.GetIO().FontGlobalScale=scale;
                var theme=BotologyPresentation.Theme(0x1CC9E6);
                theme.Density=compact?MaterialDensity.Compact:MaterialDensity.Standard;
                void Draw()
                {
                    using var locale=text.Enter();
                    using var material=MaterialTheme.Push(theme,scale);
                    using var font=UiText.Font(UiFontRole.Body);
                    using var controls=MaterialControls.Push(BotologyPresentation.Controls(34));
                    ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new(600*scale,600*scale));
                    ImGui.Begin("Readable fields",ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.HorizontalScrollbar);
                    var original=42;
                    ImGui.InputInt("Master catalog check interval (minutes)",ref original,1,100);
                    var groupId=ImGui.GetCurrentContext().LastItemData.ID;
                    var number=42;ImGui.SetNextItemWidth(1);
                    UiGui.InputInt("Master catalog check interval (minutes)",ref number,1,100);
                    var editor=ImGui.GetItemRectSize().X-2*(ImGui.GetFrameHeight()+ImGui.GetStyle().ItemInnerSpacing.X);
                    var minimum=Math.Max(80*scale,ImGui.CalcTextSize("00000000").X+2*ImGui.GetStyle().FramePadding.X);
                    if(number!=42 || editor<minimum-.1f || ImGui.GetCurrentContext().LastItemData.ID!=groupId)
                        throw new InvalidOperationException("Numeric editor/steps/identity: "+language);
                    if(ImGuiP.GetCurrentWindow().DC.CursorPosPrevLine.X>ImGui.GetItemRectMax().X+.1f)
                        throw new InvalidOperationException("Hidden field label still expands content.");
                    var raw="raw 0007";var id=ImGui.GetID("DTR enabled glyph");ImGui.SetNextItemWidth(1);
                    UiGui.InputText("DTR enabled glyph",ref raw,64);
                    if(raw!="raw 0007" || ImGui.GetItemRectSize().X<minimum-.1f || ImGui.GetCurrentContext().LastItemData.ID!=id)
                        throw new InvalidOperationException("Text field width/raw identity: "+language);
                    var choice=1;id=ImGui.GetID("DTR mode");ImGui.SetNextItemWidth(1);
                    UiGui.Combo("DTR mode",ref choice,["Text only","Icon + text","Icon only"],3);
                    if(choice!=1 || ImGui.GetCurrentContext().LastItemData.ID!=id || ImGui.GetItemRectSize().X<MaterialText.Measure(UiText.T("Icon + text")).X+ImGui.GetFrameHeight()+2*ImGui.GetStyle().FramePadding.X-.1f)
                        throw new InvalidOperationException("Translated combo preview/identity: "+language);
                    checkedFields+=3;
                    ImGui.End();
                }
                host.Frame(Draw);host.Frame(Draw);
            }
            ImGui.GetIO().FontGlobalScale=1;
            var numberValue=42;var plus=Vector2.Zero;
            for(var frame=0;frame<5;frame++)
            {
                ImGui.GetIO().AddMousePosEvent(plus.X,plus.Y);ImGui.GetIO().AddMouseButtonEvent(0,frame is 2 or 3);
                host.Frame(()=>
                {
                    using var locale=text.Enter();using var material=MaterialTheme.Push(BotologyPresentation.Theme(0x1CC9E6));
                    using var font=UiText.Font(UiFontRole.Body);
                    ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new(600,400));
                    ImGui.Begin("Step identity",ImGuiWindowFlags.NoSavedSettings);
                    ImGui.SetNextItemWidth(70);
                    if(frame<3) ImGui.InputInt("##MasterInterval",ref numberValue,1,100);
                    else UiGui.InputInt("##MasterInterval",ref numberValue,1,100);
                    plus=new(ImGui.GetItemRectMax().X-ImGui.GetFrameHeight()*.5f,(ImGui.GetItemRectMin().Y+ImGui.GetItemRectMax().Y)*.5f);
                    ImGui.End();
                });
            }
            if(numberValue!=43) throw new InvalidOperationException("Original numeric step press lost on wrapper release: "+language);
        }
        Console.WriteLine($"Readable fields: {checkedFields} font-checked checks across {UiText.Languages.Length} locales, both densities, three scales; original native step press/release retained.");
    }

    private static void CheckHindiNativeControls()
    {
        using var host=new NativeTestContext(1100,800,1,"hi");
        using var text=new UiText("hi",host.PushFont);
        var value="क्षेत्र🙂\uE101";
        var toggle=false;
        var presses=0;
        var bounds=new Dictionary<string,Vector2>();
        void Draw()
        {
            using var locale=text.Enter();
            using var theme=MaterialTheme.Push(BotologyPresentation.Theme(0x1CC9E6));
            using var font=UiText.Font(UiFontRole.Body);
            ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new(1100,800));
            ImGui.Begin("Hindi retained controls",ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoTitleBar);
            void Identity(string label)
            {
                if(ImGui.GetCurrentContext().LastItemData.ID!=ImGui.GetID(label))
                    throw new InvalidOperationException("Hindi native identity changed: "+label);
                bounds[label]=(ImGui.GetItemRectMin()+ImGui.GetItemRectMax())*.5f;
            }
            if(UiGui.Button("Save local changes")) presses++;
            Identity("Save local changes");
            if(ImGui.GetItemRectSize().Y<MaterialText.Measure(UiText.T("Save local changes")).Y)
                throw new InvalidOperationException("Hindi action height is too small.");
            UiGui.Checkbox("Compact mode",ref toggle);Identity("Compact mode");
            if(UiGui.Selectable("Text only",false)) presses++;
            Identity("Text only");
            ImGui.SetNextItemWidth(400);
            UiGui.InputText("Category",ref value,128);Identity("Category");
            if(ImGui.GetItemRectSize().Y<MaterialText.Measure(value).Y)
                throw new InvalidOperationException("Hindi editor height is too small.");
            var disabled=false;
            ImGui.BeginDisabled();
            try { if(UiGui.Checkbox("Transparency",ref disabled) || disabled) throw new InvalidOperationException("Disabled Hindi control acted."); }
            finally { ImGui.EndDisabled(); }
            ImGui.End();
        }
        host.Frame(Draw);host.Frame(Draw);
        host.Click(bounds["Save local changes"],Draw);
        host.Click(bounds["Compact mode"],Draw);
        host.Click(bounds["Text only"],Draw);
        if(presses!=2 || !toggle || value!="क्षेत्र🙂\uE101")
            throw new InvalidOperationException("Hindi retained actions or raw input preservation failed.");
        host.Click(bounds["Category"],Draw);
        ImGui.GetIO().AddInputCharacter('x');host.Frame(Draw);
        if(!value.Contains('x') || !value.Contains("🙂\uE101",StringComparison.Ordinal))
            throw new InvalidOperationException("Hindi input edit lost Unicode data.");
        SoftwarePreview.Capture(host);
        Console.WriteLine("Hindi retained IDs, action/checkbox/selectable presses, shaped heights, Unicode edits and disabled behavior passed.");
    }

    private static void CheckDesign(NativeTestContext host,Dictionary<string,(Vector2 Min,Vector2 Max)> bounds)
    {
        void Near(string region,float actual,float expected)
        { if(Math.Abs(actual-expected)>2) throw new InvalidOperationException($"Design geometry {region}: {actual}, expected {expected} ±2 logical pixels."); }
        foreach(var (name,height) in new[]{("header",72f),("action-0",52f),("status",64f),("filters",115f),("footer",52f)})
            Near(name,bounds[name].Max.Y-bounds[name].Min.Y,height);
        foreach(var (name,width) in new[]{("category",213f),("search",222f),("author",230f)})
            Near(name,bounds[name].Max.X-bounds[name].Min.X,width);
        Near("header/action gap",bounds["action-0"].Min.Y-bounds["header"].Max.Y,16);
        Near("action/status gap",bounds["status"].Min.Y-bounds["action-0"].Max.Y,20);
        Near("status/filters gap",bounds["filters"].Min.Y-bounds["status"].Max.Y,20);
        Near("filters/table gap",bounds["table-header"].Min.Y-bounds["filters"].Max.Y,16);
        Near("table header",bounds["table-header"].Max.Y-bounds["table-header"].Min.Y,44);
        // The approved feedback widens Source beyond the original 114px to fit actual provenance labels.
        var sourceWidth=Math.Max(114,bounds["source-minimum"].Min.X);
        foreach(var (name,width) in new[]{("Category",286f),("Source",sourceWidth),("Installed",128f),("Update",122f),("Enabled",120f),("Dtr",124f),("Author",200f),("Notes",346f)})
            Near("column "+name,bounds["column-"+name].Max.X-bounds["column-"+name].Min.X,width);
        foreach(var name in bounds.Keys.Where(n=>n.StartsWith("row-",StringComparison.Ordinal)))
            Near(name,bounds[name].Max.Y-bounds[name].Min.Y,74);
        foreach(var (name,em) in new[]{("title",38f),("plugin-name",20f),("counter",22f)})
            Near(name+" em",(bounds[name].Max.Y-bounds[name].Min.Y)*.75f,em);
        Near("logo width",bounds["logo"].Max.X-bounds["logo"].Min.X,52);
        var frame=SoftwarePreview.Capture(host);
        void Color(string name,Vector2 position,uint expected)
        {
            var index=((int)position.Y*frame.Width+(int)position.X)*4;
            foreach(var (channel,shift) in new[]{(0,16),(1,8),(2,0)})
                if(Math.Abs(frame.Rgba[index+channel]-(int)((expected>>shift)&255))>6) throw new InvalidOperationException("Design color "+name);
        }
        Color("action",bounds["action-1"].Min+new Vector2(8,8),0x1B2C3C);
        Color("DTR action",bounds["action-0"].Min+new Vector2(20,20),0x154456);
        Color("field",bounds["search"].Min+new Vector2(8,8),0x11202D);
        Color("background",bounds["header"].Min+new Vector2(300,60),0x111E2A);
        Console.WriteLine("Reference bounds/colors checked: ±2 logical pixels / ±6 RGB; native chrome, selectors, snapshot data, status semantics, glyph AA, and raster grain are explicit differences.");
    }
}

public class ConfigurationSaveProxy : DispatchProxy
{
    internal string? Json;
    protected override object? Invoke(MethodInfo? method,object?[]? arguments)
    {
        if(method?.Name!="SavePluginConfig") throw new InvalidOperationException("Unexpected offline service access: "+method?.Name);
        Json=Newtonsoft.Json.JsonConvert.SerializeObject(arguments![0]);return null;
    }
}

internal sealed class SnapshotUi : IBotologyUi
{
    public Configuration Configuration { get; } = new() { ShowRepoColumn=false,ShowAiColumn=false,ShowIgnoreColumn=false,
        ShowAuthorColumn=true,ShowDetailedNotes=false,DtrIconEnabled="+",DtrIconDisabled="-" };
    public PluginAssessmentRow[] Rows { get; }
    public PluginCatalogEntry[] Entries { get; }
    public DtrEntrySnapshot[] DtrEntries { get; }
    public List<string> Actions { get; }=[];
    internal bool BlockingPopup;
    internal Vector2 AccentCenter;
    internal Vector2 LanguageCenter;
    private Vector3 accent;
    private string language;
    private readonly MaterialOptions<string> options=new(UiText.Languages.Select(l=>new MaterialOption<string>(l.Code,l.Code,l.Name)).ToArray());
    public SnapshotUi(bool empty,uint rgb,string language,bool ignored=false,bool unavailable=false)
    {
        Configuration.UiAccentRgb=rgb; Configuration.UiLanguage=language;
        var c=BotologyPresentation.Rgb(rgb); accent=new(c.X,c.Y,c.Z); this.language=language;
        Entries=empty?[]:Enumerable.Range(0,3).Select(i=>new PluginCatalogEntry("sample-"+i,i==0?"Navigation":i==1?"Utility":"Social",
            "Sample "+(char)('A'+i),[],"External catalog note.") { SourceKind=(CatalogEntrySourceKind)(i%3) }).ToArray();
        Rows=Entries.Select((e,i)=>new PluginAssessmentRow(e,
            new PluginRuntimeState(e.Id,e.DisplayName,new Version(1,i+1,3),true,i==1,null,null,new object(),null,null,null),
            new AssessmentResult((AssessmentSeverity)i,"No warning rules triggered.","External catalog detail.") { UiSummaryKey="No warning rules triggered." },false,
            new PluginRepositoryMetadata { Author="SampleAuthor" })).ToArray();
        if(ignored && Rows.Length>0) Rows[0]=Rows[0] with { Ignored=true };
        if(unavailable && Rows.Length>0) Rows[0]=Plugin.ApplyPatchCompatibilityAssessment(Rows[0] with { Metadata=Rows[0].Metadata! with { DalamudApiLevel=14 } });
        DtrEntries=empty?[]:[new("Sample A","Sample","",true,false,false,0,"sample-0"),new("Sample B","Sample","",true,true,false,1,"sample-1")];
    }
    public void DrawWindowAppearanceSettings() => DrawAppearanceSelector();
    public void DrawAppearanceSelector()
    {
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(28,18));
        AccentCenter=ImGui.GetCursorScreenPos()+new Vector2(14*MaterialTheme.Metrics.Scale);
        var changes=MaterialAppearanceSelector.Draw("appearance",ref accent,ref language,options,
            new(UiText.T("Color"),UiText.T("Language"),UiText.T("Teal"),UiText.T("Blue"),UiText.T("Pink"),UiText.T("Custom RGB")));
        LanguageCenter=(ImGui.GetItemRectMin()+ImGui.GetItemRectMax())*.5f;
        if(changes.AccentChanged || changes.LanguageChanged) Actions.Add("appearance");
    }
    public IReadOnlyList<PluginAssessmentRow> CaptureRows()=>Rows;
    public IReadOnlyList<DtrEntrySnapshot> CaptureDtrEntries()=>DtrEntries;
    public IReadOnlyList<PluginCatalogEntry> CaptureCatalogEditorEntries()=>Entries;
    public IReadOnlyList<CatalogRelease> CaptureCatalogReleases()=>[new() { Id="fixture",PublishedUtc=DateTimeOffset.Parse("2026-04-27T14:32:00Z"),
        Title="Catalog update",Sections=[new() { Heading="Changes",Items=["External catalog release item."] }] }];
    public CatalogRefreshInfo GetCatalogRefreshInfo()=>new(DateTimeOffset.Parse("2026-04-27T14:32:00Z"),DateTimeOffset.Parse("2026-04-27T14:32:00Z"),"","",false);
    public PluginCatalogEntry? GetMasterCatalogEntry(string id)=>Entries.FirstOrDefault(e=>e.Id==id);
    public bool TryGetGlobalDtrEntry(PluginAssessmentRow row,IReadOnlyList<DtrEntrySnapshot> entries,out DtrEntrySnapshot? entry)
    { entry=entries.FirstOrDefault(e=>e.OwnerInternalName==row.Entry.Id);return entry is not null; }
    public bool TryGetBlockingAlert(out string message,out bool shouldOpenPopup)
    { message="2 non-green plugin entries: Sample B (Yellow), Sample C (Red)";shouldOpenPopup=BlockingPopup;BlockingPopup=false;return true; }
    public void AcknowledgeBlockingAlert()=>Actions.Add("acknowledge");
    public void OpenUrl(string url)=>Actions.Add("url");
    public void OpenConfigUi()=>Actions.Add("settings");
    public void OpenDtrManagerUi()=>Actions.Add("dtr");
    public void OpenCatalogEditorUi()=>Actions.Add("catalog");
    public void OpenCatalogFolder()=>Actions.Add("folder");
    public bool OpenCatalogScriptFolder() { Actions.Add("script");return true; }
    public bool PrepareCatalogUploadPackage() { Actions.Add("prepare");return true; }
    public void RunTextCommand(string command)=>Actions.Add(command);
    public void PrintStatus(string message)=>Actions.Add("status");
    public void RefreshMasterCatalog(bool force=false,bool silent=false)=>Actions.Add("refresh");
    public void RescheduleMasterCatalogCheck()=>Actions.Add("reschedule");
    public void SetPluginEnabled(bool enabled,bool printStatus=false)=>Actions.Add("enabled");
    public void SetHideUninstalledPlugins(bool hide)=>Actions.Add("hide");
    public void SetIgnored(string id,bool ignored)=>Actions.Add("ignore");
    public void ToggleTrackedPlugin(PluginRuntimeState runtimeState)=>Actions.Add("toggle");
    public void ToggleTrackedPluginDtr(PluginRuntimeState runtimeState,bool enabled)=>Actions.Add("toggle-dtr");
    public void SetGlobalDtrEntryVisible(string title,bool visible)=>Actions.Add("visibility");
    public void MoveGlobalDtrEntry(string title,int delta)=>Actions.Add("move");
    public void OpenServerInfoBarSettings(string? searchText=null)=>Actions.Add("server-info");
    public void UpdateDtrBar()=>Actions.Add("update-dtr");
    public void SaveCatalogEntry(PluginCatalogEntry entry)=>Actions.Add("save-catalog");
    public bool ReplaceWithMasterData(string id) { Actions.Add("replace");return true; }
    public bool HideMasterCatalogEntry(string id) { Actions.Add("hide-master");return true; }
    public bool RestoreMasterCatalogEntry(string id) { Actions.Add("restore");return true; }
    public int DropAllLocalCatalogChanges() { Actions.Add("drop-all");return 1; }
}

internal sealed unsafe class NativeTestContext : IDisposable
{
    private readonly ImGuiContextPtr context;
    private readonly System.Runtime.InteropServices.GCHandle glyphPin;
    private readonly ImFontPtr[] fonts=new ImFontPtr[Enum.GetValues<UiFontRole>().Length];
    private SoftwarePreview.Texture[] textures=[];
    private readonly MaterialTextRenderer shapedText;
    private readonly Dictionary<ulong,RegisteredTextTexture> textTextures=[];
    private nint nextTextTextureId=0x100000;
    public byte* Pixels { get; private set; }
    public int AtlasWidth { get; private set; }
    public int AtlasHeight { get; private set; }
    public NativeTestContext(int width,int height,float scale,string language)
    {
        context=ImGui.CreateContext();
        shapedText=new MaterialTextRenderer(bitmap=>
        {
            var texture=new RegisteredTextTexture(this,nextTextTextureId++,bitmap);
            textTextures.Add(texture.Id.Handle,texture);
            return new MaterialTextTexture(texture.Id,texture);
        });
        var io=ImGui.GetIO();io.IniFilename=null;io.LogFilename=null;io.DisplaySize=new(width,height);io.DeltaTime=1f/60;io.FontGlobalScale=scale;
        io.Fonts.AddFontDefault();
        using var text=new UiText(language,_=>throw new InvalidOperationException());
        var ranges=text.GlyphRanges();
        // Native font configs retain this pointer, including subsequent baked-size rebuilds.
        glyphPin=System.Runtime.InteropServices.GCHandle.Alloc(ranges,System.Runtime.InteropServices.GCHandleType.Pinned);
        var glyphs=(ushort*)glyphPin.AddrOfPinnedObject();
        {
            for(var role=0;role<fonts.Length;role++)
            {
                var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),BotologyPresentation.FontFiles[role]);
                if(!File.Exists(path)) throw new FileNotFoundException("Required Segoe font missing.",path);
                fonts[role]=io.Fonts.AddFontFromFileTTF(path,BotologyPresentation.AtlasHeight((UiFontRole)role),default,glyphs);
                foreach(var locale in UiText.CjkLanguages(language))
                {
                    var file=locale switch { "ja"=>"msgothic.ttc","ko"=>"malgun.ttf",_=>"msyh.ttc" };
                    path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),file);
                    if(!File.Exists(path)) throw new FileNotFoundException("Required CJK font missing.",path);
                    var config=ImGui.ImFontConfig(); config.MergeMode=true; config.DstFont=fonts[role];
                    io.Fonts.AddFontFromFileTTF(path,BotologyPresentation.AtlasHeight((UiFontRole)role),config,glyphs);
                    ImGui.Destroy(config);
                }
            }
        }
        RefreshAtlas();io.Fonts.SetTexID(0,new ImTextureID((nint)1));
        foreach(var font in fonts)
            foreach(var value in UiText.Values(text.Resources).Concat(UiText.Languages.Select(l=>l.Name)).Select(MaterialText.NativeGlyphText))
                foreach(var character in value.Where(c=>!char.IsControl(c)))
                    if(ImGui.FindGlyphNoFallback(font,character).Handle==null) throw new InvalidOperationException("Missing glyph "+((int)character).ToString("X4")+" in "+language);
        foreach(var role in Enum.GetValues<UiFontRole>())
            shapedText.CheckGlyphs(text.RequiredText,BotologyPresentation.AtlasHeight(role)*scale);
    }
    internal IDisposable PushFont(UiFontRole role) { ImGui.PushFont(fonts[(int)role]);return new FontScope(); }
    private sealed class FontScope : IDisposable { public void Dispose()=>ImGui.PopFont(); }
    public void RefreshAtlas()
    {
        byte* pixels=null;int width=0,height=0;
        ImGui.GetIO().Fonts.GetTexDataAsRGBA32(0,&pixels,&width,&height);
        Pixels=pixels;AtlasWidth=width;AtlasHeight=height;
        var atlas=ImGui.GetIO().Fonts;
        textures=new SoftwarePreview.Texture[atlas.Textures.Size];
        for(var index=0;index<textures.Length;index++)
        {
            atlas.GetTexDataAsRGBA32(index,&pixels,&width,&height);
            textures[index]=new((nint)pixels,width,height);
            atlas.SetTexID(index,new ImTextureID((nint)(index+1)));
        }
    }
    public SoftwarePreview.Texture Texture(ImTextureID id)
        => id.Handle>=1 && id.Handle<=(ulong)textures.Length ? textures[(int)id.Handle-1]
            : textTextures.TryGetValue(id.Handle,out var text) ? text.Texture : throw new InvalidOperationException("Unknown diagnostic texture.");
    private sealed class RegisteredTextTexture : IDisposable
    {
        private readonly NativeTestContext owner;
        private System.Runtime.InteropServices.GCHandle pixels;
        private bool disposed;
        public ImTextureID Id { get; }
        public SoftwarePreview.Texture Texture { get; }
        public RegisteredTextTexture(NativeTestContext owner,nint id,MaterialTextBitmap bitmap)
        {
            this.owner=owner;Id=new ImTextureID(id);
            pixels=System.Runtime.InteropServices.GCHandle.Alloc(bitmap.Rgba,System.Runtime.InteropServices.GCHandleType.Pinned);
            Texture=new(pixels.AddrOfPinnedObject(),bitmap.Width,bitmap.Height);
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;owner.textTextures.Remove(Id.Handle);pixels.Free();
        }
    }
    public void Frame(Action draw) { ImGui.NewFrame();using(shapedText.Push()) draw();ImGui.Render(); }
    public void Click(Vector2 position,Action draw)
    {
        ImGui.GetIO().AddMousePosEvent(position.X,position.Y);Frame(draw);
        ImGui.GetIO().AddMouseButtonEvent(0,true);Frame(draw);
        ImGui.GetIO().AddMouseButtonEvent(0,false);Frame(draw);
    }
    public void Dispose() { shapedText.Dispose();ImGui.DestroyContext(context);glyphPin.Free(); }
}

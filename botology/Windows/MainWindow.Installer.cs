using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using botology.Models;
using botology.Services;

namespace botology.Windows;

public sealed partial class MainWindow
{
    private InstallerCatalogSnapshot installerSnapshot = InstallerCatalogSnapshot.Empty;
    private DateTime nextInstallerSnapshot;
    private InstallerOperation? observedOperation;
    private InstallerMedia? installerMedia;
    private string installerPage = "Discover", installerSearch = "", installerCategory = "", installerAuthor = "";
    private string installerFilter = "All plugins", repositoryFilter = "", pinnedAuthorFilter = "";
    private string manualRepository = "", pinSearch = "";
    private string? selectedInstaller, pendingRemoval, browseAnchor, highlightedInstaller;
    private int galleryIndex, previousColumns, installerSort;
    private bool pinsExpanded, tableView, restoreBrowse, installerSortDescending;
    private bool resetInstallerContentScroll;
    private float browseRemainder;
    private float lastBrowseScroll;
    private float profileSourceHeight;
    private int preserveBrowseFrames;
    internal bool DiagnosticCompatibilityOnly { get; set; }

    private static Vector4 InstallerShade(Vector4 color,float strength)=>new(color.X*strength,color.Y*strength,color.Z*strength,color.W);

    private static MaterialSurface.SurfaceScope InstallerSurface(string id,Vector2 size,MaterialElevation elevation=MaterialElevation.Low,
        bool outline=false,float? padding=null,float? radius=null,ImGuiWindowFlags flags=ImGuiWindowFlags.None)
    {
        var surface=MaterialSurface.Begin(id,size,elevation,outline,padding,radius,flags);
        if(surface.Visible)
        {
            var c=MaterialTheme.Current.Colors;
            var inset=new Vector2(outline?MaterialTheme.Metrics.Scale:0);
            MaterialCanvas.Surface(ImGui.GetWindowPos()+inset,ImGui.GetWindowPos()+ImGui.GetWindowSize()-inset,
                InstallerShade(c.SurfaceContainerLow,.8f),InstallerShade(c.SurfaceContainerLowest,.8f),Math.Max(0,(radius??8)-1)*MaterialTheme.Metrics.Scale);
        }
        return surface;
    }

    private bool InstallerButton(string label, string id, bool selected = false, Vector2 size = default,
        MaterialIcon leading = MaterialIcon.None, MaterialIcon trailing = MaterialIcon.None, bool primary = false, bool textOnly = false, bool linkColour = true)
    {
        var c=MaterialTheme.Current.Colors;
        var fill=textOnly?Vector4.Zero:primary?c.Primary:selected?c.PrimaryContainer:c.SurfaceContainerHigh;
        var foreground=primary?(MaterialColor.Contrast(c.Primary,c.Background)>MaterialColor.Contrast(c.Primary,c.OnSurface)?c.Background:c.OnSurface)
            :selected||textOnly&&linkColour?c.Primary:c.OnSurface;
        var clicked=MaterialButton.Draw(id,UiText.T(label),new MaterialControlAppearance(fill,foreground,textOnly?Vector4.Zero:selected||primary?c.Primary:c.Outline,6),
            size,leading:leading,trailing:trailing);
        MeasureBounds?.Invoke(id,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        return clicked;
    }

    private static void InstallerNextInline(float width)
    {
        if(ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+width<=ImGui.GetWindowPos().X+ImGui.GetWindowContentRegionMax().X)
            ImGui.SameLine();
    }

    private void DrawInstallerNavigation()
    {
        (string Label,MaterialIcon Icon)[] pages=[("Discover",MaterialIcon.Search),("Installed",MaterialIcon.Backpack),
            ("Updates",MaterialIcon.Refresh),("Repositories",MaterialIcon.Database),("Compatibility",MaterialIcon.Shield)];
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(BotologyPresentation.Compact?42:50,22,preserveHeight:true));
        for(var i=0;i<pages.Length;i++)
        {
            var (page,icon)=pages[i];
            if(i>0) InstallerNextInline(MaterialText.Measure(UiText.T(page)).X+54*MaterialTheme.Metrics.Scale);
            var selected=installerPage==page || page=="Repositories" && installerPage=="Pinned sources";
            if(InstallerButton(page,"nav-"+page,selected,leading:icon,textOnly:true,linkColour:false))
            { installerPage=page; selectedInstaller=null; pendingRemoval=null; resetInstallerContentScroll=true; installerMedia?.Clear(); }
            if(selected)
                ImGui.GetWindowDrawList().AddLine(ImGui.GetItemRectMin()+new Vector2(0,ImGui.GetItemRectSize().Y),ImGui.GetItemRectMax(),
                    MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary),3*MaterialTheme.Metrics.Scale);
        }
        ImGui.Separator();
        ImGui.Dummy(new(0,6*MaterialTheme.Metrics.Scale));
    }
    private void DrawInstaller()
    {
        if(DiagnosticCompatibilityOnly) { DrawContent(plugin.CaptureRows(),plugin.CaptureDtrEntries(),plugin.GetCatalogRefreshInfo()); return; }
        if(DateTime.UtcNow>=nextInstallerSnapshot || !ReferenceEquals(observedOperation,plugin.InstallerOperation))
        {
            installerSnapshot=plugin.CaptureInstaller(); nextInstallerSnapshot=DateTime.UtcNow.AddSeconds(1);
            observedOperation=plugin.InstallerOperation;
        }
        installerMedia?.BeginFrame();
        if(installerPage=="Compatibility")
        {
            // Keep retained table/popup IDs and geometry saves in the original root window.
            DrawInstallerNavigation();
            DrawContent(plugin.CaptureRows(),plugin.CaptureDtrEntries(),plugin.GetCatalogRefreshInfo());
            installerMedia?.EndFrame();
            return;
        }
        var s=MaterialTheme.Metrics.Scale;
        using var layout=new MaterialStyleScope();
        layout.Style(ImGuiStyleVar.ItemSpacing,new Vector2(12,BotologyPresentation.Compact?7:10)*s);
        layout.Style(ImGuiStyleVar.FramePadding,new Vector2(10,6)*s);
        layout.Style(ImGuiStyleVar.CellPadding,new Vector2(12,BotologyPresentation.Compact?7:10)*s);
        layout.Style(ImGuiStyleVar.FrameRounding,6*s);
        layout.Style(ImGuiStyleVar.ChildRounding,8*s);
        layout.Color(ImGuiCol.ChildBg,Vector4.Zero);
        var background=ImGui.GetCursorScreenPos();
        MaterialCanvas.Surface(background,background+ImGui.GetContentRegionAvail(),InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerLowest,.8f),
            InstallerShade(MaterialTheme.Current.Colors.Background,.65f),0);
        var width=ImGui.GetContentRegionAvail().X;
        var rail=(pinsExpanded?280:40)*s;
        using(var panel=InstallerSurface("InstallerPins",new(rail/s,ImGui.GetContentRegionAvail().Y/s),MaterialElevation.Lowest,padding:pinsExpanded?12:2,radius:0))
        DrawPinnedRail();
        ImGui.SameLine();
        ImGui.BeginChild("InstallerContent",new(Math.Max(1,width-rail-ImGui.GetStyle().ItemSpacing.X),-1),false);
        DrawInstallerNavigation();
        if(plugin.InstallerOperation is { } operation)
            MaterialText.TextWrapped(UiText.T(operation.IsRunning ? "Working..." : operation.Succeeded ? "Completed" : "Operation failed")+": "+UiText.T(operation.Message));
        if(installerSnapshot.Error is { Length:>0 } error) MaterialText.TextWrapped(error);
        if(installerSnapshot.SafeMode) MaterialText.TextWrapped(UiText.T("Safe mode"));
        if(resetInstallerContentScroll) { ImGui.SetScrollY(0); resetInstallerContentScroll=false; }
        if(selectedInstaller is not null)
        {
            var entry=installerSnapshot.Entries.FirstOrDefault(e=>e.Identity==selectedInstaller);
            if(entry is not null) DrawInstallerProfile(entry);
            else { selectedInstaller=null; installerMedia?.Clear(); }
        }
        else if(installerPage=="Repositories") DrawInstallerRepositories();
        else if(installerPage=="Pinned sources") DrawPinsManager();
        else DrawInstallerResults();
        if(installerPage!="Compatibility" && selectedInstaller is null && installerPage is not ("Repositories" or "Pinned sources"))
        {
            using var footer=MaterialControls.Push(BotologyPresentation.Controls(24,16,preserveHeight:true));
            MaterialIcons.Draw(MaterialIcon.Info,ImGui.GetCursorScreenPos(),18*s,MaterialTheme.Current.Colors.OnSurfaceVariant);
            ImGui.Dummy(new(20*s,20*s)); ImGui.SameLine();
            MaterialText.TextWrapped(UiText.T("Pinning a source does not add its repository."));
        }
        ImGui.EndChild();
        installerMedia?.EndFrame();
        DrawBlockingAlertPopup();
        FinalizePendingWindowPlacement();
    }

    private void DrawPinnedRail()
    {
        var s=MaterialTheme.Metrics.Scale;
        var c=MaterialTheme.Current.Colors;
        if(pinsExpanded)
        {
            using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(UiText.T("Pinned sources"));
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(),ImGui.GetWindowWidth()-42*s));
        }
        if(MaterialButton.IconButton("pins-toggle",pinsExpanded?MaterialIcon.Return:MaterialIcon.ArrowRight,size:32)) { pinsExpanded=!pinsExpanded; restoreBrowse=true; }
        MeasureBounds?.Invoke("pins-toggle",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(pinsExpanded?"Collapse pinned sources":"Expand pinned sources"));
        if(!pinsExpanded)
        {
            if(MaterialButton.IconButton("manage-pins",MaterialIcon.Star,tonal:installerPage=="Pinned sources",size:32))
            { installerPage="Pinned sources"; selectedInstaller=null; installerMedia?.Clear(); }
            MeasureBounds?.Invoke("nav-Pinned sources",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("Manage pins"));
            return;
        }
        ImGui.Dummy(new(0,10*s));
        MaterialStatus.SectionHeader(UiText.T("Repositories"));
        foreach(var repo in InstallerCatalog.VisiblePinnedRepositories(plugin.Configuration,installerSnapshot))
        {
            if(MaterialNavigation.ListItem("repo-pin-"+repo.Url,repo.Name,repositoryFilter==repo.Url,MaterialIcon.Database))
            { repositoryFilter=repositoryFilter==repo.Url?"":repo.Url; pinnedAuthorFilter=""; selectedInstaller=null; installerPage="Discover"; }
            MaterialText.TextColored(c.OnSurfaceVariant,RepositoryState(repo));
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(repo.Url+"\n"+RepositoryState(repo));
        }
        ImGui.Separator(); MaterialStatus.SectionHeader(UiText.T("Authors"));
        foreach(var author in InstallerCatalog.VisiblePinnedAuthors(plugin.Configuration,installerSnapshot))
            if(MaterialNavigation.ListItem("author-pin-"+author,author,pinnedAuthorFilter==author,MaterialIcon.Person))
            { pinnedAuthorFilter=pinnedAuthorFilter==author?"":author; repositoryFilter=""; selectedInstaller=null; installerPage="Discover"; }
        ImGui.Separator();
        if(InstallerButton("Manage pins","manage-pins",leading:MaterialIcon.Settings,textOnly:true)) { installerPage="Pinned sources"; selectedInstaller=null; installerMedia?.Clear(); }
        MeasureBounds?.Invoke("nav-Pinned sources",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
    }

    private IReadOnlyList<InstallerEntry> FilterInstallerEntries()
    {
        IEnumerable<InstallerEntry> entries=installerSnapshot.Entries.Where(e=>!e.Manifest.IsHidden || e.IsInstalled);
        if(installerPage=="Installed") entries=entries.Where(e=>e.IsInstalled);
        if(installerPage=="Updates") entries=entries.Where(e=>e.HasUpdate);
        if(installerFilter=="Installed") entries=entries.Where(e=>e.IsInstalled);
        if(installerFilter=="Not installed") entries=entries.Where(e=>!e.IsInstalled);
        if(installerFilter=="Repository not added") entries=entries.Where(e=>!e.Repository.IsAdded);
        if(repositoryFilter.Length>0) entries=entries.Where(e=>e.Repository.Url==repositoryFilter);
        if(pinnedAuthorFilter.Length>0) entries=entries.Where(e=>e.Manifest.Authors.Contains(pinnedAuthorFilter,StringComparer.OrdinalIgnoreCase));
        entries=entries.Where(e=>(installerSearch.Length==0 || string.Join(' ',e.Manifest.Name,e.Manifest.InternalName,e.Manifest.Author,e.Manifest.Description,e.Repository.Name,e.Repository.Url,string.Join(' ',e.Manifest.Tags??[])).Contains(installerSearch,StringComparison.OrdinalIgnoreCase))
            && (installerAuthor.Length==0 || e.Manifest.Author.Contains(installerAuthor,StringComparison.OrdinalIgnoreCase))
            && (installerCategory.Length==0 || (e.Compatibility?.Entry.Category??string.Join(' ',e.Manifest.Tags??[])).Contains(installerCategory,StringComparison.OrdinalIgnoreCase)));
        string Key(InstallerEntry e)=>installerSort switch { 1=>e.Repository.Name, 2=>EntryState(e), 6=>e.Manifest.Author, _=>e.Manifest.Name };
        if(installerSort==3)
        {
            Version? VersionKey(InstallerEntry entry)=>entry.Installed?.Runtime.Version??(Version.TryParse(entry.Manifest.Version,out var version)?version:null);
            return (installerSortDescending?entries.OrderByDescending(VersionKey):entries.OrderBy(VersionKey)).ThenBy(e=>e.Identity,StringComparer.Ordinal).ToArray();
        }
        return (installerSortDescending?entries.OrderByDescending(Key,StringComparer.OrdinalIgnoreCase):entries.OrderBy(Key,StringComparer.OrdinalIgnoreCase)).ThenBy(e=>e.Identity,StringComparer.Ordinal).ToArray();
    }

    private void DrawInstallerResults()
    {
        var s=MaterialTheme.Metrics.Scale;
        var width=ImGui.GetContentRegionAvail().X;
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(BotologyPresentation.Compact?36:42,20,preserveHeight:true));
        var switchWidth=112*s;
        MaterialTextField.SearchField("##InstallerSearch",ref installerSearch,
            BotologyPresentation.Field with { CornerRadius=10 },UiText.T("Search plugins, authors or repositories"),(width-switchWidth-12*s)/s,maxLength:512);
        MeasureBounds?.Invoke("installer-search",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        ImGui.SameLine();
        ReadOnlySpan<MaterialSegment<bool>> views=[new(false,"",MaterialIcon.Grid,Key:"cards"),new(true,"",MaterialIcon.List,Key:"table")];
        if(MaterialSegmented.Draw("InstallerView",views,ref tableView,switchWidth/s,disabled:false,showSelectionCheck:false,cornerRadius:6)) restoreBrowse=true;
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax(); var middle=(min.X+max.X)*.5f;
        MeasureBounds?.Invoke("view-cards",min,new(middle,max.Y));
        MeasureBounds?.Invoke("view-table",new(middle,min.Y),max);
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(tableView?"Table":"Cards"));
        foreach(var filter in new[]{"All plugins","Installed","Not installed","Repository not added"})
        {
            var label=UiText.T(filter);
            if(filter!="All plugins") InstallerNextInline(MaterialText.Measure(label).X+52*s);
            var selected=installerFilter==filter;
            var c=MaterialTheme.Current.Colors;
            var appearance=new MaterialControlAppearance(selected?c.Primary:Vector4.Zero,
                selected?(MaterialColor.Contrast(c.Primary,c.Background)>MaterialColor.Contrast(c.Primary,c.OnSurface)?c.Background:c.OnSurface):c.OnSurface,
                selected?c.Primary:c.Outline,24);
            if(MaterialChip.FilterChip("filter-"+filter,label,ref selected,appearance,showSelectionCheck:false)) installerFilter=filter;
            MeasureBounds?.Invoke("filter-"+filter,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        }
        var filterWidth=Math.Min(220*s,(width-100*s)*.36f);
        InstallerNextInline(filterWidth);
        MaterialTextField.SearchField("##InstallerCategory",ref installerCategory,BotologyPresentation.Field,UiText.T("Category"),filterWidth/s,maxLength:128);
        InstallerNextInline(filterWidth);
        MaterialTextField.SearchField("##InstallerAuthor",ref installerAuthor,BotologyPresentation.Field,UiText.T("Author"),filterWidth/s,maxLength:128);
        InstallerNextInline(42*s);
        ImGui.BeginDisabled(plugin.DiscoveryRunning || plugin.InstallerOperation?.IsRunning==true);
        if(MaterialButton.IconButton("installer-refresh",MaterialIcon.Refresh,size:36)) { plugin.RefreshInstaller(); nextInstallerSnapshot=DateTime.MinValue; }
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(plugin.DiscoveryRunning?"Refreshing...":"Refresh"));
        ImGui.EndDisabled();
        if(repositoryFilter.Length>0 || pinnedAuthorFilter.Length>0)
        {
            MaterialText.TextWrapped(repositoryFilter.Length>0?repositoryFilter:pinnedAuthorFilter);
            ImGui.SameLine(); if(InstallerButton("Clear filter","clear-pin-filter")) { repositoryFilter=pinnedAuthorFilter=""; }
        }
        var entries=FilterInstallerEntries();
        MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant,UiText.F("{0} plugins",entries.Count));
        ImGui.SameLine();
        using(var small=MaterialControls.Push(BotologyPresentation.Controls(24,14,preserveHeight:true)))
            if(InstallerButton("Name","InstallerSort",textOnly:true,trailing:MaterialIcon.ChevronDown)) { installerSort=0; installerSortDescending=!installerSortDescending; }
        if(tableView) DrawInstallerTable(entries); else DrawInstallerCards(entries);
    }

    private void DrawInstallerCards(IReadOnlyList<InstallerEntry> entries)
    {
        var s=MaterialTheme.Metrics.Scale;
        var gap=16*s;
        var width=ImGui.GetContentRegionAvail().X-ImGui.GetStyle().ScrollbarSize;
        var minimum=Math.Max(220*s,entries.Count==0?0:entries.Max(e=>MaterialText.Measure(EntryBadgeLabel(e)).X)+40*s);
        var columns=Math.Max(1,(int)((width+gap)/(minimum+gap)));
        var cardWidth=(width-gap*(columns-1))/columns;
        var lineHeight=Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(UiText.T("Repository not added")).Y);
        var actionHeight=Math.Max((BotologyPresentation.Compact?34:40)*s,new[]{"Manage","View details","Install"}.Max(label=>MaterialText.Measure(UiText.T(label)).Y)+8*s);
        var previewHeight=cardWidth*(BotologyPresentation.Compact?.36f:.47f);
        var badgeHeight=Math.Max(28*s,lineHeight+8*s);
        var titleHeight=Math.Max(ImGui.GetTextLineHeight(),entries.Count==0?0:entries.Max(e=>MaterialText.Measure(e.Manifest.Name).Y));
        var height=previewHeight+titleHeight+lineHeight+badgeHeight+actionHeight+(BotologyPresentation.Compact?38:44)*s;
        ImGui.BeginChild("InstallerBrowse",new(-1,Math.Max(80*s,ImGui.GetContentRegionAvail().Y-40*s)),false);
        MeasureBounds?.Invoke("installer-browse",ImGui.GetWindowPos(),ImGui.GetWindowSize());
        if(restoreBrowse || columns!=previousColumns)
        {
            var anchor=Enumerable.Range(0,entries.Count).FirstOrDefault(i=>entries[i].Identity==browseAnchor);
            ImGui.SetScrollY((anchor/columns+browseRemainder)*(height+gap)); restoreBrowse=false;
            preserveBrowseFrames=3;
        }
        previousColumns=columns;
        var scroll=ImGui.GetScrollY();
        var top=Math.Clamp((int)(scroll/(height+gap))*columns,0,Math.Max(0,entries.Count-1));
        if(preserveBrowseFrames>0) preserveBrowseFrames--;
        else if(browseAnchor is null || Math.Abs(scroll-lastBrowseScroll)>.1f || !entries.Any(e=>e.Identity==browseAnchor))
        { browseAnchor=entries.Count>0?entries[top].Identity:null; browseRemainder=scroll%(height+gap)/(height+gap); }
        lastBrowseScroll=scroll;
        var origin=ImGui.GetCursorPos();
        for(var i=0;i<entries.Count;i++)
        {
            ImGui.SetCursorPos(origin+new Vector2(i%columns*(cardWidth+gap),i/columns*(height+gap)));
            if(!ImGui.IsRectVisible(new(cardWidth,height))) continue;
            var entry=entries[i];
            ImGui.PushID(entry.Identity);
            using(var card=InstallerSurface("Card",new(cardWidth/s,height/s),MaterialElevation.Low,outline:true,padding:0,radius:8,
                flags:ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse))
            {
            MeasureBounds?.Invoke("card-"+entry.Identity,ImGui.GetWindowPos(),ImGui.GetWindowPos()+ImGui.GetWindowSize());
            DrawPreview(entry,previewHeight,false);
            var bodyX=12*s;
            ImGui.SetCursorPos(new(bodyX,previewHeight+8*s));
            using(UiText.Font(UiFontRole.BodyStrong))
                if(UiGui.Selectable("plugin-title",false,entry.Manifest.Name)) OpenInstallerProfile(entry);
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(entry.Manifest.Name+"\n"+entry.Manifest.Description+"\n"+CompatibilityState(entry));
            ImGui.SetCursorPosX(bodyX);
            MaterialIcons.Draw(MaterialIcon.Database,ImGui.GetCursorScreenPos()+new Vector2(0,2*s),18*s,MaterialTheme.Current.Colors.OnSurfaceVariant);
            ImGui.Dummy(new(20*s,lineHeight)); ImGui.SameLine(0,6*s);
            var sourceWidth=Math.Max(1,cardWidth-68*s);
            var sourcePos=ImGui.GetCursorScreenPos();
            var draw=ImGui.GetWindowDrawList(); draw.PushClipRect(sourcePos,sourcePos+new Vector2(sourceWidth,lineHeight),true);
            MaterialText.AddText(draw,sourcePos,MaterialCanvas.Color(MaterialTheme.Current.Colors.OnSurfaceVariant),entry.Repository.Name); draw.PopClipRect();
            ImGui.Dummy(new(sourceWidth,lineHeight));
            ImGui.SameLine(0,0);
            var pinned=InstallerCatalog.VisiblePinnedRepositories(plugin.Configuration,installerSnapshot).Any(r=>r.Url==entry.Repository.Url);
            using(var pinControls=MaterialControls.Push(BotologyPresentation.Controls(lineHeight/s,16,preserveHeight:true)))
                if(MaterialButton.IconButton("card-pin",pinned?MaterialIcon.Star:MaterialIcon.Plus,size:lineHeight/s)) plugin.ToggleRepositoryPin(entry.Repository.Url);
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(entry.Repository.Name+"\n"+UiText.T(pinned?"Unpin repository":"Pin repository"));
            ImGui.SetCursorPosX(bodyX);
            DrawEntryBadge(entry,cardWidth-24*s,badgeHeight);
            MeasureBounds?.Invoke("card-text-end-"+entry.Identity,ImGui.GetCursorScreenPos(),ImGui.GetCursorScreenPos());
            ImGui.SetCursorPos(new(bodyX,height-actionHeight-12*s));
            using(var actions=MaterialControls.Push(BotologyPresentation.Controls(actionHeight/s,18,preserveHeight:true)))
            {
                ImGui.BeginDisabled(entry.CanInstall && plugin.InstallerOperation?.IsRunning==true);
                if(InstallerButton(entry.CanInstall?"Install":entry.IsInstalled?"Manage":"View details","details-"+i,
                    size:new((cardWidth-24*s)/s,actionHeight/s),trailing:MaterialIcon.ArrowRight,primary:entry.CanInstall))
                { if(entry.CanInstall) plugin.RunInstallerAction(InstallerAction.Install,entry.Repository.Url,entry.Manifest.InternalName); else OpenInstallerProfile(entry); }
                ImGui.EndDisabled();
            }
            }
            ImGui.PopID();
        }
        ImGui.SetCursorPos(origin+new Vector2(0,(int)Math.Ceiling(entries.Count/(double)columns)*(height+gap)));
        if(entries.Count==0) MaterialText.TextWrapped(UiText.T("No plugins match the current filters."));
        ImGui.Dummy(new(1,1)); ImGui.EndChild();
    }

    private static string EntryBadgeLabel(InstallerEntry entry)=>entry.Installed?.State is "LoadError" or "UnloadError" or "DependencyResolutionFailed"
        ?UiText.T("Operation failed"):EntryState(entry);

    private void DrawEntryBadge(InstallerEntry entry,float width=0,float height=0)
    {
        var s=MaterialTheme.Metrics.Scale;
        var c=MaterialTheme.Current.Colors;
        var color=entry.Installed?.State is "LoadError" or "UnloadError" or "DependencyResolutionFailed"?BotologyPresentation.RedCounter
            :entry.HasUpdate?BotologyPresentation.YellowCounter:entry.IsLoaded?BotologyPresentation.Rgb(0xB6A5FF)
            :entry.IsInstalled?c.OnSurfaceVariant:!entry.Repository.IsAdded || !entry.Repository.IsEnabled || !entry.IsApiCompatible?BotologyPresentation.YellowCounter:c.Primary;
        var label="    "+EntryBadgeLabel(entry);
        var size=new Vector2(Math.Min(width>0?width:float.MaxValue,MaterialText.Measure(label).X+18*s),height>0?height:Math.Max(28*s,MaterialText.Measure(label).Y+8*s));
        var p=ImGui.GetCursorScreenPos();
        MaterialStatus.Badge(label,new MaterialControlAppearance(MaterialColor.Alpha(color,.12f),color,MaterialColor.Alpha(color,.4f),18),size/s);
        MaterialIcons.Draw(entry.Installed?.State is "LoadError" or "UnloadError" or "DependencyResolutionFailed"?MaterialIcon.Warning:entry.IsLoaded?MaterialIcon.Play:entry.IsInstalled?MaterialIcon.Check:!entry.Repository.IsAdded?MaterialIcon.Warning:MaterialIcon.Download,
            p+new Vector2(8*s,(size.Y-16*s)*.5f),16*s,color);
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(EntryState(entry)+"\n"+CompatibilityState(entry));
    }

    private void DrawInstallerTable(IReadOnlyList<InstallerEntry> entries)
    {
        var s=MaterialTheme.Metrics.Scale;
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(BotologyPresentation.Compact?28:34,18,preserveHeight:true)
            with { CellPadding=new Vector2(10,BotologyPresentation.Compact?6:8)*s });
        var flags=ImGuiTableFlags.RowBg|ImGuiTableFlags.Borders|ImGuiTableFlags.Resizable|ImGuiTableFlags.Sortable|ImGuiTableFlags.ScrollY|ImGuiTableFlags.ScrollX|ImGuiTableFlags.SizingFixedFit;
        var labels=new[]{"Plugin","Repository","Author","State","Version","Compatibility","Action"};
        var widths=new[]{160f,140,140,210,90,150,140};
        using var tableColors=new MaterialStyleScope();
        tableColors.Color(ImGuiCol.TableHeaderBg,InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerHigh,.7f));
        tableColors.Color(ImGuiCol.TableRowBg,InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerLowest,.7f));
        tableColors.Color(ImGuiCol.TableRowBgAlt,InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerLow,.8f));
        uint[] ids=[1,2,7,3,4,5,6];
        for(var i=0;i<labels.Length;i++) widths[i]=Math.Max(widths[i]*s,MaterialText.Measure(UiText.T(labels[i])).X+32*s);
        var tableHeight=Math.Max(80*s,ImGui.GetContentRegionAvail().Y-140*s);
        if(ImGui.BeginTable("InstallerTable",7,flags,new(-1,tableHeight),Math.Max(ImGui.GetContentRegionAvail().X,widths.Sum()+14*ImGui.GetStyle().CellPadding.X)))
        {
            for(var i=0;i<labels.Length;i++) ImGui.TableSetupColumn(labels[i],(i<3?ImGuiTableColumnFlags.WidthStretch:ImGuiTableColumnFlags.WidthFixed)|(i>=5?ImGuiTableColumnFlags.NoSort:ImGuiTableColumnFlags.None),widths[i],ids[i]);
            ImGui.TableSetupScrollFreeze(0,1);
            UiGui.TableHeadersRow(BotologyPresentation.TableHeaderHeight*s);
            unsafe
            {
                var header=ImGuiP.GetCurrentTable();
                for(var i=0;i<labels.Length;i++)
                    MeasureBounds?.Invoke("installer-column-"+labels[i],new(header.Columns.Data[i].MinX,header.RowPosY1),new(header.Columns.Data[i].MaxX,header.RowPosY2));
            }
            var specs=ImGui.TableGetSortSpecs();
            if(specs.SpecsCount>0 && specs.SpecsDirty) { installerSort=(int)specs.Specs.ColumnUserID-1; installerSortDescending=specs.Specs.SortDirection==ImGuiSortDirection.Descending; specs.SpecsDirty=false; }
            foreach(var entry in entries)
            {
                ImGui.PushID(entry.Identity);
                ImGui.TableNextRow(ImGuiTableRowFlags.None,(BotologyPresentation.Compact?42:56)*s);
                ImGui.TableSetColumnIndex(0);
                var iconSize=new Vector2(28*s);
                if(ImGui.IsRectVisible(iconSize)) DrawInstallerIcon(entry,ImGui.GetCursorScreenPos(),iconSize.X);
                ImGui.Dummy(iconSize); ImGui.SameLine();
                if(UiGui.Selectable("details",highlightedInstaller==entry.Identity,entry.Manifest.Name)) highlightedInstaller=entry.Identity;
                if(ImGui.IsItemHovered()) MaterialText.SetTooltip(entry.Manifest.Description);
                ImGui.TableSetColumnIndex(1); MaterialText.TextWrapped(entry.Repository.Name);
                ImGui.TableSetColumnIndex(2); MaterialText.TextWrapped(entry.Manifest.Author);
                ImGui.TableSetColumnIndex(3); DrawEntryBadge(entry);
                ImGui.TableSetColumnIndex(4); MaterialText.TextWrapped(entry.IsInstalled?entry.Installed!.Runtime.Version+(entry.HasUpdate?" -> "+entry.Manifest.Version:""):entry.Manifest.Version);
                ImGui.TableSetColumnIndex(5); MaterialText.TextWrapped(CompatibilityState(entry));
                ImGui.TableSetColumnIndex(6);
                ImGui.BeginDisabled(entry.CanInstall && plugin.InstallerOperation?.IsRunning==true);
                if(InstallerButton(entry.CanInstall?"Install":entry.IsInstalled?"Manage":"View details","table-details",primary:entry.CanInstall||!entry.Repository.IsAdded))
                { if(entry.CanInstall) plugin.RunInstallerAction(InstallerAction.Install,entry.Repository.Url,entry.Manifest.InternalName); else OpenInstallerProfile(entry); }
                ImGui.EndDisabled();
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        var selected=entries.FirstOrDefault(e=>e.Identity==highlightedInstaller)??entries.FirstOrDefault();
        if(selected is not null)
        {
            using var summary=InstallerSurface("InstallerSelection",new(-1,86),MaterialElevation.Low,outline:true,padding:10,radius:8);
            if(ImGui.BeginTable("Selection",2,ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Name",ImGuiTableColumnFlags.WidthStretch,2);
                ImGui.TableSetupColumn("Action",ImGuiTableColumnFlags.WidthStretch,1);
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
                using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(selected.Manifest.Name);
                MaterialText.TextWrapped(EntryState(selected));
                ImGui.TableSetColumnIndex(1);
                if(InstallerButton("View details","selection-details",size:new(-1,0),trailing:MaterialIcon.ArrowRight)) OpenInstallerProfile(selected);
                ImGui.EndTable();
            }
        }
    }

    private void OpenInstallerProfile(InstallerEntry entry)
    { selectedInstaller=entry.Identity; galleryIndex=0; profileSourceHeight=0; pendingRemoval=null; resetInstallerContentScroll=true; installerMedia?.Clear(); }

    private void DrawInstallerProfile(InstallerEntry entry)
    {
        var s=MaterialTheme.Metrics.Scale;
        var c=MaterialTheme.Current.Colors;
        if(InstallerButton("Back to results","profile-back",leading:MaterialIcon.Return,textOnly:true))
        { selectedInstaller=null; resetInstallerContentScroll=true; installerMedia?.Clear(); restoreBrowse=true; return; }
        var header=ImGui.GetCursorScreenPos();
        DrawInstallerIcon(entry,header,76*s);
        ImGui.Dummy(new(76*s,76*s)); ImGui.SameLine(0,18*s);
        ImGui.BeginGroup();
        using(UiText.Font(UiFontRole.CompactTitle)) MaterialText.TextWrapped(entry.Manifest.Name);
        DrawEntryBadge(entry);
        ImGui.EndGroup();
        MaterialText.TextWrapped(entry.Manifest.Description);
        var repoPinned=InstallerCatalog.VisiblePinnedRepositories(plugin.Configuration,installerSnapshot).Any(r=>r.Url==entry.Repository.Url);
        MaterialText.TextColored(c.OnSurfaceVariant,UiText.T("Repository")+": "+entry.Repository.Name);
        InstallerNextInline(MaterialText.Measure(UiText.T(repoPinned?"Unpin repository":"Pin repository")).X+52*s);
        if(InstallerButton(repoPinned?"Unpin repository":"Pin repository","profile-pin-repo",repoPinned,leading:MaterialIcon.Star,textOnly:true)) plugin.ToggleRepositoryPin(entry.Repository.Url);
        foreach(var author in entry.Manifest.Authors)
        {
            ImGui.PushID(author);
            InstallerNextInline(MaterialText.Measure(author).X+240*s);
            MaterialText.TextColored(c.OnSurfaceVariant,UiText.T("Author")+": "+author);
            InstallerNextInline(MaterialText.Measure(UiText.T("Pin author")).X+52*s);
            var pinned=InstallerCatalog.VisiblePinnedAuthors(plugin.Configuration,installerSnapshot).Contains(author,StringComparer.OrdinalIgnoreCase);
            if(InstallerButton(pinned?"Unpin author":"Pin author","profile-pin-author",pinned,leading:MaterialIcon.Person,textOnly:true)) plugin.ToggleAuthorPin(author);
            ImGui.PopID();
        }
        ImGui.Separator();
        var width=ImGui.GetContentRegionAvail().X;
        var stacked=width<650*s;
        var sidebar=Math.Max(260*s,Math.Min(420*s,width*.34f));
        var mediaWidth=stacked?width-24*s:Math.Max(200*s,width-sidebar-28*s);
        var previewUrls=entry.Manifest.PublisherImageUrls;
        var selectedMissing=previewUrls.Length>0 && installerMedia?.IsMissing(previewUrls[Math.Clamp(galleryIndex,0,previewUrls.Length-1)],true)==true;
        var hasGif=previewUrls.Length>0 && !selectedMissing && (IsGif(previewUrls[Math.Clamp(galleryIndex,0,previewUrls.Length-1)])
            || installerMedia?.IsGif(previewUrls[Math.Clamp(galleryIndex,0,previewUrls.Length-1)])==true);
        var hasThumbnails=previewUrls.Any(url=>installerMedia?.IsMissing(url,false)!=true);
        var galleryControls=48*s+ImGui.GetTextLineHeight()+(hasThumbnails?60*s:0)+(hasGif?44*s:0);
        var previewHeight=previewUrls.Length>0&&!selectedMissing?Math.Clamp(mediaWidth*9/16,150*s,480*s):Math.Min(190*s,Math.Max(100*s,mediaWidth*9/16));
        var mediaHeight=previewHeight+galleryControls;
        if(ImGui.BeginTable("ProfileColumns",stacked?1:2,ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Media",ImGuiTableColumnFlags.WidthStretch,mediaWidth);
            if(!stacked) ImGui.TableSetupColumn("Source",ImGuiTableColumnFlags.WidthFixed,sidebar);
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
            using(var gallery=InstallerSurface("ProfileGallery",new(-1,mediaHeight/s),MaterialElevation.Low,outline:true,padding:12,radius:8))
            {
                using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(UiText.T("Screenshots"));
                DrawPreview(entry,previewHeight,true);
                var images=entry.Manifest.PublisherImageUrls;
                var candidates=images.Count(url=>installerMedia?.IsMissing(url,false)!=true);
                var thumbWidth=Math.Min(90*s,Math.Max(36*s,(ImGui.GetContentRegionAvail().X-12*s*Math.Max(0,candidates-1))/Math.Max(1,candidates)));
                var visibleThumbs=0;
                for(var i=0;i<images.Length;i++)
                {
                    var image=images[i];
                    var thumb=(installerMedia??=new(Plugin.TextureProvider)).Get(image,false,out _);
                    if(thumb is null && installerMedia.IsMissing(image,false)) continue;
                    if(visibleThumbs++>0) ImGui.SameLine();
                    if(thumb is not null)
                    {
                        if(MaterialCanvas.ImageButton("media-"+i,thumb.Handle,new(thumbWidth/s,50),galleryIndex==i)) { galleryIndex=i; installerMedia?.Stop(); }
                    }
                    else if(InstallerButton((i+1).ToString(),"media-"+i,galleryIndex==i,size:new(thumbWidth/s,50))) { galleryIndex=i; installerMedia?.Stop(); }
                    MeasureBounds?.Invoke("media-"+i,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
                }
                if(images.Length>0 && !selectedMissing && (IsGif(images[Math.Clamp(galleryIndex,0,images.Length-1)]) || installerMedia?.IsGif(images[Math.Clamp(galleryIndex,0,images.Length-1)])==true))
                {
                    var url=images[Math.Clamp(galleryIndex,0,images.Length-1)];
                    var active=installerMedia?.IsPlaying(url)==true;
                    if(InstallerButton(active?"Stop preview":"Play preview","media-play",leading:active?MaterialIcon.Stop:MaterialIcon.Play,primary:!active))
                    { if(active) installerMedia?.Stop(); else (installerMedia??=new(Plugin.TextureProvider)).Play(url); }
                }
            }
            using(var overview=InstallerSurface("ProfileOverview",new(-1,220),MaterialElevation.Low,outline:true,padding:14,radius:8))
            {
                using var tabs=MaterialTabs.Begin("ProfileSections",new[]{UiText.T("Overview"),UiText.T("Release notes"),UiText.T("Compatibility")});
                if(tabs.Visible)
                {
                    using(var tab=MaterialTabs.Item(UiText.T("Overview")+"##Overview"))
                        if(tab.Visible) MaterialText.TextWrapped(entry.Manifest.Description);
                    using(var tab=MaterialTabs.Item(UiText.T("Release notes")+"##Notes"))
                        if(tab.Visible) MaterialText.TextWrapped(string.IsNullOrWhiteSpace(entry.Manifest.Changelog)?UiText.T("No release notes supplied."):entry.Manifest.Changelog);
                    using(var tab=MaterialTabs.Item(UiText.T("Compatibility")+"##Compatibility"))
                        if(tab.Visible) DrawInstallerCompatibility(entry);
                }
            }
            ImGui.TableSetColumnIndex(stacked?0:1);
            var sidebarHeight=(profileSourceHeight>0?profileSourceHeight:600)*s;
            using(var source=InstallerSurface("ProfileSource",new(-1,sidebarHeight/s),MaterialElevation.Low,outline:true,padding:16,radius:8))
            {
                using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(UiText.T("Plugin status"));
                MaterialText.TextWrapped(UiText.T("Installed")+": "+UiText.T(entry.IsInstalled?"Yes":"No"));
                MaterialText.TextWrapped(UiText.T("Loaded")+": "+UiText.T(entry.IsLoaded?"Yes":"No"));
                MaterialText.TextWrapped(UiText.T("Version")+": "+(entry.Installed?.Runtime.Version.ToString()??entry.Manifest.Version));
                MaterialText.TextWrapped(RepositoryState(entry.Repository));
                if(InstallerCatalog.NormalizeRepository(entry.Manifest.ProjectUrl) is { } project && InstallerButton("Project website","profile-project",leading:MaterialIcon.Globe,textOnly:true)) plugin.OpenUrl(project);
                if(entry.Compatibility?.Entry.DiscordUrl is { } discord && RepositoryLinkResolver.NormalizeDiscordUrl(discord) is { } safe && InstallerButton("Discord","profile-discord",leading:MaterialIcon.Discord,textOnly:true)) plugin.OpenUrl(safe);
                ImGui.Separator();
                using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(UiText.T("Repository"));
                MaterialText.TextColored(c.Primary,entry.Repository.Name);
                var feed=entry.Repository.Url;
                var copySide=MaterialControls.Metrics.Height;
                MaterialTextField.TextField("ProfileFeed",ref feed,width:Math.Max(1,ImGui.GetContentRegionAvail().X-copySide-8*s)/s,
                    flags:ImGuiInputTextFlags.ReadOnly,maxLength:2048);
                if(ImGui.IsItemHovered()) MaterialText.SetTooltip(entry.Repository.Url);
                ImGui.SameLine(0,8*s);
                if(MaterialButton.IconButton("profile-copy",MaterialIcon.Copy,size:copySide/s)) ImGui.SetClipboardText(entry.Repository.Url);
                if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("Copy"));
                if(!entry.Repository.IsAdded) MaterialText.TextWrapped(UiText.T("Adding a repository does not install its plugins."));
                DrawRepositoryActions(entry.Repository);
                ImGui.Separator(); DrawPluginActions(entry);
                ImGui.Separator();
                MaterialText.TextWrapped(UiText.T("Pinning a source does not add its repository."));
                profileSourceHeight=ImGui.GetCursorPosY()/s+16;
                MeasureBounds?.Invoke("profile-source-surface",ImGui.GetWindowPos(),ImGui.GetWindowPos()+ImGui.GetWindowSize());
                MeasureBounds?.Invoke("profile-source-content",ImGui.GetWindowPos(),ImGui.GetWindowPos()+new Vector2(ImGui.GetWindowWidth(),profileSourceHeight*s));
            }
            using(var compatibility=InstallerSurface("ProfileAssessment",new(-1,120),MaterialElevation.Low,outline:true,padding:14,radius:8))
            {
                using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(UiText.T("Compatibility"));
                MaterialText.TextWrapped(CompatibilityState(entry));
            }
            ImGui.EndTable();
        }
    }

    private void DrawInstallerCompatibility(InstallerEntry entry)
    {
        MaterialText.TextWrapped(CompatibilityState(entry));
        if(entry.Compatibility is not { } row) return;
        MaterialText.TextWrapped(UiText.Details(row.Assessment));
        MaterialText.TextWrapped(row.Entry.Notes);
        DrawIgnoreColumn(row);
        if(row.IsInstalled) DrawDtrColumn(row,plugin.CaptureDtrEntries());
        DrawAiAttributionColumn(row);
    }

    private void DrawPluginActions(InstallerEntry entry)
    {
        if(entry.NameCollision) MaterialText.TextWrapped(UiText.T("This name is installed from another source."));
        if(!entry.IsApiCompatible) MaterialText.TextWrapped(UiText.T("Unavailable for this API"));
        ImGui.BeginDisabled(plugin.InstallerOperation?.IsRunning==true);
        void Action(string label,InstallerAction action,bool enabled)
        {
            ImGui.BeginDisabled(!enabled);
            if(InstallerButton(label,"plugin-action-"+action,primary:action is InstallerAction.Install or InstallerAction.Update)) plugin.RunInstallerAction(action,entry.Repository.Url,entry.Manifest.InternalName);
            ImGui.EndDisabled();
        }
        if(!entry.IsInstalled) Action("Install",InstallerAction.Install,entry.CanInstall);
        else
        {
            if(entry.HasUpdate) Action("Update",InstallerAction.Update,entry.CanUpdate);
            Action(entry.IsLoaded?"Disable":"Enable",entry.IsLoaded?InstallerAction.Disable:InstallerAction.Enable,
                entry.HostReady && !entry.HostSafeMode && entry.Installed is { IsBanned:false, ScheduledForDeletion:false } && entry.Installed.State is not ("Loading" or "Unloading"));
            ImGui.BeginDisabled(!entry.CanUninstall || entry.Installed!.ScheduledForDeletion);
            if(InstallerButton("Uninstall","action-Uninstall")) pendingRemoval=entry.Identity;
            ImGui.EndDisabled();
            if(pendingRemoval==entry.Identity)
            {
                MaterialText.TextWrapped(UiText.T("Uninstall this plugin? Its settings are retained."));
                if(InstallerButton("Confirm uninstall","action-Confirm-uninstall")) { plugin.RunInstallerAction(InstallerAction.Uninstall,entry.Repository.Url,entry.Manifest.InternalName); pendingRemoval=null; }
                ImGui.SameLine(); if(InstallerButton("Cancel","action-Cancel")) pendingRemoval=null;
            }
        }
        ImGui.EndDisabled();
    }

    private void DrawInstallerRepositories()
    {
        var s=MaterialTheme.Metrics.Scale;
        using(UiText.Font(UiFontRole.CompactTitle)) MaterialText.Text(UiText.T("Repositories"));
        DrawInstallerSourceTabs();
        MaterialText.TextWrapped(UiText.T("Adding a repository does not install its plugins."));
        MaterialTextField.TextField("##ManualRepository",ref manualRepository,UiText.T("Repo JSON URL"),leading:MaterialIcon.Link,maxLength:2048);
        if(InstallerCatalog.NormalizeRepository(manualRepository) is { } url)
        {
            MaterialText.TextWrapped(url);
            DrawRepositoryActions(installerSnapshot.Repositories.FirstOrDefault(r=>r.Url==url) ?? new(url,InstallerCatalog.RepositoryName(url),false,false,url==InstallerCatalog.OfficialRepository));
        }
        if(InstallerButton("Refresh","repositories-refresh",leading:MaterialIcon.Refresh)) plugin.RefreshInstaller();
        foreach(var repo in installerSnapshot.Repositories)
        {
            ImGui.PushID(repo.Url);
            var urlHeight=MaterialText.Measure(repo.Url,false,Math.Max(1,ImGui.GetContentRegionAvail().X-32*s)).Y;
            var height=Math.Max(150*s,urlHeight+3*ImGui.GetTextLineHeight()+(repo.IsOfficial?1:repo.IsAdded?3:2)*(MaterialControls.Metrics.Height+10*s)+44*s+(pendingRemoval==repo.Url?180*s:0));
            using(var card=InstallerSurface("RepositoryCard",new(-1,height/s),outline:true,padding:16,radius:8))
            {
            using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(repo.Name);
            MaterialText.TextWrapped(repo.Url); MaterialText.TextWrapped(RepositoryState(repo));
            if(plugin.DiscoveryErrors.TryGetValue(repo.Url,out var error)) MaterialText.TextWrapped(UiText.T("Discovery failed")+": "+error);
            DrawRepositoryActions(repo);
            var pinned=InstallerCatalog.VisiblePinnedRepositories(plugin.Configuration,installerSnapshot).Any(r=>r.Url==repo.Url);
            if(InstallerButton(pinned?"Unpin repository":"Pin repository","repository-pin",pinned,leading:MaterialIcon.Star,textOnly:true)) plugin.ToggleRepositoryPin(repo.Url);
            }
            ImGui.PopID();
        }
    }

    private void DrawInstallerSourceTabs()
    {
        if(InstallerButton("Repositories","sources-repositories",installerPage=="Repositories",textOnly:true)) installerPage="Repositories";
        InstallerNextInline(MaterialText.Measure(UiText.T("Pinned sources")).X+28*MaterialTheme.Metrics.Scale);
        if(InstallerButton("Pinned sources","sources-pins",installerPage=="Pinned sources",textOnly:true)) installerPage="Pinned sources";
        ImGui.Separator();
    }

    private void DrawRepositoryActions(InstallerRepository repo)
    {
        if(repo.IsOfficial || InstallerCatalog.NormalizeRepository(repo.Url) is null) return;
        ImGui.BeginDisabled(plugin.InstallerOperation?.IsRunning==true);
        if(!repo.IsAdded)
        { if(InstallerButton("Add repository","repo-add-"+repo.Url,primary:true,leading:MaterialIcon.Plus)) plugin.RunInstallerAction(InstallerAction.AddRepository,repo.Url); }
        else
        {
            if(InstallerButton(repo.IsEnabled?"Disable repository":"Enable repository","repo-enable")) plugin.RunInstallerAction(repo.IsEnabled?InstallerAction.DisableRepository:InstallerAction.EnableRepository,repo.Url);
            if(InstallerButton("Remove repository","action-Remove-repository")) pendingRemoval=repo.Url;
            if(pendingRemoval==repo.Url)
            {
                MaterialText.TextWrapped(UiText.T("Removing a repository does not uninstall its plugins."));
                if(InstallerButton("Confirm removal","action-Confirm-removal")) { plugin.RunInstallerAction(InstallerAction.RemoveRepository,repo.Url); pendingRemoval=null; }
                ImGui.SameLine(); if(InstallerButton("Cancel","action-Cancel")) pendingRemoval=null;
            }
        }
        ImGui.EndDisabled();
    }
    private void DrawPinsManager()
    {
        var s=MaterialTheme.Metrics.Scale;
        using(UiText.Font(UiFontRole.CompactTitle)) MaterialText.Text(UiText.T("Pinned sources"));
        DrawInstallerSourceTabs();
        MaterialTextField.SearchField("##PinnedSearch",ref pinSearch,BotologyPresentation.Field,
            UiText.T("Search plugins, authors or repositories"),Math.Min(620,ImGui.GetContentRegionAvail().X/s),maxLength:512);
        var repositories=InstallerCatalog.VisiblePinnedRepositories(plugin.Configuration,installerSnapshot)
            .Where(r=>pinSearch.Length==0||(r.Name+" "+r.Url).Contains(pinSearch,StringComparison.OrdinalIgnoreCase))
            .Select(r=>(r.Name,Detail:RepositoryState(r)+"\n"+r.Url,Key:r.Url)).ToArray();
        var authors=InstallerCatalog.VisiblePinnedAuthors(plugin.Configuration,installerSnapshot)
            .Where(a=>pinSearch.Length==0||a.Contains(pinSearch,StringComparison.OrdinalIgnoreCase))
            .Select(a=>(Name:a,Detail:UiText.F("{0} plugins",installerSnapshot.Entries.Count(e=>e.Manifest.Authors.Contains(a,StringComparer.OrdinalIgnoreCase))),Key:a)).ToArray();
        DrawPinnedGroup("PinnedRepositories","Repositories",repositories,true);
        DrawPinnedGroup("PinnedAuthors","Authors",authors,false);
        using(var note=InstallerSurface("PinnedNote",new(-1,Math.Max(68,MaterialText.Measure(UiText.T("Pinning a source does not add its repository."),false,ImGui.GetContentRegionAvail().X-24*s).Y/s+24)),outline:true,padding:12,radius:8))
            MaterialText.TextWrapped(UiText.T("Pinning a source does not add its repository."));
    }

    private void DrawPinnedGroup(string id,string label,IReadOnlyList<(string Name,string Detail,string Key)> rows,bool repository)
    {
        if(rows.Count==0) return;
        var s=MaterialTheme.Metrics.Scale;
        var viewWidth=MaterialText.Measure(UiText.T("View plugins")).X+52*s;
        var removeLabel=repository?"Unpin repository":"Unpin author";
        var removeWidth=MaterialText.Measure(UiText.T(removeLabel)).X+52*s;
        var rowWidth=ImGui.GetContentRegionAvail().X-28*s;
        var spacing=ImGui.GetStyle().ItemSpacing.Y;
        var stacked=rowWidth<180*s+viewWidth+removeWidth+6*ImGui.GetStyle().CellPadding.X;
        var textWidth=Math.Max(100*s,stacked?rowWidth:rowWidth-viewWidth-removeWidth-6*ImGui.GetStyle().CellPadding.X);
        var actionHeight=Math.Max(MaterialControls.Metrics.Height,Math.Max(MaterialText.Measure(UiText.T("View plugins")).Y,
            MaterialText.Measure(UiText.T(removeLabel)).Y)+2*MaterialControls.Metrics.Padding.Y);
        var actionRows=rowWidth>=viewWidth+removeWidth+ImGui.GetStyle().ItemSpacing.X?1:2;
        var height=rows.Sum(row=>
        {
            var textHeight=MaterialText.Measure(row.Name,false,textWidth).Y+MaterialText.Measure(row.Detail,false,textWidth).Y+spacing;
            return (stacked?textHeight+actionRows*(actionHeight+spacing):Math.Max(textHeight,actionHeight)+2*ImGui.GetStyle().CellPadding.Y)+2*spacing;
        })+MaterialText.Measure(UiText.T(label)).Y+56*s;
        using var group=InstallerSurface(id,new(-1,height/s),outline:true,padding:14,radius:8);
        using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(UiText.T(label));
        ImGui.Separator();
        foreach(var row in rows)
        {
            ImGui.PushID(row.Key);
            if(!stacked)
            {
                if(!ImGui.BeginTable("PinnedRow",3,ImGuiTableFlags.SizingStretchProp)) { ImGui.PopID(); continue; }
                ImGui.TableSetupColumn("Name",ImGuiTableColumnFlags.WidthStretch,1);
                ImGui.TableSetupColumn("Browse",ImGuiTableColumnFlags.WidthFixed,viewWidth);
                ImGui.TableSetupColumn("Pin",ImGuiTableColumnFlags.WidthFixed,removeWidth);
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
            }
            using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.TextWrapped(row.Name);
            MaterialText.TextWrapped(row.Detail);
            if(!stacked) ImGui.TableSetColumnIndex(1);
            if(InstallerButton("View plugins",(repository?"pin-repo-view-":"pin-author-view-")+row.Key,leading:repository?MaterialIcon.Database:MaterialIcon.Person))
            {
                repositoryFilter=repository?row.Key:""; pinnedAuthorFilter=repository?"":row.Key;
                installerPage="Discover"; selectedInstaller=null; resetInstallerContentScroll=true;
            }
            if(stacked) InstallerNextInline(removeWidth); else ImGui.TableSetColumnIndex(2);
            if(InstallerButton(removeLabel,(repository?"pin-repo-remove-":"pin-author-remove-")+row.Key,leading:MaterialIcon.Star))
            { if(repository) plugin.ToggleRepositoryPin(row.Key); else plugin.ToggleAuthorPin(row.Key); }
            if(!stacked) ImGui.EndTable();
            ImGui.Separator(); ImGui.PopID();
        }
    }

    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? InstallerIcon(InstallerEntry entry)
    {
        if(InstallerCatalog.NormalizeRepository(entry.Manifest.PublisherIconUrl) is { } url)
            return (installerMedia??=new(Plugin.TextureProvider)).Get(url,false,out _);
        return entry.IsInstalled && entry.Manifest.InternalName.Equals("botology",StringComparison.OrdinalIgnoreCase)?BotologyPresentation.OriginalIcon:null;
    }

    private void DrawInstallerIcon(InstallerEntry entry,Vector2 origin,float side)
    {
        var icon=InstallerIcon(entry);
        if(icon is not null)
            MaterialCanvas.DrawImage(ImGui.GetWindowDrawList(),icon.Handle,new(icon.Width,icon.Height),origin,origin+new Vector2(side),
                tint:entry.Repository.IsAdded?Vector4.One:new Vector4(.48f,.48f,.48f,1));
        else MaterialIcons.Draw(MaterialIcon.Cube,origin+new Vector2(side*.15f),side*.7f,MaterialTheme.Current.Colors.OnSurfaceVariant);
    }

    private void DrawPreview(InstallerEntry entry,float height,bool full)
    {
        var images=entry.Manifest.PublisherImageUrls;
        var url=images.Length>0?images[Math.Clamp(full?galleryIndex:0,0,images.Length-1)]:entry.Manifest.PublisherIconUrl;
        var origin=ImGui.GetCursorScreenPos(); var size=new Vector2(Math.Max(1,ImGui.GetContentRegionAvail().X),height);
        var s=MaterialTheme.Metrics.Scale;
        var draw=ImGui.GetWindowDrawList();
        MaterialCanvas.Surface(origin,origin+size,MaterialTheme.Current.Colors.SurfaceContainerHigh,MaterialTheme.Current.Colors.SurfaceContainerLowest,8*s);
        var failed=false;
        Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? texture=null;
        if(InstallerCatalog.NormalizeRepository(url) is not null)
        {
            installerMedia??=new(Plugin.TextureProvider);
            texture=full && installerMedia.IsPlaying(url!)?installerMedia.Animation(url!,out failed):installerMedia.Get(url,full,out failed);
        }
        var hasPreview=texture is not null && images.Length>0;
        if(texture is null && images.Length>0 && InstallerCatalog.NormalizeRepository(entry.Manifest.PublisherIconUrl) is not null)
            texture=(installerMedia??=new(Plugin.TextureProvider)).Get(entry.Manifest.PublisherIconUrl,false,out _);
        if(texture is null && images.Length==0) texture=InstallerIcon(entry);
        var label=images.Length==0 || failed && installerMedia?.IsMissing(url,full)==true ? UiText.T("No preview supplied")
            :failed?UiText.T("Preview unavailable"):null;
        var labelHeight=label is null?0:MaterialText.Measure(label,false,Math.Max(1,size.X-16*s)).Y;
        var placeholderSide=Math.Min(48*s,Math.Max(1,height-labelHeight-12*s));
        var placeholderTop=Math.Max(0,(height-placeholderSide-labelHeight-8*s)*.5f);
        var tint=full||entry.Repository.IsAdded?Vector4.One:new Vector4(.48f,.48f,.48f,1);
        if(texture is not null)
        {
            var media=hasPreview && !failed;
            var min=media?origin:origin+new Vector2((size.X-placeholderSide)*.5f,placeholderTop);
            var max=media?origin+size:min+new Vector2(placeholderSide);
            if(media && !full)
            {
                var imageAspect=(float)texture.Width/texture.Height;
                var boxAspect=size.X/size.Y;
                var span=imageAspect>boxAspect?new Vector2(boxAspect/imageAspect,1):new Vector2(1,imageAspect/boxAspect);
                draw.AddImageRounded(texture.Handle,min,max,(Vector2.One-span)*.5f,(Vector2.One+span)*.5f,
                    MaterialCanvas.Color(tint),8*s,ImDrawFlags.RoundCornersTop);
            }
            else MaterialCanvas.DrawImage(draw,texture.Handle,new(texture.Width,texture.Height),min,max,tint:tint);
            if(!media) MeasureBounds?.Invoke("preview-icon-"+entry.Identity,min,max);
        }
        else
        {
            var iconMin=origin+new Vector2((size.X-placeholderSide)*.5f,placeholderTop);
            MaterialIcons.Draw(MaterialIcon.Cube,iconMin,placeholderSide,MaterialTheme.Current.Colors.OnSurfaceVariant);
            MeasureBounds?.Invoke("preview-icon-"+entry.Identity,iconMin,iconMin+new Vector2(placeholderSide));
        }
        if(label is not null)
        {
            var labelWidth=Math.Min(size.X-16*s,MaterialText.Measure(label).X);
            ImGui.SetCursorScreenPos(origin+new Vector2(Math.Max(8*s,(size.X-labelWidth)*.5f),placeholderTop+placeholderSide+8*s));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX()+size.X-16*s);
            MaterialText.TextWrapped(label); ImGui.PopTextWrapPos();
            MeasureBounds?.Invoke("preview-label-"+entry.Identity,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        }
        ImGui.SetCursorScreenPos(origin); ImGui.Dummy(size);
    }
    private static bool IsGif(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.AbsolutePath.EndsWith(".gif",StringComparison.OrdinalIgnoreCase);
    private static string RepositoryState(InstallerRepository repo)=>UiText.T(!repo.IsAdded?"Repository not added":!repo.IsEnabled?"Repository disabled":repo.Status is "Success" or ""?"Added":repo.Status);
    private static string EntryState(InstallerEntry entry)
    {
        if(entry.Installed?.ScheduledForDeletion==true) return UiText.T("Removal scheduled");
        if(entry.Installed?.State is "LoadError" or "UnloadError" or "DependencyResolutionFailed") return UiText.T("Operation failed")+" ("+entry.Installed.State+")";
        return UiText.T(entry.HasUpdate?"Update available":entry.IsLoaded?"Loaded":entry.Installed?.IsOutdated==true?"Unavailable for this API":entry.IsInstalled?"Disabled":!entry.Repository.IsAdded?"Repository not added":!entry.Repository.IsEnabled?"Repository disabled":!entry.IsApiCompatible?"Unavailable for this API":"Available");
    }
    private static string CompatibilityState(InstallerEntry entry)=>entry.Compatibility is { IsAssessable:true } row?UiText.Assessment(row.Assessment):UiText.T("Not assessed");
}

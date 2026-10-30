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
    private enum InstallerFilterMode { Required, Blocked }
    private readonly Dictionary<string,InstallerFilterMode> installerFilters = new(StringComparer.Ordinal)
        { ["Not available on this API"]=InstallerFilterMode.Blocked };
    private readonly Dictionary<string,InstallerFilterMode> repositoryFilters = new(StringComparer.Ordinal);
    private readonly Dictionary<string,InstallerFilterMode> pinnedAuthorFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,InstallerFilterMode> dalamudCategoryFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,InstallerFilterMode> thirdPartyCategoryFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,InstallerFilterMode> botologyCategoryFilters = new(StringComparer.OrdinalIgnoreCase);
    private string manualRepository = "", pinSearch = "";
    private string? selectedInstaller, pendingRemoval, browseAnchor, highlightedInstaller;
    private int galleryIndex, previousColumns, installerSort;
    private bool pinsExpanded, tableView, restoreBrowse, installerSortDescending;
    private bool resetInstallerContentScroll, resetInstallerBrowseScroll;
    private float browseRemainder;
    private float lastBrowseScroll;
    private float profileSourceHeight;
    private int preserveBrowseFrames;
    private InstallerEntry[] cardLayoutEntries=[];
    private float cardHeight,cardTitleHeight,cardMediaHeight;
    private float[] cardRowOffsets=[0];
    private (float Width,float Scale,nint Font,float FontSize,nint TitleFont,float TitleSize,bool Compact,bool Lcars,string Language,int Columns) cardLayoutKey;
    internal bool DiagnosticCompatibilityOnly { get; set; }

    // Reference proportions follow the current text and container, rather than a design resolution.
    private static MaterialControlMetrics InstallerControls()
    {
        var line=ImGui.GetTextLineHeight();
        var compact=BotologyPresentation.Compact;
        var padding=new Vector2(line*(compact?.4f:.6f),line*(compact?.15f:.3f));
        return new() { Height=line+2*padding.Y,Padding=padding,IconSize=line,Gap=line*.4f,
            Rounding=line*.3f,ItemSpacing=new(line*.55f,line*(compact?.2f:.4f)),CellPadding=padding };
    }
    private static float CardInset(float width)=>MaterialTheme.Current.IsLcars ? Math.Max(18*MaterialTheme.Metrics.Scale,width*(BotologyPresentation.Compact?.04f:.055f)) : width*(BotologyPresentation.Compact?.04f:.055f);
    private static float CardContentGap()=>ImGui.GetTextLineHeight()*(BotologyPresentation.Compact?.18f:.3f);

    private static Vector4 InstallerShade(Vector4 color,float strength)=>new(color.X*strength,color.Y*strength,color.Z*strength,color.W);

    private static MaterialSurface.SurfaceScope InstallerSurface(string id,Vector2 size,MaterialElevation elevation=MaterialElevation.Low,
        bool outline=false,float? padding=null,float? radius=null,ImGuiWindowFlags flags=ImGuiWindowFlags.None,int accent=0)
    {
        var lcars=MaterialTheme.Current.IsLcars;
        var insetPadding=lcars && id!="Card" ? Math.Max(16,padding??16) : padding;
        if(lcars && id is not ("Card" or "InstallerPins") && size.Y>0) size.Y+=2*Math.Max(0,(insetPadding??16)-(padding??16));
        var surface=MaterialSurface.Begin(id,size,elevation,outline,insetPadding,radius,flags);
        if(surface.Visible && !BotologyPresentation.IsMaterial)
        {
            var c=MaterialTheme.Current.Colors;
            var inset=new Vector2(outline?MaterialTheme.Metrics.Scale:0);
            MaterialCanvas.Surface(ImGui.GetWindowPos()+inset,ImGui.GetWindowPos()+ImGui.GetWindowSize()-inset,
                MaterialSkin.BackgroundFill(InstallerShade(c.SurfaceContainerLow,.8f)),MaterialSkin.BackgroundFill(InstallerShade(c.SurfaceContainerLowest,.8f)),Math.Max(0,(radius??8)-1)*MaterialTheme.Metrics.Scale);
            if(lcars) BotologyPresentation.LcarsFrame(id switch { "ProfileAssessment"=>2,"ProfileSource"=>1,"RepositoryCard"=>1,_=>accent });
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
        if(MaterialTheme.Current.IsLcars)
        {
            fill=primary?MaterialLcars.ControlBlue:selected?MaterialLcars.Lilac:MaterialLcars.Background;
            foreground=selected||primary?MaterialLcars.Background:MaterialLcars.Text;
        }
        var border=MaterialTheme.Current.IsLcars ? selected||primary?MaterialLcars.ControlBlue:MaterialLcars.Lilac : textOnly?Vector4.Zero:selected||primary?c.Primary:c.Outline;
        var clicked=MaterialButton.Draw(id,UiText.T(label),new MaterialControlAppearance(fill,foreground,border,MaterialControls.Metrics.Rounding/MaterialTheme.Metrics.Scale),
            size,leading:leading,trailing:trailing);
        MeasureBounds?.Invoke(id,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        return clicked;
    }

    private static void InstallerNextInline(float width)
    {
        if(ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+width<=ImGui.GetWindowPos().X+ImGui.GetWindowContentRegionMax().X)
            ImGui.SameLine();
    }

    private void ResetInstallerResults()
    {
        resetInstallerContentScroll=resetInstallerBrowseScroll=true;
        browseAnchor=highlightedInstaller=null; browseRemainder=0; restoreBrowse=false; preserveBrowseFrames=0;
    }

    private void ToggleInstallerFilter(Dictionary<string,InstallerFilterMode> filters,string key,InstallerFilterMode mode)
    {
        if(filters.TryGetValue(key,out var current) && current==mode) filters.Remove(key);
        else filters[key]=mode;
        ResetInstallerResults();
    }

    private static Vector4 InstallerFilterColor(InstallerFilterMode mode)
        =>mode==InstallerFilterMode.Required?BotologyPresentation.GreenCounter:BotologyPresentation.RedCounter;

    private static string InstallerFilterHelp(InstallerFilterMode? mode)
        =>(mode.HasValue?UiText.T(mode.Value.ToString())+"\n":"")+UiText.T("Left-click: require. Right-click: block. Click again to clear.");

    private bool MatchesInstallerAuthors(string value,bool requireAny)
    {
        var matched=false;
        var lookup=pinnedAuthorFilters.GetAlternateLookup<ReadOnlySpan<char>>();
        var authors=value.AsSpan();
        foreach(var range in authors.SplitAny(",;"))
            if(lookup.TryGetValue(authors[range].Trim(),out var mode))
            {
                if(mode==InstallerFilterMode.Blocked) return false;
                matched=true;
            }
        return !requireAny || matched;
    }

    private void DrawInstallerNavigation()
    {
        (string Label,MaterialIcon Icon)[] pages=[("Discover",MaterialIcon.Search),("Installed",MaterialIcon.Backpack),
            ("Updates",MaterialIcon.Refresh),("Repositories",MaterialIcon.Database),("Compatibility",MaterialIcon.Shield)];
        using var controls=MaterialControls.Push(InstallerControls());
        for(var i=0;i<pages.Length;i++)
        {
            var (page,icon)=pages[i];
            if(i>0) InstallerNextInline(MaterialText.Measure(UiText.T(page)).X+2*MaterialControls.Metrics.Padding.X+MaterialControls.Metrics.IconSize+MaterialControls.Metrics.Gap);
            var selected=installerPage==page || page=="Repositories" && installerPage=="Pinned sources";
            if(InstallerButton(page,"nav-"+page,selected,leading:icon,textOnly:true,linkColour:false))
            {
                installerPage=page; selectedInstaller=null; pendingRemoval=null;
                if(page!="Discover") { installerFilters.Remove("Installed"); installerFilters.Remove("Not installed"); }
                ResetInstallerResults(); installerMedia?.Clear();
            }
            if(selected && !MaterialTheme.Current.IsLcars)
                ImGui.GetWindowDrawList().AddLine(ImGui.GetItemRectMin()+new Vector2(0,ImGui.GetItemRectSize().Y),ImGui.GetItemRectMax(),
                    MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary),3*MaterialTheme.Metrics.Scale);
        }
        ImGui.Separator();
        ImGui.Dummy(new(0,CardContentGap()));
    }
    private void DrawInstaller()
    {
        if(DiagnosticCompatibilityOnly) { DrawContent(plugin.CaptureRows(),plugin.CaptureDtrEntries(),plugin.GetCatalogRefreshInfo()); return; }
        if(DateTime.UtcNow>=nextInstallerSnapshot || !ReferenceEquals(observedOperation,plugin.InstallerOperation))
        {
            installerSnapshot=plugin.CaptureInstaller(); nextInstallerSnapshot=DateTime.UtcNow.AddSeconds(1);
            RefreshCategoryTracks();
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
        var line=ImGui.GetTextLineHeight();
        var metrics=InstallerControls();
        using var layout=new MaterialStyleScope();
        layout.Style(ImGuiStyleVar.ItemSpacing,metrics.ItemSpacing);
        layout.Style(ImGuiStyleVar.FramePadding,metrics.Padding);
        layout.Style(ImGuiStyleVar.CellPadding,metrics.CellPadding);
        layout.Style(ImGuiStyleVar.FrameRounding,metrics.Rounding);
        layout.Style(ImGuiStyleVar.ChildRounding,metrics.Rounding);
        layout.Color(ImGuiCol.ChildBg,Vector4.Zero);
        // Keep the media canvas stable when density changes the host's outer padding.
        // Compact reduces content padding, not responsive grid breakpoints at the same window size.
        var horizontalInset=Math.Max(0,line*.75f-ImGui.GetStyle().WindowPadding.X);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX()+horizontalInset);
        var width=Math.Max(1,ImGui.GetContentRegionAvail().X-horizontalInset);
        var background=ImGui.GetCursorScreenPos();
        if(!BotologyPresentation.IsMaterial)
            MaterialCanvas.Surface(background,background+new Vector2(width,ImGui.GetContentRegionAvail().Y),MaterialSkin.BackgroundFill(InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerLowest,.8f)),
                MaterialSkin.BackgroundFill(InstallerShade(MaterialTheme.Current.Colors.Background,.65f)),0);
        var rail=line*(pinsExpanded?13:MaterialTheme.Current.IsLcars?3.25f:1.875f);
        using(var panel=InstallerSurface("InstallerPins",new(rail/s,ImGui.GetContentRegionAvail().Y/s),MaterialElevation.Lowest,padding:(pinsExpanded?metrics.Padding.X:line*.1f)/s,radius:0))
        DrawPinnedRail();
        ImGui.SameLine();
        ImGui.BeginChild("InstallerContent",new(Math.Max(1,width-rail-ImGui.GetStyle().ItemSpacing.X),-1),false);
        MeasureBounds?.Invoke("installer-content",ImGui.GetWindowPos(),ImGui.GetWindowPos()+ImGui.GetWindowSize());
        DrawInstallerNavigation();
        if(MaterialTheme.Current.IsLcars || MaterialTheme.Current.IsCyberpunk)
        {
            var heading=UiText.T(selectedInstaller is null ? installerPage : "View details");
            MaterialSkin.Heading(MaterialTheme.Current.IsCyberpunk ? heading.ToUpper(UiText.Current.Culture) : heading,1);
        }
        if(plugin.InstallerOperation is { } operation)
            MaterialText.TextWrapped(UiText.T(operation.IsRunning ? "Working..." : operation.Succeeded ? "Completed" : "Operation failed")+": "+
                (operation.MessageArguments is { } arguments?UiText.F(operation.Message,
                    arguments.Select(argument=>argument is string message?UiText.T(message):argument).ToArray()):UiText.T(operation.Message)));
        if(installerSnapshot.Error is { Length:>0 } error) MaterialText.TextWrapped(error);
        if(installerSnapshot.SafeMode) MaterialText.TextWrapped(UiText.T("Safe mode"));
        if(resetInstallerContentScroll) { ImGui.SetScrollY(0); resetInstallerContentScroll=false; }
        if(selectedInstaller is not null)
        {
            var entry=installerSnapshot.Entries.FirstOrDefault(e=>MatchesBrowseAnchor(e,selectedInstaller));
            if(entry is not null) DrawInstallerProfile(entry);
            else { selectedInstaller=null; installerMedia?.Clear(); }
        }
        else if(installerPage=="Repositories") DrawInstallerRepositories();
        else if(installerPage=="Pinned sources") DrawPinsManager();
        else DrawInstallerResults();
        if(installerPage!="Compatibility" && selectedInstaller is null && installerPage is not ("Repositories" or "Pinned sources"))
        {
            using var footer=MaterialControls.Push(InstallerControls());
            MaterialIcons.Draw(MaterialIcon.Info,ImGui.GetCursorScreenPos(),line*.85f,MaterialTheme.Current.Colors.OnSurfaceVariant);
            ImGui.Dummy(new(line,line)); ImGui.SameLine();
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
        var line=ImGui.GetTextLineHeight();
        using var controls=MaterialControls.Push(InstallerControls());
        if(pinsExpanded)
        {
            using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.Text(UiText.T("Pinned sources"));
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(),ImGui.GetWindowWidth()-line*2));
        }
        if(MaterialButton.IconButton("pins-toggle",pinsExpanded?MaterialIcon.Return:MaterialIcon.ArrowRight,size:line*1.5f/s)) { pinsExpanded=!pinsExpanded; restoreBrowse=true; }
        MeasureBounds?.Invoke("pins-toggle",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(pinsExpanded?"Collapse pinned sources":"Expand pinned sources"));
        if(!pinsExpanded)
        {
            if(MaterialButton.IconButton("manage-pins",MaterialIcon.Star,tonal:installerPage=="Pinned sources",size:line*1.5f/s))
            { installerPage="Pinned sources"; selectedInstaller=null; installerMedia?.Clear(); }
            MeasureBounds?.Invoke("nav-Pinned sources",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("Manage pins"));
            return;
        }
        ImGui.Dummy(new(0,MaterialControls.Metrics.Gap));
        MaterialStatus.SectionHeader(UiText.T("Repositories"));
        foreach(var repo in InstallerCatalog.VisiblePinnedRepositories(plugin.Configuration,installerSnapshot))
            DrawPinnedFilter("repo-pin-"+repo.Url,repo.Url,repo.Name,RepositoryState(repo),MaterialIcon.Database,repositoryFilters,repo.Url);
        ImGui.Separator(); MaterialStatus.SectionHeader(UiText.T("Authors"));
        foreach(var author in InstallerCatalog.VisiblePinnedAuthors(plugin.Configuration,installerSnapshot))
            DrawPinnedFilter("author-pin-"+author,author,author,"",MaterialIcon.Person,pinnedAuthorFilters);
        ImGui.Separator();
        if(InstallerButton("Manage pins","manage-pins",leading:MaterialIcon.Settings,textOnly:true)) { installerPage="Pinned sources"; selectedInstaller=null; installerMedia?.Clear(); }
        MeasureBounds?.Invoke("nav-Pinned sources",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
    }

    private void DrawPinnedFilter(string id,string key,string label,string detail,MaterialIcon icon,
        Dictionary<string,InstallerFilterMode> filters,string? url=null)
    {
        var active=filters.TryGetValue(key,out var mode);
        var ink=active?InstallerFilterColor(mode):MaterialTheme.Current.Colors.OnSurfaceVariant;
        var textHeight=MaterialText.Measure(label).Y+(detail.Length>0?MaterialText.Measure(detail).Y:0);
        var metrics=InstallerControls();
        using var controls=MaterialControls.Push(metrics with { Height=Math.Max(metrics.Height,textHeight+2*metrics.Padding.Y) });
        var left=MaterialNavigation.ListItem(id,"",false);
        var right=ImGui.IsItemClicked(ImGuiMouseButton.Right);
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax();
        MeasureBounds?.Invoke(id,min,max);
        if(ImGui.IsItemVisible())
        {
            var draw=ImGui.GetWindowDrawList(); draw.PushClipRect(min,max,true);
            var position=min+new Vector2(metrics.Padding.X+metrics.IconSize+metrics.Gap,(max.Y-min.Y-textHeight)*.5f);
            if(active)
            {
                var edge=ImGui.GetTextLineHeight()*.05f;
                draw.AddRectFilled(min+new Vector2(edge),max-new Vector2(edge),MaterialCanvas.Color(MaterialColor.Alpha(ink,.16f)),metrics.Rounding);
                draw.AddRect(min+new Vector2(edge),max-new Vector2(edge),MaterialCanvas.Color(ink),metrics.Rounding);
                MaterialIcons.Draw(mode==InstallerFilterMode.Required?MaterialIcon.Check:MaterialIcon.Close,
                    new(max.X-metrics.Padding.X-metrics.IconSize,position.Y+(MaterialText.Measure(label).Y-metrics.IconSize)*.5f),metrics.IconSize,ink);
            }
            MaterialIcons.Draw(icon,new(min.X+metrics.Padding.X,(min.Y+max.Y-metrics.IconSize)*.5f),metrics.IconSize,ink);
            var name=FitSourceLabel(label,Math.Max(1,max.X-position.X-metrics.Padding.X-(active?metrics.IconSize+metrics.Gap:0)));
            // Keep Latin source names independent of a shaped translated status.
            MaterialText.AddText(draw,position,MaterialCanvas.Color(ink),name);
            if(url is not null) MeasureBounds?.Invoke("repo-pin-name-"+url,position,position+MaterialText.Measure(name));
            if(detail.Length>0) MaterialText.AddText(draw,position+new Vector2(0,MaterialText.Measure(name).Y),MaterialCanvas.Color(ink),detail);
            draw.PopClipRect();
        }
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(label+(url is not null?"\n"+url:"")+(detail.Length>0?"\n"+detail:"")+"\n"+InstallerFilterHelp(active?mode:null));
        if(left || right)
        {
            ToggleInstallerFilter(filters,key,right?InstallerFilterMode.Blocked:InstallerFilterMode.Required);
            selectedInstaller=null; installerPage="Discover"; installerMedia?.Clear();
        }
    }

    private IReadOnlyList<InstallerEntry> FilterInstallerEntries()
    {
        IEnumerable<InstallerEntry> entries=installerSnapshot.Entries.Where(e=>!e.Manifest.IsHidden || e.IsInstalled);
        if(installerPage=="Installed") entries=entries.Where(e=>e.IsInstalled);
        if(installerPage=="Updates") entries=entries.Where(e=>e.HasUpdate);
        foreach(var (filter,mode) in installerFilters)
            entries=entries.Where(e=>(filter switch {
                "Installed"=>e.IsInstalled,"Not installed"=>!e.IsInstalled,"Repository not added"=>!e.Repository.IsAdded,
                "Not available on this API"=>!e.IsApiCompatible || e.Installed?.IsOutdated==true,_=>false })==(mode==InstallerFilterMode.Required));
        if(repositoryFilters.Count>0)
        {
            var required=repositoryFilters.Values.Contains(InstallerFilterMode.Required);
            entries=entries.Where(e=>(!required
                || repositoryFilters.TryGetValue(e.Repository.Url,out var include) && include==InstallerFilterMode.Required)
                && (!repositoryFilters.TryGetValue(e.Repository.Url,out var exclude) || exclude!=InstallerFilterMode.Blocked));
        }
        if(pinnedAuthorFilters.Count>0)
        {
            var required=pinnedAuthorFilters.Values.Contains(InstallerFilterMode.Required);
            entries=entries.Where(e=>MatchesInstallerAuthors(e.Manifest.Author,required));
        }
        if(dalamudCategoryFilters.Count+thirdPartyCategoryFilters.Count>0)
        {
            var required=dalamudCategoryFilters.Values.Contains(InstallerFilterMode.Required) || thirdPartyCategoryFilters.Values.Contains(InstallerFilterMode.Required);
            entries=entries.Where(entry=>MatchesCategoryTags(entry.ManifestTags??[],
                entry.Repository.Url==InstallerCatalog.OfficialRepository?dalamudCategoryFilters:thirdPartyCategoryFilters,required));
        }
        if(botologyCategoryFilters.Count>0)
        {
            var required=botologyCategoryFilters.Values.Contains(InstallerFilterMode.Required);
            entries=entries.Where(entry=>entry.BotologyCategory is { } category
                ?(!required || botologyCategoryFilters.TryGetValue(category,out var include) && include==InstallerFilterMode.Required)
                    && (!botologyCategoryFilters.TryGetValue(category,out var exclude) || exclude!=InstallerFilterMode.Blocked)
                :!required);
        }
        entries=entries.Where(e=>(installerSearch.Length==0 || string.Join(' ',e.Manifest.Name,e.Manifest.InternalName,e.Manifest.Author,e.Manifest.Description,e.Repository.Name,e.Repository.Url,string.Join(' ',e.ManifestTags??[])).Contains(installerSearch,StringComparison.OrdinalIgnoreCase))
            && (installerAuthor.Length==0 || e.Manifest.Author.Contains(installerAuthor,StringComparison.OrdinalIgnoreCase))
            && (installerCategory.Length==0 || (e.BotologyCategory??string.Join(' ',e.ManifestTags??[])).Contains(installerCategory,StringComparison.OrdinalIgnoreCase)));
        // Merge supplies name/identity order; filtering retains it without preparing unused secondary sort keys.
        if(installerSort==0 && !installerSortDescending) return entries.ToArray();
        var sort=installerSort;var descending=installerSortDescending;
        string Key(InstallerEntry e)=>sort switch { 1=>e.Repository.Name, 2=>EntryState(e), 6=>e.Manifest.Author, _=>e.Manifest.Name };
        Version? VersionKey(InstallerEntry entry)=>entry.Installed?.Runtime.Version??(Version.TryParse(entry.Manifest.Version,out var version)?version:null);
        var result=entries.ToArray();
        var keys=result.Select(entry=>(Key:Key(entry),Version:sort==3?VersionKey(entry):null,
            Source:entry.Manifest.RepositoryUrl,Name:entry.Manifest.InternalName.Trim(),Ascii:System.Text.Ascii.IsValid(entry.Manifest.InternalName))).ToArray();
        int Compare(int leftIndex,int rightIndex)
        {
            ref readonly var left=ref keys[leftIndex];ref readonly var right=ref keys[rightIndex];
            var comparison=sort==3?Comparer<Version?>.Default.Compare(left.Version,right.Version):StringComparer.OrdinalIgnoreCase.Compare(left.Key,right.Key);
            if(comparison!=0) return descending?-comparison:comparison;
            var source=StringComparer.Ordinal.Compare(left.Source,right.Source);
            if(source!=0) return source;
            return left.Ascii && right.Ascii?StringComparer.OrdinalIgnoreCase.Compare(left.Name,right.Name)
                :StringComparer.Ordinal.Compare(left.Name.ToUpperInvariant(),right.Name.ToUpperInvariant());
        }
        // Filtering preserves the merged catalog order. Sort only when its current order differs.
        for(var index=1;index<result.Length;index++)
            if(Compare(index-1,index)>0)
            {
                // Move small indices, retaining the prepared keys in place during comparisons and swaps.
                var order=Enumerable.Range(0,result.Length).ToArray();Array.Sort(order,Compare);
                return Array.ConvertAll(order,item=>result[item]);
            }
        return result;
    }

    private static int CompareInstallerIdentity(InstallerEntry left,InstallerEntry right)
    {
        // Merge already canonicalizes these URLs. Compare the identity parts without reparsing/allocating the full key.
        var source=StringComparer.Ordinal.Compare(left.Manifest.RepositoryUrl,right.Manifest.RepositoryUrl);
        if(source!=0) return source;
        var leftName=left.Manifest.InternalName;var rightName=right.Manifest.InternalName;
        return System.Text.Ascii.IsValid(leftName) && System.Text.Ascii.IsValid(rightName)
            ?leftName.AsSpan().Trim().CompareTo(rightName.AsSpan().Trim(),StringComparison.OrdinalIgnoreCase)
            :StringComparer.Ordinal.Compare(leftName.Trim().ToUpperInvariant(),rightName.Trim().ToUpperInvariant());
    }

    private void DrawInstallerResults()
    {
        var s=MaterialTheme.Metrics.Scale;
        var width=ImGui.GetContentRegionAvail().X;
        using var controls=MaterialControls.Push(InstallerControls());
        var metrics=MaterialControls.Metrics;
        if(installerPage=="Updates")
        {
            ImGui.BeginDisabled(plugin.InstallerOperation?.IsRunning==true || !installerSnapshot.Ready || installerSnapshot.SafeMode
                || !installerSnapshot.Entries.Any(entry=>entry.CanUpdate));
            if(InstallerButton("Update all","installer-update-all",primary:true,leading:MaterialIcon.Refresh))
                plugin.RunInstallerAction(InstallerAction.UpdateAll,"");
            if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                MaterialText.SetTooltip(UiText.T("Updates every eligible plugin, including plugins hidden by the current filters."));
            ImGui.EndDisabled();
        }
        var switchWidth=2*(metrics.IconSize+2*metrics.Padding.X+metrics.Gap);
        if(MaterialTextField.SearchField("##InstallerSearch",ref installerSearch,
            BotologyPresentation.Field with { CornerRadius=metrics.Rounding/s },UiText.T("Search plugins, authors or repositories"),(width-switchWidth-ImGui.GetStyle().ItemSpacing.X)/s,maxLength:512)) ResetInstallerResults();
        MeasureBounds?.Invoke("installer-search",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        ImGui.SameLine();
        ReadOnlySpan<MaterialSegment<bool>> views=[new(false,"",MaterialIcon.Grid,Key:"cards"),new(true,"",MaterialIcon.List,Key:"table")];
        if(MaterialSegmented.Draw("InstallerView",views,ref tableView,switchWidth/s,disabled:false,showSelectionCheck:false,cornerRadius:metrics.Rounding/s)) restoreBrowse=true;
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax(); var middle=(min.X+max.X)*.5f;
        MeasureBounds?.Invoke("view-cards",min,new(middle,max.Y));
        MeasureBounds?.Invoke("view-table",new(middle,min.Y),max);
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(tableView?"Table":"Cards"));
        foreach(var filter in new[]{"All plugins","Installed","Not installed","Repository not added","Not available on this API"})
        {
            if(installerPage!="Discover" && filter is "Installed" or "Not installed") continue;
            var label=UiText.T(filter);
            if(filter!="All plugins") InstallerNextInline(MaterialText.Measure(label).X+2*metrics.Padding.X);
            var active=installerFilters.TryGetValue(filter,out var mode);
            var selected=filter=="All plugins"?installerFilters.Count==0:active;
            var c=MaterialTheme.Current.Colors;
            var color=active?InstallerFilterColor(mode):c.Primary;
            var appearance=new MaterialControlAppearance(selected?MaterialColor.Alpha(color,.18f):Vector4.Zero,
                selected?color:c.OnSurface,selected?color:c.Outline,ImGui.GetTextLineHeight()/s);
            var left=MaterialChip.FilterChip("filter-"+filter,label,ref selected,appearance,showSelectionCheck:false);
            var right=ImGui.IsItemClicked(ImGuiMouseButton.Right);
            if(left || right)
            {
                if(filter=="All plugins") { installerFilters.Clear(); ResetInstallerResults(); }
                else ToggleInstallerFilter(installerFilters,filter,right?InstallerFilterMode.Blocked:InstallerFilterMode.Required);
            }
            MeasureBounds?.Invoke("filter-"+filter,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            if(filter!="All plugins" && ImGui.IsItemHovered()) MaterialText.SetTooltip(InstallerFilterHelp(active?mode:null));
        }
        var filterWidth=Math.Min(ImGui.GetTextLineHeight()*10,width*.3f);
        InstallerNextInline(filterWidth);
        if(MaterialTextField.SearchField("##InstallerCategory",ref installerCategory,BotologyPresentation.Field,UiText.T("Category"),filterWidth/s,maxLength:128)) ResetInstallerResults();
        InstallerNextInline(filterWidth);
        if(MaterialTextField.SearchField("##InstallerAuthor",ref installerAuthor,BotologyPresentation.Field,UiText.T("Author"),filterWidth/s,maxLength:128)) ResetInstallerResults();
        InstallerNextInline(metrics.Height);
        ImGui.BeginDisabled(plugin.DiscoveryRunning || plugin.InstallerOperation?.IsRunning==true);
        if(MaterialButton.IconButton("installer-refresh",MaterialIcon.Refresh,size:metrics.Height/s)) { plugin.RefreshInstaller(); nextInstallerSnapshot=DateTime.MinValue; }
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(plugin.DiscoveryRunning?"Refreshing...":"Refresh"));
        ImGui.EndDisabled();
        DrawInstallerCategories();
        var pinFilters=repositoryFilters.Count>0 || pinnedAuthorFilters.Count>0;
        var filtered=pinFilters || installerFilters.Count>0 || dalamudCategoryFilters.Count>0 || thirdPartyCategoryFilters.Count>0 || botologyCategoryFilters.Count>0 || installerSearch.Length>0 || installerCategory.Length>0 || installerAuthor.Length>0;
        if(pinFilters)
        {
            foreach(var group in repositoryFilters.Concat(pinnedAuthorFilters).GroupBy(pair=>pair.Value))
            {
                var names=group.Select(pair=>repositoryFilters.ContainsKey(pair.Key)?installerSnapshot.Repositories.FirstOrDefault(r=>r.Url==pair.Key)?.Name??pair.Key:pair.Key);
                ImGui.PushTextWrapPos(0);
                MaterialText.TextColored(InstallerFilterColor(group.Key),UiText.T(group.Key.ToString())+": "+string.Join(", ",names));
                ImGui.PopTextWrapPos();
                if(ImGui.IsItemHovered()) MaterialText.SetTooltip(string.Join("\n",group.Select(pair=>pair.Key)));
                MeasureBounds?.Invoke("active-pin-filter",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            }
        }
        var entries=FilterInstallerEntries();
        ImGui.AlignTextToFramePadding();
        MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant,UiText.F("{0} plugins",entries.Count));
        MeasureBounds?.Invoke("installer-result-count",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        InstallerNextInline(MaterialText.Measure(UiText.T("Name")).X+2*metrics.Padding.X+metrics.IconSize+metrics.Gap);
        if(InstallerButton("Name","InstallerSort",textOnly:true,trailing:MaterialIcon.ChevronDown)) { installerSort=0; installerSortDescending=!installerSortDescending; }
        if(filtered)
        {
            InstallerNextInline(MaterialText.Measure(UiText.T("Clear filter")).X+2*metrics.Padding.X);
            if(InstallerButton("Clear filter",pinFilters?"clear-pin-filter":"clear-installer-filters",textOnly:true))
            {
                repositoryFilters.Clear(); pinnedAuthorFilters.Clear(); installerFilters.Clear(); ClearCategoryFilters(); installerSearch=installerCategory=installerAuthor="";
                ResetInstallerResults();
                // The reset must seed its anchor from the new results in this same frame.
                entries=FilterInstallerEntries();
            }
        }
        if(tableView) DrawInstallerTable(entries); else DrawInstallerCards(entries);
    }

    private void DrawInstallerCards(IReadOnlyList<InstallerEntry> entries)
    {
        var s=MaterialTheme.Metrics.Scale;
        var lineHeight=Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(UiText.T("Repository not added")).Y);
        var gap=lineHeight*.75f;
        var width=ImGui.GetContentRegionAvail().X-ImGui.GetStyle().ScrollbarSize;
        var minimum=Math.Max(lineHeight*10,entries.Count==0?0:entries.Select(EntryBadgeLabel).Distinct(StringComparer.Ordinal).Max(label=>MaterialText.Measure("    "+label).X)+2*lineHeight);
        var columns=Math.Max(1,(int)((width+gap)/(minimum+gap)));
        // Native child allocation rounds down; measure content against that same available width.
        var cardWidth=Math.Max(1,MathF.Floor((width-gap*(columns-1))/columns));
        var innerGap=CardContentGap();
        var inset=CardInset(cardWidth);
        var actionHeight=new[]{"Manage","View details","Install"}.Max(label=>MaterialText.Measure(UiText.T(label)).Y)+2*InstallerControls().Padding.Y;
        var badgeHeight=lineHeight+innerGap;
        var rowGap=lineHeight*(BotologyPresentation.Compact?.5f:.75f);
        var layoutChanged=PrepareInstallerCardLayout(entries,columns,cardWidth,lineHeight,badgeHeight,actionHeight,rowGap);
        var footerHeight=Math.Max(lineHeight,MaterialText.Measure(UiText.T("Pinning a source does not add its repository."),false,Math.Max(1,width-2*lineHeight)).Y)+ImGui.GetStyle().ItemSpacing.Y;
        ImGui.BeginChild("InstallerBrowse",new(-1,Math.Max(lineHeight*3,ImGui.GetContentRegionAvail().Y-footerHeight-ImGui.GetStyle().ItemSpacing.Y)),false);
        MeasureBounds?.Invoke("installer-browse",ImGui.GetWindowPos(),ImGui.GetWindowSize());
        var resetting=resetInstallerBrowseScroll;
        if(resetting)
        {
            ImGui.SetScrollY(0);resetInstallerBrowseScroll=false;restoreBrowse=false;
            browseAnchor=entries.Count>0?entries[0].Identity:null;browseRemainder=0;preserveBrowseFrames=3;
        }
        else if(restoreBrowse || columns!=previousColumns || layoutChanged && browseAnchor is not null)
        {
            var anchor=Enumerable.Range(0,entries.Count).FirstOrDefault(i=>MatchesBrowseAnchor(entries[i],browseAnchor));
            var row=anchor/columns;
            var extent=row+1<cardRowOffsets.Length?cardRowOffsets[row+1]-cardRowOffsets[row]:0;
            ImGui.SetScrollY(cardRowOffsets[row]+browseRemainder*extent); restoreBrowse=false;
            preserveBrowseFrames=3;
        }
        previousColumns=columns;
        // Native scroll targets apply next frame; do not capture the old position as a new filter's anchor.
        var scroll=resetting?0:ImGui.GetScrollY();
        var topRow=InstallerCardRowAt(scroll);
        var top=Math.Clamp(topRow*columns,0,Math.Max(0,entries.Count-1));
        if(preserveBrowseFrames>0) preserveBrowseFrames--;
        else if(browseAnchor is null || Math.Abs(scroll-lastBrowseScroll)>.1f || !entries.Any(e=>MatchesBrowseAnchor(e,browseAnchor)))
        {
            browseAnchor=entries.Count>0?entries[top].Identity:null;
            var extent=topRow+1<cardRowOffsets.Length?cardRowOffsets[topRow+1]-cardRowOffsets[topRow]:0;
            browseRemainder=extent>0?Math.Clamp((scroll-cardRowOffsets[topRow])/extent,0,1):0;
        }
        lastBrowseScroll=scroll;
        var origin=ImGui.GetCursorPos();
        var first=Math.Max(0,topRow-1)*columns;
        var last=Math.Min(entries.Count,(InstallerCardRowAt(scroll+ImGui.GetWindowHeight())+2)*columns);
        var pinnedSources=InstallerCatalog.VisiblePinnedRepositories(plugin.Configuration,installerSnapshot).Select(repo=>repo.Url).ToHashSet(StringComparer.Ordinal);
        for(var i=first;i<last;i++)
        {
            var height=cardHeight;
            var previewHeight=cardMediaHeight;
            ImGui.SetCursorPos(origin+new Vector2(i%columns*(cardWidth+gap),cardRowOffsets[i/columns]));
            if(!ImGui.IsRectVisible(new(cardWidth,height))) continue;
            var entry=entries[i];
            ImGui.PushID(entry.Identity);
            using(var card=InstallerSurface("Card",new(cardWidth/s,height/s),MaterialElevation.Low,outline:true,padding:0,radius:inset*.65f/s,
                flags:ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse,accent:i%3))
            {
            MeasureBounds?.Invoke("card-"+entry.Identity,ImGui.GetWindowPos(),ImGui.GetWindowPos()+ImGui.GetWindowSize());
            using var cardSpacing=new MaterialStyleScope();
            cardSpacing.Style(ImGuiStyleVar.ItemSpacing,new Vector2(ImGui.GetStyle().ItemSpacing.X,innerGap));
            DrawPreview(entry,previewHeight,false);
            var bodyX=inset;
            var titleX=bodyX;
            ImGui.SetCursorPos(new(titleX,previewHeight+inset));
            using(UiText.Font(UiFontRole.BodyStrong))
            {
                var titleOrigin=ImGui.GetCursorScreenPos();
                var titleSize=new Vector2(cardWidth-titleX-bodyX,cardTitleHeight);
                using(var titleStyle=new MaterialStyleScope())
                {
                    titleStyle.Color(ImGuiCol.Text,Vector4.Zero);
                    if(ImGui.Selectable("plugin-title",false,ImGuiSelectableFlags.None,titleSize)) OpenInstallerProfile(entry);
                }
                var titleDraw=ImGui.GetWindowDrawList();
                titleDraw.PushClipRect(titleOrigin,titleOrigin+titleSize,true);
                MaterialText.AddText(titleDraw,titleOrigin,ImGui.GetColorU32(ImGuiCol.Text),entry.Manifest.Name,titleSize.X);
                titleDraw.PopClipRect();
                MeasureBounds?.Invoke("card-title-ink-"+entry.Identity,titleOrigin,titleOrigin+titleSize);
            }
            MeasureBounds?.Invoke("card-title-"+entry.Identity,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(entry.Manifest.Name+"\n"+entry.Manifest.Description+"\n"+CompatibilityState(entry));
            var sourceY=previewHeight+inset+cardTitleHeight+innerGap;
            ImGui.SetCursorPos(new(bodyX,sourceY));
            MaterialIcons.Draw(MaterialIcon.Database,ImGui.GetCursorScreenPos()+new Vector2(0,lineHeight*.1f),lineHeight*.8f,MaterialTheme.Current.Colors.OnSurfaceVariant);
            ImGui.Dummy(new(lineHeight,lineHeight)); ImGui.SameLine(0,innerGap);
            var sourceWidth=Math.Max(1,cardWidth-2*inset-2*lineHeight-innerGap);
            var sourcePos=ImGui.GetCursorScreenPos();
            var draw=ImGui.GetWindowDrawList(); draw.PushClipRect(sourcePos,sourcePos+new Vector2(sourceWidth,lineHeight),true);
            MaterialText.AddText(draw,sourcePos,MaterialCanvas.Color(MaterialTheme.Current.Colors.OnSurfaceVariant),FitSourceLabel(entry.Repository.Name,sourceWidth)); draw.PopClipRect();
            ImGui.Dummy(new(sourceWidth,lineHeight));
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(entry.Repository.Name+"\n"+entry.Repository.Url);
            ImGui.SameLine(0,0);
            var pinned=pinnedSources.Contains(entry.Repository.Url);
            using(var pinControls=MaterialControls.Push(InstallerControls() with { Height=lineHeight,IconSize=lineHeight*.75f,Padding=Vector2.Zero }))
                if(MaterialButton.IconButton("card-pin",MaterialIcon.Star,tonal:pinned,size:lineHeight/s)) plugin.ToggleRepositoryPin(entry.Repository.Url);
            MeasureBounds?.Invoke("card-pin-"+entry.Identity,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(entry.Repository.Name+"\n"+entry.Repository.Url+"\n"+UiText.T(pinned?"Unpin repository":"Pin repository"));
            var badgeY=sourceY+lineHeight+innerGap;
            ImGui.SetCursorPos(new(bodyX,badgeY));
            DrawEntryBadge(entry,cardWidth-2*inset,badgeHeight);
            MeasureBounds?.Invoke("card-state-"+entry.Identity,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            ImGui.SetCursorPos(new(bodyX,badgeY+badgeHeight+innerGap));
            using(var compatibilityStyle=new MaterialStyleScope())
            {
                var color=entry.Compatibility is { IsAssessable:true,Ignored:false } assessment && assessment.Assessment.Severity!=AssessmentSeverity.Green
                    ?assessment.Assessment.Severity==AssessmentSeverity.Red?BotologyPresentation.RedCounter:BotologyPresentation.YellowCounter
                    :MaterialTheme.Current.Colors.OnSurfaceVariant;
                compatibilityStyle.Color(ImGuiCol.Text,color);
                MaterialText.Text(CardCompatibilityLabel(entry),Math.Max(1,cardWidth-2*bodyX));
            }
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(CompatibilityState(entry));
            MeasureBounds?.Invoke("card-compatibility-"+entry.Identity,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            MeasureBounds?.Invoke("card-text-end-"+entry.Identity,ImGui.GetCursorScreenPos(),ImGui.GetCursorScreenPos());
            ImGui.SetCursorPos(new(bodyX,height-actionHeight-inset));
            using(var actions=MaterialControls.Push(InstallerControls() with { Height=actionHeight }))
            {
                ImGui.BeginDisabled(entry.CanInstall && plugin.InstallerOperation?.IsRunning==true);
                if(InstallerButton(entry.CanInstall?"Install":entry.IsInstalled?"Manage":"View details","details-"+i,
                    size:new((cardWidth-2*inset)/s,actionHeight/s),trailing:MaterialIcon.ArrowRight))
                { if(entry.CanInstall) plugin.RunInstallerAction(InstallerAction.Install,entry.Repository.Url,entry.Manifest.InternalName); else OpenInstallerProfile(entry); }
                ImGui.EndDisabled();
            }
            }
            ImGui.PopID();
        }
        ImGui.SetCursorPos(origin+new Vector2(0,cardRowOffsets[^1]));
        if(entries.Count==0) MaterialText.TextWrapped(UiText.T("No plugins match the current filters."));
        ImGui.Dummy(new(1,1)); ImGui.EndChild();
    }

    private unsafe bool PrepareInstallerCardLayout(IReadOnlyList<InstallerEntry> entries,int columns,float width,float lineHeight,float badgeHeight,float actionHeight,float rowGap)
    {
        var s=MaterialTheme.Metrics.Scale;
        var compact=BotologyPresentation.Compact;
        var font=(nint)ImGui.GetFont().Handle;var fontSize=ImGui.GetFontSize();
        nint titleFont;float titleSize;
        using(UiText.Font(UiFontRole.BodyStrong)) { titleFont=(nint)ImGui.GetFont().Handle;titleSize=ImGui.GetFontSize(); }
        var key=(width,s,font,fontSize,titleFont,titleSize,compact,MaterialTheme.Current.IsLcars,UiText.Current.Language,columns);
        var changed=key!=cardLayoutKey || entries.Count!=cardLayoutEntries.Length;
        for(var i=0;!changed && i<entries.Count;i++)
        {
            var previous=cardLayoutEntries[i];var current=entries[i];
            if(ReferenceEquals(previous,current)) continue;
            changed=!SameCardIdentity(previous,current) || previous.Manifest.Name!=current.Manifest.Name;
            // Host captures replace records every second; release old runtime references without remeasuring unchanged text.
            if(!changed) cardLayoutEntries[i]=current;
        }
        if(!changed) return false;
        cardLayoutKey=key;
        cardLayoutEntries=entries.ToArray();
        var inset=CardInset(width);var gap=CardContentGap();
        var bodyWidth=Math.Max(1,width-2*inset);
        using(UiText.Font(UiFontRole.BodyStrong))
        {
            var titleLineHeight=ImGui.GetTextLineHeight();
            foreach(var entry in entries)
                if(!System.Text.Ascii.IsValid(entry.Manifest.Name) && MaterialText.RequiresShaping(entry.Manifest.Name))
                    titleLineHeight=Math.Max(titleLineHeight,MaterialText.Measure(entry.Manifest.Name.Replace('\r',' ').Replace('\n',' ')).Y);
            // Reserve the same bounded title area even for short names and missing previews.
            cardTitleHeight=titleLineHeight*2;
        }
        var compatibilityHeight=new[]{"Not assessed","Ignored","Green","Yellow","Red"}
            .Max(state=>MaterialText.Measure(UiText.T("Compatibility")+": "+UiText.T(state),false,bodyWidth).Y);
        cardMediaHeight=width*.45f;
        cardHeight=cardMediaHeight+2*inset+cardTitleHeight+lineHeight+badgeHeight+compatibilityHeight+actionHeight+4*gap;
        Array.Resize(ref cardRowOffsets,(entries.Count+columns-1)/columns+1);
        for(var row=0;row<cardRowOffsets.Length;row++) cardRowOffsets[row]=row*(cardHeight+rowGap);
        return true;
    }

    private static bool SameCardIdentity(InstallerEntry left,InstallerEntry right)=>
        left.Manifest.RepositoryUrl==right.Manifest.RepositoryUrl && left.Manifest.InternalName==right.Manifest.InternalName;

    private int InstallerCardRowAt(float y)
    {
        var row=Array.BinarySearch(cardRowOffsets,y);
        return Math.Clamp(row>=0?row:~row-1,0,Math.Max(0,cardRowOffsets.Length-2));
    }

    private static string CardCompatibilityKey(InstallerEntry entry)=>
        entry.Compatibility is { IsAssessable:true } row?row.Ignored?"Ignored":row.Assessment.Severity.ToString():"Not assessed";
    private static string CardCompatibilityLabel(InstallerEntry entry)=>UiText.T("Compatibility")+": "+UiText.T(CardCompatibilityKey(entry));

    private static bool MatchesBrowseAnchor(InstallerEntry entry,string? anchor)=>anchor is not null
        && anchor.AsSpan(anchor.LastIndexOf('\n')+1).Equals(entry.Manifest.InternalName.AsSpan().Trim(),StringComparison.OrdinalIgnoreCase)
        && entry.Identity==anchor;

    private static string FitSourceLabel(string value,float width)
    {
        string Fit(string text,float available)
        {
            if(MaterialText.Measure(text).X<=available) return text;
            var boundaries=System.Globalization.StringInfo.ParseCombiningCharacters(text);
            for(var count=boundaries.Length-1;count>=0;count--)
            {
                var left=(count+1)/2;var right=count/2;
                var candidate=text[..(left==0?0:boundaries[left])]+"..."+(right==0?"":text[boundaries[boundaries.Length-right]..]);
                if(MaterialText.Measure(candidate).X<=available) return candidate;
            }
            return "";
        }
        var separator=value.IndexOf(" · ",StringComparison.Ordinal);
        if(separator>=0 && MaterialText.Measure(value).X>width)
        {
            var detail=" · "+value[(separator+3)..];
            if(MaterialText.Measure(detail).X<width*.7f) return Fit(value[..separator],width-MaterialText.Measure(detail).X)+detail;
        }
        return Fit(value,width);
    }

    private static string EntryBadgeLabel(InstallerEntry entry)=>entry.Installed?.State is "LoadError" or "UnloadError" or "DependencyResolutionFailed"
        ?UiText.T("Operation failed"):EntryState(entry);

    private void DrawEntryBadge(InstallerEntry entry,float width=0,float height=0)
    {
        var s=MaterialTheme.Metrics.Scale;
        var metrics=InstallerControls();
        var c=MaterialTheme.Current.Colors;
        var color=entry.Installed?.State is "LoadError" or "UnloadError" or "DependencyResolutionFailed"?BotologyPresentation.RedCounter
            :entry.HasUpdate?BotologyPresentation.YellowCounter:entry.IsLoaded?BotologyPresentation.Rgb(0xB6A5FF)
            :entry.IsInstalled?c.OnSurfaceVariant:!entry.CanInstall?BotologyPresentation.YellowCounter:c.Primary;
        var label="    "+EntryBadgeLabel(entry);
        var text=MaterialText.Measure(label);
        var size=new Vector2(Math.Min(width>0?width:float.MaxValue,text.X+2*metrics.Padding.X),height>0?height:Math.Max(metrics.Height,text.Y+2*metrics.Padding.Y));
        var p=ImGui.GetCursorScreenPos();
        MaterialStatus.Badge(label,new MaterialControlAppearance(MaterialColor.Alpha(color,.12f),color,MaterialColor.Alpha(color,.4f),size.Y*.5f/s),size/s);
        var iconSide=Math.Min(metrics.IconSize*.8f,size.Y);
        MaterialIcons.Draw(entry.Installed?.State is "LoadError" or "UnloadError" or "DependencyResolutionFailed"?MaterialIcon.Warning:entry.IsLoaded?MaterialIcon.Play:entry.IsInstalled?MaterialIcon.Check:!entry.Repository.IsAdded?MaterialIcon.Warning:MaterialIcon.Download,
            p+new Vector2(metrics.Padding.X,(size.Y-iconSide)*.5f),iconSide,color);
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
        tableColors.Color(ImGuiCol.TableHeaderBg,MaterialSkin.BackgroundFill(InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerHigh,.7f)));
        tableColors.Color(ImGuiCol.TableRowBg,MaterialSkin.BackgroundFill(InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerLowest,.7f)));
        tableColors.Color(ImGuiCol.TableRowBgAlt,MaterialSkin.BackgroundFill(InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerLow,.8f)));
        uint[] ids=[1,2,7,3,4,5,6];
        for(var i=0;i<labels.Length;i++) widths[i]=Math.Max(widths[i]*s,MaterialText.Measure(UiText.T(labels[i])).X+32*s);
        var badgeSizes=entries.Select(EntryBadgeLabel).Distinct(StringComparer.Ordinal).ToDictionary(label=>label,label=>MaterialText.Measure("    "+label),StringComparer.Ordinal);
        var badgeMetrics=InstallerControls();
        if(badgeSizes.Count>0) widths[3]=Math.Max(widths[3],badgeSizes.Values.Max(size=>size.X)+2*badgeMetrics.Padding.X);
        var tableHeight=Math.Max(80*s,ImGui.GetContentRegionAvail().Y-140*s);
        if(ImGui.BeginTable("InstallerTable",7,flags,new(-1,tableHeight),Math.Max(ImGui.GetContentRegionAvail().X,widths.Sum()+14*ImGui.GetStyle().CellPadding.X)))
        {
            if(resetInstallerBrowseScroll) { ImGui.SetScrollY(0); resetInstallerBrowseScroll=false; }
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
            // Reserve every row's real wrapped height, but only submit controls in the viewport.
            // Measurements are deduplicated within this frame, so resizing or changing fonts takes effect immediately.
            var textHeights=new Dictionary<(string Text,int Column),float>();
            var columnWidths=new float[7];
            unsafe
            {
                var table=ImGuiP.GetCurrentTable();
                for(var column=0;column<columnWidths.Length;column++)
                    columnWidths[column]=Math.Max(1,table.Columns.Data[column].WorkMaxX-table.Columns.Data[column].WorkMinX);
            }
            float TextHeight(string value,int column)
            {
                if(!textHeights.TryGetValue((value,column),out var height))
                    textHeights[(value,column)]=height=MaterialText.Measure(value,false,columnWidths[column]).Y;
                return height;
            }
            var metrics=MaterialControls.Metrics;
            var actionHeights=new[]{"Install","Manage","View details"}.ToDictionary(label=>label,label=>
                MaterialText.RequiresShaping(UiText.T(label))?Math.Max(metrics.Height,MaterialText.Measure(UiText.T(label)).Y+2*metrics.Padding.Y):metrics.Height);
            var cellPadding=ImGui.GetStyle().CellPadding.Y;
            var rowHeights=new float[entries.Count];
            for(var index=0;index<entries.Count;index++)
            {
                var entry=entries[index];
                var version=entry.IsInstalled?entry.Installed!.Runtime.Version+(entry.HasUpdate?" -> "+entry.Manifest.Version:""):entry.Manifest.Version;
                var compatibility=CompatibilityState(entry);
                var contentHeight=Math.Max(28*s,actionHeights[entry.CanInstall?"Install":entry.IsInstalled?"Manage":"View details"]);
                contentHeight=Math.Max(contentHeight,TextHeight(entry.Repository.Name,1));
                contentHeight=Math.Max(contentHeight,TextHeight(entry.Manifest.Author,2));
                contentHeight=Math.Max(contentHeight,TextHeight(version,4));
                contentHeight=Math.Max(contentHeight,TextHeight(compatibility,5));
                // Badge text is a single line; the plugin name sits beside its icon.
                contentHeight=Math.Max(contentHeight,Math.Max(badgeMetrics.Height,badgeSizes[EntryBadgeLabel(entry)].Y+2*badgeMetrics.Padding.Y));
                if(!System.Text.Ascii.IsValid(entry.Manifest.Name) && MaterialText.RequiresShaping(entry.Manifest.Name))
                    contentHeight=Math.Max(contentHeight,MaterialText.Measure(entry.Manifest.Name).Y);
                rowHeights[index]=Math.Max((BotologyPresentation.Compact?42:56)*s,contentHeight+2*cellPadding);
            }
            var uniform=entries.Count>0 && rowHeights.All(height=>height==rowHeights[0]);
            if(uniform)
            {
                using var clipper=new MaterialClipper(entries.Count,rowHeights[0]);
                while(clipper.Step()) DrawRange(clipper.Start,clipper.End);
            }
            else DrawRange(0,entries.Count);
            void DrawRange(int start,int end)
            {
                for(var index=start;index<end;index++)
                {
                var entry=entries[index];
                var version=entry.IsInstalled?entry.Installed!.Runtime.Version+(entry.HasUpdate?" -> "+entry.Manifest.Version:""):entry.Manifest.Version;
                var compatibility=CompatibilityState(entry);
                ImGui.TableNextRow(ImGuiTableRowFlags.None,rowHeights[index]);
                unsafe
                {
                    var table=ImGuiP.GetCurrentTable();
                    // Variable rows keep native row advancement and adjacent navigation targets.
                    var clip=ImGuiP.GetCurrentWindow().InnerClipRect;
                    if(!uniform && (table.RowPosY2<clip.Min.Y-rowHeights[index] || table.RowPosY1>clip.Max.Y+rowHeights[index])) continue;
                    MeasureBounds?.Invoke("installer-table-reserved-"+entry.Identity,new(table.OuterRect.Min.X,table.RowPosY1),new(table.OuterRect.Max.X,table.RowPosY2));
                }
                ImGui.PushID(entry.Identity);
                if(ImGui.TableSetColumnIndex(0))
                {
                var iconSize=new Vector2(28*s);
                if(ImGui.IsRectVisible(iconSize)) DrawInstallerIcon(entry,ImGui.GetCursorScreenPos(),iconSize.X);
                ImGui.Dummy(iconSize); ImGui.SameLine();
                if(UiGui.Selectable("details",highlightedInstaller==entry.Identity,entry.Manifest.Name)) highlightedInstaller=entry.Identity;
                if(ImGui.IsItemHovered()) MaterialText.SetTooltip(entry.Manifest.Description);
                }
                if(ImGui.TableSetColumnIndex(1)) MaterialText.TextWrapped(entry.Repository.Name);
                if(ImGui.TableSetColumnIndex(2)) MaterialText.TextWrapped(entry.Manifest.Author);
                if(ImGui.TableSetColumnIndex(3)) DrawEntryBadge(entry);
                if(ImGui.TableSetColumnIndex(4)) MaterialText.TextWrapped(version);
                if(ImGui.TableSetColumnIndex(5)) MaterialText.TextWrapped(compatibility);
                if(ImGui.TableSetColumnIndex(6))
                {
                ImGui.BeginDisabled(entry.CanInstall && plugin.InstallerOperation?.IsRunning==true);
                if(InstallerButton(entry.CanInstall?"Install":entry.IsInstalled?"Manage":"View details","table-details"))
                { if(entry.CanInstall) plugin.RunInstallerAction(InstallerAction.Install,entry.Repository.Url,entry.Manifest.InternalName); else OpenInstallerProfile(entry); }
                ImGui.EndDisabled();
                }
                if(MeasureBounds is not null)
                {
                    ImGui.TableSetColumnIndex(0); // Finish the last cell before observing the actual row extent.
                    unsafe
                    {
                        var table=ImGuiP.GetCurrentTable();
                        MeasureBounds("installer-table-row-"+entry.Identity,new(table.OuterRect.Min.X,table.RowPosY1),new(table.OuterRect.Max.X,table.RowPosY2));
                    }
                }
                ImGui.PopID();
                }
            }
            ImGui.EndTable();
        }
        var selected=(highlightedInstaller is null?null:entries.FirstOrDefault(e=>MatchesBrowseAnchor(e,highlightedInstaller)))??entries.FirstOrDefault();
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
        ImGui.PushTextWrapPos(0); MaterialText.TextColored(c.OnSurfaceVariant,UiText.T("Repository")+": "+entry.Repository.Name); ImGui.PopTextWrapPos();
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
                    using(var tab=UiGui.TabItem(UiText.T("Overview")+"##Overview"))
                        if(tab.Visible) MaterialText.TextWrapped(entry.Manifest.Description);
                    using(var tab=UiGui.TabItem(UiText.T("Release notes")+"##Notes"))
                        if(tab.Visible) MaterialText.TextWrapped(string.IsNullOrWhiteSpace(entry.Manifest.Changelog)?UiText.T("No release notes supplied."):entry.Manifest.Changelog);
                    using(var tab=UiGui.TabItem(UiText.T("Compatibility")+"##Compatibility"))
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
                ImGui.PushTextWrapPos(0); MaterialText.TextColored(c.Primary,entry.Repository.Name); ImGui.PopTextWrapPos();
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
            float nameHeight;
            using(UiText.Font(UiFontRole.BodyStrong)) nameHeight=MaterialText.Measure(repo.Name,false,Math.Max(1,ImGui.GetContentRegionAvail().X-32*s)).Y;
            var height=Math.Max(150*s,urlHeight+nameHeight+2*ImGui.GetTextLineHeight()+(repo.IsOfficial?1:repo.IsAdded?3:2)*(MaterialControls.Metrics.Height+10*s)+44*s+(pendingRemoval==repo.Url?180*s:0));
            using(var card=InstallerSurface("RepositoryCard",new(-1,height/s),outline:true,padding:16,radius:8))
            {
            MeasureBounds?.Invoke("repository-card-"+repo.Url,ImGui.GetWindowPos(),ImGui.GetWindowPos()+ImGui.GetWindowSize());
            using(UiText.Font(UiFontRole.BodyStrong)) MaterialText.TextWrapped(repo.Name);
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
            var view=InstallerButton("View plugins",(repository?"pin-repo-view-":"pin-author-view-")+row.Key,leading:repository?MaterialIcon.Database:MaterialIcon.Person);
            var block=ImGui.IsItemClicked(ImGuiMouseButton.Right);
            if(view || block)
            {
                (repository?repositoryFilters:pinnedAuthorFilters)[row.Key]=block?InstallerFilterMode.Blocked:InstallerFilterMode.Required;
                installerPage="Discover"; selectedInstaller=null; ResetInstallerResults(); installerMedia?.Clear();
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
        if(!full && MaterialTheme.Current.IsLcars)
        {
            origin+=new Vector2(16*s,10*s);
            size-=new Vector2(32*s,10*s);
        }
        var draw=ImGui.GetWindowDrawList();
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
            :failed?UiText.T("Preview unavailable"):images.Length>0&&!hasPreview?UiText.T("Working..."):null;
        if(!full)
        {
            var c=MaterialTheme.Current.Colors;
            var cardTint=entry.Repository.IsAdded?Vector4.One:new Vector4(.48f,.48f,.48f,1);
            var inset=CardInset(size.X);
            if(BotologyPresentation.IsMaterial) MaterialSkin.PanelFrame(origin,origin+size,inset:true);
            else MaterialCanvas.Surface(origin,origin+size,hasPreview?c.SurfaceContainerHigh:InstallerShade(c.SurfaceContainerLow,.75f),c.SurfaceContainerLowest,inset*.65f);
            if(texture is not null)
            {
                // Screenshots and real plugin icons share the complete band, including upscaling.
                var ratio=Math.Min(size.X/texture.Width,size.Y/texture.Height);
                var painted=new Vector2(texture.Width,texture.Height)*ratio;
                var min=origin+(size-painted)*.5f;
                MaterialCanvas.DrawImage(draw,texture.Handle,new(texture.Width,texture.Height),origin,origin+size,tint:cardTint);
                MeasureBounds?.Invoke((hasPreview?"preview-image-":"preview-icon-")+entry.Identity,min,min+painted);
            }
            else
            {
                var side=Math.Min(size.X,size.Y);
                var min=origin+(size-new Vector2(side))*.5f;
                MaterialIcons.Draw(MaterialIcon.Cube,min,side,MaterialColor.Alpha(c.OnSurfaceVariant,.65f));
                MeasureBounds?.Invoke("preview-placeholder-"+entry.Identity,min,min+new Vector2(side));
            }
            MeasureBounds?.Invoke("preview-slot-"+entry.Identity,origin,origin+size);
            ImGui.SetCursorScreenPos(origin);ImGui.Dummy(size);
            if(label is not null && ImGui.IsItemHovered()) MaterialText.SetTooltip(label);
            return;
        }
        var surface=hasPreview?MaterialTheme.Current.Colors.SurfaceContainerHigh:InstallerShade(MaterialTheme.Current.Colors.SurfaceContainerLow,.75f);
        if(BotologyPresentation.IsMaterial) MaterialSkin.PanelFrame(origin,origin+size,inset:true);
        else MaterialCanvas.Surface(origin,origin+size,surface,MaterialTheme.Current.Colors.SurfaceContainerLowest,8*s);
        var labelHeight=label is null?0:MaterialText.Measure(label,false,Math.Max(1,size.X-16*s)).Y;
        var placeholderSide=Math.Min((full?48:32)*s,Math.Max(1,height-labelHeight-12*s));
        var placeholderTop=Math.Max(0,(height-placeholderSide-labelHeight-8*s)*.5f);
        var tint=full||entry.Repository.IsAdded?Vector4.One:new Vector4(.48f,.48f,.48f,1);
        if(texture is not null)
        {
            var media=hasPreview && !failed;
            var min=media?origin:origin+new Vector2((size.X-placeholderSide)*.5f,placeholderTop);
            var max=media?origin+size:min+new Vector2(placeholderSide);
            MaterialCanvas.DrawImage(draw,texture.Handle,new(texture.Width,texture.Height),min,max,tint:tint);
            if(!media) MeasureBounds?.Invoke("preview-icon-"+entry.Identity,min,max);
        }
        else
        {
            var iconMin=origin+new Vector2((size.X-placeholderSide)*.5f,placeholderTop);
            MaterialIcons.Draw(MaterialIcon.Cube,iconMin,placeholderSide,MaterialColor.Alpha(MaterialTheme.Current.Colors.OnSurfaceVariant,full?1:.55f));
            MeasureBounds?.Invoke("preview-placeholder-"+entry.Identity,iconMin,iconMin+new Vector2(placeholderSide));
        }
        if(label is not null)
        {
            var labelWidth=Math.Min(size.X-16*s,MaterialText.Measure(label).X);
            ImGui.SetCursorScreenPos(origin+new Vector2(Math.Max(8*s,(size.X-labelWidth)*.5f),placeholderTop+placeholderSide+8*s));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX()+size.X-16*s);
            using(var labelStyle=new MaterialStyleScope())
            {
                labelStyle.Color(ImGuiCol.Text,MaterialColor.Alpha(MaterialTheme.Current.Colors.OnSurfaceVariant,full?1:.8f));
                MaterialText.TextWrapped(label);
            }
            ImGui.PopTextWrapPos();
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
        return UiText.T(entry.HasUpdate?"Update available":entry.IsLoaded?"Loaded":entry.Installed?.IsOutdated==true?"Unavailable for this API":entry.IsInstalled?"Disabled":entry.InstallBlockReason??"Available");
    }
    private static string CompatibilityState(InstallerEntry entry)=>entry.Compatibility is { IsAssessable:true } row
        ?(row.Ignored?UiText.T("Ignored")+" — ":"")+UiText.Assessment(row.Assessment):UiText.T("Not assessed");
}

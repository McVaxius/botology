using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using botology.Models;
using botology.Services;

namespace botology.Windows;

public sealed class MainWindow : PositionedWindow, IDisposable
{
    private const string BlockingPopupId = "BotologyBlockingAlert";
    private const string ColumnSelectionPopupId = "BotologyColumnSelection";
    private const string PatchNotesPopupId = "BotologyPatchNotes";
    private const string AiAttributionTooltip = "Likely AI-written code based on Aetherfeed contributor and coding-pattern attribution; snapshot 2026-07-22.";

    private readonly IBotologyUi plugin;
    private string categoryFilterText = string.Empty;
    private string pluginNameFilterText = string.Empty;
    private string authorFilterText = string.Empty;

    private GridColumn sortColumn = GridColumn.Category;
    private bool sortDescending;
    private float topHeight=375;
    internal Action<string,Vector2,Vector2>? MeasureBounds { get; set; }

    private enum GridColumn
    {
        Category,
        Source,
        AiAttribution,
        Installed,
        Update,
        Repo,
        Enabled,
        Dtr,
        Downloads,
        LastUpdated,
        DalamudApiLevel,
        Author,
        Notes,
        Ignore,
    }

    public MainWindow(Plugin plugin) : this((IBotologyUi)plugin) { }

    internal MainWindow(IBotologyUi plugin)
        : base($"{PluginInfo.DisplayName}##Main",ImGuiWindowFlags.HorizontalScrollbar)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(1240f, 640f),
            MaximumSize = new Vector2(1880f, 1400f),
        };
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog, Priority = 0, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.OpenConfigUi(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Settings")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.PowerOff, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.SetPluginEnabled(!plugin.Configuration.PluginEnabled, printStatus: true); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T(plugin.Configuration.PluginEnabled ? "Disable plugin" : "Enable plugin") + "\n" + UiText.T("Manager enabled")),
        });
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        UiGui.ReserveTitleSpace(this, PluginInfo.DisplayName + " v" + typeof(Plugin).Assembly.GetName().Version, 1240);
        base.PreDraw();
    }

    public override void Draw()
    {
        windowMotion.DrawChrome();
        UiGui.TitleWithButtons(PluginInfo.DisplayName,PluginInfo.DisplayName+" v"+Assembly.GetExecutingAssembly().GetName().Version, this);
        using var font=UiText.Font(UiFontRole.Body);
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(34,20));
        DrawContent(plugin.CaptureRows(), plugin.CaptureDtrEntries(), plugin.GetCatalogRefreshInfo());
    }

    private void DrawContent(IReadOnlyList<PluginAssessmentRow> rows, IReadOnlyList<DtrEntrySnapshot> dtrEntries, CatalogRefreshInfo refreshInfo)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        var scale=MaterialTheme.Metrics.Scale;
        var topLimit=Math.Max(1,ImGui.GetContentRegionAvail().Y-(BotologyPresentation.FooterHeight+BotologyPresentation.RegionGap+118)*scale);
        var topPixels=Math.Min(topHeight*scale,topLimit);
        var parentId=ImGui.GetID("");
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg,Vector4.Zero);
        if(ImGui.BeginChild("##MainControls",new Vector2(-1,topPixels),false,ImGuiWindowFlags.HorizontalScrollbar))
        {
        ImGuiP.PushOverrideID(parentId);
        var topStart=ImGui.GetCursorPosY();
        DrawHeader(version);
        Gap(16);
        DrawActionsRow(rows);
        Gap(BotologyPresentation.Compact ? 18 : 20);
        var assessed = rows.Where(row => row.IsAssessable && !row.Ignored).ToList();
        DrawStatusRow(refreshInfo, assessed.Count(row => row.Assessment.Severity == AssessmentSeverity.Green),
            assessed.Count(row => row.Assessment.Severity == AssessmentSeverity.Yellow),
            assessed.Count(row => row.Assessment.Severity == AssessmentSeverity.Red), GetVisibleRows(rows, dtrEntries).Count);
        Gap(BotologyPresentation.Compact ? 16 : 20);
        DrawFilterRow(rows);
        Gap(16);
        topHeight=(ImGui.GetCursorPosY()-topStart)/scale;
        ImGui.PopID();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY()-ImGui.GetStyle().ItemSpacing.Y);
        var visibleRows = GetVisibleRows(rows, dtrEntries);
        var footerHeight = BotologyPresentation.FooterHeight * MaterialTheme.Metrics.Scale;
        var tableHeight = Math.Max(1, ImGui.GetContentRegionAvail().Y - footerHeight - BotologyPresentation.RegionGap * MaterialTheme.Metrics.Scale - ImGui.GetStyle().ItemSpacing.Y);
        var visibleColumns = GetVisibleColumns();
        using (var tableControls = MaterialControls.Push(BotologyPresentation.Controls(34,20)))
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg,MaterialTheme.Current.Colors.Background);
            var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.Sortable | ImGuiTableFlags.SizingFixedFit;
            if (ImGui.BeginTable("BotologyGrid", visibleColumns.Length, tableFlags, new Vector2(-1, tableHeight),
                    Math.Max(ImGui.GetContentRegionAvail().X, visibleColumns.Sum(ColumnWidth) * MaterialTheme.Metrics.Scale)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                foreach (var column in visibleColumns)
                    SetupColumn(column);
                using(UiText.Font(UiFontRole.BodyStrong))
                    UiGui.TableHeadersRow(BotologyPresentation.TableHeaderHeight*MaterialTheme.Metrics.Scale);
                var nativeTable=ImGuiP.GetCurrentTable();
                MeasureBounds?.Invoke("table-header",new Vector2(nativeTable.WorkRect.Min.X,nativeTable.RowPosY1),new Vector2(nativeTable.WorkRect.Max.X,nativeTable.RowPosY2));
                unsafe
                {
                    for(var i=0;i<visibleColumns.Length;i++)
                        MeasureBounds?.Invoke("column-"+visibleColumns[i],new Vector2(nativeTable.Columns.Data[i].MinX,0),new Vector2(nativeTable.Columns.Data[i].MaxX,0));
                }
                var specs = ImGui.TableGetSortSpecs();
                if (specs.SpecsCount > 0)
                {
                    sortColumn = (GridColumn)(specs.Specs.ColumnUserID - 1);
                    sortDescending = specs.Specs.SortDirection == ImGuiSortDirection.Descending;
                    specs.SpecsDirty = false;
                }
                visibleRows.Sort((left, right) =>
                {
                    var comparison = sortColumn == GridColumn.Dtr
                        ? Nullable.Compare(GetDtrVisibility(left, dtrEntries), GetDtrVisibility(right, dtrEntries))
                        : CompareRows(left, right, sortColumn);
                    if (comparison != 0) return sortDescending ? -comparison : comparison;
                    return StringComparer.Ordinal.Compare(left.Entry.Id, right.Entry.Id);
                });
                foreach (var row in visibleRows)
                    DrawPluginRow(row, visibleColumns, dtrEntries);
                if (visibleRows.Count == 0)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    MaterialText.TextWrapped(UiText.T("No plugins match the current filters."));
                }
                ImGui.EndTable();
            }
            ImGui.PopStyleColor();
        }
        Gap(BotologyPresentation.RegionGap);
        DrawFooter(footerHeight / MaterialTheme.Metrics.Scale);
        DrawColumnSelectionPopup();
        DrawPatchNotesPopup();
        DrawBlockingAlertPopup();
        FinalizePendingWindowPlacement();
    }

    private static void Gap(float logical)
        => ImGui.SetCursorPosY(ImGui.GetCursorPosY()+Math.Max(0,logical*MaterialTheme.Metrics.Scale-ImGui.GetStyle().ItemSpacing.Y));

    private void DrawHeader(string version)
    {
        var s=MaterialTheme.Metrics.Scale;
        var origin=ImGui.GetCursorScreenPos();
        var width=ImGui.GetContentRegionAvail().X;
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(32,20));
        DrawLogo((BotologyPresentation.Compact ? 48 : 52)*s);
        MeasureBounds?.Invoke("logo",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        using(UiText.Font(BotologyPresentation.Compact ? UiFontRole.CompactTitle : UiFontRole.Title))
        {
            ImGui.SetCursorScreenPos(origin+new Vector2(68*s,-ImGui.FindGlyphNoFallback(ImGui.GetFont(),'B').Y0*ImGui.GetFontSize()/ImGui.GetFont().FontSize));
            MaterialText.Text(PluginInfo.DisplayName);
            MeasureBounds?.Invoke("title",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        }
        ImGui.SetCursorScreenPos(origin+new Vector2(68*s,(BotologyPresentation.Compact ? 38 : 44)*s-ImGui.FindGlyphNoFallback(ImGui.GetFont(),'D').Y0*ImGui.GetFontSize()/ImGui.GetFont().FontSize));
        MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant,UiText.T("Dalamud Plugin Compatibility Manager"));
        var identity=Math.Max(374*s,68*s+MaterialText.Measure(UiText.T("Dalamud Plugin Compatibility Manager")).X+20*s);
        var enabled=plugin.Configuration.PluginEnabled;
        var managerWidth=MaterialText.Measure(UiText.T("Manager enabled")).X+40*s;
        var compactWidth = plugin.Configuration.UiCompactVisibleOnMainWindow
            ? MaterialText.Measure("C").X + ImGui.GetFrameHeight() + 16 * s : 0;
        var opacityWidth = MaterialText.Measure(UiText.T("Transparency")).X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        var selectorMetrics = BotologyPresentation.Controls(28, 18);
        var selectedLanguage = UiText.Languages.First(entry => entry.Code == UiText.Current.Language).Name;
        var languageWidth = plugin.Configuration.UiLanguageVisibleOnMainWindow
            ? Math.Max(112 * s, MathF.Ceiling(MaterialText.Measure(selectedLanguage).X + selectorMetrics.Height
                + 3 * selectorMetrics.Gap + Math.Min(selectorMetrics.IconSize, selectorMetrics.Height))) : 0;
        var selectorsWidth = compactWidth + opacityWidth + languageWidth + 2 * ImGui.GetStyle().ItemSpacing.X;
        var linksWidth=new[] { "     Ko-fi","     Discord","AETHERFEED",UiText.T("Settings") }.Sum(l=>MaterialText.Measure(l).X+2*MaterialControls.Metrics.Padding.X)
            +2*(MaterialControls.Metrics.IconSize+MaterialControls.Metrics.Gap)+3*ImGui.GetStyle().ItemSpacing.X;
        var wrapped=identity+managerWidth+selectorsWidth+linksWidth+40*s>width;
        var y=wrapped?BotologyPresentation.HeaderHeight*s:(BotologyPresentation.Compact ? 16 : 22)*s;
        var x=wrapped?0:identity;
        BotologyPresentation.Separator(origin+new Vector2(x-16*s,y),36*s);
        ImGui.SetCursorScreenPos(origin+new Vector2(x,y));
        void Next(float nextWidth)
        {
            if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + nextWidth <= origin.X + width) ImGui.SameLine();
        }
        if(MaterialSelection.Checkbox("manager-enabled",UiText.T("Manager enabled"),ref enabled)) plugin.SetPluginEnabled(enabled,printStatus:true);
        if (plugin.Configuration.UiCompactVisibleOnMainWindow)
        {
            Next(compactWidth);
            var compact=plugin.Configuration.UiCompact;
            if(ImGui.Checkbox("C##CompactMode",ref compact)) { plugin.Configuration.UiCompact=compact; plugin.Configuration.Save(); }
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("Compact mode"));
        }
        Next(opacityWidth);
        var transparency = plugin.Configuration.UiTransparencyEnabled;
        if (UiGui.Checkbox("Transparency##MainWindow", ref transparency))
        { plugin.Configuration.UiTransparencyEnabled = transparency; plugin.Configuration.Save(); }
        if (plugin.Configuration.UiLanguageVisibleOnMainWindow)
        {
            Next(languageWidth);
            plugin.DrawAppearanceSelector();
        }
        var linksY = ImGui.GetItemRectMin().Y - origin.Y;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + linksWidth <= origin.X + width)
            ImGui.SetCursorScreenPos(new Vector2(origin.X + Math.Max(ImGui.GetItemRectMax().X - origin.X + 12 * s, width - linksWidth), origin.Y + linksY));
        else
        {
            linksY = ImGui.GetItemRectMax().Y - origin.Y + ImGui.GetStyle().ItemSpacing.Y;
            ImGui.SetCursorScreenPos(origin + new Vector2(0, linksY));
        }
        if(DrawBrandLink("support","Ko-fi",false)) plugin.OpenUrl(PluginInfo.SupportUrl);
        ImGui.SameLine();
        BotologyPresentation.Separator(ImGui.GetCursorScreenPos()+new Vector2(-5*s,4*s),24*s);
        if(DrawBrandLink("discord","Discord",true)) plugin.OpenUrl(PluginInfo.DiscordUrl);
        ImGui.SameLine();
        BotologyPresentation.Separator(ImGui.GetCursorScreenPos()+new Vector2(-5*s,4*s),24*s);
        var link=new MaterialControlAppearance(Vector4.Zero,BotologyPresentation.Rgb(0xA8BCFF),Vector4.Zero);
        if(MaterialButton.Draw("feed","AETHERFEED",link,leading:MaterialIcon.ExternalLink)) plugin.OpenUrl(PluginInfo.AetherfeedUrl);
        ImGui.SameLine();
        BotologyPresentation.Separator(ImGui.GetCursorScreenPos()+new Vector2(-5*s,4*s),24*s);
        if(MaterialButton.Draw("settings",UiText.T("Settings"),new MaterialControlAppearance(Vector4.Zero,MaterialTheme.Current.Colors.OnSurface,Vector4.Zero),leading:MaterialIcon.Settings)) plugin.OpenConfigUi();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new(width,Math.Max(BotologyPresentation.HeaderHeight*s,linksY+36*s)));
        MeasureBounds?.Invoke("header",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.F("{0} {1}",PluginInfo.DisplayName,version));
    }

    private static void DrawLogo(float size)
    {
        var p=ImGui.GetCursorScreenPos();
        var dl=ImGui.GetWindowDrawList();
        var color=MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary);
        var background=MaterialCanvas.Color(MaterialTheme.Current.Colors.Background);
        Vector2[] points=[p+new Vector2(.50f,.05f)*size,p+new Vector2(.90f,.29f)*size,p+new Vector2(.90f,.70f)*size,
            p+new Vector2(.50f,.96f)*size,p+new Vector2(.10f,.70f)*size,p+new Vector2(.10f,.29f)*size,p+new Vector2(.50f,.54f)*size];
        for(var i=0;i<6;i++) dl.AddLine(points[i],points[(i+1)%6],color,2*MaterialTheme.Metrics.Scale);
        foreach(var i in new[]{1,3,5}) dl.AddLine(points[i],points[6],color,2*MaterialTheme.Metrics.Scale);
        foreach(var point in points) { dl.AddCircleFilled(point,size*.065f,color,16); dl.AddCircleFilled(point,size*.035f,background,16); }
        ImGui.Dummy(new(size,size));
    }

    private static bool DrawBrandLink(string id,string label,bool discord)
    {
        var s=MaterialTheme.Metrics.Scale;
        var clicked=MaterialButton.Draw(id,"     "+label,new MaterialControlAppearance(Vector4.Zero,BotologyPresentation.Rgb(0xA8BCFF),Vector4.Zero));
        var p=ImGui.GetItemRectMin()+new Vector2(MaterialControls.Metrics.Padding.X,(MaterialControls.Metrics.Height-22*s)*.5f);
        var dl=ImGui.GetWindowDrawList();
        if(discord)
        {
            var ink=MaterialCanvas.Color(BotologyPresentation.Rgb(0x8B9DF8));
            Vector2[] outline=[new(3,4),new(7,2),new(15,2),new(19,4),new(22,16),new(17,19),new(15,16),new(7,16),new(5,19),new(0,16)];
            foreach(var point in outline) dl.PathLineTo(p+point*s);
            dl.PathFillConvex(ink);
            var eye=MaterialCanvas.Color(MaterialTheme.Current.Colors.Background);
            dl.AddCircleFilled(p+new Vector2(7,10)*s,2*s,eye,12);
            dl.AddCircleFilled(p+new Vector2(15,10)*s,2*s,eye,12);
        }
        else
        {
            var ink=MaterialCanvas.Color(BotologyPresentation.Rgb(0xF97789));
            dl.AddCircleFilled(p+new Vector2(6,7)*s,6*s,ink,20);
            dl.AddCircleFilled(p+new Vector2(15,7)*s,6*s,ink,20);
            dl.AddTriangleFilled(p+new Vector2(0,8)*s,p+new Vector2(21,8)*s,p+new Vector2(10,21)*s,ink);
            dl.AddTriangleFilled(p+new Vector2(4,19)*s,p+new Vector2(7,15)*s,p+new Vector2(9,22)*s,MaterialCanvas.Color(BotologyPresentation.Rgb(0x8DDFA9)));
        }
        return clicked;
    }

    private void DrawActionsRow(IReadOnlyList<PluginAssessmentRow> rows)
    {
        var s=MaterialTheme.Metrics.Scale;
        using var font=UiText.Font(UiFontRole.Action);
        var labels=new[]{"DTR","Catalog","Data","Patch Notes","Reload","Status"};
        var textHeight=labels.Max(label=>MaterialText.Measure(UiText.T(label)).Y);
        var toolbar=MaterialControlMetrics.Measure(MaterialTheme.Metrics,Math.Max(Math.Max(ImGui.GetTextLineHeight(),textHeight),28*s),MaterialControlContext.Toolbar);
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(BotologyPresentation.ActionHeight,28) with { Height=toolbar.Height,Padding=toolbar.Padding });
        var width=ImGui.GetContentRegionAvail().X;
        var icons=new[]{MaterialIcon.Document,MaterialIcon.Grid,MaterialIcon.Database,MaterialIcon.Document,MaterialIcon.Refresh,MaterialIcon.Chart};
        var ids=new[]{"dtr-manager","catalog","data","patch-notes","reload","status"};
        float[] proportions=[213,220,224,257,236,236];
        var natural=labels.Max(l=>MaterialText.Measure(UiText.T(l)).X)+68*s;
        var columns=Math.Clamp((int)((width+10*s)/(natural+10*s)),1,6);
        if(columns==6 && proportions.Select((p,i)=>p*(width-50*s)/proportions.Sum()<MaterialText.Measure(UiText.T(labels[i])).X+68*s).Any(tooSmall=>tooSmall)) columns=5;
        var tileWidth=Math.Max(natural,(width-(columns-1)*10*s)/columns)/s;
        for(var i=0;i<6;i++)
        {
            if(i%columns!=0) ImGui.SameLine();
            var actionWidth=columns==6 ? proportions[i]*(width-50*s)/proportions.Sum()/s : tileWidth;
            var clicked=MaterialButton.Draw(ids[i],UiText.T(labels[i]),BotologyPresentation.Action(i==0),new(actionWidth,toolbar.Height/s),leading:icons[i]);
            MeasureBounds?.Invoke("action-"+i,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            if(!clicked) continue;
            switch(i)
            {
                case 0: plugin.OpenDtrManagerUi(); break;
                case 1: plugin.OpenCatalogEditorUi(); break;
                case 2: plugin.OpenCatalogFolder(); break;
                case 3: ImGui.OpenPopup(PatchNotesPopupId); break;
                case 4: plugin.RefreshMasterCatalog(force:true,silent:false); break;
                case 5: plugin.PrintStatus(BotologyCatalog.BuildAlertSummary(rows)); break;
            }
        }
    }

    private void DrawStatusRow(CatalogRefreshInfo info,int green,int yellow,int red,int visible)
    {
        var s=MaterialTheme.Metrics.Scale;
        var start=ImGui.GetCursorScreenPos();
        var available=ImGui.GetContentRegionAvail().X;
        var height=Math.Max(BotologyPresentation.StatusHeight*s,(info.RefreshInProgress ? 3 : 2)*ImGui.GetTextLineHeight()+8*s);
        (string Label,int Count,Vector4 Color)[] counters=[("Green",green,BotologyPresentation.GreenCounter),
            ("Yellow",yellow,BotologyPresentation.YellowCounter),("Red",red,BotologyPresentation.RedCounter),("Visible",visible,BotologyPresentation.VisibleCounter)];
        float[] proportions=BotologyPresentation.Compact ? [244,230,226,206] : [239,222,217,172];
        ImGui.SetCursorScreenPos(start+new Vector2((BotologyPresentation.Compact ? 0 : 5)*s,0));
        for(var index=0;index<counters.Length;index++)
        {
            var counter=counters[index];
            var inline=BotologyPresentation.Compact;
            var label=UiText.T(counter.Label);
            var count=counter.Count.ToString(UiText.Current.Culture);
            var labelX=inline ? index==0 ? 72 : 68 : 65;
            var labelWidth=MaterialText.Measure(label).X;
            var width=proportions[index]*Math.Max(1,available*(inline ? .671f : .632f)-60*s)/(inline ? 906 : 850);
            if(inline)
            {
                using var counterFont=UiText.Font(UiFontRole.Counter);
                width=Math.Max(width,labelX*s+labelWidth+16*s+MaterialText.Measure(count).X+20*s);
            }
            var p=ImGui.GetCursorScreenPos(); var dl=ImGui.GetWindowDrawList();
            var c=MaterialTheme.Current.Colors;
            MaterialCanvas.Surface(p,p+new Vector2(width,height),BotologyPresentation.PanelTop,BotologyPresentation.PanelBottom,4*s);
            dl.AddRect(p,p+new Vector2(width,height),MaterialCanvas.Color(c.OutlineVariant),4*s);
            dl.AddCircleFilled(p+new Vector2((inline && index==0 ? 35 : 31)*s,height*.5f),13*s,MaterialCanvas.Color(counter.Color),24);
            MaterialText.AddText(dl,p+new Vector2(labelX*s,inline ? (height-ImGui.GetTextLineHeight())*.5f : 10*s),MaterialCanvas.Color(c.OnSurfaceVariant),label);
            var countX=inline ? labelX*s+labelWidth+16*s : 65*s;
            using(UiText.Font(UiFontRole.Counter))
            {
                ImGui.SetCursorScreenPos(p+new Vector2(countX,inline ? (height-ImGui.GetTextLineHeight())*.5f : 31*s));
                MaterialText.Text(count);
                MeasureBounds?.Invoke("counter",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            }
            ImGui.SetCursorScreenPos(p); ImGui.Dummy(new(width,height)); ImGui.SameLine(0,(inline ? 16 : 20)*s);
        }
        var origin=ImGui.GetCursorScreenPos();
        BotologyPresentation.Separator(origin-new Vector2(2*s,-13*s),38*s);
        ImGui.GetWindowDrawList().AddCircleFilled(origin+new Vector2(40*s,height*.5f),13*s,MaterialCanvas.Color(BotologyPresentation.Rgb(0x50D1A0)),24);
        MaterialIcons.Draw(MaterialIcon.Check,origin+new Vector2(30*s,height*.5f-10*s),20*s,MaterialTheme.Current.Colors.Background);
        ImGui.SetCursorScreenPos(origin+new Vector2(67*s,10*s));
        ImGui.BeginGroup();
        MaterialText.Text(UiText.T("Master updated"));
        MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant,UiText.Date(info.LastUpdatedUtc));
        if(info.RefreshInProgress) MaterialText.TextColored(MaterialTheme.Current.Colors.Primary,UiText.T("Refreshing..."));
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.F("Checked: {0}",UiText.Date(info.LastCheckedUtc)));
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(start); ImGui.Dummy(new(available,height));
        MeasureBounds?.Invoke("status",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
    }

    private void DrawFilterRow(IReadOnlyList<PluginAssessmentRow> rows)
    {
        var s=MaterialTheme.Metrics.Scale;
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(38,20,preserveHeight:true));
        var origin=ImGui.GetCursorScreenPos(); var width=ImGui.GetContentRegionAvail().X; var gap=(BotologyPresentation.Compact ? 20 : 16)*s;
        var checkWidth=new[]{"Hide uninstalled","Detailed Notes","Has DTR entry"}.Max(l=>MaterialText.Measure(UiText.T(l)).X)+32*s;
        var columnWidth=Math.Max(150*s,MaterialText.Measure(UiText.T("Columns")).X+70*s);
        var colorWidth=new[]{"Green","Yellow","Red"}.Sum(l=>MaterialText.Measure(UiText.T(l)).X+32*s)+16*s;
        var categories=rows.Select(r=>r.Entry.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v=>v,StringComparer.OrdinalIgnoreCase).ToArray();
        var categoryWidth=Math.Max((BotologyPresentation.Compact ? 206 : 213)*s,categories.Append(UiText.T("All Categories")).Max(v=>MaterialText.Measure(v).X)+ImGui.GetFrameHeight()+2*MaterialControls.Metrics.Padding.X);
        var searchWidth=Math.Max((BotologyPresentation.Compact ? 236 : 222)*s,MaterialText.Measure(UiText.T("Search plugins...")).X+2*MaterialControls.Metrics.Padding.X+ImGui.GetFrameHeight());
        var authorWidth=Math.Max((BotologyPresentation.Compact ? 238 : 230)*s,MaterialText.Measure(UiText.T("Search authors...")).X+2*MaterialControls.Metrics.Padding.X+ImGui.GetFrameHeight());
        var widths=new[]{categoryWidth,searchWidth,authorWidth,checkWidth,columnWidth,colorWidth};
        var padding=16*s;
        var positions=new Vector2[6]; var x=padding; var y=padding; var rowHeight=0f;
        for(var i=0;i<6;i++)
        {
            var groupHeight=i==3?(BotologyPresentation.Compact ? 68 : 84)*s:(BotologyPresentation.Compact ? 62 : 70)*s;
            if(x+widths[i]>width-padding && x>padding) { x=padding; y+=rowHeight+12*s; rowHeight=0; }
            positions[i]=origin+new Vector2(x,y);
            var separatorGap=i switch
            {
                2 => BotologyPresentation.Compact ? 12 : 40,
                3 => BotologyPresentation.Compact ? 24 : 36,
                4 => BotologyPresentation.Compact ? 20 : 28,
                _ => 0,
            };
            x+=widths[i]+gap+separatorGap*s; rowHeight=Math.Max(rowHeight,groupHeight);
        }
        var height=Math.Max(BotologyPresentation.FilterHeight*s,y+rowHeight+padding);
        var c=MaterialTheme.Current.Colors; var dl=ImGui.GetWindowDrawList();
        MaterialCanvas.Surface(origin,origin+new Vector2(width,height),BotologyPresentation.PanelTop,BotologyPresentation.PanelBottom,4*s);
        dl.AddRect(origin,origin+new Vector2(width,height),MaterialCanvas.Color(c.OutlineVariant),4*s);
        ImGui.SetCursorScreenPos(positions[0]); ImGui.BeginGroup();
        MaterialText.Text(UiText.T("Category"));
        var options=new MaterialOptions<string>(new[]{new MaterialOption<string>("all","",UiText.T("All Categories"))}
            .Concat(categories.Select(v=>new MaterialOption<string>("category:"+v,v,v))).ToArray());
        MaterialCombo.Draw("##CategoryFilter",options,ref categoryFilterText,BotologyPresentation.Field,categoryWidth/s,searchable:false);
        MeasureBounds?.Invoke("category",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(positions[1]); ImGui.BeginGroup();
        MaterialText.Text(UiText.T("Plugin Name"));
        MaterialTextField.SearchField("##PluginNameFilter",ref pluginNameFilterText,BotologyPresentation.Field,UiText.T("Search plugins..."),searchWidth/s,maxLength:128);
        MeasureBounds?.Invoke("search",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(positions[2]); ImGui.BeginGroup();
        MaterialText.Text(UiText.T("Author"));
        MaterialTextField.SearchField("##AuthorFilter",ref authorFilterText,BotologyPresentation.Field,UiText.T("Search authors..."),authorWidth/s,maxLength:128);
        MeasureBounds?.Invoke("author",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(positions[3]); ImGui.BeginGroup();
        BotologyPresentation.Separator(positions[3]-new Vector2(16*s,0),84*s);
        using(var checks=MaterialControls.Push(BotologyPresentation.Controls(24,20)))
        {
            var hide=plugin.Configuration.HideUninstalledPlugins;
            if(MaterialSelection.Checkbox("hide-uninstalled",UiText.T("Hide uninstalled"),ref hide)) plugin.SetHideUninstalledPlugins(hide);
            DrawSavedCheckbox("Detailed Notes",plugin.Configuration.ShowDetailedNotes,v=>plugin.Configuration.ShowDetailedNotes=v);
            DrawSavedCheckbox("Has DTR entry",plugin.Configuration.ShowOnlyPluginsWithDtrEntry,v=>plugin.Configuration.ShowOnlyPluginsWithDtrEntry=v);
        }
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(positions[4]+new Vector2(0,21*s));
        BotologyPresentation.Separator(positions[4]-new Vector2(12*s,0),84*s);
        using(var buttonControls=MaterialControls.Push(MaterialControlMetrics.Measure(MaterialTheme.Metrics,Math.Max(Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(UiText.T("Columns")).Y),20*s),MaterialControlContext.Toolbar) with { IconSize=20*s }))
        {
            if(MaterialButton.Draw("columns",UiText.T("Columns"),BotologyPresentation.Action(),new(columnWidth/s,0),leading:MaterialIcon.Table,trailing:MaterialIcon.ChevronDown)) ImGui.OpenPopup(ColumnSelectionPopupId);
            MeasureBounds?.Invoke("columns-button",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        }
        ImGui.SetCursorScreenPos(positions[5]); ImGui.BeginGroup();
        BotologyPresentation.Separator(positions[5]-new Vector2(12*s,0),84*s);
        MaterialText.Text(UiText.T("Color filter"));
        ImGui.Dummy(new(0,8*s));
        using(var checks=MaterialControls.Push(BotologyPresentation.Controls(24,20)))
        {
            DrawSavedCheckbox("Green",plugin.Configuration.ShowOnlyGreenPlugins,v=>plugin.Configuration.ShowOnlyGreenPlugins=v); ImGui.SameLine();
            DrawSavedCheckbox("Yellow",plugin.Configuration.ShowOnlyYellowPlugins,v=>plugin.Configuration.ShowOnlyYellowPlugins=v); ImGui.SameLine();
            DrawSavedCheckbox("Red",plugin.Configuration.ShowOnlyRedPlugins,v=>plugin.Configuration.ShowOnlyRedPlugins=v);
        }
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("No colors selected shows all rows. Selected colors include only assessed, non-ignored rows."));
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(origin); ImGui.Dummy(new(width,height));
        MeasureBounds?.Invoke("filters",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
    }

    private void DrawFooter(float height)
    {
        var origin=ImGui.GetCursorScreenPos(); var s=MaterialTheme.Metrics.Scale;
        var toolbar=MaterialControlMetrics.Measure(MaterialTheme.Metrics,Math.Max(ImGui.GetTextLineHeight(),24*s),MaterialControlContext.Toolbar);
        using var controls=MaterialControls.Push(BotologyPresentation.Controls(height,24) with { Height=toolbar.Height,Padding=toolbar.Padding });
        var buttonHeight=toolbar.Height/s;
        if(MaterialButton.Draw("xlsettings","XLSETTINGS",BotologyPresentation.Action(),new(176,buttonHeight),leading:MaterialIcon.Download)) plugin.RunTextCommand("/xlsettings");
        ImGui.SameLine(0,32*s); BotologyPresentation.Separator(origin+new Vector2(192*s,13*s),26*s);
        if(MaterialButton.Draw("xlplugins","XLPLUGINS",BotologyPresentation.Action(),new(170,buttonHeight),leading:MaterialIcon.Download)) plugin.RunTextCommand("/xlplugins");
        ImGui.SameLine(0,32*s); BotologyPresentation.Separator(origin+new Vector2(394*s,13*s),26*s);
        if(MaterialButton.Draw("xllog","XLLOG",BotologyPresentation.Action(),new(122,buttonHeight),leading:MaterialIcon.Document)) plugin.RunTextCommand("/xllog");
        MeasureBounds?.Invoke("footer",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
    }

    private List<PluginAssessmentRow> GetVisibleRows(
        IReadOnlyList<PluginAssessmentRow> rows,
        IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        var visibleRows = plugin.Configuration.HideUninstalledPlugins
            ? rows.Where(row => row.IsInstalled)
            : rows;

        return visibleRows
            .Where(row => MatchesFilters(row, dtrEntries))
            .ToList();
    }

    private GridColumn[] GetVisibleColumns()
    {
        var cfg = plugin.Configuration;
        var columns = new System.Collections.Generic.List<GridColumn>
        {
            GridColumn.Category,
            GridColumn.Source,
        };

        if (cfg.ShowAiColumn)
            columns.Add(GridColumn.AiAttribution);

        if (cfg.ShowInstalledColumn)
            columns.Add(GridColumn.Installed);
        if (cfg.ShowUpdateColumn)
            columns.Add(GridColumn.Update);
        if (cfg.ShowRepoColumn)
            columns.Add(GridColumn.Repo);

        columns.Add(GridColumn.Enabled);

        if (cfg.ShowDtrColumn)
            columns.Add(GridColumn.Dtr);
        if (cfg.ShowDownloadsColumn)
            columns.Add(GridColumn.Downloads);
        if (cfg.ShowLastUpdateColumn)
            columns.Add(GridColumn.LastUpdated);
        if (cfg.ShowDalamudApiLevelColumn)
            columns.Add(GridColumn.DalamudApiLevel);
        if (cfg.ShowAuthorColumn)
            columns.Add(GridColumn.Author);
        if (cfg.ShowNotesColumn)
            columns.Add(GridColumn.Notes);
        if (cfg.ShowIgnoreColumn)
            columns.Add(GridColumn.Ignore);

        return columns.ToArray();
    }

    private static float ColumnWidth(GridColumn column)
    {
        var minimum=ColumnContentWidth(column);
        using var font=UiText.Font(UiFontRole.BodyStrong);
        var header=MaterialText.Measure(UiText.T(ColumnLabel(column))).X+2*ImGui.GetStyle().CellPadding.X+16*MaterialTheme.Metrics.Scale;
        return Math.Max(minimum,header/MaterialTheme.Metrics.Scale);
    }

    private static float ColumnContentWidth(GridColumn column) => column switch
    {
        GridColumn.Category => BotologyPresentation.Compact ? 270 : 286, GridColumn.Source => Math.Max(BotologyPresentation.Compact ? 123 : 114,new[]{"master","local override","local only","hidden master"}.Max(l=>BadgeWidth(UiText.T(l)))+2*ImGui.GetStyle().CellPadding.X/MaterialTheme.Metrics.Scale), GridColumn.AiAttribution => 64,
        GridColumn.Installed => BotologyPresentation.Compact ? 130 : 128, GridColumn.Update => Math.Max(BotologyPresentation.Compact ? 130 : 122,BadgeWidth(UiText.T("Available"),86)+2*ImGui.GetStyle().CellPadding.X/MaterialTheme.Metrics.Scale),
        GridColumn.Repo => Math.Max(135,(MaterialText.Measure(UiText.T("Repo")).X+MaterialText.Measure(UiText.T("Copy")).X+
            4*MaterialControls.Metrics.Padding.X+ImGui.GetStyle().ItemSpacing.X+2*ImGui.GetStyle().CellPadding.X)/MaterialTheme.Metrics.Scale),
        GridColumn.Enabled => BotologyPresentation.Compact ? 128 : 120, GridColumn.Dtr => BotologyPresentation.Compact ? 112 : 124, GridColumn.Downloads => 110,
        GridColumn.LastUpdated => 125, GridColumn.DalamudApiLevel => 115,
        GridColumn.Author => BotologyPresentation.Compact ? 214 : 200, GridColumn.Notes => BotologyPresentation.Compact ? 330 : 346,
        GridColumn.Ignore => Math.Max(110,(MaterialText.Measure(UiText.T("Ignore")).X+2*ImGui.GetStyle().CellPadding.X)/MaterialTheme.Metrics.Scale+20),
        _ => 100,
    };

    private static float BadgeWidth(string label,float minimum=0)
        => Math.Max(minimum,(MaterialText.Measure(label).X+2*MaterialTheme.Metrics.Gap)/MaterialTheme.Metrics.Scale);

    private static string ColumnLabel(GridColumn column) => column switch
        {
            GridColumn.Category => "Category / Plugin", GridColumn.AiAttribution => "AI",
            GridColumn.Dtr => "DTR", GridColumn.LastUpdated => "Last Update Date",
            GridColumn.DalamudApiLevel => "DalamudApiLevel", GridColumn.Notes => "Notes / Warnings",
            _ => column.ToString(),
        };

    private static void SetupColumn(GridColumn column)
    {
        var flags = ImGuiTableColumnFlags.WidthFixed;
        if (column == GridColumn.Category) flags |= ImGuiTableColumnFlags.DefaultSort;
        ImGui.TableSetupColumn(ColumnLabel(column), flags, Math.Max(1,ColumnWidth(column) * MaterialTheme.Metrics.Scale-2*ImGui.GetStyle().CellPadding.X), (uint)column + 1);
    }

    // Compare the captured values, never presentation labels or a fabricated update target version.
    private static int CompareRows(PluginAssessmentRow left, PluginAssessmentRow right, GridColumn column)
    {
        int Text(string? a, string? b) => StringComparer.OrdinalIgnoreCase.Compare(a, b);
        switch (column)
        {
            case GridColumn.Category:
                var category = Text(left.Entry.Category, right.Entry.Category);
                return category != 0 ? category : Text(left.Entry.DisplayName, right.Entry.DisplayName);
            case GridColumn.Source: return Text(left.Entry.SourceLabel, right.Entry.SourceLabel);
            case GridColumn.AiAttribution: return left.Entry.IsAiAttributed.CompareTo(right.Entry.IsAiAttributed);
            case GridColumn.Installed: return Comparer<Version>.Default.Compare(left.RuntimeState?.Version, right.RuntimeState?.Version);
            case GridColumn.Update: return (left.RuntimeState?.HasUpdate == true).CompareTo(right.RuntimeState?.HasUpdate == true);
            case GridColumn.Repo: return Text(RepositoryLinkResolver.ResolveRepoUrl(left), RepositoryLinkResolver.ResolveRepoUrl(right));
            case GridColumn.Enabled: return left.IsLoaded.CompareTo(right.IsLoaded);
            case GridColumn.Downloads: return Nullable.Compare(left.Metadata?.Downloads, right.Metadata?.Downloads);
            case GridColumn.LastUpdated: return Nullable.Compare(left.Metadata?.LastUpdateUtc, right.Metadata?.LastUpdateUtc);
            case GridColumn.DalamudApiLevel: return Nullable.Compare(left.Metadata?.DalamudApiLevel, right.Metadata?.DalamudApiLevel);
            case GridColumn.Author: return Text(left.Metadata?.Author, right.Metadata?.Author);
            case GridColumn.Notes:
                var severity = AssessmentOrder(left).CompareTo(AssessmentOrder(right));
                return severity != 0 ? severity : Text(left.Assessment.Summary, right.Assessment.Summary);
            case GridColumn.Ignore: return left.Ignored.CompareTo(right.Ignored);
            default: return 0;
        }
    }

    private static int AssessmentOrder(PluginAssessmentRow row)
        => !row.IsAssessable ? -1 : row.Ignored ? 3 : (int)row.Assessment.Severity;

    private bool? GetDtrVisibility(PluginAssessmentRow row, IReadOnlyList<DtrEntrySnapshot> entries)
    {
        if (HasDirectWritableDtrEntry(row)) return row.RuntimeState?.DtrBarEnabled;
        return row.RuntimeState != null && plugin.TryGetGlobalDtrEntry(row, entries, out var entry) && entry != null
            ? entry.UserVisible : null;
    }

    private void DrawPluginRow(
        PluginAssessmentRow row,
        GridColumn[] columns,
        IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.None, BotologyPresentation.RowHeight * MaterialTheme.Metrics.Scale);
        if (row.IsUnavailableForCurrentPatch)
        {
            var bg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.24f, 0.24f, 0.24f, 0.38f));
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, bg);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, bg);
        }

        ImGui.PushID(row.Entry.Id);

        for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            if (!ImGui.TableSetColumnIndex(columnIndex)) continue;
            var s=MaterialTheme.Metrics.Scale;
            var contentHeight=columns[columnIndex] switch
            {
                GridColumn.Source => 32*s,
                GridColumn.Installed when row.RuntimeState!=null => 34*s,
                GridColumn.Update when row.RuntimeState?.HasUpdate==true => 34*s,
                GridColumn.Enabled when row.RuntimeState!=null => 26*s,
                GridColumn.Dtr when GetDtrVisibility(row,dtrEntries).HasValue => 26*s,
                GridColumn.Installed or GridColumn.Update or GridColumn.Enabled or GridColumn.Dtr or GridColumn.Author => ImGui.GetTextLineHeight(),
                _ => 0,
            };
            if(contentHeight>0)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY()+Math.Max(0,(BotologyPresentation.RowHeight*s-contentHeight)*.5f-ImGui.GetStyle().CellPadding.Y));
            switch (columns[columnIndex])
            {
                case GridColumn.Category:
                    DrawCategoryColumn(row);
                    break;
                case GridColumn.Source:
                    DrawSourceColumn(row);
                    MeasureBounds?.Invoke("source-"+row.Entry.Id,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
                    break;
                case GridColumn.AiAttribution:
                    DrawAiAttributionColumn(row);
                    break;
                case GridColumn.Installed:
                    if (row.RuntimeState != null)
                        MaterialStatus.Badge(row.RuntimeState.Version.ToString(),new MaterialControlAppearance(BotologyPresentation.Rgb(0x184D48),BotologyPresentation.Rgb(0xA0ECE4),BotologyPresentation.Rgb(0x28635B)),new Vector2(BadgeWidth(row.RuntimeState.Version.ToString(),90),34));
                    else MaterialText.Text(UiText.T("\u2014"));
                    break;
                case GridColumn.Update:
                    if (row.RuntimeState?.HasUpdate == true)
                        MaterialStatus.Badge(UiText.T("Available"),new MaterialControlAppearance(BotologyPresentation.Rgb(0x514B24),BotologyPresentation.Rgb(0xE8DC91),BotologyPresentation.Rgb(0x8D7830)),new Vector2(BadgeWidth(UiText.T("Available"),86),34));
                    else
                        MaterialText.Text(UiText.T("\u2014"));
                    break;
                case GridColumn.Repo:
                    DrawRepoColumn(row);
                    break;
                case GridColumn.Enabled:
                    DrawEnabledColumn(row);
                    break;
                case GridColumn.Dtr:
                    DrawDtrColumn(row, dtrEntries);
                    break;
                case GridColumn.Downloads:
                    DrawDownloadsColumn(row);
                    break;
                case GridColumn.LastUpdated:
                    DrawLastUpdatedColumn(row);
                    break;
                case GridColumn.DalamudApiLevel:
                    DrawDalamudApiLevelColumn(row);
                    break;
                case GridColumn.Author:
                    DrawAuthorColumn(row);
                    break;
                case GridColumn.Notes:
                    DrawNotesColumn(row, plugin.Configuration.ShowDetailedNotes);
                    break;
                case GridColumn.Ignore:
                    DrawIgnoreColumn(row);
                    break;
            }
        }

        var nativeTable=ImGuiP.GetCurrentTable();
        MeasureBounds?.Invoke("row-"+row.Entry.Id,new Vector2(nativeTable.WorkRect.Min.X,nativeTable.RowPosY1),new Vector2(nativeTable.WorkRect.Max.X,nativeTable.RowPosY2));
        ImGui.PopID();
    }

    private void DrawCategoryColumn(PluginAssessmentRow row)
    {
        MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant, row.Entry.Category);
        var color = row.IsUnavailableForCurrentPatch
            ? new Vector4(0.58f, 0.58f, 0.58f, 1f)
            : GetAssessmentColor(row);
        using(UiText.Font(UiFontRole.PluginName))
        {
            MaterialText.TextColored(row.IsUnavailableForCurrentPatch || row.Ignored ? color : MaterialTheme.Current.Colors.OnSurface,row.Entry.DisplayName);
            MeasureBounds?.Invoke("plugin-name",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        }

        if (row.HasLocalChanges)
        {
            ImGui.SameLine();
            MaterialText.TextColored(new Vector4(1.0f, 0.84f, 0.25f, 1f), "[*]");
        }

        ImGui.SameLine();
        if (MaterialButton.IconButton("description", MaterialIcon.Info))
            ImGui.OpenPopup("PluginDescription");
        MeasureBounds?.Invoke("description-"+row.Entry.Id,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());

        ImGui.SetNextWindowSize(new Vector2(540f*MaterialTheme.Metrics.Scale, 0f));
        if (ImGui.BeginPopup("PluginDescription"))
        {
            ImGui.PushTextWrapPos(500f*MaterialTheme.Metrics.Scale);
            MaterialText.TextWrapped(GetPluginDescription(row));
            ImGui.PopTextWrapPos();
            ImGui.EndPopup();
        }
    }

    private static void DrawSourceColumn(PluginAssessmentRow row)
    {
        var color = row.Entry.SourceKind switch
        {
            CatalogEntrySourceKind.LocalOverride => new Vector4(1.0f, 0.84f, 0.25f, 1f),
            CatalogEntrySourceKind.LocalOnly => new Vector4(0.90f, 0.72f, 0.28f, 1f),
            _ => new Vector4(0.78f, 0.78f, 0.78f, 1f),
        };
        MaterialStatus.Badge(UiText.T(row.Entry.SourceLabel),BotologyPresentation.SourceBadge(color),new Vector2(BadgeWidth(UiText.T(row.Entry.SourceLabel)),32));
        if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(row.Entry.SourceLabel));
    }

    private static void DrawAiAttributionColumn(PluginAssessmentRow row)
    {
        if (row.Entry.IsAiAttributed)
            MaterialText.TextColored(new Vector4(0.72f, 0.48f, 1.0f, 1f), "AI");
        else
            MaterialText.Text(UiText.T("\u2014"));

        if (ImGui.IsItemHovered())
            MaterialText.SetTooltip(UiText.T(AiAttributionTooltip));
    }

    private void DrawRepoColumn(PluginAssessmentRow row)
    {
        var repoUrl = RepositoryLinkResolver.ResolveRepoUrl(row);
        var repoJsonUrls = RepositoryLinkResolver.ResolveRepoJsonUrls(row).ToArray();
        var repoJsonUrl = repoJsonUrls.FirstOrDefault() ?? RepositoryLinkResolver.ResolveRepoJsonUrl(row);
        var hasConcreteRepoJsonUrl = RepositoryLinkResolver.HasConcreteRepoJsonUrl(row);
        var copyText = repoJsonUrls.Length > 1
            ? string.Join(Environment.NewLine, repoJsonUrls)
            : repoJsonUrl;
        if (MaterialButton.Draw("repo", UiText.T("Repo"), 4, MaterialButtonVariant.Text, disabled: string.IsNullOrWhiteSpace(repoUrl)) && !string.IsNullOrWhiteSpace(repoUrl))
            plugin.OpenUrl(repoUrl);
        if(ImGui.GetItemRectMax().X+MaterialText.Measure(UiText.T("Copy")).X+2*MaterialControls.Metrics.Padding.X+ImGui.GetStyle().ItemSpacing.X
            <=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X)
            ImGui.SameLine();
        if (MaterialButton.Draw("copy", UiText.T("Copy"), 4, MaterialButtonVariant.Text))
        {
            ImGui.SetClipboardText(copyText);
            plugin.PrintStatus(hasConcreteRepoJsonUrl
                ? repoJsonUrls.Length > 1
                    ? $"Copied {repoJsonUrls.Length} repo feed URLs for {row.Entry.DisplayName}."
                    : $"Copied repo.json URL for {row.Entry.DisplayName}."
                : $"Copied placeholder repo.json URL for {row.Entry.DisplayName}; adjust owner, repo, or branch if needed.");
        }

        if (ImGui.IsItemHovered())
        {
            var tooltipText = hasConcreteRepoJsonUrl
                ? repoJsonUrls.Length > 1
                    ? UiText.F("Repo feeds: {0}",copyText)
                    : repoJsonUrl
                : UiText.F("Placeholder repo.json URL: {0}",repoJsonUrl);
            MaterialText.SetTooltip(tooltipText);
        }
    }

    private void DrawEnabledColumn(PluginAssessmentRow row)
    {
        if (row.RuntimeState == null)
        {
            MaterialText.Text(UiText.T("\u2014"));
            return;
        }

        var enabled = row.RuntimeState.IsLoaded;
        if (MaterialSwitch.Draw("enabled", "", ref enabled, new Vector2(44,26), disabled: !row.RuntimeState.CanToggleEnabled))
            plugin.ToggleTrackedPlugin(row.RuntimeState);
        if (ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(enabled ? "Disable plugin" : "Enable plugin"));
    }

    private void DrawDtrColumn(PluginAssessmentRow row, IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        if (HasDirectWritableDtrEntry(row) && row.RuntimeState?.DtrBarEnabled is bool dtrValue)
        {
            var dtrEnabled = dtrValue;
            if (MaterialSwitch.Draw("##DtrEnabled", "", ref dtrEnabled, new Vector2(44,26)))
                plugin.ToggleTrackedPluginDtr(row.RuntimeState, dtrEnabled);

            return;
        }

        if (row.RuntimeState == null)
        {
            MaterialText.Text(UiText.T("\u2014"));
            return;
        }

        if (plugin.TryGetGlobalDtrEntry(row, dtrEntries, out var globalDtrEntry) && globalDtrEntry != null)
        {
            var userVisible = globalDtrEntry.UserVisible;
            if (MaterialSwitch.Draw("##GlobalDtrEnabled", "", ref userVisible, new Vector2(44,26)))
                plugin.SetGlobalDtrEntryVisible(globalDtrEntry.Title, userVisible);
            if (ImGui.IsItemHovered())
                MaterialText.SetTooltip(globalDtrEntry.PluginShown
                    ? UiText.F("Global DTR: {0} ({1})",globalDtrEntry.Title,UiText.T(globalDtrEntry.StateLabel))
                    : UiText.F("Global DTR: {0} ({1}; plugin sets Shown=false)",globalDtrEntry.Title,UiText.T(globalDtrEntry.StateLabel)));

            return;
        }

        MaterialText.Text(UiText.T("\u2014"));
    }

    private static void DrawDownloadsColumn(PluginAssessmentRow row)
    {
        if (row.Metadata?.Downloads is long downloads)
            MaterialText.Text(downloads.ToString("N0", UiText.Current.Culture));
        else
            MaterialText.Text(UiText.T("\u2014"));
    }

    private static void DrawLastUpdatedColumn(PluginAssessmentRow row)
    {
        if (row.Metadata?.LastUpdateUtc is DateTimeOffset lastUpdateUtc)
            MaterialText.Text(lastUpdateUtc.ToLocalTime().ToString("d", UiText.Current.Culture));
        else
            MaterialText.Text(UiText.T("\u2014"));
    }

    private static void DrawDalamudApiLevelColumn(PluginAssessmentRow row)
    {
        if (row.Metadata?.DalamudApiLevel is int apiLevel)
            MaterialText.Text(apiLevel.ToString(UiText.Current.Culture));
        else
            MaterialText.Text(UiText.T("\u2014"));
    }

    private static void DrawAuthorColumn(PluginAssessmentRow row)
    {
        var author = row.Metadata?.Author;
        MaterialText.Text(string.IsNullOrWhiteSpace(author) ? "\u2014" : author);
    }

    private static void DrawNotesColumn(PluginAssessmentRow row, bool showDetailedNotes)
    {
        if (!row.IsAssessable)
        {
            if (row.IsUnavailableForCurrentPatch)
            {
                MaterialText.TextColored(new Vector4(0.58f, 0.58f, 0.58f, 1f), UiText.Assessment(row.Assessment));
                return;
            }

            MaterialText.Text(UiText.T("\u2014"));
            return;
        }

        var color = GetAssessmentColor(row);
        var label = row.Ignored ? "Ignored" : row.Assessment.Severity.ToString();
        var summary=UiText.Assessment(row.Assessment);
        if(BotologyPresentation.Compact)
        {
            var s=MaterialTheme.Metrics.Scale;
            var height=Math.Max(24*s,MaterialText.Measure(summary,false,Math.Max(1,ImGui.GetContentRegionAvail().X-40*s)).Y);
            if(!showDetailedNotes)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY()+Math.Max(0,(BotologyPresentation.RowHeight*s-height)*.5f-ImGui.GetStyle().CellPadding.Y));
            var p=ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddCircleFilled(p+new Vector2(12*s,12*s),12*s,MaterialCanvas.Color(color),24);
            ImGui.Dummy(new(24*s,24*s));
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(label));
            ImGui.SameLine(0,16*s);
            MaterialText.TextWrapped(summary);
        }
        else
        {
            MaterialStatus.Badge(UiText.T(label), 4, color);
            MaterialText.TextWrapped(summary);
        }

        if (showDetailedNotes && !string.IsNullOrWhiteSpace(row.Assessment.Details))
            MaterialText.TextWrapped(UiText.Details(row.Assessment));
    }

    private static Vector4 GetAssessmentColor(PluginAssessmentRow row)
    {
        if (row.Ignored)
            return new Vector4(0.40f, 0.70f, 1.0f, 1f);

        return row.Assessment.Severity switch
        {
            AssessmentSeverity.Red => new Vector4(1f, 0.35f, 0.35f, 1f),
            AssessmentSeverity.Yellow => new Vector4(1f, 0.86f, 0.35f, 1f),
            _ => new Vector4(0.45f, 0.95f, 0.45f, 1f),
        };
    }

    private void DrawIgnoreColumn(PluginAssessmentRow row)
    {
        var ignored = row.Ignored;
        if (MaterialSelection.Checkbox("##Ignore", "", ref ignored))
            plugin.SetIgnored(row.Entry.Id, ignored);
        ImGui.SameLine();
        if (MaterialButton.IconButton("rule-info", MaterialIcon.Info))
            ImGui.OpenPopup("RuleInfo");
        MeasureBounds?.Invoke("rule-"+row.Entry.Id,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());

        ImGui.SetNextWindowSize(new Vector2(520f*MaterialTheme.Metrics.Scale, 0f));
        if (ImGui.BeginPopup("RuleInfo"))
        {
            ImGui.PushTextWrapPos(480f*MaterialTheme.Metrics.Scale);
            if (row.Ignored)
                MaterialText.TextWrapped(UiText.F("Blue: ignored row. Underlying assessment is {0}: {1}",UiText.T(row.Assessment.Severity.ToString()),UiText.Assessment(row.Assessment)));
            else
                MaterialText.TextWrapped(UiText.F("{0}: {1}",UiText.T(row.Assessment.Severity.ToString()),UiText.Assessment(row.Assessment)));
            ImGui.Separator();
            MaterialText.TextWrapped(UiText.Details(row.Assessment));
            ImGui.PopTextWrapPos();
            ImGui.EndPopup();
        }
    }

    private void DrawColumnSelectionPopup()
    {
        // BeginPopup auto-sizes each frame. Wrapped text and controls need a stable
        // width before their content is measured, including after the opening frame.
        ImGui.SetNextWindowSize(new Vector2(360f*MaterialTheme.Metrics.Scale,0));
        if (!ImGui.BeginPopup(ColumnSelectionPopupId))
            return;

        DrawColumnToggle("Installed?", plugin.Configuration.ShowInstalledColumn, value => plugin.Configuration.ShowInstalledColumn = value);
        DrawColumnToggle("Update?", plugin.Configuration.ShowUpdateColumn, value => plugin.Configuration.ShowUpdateColumn = value);
        DrawColumnToggle("Repo", plugin.Configuration.ShowRepoColumn, value => plugin.Configuration.ShowRepoColumn = value);
        DrawColumnToggle("AI attribution", plugin.Configuration.ShowAiColumn, value => plugin.Configuration.ShowAiColumn = value);
        DrawColumnToggle("DTR", plugin.Configuration.ShowDtrColumn, value => plugin.Configuration.ShowDtrColumn = value);
        DrawColumnToggle("Downloads", plugin.Configuration.ShowDownloadsColumn, value => plugin.Configuration.ShowDownloadsColumn = value);
        DrawColumnToggle("Last Update Date", plugin.Configuration.ShowLastUpdateColumn, value => plugin.Configuration.ShowLastUpdateColumn = value);
        DrawColumnToggle("DalamudApiLevel", plugin.Configuration.ShowDalamudApiLevelColumn, value => plugin.Configuration.ShowDalamudApiLevelColumn = value);
        DrawColumnToggle("Author", plugin.Configuration.ShowAuthorColumn, value => plugin.Configuration.ShowAuthorColumn = value);
        DrawColumnToggle("Notes / Warnings", plugin.Configuration.ShowNotesColumn, value => plugin.Configuration.ShowNotesColumn = value);
        DrawColumnToggle("Ignore / ?", plugin.Configuration.ShowIgnoreColumn, value => plugin.Configuration.ShowIgnoreColumn = value);

        ImGui.Separator();
        MaterialText.TextWrapped(UiText.T("Category / Plugin, Source, and Enabled stay visible so the grid remains operable."));
        ImGui.EndPopup();
    }

    private void DrawBlockingAlertPopup()
    {
        if (plugin.TryGetBlockingAlert(out var message, out var shouldOpenPopup) && shouldOpenPopup)
            ImGui.OpenPopup(BlockingPopupId);

        ImGui.SetNextWindowSize(new Vector2(560f*MaterialTheme.Metrics.Scale,0));
        if (!ImGui.BeginPopupModal(BlockingPopupId, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        UiGui.Title(BlockingPopupId,UiText.T("Compatibility warning"));
        MaterialText.TextWrapped(UiText.Alert(message));
        if (UiGui.Button("OK"))
        {
            plugin.AcknowledgeBlockingAlert();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawPatchNotesPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(760f, 560f)*MaterialTheme.Metrics.Scale, ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(PatchNotesPopupId, ImGuiWindowFlags.None))
            return;
        UiGui.Title(PatchNotesPopupId,UiText.T("Patch Notes"));

        var releases = plugin.CaptureCatalogReleases();
        if (ImGui.BeginChild("##PatchNotesScroll", new Vector2(0f, -40f*MaterialTheme.Metrics.Scale), true))
        {
            if (releases.Count == 0)
            {
                MaterialText.TextWrapped(UiText.T("No patch notes available"));
            }
            else
            {
                for (var releaseIndex = 0; releaseIndex < releases.Count; releaseIndex++)
                {
                    var release = releases[releaseIndex];
                    if (releaseIndex > 0)
                        ImGui.Separator();

                    MaterialText.TextColored(MaterialTheme.Current.Colors.Primary, release.Title);
                    MaterialText.TextDisabled(release.PublishedUtc.ToLocalTime().ToString("g", UiText.Current.Culture));
                    foreach (var section in release.Sections)
                    {
                        ImGui.Spacing();
                        MaterialText.Text(section.Heading);
                        foreach (var item in section.Items)
                        {
                            ImGui.Bullet();
                            ImGui.SameLine();
                            MaterialText.TextWrapped(item);
                        }
                    }
                }
            }
        }
        ImGui.EndChild();

        if (UiGui.Button("Close"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void DrawColumnToggle(string label, bool currentValue, Action<bool> apply)
    {
        var value = currentValue;
        if (MaterialSelection.Checkbox(label, UiText.T(label), ref value))
        {
            apply(value);
            plugin.Configuration.Save();
        }
    }

    private void DrawSavedCheckbox(string label, bool currentValue, Action<bool> apply)
    {
        var value = currentValue;
        if (MaterialSelection.Checkbox(label, UiText.T(label), ref value))
        {
            apply(value);
            plugin.Configuration.Save();
        }
    }

    private static string GetPluginDescription(PluginAssessmentRow row)
    {
        var configuredDescription = row.Entry.Description?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredDescription) &&
            !configuredDescription.Equals("placeholder description", StringComparison.OrdinalIgnoreCase))
            return configuredDescription;

        var scrapedDescription = row.Metadata?.Description?.Trim();
        if (!string.IsNullOrWhiteSpace(scrapedDescription))
            return scrapedDescription;

        return UiText.T("placeholder description");
    }

    private bool MatchesFilters(PluginAssessmentRow row, IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        if (!MatchesColorFilters(row))
            return false;

        if (!string.IsNullOrWhiteSpace(categoryFilterText) &&
            !string.Equals(row.Entry.Category, categoryFilterText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(authorFilterText) &&
            !ContainsInvariant(row.Metadata?.Author, authorFilterText))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pluginNameFilterText) &&
            !ContainsInvariant(row.Entry.DisplayName, pluginNameFilterText) &&
            !row.Entry.MatchTokens.Any(token => ContainsInvariant(token, pluginNameFilterText)))
        {
            return false;
        }

        return !plugin.Configuration.ShowOnlyPluginsWithDtrEntry || HasDtrEntry(row, dtrEntries);
    }

    private bool MatchesColorFilters(PluginAssessmentRow row)
    {
        var cfg = plugin.Configuration;
        if (!cfg.ShowOnlyGreenPlugins && !cfg.ShowOnlyYellowPlugins && !cfg.ShowOnlyRedPlugins)
            return true;

        if (!row.IsAssessable || row.Ignored)
            return false;

        return row.Assessment.Severity switch
        {
            AssessmentSeverity.Green => cfg.ShowOnlyGreenPlugins,
            AssessmentSeverity.Yellow => cfg.ShowOnlyYellowPlugins,
            AssessmentSeverity.Red => cfg.ShowOnlyRedPlugins,
            _ => false,
        };
    }

    private bool HasDtrEntry(PluginAssessmentRow row, IReadOnlyList<DtrEntrySnapshot> dtrEntries)
    {
        if (HasDirectWritableDtrEntry(row))
            return true;

        return plugin.TryGetGlobalDtrEntry(row, dtrEntries, out var globalDtrEntry) && globalDtrEntry != null;
    }

    private static bool HasDirectWritableDtrEntry(PluginAssessmentRow row)
        => row.RuntimeState?.CanToggleDtr == true;

    private static bool ContainsInvariant(string? haystack, string needle)
        => !string.IsNullOrWhiteSpace(haystack) &&
           haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("g", UiText.Current.Culture) ?? "Never";
}

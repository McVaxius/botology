using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace botology.Windows;

public sealed partial class MainWindow
{
    private bool categoriesExpanded;
    private string categoryChooserSearch="";
    private readonly CategoryTrack dalamudCategories=new(),botologyCategories=new(),thirdPartyCategories=new(),selectedCategories=new();
    private enum CategorySource { Dalamud, Botology, ThirdParty }
    private readonly record struct CategoryChip(CategorySource Source,string Key,bool Qualified=false)
    {
        public string SourceCaption=>Source==CategorySource.ThirdParty?UiText.T("Third-party"):Source.ToString();
        public string IdPrefix=>Source switch { CategorySource.Botology=>"category-botology-",CategorySource.ThirdParty=>"category-third-party-",_=>"category-dalamud-" };
        public string DisplayCaption=>((Qualified?SourceCaption+": ":"")+(Source==CategorySource.Botology?UiText.T(Key):Key)).ReplaceLineEndings(" ");
    }

    private Dictionary<string,InstallerFilterMode> CategoryFilters(CategorySource source)=>source switch
        { CategorySource.Botology=>botologyCategoryFilters,CategorySource.ThirdParty=>thirdPartyCategoryFilters,_=>dalamudCategoryFilters };

    // Only this window's derived chip geometry; rebuilt when labels/font/metrics change, never saved.
    private sealed class CategoryTrack
    {
        public CategoryChip[] Items=[];
        public float[] Starts=[],Widths=[];
        public float Height,Width,ScrollRequest;
        public int FocusIndex,FocusRequest=-1;
        private (nint Font,float Size,float Scale,MaterialControlMetrics Controls,string Language) metrics;

        public void SetItems(IEnumerable<CategoryChip> items)
        {
            var next=items.ToArray();
            if(Items.AsSpan().SequenceEqual(next)) return;
            Items=next;Starts=Widths=[];Width=0;FocusIndex=Math.Clamp(FocusIndex,0,Math.Max(0,next.Length-1));FocusRequest=-1;
        }

        public unsafe void Measure()
        {
            var current=((nint)ImGui.GetFont().Handle,ImGui.GetFontSize(),MaterialTheme.Metrics.Scale,MaterialControls.Metrics,UiText.Current.Language);
            if(Starts.Length==Items.Length && current==metrics && (Items.Length==0 || Width>0)) return;
            metrics=current;Starts=new float[Items.Length];Widths=new float[Items.Length];
            var controls=MaterialControls.Metrics;var gap=controls.Gap;
            Height=controls.Height;Width=0;
            for(var index=0;index<Items.Length;index++)
            {
                var text=MaterialText.Measure(Items[index].DisplayCaption);
                Height=Math.Max(Height,text.Y+2*controls.Padding.Y);
                Starts[index]=Width;
                Widths[index]=text.X+2*controls.Padding.X+controls.IconSize+controls.Gap;
                Width+=Widths[index]+gap;
            }
            Width=Math.Max(0,Width-gap);
        }
    }

    private static bool MatchesCategoryTags(IReadOnlyList<string> tags,Dictionary<string,InstallerFilterMode> filters,bool required)
    {
        var matched=false;
        for(var index=0;index<tags.Count;index++)
            if(filters.TryGetValue(tags[index],out var mode))
            {
                if(mode==InstallerFilterMode.Blocked) return false;
                matched=true;
            }
        return !required || matched;
    }

    private void RefreshCategoryTracks()
    {
        bool Visible(string value)=>categoryChooserSearch.Length==0 || value.Contains(categoryChooserSearch,StringComparison.OrdinalIgnoreCase);
        dalamudCategories.SetItems((installerSnapshot.DalamudTags??[]).Where(Visible).Select(value=>new CategoryChip(CategorySource.Dalamud,value)));
        botologyCategories.SetItems((installerSnapshot.BotologyCategories??[]).Where(value=>Visible(value)||Visible(UiText.T(value))).Select(value=>new CategoryChip(CategorySource.Botology,value)));
        thirdPartyCategories.SetItems((installerSnapshot.ThirdPartyTags??[]).Where(Visible).Select(value=>new CategoryChip(CategorySource.ThirdParty,value)));
        RefreshSelectedCategories();
    }

    private void RefreshSelectedCategories()=>selectedCategories.SetItems(
        dalamudCategoryFilters.Keys.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).Select(value=>new CategoryChip(CategorySource.Dalamud,value,true))
            .Concat(botologyCategoryFilters.Keys.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).Select(value=>new CategoryChip(CategorySource.Botology,value,true)))
            .Concat(thirdPartyCategoryFilters.Keys.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).Select(value=>new CategoryChip(CategorySource.ThirdParty,value,true))));

    private void ClearCategoryFilters()
    {
        dalamudCategoryFilters.Clear();botologyCategoryFilters.Clear();thirdPartyCategoryFilters.Clear();RefreshSelectedCategories();ResetInstallerResults();
    }

    private void DrawInstallerCategories()
    {
        var s=MaterialTheme.Metrics.Scale;
        using var controls=MaterialControls.Push(InstallerControls());
        var metrics=MaterialControls.Metrics;
        var pad=MaterialTheme.Current.IsLcars?Math.Max(16*s,metrics.Padding.Y):metrics.Padding.Y;var gap=metrics.ItemSpacing.Y;
        var edge=ImGui.GetTextLineHeight()*.1f;
        var headerHeight=Math.Max(metrics.Height,new[]{"Categories & tags","Clear categories"}.Max(value=>MaterialText.Measure(UiText.T(value)).Y)+2*metrics.Padding.Y);
        var width=ImGui.GetContentRegionAvail().X-2*pad;
        var titleWidth=MaterialText.Measure(UiText.T("Categories & tags")).X+2*metrics.Padding.X+metrics.IconSize+metrics.Gap;
        var clearWidth=MaterialText.Measure(UiText.T("Clear categories")).X+2*metrics.Padding.X;
        var clearFits=titleWidth+clearWidth+metrics.Gap<=width;
        var searchFits=clearFits && width-titleWidth-clearWidth-2*metrics.Gap>=MaterialText.Measure(UiText.T("Find a tag...")).X+2*metrics.Padding.X+metrics.IconSize+metrics.Gap;
        var summaryFits=clearFits && width-titleWidth-clearWidth-2*metrics.Gap>=2*metrics.Height;
        if(categoriesExpanded) { dalamudCategories.Measure();botologyCategories.Measure();thirdPartyCategories.Measure(); }
        else selectedCategories.Measure();
        var trackHeight=categoriesExpanded?Math.Max(Math.Max(dalamudCategories.Height,botologyCategories.Height),thirdPartyCategories.Height):selectedCategories.Height;
        var headerRows=1+(clearFits?0:1)+(categoriesExpanded&&!searchFits?1:0);
        if(!categoriesExpanded && selectedCategories.Items.Length>0 && summaryFits) headerHeight=Math.Max(headerHeight,trackHeight+2*edge);
        var height=headerRows*headerHeight+(headerRows-1)*gap+2*pad;
        var hint=UiText.T("Left-click: require. Right-click: block. Click again to clear.");
        if(categoriesExpanded) height+=gap+3*(trackHeight+2*edge+gap)+MaterialText.Measure(hint,false,Math.Max(1,width)).Y;
        else if(selectedCategories.Items.Length>0 && !summaryFits) height+=gap+trackHeight+2*edge;
        using var surface=InstallerSurface("InstallerCategories",new(-1,height/s),outline:true,padding:pad/s,radius:metrics.Rounding/s,
            flags:ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse);
        MeasureBounds?.Invoke("category-surface",ImGui.GetWindowPos(),ImGui.GetWindowPos()+ImGui.GetWindowSize());
        var wasExpanded=categoriesExpanded;
        if(InstallerButton("Categories & tags","categories-toggle",size:new(titleWidth/s,headerHeight/s),textOnly:true,leading:categoriesExpanded?MaterialIcon.Return:MaterialIcon.ChevronDown,linkColour:false))
        { categoriesExpanded=!categoriesExpanded;restoreBrowse=true; }
        if(categoriesExpanded && searchFits)
        {
            ImGui.SameLine();
            DrawCategorySearch((width-titleWidth-clearWidth-2*metrics.Gap)/s);
        }
        if(clearFits) ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(),ImGui.GetWindowContentRegionMax().X-clearWidth));
        ImGui.BeginDisabled(dalamudCategoryFilters.Count+botologyCategoryFilters.Count+thirdPartyCategoryFilters.Count==0);
        if(InstallerButton("Clear categories","categories-clear",size:new(clearWidth/s,headerHeight/s),textOnly:true)) ClearCategoryFilters();
        ImGui.EndDisabled();
        if(categoriesExpanded && !searchFits) DrawCategorySearch(-1);
        if(categoriesExpanded!=wasExpanded) return; // Next frame reserves the new height before opening this surface.
        if(categoriesExpanded)
        {
            DrawCategoryTrack("Dalamud",dalamudCategories,true);
            DrawCategoryTrack("Botology",botologyCategories,true);
            DrawCategoryTrack("Third-party",thirdPartyCategories,true);
            using var hintStyle=new MaterialStyleScope();hintStyle.Color(ImGuiCol.Text,MaterialTheme.Current.Colors.OnSurfaceVariant);
            MaterialText.TextWrapped(hint);
            MeasureBounds?.Invoke("category-hint",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        }
        else if(selectedCategories.Items.Length>0)
        {
            // Share the header line with the summary when it fits, otherwise use a horizontally clipped second row.
            var remaining=width-titleWidth-clearWidth-2*metrics.Gap;
            if(summaryFits)
            {
                var position=ImGui.GetCursorPos();
                ImGui.SetCursorPos(new(pad+titleWidth+metrics.Gap,position.Y-headerHeight-ImGui.GetStyle().ItemSpacing.Y));
                DrawCategoryTrack("Selected",selectedCategories,false,remaining);
            }
            else DrawCategoryTrack("Selected",selectedCategories,false);
        }
    }

    private void DrawCategorySearch(float width)
    {
        if(MaterialTextField.SearchField("##CategoryChooser",ref categoryChooserSearch,BotologyPresentation.Field,UiText.T("Find a tag..."),width,maxLength:512))
            RefreshCategoryTracks();
        MeasureBounds?.Invoke("category-search",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
    }

    private void DrawCategoryTrack(string id,CategoryTrack track,bool label,float requestedWidth=0)
    {
        track.Measure();
        var s=MaterialTheme.Metrics.Scale;var origin=ImGui.GetCursorPos();
        var width=requestedWidth>0?requestedWidth:ImGui.GetContentRegionAvail().X;
        var metrics=MaterialControls.Metrics;var edge=ImGui.GetTextLineHeight()*.1f;
        var labelWidth=label?new[]{"Dalamud","Botology","Third-party"}.Max(value=>MaterialText.Measure(UiText.T(value)).X)+metrics.Gap:0;
        var overflow=track.Width>width-labelWidth;
        var arrow=metrics.Height;var childWidth=Math.Max(1,width-labelWidth-(overflow?2*arrow+2*metrics.Gap:0));
        if(label) { MaterialText.Text(UiText.T(id));ImGui.SameLine(labelWidth+origin.X); }
        if(overflow)
        {
            if(MaterialButton.IconButton("category-left-"+id,MaterialIcon.Return,size:arrow/s)) track.ScrollRequest-=childWidth*.7f;
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("Scroll categories left"));
            ImGui.SameLine(0,metrics.Gap);
        }
        ImGui.SetNextWindowContentSize(new(track.Width,track.Height));
        CategoryChip? clicked=null;var blocked=false;
        using(var style=new MaterialStyleScope())
        {
            style.Style(ImGuiStyleVar.WindowPadding,new Vector2(0,edge));
            ImGui.BeginChild("CategoryTrack-"+id,new(childWidth,track.Height+2*edge),false,ImGuiWindowFlags.HorizontalScrollbar|ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.AlwaysUseWindowPadding);
            MeasureBounds?.Invoke("category-track-"+id,ImGui.GetWindowPos(),ImGui.GetWindowPos()+ImGui.GetWindowSize());
            if(track.ScrollRequest!=0) { ImGui.SetScrollX(ImGui.GetScrollX()+track.ScrollRequest);track.ScrollRequest=0; }
            if(track.Items.Length>0 && ImGui.IsWindowFocused())
            {
                var target=track.FocusIndex;
                if(ImGui.IsKeyPressed(ImGuiKey.RightArrow)) target++;
                if(ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) target--;
                if(ImGui.IsKeyPressed(ImGuiKey.Home)) target=0;
                if(ImGui.IsKeyPressed(ImGuiKey.End)) target=track.Items.Length-1;
                target=Math.Clamp(target,0,track.Items.Length-1);
                if(target!=track.FocusIndex)
                {
                    track.FocusIndex=track.FocusRequest=target;
                    ImGui.SetScrollX(Math.Clamp(track.Starts[target],0,Math.Max(0,track.Width-childWidth)));
                }
            }
            var scroll=ImGui.GetScrollX();var first=Array.BinarySearch(track.Starts,scroll);
            first=first<0?Math.Max(0,~first-1):first;
            var end=first;
            while(end<track.Items.Length && track.Starts[end]<scroll+childWidth) end++;
            first=Math.Max(0,first-1);end=Math.Min(track.Items.Length,end+1);
            var cursor=ImGui.GetCursorPos();
            void DrawChip(int index)
            {
                var item=track.Items[index];var filters=CategoryFilters(item.Source);
                var active=filters.TryGetValue(item.Key,out var mode);var c=MaterialTheme.Current.Colors;
                var color=active?InstallerFilterColor(mode):c.OnSurface;
                ImGui.SetCursorPos(cursor+new Vector2(track.Starts[index],0));
                ImGui.PushID((int)item.Source);
                if(track.FocusRequest==index) { ImGui.SetKeyboardFocusHere();track.FocusRequest=-1; }
                var left=MaterialButton.Draw(item.Key,item.DisplayCaption,new MaterialControlAppearance(active?MaterialColor.Alpha(color,.16f):Vector4.Zero,color,active?color:c.Outline,metrics.Rounding/s),
                    new(track.Widths[index]/s,track.Height/s),leading:active?mode==InstallerFilterMode.Required?MaterialIcon.Check:MaterialIcon.Close:MaterialIcon.None);
                var right=ImGui.IsItemClicked(ImGuiMouseButton.Right);
                if(ImGui.IsItemFocused()) track.FocusIndex=index;
                if(left || right) { clicked=item;blocked=right; }
                MeasureBounds?.Invoke(item.IdPrefix+item.Key,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
                if(ImGui.IsItemHovered()) MaterialText.SetTooltip(item.SourceCaption+": "+item.Key+"\n"+InstallerFilterHelp(active?mode:null));
                ImGui.PopID();
            }
            for(var index=first;index<end;index++) DrawChip(index);
            if(track.FocusRequest>=0 && (track.FocusRequest<first || track.FocusRequest>=end)) DrawChip(track.FocusRequest);
            ImGui.SetCursorPos(cursor+new Vector2(track.Width,0));ImGui.Dummy(new(1,track.Height));
            ImGui.EndChild();
        }
        if(overflow)
        {
            ImGui.SameLine(0,metrics.Gap);
            if(MaterialButton.IconButton("category-right-"+id,MaterialIcon.ArrowRight,size:arrow/s)) track.ScrollRequest+=childWidth*.7f;
            MeasureBounds?.Invoke("category-next-"+id,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            if(ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("Scroll categories right"));
        }
        ImGui.SetCursorPos(new(origin.X,origin.Y+track.Height+2*edge+ImGui.GetStyle().ItemSpacing.Y));
        if(clicked is { } choice)
        {
            ToggleInstallerFilter(CategoryFilters(choice.Source),choice.Key,blocked?InstallerFilterMode.Blocked:InstallerFilterMode.Required);
            RefreshSelectedCategories();
        }
    }
}

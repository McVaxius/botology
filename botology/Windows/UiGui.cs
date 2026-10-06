using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace botology.Windows;

// Native widgets receive their original English labels/IDs. Only their visible label is painted in the selected locale.
// This also preserves English-derived helper IDs and existing saved window identities.
internal static class UiGui
{
    private static void Label(string original,Vector2 position,Vector4 background,Vector4 foreground,Vector2? clip=null,string? display=null)
    {
        var visible=original.Split("##",2)[0];
        var translated=display ?? UiText.T(visible);
        if(translated==visible && !MaterialText.RequiresShaping(translated)) return;
        var dl=ImGui.GetWindowDrawList();
        var width=Math.Max(MaterialText.Measure(visible).X,MaterialText.Measure(translated).X);
        var height=Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(translated).Y);
        if(clip is { } max) dl.PushClipRect(position,max,true);
        try
        {
        dl.AddRectFilled(position,position+new Vector2(width,height),ImGui.ColorConvertFloat4ToU32(background));
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(dl,position,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { if(clip.HasValue) dl.PopClipRect(); }
    }
    internal static bool Button(string label,string? display=null)
    {
        var translated=display ?? UiText.T(label.Split("##",2)[0]);
        using var height=MaterialText.PushLineHeight(translated);
        var width=MaterialLayout.FitNextItemWidth(0,MaterialText.Measure(translated).X+2*ImGui.GetStyle().FramePadding.X);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Button(label,new Vector2(width,0));
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax();
        foreground.W*=ImGui.GetStyle().Alpha;
        ImGui.GetWindowDrawList().PushClipRect(min,max,true);
        try { MaterialText.AddText(ImGui.GetWindowDrawList(),min+(max-min-MaterialText.Measure(translated))*.5f,ImGui.ColorConvertFloat4ToU32(foreground),translated); }
        finally { ImGui.GetWindowDrawList().PopClipRect(); }
        return clicked;
    }
    internal static bool SmallButton(string label,string? display=null)
    {
        // Native small buttons use the same ID and behavior with zero vertical padding.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,0));
        try { return Button(label,display); }
        finally { ImGui.PopStyleVar(); }
    }
    internal static bool Checkbox(string label,ref bool value)
    {
        var visible=label.Split("##",2)[0];
        var translated=UiText.T(visible);
        using var height=MaterialText.PushLineHeight(translated);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var gap=ImGui.GetStyle().ItemInnerSpacing;
        MaterialLayout.FitNextItemWidth(0,ImGui.GetFrameHeight()+gap.X+Math.Max(MaterialText.Measure(visible).X,MaterialText.Measure(translated).X));
        // Native Checkbox sizes its hit area from the original label. Adjust that size for the
        // translated ink while keeping the native widget and its original ID.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(Math.Max(0,gap.X+MaterialText.Measure(translated).X-MaterialText.Measure(visible).X),gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var changed=ImGui.Checkbox(label,ref value);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var p=ImGui.GetItemRectMin()+new Vector2(ImGui.GetFrameHeight()+gap.X,
            MaterialText.RequiresShaping(translated) ? (ImGui.GetFrameHeight()-MaterialText.Measure(translated).Y)*.5f : ImGui.GetStyle().FramePadding.Y);
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(),p,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        return changed;
    }
    internal static bool Selectable(string original,bool selected,string? display=null)
    {
        var translated=display ?? UiText.T(original.Split("##",2)[0]);
        if(MaterialText.RequiresShaping(translated))
        {
            var available=ImGui.GetContentRegionAvail().X;
            var pressed=MaterialText.Selectable(original,selected,display:translated);
            if(MaterialText.Measure(translated).X>available && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
            return pressed;
        }
        var origin=ImGui.GetCursorScreenPos();
        var width=ImGui.GetContentRegionAvail().X;
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Selectable(original,selected,ImGuiSelectableFlags.None,new Vector2(0,ImGui.GetTextLineHeight()));
        ImGui.PopStyleColor();
        foreground.W*=ImGui.GetStyle().Alpha;
        var dl=ImGui.GetWindowDrawList();
        dl.PushClipRect(origin,origin+new Vector2(width,ImGui.GetTextLineHeight()),true);
        MaterialText.AddText(dl,origin,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        dl.PopClipRect();
        if(MaterialText.Measure(translated).X>width && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
        return clicked;
    }
    private static void BeginField(string label,bool hasSteps=false,float previewWidth=0)
    {
        var requested=ImGui.CalcItemWidth();
        var minimum=Math.Max(80*MaterialTheme.Metrics.Scale,MaterialText.Measure("00000000").X+2*ImGui.GetStyle().FramePadding.X);
        if(hasSteps) minimum+=2*(ImGui.GetFrameHeight()+ImGui.GetStyle().ItemInnerSpacing.X);
        minimum=MathF.Ceiling(Math.Max(minimum,previewWidth));
        var visible=label.Split("##",2)[0];
        if(visible.Length!=0) MaterialText.Text(UiText.T(visible));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(requested,minimum));
        // An empty label removes hidden English layout width while retaining the original native ID root.
        ImGuiP.PushOverrideID(ImGui.GetID(label));
    }
    internal static bool InputText(string label,ref string value,int length)
    {
        BeginField(label);
        try
        {
            using var height=MaterialText.PushLineHeight(value);
            return MaterialShapedInput.SingleLine("","",ref value,length);
        }
        finally { ImGui.PopID(); }
    }
    internal static bool InputTextWithHint(string label,string hint,ref string value,int length)
    {
        using var height=MaterialText.PushLineHeight(value,hint);
        return MaterialShapedInput.SingleLine(label,hint,ref value,length);
    }
    internal static bool InputInt(string label,ref int value,int step=0,int fastStep=0) { BeginField(label,step>0);var changed=ImGui.InputInt("",ref value,step,fastStep);ImGui.PopID();return changed; }
    internal static bool Combo(string label,ref int value,string[] options,int count)
    {
        var preview=value>=0 && value<count?UiText.T(options[value]):"";
        BeginField(label,previewWidth:MaterialText.Measure(preview).X+ImGui.GetFrameHeight()+2*ImGui.GetStyle().FramePadding.X);
        try
        {
            var changed=false;
            if(!MaterialText.BeginCombo("",preview)) return false;
            try
            {
                for(var index=0;index<count;index++)
                {
                    ImGui.PushID(index);
                    try
                    {
                        if(Selectable(options[index],value==index)) { changed=value!=index;value=index; }
                        if(value==index) ImGui.SetItemDefaultFocus();
                    }
                    finally { ImGui.PopID(); }
                }
            }
            finally { ImGui.EndCombo(); }
            return changed;
        }
        finally { ImGui.PopID(); }
    }
    internal static void Title(string original,string translated)
    {
        var s=ImGui.GetStyle(); var size=ImGui.GetFontSize();var height=ImGui.GetFrameHeight();
        var flags=ImGuiP.GetCurrentWindow().Flags;
        var collapseOnLeft=(flags & (ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Modal))==0 && s.WindowMenuButtonPosition==ImGuiDir.Left;
        var position=ImGui.GetWindowPos()+new Vector2(s.FramePadding.X+(collapseOnLeft?size+s.ItemInnerSpacing.X:0),s.FramePadding.Y);
        var originalWidth=MaterialText.Measure(original).X;
        using var font=UiText.Font(UiFontRole.Body);
        var translatedWidth=MaterialText.Measure(translated).X*size/ImGui.GetFontSize();
        var dl=ImGui.GetWindowDrawList();
        var rightButtons=size+s.FramePadding.X*2;
        if((flags & ImGuiWindowFlags.NoCollapse)==0 && s.WindowMenuButtonPosition==ImGuiDir.Right)
            rightButtons+=size+s.ItemInnerSpacing.X;
        dl.PushClipRect(position,ImGui.GetWindowPos()+new Vector2(Math.Max(0,ImGui.GetWindowSize().X-rightButtons),height),false);
        try
        {
        var bg=s.Colors[(int)(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)?ImGuiCol.TitleBgActive:ImGuiCol.TitleBg)];
        dl.AddRectFilled(position,position+new Vector2(Math.Max(originalWidth,translatedWidth),height-s.FramePadding.Y),ImGui.ColorConvertFloat4ToU32(bg));
        MaterialText.AddText(dl,ImGui.GetFont(),size,position,ImGui.ColorConvertFloat4ToU32(s.Colors[(int)ImGuiCol.Text]),translated);
        }
        finally { dl.PopClipRect(); }
    }
    internal static void TableHeadersRow(float height=0)
    {
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            var translated=UiText.T(ImGui.TableGetColumnName(index));
            if(MaterialText.RequiresShaping(translated))
                height=Math.Max(height,MaterialText.Measure(translated).Y+2*ImGui.GetStyle().CellPadding.Y);
        }
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers,height);
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            if(!ImGui.TableSetColumnIndex(index)) continue;
            var original=ImGui.TableGetColumnName(index);
            var position=ImGui.GetCursorScreenPos();
            var available=ImGui.GetContentRegionAvail().X;
            ImGui.TableHeader(original);
            var translated=UiText.T(original);
            Label(original,position,ImGui.GetStyle().Colors[(int)ImGuiCol.TableHeaderBg],ImGui.GetStyle().Colors[(int)ImGuiCol.Text],position+new Vector2(Math.Max(1,available),Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(translated).Y)));
            if(translated!=original && MaterialText.Measure(translated).X>available-16*AethertekUI.MaterialTheme.Metrics.Scale && ImGui.IsItemHovered())
                MaterialText.SetTooltip(translated);
        }
    }
}

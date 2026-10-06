using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace Krangler.Windows;

// Keep the ORIGINAL label passed to native ImGui. Translated ink is painted into the native item's measured bounds.
// This retains English-derived IDs, popup identity, selection, editing, focus and navigation behavior.
internal static class UiGui
{
    internal static void Text(string text) => MaterialText.Text(UiText.T(text));
    internal static void Text(FormattableString text) => MaterialText.Text(UiText.F(text));
    internal static void TextUnformatted(string text) => MaterialText.Text(UiText.T(text));
    internal static void TextWrapped(string text) => MaterialText.TextWrapped(UiText.T(text));
    internal static void TextDisabled(string text) { ImGui.PushTextWrapPos(0);try { MaterialText.TextDisabled(UiText.T(text)); } finally { ImGui.PopTextWrapPos(); } }
    internal static void TextColored(Vector4 color,string text) { ImGui.PushTextWrapPos(0);try { MaterialText.TextColored(color,UiText.T(text)); } finally { ImGui.PopTextWrapPos(); } }
    internal static void BulletText(string text) => MaterialText.BulletText(UiText.T(text));
    internal static void SetTooltip(string text) { ImGui.BeginTooltip();ImGui.PushTextWrapPos(Math.Min(560*MaterialTheme.Metrics.Scale,ImGui.GetMainViewport().WorkSize.X*.8f));try { MaterialText.Text(UiText.T(text)); } finally { ImGui.PopTextWrapPos();ImGui.EndTooltip(); } }
    private static string Visible(string label) => UiText.T(label.Split("##",2)[0]);
    private static void Ink(string label,Vector2 position,Vector2 min,Vector2 max)
    {
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(min,max,true);
        try { MaterialText.AddText(dl,position,ImGui.GetColorU32(ImGuiCol.Text),label); }
        finally { dl.PopClipRect(); }
    }
    internal static bool Button(string original,Vector2 size=default,string? display=null)
    {
        var translated=display ?? Visible(original);
        using var height = MaterialText.PushLineHeight(translated);
        if (MaterialText.RequiresShaping(translated) && size.Y > 0)
            size.Y = Math.Max(size.Y, MaterialText.Measure(translated).Y + ImGui.GetStyle().FramePadding.Y * 2);
        var natural=MaterialText.Measure(translated).X+ImGui.GetStyle().FramePadding.X*2;
        size.X=MaterialLayout.FitNextItemWidth(size.X,Math.Max(size.X,natural));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Button(original,size);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        Ink(translated,min+(max-min-MaterialText.Measure(translated))*.5f,min,max);
        if (MaterialText.Measure(translated).X>max.X-min.X-ImGui.GetStyle().FramePadding.X*2 && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
        return clicked;
    }
    internal static bool SmallButton(string label)
    { using var padding = new MaterialStyleScope();padding.Style(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,0));return Button(label); }
    internal static bool Checkbox(string label,ref bool value,string? display=null)
        => CheckboxCore(label,ref value,display,null);
    internal static bool Checkbox(string label,ref bool value,string? display,UiFontRole labelRole)
        => CheckboxCore(label,ref value,display,labelRole);
    private static bool CheckboxCore(string label,ref bool value,string? display,UiFontRole? labelRole)
    {
        var translated=UiText.T(display ?? label.Split("##",2)[0]);var padding=ImGui.GetStyle().FramePadding;var gap=ImGui.GetStyle().ItemInnerSpacing;
        var original=label.Split("##",2)[0];
        Vector2 textSize;
        using (labelRole is { } role?UiText.Font(role):null) textSize=MaterialText.Measure(translated);
        using var shapedHeight = new MaterialStyleScope();
        if (MaterialText.RequiresShaping(translated) && textSize.Y > ImGui.GetTextLineHeight())
            shapedHeight.Style(ImGuiStyleVar.FramePadding, new Vector2(padding.X, padding.Y + (textSize.Y - ImGui.GetTextLineHeight()) * .5f));
        var height=ImGui.GetFrameHeight();var colors=MaterialTheme.Current.Colors;
        MaterialLayout.FitNextItemWidth(0,height+gap.X+textSize.X);
        ImGui.PushStyleColor(ImGuiCol.FrameBg,value?colors.Primary:colors.SurfaceContainerLowest);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,MaterialColor.Layer(value?colors.Primary:colors.SurfaceContainerLowest,colors.OnSurface,.08f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,MaterialColor.Layer(value?colors.Primary:colors.SurfaceContainerLowest,colors.OnSurface,.14f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark,colors.OnPrimary);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize,MaterialTheme.Metrics.Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(Math.Max(0,gap.X+textSize.X-MaterialText.Measure(original).X),gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.Checkbox(label,ref value);ImGui.PopStyleColor();ImGui.PopStyleVar();
        ImGui.PopStyleVar();ImGui.PopStyleColor(4);
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        var p=min+new Vector2(height+gap.X,(height-textSize.Y)*.5f);
        using (labelRole is { } inkRole?UiText.Font(inkRole):null)
            MaterialText.AddText(ImGui.GetWindowDrawList(),p,ImGui.GetColorU32(ImGuiCol.Text),translated);
        return changed;
    }
    internal static bool RadioButton(string label,bool selected,string? display=null)
    {
        var translated=UiText.T(display ?? label.Split("##",2)[0]);var original=label.Split("##",2)[0];var gap=ImGui.GetStyle().ItemInnerSpacing;
        using var height = MaterialText.PushLineHeight(translated);
        MaterialLayout.FitNextItemWidth(0,ImGui.GetFrameHeight()+gap.X+MaterialText.Measure(translated).X);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(Math.Max(0,gap.X+MaterialText.Measure(translated).X-MaterialText.Measure(original).X),gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.RadioButton(label,selected);ImGui.PopStyleColor();ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin();var p=min+new Vector2(ImGui.GetFrameHeight()+gap.X,ImGui.GetStyle().FramePadding.Y);
        Ink(translated,p,min,new Vector2(p.X+MaterialText.Measure(translated).X,ImGui.GetItemRectMax().Y));return changed;
    }
    internal static bool CollapsingHeader(string label,ImGuiTreeNodeFlags flags=ImGuiTreeNodeFlags.None)
    {
        using var height = MaterialText.PushLineHeight(Visible(label));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.CollapsingHeader(label,flags);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();var padding=ImGui.GetStyle().FramePadding;
        MaterialIcons.Draw(open?MaterialIcon.ChevronDown:MaterialIcon.ArrowRight,min+padding,ImGui.GetFontSize(),MaterialTheme.Current.Colors.OnSurface,ImGui.GetStyle().Alpha);
        Ink(Visible(label),min+new Vector2(ImGui.GetFontSize()+padding.X*2,padding.Y),min,max);return open;
    }
    internal static bool TreeNode(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.TreeNode(label);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        MaterialIcons.Draw(open?MaterialIcon.ChevronDown:MaterialIcon.ArrowRight,min,ImGui.GetFontSize(),MaterialTheme.Current.Colors.OnSurface,ImGui.GetStyle().Alpha);
        var p=min+new Vector2(ImGui.GetFontSize()+ImGui.GetStyle().FramePadding.X*2,0);
        Ink(Visible(label),p,min,new Vector2(p.X+MaterialText.Measure(Visible(label)).X,max.Y));return open;
    }
    internal static bool BeginTabItem(string label,ImGuiTabItemFlags flags=ImGuiTabItemFlags.None)
    {
        var translated=Visible(label);
        using var height = MaterialText.PushLineHeight(translated);
        ImGui.SetNextItemWidth(MaterialText.Measure(translated).X+ImGui.GetStyle().FramePadding.X*2+12*MaterialTheme.Metrics.Scale);
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.BeginTabItem(label,flags);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();Ink(translated,min+(max-min-MaterialText.Measure(translated))*.5f,min,max);return open;
    }
    internal static bool BeginPrimaryTabItem(string label,ImGuiTabItemFlags flags,MaterialIcon icon,float width)
    {
        var scale=MaterialTheme.Metrics.Scale;using var font=UiText.Font(UiFontRole.BodyStrong);
        var height=(KranglerPresentation.Compact?40:44)*scale;
        if (MaterialText.RequiresShaping(Visible(label))) height = Math.Max(height, MaterialText.Measure(Visible(label)).Y + ImGui.GetStyle().FramePadding.Y * 2);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(12*scale,Math.Max(0,(height-ImGui.GetTextLineHeight())*.5f)));
        ImGui.SetNextItemWidth(width);
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.BeginTabItem(label,flags);ImGui.PopStyleColor();ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();var text=Visible(label);var textSize=MaterialText.Measure(text);
        var p=min+new Vector2(Math.Max(4*scale,(max.X-min.X-textSize.X-30*scale)*.5f),(max.Y-min.Y-24*scale)*.5f);
        var color=MaterialTheme.Current.Colors.OnSurface;
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(min,max,true);MaterialIcons.Draw(icon,p,24*scale,color);
        MaterialText.AddText(dl,p+new Vector2(32*scale,(24*scale-textSize.Y)*.5f),ImGui.GetColorU32(ImGuiCol.Text),text);dl.PopClipRect();
        if (textSize.X+30*scale>max.X-min.X && ImGui.IsItemHovered()) MaterialText.SetTooltip(text);
        return open;
    }

    internal static bool BeginCombo(string label,string preview,ImGuiComboFlags flags=ImGuiComboFlags.None,bool translatePreview=true)
        => BeginComboCore(label,preview,flags,translatePreview,0);
    private static bool BeginComboCore(string label,string preview,ImGuiComboFlags flags,bool translatePreview,float minimum,bool showLabel=true)
    {
        var shown=translatePreview?UiText.T(preview):preview;
        minimum=Math.Max(minimum,Math.Max(80*MaterialTheme.Metrics.Scale,MaterialText.Measure(shown).X+ImGui.GetFrameHeight()+2*ImGui.GetStyle().FramePadding.X));
        using var height = MaterialText.PushLineHeight(MaterialText.RequiresShaping(shown) || MaterialText.RequiresShaping(label) || !showLabel ? "" : Visible(label));
        var field=FitField(label,minimum,showLabel);
        bool open;
        try { open=MaterialText.BeginCombo(label,shown,flags); }
        finally { field.Drawing.PopClipRect(); }
        try { FieldLabel(field); }
        catch { if (open) ImGui.EndCombo(); throw; }
        if(!open && MaterialText.Measure(shown).X>field.Width-ImGui.GetFrameHeight() && ImGui.IsItemHovered()) MaterialText.SetTooltip(shown);
        return open;
    }
    internal static bool Selectable(string label,bool selected=false,ImGuiSelectableFlags flags=ImGuiSelectableFlags.None,Vector2 size=default)
    {
        var translated = Visible(label);
        if (MaterialText.RequiresShaping(translated)) size.Y = Math.Max(size.Y, MaterialText.Measure(translated).Y);
        var origin=ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.Selectable(label,selected,flags,size);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();Ink(Visible(label),origin,min,max);
        if (MaterialText.Measure(Visible(label)).X>max.X-min.X && ImGui.IsItemHovered()) MaterialText.SetTooltip(Visible(label));return changed;
    }
    private static (Vector2 Min,Vector2 PreviousMax,float Width,string Label,ImGuiWindowPtr Window,ImDrawListPtr Drawing) FitField(string label,float minimum,bool showLabel=true,float? preferred=null)
    {
        var requested=preferred ?? ImGui.CalcItemWidth();
        var translated=showLabel?Visible(label):"";
        var gap=translated.Length>0?ImGui.GetStyle().ItemInnerSpacing.X:0;
        var labelWidth=MaterialText.Measure(translated).X;minimum=MathF.Ceiling(minimum);
        MaterialLayout.FitNextItemWidth(requested+labelWidth+gap,minimum+labelWidth+gap);
        var available=ImGui.GetContentRegionAvail().X;
        if(labelWidth+gap+minimum>available && translated.Length>0)
        {
            MaterialText.Text(translated);translated="";labelWidth=0;gap=0;
            available=ImGui.GetContentRegionAvail().X;
        }
        var width=MaterialLayout.FitNextItemWidth(Math.Min(requested,available-labelWidth-gap),minimum);
        ImGui.SetNextItemWidth(width);var min=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var drawing=ImGui.GetWindowDrawList();
        var previousMax=parent.DC.CursorMaxPos;
        // Native IDs and field contents remain exact; the translated label owns its visible layout.
        var window=ImGui.GetWindowPos();
        drawing.PushClipRect(new Vector2(min.X,window.Y),new Vector2(min.X+width,window.Y+ImGui.GetWindowSize().Y),true);
        return (min,previousMax,width,translated,parent,drawing);
    }
    private static void FieldLabel((Vector2 Min,Vector2 PreviousMax,float Width,string Label,ImGuiWindowPtr Window,ImDrawListPtr Drawing) field)
    {
        var right=field.Min.X+field.Width;
        if(field.Label.Length>0)
        {
            var p=new Vector2(right+ImGui.GetStyle().ItemInnerSpacing.X,field.Min.Y+ImGui.GetStyle().FramePadding.Y);
            MaterialText.AddText(field.Drawing,p,ImGui.GetColorU32(ImGuiCol.Text),field.Label);right=p.X+MaterialText.Measure(field.Label).X;
        }
        field.Window.DC.CursorMaxPos=new Vector2(Math.Max(field.PreviousMax.X,right),field.Window.DC.CursorMaxPos.Y);
        field.Window.DC.CursorPosPrevLine=new Vector2(right,field.Window.DC.CursorPosPrevLine.Y);
    }
    internal static bool InputText(string label,ref string value,int length,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None,bool showLabel=true)
    {
        using var height = MaterialText.PushLineHeight(value, showLabel ? Visible(label) : "");
        var field=FitField(label,TextMinimum(length),showLabel);
        bool changed;
        try { changed=MaterialShapedInput.SingleLine(label,"",ref value,length,flags); }
        finally { field.Drawing.PopClipRect(); }
        FieldLabel(field);
        return changed;
    }
    internal static bool InputText(string label,ref string value,int length,ImGuiInputTextFlags flags,bool showLabel,float minimum)
    {
        using var height = MaterialText.PushLineHeight(value, showLabel ? Visible(label) : "");
        var field=FitField(label,minimum,showLabel);
        bool changed;
        try { changed=MaterialShapedInput.SingleLine(label,"",ref value,length,flags); }
        finally { field.Drawing.PopClipRect(); }
        FieldLabel(field);
        return changed;
    }
    internal static bool InputTextWithHint(string label,string hint,ref string value,int length,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    {
        using var height = MaterialText.PushLineHeight(value, UiText.T(hint), Visible(label));
        var field=FitField(label,TextMinimum(length));bool changed;
        try { changed=MaterialShapedInput.SingleLine(label,UiText.T(hint),ref value,length,flags); }
        finally { field.Drawing.PopClipRect(); }
        FieldLabel(field);return changed;
    }
    internal static bool InputTextMultiline(string label,ref string value,int length,Vector2 size,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    { var field=FitField(label,TextMinimum(length),preferred:size.X>0?size.X:ImGui.CalcItemWidth());size.X=field.Width;bool changed;try { changed=ImGui.InputTextMultiline(label,ref value,length,size,flags); }finally { field.Drawing.PopClipRect(); }FieldLabel(field);return changed; }
    internal static bool InputInt(string label,ref int value,int step=0,int fastStep=0,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    { using var height=MaterialText.PushLineHeight(Visible(label));var field=FitField(label,NumberMinimum(step>0));bool changed;try { changed=ImGui.InputInt(label,ref value,step,fastStep,"%d",flags); }finally { field.Drawing.PopClipRect(); }FieldLabel(field);return changed; }
    internal static bool SliderInt(string label,ref int value,int min,int max,string format="%d",ImGuiSliderFlags flags=ImGuiSliderFlags.None)
    { using var height=MaterialText.PushLineHeight(Visible(label));var field=FitField(label,NumberMinimum(false));bool changed;try { changed=ImGui.SliderInt(label,ref value,min,max,UiText.T(format),flags); }finally { field.Drawing.PopClipRect(); }FieldLabel(field);return changed; }
    internal static float TextMinimum(int length) => MathF.Ceiling(Math.Max((length<=8?44:80)*MaterialTheme.Metrics.Scale,MaterialText.Measure(new string('0',Math.Clamp(length-1,3,10))).X+2*ImGui.GetStyle().FramePadding.X));
    internal static float NumberMinimum(bool steps) => MathF.Ceiling(Math.Max(80*MaterialTheme.Metrics.Scale,MaterialText.Measure("-000000").X+2*ImGui.GetStyle().FramePadding.X))+(steps?2*(ImGui.GetFrameHeight()+ImGui.GetStyle().ItemInnerSpacing.X):0);
    internal static bool Combo(string label,ref int value,string[] options,int count)
        => ComboCore(label,ref value,options,count,true);
    private static bool ComboCore(string label,ref int value,string[] options,int count,bool showLabel)
    {
        var changed=false;
        var minimum=options.Take(count).Select(option=>MaterialText.Measure(UiText.T(option)).X).DefaultIfEmpty(0).Max()+ImGui.GetFrameHeight()+2*ImGui.GetStyle().FramePadding.X;
        if (BeginComboCore(label,value>=0 && value<count?options[value]:string.Empty,ImGuiComboFlags.None,true,minimum,showLabel))
        {
            for(var index=0;index<count;index++)
            { ImGui.PushID(index);if (Selectable(options[index],value==index)) { changed=value!=index;value=index; }if (value==index) ImGui.SetItemDefaultFocus();ImGui.PopID(); }
            ImGui.EndCombo();
        }
        return changed;
    }
    internal static bool Combo(string label,ref int value,string options)
    { var items=options.Split('\0').Where(v=>v.Length>0).ToArray();return Combo(label,ref value,items,items.Length); }
    internal static bool ComboWithoutLabel(string label,ref int value,string options)
    { var items=options.Split('\0').Where(v=>v.Length>0).ToArray();return ComboCore(label,ref value,items,items.Length,false); }
    internal static bool BeginPopupModal(string original,ImGuiWindowFlags flags)
    {
        ImGui.SetNextWindowSize(new Vector2(520*MaterialTheme.Metrics.Scale,0),ImGuiCond.Always);
        var open=ImGui.BeginPopupModal(original,flags);if(open) Title(original.Split("##",2)[0]);return open;
    }
    internal static void Title(string original,string? display=null)
    {
        var translated=display ?? UiText.T(original);if (translated==original) return;
        var style=ImGui.GetStyle();var fontSize=ImGui.GetFontSize();var height=ImGui.GetFrameHeight();
        var flags=ImGuiP.GetCurrentWindow().Flags;
        var collapseLeft=(flags & (ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Modal))==0 && style.WindowMenuButtonPosition==ImGuiDir.Left;
        var p=ImGui.GetWindowPos()+new Vector2(style.FramePadding.X+(collapseLeft?fontSize+style.ItemInnerSpacing.X:0),style.FramePadding.Y);
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(ImGui.GetWindowPos(),ImGui.GetWindowPos()+new Vector2(ImGui.GetWindowSize().X-2*height,height),false);
        try
        {
        var background=style.Colors[(int)(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)?ImGuiCol.TitleBgActive:ImGuiCol.TitleBg)];
        dl.AddRectFilled(p,p+new Vector2(Math.Max(MaterialText.Measure(original).X,MaterialText.Measure(translated).X),height-style.FramePadding.Y),ImGui.ColorConvertFloat4ToU32(background));
        MaterialText.AddText(dl,p,ImGui.GetColorU32(ImGuiCol.Text),translated);
        }
        finally { dl.PopClipRect(); }
    }

    internal static void TableHeadersRow()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            if(!ImGui.TableSetColumnIndex(index)) continue;
            TableHeader(ImGui.TableGetColumnName(index));
        }
    }
    internal static void TableHeader(string original)
    {
        var translated=UiText.T(original);
        using var height = MaterialText.PushLineHeight(translated);
        var p=ImGui.GetCursorScreenPos();var width=ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);ImGui.TableHeader(original);ImGui.PopStyleColor();
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(p,p+new Vector2(Math.Max(1,width),Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(translated).Y)),true);
        try { MaterialText.AddText(dl,p,ImGui.GetColorU32(ImGuiCol.Text),translated); }
        finally { dl.PopClipRect(); }
        if(MaterialText.Measure(translated).X>width && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
    }
}

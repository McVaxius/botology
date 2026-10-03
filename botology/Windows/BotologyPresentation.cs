using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace botology.Windows;

internal enum UiFontRole { Body, BodyStrong, Title, PluginName, Counter, Action }

internal static class BotologyPresentation
{
    private const uint ReferenceAccent=0x1CC9E6;
    internal const float ContentWidth=1440, ContentHeight=942, HeaderHeight=72, ActionHeight=52,
        StatusHeight=64, FilterHeight=115, TableHeaderHeight=44, RowHeight=74, FooterHeight=52, RegionGap=20;
    internal static readonly float[] FontSizes=[16,16,38,20,22,18];
    // Reference sizes are em sizes. Segoe's ascent/descent span is about 4/3 em;
    // ImGui's TTF loader takes that full pixel height rather than a CSS/Windows em size.
    internal static float AtlasHeight(UiFontRole role) => FontSizes[(int)role]*4/3;
    internal static readonly string[] FontFiles=["segoeui.ttf","seguisb.ttf","segoeuib.ttf","seguisb.ttf","seguisb.ttf","seguisb.ttf"];
    private static bool IsReferenceTheme => MaterialTheme.Current.Colors.Primary==Rgb(ReferenceAccent);
    internal static Vector4 PanelTop => IsReferenceTheme ? Rgb(0x192B39)
        : MaterialColor.Layer(MaterialTheme.Current.Colors.SurfaceContainerLow,MaterialTheme.Current.Colors.OnSurface,.025f);
    internal static readonly Vector4 GreenCounter=Rgb(0x57CE7D),YellowCounter=Rgb(0xFEDC5C),RedCounter=Rgb(0xFD6876);
    internal static Vector4 VisibleCounter => IsReferenceTheme ? Rgb(0x486077)
        : MaterialColor.Layer(MaterialTheme.Current.Colors.Outline,MaterialTheme.Current.Colors.OnSurfaceVariant,.125f);
    internal static Vector4 Rgb(uint rgb) => new(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,1);
    internal static MaterialTheme Theme(uint accent)
    {
        accent &= 0xFFFFFF;
        var selectedRgb=Rgb(accent);
        var referenceRgb=Rgb(ReferenceAccent);
        var selected=MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(selectedRgb.X,selectedRgb.Y,selectedRgb.Z)));
        var reference=MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(referenceRgb.X,referenceRgb.Y,referenceRgb.Z)));
        var hueShift=selected.Y<.001f ? 0 : selected.Z-reference.Z;
        var chromaScale=selected.Y<.001f ? 0 : selected.Y/reference.Y;
        // Keep each measured role's perceptual lightness and relative hue/chroma.
        // Seed brightness never makes a dark surface or its foreground unreadable.
        Vector4 RelativeColor(Vector4 color)
        {
            if(accent==ReferenceAccent) return color;
            var lch=MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(color.X,color.Y,color.Z)));
            return new(MaterialColor.GamutMap(lch.X,lch.Y*chromaScale,lch.Z+hueShift),1);
        }
        Vector4 Relative(uint rgb) => RelativeColor(Rgb(rgb));
        var palette=new OklchPaletteGenerator().Generate(new(.11f,.79f,.90f));
        var referenceColors=new MaterialColorScheme(palette);
        var background=Relative(0x111E2A);
        var foreground=Relative(0xDFECFC);
        var primary=Relative(ReferenceAccent);
        var colors=new MaterialColorScheme(palette)
        {
            Background=background, Surface=Relative(0x152633), SurfaceContainerLowest=Relative(0x11202D),
            SurfaceContainerLow=Relative(0x152633), SurfaceContainer=Relative(0x152633), SurfaceContainerHigh=Relative(0x1B2C3C),
            SurfaceContainerHighest=Relative(0x1B2C3C), SurfaceVariant=Relative(0x2C4355),
            Outline=Relative(0x3A5267), OutlineVariant=Relative(0x2C4355), OnSurface=foreground,
            OnSurfaceVariant=Relative(0xADCAE1), OnBackground=foreground, SecondaryContainer=Relative(0x1B2C3C),
            OnSecondaryContainer=foreground, Primary=primary,
            OnPrimary=accent==ReferenceAccent ? Rgb(0xF2FBFF)
                : (MaterialColor.Contrast(primary,background)>=MaterialColor.Contrast(primary,foreground) ? background : foreground),
            PrimaryContainer=Relative(0x154456), OnPrimaryContainer=Relative(0xC0F5FF),
            Secondary=RelativeColor(referenceColors.Secondary), OnSecondary=RelativeColor(referenceColors.OnSecondary),
            Tertiary=RelativeColor(referenceColors.Tertiary), OnTertiary=RelativeColor(referenceColors.OnTertiary),
            TertiaryContainer=RelativeColor(referenceColors.TertiaryContainer), OnTertiaryContainer=RelativeColor(referenceColors.OnTertiaryContainer),
            InverseSurface=RelativeColor(referenceColors.InverseSurface), InverseOnSurface=RelativeColor(referenceColors.InverseOnSurface),
            InversePrimary=RelativeColor(referenceColors.InversePrimary),
        };
        return new(colors) { SurfaceOpacity=1 };
    }
    internal static MaterialControlAppearance Action(bool emphasized=false)
    {
        var c=MaterialTheme.Current.Colors;
        return new(emphasized?c.PrimaryContainer:c.SurfaceContainerHigh,c.OnSurface,emphasized?c.Primary:c.OutlineVariant,4);
    }
    internal static MaterialControlAppearance Field => new(MaterialTheme.Current.Colors.SurfaceContainerLowest,
        MaterialTheme.Current.Colors.OnSurface,MaterialTheme.Current.Colors.Outline,4);
    internal static MaterialControlAppearance SourceBadge(Vector4 foreground)
    {
        var c=MaterialTheme.Current.Colors;
        return new(IsReferenceTheme ? Rgb(0x2B3B4C) : MaterialColor.Layer(c.SurfaceContainerHigh,c.OnSurface,.08f),
            foreground,IsReferenceTheme ? Rgb(0x405164) : c.Outline,4);
    }
    internal static MaterialControlMetrics Controls(float height, float icon=20)
    {
        var s=MaterialTheme.Metrics.Scale;
        return new() { Height=height*s,Padding=new(12*s,Math.Max(0,(height*s-ImGui.GetTextLineHeight())*.5f)),
            Gap=8*s, IconSize=icon*s,Rounding=4*s,ItemSpacing=new(10*s,4*s),CellPadding=new(16*s,8*s) };
    }
    internal static void Separator(Vector2 start,float height)
        => ImGui.GetWindowDrawList().AddLine(start,start+new Vector2(0,height),MaterialCanvas.Color(MaterialTheme.Current.Colors.OutlineVariant),MaterialTheme.Metrics.Scale);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace Krangler.Windows;

internal enum UiFontRole { Body, BodyStrong, Title, Caption, Small, Heading, Action }

internal static class KranglerPresentation
{
    // Approved Krangler-review-v2 / compact-review-v1, logical pixels; retain native chrome.
    internal static bool Compact { get; set; }
    internal static float HeaderHeight => Compact ? 68 : 87;
    internal static float ContentTop => Compact ? 24 : 16;
    internal static float SidebarWidth => Compact ? 278 : 282;
    internal static float OverviewHeight => Compact ? 323 : 408;
    internal static float DtrHeight => Compact ? 246 : 324;
    internal static float PanelPadding => Compact ? 24 : 26;
    internal static float NavigationHeight => Compact ? 54 : 64;
    internal static float ActionHeight => Compact ? 48 : 50;
    internal static float Gap => Compact ? 14 : 20;
    internal const uint ReferenceAccent = 0x6951E0;
    internal static readonly float[] FontSizes = [16, 16, 38, 14, 12, 28, 18];
    internal static readonly string[] FontFiles = ["segoeui.ttf", "seguisb.ttf", "segoeuib.ttf", "segoeui.ttf", "segoeui.ttf", "seguisb.ttf", "seguisb.ttf"];
    internal static float AtlasHeight(UiFontRole role) => FontSizes[(int)role] * 4 / 3;
    internal static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    internal static readonly Vector4 Ready = Rgb(0x3CDD88), Pending = Rgb(0xE8B86C), Error = Rgb(0xEE7788);
    internal static MaterialControlMetrics Controls(float height, float icon = 20)
    {
        var s = MaterialTheme.Metrics.Scale;
        return new() { Height = height * s, Padding = new(12 * s, Math.Max(0, (height * s - ImGui.GetTextLineHeight()) * .5f)),
            Gap = 8 * s, IconSize = icon * s, Rounding = 4 * s, ItemSpacing = new(10 * s, 4 * s), CellPadding = new(16 * s, 8 * s) };
    }
    internal static MaterialTheme Theme(uint accent)
    {
        accent &= 0xFFFFFF;
        var reference = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new Vector3(Rgb(ReferenceAccent).X, Rgb(ReferenceAccent).Y, Rgb(ReferenceAccent).Z)));
        var selected = Rgb(accent);
        var seed = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(selected.X, selected.Y, selected.Z)));
        var hueShift = seed.Y < .001f ? 0 : seed.Z - reference.Z;
        var chromaScale = seed.Y < .001f ? 0 : seed.Y / reference.Y;
        Vector4 Relative(uint rgb)
        {
            var color = Rgb(rgb);
            if (accent == ReferenceAccent) return color;
            var lch = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(color.X, color.Y, color.Z)));
            return new(MaterialColor.GamutMap(lch.X, lch.Y * chromaScale, lch.Z + hueShift), 1);
        }
        var palette = new OklchPaletteGenerator().Generate(new(selected.X, selected.Y, selected.Z));
        var background = Relative(0x1B1E28);
        var foreground = Relative(0xECEAF4);
        var primary = Relative(ReferenceAccent);
        var onPrimary = MaterialColor.Contrast(primary, background) >= MaterialColor.Contrast(primary, foreground) ? background : foreground;
        if (MaterialColor.Contrast(primary,onPrimary)<4.5f)
            onPrimary=MaterialColor.Contrast(primary,Rgb(0x000000))>=MaterialColor.Contrast(primary,Rgb(0xFFFFFF))?Rgb(0x000000):Rgb(0xFFFFFF);
        var colors = new MaterialColorScheme(palette)
        {
            Background = background, OnBackground = foreground, Surface = Relative(0x222632), OnSurface = foreground,
            SurfaceContainerLowest = Relative(0x1B1F2B), SurfaceContainerLow = Relative(0x222632),
            SurfaceContainer = Relative(0x282D3B), SurfaceContainerHigh = Relative(0x343849), SurfaceContainerHighest = Relative(0x1C202B),
            SurfaceVariant = Relative(0x383C50), OnSurfaceVariant = Relative(0xC4BEDD),
            Outline = Relative(0x57586C), OutlineVariant = Relative(0x383C50),
            Primary = primary, OnPrimary = onPrimary,
            PrimaryContainer = Relative(0x614CC0), OnPrimaryContainer = foreground,
            Secondary = Relative(0xB59AF7), OnSecondary = background, SecondaryContainer = Relative(0x343849), OnSecondaryContainer = foreground,
            Tertiary = Relative(0xB7A8ED), OnTertiary = background, TertiaryContainer = Relative(0x35304F), OnTertiaryContainer = foreground,
            InverseSurface = foreground, InverseOnSurface = background, InversePrimary = Relative(0x956FF2),
        };
        return new(colors) { SurfaceOpacity = 1 };
    }
    internal static void Surface(Vector2 min, Vector2 max, bool raised = false)
    {
        var c = MaterialTheme.Current.Colors;
        MaterialCanvas.Surface(min, max, raised ? c.SurfaceContainerHigh : c.Surface, c.Background, 4 * MaterialTheme.Metrics.Scale);
        Dalamud.Bindings.ImGui.ImGui.GetWindowDrawList().AddRect(min, max, MaterialCanvas.Color(c.OutlineVariant), 4 * MaterialTheme.Metrics.Scale);
    }
}

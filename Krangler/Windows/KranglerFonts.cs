using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using AethertekUI;
using AethertekUI.Dalamud;

namespace Krangler.Windows;

internal sealed class KranglerFonts : IDisposable
{
    private readonly IFontAtlas atlas;
    private readonly Dictionary<UiFontRole, IFontHandle> roles = [];
    private readonly List<IFontHandle> handles = [];
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal KranglerFonts(IFontAtlas atlas, ushort[] ranges, string language, string assetRoot,
        bool mainWindow, bool compact)
    {
        this.atlas = atlas;
        UiFontRole[] selectedRoles = !mainWindow ? [UiFontRole.Body]
            : compact ? [UiFontRole.Body, UiFontRole.BodyStrong, UiFontRole.Heading, UiFontRole.Action]
            : [UiFontRole.Body, UiFontRole.BodyStrong, UiFontRole.Heading, UiFontRole.Action, UiFontRole.Title];
        var glyphCount = 0;
        for (var index = 0; index + 1 < ranges.Length; index += 2)
            glyphCount += ranges[index + 1] - ranges[index] + 1;
        var pageSize = glyphCount > 1000 && mainWindow ? 2048 : 1024;
        var faces = new Dictionary<(MaterialFontWeight Weight, float Height), IFontHandle>();
        atlas.BuildStepChange += MaterialFontAtlas.ReleaseRasterizedInputs;
        try
        {
            foreach (var role in selectedRoles)
            {
                var weight = KranglerPresentation.FontWeights[(int)role];
                var height = KranglerPresentation.AtlasHeight(role);
                if (!faces.TryGetValue((weight, height), out var handle))
                {
                    handle = MaterialFontAtlas.CreateFont(atlas, weight, height, ranges, language, assetRoot,
                        pageSize, includeHostLanguage: role == UiFontRole.Body);
                    faces.Add((weight, height), handle);
                    handles.Add(handle);
                    handle.ImFontChanged += FontChanged;
                }
                roles.Add(role, handle);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal IEnumerable<float> Sizes => roles.Keys.Select(role => KranglerPresentation.FontSizes[(int)role]).Distinct();
    private void FontChanged(IFontHandle handle,ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal bool Ready => handles.All(h=>h.Available && h.LoadException is null);
    internal Exception? LoadException => handles.FirstOrDefault(h=>h.LoadException is not null)?.LoadException;
    internal unsafe void CheckGlyphs(IEnumerable<string> strings)
    {
        foreach (var (role, handle) in roles)
        {
            using var font = handle.Lock();
            foreach(var text in strings)
                foreach(var character in MaterialText.NativeGlyphText(text).Where(c=>!char.IsControl(c)))
                    if(ImGui.FindGlyphNoFallback(font.ImFont,character).Handle==null)
                        throw new InvalidOperationException("Required UI glyph missing: U+"+((int)character).ToString("X4")+" in "+role);
        }
    }
    internal IDisposable Push(UiFontRole role)
    {
        var handle = roles[role];
        if(!handle.Available || handle.LoadException is not null) throw new InvalidOperationException("Krangler fonts are not ready.",handle.LoadException);
        return handle.Push();
    }
    public void Dispose()
    {
        atlas.BuildStepChange -= MaterialFontAtlas.ReleaseRasterizedInputs;
        foreach (var handle in handles)
        {
            handle.ImFontChanged -= FontChanged;
            handle.Dispose();
        }
        atlas.Dispose();
    }
}

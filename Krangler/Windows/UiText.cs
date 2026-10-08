using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using System.Text.RegularExpressions;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace Krangler.Windows;

internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? throw new InvalidOperationException("Enter the Krangler UI frame before drawing.");
    internal static readonly (string Code,string Name)[] Languages=[("en","English"),("de","Deutsch"),("fr","Français"),
        ("es","Español"),("it","Italiano"),("ru","Русский"),("ja","日本語"),("ko","한국어"),("zh-Hans","简体中文"),
        ("vi","Tiếng Việt"),("pt-BR","Português (Brasil)"),("id","Bahasa Indonesia"),("pl","Polski"),("tr","Türkçe"),("hi","हिन्दी")];
    internal static IEnumerable<string> CjkLanguages(string selected) => new[]{"ja","ko","zh-Hans"}.OrderBy(code=>code==selected?0:1);
    private readonly ResourceManager manager;
    private readonly ResourceManager englishManager;
    private readonly Dictionary<string,string> labels;
    internal ResourceSet Resources { get; }
    internal IReadOnlyList<string> RequiredText { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly Func<UiFontRole,IDisposable> pushFont;
    private readonly (Regex Pattern, string Key, string Prefix, int ArgumentCount)[] messageTemplates;
    internal UiText(string language, Func<UiFontRole,IDisposable> pushFont)
    {
        Language=Languages.Any(l=>l.Code==language)?language:"en";
        Culture=CultureInfo.GetCultureInfo(Language);
        manager=new ResourceManager("Krangler.Localization.Strings_"+Language.Replace('-','_'),typeof(UiText).Assembly);
        Resources=manager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException(Language);
        englishManager=new ResourceManager("Krangler.Localization.Strings_en",typeof(UiText).Assembly);
        var english=englishManager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException("en");
        RequiredText=Values(Resources).Concat(Values(english)).Concat(Languages.Where(l=>l.Code!="hi").Select(l=>l.Name)).Append("\u2661").Distinct().ToArray();
        labels=english.Cast<DictionaryEntry>().ToDictionary(entry=>(string)entry.Value!,entry=>(string)entry.Key,StringComparer.OrdinalIgnoreCase);
        if (labels.Count!=Resources.Cast<DictionaryEntry>().Count() || labels.Values.Any(key=>string.IsNullOrEmpty(Resources.GetString(key,false))))
            throw new MissingManifestResourceException("Incomplete Krangler UI translations for "+Language);
        this.pushFont=pushFont;
        // Service messages remain English in logs; only their UI copies are localized.
        var parameter = new Regex(@"\{(\d+)(?::([^}]+))?\}");
        messageTemplates = labels.Keys.OrderByDescending(key=>key.Length)
            .Where(key => parameter.IsMatch(key)).Select(key =>
            {
                var pattern = "^";
                var offset = 0;
                var holes = parameter.Matches(key);
                foreach (Match hole in holes)
                {
                    pattern += Regex.Escape(key[offset..hole.Index]) + $"(?<arg{hole.Groups[1].Value}>.*?)";
                    offset = hole.Index + hole.Length;
                }
                pattern += Regex.Escape(key[offset..]) + "$";
                return (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(20)), key, key[..holes[0].Index],
                    holes.Cast<Match>().Max(hole => int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture)) + 1);
            }).ToArray();
    }
    internal static string T(string english)
    {

        if (Current.labels.TryGetValue(english,out var resourceKey)) return Current.Resources.GetString(resourceKey,false)!;
        foreach (var template in Current.messageTemplates)
        {
            if (template.Prefix.Length==0 || !english.StartsWith(template.Prefix,StringComparison.Ordinal)) continue;
            var match = template.Pattern.Match(english);
            if (!match.Success) continue;
            var args = Enumerable.Range(0, template.ArgumentCount).Select(index =>
            {
                var value = match.Groups[$"arg{index}"].Value;
                return Argument(template.Key,index,value);
            }).ToArray();
            return string.Format(Current.Culture, Current.Resources.GetString(Current.labels[template.Key], false)!, args);
        }
        return english; // External names, command tokens and raw runtime data retain their original values.
    }
    internal static string Status(string english) => T(english);
    internal static string FrenError(string status,string error)
        => status.StartsWith("Imaginary Fren hidden:",StringComparison.Ordinal) ||
           status=="Imaginary Fren partial: body customize refresh failed." ||
           status.Contains("with partial preset:",StringComparison.Ordinal) ||
           status.StartsWith("Following as '",StringComparison.Ordinal) && status.Contains("using partial preset '",StringComparison.Ordinal)
            ? Status(error) : error;
    private static object? Argument(string template,int index,object? value)
    {
        if (value is null) return null;
        var authored = template is "{0} Icon" or "{0} Icon Code" or "Shown when Krangler is {0}" or
            "draw object type is {0}." or "draw object model type is {0}.";
        var nested = index==0 && template is "Despawned Imaginary Fren: {0}." or "Imaginary Fren hidden: {0}" or "preset change: {0}";
        var boolean = template=="seededCustomize={0}, requestedCustomize={1}, seededEquipment={2}" && index<2 ||
            template=="appearance={0}, requestedCustomize={1}, refresh={2}, equipment={3}, weapons={4}, bonus={5}, meta={6}" && (index<3 || index>4);
        return authored ? T(value.ToString()!) : nested ? Status(value.ToString()!) : boolean ? T(value.ToString()!) : value;
    }
    internal static string F(string english,params object?[] args) => string.Format(Current.Culture,T(english),args.Select((value,index)=>Argument(english,index,value)).ToArray());
    internal static string F(FormattableString text) => F(text.Format,text.GetArguments());
    internal static IDisposable Font(UiFontRole role) => Current.pushFont(role);
    internal Scope Enter() => new(this);
    internal readonly struct Scope : IDisposable
    {
        private readonly UiText? previous;
        internal Scope(UiText value) { previous=current; current=value; }
        public void Dispose() => current=previous;
    }
    internal static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("g",Current.Culture) ?? T("Never");
    internal ushort[] GlyphRanges()
    {
        var chars=RequiredText.SelectMany(MaterialText.NativeGlyphText).Where(c=>!char.IsControl(c))
            .Concat(Enumerable.Range(0x20,0x024F-0x20+1).Select(i=>(char)i))
            .Concat(Enumerable.Range(0x0400,0x052F-0x0400+1).Select(i=>(char)i)).Concat("—").Distinct().Order().ToArray();
        var result=new List<ushort>();
        for(var index=0;index<chars.Length;index++)
        {
            var first=chars[index]; var last=first;
            while(index+1<chars.Length && chars[index+1]==last+1) last=chars[++index];
            result.Add(first); result.Add(last);
        }
        result.Add(0); return result.ToArray();
    }
    internal static IEnumerable<string> Values(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e=>(string)e.Value!);
    public void Dispose() { manager.ReleaseAllResources();englishManager.ReleaseAllResources(); }
}

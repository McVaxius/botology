using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace botology.Windows;

internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? throw new InvalidOperationException("Enter the Botology UI frame before drawing.");
    internal static readonly (string Code,string Name)[] Languages=[("en","English"),("de","Deutsch"),("fr","Français"),
        ("es","Español"),("it","Italiano"),("ru","Русский"),("ja","日本語"),("ko","한국어"),("zh-Hans","简体中文"),
        ("vi","Tiếng Việt"),("pt-BR","Português (Brasil)"),("id","Bahasa Indonesia"),("pl","Polski"),("tr","Türkçe"),("hi","हिन्दी")];
    internal static IEnumerable<string> CjkLanguages(string selected) => new[]{"ja","ko","zh-Hans"}.OrderBy(code=>code==selected?0:1);
    private readonly ResourceManager manager;
    internal ResourceSet Resources { get; }
    internal IReadOnlyList<string> RequiredText { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly Func<UiFontRole,IDisposable> pushFont;
    internal UiText(string language, Func<UiFontRole,IDisposable> pushFont)
    {
        Language=Languages.Any(l=>l.Code==language)?language:"en";
        Culture=CultureInfo.GetCultureInfo(Language);
        manager=new ResourceManager("botology.Localization.Strings_"+Language.Replace('-','_'),typeof(UiText).Assembly);
        Resources=manager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException(Language);
        this.pushFont=pushFont;
        var english=new ResourceManager("botology.Localization.Strings_en",typeof(UiText).Assembly);
        try
        {
            var fallback=english.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException("en");
            RequiredText=Values(Resources).Concat(Values(fallback)).Concat(Languages.Where(l => l.Code != "hi").Select(l=>l.Name)).Append("\u2661").Distinct().ToArray();
        }
        finally { english.ReleaseAllResources(); }
    }
    internal static string T(string english) => Current.Language=="en" ? english : Current.Resources.GetString(english,true) ?? english;
    internal static string F(string english,params object?[] args) => string.Format(Current.Culture,T(english),args);
    internal static string Assessment(botology.Models.AssessmentResult result)
    {
        var summary=result.UiSummaryKey is null ? result.Summary : F(result.UiSummaryKey,result.UiSummaryArguments);
        return result.UiUnderlyingAssessment is { Severity:not botology.Models.AssessmentSeverity.Green } underlying
            ? summary+" "+Assessment(underlying) : summary;
    }
    internal static string Details(botology.Models.AssessmentResult result)
        => result.UiUnderlyingAssessment is { } underlying
            ? F(result.UiSummaryKey!,result.UiSummaryArguments)+(string.IsNullOrWhiteSpace(underlying.Details)?"":Environment.NewLine+Environment.NewLine+underlying.Details)
            : result.Details;
    internal static string Alert(string original)
    {
        if(original=="All active tracked plugins are green.") return T(original);
        var match=System.Text.RegularExpressions.Regex.Match(original,@"^(\d+) non-green plugin entries: (.+)$");
        if(!match.Success) return original;
        var labels=match.Groups[2].Value;
        foreach(var severity in new[]{"Green","Yellow","Red"}) labels=labels.Replace("("+severity+")","("+T(severity)+")",StringComparison.Ordinal);
        return F("{0} non-green plugin entries: {1}",int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture),labels);
    }
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
        var chars=RequiredText.Select(MaterialText.NativeGlyphText).SelectMany(t=>t).Where(c=>!char.IsControl(c))
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
    public void Dispose() => manager.ReleaseAllResources();
}

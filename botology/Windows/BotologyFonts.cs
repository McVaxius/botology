using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

namespace botology.Windows;

internal sealed class BotologyFonts : IDisposable
{
    private readonly IFontHandle[] handles;
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal BotologyFonts(IFontAtlas atlas, ushort[] ranges,string language)
    {
        handles=BotologyPresentation.FontSizes.Select((size,index)=>atlas.NewDelegateFontHandle(toolkit=>toolkit.OnPreBuild(build=>
        {
            size=BotologyPresentation.AtlasHeight((UiFontRole)index);
            var config=new SafeFontConfig { SizePx=size, GlyphRanges=ranges };
            build.Font=build.AddFontFromFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),BotologyPresentation.FontFiles[index]),config);
            // The language selector always displays all nine native names. Host-managed merges cover these too.
            foreach(var locale in UiText.CjkLanguages(language))
                build.AddDalamudAssetFont(DalamudAsset.NotoSansCjkRegular,new SafeFontConfig
                {
                    SizePx=size, MergeFont=build.Font, GlyphRanges=ranges,
                    // Dalamud's bundled TTC faces: Japanese, Traditional Chinese,
                    // Simplified Chinese, Korean. Keep the selected locale first.
                    FontNo=locale switch { "ja"=>0, "zh-Hans"=>2, "ko"=>3, _=>0 },
                });
            build.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx=size, MergeFont=build.Font });
            build.AddGameSymbol(new SafeFontConfig { SizePx=size,MergeFont=build.Font });
        }))).ToArray();
        foreach(var handle in handles) handle.ImFontChanged+=FontChanged;
    }
    private void FontChanged(IFontHandle handle,ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal bool Ready => handles.All(h=>h.Available && h.LoadException is null);
    internal Exception? LoadException => handles.FirstOrDefault(h=>h.LoadException is not null)?.LoadException;
    internal unsafe void CheckGlyphs(IEnumerable<string> strings)
    {
        foreach(var handle in handles)
        {
            using var font=handle.Lock();
            foreach(var text in strings)
                foreach(var character in text.Where(c=>!char.IsControl(c)))
                    if(ImGui.FindGlyphNoFallback(font.ImFont,character).Handle==null)
                        throw new InvalidOperationException("Required UI glyph missing: U+"+((int)character).ToString("X4"));
        }
    }
    internal IDisposable Push(UiFontRole role)
    {
        var handle=handles[(int)role];
        if(!handle.Available || handle.LoadException is not null) throw new InvalidOperationException("Botology fonts are not ready.",handle.LoadException);
        return handle.Push();
    }
    public void Dispose() { foreach(var handle in handles) { handle.ImFontChanged-=FontChanged;handle.Dispose(); } }
}

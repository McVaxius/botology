using botology.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

internal static class InstallerMediaTests
{
    internal static async Task CheckOwnershipAsync()
    {
        var handler=new Images();
        var textures=new List<Texture>();
        using var media=new InstallerMedia(frame=> { var texture=new Texture(); textures.Add(texture);return texture; },new HttpClient(handler));
        const string first="HTTPS://FIXTURE.INVALID/one.gif",second="https://fixture.invalid/two.gif";
        if(handler.Calls!=0) throw new Exception("Media loaded before it was requested.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        media.BeginFrame();
        while(media.Get(first,false,out var failed) is null)
        { if(failed) throw new Exception("Static thumbnail failed.");await Task.Delay(5,timeout.Token); }
        media.EndFrame();
        if(handler.Calls!=1 || media.IsPlaying(first)) throw new Exception("A visible thumbnail started animation or duplicated loading.");
        media.BeginFrame();media.EndFrame();
        if(textures[0].Disposed) throw new Exception("Texture was disposed before render submission.");
        media.BeginFrame();
        if(!textures[0].Disposed) throw new Exception("Offscreen texture was retained.");
        media.Play(first);
        while(media.Animation(first,out var failed) is null)
        { if(failed) throw new Exception("GIF playback failed.");await Task.Delay(5,timeout.Token); }
        media.Play(second);
        if(media.IsPlaying(first) || !media.IsPlaying(second)) throw new Exception("More than one GIF remained active.");
        while(media.Animation(second,out var failed) is null)
        { if(failed) throw new Exception("Replacement GIF playback failed.");await Task.Delay(5,timeout.Token); }
        media.Clear();media.BeginFrame();
        if(textures.Any(t=>!t.Disposed) || media.IsPlaying(second)) throw new Exception("Leaving the page retained playback or textures.");
        foreach(var status in new[]{System.Net.HttpStatusCode.NotFound,System.Net.HttpStatusCode.InternalServerError})
        {
            using var unavailable=new InstallerMedia(_=>throw new Exception("Failed image uploaded a texture."),new HttpClient(new FixedResponseHandler(status,"")));
            var failed=false;
            while(!failed) { unavailable.Get(first,true,out failed);await Task.Delay(5,timeout.Token); }
            if(unavailable.IsMissing(first,true)!=(status==System.Net.HttpStatusCode.NotFound)) throw new Exception("Missing publisher media was confused with a server failure.");
        }
        Console.WriteLine("Media ownership checks passed: no eager loading/autoplay, visible stills, single active GIF, frame-safe retirement and stop/cleanup on leave.");
    }
    private sealed class Texture : IDalamudTextureWrap
    {
        internal bool Disposed;
        public ImTextureID Handle=>new((nint)123);
        public int Width=>2;
        public int Height=>2;
        public void Dispose()=>Disposed=true;
        public IDalamudTextureWrap CreateWrapSharingLowLevelResource()=>throw new NotSupportedException();
    }
    private sealed class Images : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { Calls++;return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(Gif())}); }
    }
    internal static void Check()
    {
        using var supplied=typeof(botology.Plugin).Assembly.GetManifestResourceStream("botology.images.icon.png")!;
        using var png=new MemoryStream();supplied.CopyTo(png);
        var screenshot=NativePreviewDecoder.Decode(png.ToArray(),false,320);
        if(screenshot.IsGif || screenshot.Frames.Count!=1 || screenshot.Frames[0].Width>320 || screenshot.Frames[0].Bgra.Length==0)
            throw new Exception("Native PNG screenshot decoding/scaling failed.");
        var gif=Gif();
        var still=NativePreviewDecoder.Decode(gif,false,320);
        var animation=NativePreviewDecoder.Decode(gif,true,1280);
        if(still.Frames.Count!=1 || !still.IsGif || animation.Frames.Count!=4 || animation.Loops!=0) throw new Exception("GIF animation/explicit still decoding failed.");
        if(animation.Frames.Select(f=>f.DelayMilliseconds).Where((delay,i)=>delay!=(i+1)*50).Any()) throw new Exception("GIF timing was not retained.");
        uint Pixel(int frame,int x,int y)=>BitConverter.ToUInt32(animation.Frames[frame].Bgra,(y*2+x)*4);
        if(Pixel(0,0,0)!=0xffff0000 || Pixel(1,1,0)!=0xff00ff00 || Pixel(2,1,0)!=0 || Pixel(2,0,1)!=0xff0000ff
            || Pixel(3,0,1)!=0 || Pixel(3,1,1)!=0xffff0000 || Pixel(3,0,0)!=0xffff0000)
            throw new Exception("WIC GIF offsets, transparency or disposal composition failed.");
        var opaqueFirst=gif.ToArray();
        for(var i=0;i<opaqueFirst.Length-4;i++)
            if(opaqueFirst[i]==0x21 && opaqueFirst[i+1]==0xf9) { opaqueFirst[i+3]&=0xfe;break; }
        var mixed=NativePreviewDecoder.Decode(opaqueFirst,true,1280);
        if(BitConverter.ToUInt32(mixed.Frames[0].Bgra,4)!=0xff000000) throw new Exception("A later transparent frame changed the initial opaque background.");
        var bounded=NativePreviewDecoder.Decode(Gif(64,1024,1024),true,1280);
        if(bounded.Frames.Count!=64 || bounded.Frames.Sum(f=>(long)f.Bgra.Length)>64*1024*1024 || bounded.Frames[0].Width>=1024)
            throw new Exception("Large animations were not resized into the bounded media budget.");
        using var cancel=new CancellationTokenSource(); cancel.Cancel();
        try { NativePreviewDecoder.Decode(gif,true,1280,cancel.Token); throw new Exception("Cancelled decoding completed."); }
        catch(OperationCanceledException) { }
        try { NativePreviewDecoder.Decode(new byte[16*1024*1024+1],true,1280); throw new Exception("Oversized media was accepted."); }
        catch(InvalidDataException) { }
        Console.WriteLine("Native WIC GIF checks passed: four distinct composed frames, offsets, transparency, disposal 2/3, timing, still thumbnails, cancellation and payload limit.");
    }
    internal static byte[] Gif(int frames=4,int width=2,int height=2)
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        writer.Write("GIF89a"u8); writer.Write((ushort)width);writer.Write((ushort)height);writer.Write((byte)0x81);writer.Write((byte)0);writer.Write((byte)0);
        writer.Write(new byte[]{0,0,0,255,0,0,0,255,0,0,0,255});
        writer.Write(new byte[]{0x21,0xff,11});writer.Write("NETSCAPE2.0"u8);writer.Write(new byte[]{3,1,0,0,0});
        for(var i=0;i<frames;i++)
        {
            var disposal=i==1?3:i==2?2:1;
            writer.Write(new byte[]{0x21,0xf9,4,(byte)((disposal<<2)|1),(byte)((i+1)*5),0,0,0,0x2c});
            writer.Write((ushort)(i%2)); writer.Write((ushort)(i/2));writer.Write((ushort)1);writer.Write((ushort)1);writer.Write((byte)0);
            var color=i==1?2:i==2?3:1; var codes=4|(color<<3)|(5<<6);
            writer.Write(new byte[]{2,2,(byte)codes,(byte)(codes>>8),0});
        }
        writer.Write((byte)0x3b);return stream.ToArray();
    }
}

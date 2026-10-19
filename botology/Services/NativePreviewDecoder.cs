using System.Runtime.InteropServices;

namespace botology.Services;

internal sealed record PreviewBitmap(int Width, int Height, byte[] Bgra, int DelayMilliseconds = 100);
internal sealed record PreviewAnimation(IReadOnlyList<PreviewBitmap> Frames, int Loops, bool IsGif = false);

// Windows Imaging Component is supplied by the OS. No codec or helper is shipped with Botology.
internal static unsafe class NativePreviewDecoder
{
    private static readonly Guid FactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid FactoryInterface = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid Bgra = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [DllImport("shlwapi.dll")] private static extern nint SHCreateMemStream(byte* bytes, uint count);
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static void Release(nint value) { if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)value)[2])(value); }

    internal static PreviewAnimation Decode(byte[] bytes, bool animate, int maxEdge, CancellationToken token = default)
    {
        if (bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Preview exceeds the media limit.");
        var gif = bytes.AsSpan().StartsWith("GIF8"u8) ? ReadGif(bytes) : null;
        var init = CoInitializeEx(0, 0);
        if (init < 0 && init != unchecked((int)0x80010106)) Check(init);
        nint factory = 0, stream = 0, decoder = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            Check(CoCreateInstance(FactoryClass, 0, 1, FactoryInterface, out factory));
            fixed (byte* data = bytes) stream = SHCreateMemStream(data, (uint)bytes.Length);
            if (stream == 0) throw new InvalidDataException("Cannot open preview.");
            Check(((delegate* unmanaged[Stdcall]<nint,nint,nint,uint,nint*,int>)(*(nint**)factory)[4])(factory,stream,0,1,&decoder));
            uint count;
            Check(((delegate* unmanaged[Stdcall]<nint,uint*,int>)(*(nint**)decoder)[12])(decoder,&count));
            if (count == 0 || animate && count > 240) throw new InvalidDataException("Preview exceeds the media limit.");
            if (gif is not null && gif.Frames.Count != count) throw new InvalidDataException("Invalid GIF frame count.");
            if(animate && gif is not null)
            {
                // Fit complete animations into the CPU budget instead of rejecting ordinary long HD previews.
                var budgetScale=Math.Sqrt(48d*1024*1024/(4d*count*gif.Width*gif.Height));
                maxEdge=Math.Min(maxEdge,Math.Max(1,(int)(Math.Max(gif.Width,gif.Height)*budgetScale)));
            }
            var output = new List<PreviewBitmap>();
            var canvas = gif is null ? null : new byte[checked(gif.Width * gif.Height * 4)];
            if (gif is not null && gif.Background != 0) Fill(canvas!, gif.Background);
            var frames = animate && gif is not null ? count : 1;
            long allocated = 0;
            for (uint index = 0; index < frames; index++)
            {
                token.ThrowIfCancellationRequested();
                nint frame = 0;
                try
                {
                    Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,int>)(*(nint**)decoder)[13])(decoder,index,&frame));
                    // GIF frames must be composed at their original offsets before thumbnail scaling.
                    var decoded = ReadBitmap(factory, frame, gif is null ? maxEdge : 4096);
                    if (gif is not null)
                    {
                        var info = gif.Frames[(int)index];
                        if (decoded.Width != info.Width || decoded.Height != info.Height) throw new InvalidDataException("Invalid GIF frame bounds.");
                        var previous = info.Disposal == 3 ? (byte[])canvas!.Clone() : null;
                        for (var y = 0; y < info.Height; y++)
                        for (var x = 0; x < info.Width; x++)
                        {
                            var src = (y * info.Width + x) * 4;
                            var dst = ((y + info.Top) * gif.Width + x + info.Left) * 4;
                            if (decoded.Bgra[src + 3] == 0) continue;
                            decoded.Bgra.AsSpan(src, 4).CopyTo(canvas!.AsSpan(dst, 4));
                        }
                        decoded = Resize(gif.Width, gif.Height, canvas!, maxEdge, info.Delay);
                        if (info.Disposal == 2)
                            for (var y = 0; y < info.Height; y++)
                                for (var x = 0; x < info.Width; x++)
                                    BitConverter.TryWriteBytes(canvas!.AsSpan(((y + info.Top) * gif.Width + info.Left + x) * 4, 4), info.Background);
                        if (previous is not null) canvas = previous;
                    }
                    allocated += decoded.Bgra.Length;
                    if (allocated > 64 * 1024 * 1024) throw new InvalidDataException("Preview exceeds the media limit.");
                    output.Add(decoded);
                }
                finally { Release(frame); }
            }
            return new(output, gif?.Loops ?? 1, gif is not null);
        }
        finally { Release(decoder); Release(stream); Release(factory); if (init >= 0) CoUninitialize(); }
    }

    private static PreviewBitmap ReadBitmap(nint factory, nint source, int maxEdge)
    {
        uint width, height;
        Check(((delegate* unmanaged[Stdcall]<nint,uint*,uint*,int>)(*(nint**)source)[3])(source,&width,&height));
        if (width == 0 || height == 0 || width > 4096 || height > 4096) throw new InvalidDataException("Preview exceeds the media limit.");
        nint scaler = 0, converter = 0;
        try
        {
            if (Math.Max(width,height) > maxEdge)
            {
                var ratio = maxEdge / (double)Math.Max(width,height);
                width = (uint)Math.Max(1, Math.Round(width * ratio)); height = (uint)Math.Max(1, Math.Round(height * ratio));
                Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)(*(nint**)factory)[11])(factory,&scaler));
                Check(((delegate* unmanaged[Stdcall]<nint,nint,uint,uint,uint,int>)(*(nint**)scaler)[8])(scaler,source,width,height,2));
                source = scaler;
            }
            Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)(*(nint**)factory)[10])(factory,&converter));
            var format = Bgra;
            Check(((delegate* unmanaged[Stdcall]<nint,nint,Guid*,uint,nint,double,uint,int>)(*(nint**)converter)[8])(converter,source,&format,0,0,0,0));
            var pixels = new byte[checked((int)(width * height * 4))];
            fixed (byte* buffer = pixels)
                Check(((delegate* unmanaged[Stdcall]<nint,nint,uint,uint,byte*,int>)(*(nint**)converter)[7])(converter,0,width*4,(uint)pixels.Length,buffer));
            return new((int)width,(int)height,pixels);
        }
        finally { Release(converter); Release(scaler); }
    }

    private static PreviewBitmap Resize(int width, int height, byte[] pixels, int edge, int delay)
    {
        var scale = Math.Min(1, edge / (double)Math.Max(width,height));
        var w = Math.Max(1,(int)(width*scale)); var h = Math.Max(1,(int)(height*scale));
        var result = new byte[w*h*4];
        for (var y=0;y<h;y++) for (var x=0;x<w;x++)
            pixels.AsSpan(((int)(y/scale)*width+(int)(x/scale))*4,4).CopyTo(result.AsSpan((y*w+x)*4,4));
        return new(w,h,result,delay);
    }
    private static void Fill(byte[] data, uint color)
    { for(var i=0;i<data.Length;i+=4) BitConverter.TryWriteBytes(data.AsSpan(i,4),color); }

    private sealed record GifFrame(int Left,int Top,int Width,int Height,int Delay,int Disposal,uint Background);
    private sealed record GifInfo(int Width,int Height,uint Background,int Loops,List<GifFrame> Frames);
    private static GifInfo ReadGif(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.ReadBytes(6);
        var width=reader.ReadUInt16(); var height=reader.ReadUInt16(); var packed=reader.ReadByte(); var backgroundIndex=reader.ReadByte(); reader.ReadByte();
        if(width==0 || height==0 || width>4096 || height>4096) throw new InvalidDataException("Preview exceeds the media limit.");
        var palette=(packed&128)!=0 ? reader.ReadBytes(3*(1<<((packed&7)+1))) : [];
        uint background=palette.Length>backgroundIndex*3+2 ? 0xff000000u|(uint)(palette[backgroundIndex*3]<<16|palette[backgroundIndex*3+1]<<8|palette[backgroundIndex*3+2]) : 0;
        var frames=new List<GifFrame>(); var delay=100; var disposal=0; var loops=1;
        var frameBackground=background;
        byte[] Blocks()
        {
            using var output=new MemoryStream();
            while(true) { var length=reader.ReadByte(); if(length==0) return output.ToArray(); var data=reader.ReadBytes(length); if(data.Length!=length) throw new EndOfStreamException(); output.Write(data); }
        }
        while(reader.BaseStream.Position<bytes.Length)
        {
            var marker=reader.ReadByte();
            if(marker==0x3b) break;
            if(marker==0x21)
            {
                var kind=reader.ReadByte(); var data=Blocks();
                if(kind==0xf9 && data.Length==4)
                {
                    disposal=(data[0]>>2)&7; delay=Math.Max(20,BitConverter.ToUInt16(data,1)*10);
                    frameBackground=(data[0]&1)!=0 && data[3]==backgroundIndex ? 0 : background;
                }
                if(kind==0xff && data.Length>=14 && System.Text.Encoding.ASCII.GetString(data,0,11) is "NETSCAPE2.0" or "ANIMEXTS1.0")
                    loops=BitConverter.ToUInt16(data,12) is var repeats && repeats==0 ? 0 : repeats+1;
                continue;
            }
            if(marker!=0x2c) throw new InvalidDataException("Invalid GIF.");
            var left=reader.ReadUInt16(); var top=reader.ReadUInt16(); var w=reader.ReadUInt16(); var h=reader.ReadUInt16(); var flags=reader.ReadByte();
            if(w==0 || h==0 || left+w>width || top+h>height || frames.Count>=240) throw new InvalidDataException("Invalid GIF frame bounds.");
            if((flags&128)!=0) reader.ReadBytes(3*(1<<((flags&7)+1)));
            reader.ReadByte(); Blocks();
            frames.Add(new(left,top,w,h,delay,disposal,frameBackground)); delay=100; disposal=0; frameBackground=background;
        }
        return new(width,height,frames.Count>0?frames[0].Background:background,loops,frames);
    }
}

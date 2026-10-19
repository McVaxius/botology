using System.Diagnostics;
using System.Net.Http;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace botology.Services;

// Only visible requests survive the frame. Downloads, decoded bytes and textures are session-owned.
internal sealed class InstallerMedia : IDisposable
{
    private readonly Func<PreviewBitmap, IDalamudTextureWrap> upload;
    private readonly HttpClient http;
    internal InstallerMedia(ITextureProvider textures) : this(frame => textures.CreateFromRaw(
        RawImageSpecification.Bgra32(frame.Width,frame.Height),frame.Bgra,"Botology preview"), new() { Timeout = TimeSpan.FromSeconds(20) }) { }
    internal InstallerMedia(Func<PreviewBitmap, IDalamudTextureWrap> upload, HttpClient http)
    { this.upload=upload; this.http=http; }
    private readonly SemaphoreSlim downloads = new(2);
    private readonly Dictionary<string, Request> requests = new(StringComparer.Ordinal);
    private readonly List<IDalamudTextureWrap> retired = [];
    private string? playing;
    private readonly Stopwatch playback = new();
    private int uploadedFrame = -1;
    private IDalamudTextureWrap? animatedTexture;
    private bool disposed;
    private sealed class Request(bool animated, int edge)
    {
        internal readonly CancellationTokenSource Cancel = new();
        internal Task<PreviewAnimation> Task = null!;
        internal IDalamudTextureWrap? Texture;
        internal bool Visible;
        internal bool Animated = animated;
        internal int Edge = edge;
        internal bool Failed;
    }

    internal void BeginFrame()
    {
        foreach(var texture in retired) texture.Dispose();
        retired.Clear();
        foreach(var request in requests.Values) request.Visible=false;
    }

    internal IDalamudTextureWrap? Get(string? url, bool full, out bool failed)
    {
        failed=false;
        if(disposed || InstallerCatalog.NormalizeRepository(url) is not { } normalized) return null;
        var key=(full ? "full:" : "thumb:")+normalized;
        var request=Find(key,normalized,false,full?1920:320);
        request.Visible=true;
        if(request.Task.IsFaulted || request.Task.IsCanceled) request.Failed=true;
        if(!request.Failed && request.Texture is null && request.Task.IsCompletedSuccessfully)
        {
            try { request.Texture=upload(request.Task.Result.Frames[0]); }
            catch { request.Failed=true; }
        }
        failed=request.Failed;
        return request.Texture;
    }

    internal bool IsPlaying(string url) => InstallerCatalog.NormalizeRepository(url) is { } normalized && playing==normalized;
    internal bool IsMissing(string? url,bool full)
        => InstallerCatalog.NormalizeRepository(url) is { } normalized && requests.TryGetValue((full?"full:":"thumb:")+normalized,out var request)
            && request.Task.IsFaulted && request.Task.Exception?.GetBaseException() is HttpRequestException
                { StatusCode:System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone };
    internal bool IsGif(string url) => InstallerCatalog.NormalizeRepository(url) is { } normalized && requests.TryGetValue("full:"+normalized,out var request) && request.Task.IsCompletedSuccessfully && request.Task.Result.IsGif;
    internal void Play(string url)
    {
        Stop();
        if(InstallerCatalog.NormalizeRepository(url) is not { } normalized) return;
        playing=normalized;
        Find("animation:"+normalized,normalized,true,1280).Visible=true;
    }

    internal IDalamudTextureWrap? Animation(string url, out bool failed)
    {
        failed=false;
        var normalized=InstallerCatalog.NormalizeRepository(url);
        if(normalized is null || playing!=normalized || !requests.TryGetValue("animation:"+normalized,out var request)) return null;
        request.Visible=true;
        if(request.Task.IsFaulted || request.Task.IsCanceled) request.Failed=true;
        failed=request.Failed;
        if(failed || !request.Task.IsCompletedSuccessfully) return null;
        if(!playback.IsRunning) playback.Start();
        var animation=request.Task.Result;
        var duration=animation.Frames.Sum(f=>f.DelayMilliseconds);
        var elapsed=playback.ElapsedMilliseconds;
        var at=animation.Loops>0 && elapsed>=duration*(long)animation.Loops ? duration-1 : elapsed%duration;
        var index=0;
        while(index<animation.Frames.Count-1 && at>=animation.Frames[index].DelayMilliseconds) at-=animation.Frames[index++].DelayMilliseconds;
        if(index!=uploadedFrame)
        {
            try
            {
                var frame=animation.Frames[index];
                var next=upload(frame);
                if(animatedTexture is not null) retired.Add(animatedTexture);
                animatedTexture=next; uploadedFrame=index;
            }
            catch { request.Failed=failed=true; }
        }
        return animatedTexture;
    }

    private Request Find(string key,string url,bool animated,int edge)
    {
        if(requests.TryGetValue(key,out var existing)) return existing;
        var request=new Request(animated,edge);
        request.Task=Load(url,request.Cancel.Token,animated,edge);
        requests.Add(key,request);
        return request;
    }
    private async Task<PreviewAnimation> Load(string url,CancellationToken token,bool animated,int edge)
    {
        await downloads.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>16*1024*1024) throw new InvalidDataException("Preview exceeds the media limit.");
            using var data=new MemoryStream();
            await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer=new byte[16384];
            int count;
            while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0)
            {
                if(data.Length+count>16*1024*1024) throw new InvalidDataException("Preview exceeds the media limit.");
                data.Write(buffer,0,count);
            }
            return await Task.Run(()=>NativePreviewDecoder.Decode(data.ToArray(),animated,edge,token),token).ConfigureAwait(false);
        }
        finally { downloads.Release(); }
    }

    internal void Stop()
    {
        if(playing is not null) Remove("animation:"+playing);
        playing=null; playback.Reset(); uploadedFrame=-1;
        if(animatedTexture is not null) { retired.Add(animatedTexture); animatedTexture=null; }
    }
    internal void EndFrame()
    { foreach(var key in requests.Where(p=>!p.Value.Visible).Select(p=>p.Key).ToArray()) { if(key=="animation:"+playing) Stop(); else Remove(key); } }
    private void Remove(string key)
    {
        if(!requests.Remove(key,out var request)) return;
        request.Cancel.Cancel();
        // Observe faults and dispose cancellation state only after the worker releases its token.
        _=request.Task.ContinueWith(task=> { _=task.Exception; request.Cancel.Dispose(); },TaskScheduler.Default);
        if(request.Texture is not null) retired.Add(request.Texture);
    }
    internal void Clear()
    { Stop(); foreach(var key in requests.Keys.ToArray()) Remove(key); }
    public void Dispose()
    {
        if(disposed) return;
        disposed=true; Clear();
        foreach(var texture in retired) texture.Dispose();
        retired.Clear(); http.Dispose();
    }
}

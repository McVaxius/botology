using System.Net;
using System.Reflection;
using botology.Services;
using AethertekUI.Tests;
using Dalamud.Plugin.Services;

internal static class InstallerDiscoveryTests
{
    internal static async Task CheckAsync()
    {
        var handler=new Feeds();
        var log=DispatchProxy.Create<IPluginLog,ConfigurationSaveProxy>();
        ((ConfigurationSaveProxy)(object)log).Handler=(_,_)=>null;
        using var service=new RepositoryMetadataService(log,new HttpClient(handler));
        const string source="https://fixture.invalid/feed.json";
        service.QueueDiscovery([source,source]);
        async Task Finish()
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while(service.DiscoveryRunning) await Task.Delay(5,timeout.Token);
        }
        await Finish();
        if(handler.Calls!=1 || service.CaptureDiscoveryManifests().Count!=2) throw new Exception("Discovery did not bootstrap the full feed independently of the metadata cache.");
        service.QueueDiscovery([source]); await Finish();
        if(handler.Calls!=1) throw new Exception("Draw-triggered discovery repeated a completed session fetch.");
        handler.Body="{}";
        service.QueueDiscovery([source],true); await Finish();
        if(handler.Calls!=2 || service.CaptureDiscoveryManifests().Count!=2 || !service.DiscoveryErrors.ContainsKey(source))
            throw new Exception("Failed manual discovery must retain last-valid data and expose an error.");
        handler.Body="[]";
        service.QueueDiscovery([source],true); await Finish();
        if(service.CaptureDiscoveryManifests().Count!=0 || service.DiscoveryErrors.Count!=0) throw new Exception("A valid empty feed must replace stale entries.");
        Console.WriteLine("Discovery checks passed: full-feed bootstrap, no automatic retry, explicit refresh, retained last-valid data, invalid-feed errors and valid empty feeds.");
    }
    private sealed class Feeds : HttpMessageHandler
    {
        internal int Calls;
        internal string Body="""[{"InternalName":"One","DalamudApiLevel":15},{"InternalName":"Two","DalamudApiLevel":15}]""";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Body)}); }
    }
}

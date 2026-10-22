using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Tests.Mods.Support;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModLibraryEndToEndTests
    {
        [Fact] public async Task Real_http_parser_trust_archive_queue_and_restart_recovery_restore_original()
        {
            using(var s=new ModScenario())using(var handler=new Api(s))using(var http=new HttpClient(handler))using(var catalog=new ModCatalogClient(http,new Uri("https://ps4manager.devkit.online/"),Path.Combine(s.Root,"cache"))){
                var discovery=new ModGameDiscovery(s.Ftp,s.Gates);var planner=new ModInstallPlanner(s.Store,s.Verifier,discovery,s.Ftp,s.Gates,s.Profile,s.Session,catalog);
                using(var controller=new ModLibraryController(catalog,discovery,planner,s.Store,s.Profile,s.Queue,CancellationToken.None)){
                    await controller.RefreshGamesAsync();await controller.SelectGameAsync(Assert.Single(controller.State.Games));await controller.SelectModAsync(Assert.Single(controller.State.Catalog.Items));
                    var plan=await controller.PrepareInstallAsync();bool submitted=false;
                    s.Ftp.BeforeMutation=path=>{if(!submitted){submitted=true;s.Queue.Enqueue(new TransferJob(TransferKind.CreateFolder,"ordinary",new[]{new TransferStep(StepKind.CreateRemoteFolder,"/data/ordinary",null,null,0)}));}};
                    await controller.ConfirmAndEnqueue(plan,true,true).Completion;await s.Queue.WhenIdle();Assert.Equal("one",s.Read());Assert.Equal("mkdir /data/ordinary",s.Ftp.Mutations.Last());s.Ftp.BeforeMutation=null;
                    s.Package("two");await controller.SelectGameAsync(controller.State.Game);await controller.SelectModAsync(Assert.Single(controller.State.Catalog.Items));
                    var second=await controller.PrepareInstallAsync();await controller.ConfirmAndEnqueue(second,true,true).Completion;Assert.Equal("two",s.Read());
                    await controller.Recover(second.TransactionId,"rollback",true,true).Completion;Assert.Equal("one",s.Read());
                    handler.Offline=true;var newSession=new object();s.Profile.ConfirmForSession(newSession);
                    var restarted=new ModInstallPlanner(new ModReceiptStore(s.Root),s.Verifier,discovery,s.Ftp,s.Gates,s.Profile,newSession,catalog);
                    await s.Run(new ModRecovery(restarted).CreateUninstall(plan.TransactionId,true,true));Assert.Equal("original",s.Read());
                    Assert.DoesNotContain(s.Ftp.Mutations,p=>p.StartsWith("delete "));
                }
            }
        }
        [Theory][InlineData("bytes")][InlineData("key")]
        public async Task Http_package_or_trust_substitution_never_writes_console(string fault)
        {
            using(var s=new ModScenario())using(var handler=new Api(s){Fault=fault})using(var http=new HttpClient(handler))using(var catalog=new ModCatalogClient(http,new Uri("https://ps4manager.devkit.online/"),Path.Combine(s.Root,"cache"))){
                var discovery=new ModGameDiscovery(s.Ftp,s.Gates);var planner=new ModInstallPlanner(s.Store,s.Verifier,discovery,s.Ftp,s.Gates,s.Profile,s.Session,catalog);
                await Assert.ThrowsAnyAsync<Exception>(()=>planner.PrepareAsync(s.Release,new ModGame{TitleId="CUSA00411",Version="1.57"},CancellationToken.None));
                Assert.Empty(s.Ftp.Mutations);Assert.Empty(s.Store.Load(s.Profile.Id));Assert.Equal("original",s.Read());
            }
        }
        private sealed class Api:HttpMessageHandler
        {
            private readonly ModScenario scenario;public bool Offline;public string Fault;
            public Api(ModScenario scenario){this.scenario=scenario;}
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
            {
                if(Offline)throw new HttpRequestException("offline");var path=request.RequestUri.AbsolutePath;var r=scenario.Release;
                string json=r.RawJson;
                if(Fault=="key")json=json.Replace(r.SigningKeyId,"0000000000000000");
                HttpContent content;
                if(path.EndsWith("/download")){var bytes=scenario.PackageBytes;if(Fault=="bytes")bytes[0]^=1;content=new ByteArrayContent(bytes);}
                else if(path.EndsWith("/catalog"))content=new StringContent("{\"page\":1,\"pageSize\":24,\"items\":["+json+"]}",Encoding.UTF8,"application/json");
                else if(path.Contains("/mods/"))content=new StringContent("{\"modId\":\""+r.ModId+"\",\"title\":\"Luma\",\"description\":\"Test\",\"releases\":[],\"media\":[]}",Encoding.UTF8,"application/json");
                else content=new StringContent(json,Encoding.UTF8,"application/json");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=content});
            }
        }
    }
}

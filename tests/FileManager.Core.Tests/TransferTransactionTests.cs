using System;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class TransferTransactionTests
    {
        private sealed class Body:IQueuedTransaction
        {
            private readonly Func<TransferTransactionContext,CancellationToken,Task> action;
            public Body(Func<TransferTransactionContext,CancellationToken,Task> action){this.action=action;}
            public Task ExecuteAsync(TransferTransactionContext context,CancellationToken ct){return action(context,ct);}
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Transaction_owns_gate_and_completion_and_failure_does_not_wedge_queue(bool cancel)
        {
            var ftp=new InMemoryFtpClient();ftp.AddDir("/data");var gates=new FtpGates();
            var queue=new TransferQueue(new ZoneGuardFtpClient(ftp,new ZonePolicy()),gates,new ScriptedResolver(),new TestClock());
            var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var a=new TransferJob(TransferKind.CreateFolder,"A",new[]{new TransferStep(StepKind.CreateRemoteFolder,"/data/a",null,null,0)});
            var b=new TransferJob("B",new Body(async (context,ct)=>{
                Assert.Contains("a",await Names(context));Assert.Equal(0,gates.Transfer.CurrentCount);entered.SetResult(true);
                using(ct.Register(()=>release.TrySetCanceled()))await release.Task;
                throw new System.IO.IOException("disconnect");
            }),new[]{"/data/b"},CancellationToken.None);
            var c=new TransferJob(TransferKind.CreateFolder,"C",new[]{new TransferStep(StepKind.CreateRemoteFolder,"/data/c",null,null,0)});
            queue.JobChanged+=(s,j)=>throw new Exception("subscriber");
            queue.Enqueue(a);queue.Enqueue(b);queue.Enqueue(c);await entered.Task;
            Assert.False(b.Completion.IsCompleted);Assert.Equal(JobState.Waiting,c.State);
            if(cancel)await queue.CancelAsync(b);else release.SetResult(true);
            await b.Completion;await queue.WhenIdle();
            Assert.Equal(JobState.Interrupted,b.State);Assert.Equal(JobState.Completed,c.State);Assert.Equal(1,gates.Transfer.CurrentCount);
            Assert.False(b.CanResume);
            Assert.Throws<InvalidOperationException>(()=>queue.Resume(b));
        }
        private static async Task<string[]> Names(TransferTransactionContext context)
        {var entries=await context.Ftp.ListAsync("/data",CancellationToken.None);var result=new string[entries.Count];for(int i=0;i<entries.Count;i++)result[i]=entries[i].Name;return result;}
    }
}

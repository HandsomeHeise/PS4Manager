using System;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Games;
using PS4Manager.Core.Games.Bo3;
using Xunit;

namespace PS4Manager.Core.Tests.Games
{
    public sealed class Bo3GodmodeTests
    {
        [Fact]
        public async Task Observed_exit_invalidates_rpc_before_same_pid_returns()
        {
            var f = Bo3Fixture.Create(); await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            var processes = f.Transport.Processes; f.Transport.Processes = new PS4Manager.Core.Frame4.Frame4Process[0];
            Assert.False((await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Ready);
            Assert.True(f.Transport.Invalidations > 0);
            f.Transport.Processes = processes; Assert.True((await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Ready);
        }
        [Fact]
        public async Task Confirmed_player_context_is_checked_at_execute_entry()
        {
            var f = Bo3Fixture.Create(); var status = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            f.Transport.Put(f.Client + 0x16C44, System.Text.Encoding.UTF8.GetBytes("Changed\0"));
            var result = await f.Trainer.ExecuteAsync(f.Target, status.Identity, TrainerAction.EnableGodmode, CancellationToken.None, status.ContextKey);
            Assert.Equal(ActionOutcome.Blocked, result.Outcome); Assert.Empty(f.Transport.Writes); Assert.Empty(f.Transport.Calls);
        }
        [Theory]
        [InlineData(4U, 5U, TrainerAction.EnableGodmode)]
        [InlineData(0xA0U, 0xA1U, TrainerAction.EnableGodmode)]
        [InlineData(0xA1U, 0xA0U, TrainerAction.DisableGodmode)]
        public async Task Only_bit_zero_is_changed(uint before, uint after, TrainerAction action)
        {
            var f = Bo3Fixture.Create(); f.PutFlags(before);
            var status = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            var result = await f.Trainer.ExecuteAsync(f.Target, status.Identity, action, CancellationToken.None);
            Assert.Equal(ActionOutcome.Success, result.Outcome);
            Assert.Equal(after, f.Transport.ReadUInt32(f.Client + 0x20));
            Assert.Single(f.Transport.Writes); Assert.Empty(f.Transport.Calls);
        }

        [Fact]
        public async Task Existing_bit_is_not_adopted_or_rewritten()
        {
            var f = Bo3Fixture.Create(); f.PutFlags(5);
            var s = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            Assert.Equal(GodmodeState.OnExternal, s.Godmode);
            Assert.Equal(ActionOutcome.NoChange, (await f.Trainer.ExecuteAsync(f.Target, s.Identity, TrainerAction.EnableGodmode, CancellationToken.None)).Outcome);
            Assert.Equal(GodmodeState.OnExternal, (await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Godmode);
            Assert.Empty(f.Transport.Writes);
        }

        [Fact]
        public async Task Reopen_does_not_claim_ownership_and_probe_does_not_reapply()
        {
            var f = Bo3Fixture.Create(); var s = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            await f.Trainer.ExecuteAsync(f.Target, s.Identity, TrainerAction.EnableGodmode, CancellationToken.None);
            Assert.Equal(GodmodeState.OnOwned, (await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Godmode);
            Assert.Equal(GodmodeState.OnExternal, (await new Bo3Trainer(f.Transport).ProbeAsync(f.Target, CancellationToken.None)).Godmode);
            f.PutHealth(0); Assert.False((await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Ready);
            f.PutHealth(100); f.PutFlags(4);
            Assert.Equal(GodmodeState.Off, (await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Godmode);
            Assert.Single(f.Transport.Writes);
        }

        [Fact]
        public async Task Failed_readback_is_unknown_without_rollback()
        {
            var f = Bo3Fixture.Create(); var s = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            f.Transport.AfterWrite = a => f.Transport.Memory.Remove(a);
            var r = await f.Trainer.ExecuteAsync(f.Target, s.Identity, TrainerAction.EnableGodmode, CancellationToken.None);
            Assert.Equal(ActionOutcome.Unknown, r.Outcome); Assert.Single(f.Transport.Writes);
        }

        [Fact]
        public async Task Changing_player_and_dead_player_block_before_write()
        {
            var f = Bo3Fixture.Create(); var s = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            f.PutHealth(0);
            Assert.Equal(ActionOutcome.Blocked, (await f.Trainer.ExecuteAsync(f.Target, s.Identity, TrainerAction.EnableGodmode, CancellationToken.None)).Outcome);
            f.PutHealth(100); int reads = 0;
            f.Transport.BeforeRead = a => { if (a == f.Entity + 0x250 && ++reads == 3) f.Transport.Put64(a, 0); };
            Assert.Equal(ActionOutcome.Blocked, (await f.Trainer.ExecuteAsync(f.Target, s.Identity, TrainerAction.EnableGodmode, CancellationToken.None)).Outcome);
            Assert.Empty(f.Transport.Writes);
        }
    }
}

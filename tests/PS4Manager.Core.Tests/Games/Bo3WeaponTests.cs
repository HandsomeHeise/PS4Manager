using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Games;
using Xunit;

namespace PS4Manager.Core.Tests.Games
{
    public sealed class Bo3WeaponTests
    {
        private static async Task<TrainerResult> Act(Bo3Fixture f, TrainerAction action)
        {
            f.EnableRpc(); var s = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            Assert.True(s.Ready, s.Reason);
            return await f.Trainer.ExecuteAsync(f.Target, s.Identity, action, CancellationToken.None);
        }
        [Fact]
        public async Task Existing_rk5_is_not_granted_again()
        {
            var f = Bo3Fixture.Create(); f.PutInventory("pistol_revolver38_zm", "pistol_burst_zm");
            Assert.Equal(ActionOutcome.NoChange, (await Act(f, TrainerAction.GiveRk5)).Outcome);
            Assert.Empty(f.Transport.Calls); Assert.Empty(f.Transport.Writes);
        }
        [Fact]
        public async Task Grant_is_once_and_initializes_from_runtime_deficit()
        {
            var f = Bo3Fixture.Create(); var result = await Act(f, TrainerAction.GiveRk5);
            Assert.Equal(ActionOutcome.Success, result.Outcome);
            var grant = Assert.Single(f.Transport.Calls, c => c.Symbol == "BO3_GivePlayerWeaponInternal");
            Assert.Equal(new ulong[] { f.Entity, f.Client, 9, 0, 0, 0 }, grant.Arguments);
            Assert.Equal(9U, f.Transport.ReadUInt32(f.Client + 0x378 + 48));
            Assert.Equal(8U, f.Transport.ReadUInt32(f.Client + 0x684));
            Assert.Equal(32U, f.Transport.ReadUInt32(f.Client + 0x648));
            Assert.Equal(10U, f.Transport.ReadUInt32(f.Client + 0x684 + 4));
            Assert.Equal(40U, f.Transport.ReadUInt32(f.Client + 0x648 + 4));
            Assert.Equal(50UL, Assert.Single(f.Transport.Calls, c => c.Symbol == "BO3_AddWeaponAmmo").Arguments[2]);
        }
        [Fact]
        public async Task Lost_grant_response_does_not_repeat_or_add_ammo()
        {
            var f = Bo3Fixture.Create(); f.FailSymbol = "BO3_GivePlayerWeaponInternal"; f.LoseAfterEffect = true;
            Assert.Equal(ActionOutcome.Unknown, (await Act(f, TrainerAction.GiveRk5)).Outcome);
            Assert.Equal(9U, f.Transport.ReadUInt32(f.Client + 0x378 + 48));
            Assert.Single(f.Transport.Calls, c => c.Symbol == "BO3_GivePlayerWeaponInternal");
            Assert.DoesNotContain(f.Transport.Calls, c => c.Symbol == "BO3_AddWeaponAmmo");
        }
        [Fact]
        public async Task Ammo_failure_after_grant_is_partial_not_rollback()
        {
            var f = Bo3Fixture.Create(); f.FailSymbol = "BO3_AddWeaponAmmo";
            Assert.Equal(ActionOutcome.Partial, (await Act(f, TrainerAction.GiveRk5)).Outcome);
            Assert.Equal(9U, f.Transport.ReadUInt32(f.Client + 0x378 + 48)); Assert.Empty(f.Transport.Writes);
        }
        [Theory]
        [InlineData(8, 32)]
        [InlineData(0, 0)]
        [InlineData(0, 60)]
        [InlineData(10, 60)]
        public async Task Refill_uses_actual_capacities_without_a_loop(int clip, int reserve)
        {
            var f = Bo3Fixture.Create(); f.SetAmmo(clip, reserve);
            var result = await Act(f, TrainerAction.RefillAmmo);
            Assert.Equal(clip == 10 && reserve == 60 ? ActionOutcome.NoChange : ActionOutcome.Success, result.Outcome);
            Assert.Equal(10U, f.Transport.ReadUInt32(f.Client + 0x684)); Assert.Equal(60U, f.Transport.ReadUInt32(f.Client + 0x648));
            Assert.InRange(f.Transport.Calls.Count(c => c.Symbol == "BO3_AddWeaponAmmo"), 0, 1);
            Assert.Empty(f.Transport.Writes);
        }
        [Theory]
        [InlineData("unsupported")]
        [InlineData("two-guns")]
        [InlineData("special")]
        [InlineData("packed")]
        public async Task Unsupported_weapon_context_has_no_rpc(string condition)
        {
            var f = Bo3Fixture.Create(); var action = TrainerAction.GiveRk5;
            if (condition == "unsupported") { f.PutInventory("rifle_zm"); f.PutHeldWeapon("rifle_zm"); action = TrainerAction.RefillAmmo; }
            if (condition == "two-guns") f.PutInventory("pistol_revolver38_zm", "rifle_zm");
            if (condition == "special") f.Transport.Put(f.DefAddress(9) + 3712, new byte[] { 1 });
            if (condition == "packed") { f.Transport.Put64(f.Client + 0x378, 0x207); f.Transport.Put64(f.Client + 0x2C0, 0x207); }
            Assert.Equal(ActionOutcome.Blocked, (await Act(f, action)).Outcome); Assert.Empty(f.Transport.Calls);
        }
        [Fact]
        public async Task Missing_rk5_and_full_inventory_are_blocked_without_calls()
        {
            var f = Bo3Fixture.Create(); f.Transport.Put64(f.Base + 0xE978840 + 9 * 8, 0);
            Assert.Equal(ActionOutcome.Blocked, (await Act(f, TrainerAction.GiveRk5)).Outcome); Assert.Empty(f.Transport.Calls);
            f = Bo3Fixture.Create(); f.PutInventory(Enumerable.Repeat("pistol_revolver38_zm", 15).ToArray());
            Assert.Equal(ActionOutcome.Blocked, (await Act(f, TrainerAction.GiveRk5)).Outcome); Assert.Empty(f.Transport.Calls);
        }
        [Fact]
        public async Task Changed_held_weapon_during_getters_never_grants()
        {
            var f = Bo3Fixture.Create(); f.BeforeCall = c => { if (c.Symbol == "BO3_GetWeaponAlternate") f.PutHeldWeapon("rifle_zm"); };
            Assert.Equal(ActionOutcome.Unknown, (await Act(f, TrainerAction.GiveRk5)).Outcome);
            Assert.DoesNotContain(f.Transport.Calls, c => c.Symbol == "BO3_GivePlayerWeaponInternal");
        }
        [Fact]
        public async Task Refill_cancellation_after_add_has_no_second_add()
        {
            var f = Bo3Fixture.Create(); f.BeforeCall = c => { if (c.Symbol == "BO3_AddWeaponAmmo") f.Current = false; };
            Assert.Equal(ActionOutcome.Unknown, (await Act(f, TrainerAction.RefillAmmo)).Outcome);
            Assert.Single(f.Transport.Calls, c => c.Symbol == "BO3_AddWeaponAmmo");
        }
        [Fact]
        public async Task Context_changed_after_grant_blocks_ammo_and_reports_partial()
        {
            var f = Bo3Fixture.Create(); f.BeforeCall = c => { if (c.Symbol == "BO3_GivePlayerWeaponInternal") f.Current = false; };
            Assert.Equal(ActionOutcome.Unknown, (await Act(f, TrainerAction.GiveRk5)).Outcome);
            Assert.DoesNotContain(f.Transport.Calls, c => c.Symbol == "BO3_AddWeaponAmmo");
        }
    }
}

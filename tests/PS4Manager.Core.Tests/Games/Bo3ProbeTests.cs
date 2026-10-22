using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.Games;
using PS4Manager.Core.Games.Bo3;
using Xunit;

namespace PS4Manager.Core.Tests.Games
{
    public sealed class Bo3ProbeTests
    {
        [Fact]
        public async Task Probe_is_read_only_and_relocates_once()
        {
            var f = Bo3Fixture.Create(0x100400000);
            var s = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            Assert.True(s.Ready, s.Reason); Assert.Equal("Bloodhound", s.WeaponName);
            Assert.Equal("CUSA02624", s.Identity.TitleId); Assert.Equal(0x100400000UL, s.Identity.ModuleBase);
            Assert.Contains(0x106AEAFE0UL + 0x250, f.Transport.Reads);
            Assert.Empty(f.Transport.Writes); Assert.Empty(f.Transport.Calls);
        }

        [Theory]
        [InlineData("title")]
        [InlineData("signature")]
        [InlineData("dead")]
        [InlineData("zero-client")]
        [InlineData("unaligned-client")]
        [InlineData("large-table")]
        [InlineData("unterminated-name")]
        [InlineData("short-read")]
        [InlineData("changing-inventory")]
        public async Task Unsafe_probe_disables_actions_without_mutations(string damage)
        {
            var f = Bo3Fixture.Create();
            switch (damage) {
                case "title": f.SetTitleId("CUSA99999"); break;
                case "signature": f.CorruptSignature("BO3_PlayerStateDamageSkipCheck"); break;
                case "dead": f.PutHealth(0); break;
                case "zero-client": f.Transport.Put64(f.Entity + 0x250, 0); break;
                case "unaligned-client": f.Transport.Put64(f.Entity + 0x250, f.Client + 1); break;
                case "large-table": f.Transport.Put32(f.Base + 0xE978834, 512); break;
                case "unterminated-name": f.Transport.Put(f.Client + 0x16C44, Enumerable.Repeat((byte)65, 128).ToArray()); break;
                case "short-read": f.Transport.Memory.Remove(f.Client + 0x20); break;
                case "changing-inventory": int reads = 0; f.Transport.BeforeRead = a => { if (a == f.Client + 0x378 && ++reads > 1) f.Transport.Put64(a, 9); }; break;
            }
            var s = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
            Assert.False(s.Ready); Assert.False(string.IsNullOrEmpty(s.Reason));
            Assert.Empty(f.Transport.Writes); Assert.Empty(f.Transport.Calls);
        }

        [Fact]
        public async Task Cached_definitions_are_revalidated_without_rescanning_all_names()
        {
            var f = Bo3Fixture.Create();
            Assert.True((await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Ready);
            f.Transport.Reads.Clear();
            Assert.True((await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Ready);
            Assert.DoesNotContain(f.NameAddress(11), f.Transport.Reads);
            f.Transport.Put64(f.Base + 0xE978840 + 7 * 8, 0);
            Assert.False((await f.Trainer.ProbeAsync(f.Target, CancellationToken.None)).Ready);
        }
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenInventoryTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task Files_are_grouped_by_their_title_id()
        {
            var files = new[] { Item("/c/CUSA00411.json"), Item("/c/CUSA00411.txt"), Item("/c/PPSA00001.json") };

            var groups = await GoldHenInventory.GroupAsync(files, Name, None);

            Assert.Equal(2, groups.Count);
            Assert.Equal(new[] { "CUSA00411", "PPSA00001" }, groups.Select(g => g.TitleId));
            Assert.Equal(2, groups.First().Files.Count);
        }

        [Fact]
        public async Task The_label_is_the_resolved_game_name_and_falls_back_to_the_id()
        {
            var files = new[] { Item("/c/CUSA00411.json"), Item("/c/PPSA00001.json") };

            var groups = await GoldHenInventory.GroupAsync(files, Name, None);

            Assert.Equal("Grand Theft Auto V", groups.Single(g => g.TitleId == "CUSA00411").Label);
            Assert.Equal("PPSA00001", groups.Single(g => g.TitleId == "PPSA00001").Label);
        }

        [Fact]
        public async Task Files_without_a_title_id_land_in_one_group_without_an_id()
        {
            var files = new[] { Item("/c/readme.txt"), Item("/c/notes.json") };

            var groups = await GoldHenInventory.GroupAsync(files, Name, None);

            var group = Assert.Single(groups);
            Assert.Null(group.TitleId);
            Assert.Equal(GoldHenInventory.OtherLabel, group.Label);
            Assert.Equal(2, group.Files.Count);
        }

        [Fact]
        public async Task A_failing_name_lookup_does_not_lose_files()
        {
            var files = new[] { Item("/c/CUSA00411.json") };

            var groups = await GoldHenInventory.GroupAsync(files,
                (id, ct) => Task.FromException<string>(new InvalidOperationException("offline")), None);

            Assert.Equal("CUSA00411", Assert.Single(groups).Label);
        }

        [Fact]
        public void The_title_id_comes_from_the_file_name_without_its_extension()
        {
            Assert.Equal("CUSA00411", GoldHenInventory.TitleIdOf("/c/cheats/CUSA00411.json"));
            Assert.Null(GoldHenInventory.TitleIdOf("/c/cheats/readme.txt"));
            Assert.Null(GoldHenInventory.TitleIdOf("/c/cheats/CUSA0041.json"));
        }

        private static Task<string> Name(string titleId, CancellationToken ct)
        {
            return Task.FromResult(titleId == "CUSA00411" ? "Grand Theft Auto V" : null);
        }

        private static RemoteItem Item(string path)
        {
            return new RemoteItem(path, false, 10, DateTime.MinValue);
        }
    }
}

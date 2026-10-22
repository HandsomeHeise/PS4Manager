using System;
using PS4Manager.Core.Games;
using Xunit;

namespace PS4Manager.Core.Tests.Games
{
    public sealed class GameCatalogTests
    {
        [Fact]
        public void Offline_catalog_is_searchable_without_fake_games()
        {
            var catalog = new GameCatalog();
            var games = catalog.Filter("black ops", false, new InstalledGame[0]);
            Assert.Single(games);
            Assert.Equal("CUSA02624", games[0].TitleId);
            Assert.Equal("md5-bf1ffcd3", games[0].ProfileId);
            Assert.True(games[0].Experimental);
            Assert.Empty(catalog.Filter("", true, new InstalledGame[0]));
            Assert.Single(catalog.Filter(" cusa02624 ", false, null));
            Assert.Empty(catalog.Filter("not a game", false, null));
        }

        [Theory]
        [InlineData(InstalledState.Unknown, 0)]
        [InlineData(InstalledState.NotInstalled, 0)]
        [InlineData(InstalledState.Installed, 1)]
        public void Installed_filter_requires_positive_evidence(InstalledState state, int count)
        {
            var installed = new[] { new InstalledGame("CUSA02624", null, null, state, null) };
            Assert.Equal(count, new GameCatalog().Filter("", true, installed).Count);
        }

        [Fact]
        public void Identity_is_not_just_a_pid()
        {
            var a = new TrainerIdentity(1, "CONSOLE", 42, "cusa02624", 0x400000, "profile");
            Assert.Equal(a, new TrainerIdentity(1, "console", 42, "CUSA02624", 0x400000, "profile"));
            Assert.NotEqual(a, new TrainerIdentity(2, "console", 42, "CUSA02624", 0x400000, "profile"));
            Assert.NotEqual(a, new TrainerIdentity(1, "other", 42, "CUSA02624", 0x400000, "profile"));
            Assert.NotEqual(a, new TrainerIdentity(1, "console", 42, "CUSA02624", 0x800000, "profile"));
        }
    }
}

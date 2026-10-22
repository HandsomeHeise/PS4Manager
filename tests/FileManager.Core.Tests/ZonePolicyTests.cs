using System.Linq;
using FileManager.Core.Paths;
using FileManager.Core.Zones;
using Xunit;

namespace FileManager.Core.Tests
{
    public class ZonePolicyTests
    {
        [Fact]
        public void No_zone_root_lies_inside_another_root()
        {
            var roots = ZonePolicy.FreeRootsForTests.Concat(ZonePolicy.WarnRootsForTests).ToList();
            foreach (var root in roots)
                foreach (var other in roots)
                {
                    if (root == other) continue;
                    Assert.False(RemotePath.IsSameOrBelow(root, other),
                        "„" + root + "“ liegt unterhalb von „" + other + "“ — das verletzt die Nicht-Verschachtelungs-Invariante der Zonenwurzeln.");
                }
        }

        [Theory]
        // frei
        [InlineData("/data/x", Zone.Free)]
        [InlineData("/data/GoldHEN/plugins.ini", Zone.Free)]
        [InlineData("/user/data/pkg/a.pkg", Zone.Free)]
        [InlineData("/mnt/usb0/x", Zone.Free)]
        [InlineData("/mnt/usb7/a/b", Zone.Free)]
        [InlineData("/user/av_contents/photo/x.jpg", Zone.Free)]
        // Warnung
        [InlineData("/user/home/194450ad/savedata/x", Zone.Warn)]
        [InlineData("/user/home/194450ad", Zone.Warn)]
        [InlineData("/user/app/CUSA00411", Zone.Warn)]
        [InlineData("/user/patch/x", Zone.Warn)]
        [InlineData("/user/addcont/x", Zone.Warn)]
        [InlineData("/user/appmeta/x", Zone.Warn)]
        [InlineData("/mnt/ext0/user/app/CUSA23827", Zone.Warn)]
        // gesperrt — die Wurzeln der Zonen selbst gehören dazu
        [InlineData("/", Zone.Locked)]
        [InlineData("/data", Zone.Locked)]
        [InlineData("/user/data", Zone.Locked)]
        [InlineData("/mnt/usb0", Zone.Locked)]
        [InlineData("/mnt/usb8/x", Zone.Locked)]
        [InlineData("/user/av_contents", Zone.Locked)]
        [InlineData("/user/home", Zone.Locked)]
        [InlineData("/system/x", Zone.Locked)]
        [InlineData("/system_ex/x", Zone.Locked)]
        [InlineData("/system_data/priv/x", Zone.Locked)]
        [InlineData("/update/PS4UPDATE.PUP", Zone.Locked)]
        [InlineData("/preinst/x", Zone.Locked)]
        [InlineData("/preinst2/x", Zone.Locked)]
        [InlineData("/dev/x", Zone.Locked)]
        [InlineData("/mnt/sandbox/x", Zone.Locked)]
        [InlineData("/mnt/pfs/x", Zone.Locked)]
        [InlineData("/mnt/disc/x", Zone.Locked)]
        [InlineData("/mnt/ext0/x", Zone.Locked)]
        [InlineData("/mnt/ext0/system_data/x", Zone.Locked)]
        [InlineData("/user/settings/x", Zone.Locked)]
        [InlineData("/user/license/x", Zone.Locked)]
        [InlineData("/user/system/x", Zone.Locked)]
        [InlineData("/mini-syscore.elf", Zone.Locked)]
        public void The_zone_table_of_the_spec_holds(string path, Zone expected)
        {
            Assert.Equal(expected, new ZonePolicy().Classify(path));
        }

        [Theory]
        [InlineData("/data/../system/x", Zone.Locked)]
        [InlineData("//system/x", Zone.Locked)]
        [InlineData("/data/./x", Zone.Free)]
        [InlineData("/data/x/../../update/y", Zone.Locked)]
        [InlineData("data/x", Zone.Free)]
        [InlineData("/user/data/../system_data/x", Zone.Locked)]
        public void Tricks_with_the_path_do_not_get_through(string path, Zone expected)
        {
            Assert.Equal(expected, new ZonePolicy().Classify(path));
        }

        [Fact]
        public void Expert_mode_turns_locked_into_warn_and_leaves_the_rest()
        {
            var policy = new ZonePolicy { ExpertMode = true };
            Assert.Equal(Zone.Warn, policy.Effective("/system_data/x"));
            Assert.Equal(Zone.Free, policy.Effective("/data/x"));
            Assert.Equal(Zone.Warn, policy.Effective("/user/home/x"));
            Assert.Equal(Zone.Locked, policy.Classify("/system_data/x"));
        }

        [Fact]
        public void The_strictest_zone_wins()
        {
            var policy = new ZonePolicy();
            Assert.Equal(Zone.Free, policy.Strictest(new[] { "/data/a", "/mnt/usb0/b" }));
            Assert.Equal(Zone.Warn, policy.Strictest(new[] { "/data/a", "/user/home/x/y" }));
            Assert.Equal(Zone.Locked, policy.Strictest(new[] { "/data/a", "/user/home/x/y", "/system/z" }));
            Assert.Equal(Zone.Free, policy.Strictest(new string[0]));
        }

        [Fact]
        public void Zones_have_german_names()
        {
            Assert.Equal("Free", ZonePolicy.Describe(Zone.Free));
            Assert.Equal("Warning", ZonePolicy.Describe(Zone.Warn));
            Assert.Equal("Locked", ZonePolicy.Describe(Zone.Locked));
        }
    }
}

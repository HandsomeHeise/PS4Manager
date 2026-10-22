using System;
using FileManager.Core.Paths;
using Xunit;

namespace FileManager.Core.Tests
{
    public class RemotePathTests
    {
        [Theory]
        [InlineData(null, "/")]
        [InlineData("", "/")]
        [InlineData("/", "/")]
        [InlineData("/data/", "/data")]
        [InlineData("data/x", "/data/x")]
        [InlineData("//data//x", "/data/x")]
        [InlineData("/data/./x", "/data/x")]
        [InlineData("/data/../system", "/system")]
        [InlineData("/../..", "/")]
        [InlineData("/data/a b/c", "/data/a b/c")]
        public void Normalize_folds_the_path(string input, string expected)
        {
            Assert.Equal(expected, RemotePath.Normalize(input));
        }

        [Fact]
        public void Combine_parent_and_name_work_together()
        {
            Assert.Equal("/data/GoldHEN/plugins.ini", RemotePath.Combine("/data/GoldHEN/", "plugins.ini"));
            Assert.Equal("/x", RemotePath.Combine("/", "x"));
            Assert.Equal("/data/GoldHEN", RemotePath.Parent("/data/GoldHEN/plugins.ini"));
            Assert.Equal("/", RemotePath.Parent("/data"));
            Assert.Equal("/", RemotePath.Parent("/"));
            Assert.Equal("plugins.ini", RemotePath.Name("/data/GoldHEN/plugins.ini"));
            Assert.Equal("", RemotePath.Name("/"));
        }

        [Fact]
        public void Below_respects_segment_boundaries()
        {
            Assert.True(RemotePath.IsSameOrBelow("/data", "/data"));
            Assert.True(RemotePath.IsSameOrBelow("/data/x", "/data"));
            Assert.False(RemotePath.IsSameOrBelow("/database", "/data"));
            Assert.True(RemotePath.IsSameOrBelow("/anything", "/"));

            Assert.False(RemotePath.IsStrictlyBelow("/data", "/data"));
            Assert.True(RemotePath.IsStrictlyBelow("/data/x", "/data"));
            Assert.False(RemotePath.IsStrictlyBelow("/mnt/usb00/x", "/mnt/usb0"));
            Assert.False(RemotePath.IsStrictlyBelow("/", "/"));
            Assert.True(RemotePath.IsStrictlyBelow("/data", "/"));
        }

        [Theory]
        [InlineData("/user/data", "/data")]
        [InlineData("/user/data/pkg/a.pkg", "/data/pkg/a.pkg")]
        [InlineData("/user/database/x", "/user/database/x")]
        [InlineData("/user/data/../home", "/user/home")]
        [InlineData("/data/x", "/data/x")]
        public void Canonical_maps_user_data_onto_data(string input, string expected)
        {
            Assert.Equal(expected, RemotePath.Canonical(input));
        }

        [Theory]
        [InlineData("/mnt/usb0", true)]
        [InlineData("/mnt/usb7/Musik/a.mp3", true)]
        [InlineData("/mnt/usb8", false)]
        [InlineData("/mnt/usb", false)]
        [InlineData("/mnt/usb0x", false)]
        [InlineData("/data/a", false)]
        [InlineData("/mnt/ext0/user", false)]
        public void Only_usb_drives_ignore_case(string path, bool expected)
        {
            Assert.Equal(expected, RemotePath.IsCaseInsensitive(path));
        }

        [Fact]
        public void The_name_comparer_follows_the_drive()
        {
            Assert.True(RemotePath.NameComparer("/mnt/usb0").Equals("README.TXT", "Readme.txt"));
            Assert.False(RemotePath.NameComparer("/data").Equals("README.TXT", "Readme.txt"));
        }

        [Theory]
        [InlineData("/data", "/system", "/system")]
        [InlineData("/data/x", "../pkg", "/data/pkg")]
        [InlineData("/data", "GoldHEN/plugins", "/data/GoldHEN/plugins")]
        [InlineData("/data", "/mnt/usb0/", "/mnt/usb0")]
        public void A_link_target_resolves_against_the_folder_of_the_link(string dir, string target, string expected)
        {
            Assert.Equal(expected, RemotePath.ResolveLink(dir, target));
        }

        [Fact]
        public void A_link_without_target_is_refused()
        {
            Assert.Throws<ArgumentException>(() => RemotePath.ResolveLink("/data", ""));
        }
    }
}

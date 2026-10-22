using System;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    public class FtpClientTests
    {
        [Fact]
        public void The_uri_keeps_the_path_and_escapes_spaces()
        {
            var uri = FtpClient.BuildUri("10.10.10.236", 2121,
                "/user/av_contents/photo/NPXS20001/CUSA00411/104/mit leerzeichen.jpg");

            Assert.Equal("ftp://10.10.10.236:2121/user/av_contents/photo/NPXS20001/CUSA00411/104/mit%20leerzeichen.jpg",
                uri.AbsoluteUri);
        }

        [Fact]
        public void A_hash_in_a_name_does_not_truncate_the_path()
        {
            var uri = FtpClient.BuildUri("h", 21, "/a/b#c.jpg");
            Assert.EndsWith("/a/b%23c.jpg", uri.AbsoluteUri);
        }

        [Fact]
        public void A_control_character_in_the_path_is_refused()
        {
            Assert.Throws<ArgumentException>(() => FtpClient.BuildUri("10.10.10.236", 2121, "/data/a\nb"));
        }
    }
}

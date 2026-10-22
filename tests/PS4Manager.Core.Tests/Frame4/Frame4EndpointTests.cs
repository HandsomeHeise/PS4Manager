using System;
using PS4Manager.Core.Frame4;
using Xunit;

namespace PS4Manager.Core.Tests.Frame4
{
    public sealed class Frame4EndpointTests
    {
        [Fact]
        public void Defaults_target_the_frame4_http_and_uart_ports()
        {
            var endpoint = new Frame4Endpoint("  192.168.0.10  ");

            Assert.Equal("192.168.0.10", endpoint.Host);
            Assert.Equal(2812, endpoint.HttpPort);
            Assert.Equal(3321, endpoint.UartPort);
            Assert.Equal("http://192.168.0.10:2812/", endpoint.HttpBaseUri.AbsoluteUri);
        }

        [Fact]
        public void Bare_ipv6_hosts_are_bracketed_for_frame4_http_uris()
        {
            var endpoint = new Frame4Endpoint("::1");

            Assert.Equal("[::1]", endpoint.Host);
            Assert.Equal("http://[::1]:2812/", endpoint.HttpBaseUri.AbsoluteUri);
        }

        [Theory]
        [InlineData(null, 2812, 3321)]
        [InlineData("", 2812, 3321)]
        [InlineData("   ", 2812, 3321)]
        [InlineData("http://ps4", 2812, 3321)]
        [InlineData("ps4/path", 2812, 3321)]
        [InlineData("ps4\r\n", 2812, 3321)]
        [InlineData("ps4", 0, 3321)]
        [InlineData("ps4", 65536, 3321)]
        [InlineData("ps4", 2812, 0)]
        [InlineData("ps4", 2812, 65536)]
        public void Invalid_hosts_and_ports_are_rejected(string host, int httpPort, int uartPort)
        {
            Assert.ThrowsAny<ArgumentException>(() => new Frame4Endpoint(host, httpPort, uartPort));
        }
    }
}

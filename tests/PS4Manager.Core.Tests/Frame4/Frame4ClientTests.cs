using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;
using Xunit;

namespace PS4Manager.Core.Tests.Frame4
{
    public sealed class Frame4ClientTests
    {
        [Fact]
        public async Task Process_list_uses_the_frame4_http_endpoint()
        {
            Uri requested = null;
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
            {
                requested = uri;
                return "[{\"name\":\"SceShellCore\",\"pid\":101},{\"name\":\"eboot.bin\",\"pid\":202 }]\n";
            }));

            var processes = await client.GetProcessesAsync(new Frame4Endpoint("192.168.0.10"), CancellationToken.None);

            Assert.Equal("http://192.168.0.10:2812/process-list", requested.AbsoluteUri);
            Assert.Equal(new[] { "SceShellCore", "eboot.bin" }, processes.Select(p => p.Name));
            Assert.Equal(new[] { 101, 202 }, processes.Select(p => p.Pid));
        }

        [Fact]
        public async Task Process_list_errors_are_reported_with_frame4_message()
        {
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
                "{\"message\": \"Could not get list of processes.\"}\n"));

            var error = await Assert.ThrowsAsync<Frame4Exception>(() =>
                client.GetProcessesAsync(new Frame4Endpoint("192.168.0.10"), CancellationToken.None));

            Assert.Contains("Could not get list of processes.", error.Message);
        }

        [Fact]
        public async Task Process_info_uses_pid_query_and_parses_title_fields()
        {
            Uri requested = null;
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
            {
                requested = uri;
                return "{\"pid\":42,\"name\":\"SceShellCore\",\"path\":\"/system/SceShellCore\",\"title_id\":\"CUSA00411\",\"content_id\":\"IV0000-CUSA00411_00-GTAV000000000001\"}";
            }));

            var info = await client.GetProcessInfoAsync(new Frame4Endpoint("192.168.0.10"), 42, CancellationToken.None);

            Assert.Equal("http://192.168.0.10:2812/process-info?pid=42", requested.AbsoluteUri);
            Assert.Equal(42, info.Pid);
            Assert.Equal("SceShellCore", info.Name);
            Assert.Equal("CUSA00411", info.TitleId);
        }

        [Fact]
        public async Task Process_maps_parse_hex_addresses()
        {
            Uri requested = null;
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
            {
                requested = uri;
                return "[{\"name\":\"executable\",\"start\":\"0x1000\",\"end\":\"0x1FFF\",\"offset\":\"0x0\",\"prot\":\"5\"}]\n";
            }));

            var maps = await client.GetProcessMapsAsync(new Frame4Endpoint("192.168.0.10"), 42, CancellationToken.None);

            var map = Assert.Single(maps);
            Assert.Equal("http://192.168.0.10:2812/process-maps?pid=42", requested.AbsoluteUri);
            Assert.Equal(0x1000UL, map.Start);
            Assert.Equal(0x1FFFUL, map.End);
            Assert.Equal(5, map.Protection);
        }

        [Fact]
        public async Task Process_maps_ignore_invalid_ranges_reported_by_frame4()
        {
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
                "[{\"name\":\"bad\",\"start\":\"0x2000\",\"end\":\"0x10\",\"offset\":\"0x0\",\"prot\":\"0\"}," +
                "{\"name\":\"good\",\"start\":\"0x4000\",\"end\":\"0x5000\",\"offset\":\"0x0\",\"prot\":\"5\"}]"));

            var maps = await client.GetProcessMapsAsync(new Frame4Endpoint("192.168.0.10"), 42, CancellationToken.None);

            var map = Assert.Single(maps);
            Assert.Equal("good", map.Name);
            Assert.Equal(0x4000UL, map.Start);
        }

        [Fact]
        public async Task Process_maps_tolerate_unescaped_control_bytes_in_frame4_names()
        {
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
                "[{\"name\":\"bad" + '\b' + "name\",\"start\":\"0x2000\",\"end\":\"0x10\",\"offset\":\"0x0\",\"prot\":\"0\"}," +
                "{\"name\":\"executable\",\"start\":\"0x4000\",\"end\":\"0x5000\",\"offset\":\"0x0\",\"prot\":\"5\"}]"));

            var maps = await client.GetProcessMapsAsync(new Frame4Endpoint("192.168.0.10"), 42, CancellationToken.None);

            var map = Assert.Single(maps);
            Assert.Equal("executable", map.Name);
        }

        [Fact]
        public async Task Read_memory_parses_space_separated_hex_bytes()
        {
            Uri requested = null;
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
            {
                requested = uri;
                return "{\"bytes\": \"DE AD 00 EF\"}";
            }));

            var bytes = await client.ReadMemoryAsync(new Frame4Endpoint("192.168.0.10"), 42, 0x1000, 4, CancellationToken.None);

            Assert.Equal("http://192.168.0.10:2812/read-memory?pid=42&address=0x1000&length=0x4", requested.AbsoluteUri);
            Assert.Equal(new byte[] { 0xDE, 0xAD, 0x00, 0xEF }, bytes);
        }

        [Fact]
        public async Task Write_memory_sends_compact_hex_bytes()
        {
            Uri requested = null;
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
            {
                requested = uri;
                return "{\"success\": true}\n";
            }));

            await client.WriteMemoryAsync(new Frame4Endpoint("192.168.0.10"), 42, 0x1000,
                new byte[] { 0xDE, 0xAD, 0xEF }, CancellationToken.None);

            Assert.Equal("http://192.168.0.10:2812/write-memory?pid=42&address=0x1000&bytes=DEADEF", requested.AbsoluteUri);
        }

        [Fact]
        public async Task Allocate_and_free_memory_use_hex_lengths()
        {
            var requests = new System.Collections.Generic.List<string>();
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
            {
                requests.Add(uri.AbsoluteUri);
                return uri.AbsoluteUri.Contains("allocate-memory")
                    ? "{\"success\": true,\"allocated_address\": \"0xABC000\"}"
                    : "{\"success\":true}";
            }));

            var address = await client.AllocateMemoryAsync(new Frame4Endpoint("192.168.0.10"), 42, 0x2000, CancellationToken.None);
            await client.FreeMemoryAsync(new Frame4Endpoint("192.168.0.10"), 42, address, 0x2000, CancellationToken.None);

            Assert.Equal(0xABC000UL, address);
            Assert.Equal(new[]
            {
                "http://192.168.0.10:2812/allocate-memory?pid=42&length=0x2000",
                "http://192.168.0.10:2812/free-memory?pid=42&address=0xABC000&length=0x2000"
            }, requests);
        }

        [Fact]
        public async Task Notify_base64_encodes_the_message()
        {
            Uri requested = null;
            var client = new Frame4Client(new DelegateFrame4HttpTransport(uri =>
            {
                requested = uri;
                return "{\"success\": true}\n";
            }));

            await client.NotifyAsync(new Frame4Endpoint("192.168.0.10"), 222, "Hallo PS4", CancellationToken.None);

            Assert.Equal("http://192.168.0.10:2812/notify?messageType=222&message=SGFsbG8gUFM0", requested.AbsoluteUri);
        }

        private sealed class DelegateFrame4HttpTransport : IFrame4HttpTransport
        {
            private readonly Func<Uri, string> _get;

            public DelegateFrame4HttpTransport(Func<Uri, string> get)
            {
                _get = get;
            }

            public Task<string> GetStringAsync(Uri uri, CancellationToken ct)
            {
                return Task.FromResult(_get(uri));
            }
        }
    }
}

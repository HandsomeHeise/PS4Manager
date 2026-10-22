using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Luma;
using PS4Manager.Core.Luma.Studio;
using Xunit;

namespace PS4Manager.Core.Tests.Luma.Studio
{
    public sealed class StudioSocketTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Silent_real_net48_socket_is_closed_on_timeout_or_caller_cancellation(bool cancelCaller)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using (var caller = new CancellationTokenSource())
            using (var session = new StudioConnection(() => new LumaClient(new LoopbackTransport(((IPEndPoint)listener.LocalEndpoint).Port)) {
                RequestTimeoutMilliseconds = cancelCaller ? 5000 : 150
            })) {
                Task<string> connecting = null;
                try {
                    connecting = session.ConnectAsync(new LumaEndpoint("127.0.0.1", 9615), "gta5", "", caller.Token);
                    using (var peer = await listener.AcceptTcpClientAsync()) {
                        if (cancelCaller) caller.CancelAfter(150);
                        var winner = await Task.WhenAny(connecting, Task.Delay(3000));
                        Assert.Same(connecting, winner);
                        if (cancelCaller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
                        else await Assert.ThrowsAsync<LumaException>(() => connecting);
                        Assert.False(session.Connected);
                        Assert.Equal(0, session.Generation);
                    }
                } finally {
                    session.Disconnect(); listener.Stop();
                    if (connecting != null) try { await connecting; } catch { }
                }
            }
        }
        private sealed class LoopbackTransport : ILumaTransport
        {
            private readonly int port;
            public LoopbackTransport(int port) { this.port = port; }
            public Task<ILumaConnection> ConnectAsync(LumaEndpoint endpoint, CancellationToken ct) =>
                new LumaTcpTransport().ConnectAsync(new LumaEndpoint("127.0.0.1", port), ct);
        }
    }
}

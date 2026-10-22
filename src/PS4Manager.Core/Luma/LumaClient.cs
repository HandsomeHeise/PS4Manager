using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Luma
{
    public sealed class LumaClient : IDisposable
    {
        private readonly ILumaTransport _transport;
        private ushort _nextSequence;
        private ILumaConnection _connection;

        public LumaClient()
            : this(new LumaTcpTransport())
        {
        }

        public LumaClient(ILumaTransport transport)
            : this(transport, (ushort)new Random().Next(1, 0x10000))
        {
        }

        internal LumaClient(ILumaTransport transport, ushort initialSequence)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _nextSequence = initialSequence == 0 ? (ushort)1 : initialSequence;
        }

        public event EventHandler<LumaLogEventArgs> LogReceived;

        public bool Connected { get { return _connection != null; } }
        public int RequestTimeoutMilliseconds { get; set; } = 5000;

        public async Task ConnectAsync(LumaEndpoint endpoint, CancellationToken ct)
        {
            Disconnect();
            _connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false);
        }

        public void Disconnect()
        {
            Interlocked.Exchange(ref _connection, null)?.Dispose();
        }

        public async Task<string> HelloAsync(string token, CancellationToken ct)
        {
            var reply = await RequestAsync(LumaOperation.Hello, LumaProtocol.Text(token), ct).ConfigureAwait(false);
            if (reply.Operation != LumaOperation.Hello && reply.Operation != LumaOperation.Ok)
                ThrowForReply("HELLO", reply);
            return reply.Text;
        }

        public async Task<string> PingAsync(CancellationToken ct)
        {
            var reply = await RequestAsync(LumaOperation.Ping, null, ct).ConfigureAwait(false);
            if (reply.Operation != LumaOperation.Pong && reply.Operation != LumaOperation.Ok)
                ThrowForReply("PING", reply);
            return reply.Text;
        }

        public async Task<string> ReloadAsync(CancellationToken ct)
        {
            return await TextCommandAsync(LumaOperation.Reload, null, "RELOAD", ct).ConfigureAwait(false);
        }

        public async Task<string> ExecuteAsync(string lua, CancellationToken ct)
        {
            return await ExecuteAsync(lua, "console", ct).ConfigureAwait(false);
        }

        public async Task<string> ExecuteAsync(string lua, string chunkName, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(chunkName) || chunkName.Length > 100 || chunkName.IndexOf('\0') >= 0)
                throw new ArgumentException("Invalid Lua chunk label.", nameof(chunkName));
            var payload = Encoding.UTF8.GetBytes(chunkName + "\0" + (lua ?? ""));
            return await TextCommandAsync(LumaOperation.Exec, payload, "EXEC", ct).ConfigureAwait(false);
        }

        public Task<string> UploadScriptAsync(string fileName, string source, CancellationToken ct)
        {
            if (fileName == null || !System.Text.RegularExpressions.Regex.IsMatch(fileName,
                @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\.lua\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                throw new ArgumentException("Use a simple Lua filename, without folders (example: hello.lua).", nameof(fileName));
            var bytes = Encoding.UTF8.GetBytes(source ?? "");
            if (bytes.Length > 1024 * 1024) throw new ArgumentException("Lua source exceeds 1 MiB.", nameof(source));
            var prefix = Encoding.UTF8.GetBytes("scripts/" + fileName + "\0");
            var payload = new byte[prefix.Length + bytes.Length];
            Buffer.BlockCopy(prefix, 0, payload, 0, prefix.Length);
            Buffer.BlockCopy(bytes, 0, payload, prefix.Length, bytes.Length);
            return TextCommandAsync(LumaOperation.Put, payload, "PUT", ct);
        }

        public async Task<string> CommandAsync(string command, CancellationToken ct)
        {
            return await TextCommandAsync(LumaOperation.Cmd, LumaProtocol.Text(command), "CMD", ct).ConfigureAwait(false);
        }

        public async Task<LumaResourceList> GetResourcesAsync(CancellationToken ct)
        {
            var reply = await RequestAsync(LumaOperation.ResList, null, ct).ConfigureAwait(false);
            if (reply.Operation != LumaOperation.ResList && reply.Operation != LumaOperation.Ok)
                ThrowForReply("RESLIST", reply);
            return LumaResourceList.Parse(reply.Text);
        }

        public async Task<string> ResourceAsync(LumaOperation operation, string name, CancellationToken ct)
        {
            if (operation != LumaOperation.Start && operation != LumaOperation.Stop && operation != LumaOperation.Restart)
                throw new ArgumentOutOfRangeException(nameof(operation));
            return await TextCommandAsync(operation, LumaProtocol.Text(name), operation.ToString().ToUpperInvariant(), ct)
                .ConfigureAwait(false);
        }

        private async Task<string> TextCommandAsync(LumaOperation operation, byte[] payload, string name,
            CancellationToken ct)
        {
            var reply = await RequestAsync(operation, payload, ct).ConfigureAwait(false);
            if (reply.Operation != LumaOperation.Ok && reply.Operation != operation) ThrowForReply(name, reply);
            return reply.Text;
        }

        private async Task<LumaFrame> RequestAsync(LumaOperation operation, byte[] payload, CancellationToken ct)
        {
            var connection = Volatile.Read(ref _connection);
            if (connection == null) throw new LumaException("Luma is not connected.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                if (RequestTimeoutMilliseconds > 0) timeout.CancelAfter(RequestTimeoutMilliseconds);
                try
                {
                    var sequence = _nextSequence;
                    _nextSequence = _nextSequence == ushort.MaxValue ? (ushort)1 : (ushort)(_nextSequence + 1);
                    var frame = LumaProtocol.EncodeFrame(operation, sequence, payload);
                    await connection.Write.WriteAsync(frame, 0, frame.Length, timeout.Token).ConfigureAwait(false);
                    await connection.Write.FlushAsync(timeout.Token).ConfigureAwait(false);

                    for (;;)
                    {
                        var reply = await LumaProtocol.ReadFrameAsync(connection.Read, timeout.Token)
                            .ConfigureAwait(false);
                        if (reply.Sequence == 0)
                        {
                            if (reply.Operation == LumaOperation.Log || reply.Operation == LumaOperation.Event)
                                OnLog(reply.Text);
                            continue;
                        }
                        if (reply.Sequence == sequence) return reply;
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Disconnect();
                    throw new LumaException("Luma did not answer within " + RequestTimeoutMilliseconds +
                                            " ms. Disconnect VS Code or any other client and try again.");
                }
            }
        }

        private void OnLog(string text)
        {
            var handler = LogReceived;
            if (handler != null) handler(this, new LumaLogEventArgs(text));
        }

        private static void ThrowForReply(string command, LumaFrame reply)
        {
            if (reply.Operation == LumaOperation.Err || reply.Operation == LumaOperation.Busy)
                throw new LumaException("Luma " + command + " failed: " + reply.Text);
            throw new LumaException("Luma " + command + " returned unexpected operation " + reply.Operation + ".");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}

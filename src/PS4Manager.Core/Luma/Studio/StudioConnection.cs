using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Luma.Studio
{
    public sealed class StudioLogEventArgs : EventArgs
    {
        public StudioLogEventArgs(long generation, string text) { Generation = generation; Text = text; }
        public long Generation { get; }
        public string Text { get; }
    }

    // A client is never reused across sessions. All reads/writes, including log polling,
    // pass through one gate; a cancelled request invalidates its entire stream.
    public sealed class StudioConnection : IDisposable
    {
        private sealed class Session
        {
            public LumaClient Client;
            public readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            public long Id;
            public string Profile;
            public bool Ready;
        }
        private readonly object sync = new object();
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly Func<LumaClient> createClient;
        private readonly ILuaSyntaxChecker checker;
        private Session current;
        private long nextId;
        private bool disposed;

        public StudioConnection() : this(() => new LumaClient()) { }
        public StudioConnection(Func<LumaClient> createClient, ILuaSyntaxChecker checker = null)
        { this.createClient = createClient ?? throw new ArgumentNullException(nameof(createClient)); this.checker = checker ?? new LuaSyntaxChecker(); }
        public event EventHandler<StudioLogEventArgs> LogReceived;
        public bool Connected { get { lock (sync) return current != null && current.Ready && current.Client.Connected; } }
        public long Generation { get { lock (sync) return current?.Id ?? 0; } }
        public string Profile { get { lock (sync) return current?.Profile; } }

        public async Task<string> ConnectAsync(LumaEndpoint endpoint, string profile, string token, CancellationToken ct)
        {
            LuaApiCatalog.ForProfile(profile); // Reject unknown game before replacing a session.
            Disconnect();
            Session session;
            lock (sync) {
                if (disposed) throw new ObjectDisposedException(nameof(StudioConnection));
                session = new Session { Client = createClient(), Profile = profile, Id = ++nextId };
                current = session;
            }
            session.Client.LogReceived += (s, e) => {
                lock (sync) { if (current != session || session.Lifetime.IsCancellationRequested) return; }
                LogReceived?.Invoke(this, new StudioLogEventArgs(session.Id, e.Text));
            };
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Lifetime.Token)) {
                bool entered = false;
                try {
                    await gate.WaitAsync(linked.Token).ConfigureAwait(false); entered = true;
                    EnsureCurrent(session);
                    await session.Client.ConnectAsync(endpoint, linked.Token).ConfigureAwait(false);
                    EnsureCurrent(session);
                    var hello = await session.Client.HelloAsync(token ?? "", linked.Token).ConfigureAwait(false);
                    lock (sync) { EnsureCurrent(session); session.Ready = true; }
                    return hello;
                } catch { Invalidate(session); throw; }
                finally { if (entered) gate.Release(); }
            }
        }

        public Task<string> ExecuteAsync(string source, CancellationToken ct)
        { return ExecuteAsync(source, "console", ct); }
        public Task<string> ExecuteAsync(string source, string chunkName, CancellationToken ct)
        { Validate(source); return RunAsync((c, t) => c.ExecuteAsync(source, chunkName, t), ct); }
        public Task<string> UploadAsync(string name, string source, long authorizedGeneration, CancellationToken ct)
        { Validate(source); return RunAsync((c, t) => c.UploadScriptAsync(name, source, t), ct, authorizedGeneration); }
        public Task<string> ReloadAsync(CancellationToken ct) => RunAsync((c, t) => c.ReloadAsync(t), ct);
        public Task<string> PollAsync(CancellationToken ct) => RunAsync((c, t) => c.PingAsync(t), ct);

        private void Validate(string source)
        {
            var result = checker.Check(source ?? "", "console");
            if (!result.Success) throw new LumaException("Syntax check failed: " + result.Diagnostics[0].Message);
        }
        private async Task<string> RunAsync(Func<LumaClient, CancellationToken, Task<string>> action, CancellationToken ct, long? generation = null)
        {
            Session session;
            lock (sync) {
                session = current;
                if (generation.HasValue && (session == null || session.Id != generation)) throw new OperationCanceledException("Console/profile changed; confirm the upload again.");
                if (session == null || !session.Ready) throw new LumaException("Connect to Luma first.");
            }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Lifetime.Token)) {
                bool entered = false;
                try {
                    await gate.WaitAsync(linked.Token).ConfigureAwait(false); entered = true;
                    EnsureCurrent(session);
                    var result = await action(session.Client, linked.Token).ConfigureAwait(false);
                    EnsureCurrent(session);
                    return result;
                } catch (OperationCanceledException) { Invalidate(session); throw; }
                catch (IOException) { Invalidate(session); throw; }
                catch (ObjectDisposedException) { Invalidate(session); throw new OperationCanceledException("Luma disconnected."); }
                catch { if (!session.Client.Connected) Invalidate(session); throw; }
                finally { if (entered) gate.Release(); }
            }
        }
        private void EnsureCurrent(Session session)
        { lock (sync) if (current != session || session.Lifetime.IsCancellationRequested) throw new OperationCanceledException("Luma session changed."); }
        private void Invalidate(Session session)
        {
            lock (sync) { if (current == session) current = null; session.Ready = false; }
            session.Lifetime.Cancel(); session.Client.Dispose();
        }
        public void Disconnect()
        {
            Session session;
            lock (sync) { session = current; current = null; }
            if (session != null) Invalidate(session);
        }
        public void Dispose() { lock (sync) disposed = true; Disconnect(); }
    }
}

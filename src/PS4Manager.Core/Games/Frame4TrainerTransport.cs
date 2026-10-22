using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;

namespace PS4Manager.Core.Games
{
    public sealed class Frame4TrainerTransport : ITrainerTransport
    {
        private readonly Frame4Client http;
        private readonly Frame4PacketClient packets;
        private readonly SemaphoreSlim mutationGate = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();
        private readonly HashSet<string> uncertainInstall = new HashSet<string>();
        private TrainerIdentity stubIdentity;
        private ulong stub;
        private long epoch;

        private async Task VerifyStubAsync(TrainerTarget target, TrainerIdentity identity, ulong address, string processKey, CancellationToken ct)
        {
            // Frame4 851ef1c: rpcasm.h header + immutable code at 0xA3; mutable register/thread slots are excluded.
            // https://github.com/DeathRGH/frame4/blob/851ef1c6026ccf7fe04c352d58e7f172dcda3e7f/kdebugger/include/rpcasm.h
            try {
                var bytes = await ReadAsync(target, identity.Pid, address, 410, ct).ConfigureAwait(false);
                using (var hash = SHA256.Create()) {
                    string code = BitConverter.ToString(hash.ComputeHash(bytes, 163, 247)).Replace("-", "");
                    if (BitConverter.ToUInt32(bytes, 0) != 0x42545352 || BitConverter.ToUInt64(bytes, 4) != 163 ||
                        code != "BF0AACDC9D98F65C5513202DAE204C1127B9F56FD021B60C577D7474517A9609")
                        throw new InvalidDataException("RPC stub is missing or unsupported. Restart the game before further RPC actions.");
                }
            } catch {
                lock (sync) { stub = 0; stubIdentity = null; uncertainInstall.Add(processKey); }
                throw;
            }
        }

        public Frame4TrainerTransport(Frame4Client http, Frame4PacketClient packets)
        { this.http = http ?? throw new ArgumentNullException(nameof(http)); this.packets = packets ?? throw new ArgumentNullException(nameof(packets)); }

        public void Invalidate() { lock (sync) { epoch++; stub = 0; stubIdentity = null; } }
        private static void Guard(TrainerTarget target, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); target.SessionToken.ThrowIfCancellationRequested();
            if (!target.IsCurrent()) throw new OperationCanceledException("The console session changed.");
        }
        private static CancellationTokenSource Linked(TrainerTarget target, CancellationToken ct)
        { Guard(target, ct); return CancellationTokenSource.CreateLinkedTokenSource(ct, target.SessionToken); }

        public async Task<IReadOnlyList<Frame4Process>> GetProcessesAsync(TrainerTarget target, CancellationToken ct)
        {
            using (var linked = Linked(target, ct)) { var r = await http.GetProcessesAsync(target.Endpoint, linked.Token).ConfigureAwait(false); Guard(target, ct); return r; }
        }
        public async Task<Frame4ProcessInfo> GetProcessInfoAsync(TrainerTarget target, int pid, CancellationToken ct)
        {
            using (var linked = Linked(target, ct)) { var r = await http.GetProcessInfoAsync(target.Endpoint, pid, linked.Token).ConfigureAwait(false); Guard(target, ct); return r; }
        }
        public async Task<IReadOnlyList<Frame4PrxModule>> GetModulesAsync(TrainerTarget target, int pid, CancellationToken ct)
        {
            using (var linked = Linked(target, ct)) { var r = await packets.GetPrxModulesAsync(target.Endpoint, pid, linked.Token).ConfigureAwait(false); Guard(target, ct); return r; }
        }
        private static void Range(ulong address, uint length)
        {
            if (address == 0 || length == 0 || length > 4096 || address > ulong.MaxValue - length)
                throw new ArgumentOutOfRangeException(nameof(length), "Trainer reads/writes require a bounded valid range.");
        }
        public async Task<byte[]> ReadAsync(TrainerTarget target, int pid, ulong address, uint length, CancellationToken ct)
        {
            Range(address, length);
            using (var linked = Linked(target, ct)) {
                var bytes = await http.ReadMemoryAsync(target.Endpoint, pid, address, length, linked.Token).ConfigureAwait(false);
                Guard(target, ct);
                if (bytes.Length != length) throw new InvalidDataException("Incomplete trainer memory read.");
                return bytes;
            }
        }

        private async Task ValidateAsync(TrainerTarget target, TrainerIdentity identity, long capturedEpoch, CancellationToken ct)
        {
            Guard(target, ct);
            lock (sync) if (epoch != capturedEpoch) throw new OperationCanceledException("Trainer transport was invalidated.");
            if (identity == null || identity.Generation != target.Generation ||
                !string.Equals(identity.Host, target.Endpoint.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Trainer target does not match the confirmed session.");
            var info = await GetProcessInfoAsync(target, identity.Pid, ct).ConfigureAwait(false);
            var modules = await GetModulesAsync(target, identity.Pid, ct).ConfigureAwait(false);
            var main = modules.Where(m => m.Name == "eboot.bin").ToArray();
            if (info.Pid != identity.Pid || info.TitleId != identity.TitleId || main.Length != 1 || main[0].TextAddress != identity.ModuleBase)
                throw new InvalidOperationException("The running game changed. Refresh before trying again.");
            Guard(target, ct);
            lock (sync) if (epoch != capturedEpoch) throw new OperationCanceledException("Trainer transport was invalidated.");
        }

        public async Task WriteAsync(TrainerTarget target, TrainerIdentity identity, ulong address, byte[] bytes, CancellationToken ct)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes)); Range(address, (uint)bytes.Length);
            if (!await mutationGate.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("A trainer operation is in progress.");
            try {
                long capturedEpoch; lock (sync) capturedEpoch = epoch;
                using (var linked = Linked(target, ct)) {
                    await ValidateAsync(target, identity, capturedEpoch, linked.Token).ConfigureAwait(false);
                    Guard(target, linked.Token);
                    try { await http.WriteMemoryAsync(target.Endpoint, identity.Pid, address, bytes, linked.Token).ConfigureAwait(false); }
                    catch (Exception ex) { throw new TrainerDispatchException("Write outcome unknown. Do not retry automatically.", ex); }
                    Guard(target, linked.Token);
                }
            } finally { mutationGate.Release(); }
        }

        public async Task<ulong> CallAsync(TrainerTarget target, TrainerIdentity identity, ulong function, ulong[] arguments, CancellationToken ct)
        {
            if (arguments == null || arguments.Length > 6 || function == 0) throw new ArgumentException("RPC supports one address and up to six integer arguments.");
            if (!await mutationGate.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("A trainer operation is in progress.");
            try {
                long capturedEpoch; lock (sync) capturedEpoch = epoch;
                using (var linked = Linked(target, ct)) {
                    await ValidateAsync(target, identity, capturedEpoch, linked.Token).ConfigureAwait(false);
                    string processKey = identity.Host + ":" + identity.Pid + ":" + identity.ModuleBase + ":" + identity.TitleId;
                    ulong activeStub;
                    lock (sync) {
                        if (uncertainInstall.Contains(processKey)) throw new InvalidOperationException("RPC installation outcome is unknown. Restart the game before further RPC actions.");
                        activeStub = identity.Equals(stubIdentity) ? stub : 0;
                    }
                    if (activeStub == 0) {
                        Guard(target, linked.Token);
                        // Remember ambiguity even across detach; remote stub memory is never freed blindly.
                        lock (sync) uncertainInstall.Add(processKey);
                        try { activeStub = await packets.InstallRpcAsync(target.Endpoint, identity.Pid, linked.Token).ConfigureAwait(false); }
                        catch (Exception ex) { throw new TrainerDispatchException("RPC installation outcome unknown. Restart the game.", ex); }
                        lock (sync) {
                            if (epoch != capturedEpoch || !target.IsCurrent() || linked.IsCancellationRequested)
                                throw new TrainerDispatchException("RPC installation completed for an expired session.", null);
                            stub = activeStub; stubIdentity = identity; uncertainInstall.Remove(processKey);
                        }
                    }
                    await ValidateAsync(target, identity, capturedEpoch, linked.Token).ConfigureAwait(false);
                    await VerifyStubAsync(target, identity, activeStub, processKey, linked.Token).ConfigureAwait(false);
                    var a = new ulong[6]; Array.Copy(arguments, a, arguments.Length);
                    Guard(target, linked.Token);
                    try {
                        var result = await packets.CallRpcAsync(target.Endpoint, new Frame4RpcCall(identity.Pid, activeStub, function, a[0], a[1], a[2], a[3], a[4], a[5]), linked.Token).ConfigureAwait(false);
                        Guard(target, linked.Token); return result.Rax;
                    } catch (Exception ex) { throw new TrainerDispatchException("RPC outcome unknown. Do not repeat the action automatically.", ex); }
                }
            } finally { mutationGate.Release(); }
        }
    }
}

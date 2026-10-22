using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.Games;

namespace PS4Manager.Core.Tests.Games
{
    internal sealed class FakeTrainerTransport : ITrainerTransport
    {
        internal readonly Dictionary<ulong, byte> Memory = new Dictionary<ulong, byte>();
        internal readonly List<ulong> Reads = new List<ulong>();
        internal readonly List<Tuple<ulong, byte[]>> Writes = new List<Tuple<ulong, byte[]>>();
        internal readonly List<RpcCall> Calls = new List<RpcCall>();
        internal IReadOnlyList<Frame4Process> Processes = new[] { new Frame4Process("eboot.bin", 42) };
        internal Frame4ProcessInfo Info = new Frame4ProcessInfo(42, "eboot.bin", "", "CUSA02624", "");
        internal IReadOnlyList<Frame4PrxModule> Modules = new[] { new Frame4PrxModule(0, "eboot.bin", 0x400000, 0x1520000, 0x2000000, 0xF000000) };
        internal Action<ulong> BeforeRead;
        internal Action<ulong> AfterWrite;
        internal Func<ulong, Task> DelayRead { get; set; }
        internal Func<RpcCall, ulong> OnCall;
        internal readonly Dictionary<ulong, string> Symbols = new Dictionary<ulong, string>();
        internal void Put(ulong address, byte[] data) { for (int i = 0; i < data.Length; i++) Memory[address + (uint)i] = data[i]; }
        internal void Put32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal void Put64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        internal uint ReadUInt32(ulong address) => BitConverter.ToUInt32(Enumerable.Range(0, 4).Select(i => Memory[address + (uint)i]).ToArray(), 0);
        private static void Guard(TrainerTarget target, CancellationToken ct) { ct.ThrowIfCancellationRequested(); target.SessionToken.ThrowIfCancellationRequested(); if (!target.IsCurrent()) throw new OperationCanceledException(); }
        public Task<IReadOnlyList<Frame4Process>> GetProcessesAsync(TrainerTarget target, CancellationToken ct) { Guard(target, ct); return Task.FromResult(Processes); }
        public Task<Frame4ProcessInfo> GetProcessInfoAsync(TrainerTarget target, int pid, CancellationToken ct) { Guard(target, ct); return Task.FromResult(Info); }
        public Task<IReadOnlyList<Frame4PrxModule>> GetModulesAsync(TrainerTarget target, int pid, CancellationToken ct) { Guard(target, ct); return Task.FromResult(Modules); }
        public async Task<byte[]> ReadAsync(TrainerTarget target, int pid, ulong address, uint length, CancellationToken ct)
        {
            Guard(target, ct); if (length > 4096) throw new InvalidDataException("Unbounded read");
            Reads.Add(address); BeforeRead?.Invoke(address); if (DelayRead != null) await DelayRead(address);
            Guard(target, ct); var bytes = new byte[length];
            for (int i = 0; i < length; i++) { byte b; if (!Memory.TryGetValue(address + (uint)i, out b)) throw new InvalidDataException("Unreadable fixture address " + (address + (uint)i).ToString("X")); bytes[i] = b; }
            return bytes;
        }
        public Task WriteAsync(TrainerTarget target, TrainerIdentity identity, ulong address, byte[] bytes, CancellationToken ct)
        { Guard(target, ct); Writes.Add(Tuple.Create(address, bytes.ToArray())); Put(address, bytes); AfterWrite?.Invoke(address); return Task.CompletedTask; }
        public Task<ulong> CallAsync(TrainerTarget target, TrainerIdentity identity, ulong function, ulong[] arguments, CancellationToken ct)
        {
            Guard(target, ct); string symbol; if (!Symbols.TryGetValue(function, out symbol)) throw new InvalidOperationException("Unknown fixture RPC address");
            var call = new RpcCall { Identity = identity, Symbol = symbol, Arguments = arguments.ToArray() }; Calls.Add(call);
            if (OnCall == null) throw new InvalidOperationException("Unexpected fixture RPC");
            return Task.FromResult(OnCall(call));
        }
        internal int Invalidations;
        public void Invalidate() { Invalidations++; }
        internal sealed class RpcCall
        {
            internal string Symbol;
            internal ulong[] Arguments;
            internal TrainerIdentity Identity;
        }
    }
}

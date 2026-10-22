using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;

namespace PS4Manager.Core.Games
{
    public interface ITrainerTransport
    {
        Task<IReadOnlyList<Frame4Process>> GetProcessesAsync(TrainerTarget target, CancellationToken ct);
        Task<Frame4ProcessInfo> GetProcessInfoAsync(TrainerTarget target, int pid, CancellationToken ct);
        Task<IReadOnlyList<Frame4PrxModule>> GetModulesAsync(TrainerTarget target, int pid, CancellationToken ct);
        Task<byte[]> ReadAsync(TrainerTarget target, int pid, ulong address, uint length, CancellationToken ct);
        Task WriteAsync(TrainerTarget target, TrainerIdentity identity, ulong address, byte[] bytes, CancellationToken ct);
        Task<ulong> CallAsync(TrainerTarget target, TrainerIdentity identity, ulong function, ulong[] arguments, CancellationToken ct);
        void Invalidate();
    }

    // Network dispatch may have succeeded even when no response reaches the client.
    public sealed class TrainerDispatchException : IOException
    {
        public TrainerDispatchException(string message, Exception inner) : base(message, inner) { }
    }
}

using System;
using FileManager.Core.Zones;

namespace FileManager.Core.Transfers
{
    /// <summary>FTP calls already own the shared transfer slot. Do not acquire it again.</summary>
    public sealed class TransferTransactionContext
    {
        private readonly Action<string, long, long> report;
        internal TransferTransactionContext(ZoneGuardFtpClient ftp, Action<string, long, long> report)
        { Ftp = ftp; this.report = report; }
        public ZoneGuardFtpClient Ftp { get; }
        public void Report(string phase, long completed = 0, long total = 0)
        { report(phase, Math.Max(0, completed), Math.Max(0, total)); }
    }
}

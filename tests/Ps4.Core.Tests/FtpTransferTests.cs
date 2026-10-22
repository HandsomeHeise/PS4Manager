using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    public class FtpTransferTests
    {
        private static byte[] Pattern(int length)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = (byte)(i % 251);
            return data;
        }

        [Fact]
        public async Task Every_byte_arrives()
        {
            var source = Pattern(200000);
            var target = new MemoryStream();

            long copied = await FtpTransfer.CopyAsync(new MemoryStream(source), target,
                null, null, CancellationToken.None);

            Assert.Equal(source.Length, copied);
            Assert.Equal(source, target.ToArray());
        }

        [Fact]
        public async Task Progress_reports_the_running_total()
        {
            // 200000 Bytes bei 81920 Puffer = 3 Blöcke.
            // Bewusst ein direkter IProgress statt Progress<T>: Progress<T>
            // meldet über den SynchronizationContext und liefe im Test über den
            // ThreadPool — die Meldungen träfen dann zeitabhängig ein.
            var direct = new CollectingProgress();

            await FtpTransfer.CopyAsync(new MemoryStream(Pattern(200000)), Stream.Null,
                direct, null, CancellationToken.None);

            Assert.Equal(new long[] { 81920, 163840, 200000 }, direct.Reports.ToArray());
        }

        [Fact]
        public async Task The_idle_timeout_is_reset_once_per_block()
        {
            // Das ist der Kern der Sache: die Frist gilt pro Block, nicht für den
            // ganzen Transfer. Sonst bräche ein langer Clip nach 30 s ab.
            int resets = 0;
            await FtpTransfer.CopyAsync(new MemoryStream(Pattern(200000)), Stream.Null,
                null, () => resets++, CancellationToken.None);

            Assert.Equal(3, resets);
        }

        [Fact]
        public async Task An_empty_source_copies_nothing_and_resets_nothing()
        {
            int resets = 0;
            long copied = await FtpTransfer.CopyAsync(new MemoryStream(new byte[0]), Stream.Null,
                null, () => resets++, CancellationToken.None);

            Assert.Equal(0, copied);
            Assert.Equal(0, resets);
        }

        [Fact]
        public async Task Cancellation_stops_the_copy()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => FtpTransfer.CopyAsync(new MemoryStream(Pattern(200000)), Stream.Null,
                        null, null, cts.Token));
            }
        }

        private sealed class CollectingProgress : IProgress<long>
        {
            public readonly List<long> Reports = new List<long>();
            public void Report(long value) { Reports.Add(value); }
        }
    }
}

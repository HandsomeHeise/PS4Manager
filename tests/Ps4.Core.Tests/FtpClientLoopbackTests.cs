using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    public sealed class FtpClientLoopbackTests : IDisposable
    {
        private readonly LoopbackFtpServer _server = new LoopbackFtpServer();
        private readonly FtpClient _client;
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "ps4core-tests-" + Guid.NewGuid().ToString("N"));

        public FtpClientLoopbackTests()
        {
            _client = new FtpClient(_server.Host, _server.Port);
            Directory.CreateDirectory(_temp);
        }

        public void Dispose()
        {
            _server.Dispose();
            try { Directory.Delete(_temp, true); } catch (Exception) { }
        }

        private static byte[] Pattern(int length)
        {
            return Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        }

        private string LocalFile(string name, byte[] content)
        {
            var path = Path.Combine(_temp, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private sealed class Collect : IProgress<long>
        {
            public readonly List<long> Reports = new List<long>();
            public void Report(long value) { Reports.Add(value); }
        }

        [Fact]
        public async Task Upload_stores_the_file_and_reports_progress()
        {
            _server.AddDirectory("/data");
            var data = Pattern(200000);
            var progress = new Collect();

            await _client.UploadFromFileAsync(LocalFile("a.bin", data), "/data/a.bin", progress, CancellationToken.None);

            Assert.Equal(data, _server.GetFile("/data/a.bin"));
            Assert.Equal(200000, progress.Reports.Last());
        }

        [Fact]
        public async Task Append_sends_only_the_rest_of_the_local_file()
        {
            var data = Pattern(3000);
            _server.AddFile("/data/a.bin", data.Take(1000).ToArray());
            var progress = new Collect();

            await _client.AppendFromFileAsync(LocalFile("a.bin", data), 1000, "/data/a.bin", progress, CancellationToken.None);

            Assert.Equal(data, _server.GetFile("/data/a.bin"));
            Assert.Equal(2000, progress.Reports.Last());
            Assert.Contains(_server.CommandsSnapshot(), c => c.StartsWith("APPE"));
        }

        [Fact]
        public async Task Download_with_offset_continues_the_partial_local_file()
        {
            var data = Pattern(5000);
            _server.AddFile("/data/a.bin", data);
            var part = LocalFile("a.bin.part", data.Take(2000).ToArray());

            await _client.DownloadToFileAsync("/data/a.bin", part, 2000, null, CancellationToken.None);

            Assert.Equal(data, File.ReadAllBytes(part));
            Assert.Contains("REST 2000", _server.CommandsSnapshot());
        }

        [Fact]
        public async Task Download_with_offset_refuses_a_partial_file_of_the_wrong_length()
        {
            _server.AddFile("/data/a.bin", Pattern(5000));
            var part = LocalFile("a.bin.part", Pattern(10));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _client.DownloadToFileAsync("/data/a.bin", part, 2000, null, CancellationToken.None));
        }

        [Fact]
        public void OpenRead_streams_from_an_offset()
        {
            var data = Pattern(50000);
            _server.AddFile("/data/a.bin", data);

            byte[] read;
            using (var stream = _client.OpenRead("/data/a.bin", 10))
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                read = buffer.ToArray();
            }

            Assert.Equal(data.Skip(10).ToArray(), read);
        }

        [Fact]
        public async Task GetFileSize_reports_the_size()
        {
            _server.AddFile("/data/a.bin", Pattern(1234));
            Assert.Equal(1234, await _client.GetFileSizeAsync("/data/a.bin", CancellationToken.None));
        }

        [Fact]
        public async Task Directories_are_created_and_removed()
        {
            _server.AddDirectory("/data");

            await _client.MakeDirectoryAsync("/data/neu", CancellationToken.None);
            Assert.True(_server.DirectoryExists("/data/neu"));

            await _client.RemoveDirectoryAsync("/data/neu", CancellationToken.None);
            Assert.False(_server.DirectoryExists("/data/neu"));
        }

        [Fact]
        public async Task Rename_moves_a_file_into_another_directory()
        {
            _server.AddFile("/data/a/x.txt", new byte[] { 7 });
            _server.AddDirectory("/data/b");

            await _client.RenameAsync("/data/a/x.txt", "/data/b/y.txt", CancellationToken.None);

            Assert.Null(_server.GetFile("/data/a/x.txt"));
            Assert.Equal(new byte[] { 7 }, _server.GetFile("/data/b/y.txt"));
        }

        [Fact]
        public async Task A_missing_file_is_a_permanent_ftp_exception_in_plain_text()
        {
            _server.AddDirectory("/data");
            var ex = await Assert.ThrowsAsync<FtpException>(() => _client.DownloadAsync("/data/fehlt.bin", CancellationToken.None));
            Assert.Equal(FtpErrorKind.Permanent, ex.Kind);
            Assert.Equal(550, ex.StatusCode);
            Assert.Equal("Not found or access denied", ex.Message);
        }

        [Fact]
        public async Task Removing_a_non_empty_directory_is_refused_permanently()
        {
            _server.AddFile("/data/voll/x.txt", new byte[] { 1 });
            var ex = await Assert.ThrowsAsync<FtpException>(() => _client.RemoveDirectoryAsync("/data/voll", CancellationToken.None));
            Assert.Equal(FtpErrorKind.Permanent, ex.Kind);
        }

        [Fact]
        public async Task A_cut_data_connection_is_transient()
        {
            _server.AddFile("/data/a.bin", Pattern(500000));
            _server.CutDataAfterBytes = 1000;

            var ex = await Assert.ThrowsAsync<FtpException>(() =>
                _client.DownloadToFileAsync("/data/a.bin", Path.Combine(_temp, "a.part"), null, CancellationToken.None));
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public async Task A_cut_download_can_keep_its_partial_file_for_a_later_resume()
        {
            _server.AddFile("/data/a.bin", Pattern(500000));
            _server.CutDataAfterBytes = 1000;
            var part = Path.Combine(_temp, "a.part");

            var ex = await Assert.ThrowsAsync<FtpException>(() =>
                _client.DownloadToFileAsync("/data/a.bin", part, 0, null, true, CancellationToken.None));

            Assert.True(ex.IsTransient);
            Assert.True(new FileInfo(part).Length > 0);
        }

        [Fact]
        public async Task Keeping_a_partial_does_not_leave_a_file_after_a_permanent_failure()
        {
            _server.AddDirectory("/data");
            var part = Path.Combine(_temp, "missing.part");

            var ex = await Assert.ThrowsAsync<FtpException>(() =>
                _client.DownloadToFileAsync("/data/missing.bin", part, 0, null, true, CancellationToken.None));

            Assert.False(ex.IsTransient);
            Assert.False(File.Exists(part));
        }

        [Fact]
        public async Task A_refused_connection_is_transient()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int unused = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var ex = await Assert.ThrowsAsync<FtpException>(() =>
                new FtpClient("127.0.0.1", unused).ListAsync("/", CancellationToken.None));
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public async Task User_cancellation_surfaces_as_OperationCanceledException()
        {
            _server.AddDirectory("/data");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.ListAsync("/data", cts.Token));
            }
        }

        [Fact]
        public async Task A_non_ascii_name_round_trips_through_list_and_delete()
        {
            // Befund Task 2: GoldHEN speichert, was FtpWebRequest schickt (Encoding.Default).
            _server.AddDirectory("/data");
            await _client.UploadFromFileAsync(LocalFile("u.txt", new byte[] { 1 }), "/data/Ärger ü.txt", null, CancellationToken.None);

            var listed = (await _client.ListAsync("/data", CancellationToken.None)).Single().Name;
            Assert.Equal("Ärger ü.txt", listed);

            await _client.DeleteAsync("/data/" + listed, CancellationToken.None);
            Assert.Empty(await _client.ListAsync("/data", CancellationToken.None));
        }

        [Fact]
        public async Task A_utf8_named_file_from_another_tool_stays_reachable()
        {
            // FileZilla & Co. legen Namen als UTF-8 ab. Wir zeigen sie als Mojibake,
            // erreichen sie aber byte-genau.
            _server.AddFileWithRawName("/data", System.Text.Encoding.UTF8.GetBytes("Ärger.txt"), new byte[] { 9 });

            var listed = (await _client.ListAsync("/data", CancellationToken.None)).Single().Name;

            Assert.Equal(new byte[] { 9 }, await _client.DownloadAsync("/data/" + listed, CancellationToken.None));
        }

        [Fact]
        public async Task A_missing_local_upload_source_is_a_local_error_not_a_connection_loss()
        {
            _server.AddDirectory("/data");
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _client.UploadFromFileAsync(Path.Combine(_temp, "gibtsnicht.bin"), "/data/x.bin", null, CancellationToken.None));
        }

        [Fact]
        public async Task A_locked_local_target_is_a_local_error_not_a_connection_loss()
        {
            _server.AddFile("/data/a.bin", Pattern(1000));
            var target = Path.Combine(_temp, "gesperrt.bin");

            using (new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Exakt IOException, keine FtpException: die Zieldatei wird VOR
                // Guard geöffnet (Finding A), die Sperre trifft also unübersetzt zu.
                var ex = await Assert.ThrowsAsync<IOException>(() =>
                    _client.DownloadToFileAsync("/data/a.bin", target, null, CancellationToken.None));
                Assert.IsNotType<FtpException>(ex);
            }
        }

        [Fact]
        public async Task Closing_an_OpenRead_stream_early_returns_quickly()
        {
            _server.AddFile("/data/a.bin", Pattern(5 * 1024 * 1024));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using (var stream = _client.OpenRead("/data/a.bin", 0))
            {
                var buffer = new byte[1024];
                stream.Read(buffer, 0, buffer.Length);
            }
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 5000,
                "Dispose dauerte " + stopwatch.ElapsedMilliseconds + " ms — request.Abort() fehlt?");

            // Der Client bleibt danach benutzbar (dieselbe zugrunde liegende Verbindung
            // hängt nicht in einer FtpWebRequest fest).
            Assert.NotEmpty(await _client.ListAsync("/data", CancellationToken.None));
        }

        [Fact]
        public async Task A_failed_download_at_offset_zero_does_not_leave_a_stub_file()
        {
            _server.AddDirectory("/data");
            var target = Path.Combine(_temp, "fehlt.bin");

            await Assert.ThrowsAsync<FtpException>(() =>
                _client.DownloadToFileAsync("/data/fehlt.bin", target, null, CancellationToken.None));

            Assert.False(File.Exists(target));
        }

        [Fact]
        public async Task A_failed_resume_download_keeps_the_partial_local_file()
        {
            _server.AddDirectory("/data");
            var part = LocalFile("a.part", Pattern(500));

            await Assert.ThrowsAsync<FtpException>(() =>
                _client.DownloadToFileAsync("/data/fehlt.bin", part, 500, null, CancellationToken.None));

            Assert.True(File.Exists(part));
            Assert.Equal(500, new FileInfo(part).Length);
        }

        [Fact]
        public async Task RenameAsync_rejects_a_control_character_in_the_target_and_sends_no_RNFR()
        {
            _server.AddFile("/data/x.txt", new byte[] { 7 });

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _client.RenameAsync("/data/x.txt", "/data/x\ny", CancellationToken.None));

            Assert.DoesNotContain(_server.CommandsSnapshot(), c => c.StartsWith("RNFR"));
        }

        [Fact]
        public async Task Append_refuses_an_offset_beyond_the_local_file()
        {
            _server.AddDirectory("/data");
            var local = LocalFile("a.bin", Pattern(100));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _client.AppendFromFileAsync(local, 200, "/data/a.bin", null, CancellationToken.None));
        }
    }
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Tests.Support;
using FileManager.Core.Zones;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class ZoneGuardFtpClientTests : IDisposable
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly ZonePolicy _policy = new ZonePolicy();
        private readonly ZoneGuardFtpClient _guard;
        private readonly string _local = Path.GetTempFileName();

        public ZoneGuardFtpClientTests()
        {
            _guard = new ZoneGuardFtpClient(_ftp, _policy);
            _ftp.AddFile("/system_data/priv/x.dat", "sys").AddFile("/data/a.txt", "a").AddDir("/system_data/leer");
            File.WriteAllText(_local, "lokal");
        }

        public void Dispose()
        {
            File.Delete(_local);
        }

        private async Task AssertRefused(Func<Task> write)
        {
            var ex = await Assert.ThrowsAsync<ZoneViolationException>(write);
            Assert.StartsWith("/system", ex.Path);
            Assert.Equal(0, _ftp.WriteCallCount);
        }

        [Fact]
        public Task Upload_into_a_locked_zone_never_reaches_the_wire()
        {
            return AssertRefused(() => _guard.UploadFromFileAsync(_local, "/system_data/priv/neu.dat", null, CancellationToken.None));
        }

        [Fact]
        public Task Append_into_a_locked_zone_never_reaches_the_wire()
        {
            return AssertRefused(() => _guard.AppendFromFileAsync(_local, 0, "/system_data/priv/x.dat", null, CancellationToken.None));
        }

        [Fact]
        public Task Mkdir_in_a_locked_zone_never_reaches_the_wire()
        {
            return AssertRefused(() => _guard.MakeDirectoryAsync("/system_data/neu", CancellationToken.None));
        }

        [Fact]
        public Task Rmdir_in_a_locked_zone_never_reaches_the_wire()
        {
            return AssertRefused(() => _guard.RemoveDirectoryAsync("/system_data/leer", CancellationToken.None));
        }

        [Fact]
        public Task Delete_in_a_locked_zone_never_reaches_the_wire()
        {
            return AssertRefused(() => _guard.DeleteAsync("/system_data/priv/x.dat", CancellationToken.None));
        }

        [Fact]
        public Task Renaming_out_of_a_locked_zone_is_refused()
        {
            return AssertRefused(() => _guard.RenameAsync("/system_data/priv/x.dat", "/data/x.dat", CancellationToken.None));
        }

        [Fact]
        public async Task Renaming_into_a_locked_zone_is_refused()
        {
            var ex = await Assert.ThrowsAsync<ZoneViolationException>(
                () => _guard.RenameAsync("/data/a.txt", "/system_data/a.txt", CancellationToken.None));
            Assert.Equal("/system_data/a.txt", ex.Path);
            Assert.Equal(0, _ftp.WriteCallCount);
        }

        [Fact]
        public async Task Reading_a_locked_zone_is_allowed()
        {
            Assert.Equal("sys", System.Text.Encoding.UTF8.GetString(
                await _guard.DownloadAsync("/system_data/priv/x.dat", CancellationToken.None)));
            Assert.NotEmpty(await _guard.ListAsync("/system_data/priv", CancellationToken.None));
        }

        [Fact]
        public async Task Writes_in_the_free_zone_pass()
        {
            await _guard.UploadFromFileAsync(_local, "/data/neu.txt", null, CancellationToken.None);
            Assert.NotNull(_ftp.File("/data/neu.txt"));
        }

        [Fact]
        public async Task Expert_mode_lets_a_locked_write_through()
        {
            _policy.ExpertMode = true;
            await _guard.DeleteAsync("/system_data/priv/x.dat", CancellationToken.None);
            Assert.Null(_ftp.File("/system_data/priv/x.dat"));
        }

        [Fact]
        public void The_violation_explains_itself_in_english()
        {
            var ex = new ZoneViolationException("/system/x");
            Assert.Contains("Locked area", ex.Message);
            Assert.Contains("/system/x", ex.Message);
            Assert.Contains("Expert Mode", ex.Message);
        }
    }
}

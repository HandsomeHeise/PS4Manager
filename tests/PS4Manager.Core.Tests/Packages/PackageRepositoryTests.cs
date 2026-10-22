using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Packages;
using Ps4.Core.Ftp;
using Xunit;

namespace PS4Manager.Core.Tests.Packages
{
    public sealed class PackageRepositoryTests
    {
        [Fact]
        public async Task DiscoverAsync_lists_pkg_files_from_known_install_locations()
        {
            var ftp = new FakeFtp();
            ftp.AddDirectory("/data/pkg");
            ftp.AddDirectory("/mnt/usb0");
            ftp.AddDirectory("/mnt/usb1");
            ftp.AddFile("/data/pkg/Beta.pkg", 200);
            ftp.AddFile("/data/pkg/readme.txt", 10);
            ftp.AddFile("/mnt/usb0/Alpha.PKG", 100);
            ftp.AddFile("/mnt/usb1/Gamma.pkg", 300);

            var packages = await new PackageRepository(ftp).DiscoverAsync(CancellationToken.None);

            Assert.Equal(new[]
            {
                "/mnt/usb0/Alpha.PKG",
                "/data/pkg/Beta.pkg",
                "/mnt/usb1/Gamma.pkg"
            }, packages.Select(p => p.Path));
            Assert.Equal("USB 0", packages[0].Location);
            Assert.Equal("Internal", packages[1].Location);
            Assert.Equal("USB 1", packages[2].Location);
        }

        [Fact]
        public async Task DiscoverAsync_ignores_missing_locations()
        {
            var ftp = new FakeFtp();
            ftp.AddDirectory("/data/pkg");
            ftp.AddFile("/data/pkg/App.pkg", 42);

            var packages = await new PackageRepository(ftp).DiscoverAsync(CancellationToken.None);

            var package = Assert.Single(packages);
            Assert.Equal("/data/pkg/App.pkg", package.Path);
            Assert.Equal(42, package.Size);
        }

        [Theory]
        [InlineData("")]
        [InlineData("demo.txt")]
        [InlineData("demo.prx")]
        public void IsPackageFile_rejects_non_pkg_names(string name)
        {
            Assert.False(PackageRepository.IsPackageFile(name));
        }

        [Theory]
        [InlineData("demo.pkg")]
        [InlineData("DEMO.PKG")]
        public void IsPackageFile_accepts_pkg_names(string name)
        {
            Assert.True(PackageRepository.IsPackageFile(name));
        }

        private sealed class FakeFtp : IFtpClient
        {
            private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.Ordinal) { "/" };
            private readonly Dictionary<string, FtpEntry> _files = new Dictionary<string, FtpEntry>(StringComparer.Ordinal);

            public void AddDirectory(string path)
            {
                _directories.Add(path);
            }

            public void AddFile(string path, long size)
            {
                var slash = path.LastIndexOf('/');
                var directory = slash <= 0 ? "/" : path.Substring(0, slash);
                _directories.Add(directory);
                _files[path] = new FtpEntry(path.Substring(slash + 1), false, size);
            }

            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                if (!_directories.Contains(path))
                    throw new FtpException("missing", FtpErrorKind.Permanent, 550);

                var prefix = path == "/" ? "/" : path + "/";
                var entries = _files
                    .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)
                                   && pair.Key.Substring(prefix.Length).IndexOf('/') < 0)
                    .Select(pair => pair.Value)
                    .ToList();
                return Task.FromResult<IReadOnlyList<FtpEntry>>(entries);
            }

            public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public System.IO.Stream OpenRead(string path, long offset) { throw new NotSupportedException(); }
            public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task MakeDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task RemoveDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DeleteAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using PS4Manager.Core.GoldHen;
using Ps4.Core.Ftp;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenDiscoveryServiceTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task A_console_without_goldhen_reports_no_root_and_no_error()
        {
            var console = new FakeConsole();
            console.AddDir("/data");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.False(discovery.RootExists);
            Assert.Empty(discovery.Plugins);
            Assert.Null(discovery.ConfigurationPath);
            Assert.All(discovery.Locations, l => Assert.False(l.Exists));
        }

        [Fact]
        public async Task A_partial_install_reports_only_what_is_there()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddDir("/data/GoldHEN/plugins");
            console.AddFile("/data/GoldHEN/plugins/hen.prx", "plugin");
            console.AddFile("/data/GoldHEN/plugins.ini", "; hen.prx\n");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.True(discovery.RootExists);
            Assert.True(discovery.Location(GoldHenSection.Plugins).Exists);
            Assert.False(discovery.Location(GoldHenSection.Cheats).Exists);
            Assert.False(discovery.Location(GoldHenSection.Patches).Exists);
        }

        [Fact]
        public async Task The_first_existing_configuration_candidate_is_the_activation_source()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddFile("/data/GoldHEN/config.ini", "");
            console.AddFile("/data/GoldHEN/plugins.txt", "");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.Equal("/data/GoldHEN/config.ini", discovery.ConfigurationPath);
            Assert.Equal(new[] { "/data/GoldHEN/config.ini", "/data/GoldHEN/plugins.txt" },
                discovery.ConfigurationFiles);
        }

        [Fact]
        public async Task Plugins_the_configuration_does_not_mention_are_disabled_without_a_line()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddDir("/data/GoldHEN/plugins");
            console.AddFile("/data/GoldHEN/plugins/hen.prx", "a");
            console.AddFile("/data/GoldHEN/plugins/tool.prx", "b");
            console.AddFile("/data/GoldHEN/plugins.ini", "hen.prx\n");

            var discovery = await Service(console).DiscoverAsync(None);

            var hen = discovery.Plugins.Single(p => p.Name == "hen.prx");
            var tool = discovery.Plugins.Single(p => p.Name == "tool.prx");
            Assert.Equal(GoldHenActivation.Enabled, hen.Activation);
            Assert.Equal(GoldHenActivation.Disabled, tool.Activation);
            Assert.Equal(-1, tool.LineIndex);
        }

        [Fact]
        public async Task Without_a_configuration_every_plugin_stays_unknown()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddDir("/data/GoldHEN/plugins");
            console.AddFile("/data/GoldHEN/plugins/hen.prx", "a");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.Equal(GoldHenActivation.Unknown, Assert.Single(discovery.Plugins).Activation);
            Assert.False(discovery.Location(GoldHenSection.Configuration).Exists);
        }

        [Fact]
        public async Task An_unexpected_listing_failure_is_not_reported_as_missing()
        {
            var console = new FakeConsole();
            console.AddDir("/data");
            console.FailListing("/data", new InvalidOperationException("broken parser"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(console).DiscoverAsync(None));

            Assert.Equal("broken parser", error.Message);
        }

        [Fact]
        public async Task A_listed_goldhen_folder_that_cannot_be_read_is_an_error()
        {
            var console = new FakeConsole();
            console.AddDir("/data");
            console.AddDir("/data/GoldHEN");
            console.FailListing("/data/GoldHEN", new FtpException("Kein Zugriff", FtpErrorKind.Permanent, 550));

            await Assert.ThrowsAsync<FtpException>(() => Service(console).DiscoverAsync(None));
        }

        [Fact]
        public async Task Paths_are_written_with_forward_slashes_and_the_root_prefix()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddDir("/data/GoldHEN/plugins");
            console.AddFile("/data/GoldHEN/plugins/hen.prx", "a");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.Equal("/data/GoldHEN/plugins/hen.prx", Assert.Single(discovery.Plugins).Path);
        }

        private static GoldHenDiscoveryService Service(FakeConsole console)
        {
            var gates = new FtpGates();
            return new GoldHenDiscoveryService(new DirectoryCache(console, gates), console, gates);
        }

        /// <summary>Konsole im Speicher: Ordner, Dateien, sonst permanentes 550.</summary>
        private sealed class FakeConsole : IFtpClient
        {
            private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            private readonly HashSet<string> _dirs = new HashSet<string>(StringComparer.Ordinal) { "/" };
            private readonly Dictionary<string, Exception> _listingFailures =
                new Dictionary<string, Exception>(StringComparer.Ordinal);

            public void AddDir(string path)
            {
                var current = path;
                while (!string.IsNullOrEmpty(current))
                {
                    _dirs.Add(current);
                    if (current == "/") break;
                    var slash = current.LastIndexOf('/');
                    current = slash <= 0 ? "/" : current.Substring(0, slash);
                }
            }

            public void FailListing(string path, Exception error) { _listingFailures[path] = error; }

            public void AddFile(string path, string text)
            {
                var dir = path.Substring(0, path.LastIndexOf('/'));
                if (dir.Length == 0) dir = "/";
                _dirs.Add(dir);
                _files[path] = new UTF8Encoding(false).GetBytes(text);
            }

            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                Exception failure;
                if (_listingFailures.TryGetValue(path, out failure)) throw failure;
                if (!_dirs.Contains(path)) throw NotFound();
                var prefix = path == "/" ? "/" : path + "/";
                var entries = new List<FtpEntry>();
                foreach (var dir in _dirs.Where(d => d.StartsWith(prefix, StringComparison.Ordinal) && d.Length > prefix.Length)
                                          .Select(d => d.Substring(prefix.Length)).Where(n => n.IndexOf('/') < 0))
                    entries.Add(new FtpEntry(dir, true, 0));
                foreach (var file in _files.Keys.Where(f => f.StartsWith(prefix, StringComparison.Ordinal)
                                                            && f.Substring(prefix.Length).IndexOf('/') < 0))
                    entries.Add(new FtpEntry(file.Substring(prefix.Length), false, _files[file].Length));
                return Task.FromResult<IReadOnlyList<FtpEntry>>(entries);
            }

            public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
            {
                byte[] bytes;
                if (!_files.TryGetValue(path, out bytes)) throw NotFound();
                return Task.FromResult(bytes);
            }

            /// <summary>Wie die vorhandenen Tests ein fehlendes Ziel melden (InMemoryFtpClient): permanent 550.</summary>
            private static FtpException NotFound()
            {
                return new FtpException("Nicht vorhanden oder kein Zugriff", FtpErrorKind.Permanent, 550);
            }

            public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Stream OpenRead(string path, long offset) { throw new NotSupportedException(); }
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

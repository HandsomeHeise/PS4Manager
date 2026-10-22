using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Presentation;
using FileManager.Core.Tests.Support;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class FolderBrowserTests
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly DirectoryCache _cache;
        private readonly FolderBrowser _browser;

        public FolderBrowserTests()
        {
            _ftp.AddFile("/data/GoldHEN/plugins.ini", "[default]").AddDir("/data/pkg").AddDir("/user/home/1/savedata").AddDir("/system/x");
            _cache = new DirectoryCache(_ftp, new FtpGates());
            _browser = new FolderBrowser(_cache, new ZonePolicy());
        }

        [Fact]
        public async Task Navigating_lists_the_folder_and_records_the_place()
        {
            var view = await _browser.NavigateAsync("/data/GoldHEN/", CancellationToken.None);

            Assert.Equal("/data/GoldHEN", view.Path);
            Assert.Equal("plugins.ini", view.Entries.Single().Name);
            Assert.Equal("/data/GoldHEN", _browser.CurrentPath);
            Assert.False(_browser.CanGoBack);
            Assert.True(_browser.CanGoUp);
        }

        [Fact]
        public async Task Back_forward_and_up_move_through_the_places()
        {
            await _browser.NavigateAsync("/data", CancellationToken.None);
            await _browser.NavigateAsync("/data/pkg", CancellationToken.None);

            Assert.Equal("/data", (await _browser.BackAsync(CancellationToken.None)).Path);
            Assert.True(_browser.CanGoForward);
            Assert.Equal("/data/pkg", (await _browser.ForwardAsync(CancellationToken.None)).Path);
            Assert.Equal("/data", (await _browser.UpAsync(CancellationToken.None)).Path);
            Assert.Equal("/", (await _browser.UpAsync(CancellationToken.None)).Path);
            Assert.False(_browser.CanGoUp);
        }

        [Fact]
        public async Task A_failed_navigation_keeps_the_old_place()
        {
            await _browser.NavigateAsync("/data", CancellationToken.None);

            var ex = await Assert.ThrowsAsync<FtpException>(() => _browser.NavigateAsync("/data/gibtsnicht", CancellationToken.None));

            Assert.Equal(550, ex.StatusCode);
            Assert.Equal("/data", _browser.CurrentPath);
            Assert.False(_browser.CanGoBack);
        }

        [Fact]
        public async Task A_cancelled_navigation_keeps_the_old_place()
        {
            await _browser.NavigateAsync("/data", CancellationToken.None);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _browser.NavigateAsync("/data/pkg", cts.Token));

            Assert.Equal("/data", _browser.CurrentPath);
        }

        [Fact]
        public async Task A_link_is_followed_to_where_it_really_points()
        {
            _ftp.AddLink("/data/sys", "/system");

            var view = await _browser.NavigateAsync("/data/sys/x", CancellationToken.None);

            Assert.Equal("/system/x", view.Path);
            Assert.Equal(Zone.Locked, view.Zone);                       // nicht „frei" wie /data/**
        }

        [Fact]
        public async Task A_relative_link_resolves_against_its_folder()
        {
            _ftp.AddLink("/data/GoldHEN/ablage", "../pkg");

            Assert.Equal("/data/pkg", await PathResolver.ResolveAsync(_cache, "/data/GoldHEN/ablage", CancellationToken.None));
        }

        [Fact]
        public async Task A_link_loop_is_stopped()
        {
            _ftp.AddLink("/data/a", "/data/b").AddLink("/data/b", "/data/a");

            await Assert.ThrowsAsync<InvalidOperationException>(() => _browser.NavigateAsync("/data/a", CancellationToken.None));
        }

        [Fact]
        public async Task Refresh_reads_the_folder_again()
        {
            await _browser.NavigateAsync("/data/pkg", CancellationToken.None);
            _ftp.AddFile("/data/pkg/neu.pkg", "p");

            var view = await _browser.RefreshAsync(CancellationToken.None);

            Assert.Equal("neu.pkg", view.Entries.Single().Name);
            Assert.Equal(2, _ftp.CallsSnapshot().Count(c => c == "LIST /data/pkg"));
        }

        [Theory]
        [InlineData("/data", Zone.Free)]
        [InlineData("/", Zone.Locked)]
        [InlineData("/user/home", Zone.Warn)]
        [InlineData("/mnt/usb0", Zone.Free)]
        public void The_zone_is_the_zone_for_writing_into_the_folder(string folder, Zone expected)
        {
            Assert.Equal(expected, FolderBrowser.ContentsZone(new ZonePolicy(), folder));
        }
    }
}

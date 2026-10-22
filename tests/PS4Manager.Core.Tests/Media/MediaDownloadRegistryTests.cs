using System.Threading.Tasks;
using PS4Manager.Core.Media;
using Xunit;

namespace PS4Manager.Core.Tests.Media
{
    public sealed class MediaDownloadRegistryTests
    {
        [Fact]
        public async Task Late_session_A_cleanup_cannot_remove_session_B_same_key_download()
        {
            var registry = new MediaDownloadRegistry();
            var firstCompletion = new TaskCompletionSource<string>();
            var secondCompletion = new TaskCompletionSource<string>();
            var unexpectedFactoryCalls = 0;

            var first = registry.GetOrAdd("CUSA00001/clip.mp4", () => firstCompletion.Task);
            registry.Reset();
            var second = registry.GetOrAdd("CUSA00001/clip.mp4", () => secondCompletion.Task);

            firstCompletion.SetResult("first.mp4");
            Assert.Equal("first.mp4", await first);

            var observed = registry.GetOrAdd("CUSA00001/clip.mp4", () =>
            {
                unexpectedFactoryCalls++;
                return Task.FromResult("duplicate.mp4");
            });

            Assert.Same(second, observed);
            Assert.Equal(0, unexpectedFactoryCalls);
            secondCompletion.SetResult("second.mp4");
            Assert.Equal("second.mp4", await second);
        }
    }
}

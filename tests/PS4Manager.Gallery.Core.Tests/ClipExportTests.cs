using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Gallery.Core.Clips;
using Xunit;

namespace PS4Manager.Gallery.Core.Tests
{
    public sealed class ClipExportTests : IDisposable
    {
        private readonly string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "clip-test-" + Guid.NewGuid())).FullName;

        [Fact]
        public void Range_keeps_fractional_marks() => Assert.Equal(TimeSpan.FromSeconds(2.5), Range(1250, 3750, 10000).Duration);

        [Theory]
        [InlineData(-1, 1, 10)] [InlineData(5, 5, 10)] [InlineData(8, 5, 10)]
        [InlineData(0, 11, 10)] [InlineData(0, 1, 0)]
        public void Invalid_range_is_rejected(int start, int end, int duration) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Range(start, end, duration));

        [Fact]
        public void Both_marks_are_required_and_reset_drops_them()
        {
            var marks = new ClipMarks();
            marks.SetStart(TimeSpan.FromSeconds(2));
            Assert.False(marks.TryGetRange(TimeSpan.FromSeconds(10), out _));
            marks.SetEnd(TimeSpan.FromSeconds(1));
            Assert.False(marks.TryGetRange(TimeSpan.FromSeconds(10), out _));
            marks.SetEnd(TimeSpan.FromSeconds(4));
            Assert.True(marks.TryGetRange(TimeSpan.FromSeconds(10), out var range));
            Assert.Equal(TimeSpan.FromSeconds(2), range.Duration);
            marks.Reset();
            Assert.False(marks.TryGetRange(TimeSpan.FromSeconds(10), out _));
        }

        [Fact]
        public async Task Save_copies_the_exact_preview_and_never_overwrites()
        {
            string preview = FilePath("preview.mp4"), destination = FilePath("mein Ausschnitt ü.mp4");
            byte[] expected = { 1, 2, 3 };
            File.WriteAllBytes(preview, expected);
            await ClipExport.SaveAsync(preview, destination, CancellationToken.None);
            Assert.Equal(expected, File.ReadAllBytes(destination));
            File.WriteAllBytes(preview, new byte[] { 4 });
            await Assert.ThrowsAsync<IOException>(() => ClipExport.SaveAsync(preview, destination, CancellationToken.None));
            Assert.Equal(expected, File.ReadAllBytes(destination));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        [Fact]
        public async Task Cancelled_save_never_publishes_a_file()
        {
            string preview = FilePath("preview.mp4"), destination = FilePath("cancelled.mp4");
            File.WriteAllBytes(preview, new byte[] { 1, 2, 3 });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClipExport.SaveAsync(preview, destination, new CancellationToken(true)));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task Missing_encoder_leaves_source_and_destination_untouched()
        {
            var source = FilePath("source.mp4");
            File.WriteAllBytes(source, new byte[] { 9 });
            await Assert.ThrowsAsync<FileNotFoundException>(() => ClipExport.EncodeAsync(FilePath("ffmpeg.exe"), source, Range(0, 500, 1000), FilePath("preview.mp4"), CancellationToken.None));
            Assert.Single(Directory.GetFiles(directory));
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(source));
        }

        private static ClipRange Range(int start, int end, int duration) => new ClipRange(TimeSpan.FromMilliseconds(start), TimeSpan.FromMilliseconds(end), TimeSpan.FromMilliseconds(duration));

        [Fact]
        public async Task Real_encoder_preserves_source_and_saves_the_rendered_bytes()
        {
            // Opt-in on machines with an explicit, trusted test encoder and media fixture.
            var encoder = Environment.GetEnvironmentVariable("PS4MANAGER_TEST_FFMPEG");
            var fixture = Environment.GetEnvironmentVariable("PS4MANAGER_TEST_CLIP");
            if (encoder == null || fixture == null) return;
            var source = FilePath("Original mit Leerzeichen ü.mp4");
            File.Copy(fixture, source);
            var original = File.ReadAllBytes(source);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var preview = FilePath("Vorschau.mp4");
                await ClipExport.EncodeAsync(encoder, source, Range(1250, 3750, 5000), preview, CancellationToken.None);
                await ClipExport.SaveAsync(preview, FilePath("Ausschnitt.mp4"), CancellationToken.None);
                Assert.Equal(File.ReadAllBytes(preview), File.ReadAllBytes(FilePath("Ausschnitt.mp4")));
                Assert.Equal(original, File.ReadAllBytes(source));
                Assert.Equal(3, Directory.GetFiles(directory).Length);
                var output = Environment.GetEnvironmentVariable("PS4MANAGER_TEST_CLIP_OUTPUT");
                if (!string.IsNullOrEmpty(output)) File.Copy(preview, output);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Fact]
        public async Task Real_encoder_failure_and_cancellation_leave_no_output()
        {
            var encoder = Environment.GetEnvironmentVariable("PS4MANAGER_TEST_FFMPEG");
            var fixture = Environment.GetEnvironmentVariable("PS4MANAGER_TEST_CLIP");
            if (encoder == null || fixture == null) return;
            var broken = FilePath("broken.mp4");
            File.WriteAllText(broken, "not a video");
            var output = FilePath("preview.mp4");
            await Assert.ThrowsAsync<IOException>(() => ClipExport.EncodeAsync(encoder, broken, Range(0, 500, 1000), output, CancellationToken.None));
            Assert.Single(Directory.GetFiles(directory));

            using (var cancel = new CancellationTokenSource())
            {
                var task = ClipExport.EncodeAsync(encoder, fixture, Range(1250, 3750, 5000), output, cancel.Token);
                cancel.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            }
            Assert.Single(Directory.GetFiles(directory));
        }
        private string FilePath(string name) => Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}

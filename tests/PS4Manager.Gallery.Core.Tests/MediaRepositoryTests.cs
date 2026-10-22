using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using PS4Manager.Gallery.Core.Ps4;
using Xunit;

namespace PS4Manager.Gallery.Core.Tests
{
    /// <summary>In-Memory-FTP: Pfad → Bytes bzw. Pfad → Einträge.</summary>
    internal sealed class FakeFtpClient : IFtpClient
    {
        public readonly Dictionary<string, List<FtpEntry>> Listings =
            new Dictionary<string, List<FtpEntry>>();
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            List<FtpEntry> entries;
            if (!Listings.TryGetValue(path, out entries))
                throw new InvalidOperationException("LIST auf unbekanntem Pfad: " + path);
            return Task.FromResult<IReadOnlyList<FtpEntry>>(entries);
        }

        public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
        {
            byte[] data;
            if (!Files.TryGetValue(path, out data))
                throw new InvalidOperationException("550 " + path);
            return Task.FromResult(data);
        }

        public async Task DownloadToFileAsync(string path, string localPath,
            IProgress<long> progress, CancellationToken ct)
        {
            byte[] data;
            if (!Files.TryGetValue(path, out data))
                throw new InvalidOperationException("550 " + path);

            using (var target = new System.IO.FileStream(localPath, System.IO.FileMode.Create,
                System.IO.FileAccess.Write, System.IO.FileShare.None, FtpTransfer.BufferSize, true))
            {
                await FtpTransfer.CopyAsync(new System.IO.MemoryStream(data), target,
                    progress, null, ct).ConfigureAwait(false);
            }
        }

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task DownloadToFileAsync(string path, string localPath, long offset,
            IProgress<long> progress, CancellationToken ct)
        {
            if (offset != 0) throw new NotSupportedException("Die Gallery setzt keine Downloads fort.");
            return DownloadToFileAsync(path, localPath, progress, ct);
        }

        public System.IO.Stream OpenRead(string path, long offset) { throw new NotSupportedException(); }

        public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct)
        { throw new NotSupportedException(); }

        public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath,
            IProgress<long> progress, CancellationToken ct)
        { throw new NotSupportedException(); }

        public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        public Task MakeDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        public Task RemoveDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { throw new NotSupportedException(); }
    }

    public class MediaRepositoryTests
    {
        private static FtpEntry Dir(string name) { return new FtpEntry(name, true, 512); }
        private static FtpEntry File(string name, long size) { return new FtpEntry(name, false, size); }

        private const string PhotoRoot = "/user/av_contents/photo/NPXS20001";
        private const string VideoRoot = "/user/av_contents/video/NPXS20001";

        /// <summary>
        /// Baut die real erhobene Struktur nach: CUSA00411 hat Fotos UND einen
        /// Clip, CUSA12555 hat ausschließlich einen Clip (und darum im Foto-Zweig
        /// gar kein Verzeichnis — LIST darauf wirft, das muss der Normalfall sein).
        /// </summary>
        private static FakeFtpClient NewPs4()
        {
            var ftp = new FakeFtpClient();
            ftp.Listings[PhotoRoot] = new List<FtpEntry> { Dir("CUSA00411") };
            ftp.Listings[VideoRoot] = new List<FtpEntry> { Dir("CUSA00411"), Dir("CUSA12555") };

            ftp.Listings[PhotoRoot + "/CUSA00411"] = new List<FtpEntry> { Dir("104"), Dir("3d5") };
            ftp.Listings[PhotoRoot + "/CUSA00411/104"] = new List<FtpEntry>
            {
                File("20260801_084933_00470650.jpg", 461788),
                File("20260801_084933_00470650.dat", 592)
            };
            ftp.Listings[PhotoRoot + "/CUSA00411/3d5"] = new List<FtpEntry>
            {
                File("20260801_103432_00907244.jpg", 469114),
                File("20260801_103432_00907244.dat", 592)
            };

            ftp.Listings[VideoRoot + "/CUSA00411"] = new List<FtpEntry> { Dir("2c7") };
            ftp.Listings[VideoRoot + "/CUSA00411/2c7"] = new List<FtpEntry>
            {
                File("20260810_212645_00367837.mp4", 15454876),
                File("20260810_212645_00367837.dat", 592)
            };

            ftp.Listings[VideoRoot + "/CUSA12555"] = new List<FtpEntry> { Dir("1a0") };
            ftp.Listings[VideoRoot + "/CUSA12555/1a0"] = new List<FtpEntry>
            {
                File("20260809_180000_00111111.mp4", 8000000)
            };

            // param.sfo/icon0.png fehlen absichtlich → Fallback-Pfad.
            return ftp;
        }

        [Fact]
        public async Task Photos_and_clips_land_in_one_list_newest_first()
        {
            var repo = new MediaRepository(NewPs4());
            var games = await repo.LoadAsync(CancellationToken.None);

            var gta = games.Single(g => g.TitleId == "CUSA00411");
            Assert.Equal(3, gta.Items.Count);
            Assert.Equal("20260810_212645_00367837.mp4", gta.Items[0].FileName);   // 10.08.
            Assert.Equal(MediaKind.Video, gta.Items[0].Kind);
            Assert.Equal("20260801_103432_00907244.jpg", gta.Items[1].FileName);   // 01.08. 10:34
            Assert.Equal("20260801_084933_00470650.jpg", gta.Items[2].FileName);   // 01.08. 08:49
            Assert.All(gta.Items.Skip(1), i => Assert.Equal(MediaKind.Photo, i.Kind));
        }

        [Fact]
        public async Task A_game_with_only_clips_still_shows_up()
        {
            // Ohne Vereinigung der Title-IDs aus BEIDEN Wurzeln fiele dieses
            // Spiel komplett aus der Ansicht.
            var repo = new MediaRepository(NewPs4());
            var games = await repo.LoadAsync(CancellationToken.None);

            var clipOnly = games.Single(g => g.TitleId == "CUSA12555");
            var item = Assert.Single(clipOnly.Items);
            Assert.Equal(MediaKind.Video, item.Kind);
            Assert.Equal("20260809_180000_00111111.mp4", item.FileName);
        }

        [Fact]
        public async Task The_dat_companion_is_never_listed_as_a_clip()
        {
            var repo = new MediaRepository(NewPs4());
            var games = await repo.LoadAsync(CancellationToken.None);

            Assert.All(games.SelectMany(g => g.Items),
                i => Assert.False(i.FileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public async Task A_console_without_a_video_branch_still_loads_photos()
        {
            var ftp = NewPs4();
            // Den GANZEN Zweig entfernen, nicht nur die Wurzel: sonst fände
            // CollectAsync die Unterverzeichnisse weiterhin und der Test wäre
            // grün, ohne das Gemeinte zu prüfen.
            foreach (var key in ftp.Listings.Keys.Where(k => k.StartsWith(VideoRoot)).ToList())
                ftp.Listings.Remove(key);

            var repo = new MediaRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);

            var game = Assert.Single(games);
            Assert.Equal("CUSA00411", game.TitleId);
            Assert.Equal(2, game.Items.Count);
        }

        [Fact]
        public async Task A_dead_connection_fails_loudly()
        {
            // Beide Wurzeln unerreichbar heißt: kaputte Verbindung, nicht
            // "leere Konsole". Sonst meldete die App still "0 Spiele".
            var ftp = new FakeFtpClient();

            var repo = new MediaRepository(ftp);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.LoadAsync(CancellationToken.None));
        }

        [Fact]
        public async Task An_unreadable_hash_dir_fails_the_load_instead_of_hiding_items()
        {
            // Das Title-Dir-Listing hat "2c7" gerade erst gemeldet — verschwindet
            // der Ordner dann beim Listen, ist das eine Anomalie (Rechtefehler,
            // Transportfehler, Race mit der Konsole), keine normale Abwesenheit.
            // Ein stiller Continue würde die Gallery verkürzen, ohne dass der
            // Nutzer merkt, dass ihm Elemente fehlen.
            var ftp = NewPs4();
            ftp.Listings.Remove(VideoRoot + "/CUSA00411/2c7");

            var repo = new MediaRepository(ftp);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.LoadAsync(CancellationToken.None));
        }

        [Fact]
        public async Task A_missing_sfo_falls_back_to_the_title_id()
        {
            var repo = new MediaRepository(NewPs4());
            var games = await repo.LoadAsync(CancellationToken.None);
            var gta = games.Single(g => g.TitleId == "CUSA00411");
            Assert.Equal("CUSA00411", gta.Name);
            Assert.Null(gta.IconBytes);
        }

        [Fact]
        public async Task A_present_sfo_and_icon_are_used()
        {
            var ftp = NewPs4();
            ftp.Files["/system_data/priv/appmeta/CUSA00411/param.sfo"] =
                System.IO.File.ReadAllBytes(System.IO.Path.Combine("TestData", "param.sfo"));
            ftp.Files["/user/appmeta/CUSA00411/icon0.png"] = new byte[] { 1, 2, 3 };

            var repo = new MediaRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);

            var gta = games.Single(g => g.TitleId == "CUSA00411");
            Assert.Equal("Grand Theft Auto V", gta.Name);
            Assert.Equal(new byte[] { 1, 2, 3 }, gta.IconBytes);
        }

        [Fact]
        public async Task Downloading_a_clip_as_bytes_is_refused()
        {
            // Ein Clip als byte[] wäre doppelter Speicher und bei langen
            // Aufnahmen ein OOM-Kandidat — dafür gibt es DownloadToFileAsync.
            var repo = new MediaRepository(NewPs4());
            var games = await repo.LoadAsync(CancellationToken.None);
            var clip = games.Single(g => g.TitleId == "CUSA00411").Items[0];

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.DownloadFullAsync(clip, CancellationToken.None));
        }
    }
}

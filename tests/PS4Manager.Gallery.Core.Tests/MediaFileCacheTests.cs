using System;
using System.IO;
using System.Linq;
using PS4Manager.Gallery.Core.Caching;
using Xunit;

namespace PS4Manager.Gallery.Core.Tests
{
    public class MediaFileCacheTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "sg-video-cache-tests-" + Guid.NewGuid().ToString("N"));

        private const long Budget = 1000;

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private MediaFileCache NewCache() { return new MediaFileCache(_root, Budget); }

        private void Store(MediaFileCache cache, string titleId, string name, int bytes)
        {
            var part = cache.BeginWrite(titleId, name);
            File.WriteAllBytes(part, new byte[bytes]);
            cache.Commit(titleId, name);
        }

        [Fact]
        public void Begin_write_yields_a_part_file_next_to_the_target()
        {
            var cache = NewCache();
            var part = cache.BeginWrite("CUSA00411", "clip.mp4");

            Assert.EndsWith(".part", part);
            Assert.Equal(cache.PathFor("CUSA00411", "clip.mp4") + ".part", part);
            Assert.True(Directory.Exists(Path.GetDirectoryName(part)));
        }

        [Fact]
        public void Only_a_committed_file_counts_as_a_hit()
        {
            // Ein abgebrochener Download darf nicht als Treffer gelten, sonst
            // ist der Clip dauerhaft unabspielbar.
            var cache = NewCache();
            var part = cache.BeginWrite("CUSA00411", "clip.mp4");
            File.WriteAllBytes(part, new byte[100]);

            Assert.Null(cache.TryGetPath("CUSA00411", "clip.mp4"));

            cache.Commit("CUSA00411", "clip.mp4");
            Assert.Equal(cache.PathFor("CUSA00411", "clip.mp4"), cache.TryGetPath("CUSA00411", "clip.mp4"));
        }

        [Fact]
        public void Publish_makes_the_file_visible_before_separate_budget_maintenance()
        {
            var cache = NewCache();
            Store(cache, "CUSA00411", "old.mp4", 800);
            var part = cache.BeginWrite("CUSA00411", "new.mp4");
            File.WriteAllBytes(part, new byte[800]);

            var published = cache.Publish("CUSA00411", "new.mp4");

            Assert.NotNull(cache.TryGetPath("CUSA00411", "old.mp4"));
            Assert.Equal(published, cache.TryGetPath("CUSA00411", "new.mp4"));

            cache.Maintain(published);

            Assert.Null(cache.TryGetPath("CUSA00411", "old.mp4"));
            Assert.Equal(published, cache.TryGetPath("CUSA00411", "new.mp4"));
        }

        [Fact]
        public void Begin_write_clears_a_stale_part_file()
        {
            var cache = NewCache();
            var part = cache.BeginWrite("CUSA00411", "clip.mp4");
            File.WriteAllBytes(part, new byte[100]);

            var again = cache.BeginWrite("CUSA00411", "clip.mp4");
            Assert.False(File.Exists(again));
        }

        [Fact]
        public void Session_staged_writes_are_isolated_and_cleanup_cannot_remove_a_replacement()
        {
            var cache = NewCache();
            var first = cache.BeginStagedWrite("CUSA00411", "clip.mp4");
            var second = cache.BeginStagedWrite("CUSA00411", "clip.mp4");
            File.WriteAllBytes(first, new byte[] { 1 });
            File.WriteAllBytes(second, new byte[] { 2 });

            cache.Discard(first);

            Assert.False(File.Exists(first));
            Assert.True(File.Exists(second));
            var published = cache.Publish("CUSA00411", "clip.mp4", second);
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(published));
        }

        [Fact]
        public void A_miss_yields_null()
        {
            Assert.Null(NewCache().TryGetPath("CUSA00411", "fehlt.mp4"));
        }

        [Fact]
        public void A_zero_byte_file_counts_as_a_miss()
        {
            var cache = NewCache();
            var target = cache.PathFor("CUSA00411", "leer.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target, new byte[0]);

            Assert.Null(cache.TryGetPath("CUSA00411", "leer.mp4"));
        }

        [Fact]
        public void Remove_deletes_the_entry()
        {
            var cache = NewCache();
            Store(cache, "CUSA00411", "clip.mp4", 100);

            cache.Remove("CUSA00411", "clip.mp4");
            Assert.Null(cache.TryGetPath("CUSA00411", "clip.mp4"));
            cache.Remove("CUSA00411", "clip.mp4");     // doppelt: kein Wurf
        }

        [Fact]
        public void Committing_over_budget_drops_the_least_recently_used_first()
        {
            var cache = NewCache();
            Store(cache, "CUSA00411", "alt.mp4", 500);
            Store(cache, "CUSA00411", "mittel.mp4", 400);

            // Zugriffszeiten ausdrücklich setzen: NTFS führt sie standardmäßig
            // nicht nach, darum berührt der Cache seine Treffer selbst.
            File.SetLastAccessTimeUtc(cache.PathFor("CUSA00411", "alt.mp4"), new DateTime(2020, 1, 1));
            File.SetLastAccessTimeUtc(cache.PathFor("CUSA00411", "mittel.mp4"), new DateTime(2021, 1, 1));

            Store(cache, "CUSA00411", "neu.mp4", 300);   // Summe 1200 > 1000

            Assert.Null(cache.TryGetPath("CUSA00411", "alt.mp4"));
            Assert.NotNull(cache.TryGetPath("CUSA00411", "mittel.mp4"));
            Assert.NotNull(cache.TryGetPath("CUSA00411", "neu.mp4"));
        }

        [Fact]
        public void The_file_just_committed_is_never_trimmed_away()
        {
            // Selbst wenn sie allein das Budget sprengt: sie wird gerade gebraucht.
            var cache = NewCache();
            Store(cache, "CUSA00411", "riesig.mp4", 5000);

            Assert.NotNull(cache.TryGetPath("CUSA00411", "riesig.mp4"));
        }

        [Fact]
        public void A_hit_touches_the_file_so_it_survives_the_next_trim()
        {
            var cache = NewCache();
            Store(cache, "CUSA00411", "a.mp4", 500);
            Store(cache, "CUSA00411", "b.mp4", 400);
            File.SetLastAccessTimeUtc(cache.PathFor("CUSA00411", "a.mp4"), new DateTime(2020, 1, 1));
            File.SetLastAccessTimeUtc(cache.PathFor("CUSA00411", "b.mp4"), new DateTime(2021, 1, 1));

            cache.TryGetPath("CUSA00411", "a.mp4");      // berührt a → b ist jetzt die älteste
            Store(cache, "CUSA00411", "c.mp4", 300);

            Assert.NotNull(cache.TryGetPath("CUSA00411", "a.mp4"));
            Assert.Null(cache.TryGetPath("CUSA00411", "b.mp4"));
        }

        [Fact]
        public void Part_files_are_left_alone_by_the_trim()
        {
            // Ein .part gehört einem laufenden Download — wer ihn wegräumt,
            // zieht dem Transfer die Datei unter den Füßen weg.
            var cache = NewCache();
            var part = cache.BeginWrite("CUSA00411", "laufend.mp4");
            File.WriteAllBytes(part, new byte[900]);

            // 1100 > Budget, damit der Verfall wirklich läuft: der einzige
            // Kandidat wäre die gerade geschriebene Datei (geschützt), der
            // .part darf gar nicht erst in Betracht kommen.
            Store(cache, "CUSA00411", "fertig.mp4", 1100);

            Assert.True(File.Exists(part));
            Assert.NotNull(cache.TryGetPath("CUSA00411", "fertig.mp4"));
        }

        [Fact]
        public void A_stale_part_file_is_removed_while_a_fresh_one_is_left_alone()
        {
            // Verwaist ein .part (App-Ende oder Refresh mitten im Download),
            // muss ein späterer Trim ihn irgendwann wegräumen, sonst wächst
            // der Cache am Budget vorbei. Ein frischer .part gehört dagegen
            // einem laufenden Transfer und darf nicht angefasst werden.
            var cache = NewCache();

            var stalePart = cache.BeginWrite("CUSA00411", "verwaist.mp4");
            File.WriteAllBytes(stalePart, new byte[100]);
            File.SetLastWriteTimeUtc(stalePart, DateTime.UtcNow.AddHours(-2));

            var freshPart = cache.BeginWrite("CUSA00411", "laufend.mp4");
            File.WriteAllBytes(freshPart, new byte[100]);

            Store(cache, "CUSA00411", "fertig.mp4", 100);   // löst Trim über Commit aus

            Assert.False(File.Exists(stalePart));
            Assert.True(File.Exists(freshPart));
        }

        [Fact]
        public void A_read_only_file_does_not_break_remove()
        {
            // UnauthorizedAccessException stammt nicht von IOException ab —
            // eine Lesesperre ist ein echtes Risiko. Remove muss idempotent
            // bleiben: Fehler dürfen nicht wirken.
            var cache = NewCache();
            Store(cache, "CUSA00411", "clip.mp4", 100);
            var path = cache.PathFor("CUSA00411", "clip.mp4");

            try
            {
                File.SetAttributes(path, FileAttributes.ReadOnly);
                cache.Remove("CUSA00411", "clip.mp4");  // wirft nicht
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }

        [Fact]
        public void A_read_only_file_does_not_fail_a_commit()
        {
            // UnauthorizedAccessException stammt nicht von IOException ab —
            // eine Lesesperre ist ein echtes Risiko. Trim muss einen
            // Download schweigend durchlassen, auch wenn Dateien nicht zu
            // löschen sind.
            var cache = NewCache();
            Store(cache, "CUSA00411", "alt.mp4", 500);
            var oldPath = cache.PathFor("CUSA00411", "alt.mp4");

            try
            {
                File.SetLastAccessTimeUtc(oldPath, new DateTime(2020, 1, 1));
                File.SetAttributes(oldPath, FileAttributes.ReadOnly);

                // Speichern einer neuen Datei, die das Budget überschreitet:
                // Verfall würde alt.mp4 löschen wollen, kann aber nicht.
                // Commit muss das Ergebnis dennoch liefern.
                Store(cache, "CUSA00411", "neu.mp4", 600);

                Assert.NotNull(cache.TryGetPath("CUSA00411", "neu.mp4"));
            }
            finally
            {
                File.SetAttributes(oldPath, FileAttributes.Normal);
            }
        }
    }
}

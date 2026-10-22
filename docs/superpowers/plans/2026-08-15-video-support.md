# Videounterstützung — Implementierungsplan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Videoclips der PS4 erscheinen gleichberechtigt neben den Screenshots — mit Thumbnail und Play-Zeichen, herunterladbar, löschbar, in die Zwischenablage übernehmbar und im Tool abspielbar.

**Architecture:** Das Core-Modell bekommt eine Sorte (`MediaKind.Photo|Video`) statt eines zweiten Typs, weil die PS4 beide Zweige strukturgleich ablegt. `Ps4Paths` tauscht je Sorte ein Pfadsegment, `MediaRepository` liest beide Wurzeln in eine zeitsortierte Liste je Spiel. Große Dateien laufen über einen neuen streamenden FTP-Pfad mit Untätigkeits-Frist in einen Datei-Cache; abgespielt wird im Vorschaufenster über eine WPF-`MediaElement` in einem `ElementHost`.

**Tech Stack:** .NET Framework 4.8, C# 7.3, WinForms, xunit, UIFramework (ProjectReference), WPF-`MediaElement` über `WindowsFormsIntegration` — kein NuGet, kein COM-Interop.

**Spec:** `docs/superpowers/specs/2026-08-15-video-support-design.md`

## Global Constraints

- Zielplattform `net48`, `LangVersion 7.3` (aus `Directory.Build.props`) — **keine** C#-8+-Syntax (kein `switch`-Ausdruck, keine Nullable-Referenztypen, kein `using`-Deklarationsstil ohne Block).
- **Kein NuGet-Paket** wird hinzugefügt. Neue Verweise ausschließlich auf Assemblies des .NET Framework 4.8.
- Alle Kommentare, Namen in der Oberfläche und Statusmeldungen auf **Deutsch**, wie im Bestand. Kommentare begründen das *Warum*, nicht das *Was*.
- Verifizierte PS4-Pfade (2026-08-15), wörtlich zu verwenden:
  - Clip: `/user/av_contents/video/NPXS20001/<TitleID>/<hash>/<name>.mp4`
  - Clip-Thumbnail: `/user/av_contents/thumbnails/video/NPXS20001/<TitleID>/<hash>/<name>.mp4.jpg`
  - Foto: `/user/av_contents/photo/NPXS20001/<TitleID>/<hash>/<name>.jpg`
  - Foto-Thumbnail: `/user/av_contents/thumbnails/photo/NPXS20001/<TitleID>/<hash>/<name>.jpg.jpg`
- Testkommando für alle Aufgaben: `dotnet test ScreenshotGallery.sln`. Baukommando: `dotnet build -c Debug`.
- Die App hat kein Testprojekt. Aufgaben, die nur die App berühren, werden über `dotnet build` plus eine benannte Fahrprobe an der Konsole (10.10.10.236:2121) abgenommen — das ist im Plan ausdrücklich so ausgewiesen und darf **nicht** als „getestet" bezeichnet werden.
- Fehlerverhalten wie im Bestand: fehlende Nebendateien sind stiller Fallback, Verbindungsfehler landen als Klartext in der Statuszeile, die App bleibt bedienbar.

---

### Task 1: Modell — `MediaKind` und `MediaItem`

Reine Umbenennung plus ein Feld. Pfade und Repository bleiben in dieser Aufgabe Foto-only; die Sorte wird überall konstant `Photo` gesetzt. Ziel ist ein grüner Baum mit dem neuen Modell, bevor Verhalten dazukommt.

**Files:**
- Create: `src/ScreenshotGallery.Core/Ps4/MediaKind.cs`
- Rename+Modify: `src/ScreenshotGallery.Core/Ps4/Screenshot.cs` → `src/ScreenshotGallery.Core/Ps4/MediaItem.cs`
- Modify: `src/ScreenshotGallery.Core/Ps4/GameFolder.cs`
- Modify: `src/ScreenshotGallery.Core/Ps4/Ps4Paths.cs` (nur Parametertyp)
- Modify: `src/ScreenshotGallery.Core/Ps4/PhotoRepository.cs`
- Modify: `src/ScreenshotGallery.App/MainForm.cs`, `ThumbnailLoader.cs`, `PreviewForm.cs`
- Rename+Modify Test: `tests/ScreenshotGallery.Core.Tests/ScreenshotTests.cs` → `MediaItemTests.cs`
- Modify Test: `tests/ScreenshotGallery.Core.Tests/Ps4PathsTests.cs`, `PhotoRepositoryTests.cs`

**Interfaces:**
- Consumes: nichts (erste Aufgabe).
- Produces:
  - `enum MediaKind { Photo, Video }` in `ScreenshotGallery.Core.Ps4`
  - `sealed class MediaItem` mit Konstruktor `MediaItem(MediaKind kind, string titleId, string hashDir, string fileName, long size, DateTime timestamp)`, Properties `Kind`, `TitleId`, `HashDir`, `FileName`, `Size`, `Timestamp`, und `static DateTime ParseTimestamp(string fileName, DateTime fallback)`
  - `GameFolder.Items` (`IReadOnlyList<MediaItem>`) ersetzt `GameFolder.Screenshots`

- [ ] **Step 1: Tests auf das neue Modell umschreiben (sie brechen den Bau)**

`tests/ScreenshotGallery.Core.Tests/ScreenshotTests.cs` nach `MediaItemTests.cs` verschieben (`git mv`) und ersetzen durch:

```csharp
using System;
using ScreenshotGallery.Core.Ps4;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class MediaItemTests
    {
        private static readonly DateTime Fallback = new DateTime(2000, 1, 1);

        [Fact]
        public void The_capture_time_comes_from_the_file_name()
        {
            var ts = MediaItem.ParseTimestamp("20260801_084933_00470650.jpg", Fallback);
            Assert.Equal(new DateTime(2026, 8, 1, 8, 49, 33), ts);
        }

        [Fact]
        public void A_clip_name_parses_the_same_way()
        {
            // Der Video-Zweig nutzt dieselbe Namensregel (verifiziert 2026-08-15) —
            // dieser Test hält fest, dass ParseTimestamp dafür nichts Eigenes braucht.
            var ts = MediaItem.ParseTimestamp("20260810_212645_00367837.mp4", Fallback);
            Assert.Equal(new DateTime(2026, 8, 10, 21, 26, 45), ts);
        }

        [Theory]
        [InlineData("kein_datum.jpg")]
        [InlineData("2026_kaputt.jpg")]
        [InlineData("20261341_990000_x.jpg")]   // Monat 13, Stunde 99
        [InlineData("kurz.jpg")]
        public void A_malformed_name_falls_back(string name)
        {
            Assert.Equal(Fallback, MediaItem.ParseTimestamp(name, Fallback));
        }
    }
}
```

In `Ps4PathsTests.cs` das Feld ersetzen:

```csharp
        private static readonly MediaItem Shot = new MediaItem(MediaKind.Photo,
            "CUSA00411", "104", "20260801_084933_00470650.jpg", 461788, DateTime.MinValue);
```

In `PhotoRepositoryTests.cs` alle vier Vorkommen von `game.Screenshots` bzw. `games[0].Screenshots` auf `.Items` umstellen (Zeilen 80–83, 116, 135, 147).

- [ ] **Step 2: Bau zur Kontrolle scheitern lassen**

Run: `dotnet build ScreenshotGallery.sln`
Expected: FAIL mit `CS0246: Der Typ- oder Namespacename "MediaItem" wurde nicht gefunden` (und Folgefehlern zu `MediaKind` / `Items`).

- [ ] **Step 3: `MediaKind` anlegen**

`src/ScreenshotGallery.Core/Ps4/MediaKind.cs`:

```csharp
namespace ScreenshotGallery.Core.Ps4
{
    /// <summary>
    /// Foto oder Videoclip. Die PS4 legt beide strukturgleich ab (gleiche
    /// Hash-Ordner, gleiche Namensregel, gleiche .dat, gleiche Thumbnail-Regel;
    /// verifiziert 2026-08-15) — darum eine Sorte an EINEM Typ statt zweier
    /// Modelle, die dasselbe beschreiben.
    /// </summary>
    public enum MediaKind
    {
        Photo,
        Video
    }
}
```

- [ ] **Step 4: `Screenshot` zu `MediaItem` machen**

`git mv src/ScreenshotGallery.Core/Ps4/Screenshot.cs src/ScreenshotGallery.Core/Ps4/MediaItem.cs`, dann Inhalt ersetzen:

```csharp
using System;
using System.Globalization;

namespace ScreenshotGallery.Core.Ps4
{
    /// <summary>Ein Foto oder Clip auf der PS4 — reine Daten, keine FTP-Kenntnis.</summary>
    public sealed class MediaItem
    {
        public MediaItem(MediaKind kind, string titleId, string hashDir, string fileName,
            long size, DateTime timestamp)
        {
            Kind = kind;
            TitleId = titleId;
            HashDir = hashDir;
            FileName = fileName;
            Size = size;
            Timestamp = timestamp;
        }

        public MediaKind Kind { get; }
        public string TitleId { get; }
        public string HashDir { get; }
        public string FileName { get; }
        public long Size { get; }
        public DateTime Timestamp { get; }

        /// <summary>
        /// PS4-Dateinamen kodieren die Aufnahmezeit: JJJJMMTT_HHMMSS_XXXXXXXX.<ext>.
        /// Gilt für beide Zweige gleichermaßen. Bei jeder Abweichung gilt der
        /// Fallback (Spec: FTP-Änderungsdatum bzw. was der Aufrufer sonst hat) —
        /// lieber falsch sortiert als abgestürzt.
        /// </summary>
        public static DateTime ParseTimestamp(string fileName, DateTime fallback)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Length < 15) return fallback;

            DateTime parsed;
            if (DateTime.TryParseExact(fileName.Substring(0, 15), "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed;

            return fallback;
        }
    }
}
```

- [ ] **Step 5: `GameFolder` umstellen**

`src/ScreenshotGallery.Core/Ps4/GameFolder.cs`: Konstruktorparameter `IReadOnlyList<Screenshot> screenshots` → `IReadOnlyList<MediaItem> items`, Property `Screenshots` → `Items`, Doc-Kommentar auf „Ein Spiel samt seinen Fotos und Clips — eine Gruppe in der Gallery."

- [ ] **Step 6: `Ps4Paths` und `PhotoRepository` nur typmäßig nachziehen**

In `Ps4Paths.cs` die drei Parameter `Screenshot s` → `MediaItem s`. Pfade bleiben unverändert (Task 2 macht sie sortenabhängig).

In `PhotoRepository.cs`: `List<Screenshot> shots` → `List<MediaItem> items`, Konstruktion `new MediaItem(MediaKind.Photo, titleId, hash.Name, file.Name, file.Size, MediaItem.ParseTimestamp(file.Name, DateTime.MinValue))`, `new GameFolder(..., items)`, Filter `g.Items.Count > 0`, und die drei Methodensignaturen `Screenshot shot` → `MediaItem shot`.

- [ ] **Step 7: App nachziehen (rein mechanisch)**

- `ThumbnailLoader.cs`: In `Enqueue` und `LoadAsync` den Parameter `Screenshot shot` zu `MediaItem media` machen — **auch den Namen**, denn „shot" stimmt für einen Clip nicht mehr. Im Rumpf entsprechend `shot.TitleId` → `media.TitleId`, `shot.FileName` → `media.FileName`. Die andere Variable heißt weiterhin `item` und ist der `GalleryItem`; Task 6 baut genau auf diesen beiden Namen auf.
- `PreviewForm.cs`: `IReadOnlyList<Screenshot>` → `IReadOnlyList<MediaItem>`, `Func<Screenshot, Task<byte[]>>` → `Func<MediaItem, Task<byte[]>>`, Feld `_shots` bleibt vorerst.
- `MainForm.cs`: **jedes** verbliebene Vorkommen des Bezeichners `Screenshot` durch `MediaItem` ersetzen — das sind Casts (`(Screenshot)item.Tag`, `as Screenshot`), der Parametertyp von `GetFullImageBytesAsync`, und `List<Screenshot>`. Zusätzlich `game.Screenshots` → `game.Items`. Lokale Variablennamen wie `shot` dürfen in dieser Aufgabe bleiben; Task 9 benennt die um, wo es nötig wird. Zur Kontrolle danach: `grep -rn "Screenshot" src/ --include=*.cs`. Übrig bleiben dürfen **nur** deutsche Fließtexte — Kommentare und UI-Zeichenketten wie „Screenshot(s) werden auf der PS4 gelöscht" (die überarbeitet Task 9) — sowie Namensraumbezeichner `ScreenshotGallery…`. Jede Fundstelle, an der `Screenshot` als **Typ** steht, ist ein Rest und muss weg.

- [ ] **Step 8: Bau und Tests grün**

Run: `dotnet build ScreenshotGallery.sln && dotnet test ScreenshotGallery.sln`
Expected: PASS, alle bestehenden Tests plus `A_clip_name_parses_the_same_way`.

- [ ] **Step 9: Commit**

```bash
git add -A src tests
git commit -m "Rename the screenshot model to a media item with a kind"
```

---

### Task 2: `Ps4Paths` wird sortenabhängig

**Files:**
- Modify: `src/ScreenshotGallery.Core/Ps4/Ps4Paths.cs`
- Modify: `src/ScreenshotGallery.Core/Ps4/PhotoRepository.cs` (Aufrufstellen)
- Test: `tests/ScreenshotGallery.Core.Tests/Ps4PathsTests.cs`

**Interfaces:**
- Consumes: `MediaItem`, `MediaKind` aus Task 1.
- Produces:
  - `static string Ps4Paths.MediaRoot(MediaKind kind)`
  - `static string Ps4Paths.ThumbnailRoot(MediaKind kind)`
  - `static string Ps4Paths.TitleDir(MediaKind kind, string titleId)`
  - `static string Ps4Paths.MediaPath(MediaItem item)` (ersetzt `PhotoPath`)
  - `static string Ps4Paths.DatPath(MediaItem item)`, `static string Ps4Paths.ThumbnailPath(MediaItem item)`
  - `SfoPath`/`IconPath` unverändert

- [ ] **Step 1: Fehlschlagenden Test schreiben**

`Ps4PathsTests.cs` vollständig ersetzen:

```csharp
using System;
using ScreenshotGallery.Core.Ps4;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class Ps4PathsTests
    {
        private static readonly MediaItem Photo = new MediaItem(MediaKind.Photo,
            "CUSA00411", "104", "20260801_084933_00470650.jpg", 461788, DateTime.MinValue);

        // Am 2026-08-15 real auf der Konsole vorgefunden.
        private static readonly MediaItem Clip = new MediaItem(MediaKind.Video,
            "CUSA00411", "2c7", "20260810_212645_00367837.mp4", 15454876, DateTime.MinValue);

        [Fact]
        public void The_photo_paths_match_the_verified_ps4_layout()
        {
            Assert.Equal("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg",
                Ps4Paths.MediaPath(Photo));
            Assert.Equal("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.dat",
                Ps4Paths.DatPath(Photo));
            Assert.Equal("/user/av_contents/thumbnails/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg.jpg",
                Ps4Paths.ThumbnailPath(Photo));
        }

        [Fact]
        public void The_video_paths_match_the_verified_ps4_layout()
        {
            Assert.Equal("/user/av_contents/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.mp4",
                Ps4Paths.MediaPath(Clip));
            Assert.Equal("/user/av_contents/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.dat",
                Ps4Paths.DatPath(Clip));
            // Die PS4 hängt auch beim Clip schlicht ein zweites ".jpg" an den
            // VOLLEN Dateinamen an — inklusive ".mp4".
            Assert.Equal("/user/av_contents/thumbnails/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.mp4.jpg",
                Ps4Paths.ThumbnailPath(Clip));
        }

        [Fact]
        public void The_roots_and_title_dirs_follow_the_kind()
        {
            Assert.Equal("/user/av_contents/photo/NPXS20001", Ps4Paths.MediaRoot(MediaKind.Photo));
            Assert.Equal("/user/av_contents/video/NPXS20001", Ps4Paths.MediaRoot(MediaKind.Video));
            Assert.Equal("/user/av_contents/thumbnails/photo/NPXS20001", Ps4Paths.ThumbnailRoot(MediaKind.Photo));
            Assert.Equal("/user/av_contents/thumbnails/video/NPXS20001", Ps4Paths.ThumbnailRoot(MediaKind.Video));
            Assert.Equal("/user/av_contents/video/NPXS20001/CUSA00411", Ps4Paths.TitleDir(MediaKind.Video, "CUSA00411"));
        }

        [Fact]
        public void The_appmeta_paths_are_shared_by_both_kinds()
        {
            Assert.Equal("/system_data/priv/appmeta/CUSA00411/param.sfo", Ps4Paths.SfoPath("CUSA00411"));
            Assert.Equal("/user/appmeta/CUSA00411/icon0.png", Ps4Paths.IconPath("CUSA00411"));
        }
    }
}
```

- [ ] **Step 2: Test scheitern lassen**

Run: `dotnet test ScreenshotGallery.sln`
Expected: FAIL — Bau bricht mit `CS0117: "Ps4Paths" enthält keine Definition für "MediaPath"`.

- [ ] **Step 3: `Ps4Paths` umbauen**

```csharp
namespace ScreenshotGallery.Core.Ps4
{
    /// <summary>
    /// Alle PS4-Pfade an EINER Stelle — Foto-Zweig verifiziert am 2026-08-01,
    /// Video-Zweig am 2026-08-15 (siehe Specs). Beide Zweige folgen derselben
    /// Regel; es tauscht nur ein Pfadsegment, darum die Sorte als Parameter
    /// statt eines zweiten Methodensatzes.
    /// </summary>
    public static class Ps4Paths
    {
        private const string PhotoRoot = "/user/av_contents/photo/NPXS20001";
        private const string VideoRoot = "/user/av_contents/video/NPXS20001";
        private const string PhotoThumbnailRoot = "/user/av_contents/thumbnails/photo/NPXS20001";
        private const string VideoThumbnailRoot = "/user/av_contents/thumbnails/video/NPXS20001";

        public static string MediaRoot(MediaKind kind)
        {
            return kind == MediaKind.Video ? VideoRoot : PhotoRoot;
        }

        public static string ThumbnailRoot(MediaKind kind)
        {
            return kind == MediaKind.Video ? VideoThumbnailRoot : PhotoThumbnailRoot;
        }

        public static string TitleDir(MediaKind kind, string titleId)
        {
            return MediaRoot(kind) + "/" + titleId;
        }

        public static string MediaPath(MediaItem item)
        {
            return MediaRoot(item.Kind) + "/" + item.TitleId + "/" + item.HashDir + "/" + item.FileName;
        }

        public static string DatPath(MediaItem item)
        {
            var name = item.FileName;
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(0, dot);
            return MediaRoot(item.Kind) + "/" + item.TitleId + "/" + item.HashDir + "/" + name + ".dat";
        }

        /// <summary>Die PS4 hängt ans Thumbnail schlicht ein zweites ".jpg" an —
        /// im Video-Zweig genauso, also "….mp4.jpg".</summary>
        public static string ThumbnailPath(MediaItem item)
        {
            return ThumbnailRoot(item.Kind) + "/" + item.TitleId + "/" + item.HashDir + "/" + item.FileName + ".jpg";
        }

        public static string SfoPath(string titleId)
        {
            return "/system_data/priv/appmeta/" + titleId + "/param.sfo";
        }

        public static string IconPath(string titleId)
        {
            return "/user/appmeta/" + titleId + "/icon0.png";
        }
    }
}
```

- [ ] **Step 4: Aufrufstellen in `PhotoRepository` anpassen**

`Ps4Paths.PhotoRoot` → `Ps4Paths.MediaRoot(MediaKind.Photo)`, `Ps4Paths.TitleDir(titleId)` → `Ps4Paths.TitleDir(MediaKind.Photo, titleId)`, `Ps4Paths.PhotoPath(shot)` → `Ps4Paths.MediaPath(shot)`.

- [ ] **Step 5: Tests grün**

Run: `dotnet test ScreenshotGallery.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src tests
git commit -m "Make the ps4 paths follow the media kind"
```

---

### Task 3: `MediaRepository` liest beide Zweige

**Files:**
- Rename+Modify: `src/ScreenshotGallery.Core/Ps4/PhotoRepository.cs` → `src/ScreenshotGallery.Core/Ps4/MediaRepository.cs`
- Modify: `src/ScreenshotGallery.App/MainForm.cs` (Typname)
- Modify: `src/ScreenshotGallery.App/ThumbnailLoader.cs` (Typname)
- Rename+Modify Test: `tests/ScreenshotGallery.Core.Tests/PhotoRepositoryTests.cs` → `MediaRepositoryTests.cs`

**Interfaces:**
- Consumes: `Ps4Paths.MediaRoot/ThumbnailRoot/TitleDir/MediaPath/DatPath/ThumbnailPath` aus Task 2.
- Produces:
  - `sealed class MediaRepository` mit `MediaRepository(IFtpClient ftp)`
  - `Task<IReadOnlyList<GameFolder>> LoadAsync(CancellationToken ct)`
  - `Task<byte[]> DownloadThumbnailAsync(MediaItem item, CancellationToken ct)`
  - `Task<byte[]> DownloadFullAsync(MediaItem item, CancellationToken ct)` — wirft für `MediaKind.Video`
  - `Task DeleteAsync(MediaItem item, CancellationToken ct)`
  - `internal sealed class FakeFtpClient` (Testhilfe) bleibt in `MediaRepositoryTests.cs`

- [ ] **Step 1: Fehlschlagende Tests schreiben**

`git mv tests/ScreenshotGallery.Core.Tests/PhotoRepositoryTests.cs tests/ScreenshotGallery.Core.Tests/MediaRepositoryTests.cs`, dann Inhalt ersetzen. `FakeFtpClient` behält seine bisherige Gestalt und bekommt in Task 4 eine Methode dazu.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScreenshotGallery.Core.Ftp;
using ScreenshotGallery.Core.Ps4;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    /// <summary>In-Memory-FTP: Pfad → Bytes bzw. Pfad → Einträge.</summary>
    internal sealed class FakeFtpClient : IFtpClient
    {
        public readonly Dictionary<string, List<FtpEntry>> Listings =
            new Dictionary<string, List<FtpEntry>>();
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
        public readonly List<string> Deleted = new List<string>();
        public readonly HashSet<string> FailDeletes = new HashSet<string>();

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

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            if (FailDeletes.Contains(path))
                throw new InvalidOperationException("550 " + path);
            Deleted.Add(path);
            return Task.FromResult(0);
        }
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
        public async Task Deleting_a_clip_removes_the_file_dat_and_thumbnail_from_the_video_branch()
        {
            var ftp = NewPs4();
            var repo = new MediaRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);
            var clip = games.Single(g => g.TitleId == "CUSA00411").Items[0];

            await repo.DeleteAsync(clip, CancellationToken.None);

            Assert.Contains("/user/av_contents/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.mp4", ftp.Deleted);
            Assert.Contains("/user/av_contents/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.dat", ftp.Deleted);
            Assert.Contains("/user/av_contents/thumbnails/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.mp4.jpg", ftp.Deleted);
        }

        [Fact]
        public async Task Delete_removes_jpg_dat_and_thumbnail()
        {
            var ftp = NewPs4();
            var repo = new MediaRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);
            var shot = games.Single(g => g.TitleId == "CUSA00411").Items[2];

            await repo.DeleteAsync(shot, CancellationToken.None);

            Assert.Contains("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg", ftp.Deleted);
            Assert.Contains("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.dat", ftp.Deleted);
            Assert.Contains("/user/av_contents/thumbnails/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg.jpg", ftp.Deleted);
        }

        [Fact]
        public async Task A_missing_thumbnail_or_dat_does_not_fail_the_delete()
        {
            var ftp = NewPs4();
            ftp.FailDeletes.Add("/user/av_contents/thumbnails/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg.jpg");
            ftp.FailDeletes.Add("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.dat");

            var repo = new MediaRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);

            await repo.DeleteAsync(games.Single(g => g.TitleId == "CUSA00411").Items[2], CancellationToken.None);
            Assert.Contains(ftp.Deleted, p => p.EndsWith(".jpg") && !p.EndsWith(".jpg.jpg"));
        }

        [Fact]
        public async Task A_failing_media_delete_throws()
        {
            var ftp = NewPs4();
            ftp.FailDeletes.Add("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg");

            var repo = new MediaRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.DeleteAsync(games.Single(g => g.TitleId == "CUSA00411").Items[2], CancellationToken.None));
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
```

- [ ] **Step 2: Tests scheitern lassen**

Run: `dotnet test ScreenshotGallery.sln`
Expected: FAIL — `CS0246: Der Typ- oder Namespacename "MediaRepository" wurde nicht gefunden`.

- [ ] **Step 3: `MediaRepository` schreiben**

`git mv src/ScreenshotGallery.Core/Ps4/PhotoRepository.cs src/ScreenshotGallery.Core/Ps4/MediaRepository.cs`, Inhalt ersetzen:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using ScreenshotGallery.Core.Ftp;

namespace ScreenshotGallery.Core.Ps4
{
    /// <summary>
    /// Kennt die Ordnerstruktur der PS4 und sonst nichts: kein UI, kein Cache.
    /// Alle Netz-I/O läuft über IFtpClient — die Tests stecken ein Dictionary
    /// dahinter, die App den echten FtpClient.
    /// </summary>
    public sealed class MediaRepository
    {
        /// <summary>Max. gleichzeitige FTP-Zugriffe beim Laden (Spec: 3).</summary>
        private const int LoadParallelism = 3;

        private static readonly MediaKind[] Kinds = { MediaKind.Photo, MediaKind.Video };

        private readonly IFtpClient _ftp;

        public MediaRepository(IFtpClient ftp)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            _ftp = ftp;
        }

        public async Task<IReadOnlyList<GameFolder>> LoadAsync(CancellationToken ct)
        {
            var titleIds = await LoadTitleIdsAsync(ct).ConfigureAwait(false);

            var gate = new SemaphoreSlim(LoadParallelism);
            var tasks = titleIds.Select(id => LoadGameGatedAsync(id, gate, ct)).ToList();

            var games = await Task.WhenAll(tasks).ConfigureAwait(false);

            return games
                .Where(g => g.Items.Count > 0)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Vereinigt die Title-IDs aus Foto- und Video-Zweig: ein Spiel kann nur
        /// Clips haben (oder nur Screenshots) und fiele sonst aus der Ansicht.
        /// Ein fehlender Zweig ist der Normalfall und darf nicht scheitern —
        /// scheitern BEIDE, ist die Verbindung tot und der Fehler gehört laut
        /// gemeldet, sonst zeigte die App still "0 Spiele".
        /// </summary>
        private async Task<IReadOnlyList<string>> LoadTitleIdsAsync(CancellationToken ct)
        {
            var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            ExceptionDispatchInfo firstFailure = null;
            int reachable = 0;

            foreach (var kind in Kinds)
            {
                try
                {
                    var entries = await _ftp.ListAsync(Ps4Paths.MediaRoot(kind), ct).ConfigureAwait(false);
                    reachable++;
                    foreach (var entry in entries)
                        if (entry.IsDirectory) ids.Add(entry.Name);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (firstFailure == null) firstFailure = ExceptionDispatchInfo.Capture(ex);
                }
            }

            if (reachable == 0) firstFailure.Throw();
            return ids.ToList();
        }

        private async Task<GameFolder> LoadGameGatedAsync(string titleId, SemaphoreSlim gate, CancellationToken ct)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await LoadGameAsync(titleId, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<GameFolder> LoadGameAsync(string titleId, CancellationToken ct)
        {
            var items = new List<MediaItem>();
            foreach (var kind in Kinds)
                await CollectAsync(titleId, kind, items, ct).ConfigureAwait(false);

            // Neueste zuerst; bei gleicher Sekunde nach Name, damit die
            // Reihenfolge reproduzierbar ist (List.Sort ist nicht stabil).
            items.Sort((a, b) =>
            {
                int byTime = b.Timestamp.CompareTo(a.Timestamp);
                return byTime != 0
                    ? byTime
                    : string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            });

            string name = null;
            byte[] icon = null;
            try
            {
                name = SfoReader.ReadTitle(
                    await _ftp.DownloadAsync(Ps4Paths.SfoPath(titleId), ct).ConfigureAwait(false));
            }
            catch (Exception) { /* Fallback unten — Spec: stiller Fallback */ }
            try
            {
                icon = await _ftp.DownloadAsync(Ps4Paths.IconPath(titleId), ct).ConfigureAwait(false);
            }
            catch (Exception) { /* ohne Icon */ }

            return new GameFolder(titleId, string.IsNullOrEmpty(name) ? titleId : name, icon, items);
        }

        private async Task CollectAsync(string titleId, MediaKind kind, List<MediaItem> into, CancellationToken ct)
        {
            var titleDir = Ps4Paths.TitleDir(kind, titleId);

            IReadOnlyList<FtpEntry> hashDirs;
            try
            {
                hashDirs = await _ftp.ListAsync(titleDir, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return; }        // dieser Zweig kennt das Spiel nicht

            foreach (var hash in hashDirs.Where(e => e.IsDirectory))
            {
                IReadOnlyList<FtpEntry> files;
                try
                {
                    files = await _ftp.ListAsync(titleDir + "/" + hash.Name, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { continue; }

                foreach (var file in files)
                {
                    if (file.IsDirectory) continue;
                    if (!IsMedia(kind, file.Name)) continue;

                    into.Add(new MediaItem(kind, titleId, hash.Name, file.Name, file.Size,
                        MediaItem.ParseTimestamp(file.Name, DateTime.MinValue)));
                }
            }
        }

        /// <summary>
        /// Foto-Zweig: nur *.jpg. Video-Zweig: alles AUSSER der .dat-Begleitdatei —
        /// der Zweig wird allein von der Konsole gepflegt, und eine Endungsliste
        /// würde abweichende Container (.webm o. ä.) still verschlucken.
        /// </summary>
        private static bool IsMedia(MediaKind kind, string name)
        {
            if (kind == MediaKind.Photo)
                return name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
            return !name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
        }

        public Task<byte[]> DownloadThumbnailAsync(MediaItem item, CancellationToken ct)
        {
            return _ftp.DownloadAsync(Ps4Paths.ThumbnailPath(item), ct);
        }

        /// <summary>Nur für Fotos: ein Clip gehört gestreamt, nicht in ein byte[].</summary>
        public Task<byte[]> DownloadFullAsync(MediaItem item, CancellationToken ct)
        {
            if (item.Kind == MediaKind.Video)
                throw new InvalidOperationException(
                    "Ein Clip wird gestreamt (DownloadToFileAsync), nicht als byte[] geladen.");
            return _ftp.DownloadAsync(Ps4Paths.MediaPath(item), ct);
        }

        /// <summary>
        /// Löscht Mediendatei, .dat und Thumbnail. Die Mediendatei ist die
        /// Wahrheit: scheitert sie, scheitert der Löschvorgang. .dat/Thumbnail
        /// dürfen fehlen — verwaiste Reste sind auf der PS4 normal (Spec).
        /// </summary>
        public async Task DeleteAsync(MediaItem item, CancellationToken ct)
        {
            await _ftp.DeleteAsync(Ps4Paths.MediaPath(item), ct).ConfigureAwait(false);

            try { await _ftp.DeleteAsync(Ps4Paths.DatPath(item), ct).ConfigureAwait(false); }
            catch (Exception) { }
            try { await _ftp.DeleteAsync(Ps4Paths.ThumbnailPath(item), ct).ConfigureAwait(false); }
            catch (Exception) { }
        }
    }
}
```

- [ ] **Step 4: App-Typnamen nachziehen**

In `MainForm.cs` und `ThumbnailLoader.cs` jedes `PhotoRepository` durch `MediaRepository` ersetzen (Felder, Konstruktorparameter, `new PhotoRepository(...)`).

- [ ] **Step 5: Tests grün**

Run: `dotnet test ScreenshotGallery.sln`
Expected: PASS, 12 Tests in `MediaRepositoryTests`.

- [ ] **Step 6: Commit**

```bash
git add -A src tests
git commit -m "Load photos and clips into one time-sorted list per game"
```

---

### Task 4: Streamender Download mit Untätigkeits-Frist

**Files:**
- Create: `src/ScreenshotGallery.Core/Ftp/FtpTransfer.cs`
- Modify: `src/ScreenshotGallery.Core/Ftp/IFtpClient.cs`
- Modify: `src/ScreenshotGallery.Core/Ftp/FtpClient.cs`
- Modify: `src/ScreenshotGallery.Core/Ps4/MediaRepository.cs`
- Create Test: `tests/ScreenshotGallery.Core.Tests/FtpTransferTests.cs`
- Modify Test: `tests/ScreenshotGallery.Core.Tests/MediaRepositoryTests.cs` (`FakeFtpClient`)

**Interfaces:**
- Consumes: `MediaItem`, `Ps4Paths.MediaPath` aus Task 2/3.
- Produces:
  - `static class FtpTransfer` mit `const int BufferSize = 81920` und
    `Task<long> CopyAsync(Stream source, Stream destination, IProgress<long> progress, Action resetIdleTimeout, CancellationToken ct)`
  - `IFtpClient.DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct)` → `Task`
  - `MediaRepository.DownloadToFileAsync(MediaItem item, string localPath, IProgress<long> progress, CancellationToken ct)` → `Task`

- [ ] **Step 1: Fehlschlagenden Test schreiben**

`tests/ScreenshotGallery.Core.Tests/FtpTransferTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ScreenshotGallery.Core.Ftp;
using Xunit;

namespace ScreenshotGallery.Core.Tests
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
```

- [ ] **Step 2: Test scheitern lassen**

Run: `dotnet test ScreenshotGallery.sln`
Expected: FAIL — `CS0246: Der Typ- oder Namespacename "FtpTransfer" wurde nicht gefunden`.

- [ ] **Step 3: `FtpTransfer` schreiben**

`src/ScreenshotGallery.Core/Ftp/FtpTransfer.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenshotGallery.Core.Ftp
{
    /// <summary>
    /// Die Kopierschleife der Transfers — als eigener Typ, weil hier die
    /// Untätigkeits-Frist lebt und die sich ohne echten FTP-Server testen
    /// lassen muss.
    ///
    /// Der Unterschied zu Stream.CopyToAsync: nach JEDEM gelesenen Block ruft
    /// die Schleife resetIdleTimeout. Eine tote Verbindung fliegt damit weiter
    /// nach der Frist auf, ein langer gesunder Transfer läuft durch — eine
    /// Gesamtfrist würde einen mehrminütigen Clip mittendrin abschneiden.
    /// </summary>
    public static class FtpTransfer
    {
        public const int BufferSize = 81920;

        public static async Task<long> CopyAsync(Stream source, Stream destination,
            IProgress<long> progress, Action resetIdleTimeout, CancellationToken ct)
        {
            var buffer = new byte[BufferSize];
            long total = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                int read = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (read == 0) break;

                if (resetIdleTimeout != null) resetIdleTimeout();

                await destination.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                total += read;

                if (progress != null) progress.Report(total);
            }

            return total;
        }
    }
}
```

- [ ] **Step 4: Test grün**

Run: `dotnet test ScreenshotGallery.sln --filter FullyQualifiedName~FtpTransferTests`
Expected: PASS, 5 Tests.

- [ ] **Step 5: `IFtpClient` und `FtpClient` erweitern**

`IFtpClient.cs` bekommt:

```csharp
        /// <summary>
        /// Lädt streamend in eine Datei — für Clips, die als byte[] den Speicher
        /// sprengen würden. progress meldet die bisher übertragenen Bytes (die
        /// Gesamtgröße kennt der Aufrufer aus dem Listing).
        /// </summary>
        Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct);
```

`FtpClient.cs` bekommt:

```csharp
        public async Task DownloadToFileAsync(string path, string localPath,
            IProgress<long> progress, CancellationToken ct)
        {
            var request = Create(path, WebRequestMethods.Ftp.DownloadFile, TransferTimeoutMs);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                // Anders als DownloadAsync ist das hier KEINE Gesamtfrist: jeder
                // gelesene Block schiebt sie neu an (siehe FtpTransfer).
                timeout.CancelAfter(TransferTimeoutMs);
                using (timeout.Token.Register(request.Abort))
                using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var file = new FileStream(localPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, FtpTransfer.BufferSize, true))
                {
                    await FtpTransfer.CopyAsync(stream, file, progress,
                        () => timeout.CancelAfter(TransferTimeoutMs), timeout.Token).ConfigureAwait(false);
                }
            }
        }
```

- [ ] **Step 6: `MediaRepository` durchreichen**

```csharp
        /// <summary>Streamender Download — der Weg für Clips.</summary>
        public Task DownloadToFileAsync(MediaItem item, string localPath,
            IProgress<long> progress, CancellationToken ct)
        {
            return _ftp.DownloadToFileAsync(Ps4Paths.MediaPath(item), localPath, progress, ct);
        }
```

- [ ] **Step 7: `FakeFtpClient` nachziehen**

In `MediaRepositoryTests.cs` innerhalb `FakeFtpClient`:

```csharp
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
```

- [ ] **Step 8: Alles grün**

Run: `dotnet test ScreenshotGallery.sln`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add -A src tests
git commit -m "Stream large transfers with a per-block idle timeout"
```

---

### Task 5: `MediaFileCache`

**Files:**
- Create: `src/ScreenshotGallery.Core/Caching/MediaFileCache.cs`
- Create Test: `tests/ScreenshotGallery.Core.Tests/MediaFileCacheTests.cs`

**Interfaces:**
- Consumes: nichts aus vorherigen Aufgaben.
- Produces: `sealed class MediaFileCache` in `ScreenshotGallery.Core.Caching` mit
  - `MediaFileCache(string rootDirectory, long budgetBytes)`
  - `string PathFor(string titleId, string fileName)`
  - `string TryGetPath(string titleId, string fileName)` — `null` bei Fehltreffer oder 0 Byte
  - `string BeginWrite(string titleId, string fileName)` — liefert den `.part`-Pfad
  - `void Commit(string titleId, string fileName)` — `.part` → Ziel, danach trimmen
  - `void Remove(string titleId, string fileName)`

- [ ] **Step 1: Fehlschlagenden Test schreiben**

`tests/ScreenshotGallery.Core.Tests/MediaFileCacheTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using ScreenshotGallery.Core.Caching;
using Xunit;

namespace ScreenshotGallery.Core.Tests
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
        public void Begin_write_clears_a_stale_part_file()
        {
            var cache = NewCache();
            var part = cache.BeginWrite("CUSA00411", "clip.mp4");
            File.WriteAllBytes(part, new byte[100]);

            var again = cache.BeginWrite("CUSA00411", "clip.mp4");
            Assert.False(File.Exists(again));
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
    }
}
```

- [ ] **Step 2: Test scheitern lassen**

Run: `dotnet test ScreenshotGallery.sln`
Expected: FAIL — `CS0246: Der Typ- oder Namespacename "MediaFileCache" wurde nicht gefunden`.

- [ ] **Step 3: `MediaFileCache` schreiben**

`src/ScreenshotGallery.Core/Caching/MediaFileCache.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ScreenshotGallery.Core.Caching
{
    /// <summary>
    /// Datei-Cache für Clips: root\TitleID\Dateiname. Anders als der
    /// ThumbnailCache gibt er PFADE heraus statt Bytes — der Player öffnet eine
    /// Datei — und anders als dieser hat er eine Obergrenze: 19 s Clip sind
    /// 15 MB, eine Stunde Aufnahme wären knapp 3 GB. Unbegrenztes Wachstum ist
    /// hier ein reales Plattenproblem, kein theoretisches.
    ///
    /// Geladen wird immer nach <ziel>.part und erst nach vollständigem Transfer
    /// umbenannt: ohne das gälte eine abgebrochene halbe Datei als Treffer und
    /// der Clip bliebe dauerhaft unabspielbar.
    /// </summary>
    public sealed class MediaFileCache
    {
        private const string PartSuffix = ".part";

        private readonly string _root;
        private readonly long _budgetBytes;

        public MediaFileCache(string rootDirectory, long budgetBytes)
        {
            if (string.IsNullOrEmpty(rootDirectory))
                throw new ArgumentException("Cache-Wurzel fehlt.", nameof(rootDirectory));
            if (budgetBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(budgetBytes));

            _root = rootDirectory;
            _budgetBytes = budgetBytes;
        }

        public string PathFor(string titleId, string fileName)
        {
            return Path.Combine(_root, titleId, fileName);
        }

        /// <summary>
        /// Pfad der fertigen Datei oder null. Ein Treffer wird berührt, damit
        /// der Verfall ihn als "zuletzt gebraucht" sieht: NTFS führt die
        /// Zugriffszeit standardmäßig NICHT selbst nach.
        /// </summary>
        public string TryGetPath(string titleId, string fileName)
        {
            try
            {
                var path = PathFor(titleId, fileName);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0) return null;

                try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
                catch (IOException) { /* Berühren ist Kür, kein Muss */ }

                return path;
            }
            catch (IOException)
            {
                return null;
            }
        }

        public string BeginWrite(string titleId, string fileName)
        {
            var path = PathFor(titleId, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var part = path + PartSuffix;
            try { if (File.Exists(part)) File.Delete(part); }
            catch (IOException) { /* dann überschreibt der Transfer sie eben */ }

            return part;
        }

        public void Commit(string titleId, string fileName)
        {
            var path = PathFor(titleId, fileName);
            var part = path + PartSuffix;

            if (File.Exists(path)) File.Delete(path);
            File.Move(part, path);

            Trim(path);
        }

        public void Remove(string titleId, string fileName)
        {
            try
            {
                var path = PathFor(titleId, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Ein nicht löschbarer Cache-Eintrag ist keine Betriebsstörung.
            }
        }

        /// <summary>
        /// Wirft die am längsten ungenutzten Dateien weg, bis das Budget passt.
        /// Die gerade geschriebene Datei bleibt immer — sie wird ja jetzt
        /// gebraucht, selbst wenn sie allein das Budget sprengt. .part-Dateien
        /// gehören laufenden Transfers und werden nie angefasst.
        /// </summary>
        private void Trim(string keepPath)
        {
            try
            {
                if (!Directory.Exists(_root)) return;

                var files = new DirectoryInfo(_root)
                    .GetFiles("*", SearchOption.AllDirectories)
                    .Where(f => !f.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                long total = files.Sum(f => f.Length);
                if (total <= _budgetBytes) return;

                foreach (var file in files.OrderBy(f => f.LastAccessTimeUtc))
                {
                    if (total <= _budgetBytes) break;
                    if (string.Equals(file.FullName, keepPath, StringComparison.OrdinalIgnoreCase)) continue;

                    long size = file.Length;
                    try
                    {
                        file.Delete();
                        total -= size;
                    }
                    catch (IOException) { /* in Benutzung — beim nächsten Mal */ }
                }
            }
            catch (IOException)
            {
                // Aufräumen ist Nebensache; ein Fehler hier darf den Download
                // nicht nachträglich scheitern lassen.
            }
        }
    }
}
```

- [ ] **Step 4: Tests grün**

Run: `dotnet test ScreenshotGallery.sln`
Expected: PASS, 10 Tests in `MediaFileCacheTests`.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "Add the capped video file cache with part-file writes"
```

---

### Task 6: Play-Zeichen auf der Kachel

Ab hier gibt es keine Unit-Tests mehr — die App hat kein Testprojekt. Abnahme über `dotnet build` plus die benannte Fahrprobe.

**Files:**
- Modify: `src/ScreenshotGallery.App/ThumbnailLoader.cs`
- Modify: `src/ScreenshotGallery.App/Program.cs`

**Interfaces:**
- Consumes: `MediaItem`, `MediaKind` (Task 1); die in Task 1 vergebenen Namen `item` (GalleryItem) und `media` (MediaItem) in `ThumbnailLoader.LoadAsync`.
- Produces: keine neuen öffentlichen Signaturen — `ToImage` wird privat um einen `MediaKind`-Parameter erweitert.

- [ ] **Step 1: Play-Zeichen im `ThumbnailLoader`**

Oben ergänzen: `using System.Drawing.Drawing2D;`

Beide Zuweisungen an `item.Image` in `LoadAsync` um die Sorte erweitern — die aus dem Cache:

```csharp
                var cached = _cache.TryGet(media.TitleId, media.FileName);
                if (cached != null)
                {
                    item.Image = ToImage(cached, media.Kind);
                    return;
                }
```

und die nach dem Download:

```csharp
                _cache.Put(media.TitleId, media.FileName, data);
                item.Image = ToImage(data, media.Kind);
```

Dazu:

```csharp
        private static Image ToImage(byte[] data, MediaKind kind)
        {
            using (var stream = new MemoryStream(data))
            using (var direct = Image.FromStream(stream))
            {
                var bitmap = new Bitmap(direct);
                if (kind == MediaKind.Video) DrawPlayBadge(bitmap);
                return bitmap;
            }
        }

        /// <summary>
        /// Das einzige Merkmal, das eine Clip-Kachel von einer Bild-Kachel
        /// unterscheidet. Gezeichnet wird auf das ORIGINAL (die PS4 liefert
        /// 440×248), bevor die Gallery auf ImageSize verkleinert — so bleibt
        /// das Zeichen proportional, egal wie groß die Kacheln eingestellt sind.
        /// </summary>
        private static void DrawPlayBadge(Bitmap bitmap)
        {
            float side = Math.Min(bitmap.Width, bitmap.Height);
            float radius = side * 0.22f;
            float cx = bitmap.Width / 2f;
            float cy = bitmap.Height / 2f;

            using (var g = Graphics.FromImage(bitmap))
            using (var disc = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            using (var glyph = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(disc, cx - radius, cy - radius, radius * 2f, radius * 2f);

                float half = radius * 0.5f;         // halbe Höhe des Dreiecks
                var triangle = new[]
                {
                    new PointF(cx - half * 0.65f, cy - half),
                    new PointF(cx - half * 0.65f, cy + half),
                    new PointF(cx + half * 0.95f, cy)
                };
                g.FillPolygon(glyph, triangle);
            }
        }
```

- [ ] **Step 2: Verbindungsgrenze anheben**

`Program.cs`, den bestehenden Kommentar ersetzen:

```csharp
            // Spec: max. 3 parallele FTP-Zugriffe fürs Laden — plus EIN Kanal
            // Luft, damit ein laufender Clip-Transfer nicht dauerhaft einen der
            // drei belegt und die Thumbnail-Warteschlange drosselt.
            System.Net.ServicePointManager.DefaultConnectionLimit = 4;
```

- [ ] **Step 3: Bauen**

Run: `dotnet build ScreenshotGallery.sln`
Expected: PASS.

- [ ] **Step 4: Fahrprobe (keine Unit-Tests möglich)**

App starten, mit `10.10.10.236:2121` verbinden. Zu prüfen:
1. Die Gruppe „Grand Theft Auto V (CUSA00411)" enthält zusätzlich zu den Bildern eine Kachel `10.08.2026 21:26` mit `14,7 MB`.
2. Diese Kachel zeigt das Clip-Standbild **mit** Play-Zeichen in der Mitte; die Bild-Kacheln zeigen keines.
3. Die Kachel steht an ihrer Zeitposition zwischen den Screenshots, nicht am Ende.

- [ ] **Step 5: Commit**

```bash
git add -A src
git commit -m "Mark clip tiles with a play badge"
```

---

### Task 7: `SeekBar` und `VideoView`

**Files:**
- Modify: `src/ScreenshotGallery.App/ScreenshotGallery.App.csproj`
- Create: `src/ScreenshotGallery.App/SeekBar.cs`
- Create: `src/ScreenshotGallery.App/VideoView.cs`

**Interfaces:**
- Consumes: UIFramework (`SkinnedControl`, `SkinPanel`, `SkinButton`, `SkinLabel`, `SkinPainter`, `ElementKeys`, `SkinManager`).
- Produces:
  - `internal sealed class SeekBar : SkinnedControl` mit `double Value` (0..1), `bool IsDragging`, `event EventHandler SeekRequested`
  - `internal sealed class VideoView : SkinPanel` mit `void Play(string localFile)`, `void Stop()`, `void TogglePlayPause()`, `void Nudge(TimeSpan delta)`, `event EventHandler<string> Failed`

- [ ] **Step 1: WPF-Verweise ergänzen**

`ScreenshotGallery.App.csproj`, neue ItemGroup:

```xml
  <!-- Der eingebaute Player: WPF-MediaElement in einem ElementHost. Alles
       Teil des .NET Framework 4.8 — kein NuGet, kein COM-Interop. System.Xaml
       ist nötig, weil PresentationFramework darüber typisiert. -->
  <ItemGroup>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="WindowsBase" />
    <Reference Include="WindowsFormsIntegration" />
    <Reference Include="System.Xaml" />
  </ItemGroup>
```

- [ ] **Step 2: Verweise prüfen**

Run: `dotnet build ScreenshotGallery.sln`
Expected: PASS (noch ohne Nutzung der Verweise).

- [ ] **Step 3: `SeekBar` schreiben**

`src/ScreenshotGallery.App/SeekBar.cs`:

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using UIFramework.Core.Controls;
using UIFramework.Core.Rendering;
using UIFramework.Core.Skinning;

namespace ScreenshotGallery.App
{
    /// <summary>
    /// Positionsregler der Transportleiste. Das UIFramework hat keinen Slider,
    /// und SkinScrollBar ist ein Bildlaufbalken — zweckentfremdet würde er sich
    /// falsch verhalten (Seitensprünge, Pfeilflächen). Darum ein eigenes
    /// schlankes Control, das sich die Farben aus den vorhandenen
    /// ScrollBar-Einträgen des Skins holt, statt neue Skin-Schlüssel zu
    /// erfinden (die müsste sonst jeder Skin kennen).
    ///
    /// Gesprungen wird beim Loslassen, nicht während des Ziehens: ein Seek pro
    /// Mausbewegung würde die MediaElement mit Suchvorgängen überfahren.
    /// </summary>
    internal sealed class SeekBar : SkinnedControl
    {
        private double _value;
        private bool _dragging;

        public SeekBar()
        {
            Height = 18;
        }

        /// <summary>Fortschritt 0..1.</summary>
        public double Value
        {
            get { return _value; }
            set
            {
                double clamped = Clamp01(value);
                if (Math.Abs(clamped - _value) < 0.0005) return;
                _value = clamped;
                Invalidate();
            }
        }

        /// <summary>Solange wahr, darf der Tickgeber die Anzeige nicht überschreiben.</summary>
        public bool IsDragging
        {
            get { return _dragging; }
        }

        public event EventHandler SeekRequested;

        protected override string ElementKey
        {
            get { return ElementKeys.Panel; }
        }

        protected override bool ShowFocusRing
        {
            get { return false; }
        }

        protected override void PaintContent(Graphics g, ElementAppearance appearance)
        {
            int dpi = DeviceDpi;
            int grooveHeight = Scale(4, dpi);
            var groove = new Rectangle(0, (Height - grooveHeight) / 2, Width, grooveHeight);

            var grooveLook = SkinManager.Current.GetAppearance(ElementKeys.ScrollBar, State);
            var thumbLook = SkinManager.Current.GetAppearance(ElementKeys.ScrollBarThumb, State);

            SkinPainter.DrawBackground(g, groove, grooveLook, dpi);

            int filled = (int)Math.Round(groove.Width * _value);
            if (filled > 0)
                SkinPainter.DrawBackground(g,
                    new Rectangle(groove.Left, groove.Top, filled, groove.Height), thumbLook, dpi);

            int handleWidth = Scale(8, dpi);
            int handleX = groove.Left + filled - handleWidth / 2;
            handleX = Math.Max(0, Math.Min(Width - handleWidth, handleX));
            SkinPainter.DrawBackground(g,
                new Rectangle(handleX, 0, handleWidth, Height), thumbLook, dpi);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                _dragging = true;
                Value = (double)e.X / Math.Max(1, Width);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) Value = (double)e.X / Math.Max(1, Width);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _dragging)
            {
                _dragging = false;
                Value = (double)e.X / Math.Max(1, Width);

                var handler = SeekRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        private static double Clamp01(double value)
        {
            if (value < 0 || double.IsNaN(value)) return 0;
            return value > 1 ? 1 : value;
        }

        private static int Scale(int logical, int dpi)
        {
            return Math.Max(1, (int)Math.Round(logical * dpi / 96.0));
        }
    }
}
```

- [ ] **Step 4: `VideoView` schreiben**

`src/ScreenshotGallery.App/VideoView.cs`:

```csharp
using System;
using System.Drawing;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using UIFramework.Controls;
using UIFramework.Core.Skinning;

namespace ScreenshotGallery.App
{
    /// <summary>
    /// Der eingebaute Player: WPF-MediaElement in einem ElementHost, darunter
    /// eine geskinnte Transportleiste als gewöhnliches WinForms-Control. Weil
    /// sich Video- und Leistenrechteck nicht überlappen, gibt es kein
    /// Airspace-Problem — der Skin bricht nur dort, wo ohnehin Video läuft.
    ///
    /// Kennt weder FTP noch Cache: es bekommt einen fertigen lokalen Dateipfad.
    /// </summary>
    internal sealed class VideoView : SkinPanel
    {
        private readonly ElementHost _host = new ElementHost { Dock = DockStyle.Fill };
        private readonly MediaElement _media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Uniform
        };

        private readonly SkinPanel _bar = new SkinPanel { Dock = DockStyle.Bottom, Height = 40 };
        private readonly SkinButton _playPause = new SkinButton { Text = "Pause", AutoSize = true };
        private readonly SeekBar _seek = new SeekBar();
        private readonly SkinLabel _time = new SkinLabel { Text = "0:00", AutoSize = true };

        private readonly System.Windows.Forms.Timer _tick =
            new System.Windows.Forms.Timer { Interval = 250 };

        private bool _playing;

        public VideoView()
        {
            _host.Child = _media;

            // Der ElementHost malt nicht mit dem Skin — ihn wenigstens auf die
            // Panelfarbe setzen, damit die Ränder neben dem eingepassten Bild
            // nicht systemgrau aufblitzen.
            var panel = SkinManager.Current.GetAppearance(ElementKeys.Panel, ElementState.Normal);
            _host.BackColor = panel.Background;

            _bar.Controls.Add(_seek);
            _bar.Controls.Add(_playPause);
            _bar.Controls.Add(_time);

            Controls.Add(_host);
            Controls.Add(_bar);      // Dock=Bottom NACH Dock=Fill hinzufügen

            _playPause.Click += (s, e) => TogglePlayPause();
            _seek.SeekRequested += (s, e) => SeekToFraction(_seek.Value);
            _bar.Resize += (s, e) => LayoutBar();
            _tick.Tick += OnTick;

            _media.MediaOpened += (s, e) => LayoutBar();
            _media.MediaEnded += (s, e) =>
            {
                // Am Ende stehen bleiben statt still weiterzulaufen: Pause plus
                // Rücksprung auf Null macht den Zustand sichtbar richtig.
                _media.Pause();
                _media.Position = TimeSpan.Zero;
                SetPlaying(false);
            };
            _media.MediaFailed += (s, e) =>
            {
                SetPlaying(false);
                var handler = Failed;
                if (handler != null)
                    handler(this, e.ErrorException == null
                        ? "Der Clip lässt sich nicht abspielen."
                        : e.ErrorException.Message);
            };

            LayoutBar();
        }

        /// <summary>Klartext für den Fenstertitel — kein Dialog, kein Absturz.</summary>
        public event EventHandler<string> Failed;

        public void Play(string localFile)
        {
            _media.Source = new Uri(localFile);
            _media.Play();
            SetPlaying(true);
            _tick.Start();
        }

        /// <summary>
        /// Muss beim Wegblättern UND beim Schließen laufen, sonst spielt der Ton
        /// des vorigen Clips weiter.
        /// </summary>
        public void Stop()
        {
            _tick.Stop();
            _media.Stop();
            _media.Close();
            _media.Source = null;
            SetPlaying(false);
            _seek.Value = 0;
            _time.Text = "0:00";
        }

        public void TogglePlayPause()
        {
            if (_media.Source == null) return;
            if (_playing) _media.Pause(); else _media.Play();
            SetPlaying(!_playing);
        }

        public void Nudge(TimeSpan delta)
        {
            if (_media.Source == null || !_media.NaturalDuration.HasTimeSpan) return;

            var total = _media.NaturalDuration.TimeSpan;
            var target = _media.Position + delta;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > total) target = total;
            _media.Position = target;
        }

        private void SeekToFraction(double fraction)
        {
            if (_media.Source == null || !_media.NaturalDuration.HasTimeSpan) return;
            _media.Position = TimeSpan.FromSeconds(
                _media.NaturalDuration.TimeSpan.TotalSeconds * fraction);
        }

        private void SetPlaying(bool playing)
        {
            _playing = playing;
            _playPause.Text = playing ? "Pause" : "Abspielen";
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_media.NaturalDuration.HasTimeSpan)
            {
                var total = _media.NaturalDuration.TimeSpan;
                _seek.Enabled = true;
                if (!_seek.IsDragging && total.TotalSeconds > 0)
                    _seek.Value = _media.Position.TotalSeconds / total.TotalSeconds;
                _time.Text = Format(_media.Position) + " / " + Format(total);
            }
            else
            {
                // Container ohne verwertbare Dauer: abspielen geht, springen nicht.
                _seek.Enabled = false;
                _seek.Value = 0;
                _time.Text = Format(_media.Position);
            }
        }

        private void LayoutBar()
        {
            const int Margin = 8;
            _playPause.Location = new Point(Margin, (_bar.Height - _playPause.Height) / 2);

            _time.Location = new Point(
                _bar.ClientSize.Width - _time.Width - Margin,
                (_bar.Height - _time.Height) / 2);

            int left = _playPause.Right + Margin;
            int right = _time.Left - Margin;
            _seek.SetBounds(left, (_bar.Height - _seek.Height) / 2,
                Math.Max(1, right - left), _seek.Height);
        }

        private static string Format(TimeSpan value)
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            return value.Hours > 0
                ? string.Format("{0}:{1:00}:{2:00}", value.Hours, value.Minutes, value.Seconds)
                : string.Format("{0}:{1:00}", value.Minutes, value.Seconds);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tick.Stop();
                _tick.Dispose();
                _media.Close();
                _host.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 5: Bauen**

Run: `dotnet build ScreenshotGallery.sln`
Expected: PASS. Bei `CS0104` (mehrdeutiger Verweis, etwa `Stretch` oder `MediaState`) den betroffenen Typ voll qualifizieren — WPF- und WinForms-Namensräume überschneiden sich.

- [ ] **Step 6: Commit**

```bash
git add -A src
git commit -m "Add the skinned seek bar and the embedded video view"
```

---

### Task 8: Vorschaufenster mit zwei Betriebsarten

**Files:**
- Modify: `src/ScreenshotGallery.App/PreviewForm.cs`
- Modify: `src/ScreenshotGallery.App/MainForm.cs`

**Interfaces:**
- Consumes: `VideoView` (Task 7), `MediaFileCache` (Task 5), `MediaRepository.DownloadToFileAsync` (Task 4), `MediaItem`/`MediaKind`.
- Produces:
  - Konstruktor `PreviewForm(IReadOnlyList<MediaItem> items, int startIndex, string gameName, Func<MediaItem, Task<byte[]>> loadImageBytes, Func<MediaItem, IProgress<long>, Task<string>> loadVideoFile)`
  - `MainForm.GetVideoFileAsync(MediaItem item, IProgress<long> progress)` → `Task<string>` (lokaler Dateipfad) — Task 9 nutzt sie ebenfalls
  - Feld `MainForm._videoFiles` (`MediaFileCache`) und Konstante `MainForm.VideoCacheBudgetBytes`

- [ ] **Step 1: `PreviewForm` umbauen**

Kopf, Felder und Konstruktor ersetzen:

```csharp
        private readonly IReadOnlyList<MediaItem> _items;
        private readonly Func<MediaItem, Task<byte[]>> _loadImageBytes;
        private readonly Func<MediaItem, IProgress<long>, Task<string>> _loadVideoFile;
        private readonly string _gameName;
        private readonly PreviewCanvas _canvas = new PreviewCanvas { Dock = DockStyle.Fill };
        private readonly VideoView _video = new VideoView { Dock = DockStyle.Fill, Visible = false };
        private int _index;

        // Zählt Navigationen; veraltete Ladevorgänge erkennen sich daran
        // (schnelles Blättern kann Ladungen in beliebiger Reihenfolge beenden —
        // Cache-Treffer vs. Netzwerk-Miss, und ein Clip-Download dauert lange).
        private int _generation;

        public PreviewForm(IReadOnlyList<MediaItem> items, int startIndex, string gameName,
            Func<MediaItem, Task<byte[]>> loadImageBytes,
            Func<MediaItem, IProgress<long>, Task<string>> loadVideoFile)
        {
            _items = items;
            _index = startIndex;
            _gameName = gameName;
            _loadImageBytes = loadImageBytes;
            _loadVideoFile = loadVideoFile;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1280, 720);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            Controls.Add(_canvas);
            Controls.Add(_video);
            _video.Failed += (s, message) => { Text = "Fehler: " + message; };

            KeyDown += OnPreviewKeyDown;
            Shown += async (s, e) => await ShowCurrentAsync();
        }
```

- [ ] **Step 2: Tastenbehandlung ersetzen**

```csharp
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }

            // Clip-eigene Tasten zuerst. Links/Rechts bleiben bewusst das
            // Blättern — sortenabhängige Pfeiltasten wären in einer gemischten
            // Gruppe genau die Inkonsistenz, die stört.
            if (_items[_index].Kind == MediaKind.Video)
            {
                if (e.KeyCode == Keys.Space)
                {
                    _video.TogglePlayPause();
                    e.Handled = true;
                    return;
                }
                if (e.Shift && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
                {
                    _video.Nudge(TimeSpan.FromSeconds(e.KeyCode == Keys.Left ? -10 : 10));
                    e.Handled = true;
                    return;
                }
            }

            if (e.KeyCode == Keys.Left && _index > 0) { _index--; var _ = ShowCurrentAsync(); }
            if (e.KeyCode == Keys.Right && _index < _items.Count - 1) { _index++; var _ = ShowCurrentAsync(); }
        }
```

- [ ] **Step 3: `ShowCurrentAsync` in zwei Betriebsarten teilen**

```csharp
        private async Task ShowCurrentAsync()
        {
            // Eigene Generation ziehen: Falls beim Eintreffen der Antwort längst
            // weitergeblättert wurde (oder das Fenster inzwischen entsorgt ist),
            // bricht dieser Aufruf unten ab. Beim Clip ist das keine Feinheit —
            // sein Download kann Minuten dauern.
            int gen = ++_generation;
            if (IsDisposed) return;

            _video.Stop();                 // Ton des vorigen Clips beenden

            var item = _items[_index];
            var title = _gameName + " — " + item.Timestamp.ToString("dd.MM.yyyy HH:mm") +
                        "  (" + (_index + 1) + "/" + _items.Count + ")";
            Text = title;

            if (item.Kind == MediaKind.Video)
                await ShowVideoAsync(item, title, gen).ConfigureAwait(true);
            else
                await ShowImageAsync(item, gen).ConfigureAwait(true);
        }

        private async Task ShowImageAsync(MediaItem item, int gen)
        {
            _video.Visible = false;
            _canvas.Visible = true;
            _canvas.SetImage(null);

            try
            {
                var data = await _loadImageBytes(item);
                using (var stream = new MemoryStream(data))
                using (var direct = Image.FromStream(stream))
                {
                    var bitmap = new Bitmap(direct);

                    // Kein ConfigureAwait(false) oben: die Fortsetzung läuft auf
                    // dem UI-Thread, dieser Vergleich ist also race-frei.
                    if (gen != _generation || IsDisposed || _canvas.IsDisposed)
                    {
                        bitmap.Dispose();
                        return;
                    }
                    _canvas.SetImage(bitmap);
                }
            }
            catch (Exception ex)
            {
                if (gen != _generation || IsDisposed || _canvas.IsDisposed) return;
                Text = "Fehler: " + ex.Message;
            }
        }

        private async Task ShowVideoAsync(MediaItem item, string title, int gen)
        {
            _canvas.SetImage(null);
            _canvas.Visible = false;
            _video.Visible = true;

            try
            {
                var progress = new Progress<long>(bytes =>
                {
                    if (gen != _generation || IsDisposed) return;
                    Text = title + " — lädt " + Percent(bytes, item.Size) + " %";
                });

                var file = await _loadVideoFile(item, progress);

                if (gen != _generation || IsDisposed || _video.IsDisposed) return;
                Text = title;
                _video.Play(file);
            }
            catch (Exception ex)
            {
                if (gen != _generation || IsDisposed) return;
                Text = "Fehler: " + ex.Message;
            }
        }

        private static int Percent(long done, long total)
        {
            if (total <= 0) return 0;
            long percent = done * 100 / total;
            return percent > 100 ? 100 : (int)percent;
        }
```

- [ ] **Step 4: Beim Schließen aufräumen**

Am Ende der Klasse (vor `PreviewCanvas`) ergänzen:

```csharp
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Ohne das läuft der Ton weiter, bis der Finalizer aufräumt.
            _generation++;
            _video.Stop();
            base.OnFormClosed(e);
        }
```

- [ ] **Step 5: `MainForm` anpassen — Video-Cache, Nachladen, Aufrufstelle**

Bei den übrigen Cache-Feldern ergänzen:

```csharp
        /// <summary>Obergrenze des Clip-Caches (Spec: 4 GB).</summary>
        private const long VideoCacheBudgetBytes = 4L * 1024 * 1024 * 1024;

        private readonly MediaFileCache _videoFiles = new MediaFileCache(
            Path.Combine(DataRoot, "cache", "video"), VideoCacheBudgetBytes);
```

In `OpenPreview` die Konstruktion ersetzen:

```csharp
                new PreviewForm(items, start, game.Name, GetFullImageBytesAsync, GetVideoFileAsync).Show(this);
```

Und die von der Vorschau (und ab Task 9 auch von der Zwischenablage) genutzte Nachlade-Methode anlegen:

```csharp
        /// <summary>Clip-Datei aus dem Cache oder streamend nachgeladen.</summary>
        private async Task<string> GetVideoFileAsync(MediaItem item, IProgress<long> progress)
        {
            var cached = _videoFiles.TryGetPath(item.TitleId, item.FileName);
            if (cached != null) return cached;

            if (_repository == null) throw new InvalidOperationException("Nicht verbunden.");

            var part = _videoFiles.BeginWrite(item.TitleId, item.FileName);
            await _repository.DownloadToFileAsync(item, part, progress, _loadCts.Token);
            _videoFiles.Commit(item.TitleId, item.FileName);

            return _videoFiles.PathFor(item.TitleId, item.FileName);
        }
```

Außerdem in `OpenPreview` die lokale Liste von `shots` auf `items` umbenennen und `as Screenshot` → `as MediaItem` (falls in Task 1 übersehen).

- [ ] **Step 6: Bauen**

Run: `dotnet build ScreenshotGallery.sln`
Expected: PASS.

- [ ] **Step 7: Fahrprobe (keine Unit-Tests möglich)**

App starten, verbinden, Clip-Kachel doppelklicken. Zu prüfen:
1. Der Fenstertitel zählt beim Laden hoch („… — lädt 37 %"), danach spielt der Clip mit Ton.
2. Play/Pause per Knopf und per Leertaste; die Beschriftung wechselt zwischen „Pause" und „Abspielen".
3. Ziehen am Regler springt beim Loslassen an die Stelle; die Zeitanzeige zeigt „0:05 / 0:19".
4. Umschalt+Rechts springt 10 s vor, Umschalt+Links zurück.
5. Rechts/Links blättert zum nächsten Bild — **der Ton muss sofort aufhören**, und die Bildansicht erscheint statt des Players.
6. Zurückblättern auf den Clip spielt ihn erneut ab, diesmal ohne Ladeanzeige (Cache-Treffer).
7. Esc schließt; danach ist kein Ton mehr zu hören.

- [ ] **Step 8: Commit**

```bash
git add -A src
git commit -m "Give the preview window an image mode and a player mode"
```

---

### Task 9: Kontextmenü — Herunterladen, Zwischenablage, Löschen

**Files:**
- Modify: `src/ScreenshotGallery.App/MainForm.cs`

**Interfaces:**
- Consumes: `MainForm.GetVideoFileAsync` und Feld `_videoFiles` (beide aus Task 8), `MediaRepository.DownloadToFileAsync` (Task 4), `MediaKind`.
- Produces: `MainForm.Percent(long done, long total)` → `int` (privat, statisch).

- [ ] **Step 1: `DownloadSelectedAsync` sortenfähig machen**

```csharp
        private async Task DownloadSelectedAsync(List<GalleryItem> selected)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var failures = new List<string>();
                int done = 0;
                foreach (var galleryItem in selected)
                {
                    var media = (MediaItem)galleryItem.Tag;
                    var target = Path.Combine(dialog.SelectedPath, media.FileName);
                    try
                    {
                        if (media.Kind == MediaKind.Video)
                        {
                            int number = done + 1;
                            var progress = new Progress<long>(bytes =>
                                _status.Text = "Clip " + number + " von " + selected.Count +
                                               " — " + Percent(bytes, media.Size) + " %");

                            // Erst in eine .part-Datei: ein abgebrochener Transfer
                            // soll im Zielordner des Anwenders keine halbe Datei
                            // hinterlassen, die wie ein fertiger Clip aussieht.
                            var part = target + ".part";
                            await _repository.DownloadToFileAsync(media, part, progress, _loadCts.Token);
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(part, target);
                        }
                        else
                        {
                            File.WriteAllBytes(target, await GetFullImageBytesAsync(media));
                        }

                        done++;
                        _status.Text = "Heruntergeladen: " + done + " von " + selected.Count + " …";
                    }
                    catch (Exception ex)
                    {
                        failures.Add(media.FileName + " — " + Root(ex).Message);
                    }
                }

                _status.Text = done + " Datei(en) heruntergeladen.";
                if (failures.Count > 0)
                    MessageBox.Show(this, "Fehlgeschlagen:\n" + string.Join("\n", failures),
                        "Herunterladen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static int Percent(long done, long total)
        {
            if (total <= 0) return 0;
            long percent = done * 100 / total;
            return percent > 100 ? 100 : (int)percent;
        }
```

- [ ] **Step 2: `CopyToClipboardAsync` sortenfähig machen**

```csharp
        private async Task CopyToClipboardAsync(List<GalleryItem> selected)
        {
            if (selected.Count != 1) return;
            var media = (MediaItem)selected[0].Tag;
            try
            {
                if (media.Kind == MediaKind.Video)
                {
                    // Ein Clip kann kein Bild in der Zwischenablage sein. Als
                    // Dateiliste ist er dafür in Explorer, Chat oder Mail
                    // einfügbar — nützlicher, als den Eintrag zu sperren.
                    var progress = new Progress<long>(bytes =>
                        _status.Text = "Lade Clip … " + Percent(bytes, media.Size) + " %");

                    var file = await GetVideoFileAsync(media, progress);
                    var paths = new System.Collections.Specialized.StringCollection();
                    paths.Add(file);
                    Clipboard.SetFileDropList(paths);
                    _status.Text = "Clip in Zwischenablage: " + media.FileName;
                    return;
                }

                var data = await GetFullImageBytesAsync(media);
                using (var stream = new MemoryStream(data))
                using (var image = Image.FromStream(stream))
                {
                    Clipboard.SetImage(image);      // kopiert als DIB — Dispose danach ist ok
                }
                _status.Text = "In Zwischenablage kopiert: " + media.FileName;
            }
            catch (Exception ex)
            {
                _status.Text = "Zwischenablage fehlgeschlagen: " + Root(ex).Message;
            }
        }
```

- [ ] **Step 3: Löschen räumt auch den Clip-Cache**

In `DeleteSelectedAsync` die Schleifenvariable `shot` in `media` umbenennen (sie ist seit Task 1 ein `MediaItem`) und nach den beiden bestehenden `Remove`-Aufrufen ergänzen:

```csharp
                    _videoFiles.Remove(media.TitleId, media.FileName);
```

Außerdem den Bestätigungstext und die Abschlussmeldung von „Screenshot(s)" auf „Datei(en)" umstellen, damit sie bei Clips nicht lügen.

- [ ] **Step 4: Menütext für gemischte Auswahl prüfen**

In `ShowItemMenu` bleibt alles wie es ist — die vier Einträge gelten für beide Sorten. Nur die Beschriftung „Vorschau öffnen" bleibt korrekt, weil das Fenster beide Betriebsarten kennt.

- [ ] **Step 5: Bauen**

Run: `dotnet build ScreenshotGallery.sln && dotnet test ScreenshotGallery.sln`
Expected: PASS.

- [ ] **Step 6: Fahrprobe (keine Unit-Tests möglich)**

1. Einen Clip **und** zwei Bilder anhaken → *Herunterladen…* → alle drei liegen im Zielordner, der Clip vollständig (14,7 MB) und ohne `.part`-Rest; die Statuszeile zählte beim Clip Prozente hoch.
2. Nur den Clip anhaken → *In Zwischenablage kopieren* → im Explorer einfügen erzeugt eine Kopie der `.mp4`.
3. Ein Bild anhaken → *In Zwischenablage kopieren* → in Paint einfügen zeigt das Bild (unverändertes Verhalten).
4. `%LocalAppData%\ScreenshotGallery\cache\video\CUSA00411\` enthält den Clip.
5. *Löschen…* an einem **Testclip** (nicht am einzigen echten, falls er noch gebraucht wird): Kachel verschwindet, Datei auf der PS4 weg, Cache-Datei weg.

- [ ] **Step 7: Commit**

```bash
git add -A src
git commit -m "Extend the context menu actions to clips"
```

---

### Task 10: Dokumentation und Abschluss-Fahrprobe

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: alles Vorherige.
- Produces: nichts im Code.

- [ ] **Step 1: README ergänzen**

Im Abschnitt „Funktionsumfang" die Gruppierungszeile erweitern und einen Punkt ergänzen:

```markdown
- **Screenshots und Videos gemeinsam**: Beide Zweige der Konsole
  (`/user/av_contents/photo` und `/user/av_contents/video`) landen in
  derselben Spielgruppe, gemeinsam nach Aufnahmezeit sortiert. Clips sind
  am Play-Zeichen auf der Kachel zu erkennen; ihr Vorschaubild liefert die
  PS4 selbst mit.
- **Eingebauter Player**: Ein Doppelklick auf einen Clip lädt ihn und spielt
  ihn im Vorschaufenster ab — mit Play/Pause (Leertaste), Positionsregler,
  Zeitanzeige und ±10 s über Umschalt+Pfeil. Links/Rechts blättert wie bei
  den Bildern durch die Gruppe.
```

Im Abschnitt „Datenpfade" ergänzen:

```markdown
- `cache\video\` — Cache für heruntergeladene Clips. Anders als die übrigen
  Caches ist dieser auf 4 GB begrenzt: Er wirft nach jedem Download die am
  längsten ungenutzten Clips weg, bis die Grenze wieder eingehalten ist.
```

Im Abschnitt „Voraussetzungen" ergänzen:

```markdown
- Für den eingebauten Player die Medienkomponenten von Windows (`wmp.dll`),
  auf einer normalen Windows-10/11-Installation vorhanden. Auf N-Editionen
  ohne Media Feature Pack bleibt das Vorschaufenster bei Clips schwarz und
  meldet den Fehler im Fenstertitel; Herunterladen und Löschen funktionieren
  davon unabhängig.
```

- [ ] **Step 2: Gesamtlauf**

Run: `dotnet build -c Release ScreenshotGallery.sln && dotnet test ScreenshotGallery.sln`
Expected: PASS, keine Fehler.

- [ ] **Step 3: Abschluss-Fahrprobe an der Konsole**

Vollständiger Durchlauf mit `10.10.10.236:2121`:
1. Verbinden → Statuszeile nennt Spiele- und Dateizahl; die Zahl schließt den Clip ein.
2. Bilder- und Clip-Kacheln stehen zeitlich gemischt, das Play-Zeichen sitzt nur auf Clips.
3. *Aktualisieren* während ein Clip lädt: kein Absturz, keine veraltete Fehlermeldung.
4. Vorschau: Bild → Clip → Bild durchblättern, Ton hört jeweils sofort auf.
5. Herunterladen, Zwischenablage und Löschen je einmal für beide Sorten.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Document the video support"
```

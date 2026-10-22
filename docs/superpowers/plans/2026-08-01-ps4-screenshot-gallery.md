# PS4 Screenshot Gallery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WinForms-Tool, das PS4-Screenshots per FTP (GoldHEN) als Gallery zeigt — gruppiert nach Spiel mit Name/Icon, mit Herunterladen, Zwischenablage, Löschen und Vollbild-Vorschau.

**Architecture:** `ScreenshotGallery.Core` (FTP, Parser, Pfadmodell, Cache — keine UI, alles gegen `IFtpClient` testbar) + `ScreenshotGallery.App` (MainForm auf `SkinnedForm`, konsumiert das `GalleryControl` aus dem UIFramework) + xunit-Tests für Core.

**Tech Stack:** .NET Framework 4.8, LangVersion 7.3, WinForms, `FtpWebRequest` (kein NuGet), xunit 2.9.2, ProjectReferences auf `E:\Projects\UIFramework`.

**Spec:** `docs/superpowers/specs/2026-08-01-ps4-screenshot-gallery-design.md`
**Voraussetzung:** Der GalleryControl-Plan (`E:\Projects\UIFramework\docs\superpowers\plans\2026-08-01-gallerycontrol.md`) ist umgesetzt; `UIFramework.Gallery` baut.

## Global Constraints

- `TargetFramework net48`, `LangVersion 7.3`, `UseWindowsForms true` — über `Directory.Build.props` (Task 1), nichts pro Projekt.
- Keine NuGet-Pakete außer den Test-Paketen (xunit 2.9.2, xunit.runner.visualstudio 2.8.2, Microsoft.NET.Test.Sdk 17.11.1).
- PS4-Konstanten exakt aus der Spec: Foto-Wurzel `/user/av_contents/photo/NPXS20001`, Thumbnail-Wurzel `/user/av_contents/thumbnails/photo/NPXS20001` (Datei = `<name>.jpg` + angehängtes `.jpg`), `param.sfo` unter `/system_data/priv/appmeta/<TitleID>/param.sfo`, Icon `/user/appmeta/<TitleID>/icon0.png`. Defaults `10.10.10.236:2121`, anonym.
- Timeouts: Verbinden/Listen 8 s, Transfers 30 s. Parallelität: max. 3 gleichzeitige FTP-Zugriffe.
- Lokale Daten unter `%LocalAppData%\ScreenshotGallery\` (`settings.json`, `cache\<TitleID>\…`, `cache\full\<TitleID>\…`).
- Quelltexte/Kommentare Deutsch, Testnamen englisch mit Unterstrichen, Commits Englisch imperativ (Konventionen wie im UIFramework).
- Testlauf: `dotnet test ScreenshotGallery.sln --nologo -v q`.

---

### Task 1: Solution-Gerüst

**Files:**
- Create: `Directory.Build.props`, `ScreenshotGallery.sln`
- Create: `src/ScreenshotGallery.Core/ScreenshotGallery.Core.csproj`
- Create: `src/ScreenshotGallery.App/ScreenshotGallery.App.csproj`, `src/ScreenshotGallery.App/Program.cs`
- Create: `tests/ScreenshotGallery.Core.Tests/ScreenshotGallery.Core.Tests.csproj`, `tests/ScreenshotGallery.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: baubare Solution; Namespaces `ScreenshotGallery.Core.*`, App-Einstieg `Program.Main`.

- [ ] **Step 1: Dateien anlegen**

`Directory.Build.props` (Muster UIFramework):

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>7.3</LangVersion>
    <UseWindowsForms>true</UseWindowsForms>
    <Deterministic>true</Deterministic>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <Company>ScreenshotGallery</Company>
  </PropertyGroup>
</Project>
```

`src/ScreenshotGallery.Core/ScreenshotGallery.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

`src/ScreenshotGallery.App/ScreenshotGallery.App.csproj` (relative Pfade: vom Projektordner `src/ScreenshotGallery.App` sind es vier `..`-Ebenen bis `E:\Projects`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <AssemblyName>PS4ScreenshotGallery</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\ScreenshotGallery.Core\ScreenshotGallery.Core.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Core\UIFramework.Core.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Controls\UIFramework.Controls.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Gallery\UIFramework.Gallery.csproj" />
  </ItemGroup>
</Project>
```

**Achtung Pfadtiefe:** Die Solution liegt unter `E:\Projects\PS4\ScreenshotGallery`, das App-Projekt unter `…\src\ScreenshotGallery.App`. Bis `E:\Projects` sind es also VIER `..`-Schritte (`ScreenshotGallery.App` → `src` → `ScreenshotGallery` → `PS4` → `Projects`). Nach dem Anlegen sofort `dotnet build` — ein falscher Referenzpfad fällt hier, nicht später.

`src/ScreenshotGallery.App/Program.cs`:

```csharp
using System;
using System.Windows.Forms;
using UIFramework.Core.Skinning;
using UIFramework.Core.Skinning.Skins;

namespace ScreenshotGallery.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SkinManager.Current = new DarkSkin();      // Spec: Dark als Standard
            Application.Run(new MainForm());
        }
    }
}
```

Damit Task 1 baut, `src/ScreenshotGallery.App/MainForm.cs` als minimalen Rumpf anlegen (wird in Task 9 ausgebaut):

```csharp
using System.Drawing;
using UIFramework.Core.Controls;

namespace ScreenshotGallery.App
{
    internal sealed class MainForm : SkinnedForm
    {
        public MainForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            Text = "PS4 Screenshot Gallery";
            ClientSize = new Size(1100, 700);
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        }
    }
}
```

`tests/ScreenshotGallery.Core.Tests/ScreenshotGallery.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ScreenshotGallery.Core\ScreenshotGallery.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/ScreenshotGallery.Core.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void The_test_host_runs()
        {
            Assert.True(true);
        }
    }
}
```

- [ ] **Step 2: Solution bauen**

```bash
cd /e/Projects/PS4/ScreenshotGallery
dotnet new sln -n ScreenshotGallery
dotnet sln add src/ScreenshotGallery.Core src/ScreenshotGallery.App tests/ScreenshotGallery.Core.Tests
dotnet test ScreenshotGallery.sln --nologo -v q
```

Expected: Build PASS, 1 Test grün.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Scaffold the solution with core, app and test projects"
```

---

### Task 2: UnixListingParser

**Files:**
- Create: `src/ScreenshotGallery.Core/Ftp/FtpEntry.cs`
- Create: `src/ScreenshotGallery.Core/Ftp/UnixListingParser.cs`
- Test: `tests/ScreenshotGallery.Core.Tests/UnixListingParserTests.cs`

**Interfaces:**
- Produces: `FtpEntry { string Name; bool IsDirectory; long Size; }`; `static IReadOnlyList<FtpEntry> UnixListingParser.Parse(string listing)`.

- [ ] **Step 1: Failing Tests** (die Zeilen sind ECHTE GoldHEN-Ausgaben vom 2026-08-01):

```csharp
using System.Linq;
using ScreenshotGallery.Core.Ftp;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class UnixListingParserTests
    {
        private const string RealGoldHenListing =
            "drwxr-xr-x 2 0 0 512 Aug  1 2026 .\r\n" +
            "drwxr-xr-x 5 0 0 512 Aug  1 2026 ..\r\n" +
            "-rw-r--r-- 1 0 0 461788 Aug  1 2026 20260801_084933_00470650.jpg\r\n" +
            "-rwxrwx--- 1 0 0 592 Aug  1 2026 20260801_084933_00470650.dat\r\n" +
            "drwxr-xr-x 3 0 0 512 Jul 24 2026 CUSA00411\r\n";

        [Fact]
        public void Dot_and_dotdot_are_skipped()
        {
            var entries = UnixListingParser.Parse(RealGoldHenListing);
            Assert.DoesNotContain(entries, e => e.Name == "." || e.Name == "..");
            Assert.Equal(3, entries.Count);
        }

        [Fact]
        public void Files_and_directories_are_told_apart()
        {
            var entries = UnixListingParser.Parse(RealGoldHenListing);

            var jpg = entries.Single(e => e.Name == "20260801_084933_00470650.jpg");
            Assert.False(jpg.IsDirectory);
            Assert.Equal(461788, jpg.Size);

            var dir = entries.Single(e => e.Name == "CUSA00411");
            Assert.True(dir.IsDirectory);
        }

        [Fact]
        public void Special_permission_bits_do_not_confuse_the_parser()
        {
            var entries = UnixListingParser.Parse(RealGoldHenListing);
            var dat = entries.Single(e => e.Name == "20260801_084933_00470650.dat");
            Assert.False(dat.IsDirectory);
            Assert.Equal(592, dat.Size);
        }

        [Fact]
        public void A_name_with_spaces_survives()
        {
            var entries = UnixListingParser.Parse(
                "-rw-r--r-- 1 0 0 10 Aug  1 2026 mit zwei leerzeichen.jpg\n");
            Assert.Equal("mit zwei leerzeichen.jpg", entries.Single().Name);
        }

        [Fact]
        public void Garbage_lines_and_blank_lines_are_ignored()
        {
            var entries = UnixListingParser.Parse("total 12\r\n\r\nkaputt\r\n");
            Assert.Empty(entries);
        }

        [Fact]
        public void Null_or_empty_input_yields_an_empty_list()
        {
            Assert.Empty(UnixListingParser.Parse(null));
            Assert.Empty(UnixListingParser.Parse(""));
        }
    }
}
```

- [ ] **Step 2: Testlauf — muss scheitern**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~UnixListingParserTests"`
Expected: FAIL (kompiliert nicht).

- [ ] **Step 3: Implementieren**

`src/ScreenshotGallery.Core/Ftp/FtpEntry.cs`:

```csharp
namespace ScreenshotGallery.Core.Ftp
{
    /// <summary>Ein Eintrag aus einem LIST-Ergebnis. Nur, was die App braucht.</summary>
    public sealed class FtpEntry
    {
        public FtpEntry(string name, bool isDirectory, long size)
        {
            Name = name;
            IsDirectory = isDirectory;
            Size = size;
        }

        public string Name { get; }
        public bool IsDirectory { get; }
        public long Size { get; }
    }
}
```

`src/ScreenshotGallery.Core/Ftp/UnixListingParser.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ScreenshotGallery.Core.Ftp
{
    /// <summary>
    /// Parst GoldHENs Unix-artiges LIST-Format:
    ///     drwxr-xr-x 2 0 0 512 Aug  1 2026 CUSA00411
    ///     -rwxrwx--- 1 0 0 592 Aug  1 2026 name mit leerzeichen.dat
    /// Feldaufbau: Rechte, Links, UID, GID, Größe, Monat, Tag, Jahr/Zeit, Name.
    /// Der Name ist ab Feld 9 ALLES (Leerzeichen inklusive) — deshalb Split
    /// mit Obergrenze 9 statt naivem Zerhacken.
    /// </summary>
    public static class UnixListingParser
    {
        public static IReadOnlyList<FtpEntry> Parse(string listing)
        {
            var entries = new List<FtpEntry>();
            if (string.IsNullOrEmpty(listing)) return entries;

            foreach (var raw in listing.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = raw.Split(new[] { ' ' }, 9, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 9) continue;                       // "total 12", Müll
                if (fields[0].Length < 10) continue;                   // kein Rechte-Feld

                char kind = fields[0][0];
                if (kind != 'd' && kind != '-') continue;              // Links etc. braucht niemand

                long size;
                if (!long.TryParse(fields[4], out size)) continue;

                string name = fields[8];
                if (name == "." || name == "..") continue;

                entries.Add(new FtpEntry(name, kind == 'd', size));
            }

            return entries;
        }
    }
}
```

- [ ] **Step 4: Testlauf — grün**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~UnixListingParserTests"`
Expected: PASS (6 Tests).

- [ ] **Step 5: Commit**

```bash
git add src/ScreenshotGallery.Core/Ftp tests
git commit -m "Add the GoldHEN unix listing parser"
```

---

### Task 3: Screenshot-Modell mit Zeitstempel aus dem Dateinamen

**Files:**
- Create: `src/ScreenshotGallery.Core/Ps4/Screenshot.cs`
- Test: `tests/ScreenshotGallery.Core.Tests/ScreenshotTests.cs`

**Interfaces:**
- Produces: `Screenshot { string TitleId; string HashDir; string FileName; long Size; DateTime Timestamp; }` mit `static DateTime ParseTimestamp(string fileName, DateTime fallback)`.

- [ ] **Step 1: Failing Tests**

```csharp
using System;
using ScreenshotGallery.Core.Ps4;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class ScreenshotTests
    {
        private static readonly DateTime Fallback = new DateTime(2000, 1, 1);

        [Fact]
        public void The_capture_time_comes_from_the_file_name()
        {
            var ts = Screenshot.ParseTimestamp("20260801_084933_00470650.jpg", Fallback);
            Assert.Equal(new DateTime(2026, 8, 1, 8, 49, 33), ts);
        }

        [Theory]
        [InlineData("kein_datum.jpg")]
        [InlineData("2026_kaputt.jpg")]
        [InlineData("20261341_990000_x.jpg")]   // Monat 13, Stunde 99
        [InlineData("kurz.jpg")]
        public void A_malformed_name_falls_back(string name)
        {
            Assert.Equal(Fallback, Screenshot.ParseTimestamp(name, Fallback));
        }
    }
}
```

- [ ] **Step 2: Testlauf — muss scheitern**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~ScreenshotTests"`
Expected: FAIL.

- [ ] **Step 3: Implementieren** — `src/ScreenshotGallery.Core/Ps4/Screenshot.cs`:

```csharp
using System;
using System.Globalization;

namespace ScreenshotGallery.Core.Ps4
{
    /// <summary>Ein Screenshot auf der PS4 — reine Daten, keine FTP-Kenntnis.</summary>
    public sealed class Screenshot
    {
        public Screenshot(string titleId, string hashDir, string fileName, long size, DateTime timestamp)
        {
            TitleId = titleId;
            HashDir = hashDir;
            FileName = fileName;
            Size = size;
            Timestamp = timestamp;
        }

        public string TitleId { get; }
        public string HashDir { get; }
        public string FileName { get; }
        public long Size { get; }
        public DateTime Timestamp { get; }

        /// <summary>
        /// PS4-Dateinamen kodieren die Aufnahmezeit: JJJJMMTT_HHMMSS_XXXXXXXX.jpg.
        /// Bei jeder Abweichung gilt der Fallback (Spec: FTP-Änderungsdatum bzw.
        /// was der Aufrufer sonst hat) — lieber falsch sortiert als abgestürzt.
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

- [ ] **Step 4: Testlauf — grün**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~ScreenshotTests"`
Expected: PASS (5 Tests).

- [ ] **Step 5: Commit**

```bash
git add src/ScreenshotGallery.Core/Ps4 tests
git commit -m "Add the screenshot model with filename timestamp parsing"
```

---

### Task 4: SfoReader (param.sfo → Spielname)

**Files:**
- Create: `src/ScreenshotGallery.Core/Ps4/SfoReader.cs`
- Create: `tests/ScreenshotGallery.Core.Tests/TestData/param.sfo` (echte Datei von der PS4)
- Test: `tests/ScreenshotGallery.Core.Tests/SfoReaderTests.cs`
- Modify: `tests/ScreenshotGallery.Core.Tests/ScreenshotGallery.Core.Tests.csproj` (TestData kopieren)

**Interfaces:**
- Produces: `static string SfoReader.ReadTitle(byte[] data)` — null bei allem Unlesbaren.

- [ ] **Step 1: Echte param.sfo als Testdatum holen** (PS4 muss an sein; einmalig, die Datei wird eingecheckt):

```bash
mkdir -p tests/ScreenshotGallery.Core.Tests/TestData
curl -s --connect-timeout 8 "ftp://10.10.10.236:2121/system_data/priv/appmeta/CUSA00411/param.sfo" \
  --user anonymous:anonymous -o tests/ScreenshotGallery.Core.Tests/TestData/param.sfo
ls -la tests/ScreenshotGallery.Core.Tests/TestData/param.sfo    # ~3084 Bytes erwartet
```

Im Test-csproj ergänzen:

```xml
  <ItemGroup>
    <None Include="TestData\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Failing Tests**

```csharp
using System.IO;
using ScreenshotGallery.Core.Ps4;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class SfoReaderTests
    {
        private static byte[] RealSfo()
        {
            return File.ReadAllBytes(Path.Combine("TestData", "param.sfo"));
        }

        [Fact]
        public void The_real_gta_v_sfo_yields_its_title()
        {
            Assert.Equal("Grand Theft Auto V", SfoReader.ReadTitle(RealSfo()));
        }

        [Fact]
        public void Garbage_bytes_yield_null_instead_of_throwing()
        {
            Assert.Null(SfoReader.ReadTitle(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.Null(SfoReader.ReadTitle(new byte[0]));
            Assert.Null(SfoReader.ReadTitle(null));
        }

        [Fact]
        public void A_truncated_real_file_yields_null()
        {
            var real = RealSfo();
            var cut = new byte[64];
            System.Array.Copy(real, cut, 64);
            Assert.Null(SfoReader.ReadTitle(cut));
        }
    }
}
```

**Hinweis:** Sollte der TITLE der echten Datei nicht exakt `"Grand Theft Auto V"` sein, gilt die Datei als Wahrheit: den Erwartungswert im Test an den echten Inhalt anpassen (einmal per Hexdump prüfen: `xxd tests/ScreenshotGallery.Core.Tests/TestData/param.sfo | grep -A2 TITLE`), NICHT den Reader verbiegen.

- [ ] **Step 3: Testlauf — muss scheitern**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~SfoReaderTests"`
Expected: FAIL.

- [ ] **Step 4: Implementieren** — `src/ScreenshotGallery.Core/Ps4/SfoReader.cs`:

```csharp
using System;
using System.Text;

namespace ScreenshotGallery.Core.Ps4
{
    /// <summary>
    /// Liest den TITLE-Eintrag aus Sonys param.sfo.
    ///
    /// Format (little-endian): Header 0x00 Magic "\0PSF", 0x08 Offset der
    /// Schlüsseltabelle, 0x0C Offset der Datentabelle, 0x10 Eintragszahl.
    /// Ab 0x14 je Eintrag 16 Bytes: u16 Schlüssel-Offset, u16 Format,
    /// u32 Datenlänge, u32 Maximallänge, u32 Daten-Offset. Schlüssel sind
    /// nullterminiertes ASCII, TITLE ist ein UTF-8-String.
    ///
    /// Jede Abweichung liefert null — ein fehlender Name ist ein Fallback
    /// auf die Title-ID, nie ein Absturz (Spec, Fehlerbehandlung).
    /// </summary>
    public static class SfoReader
    {
        private const uint Magic = 0x46535000;      // "\0PSF" als u32 little-endian

        public static string ReadTitle(byte[] data)
        {
            return ReadString(data, "TITLE");
        }

        public static string ReadString(byte[] data, string key)
        {
            try
            {
                if (data == null || data.Length < 0x14) return null;
                if (BitConverter.ToUInt32(data, 0x00) != Magic) return null;

                uint keyTable = BitConverter.ToUInt32(data, 0x08);
                uint dataTable = BitConverter.ToUInt32(data, 0x0C);
                uint count = BitConverter.ToUInt32(data, 0x10);

                for (uint i = 0; i < count; i++)
                {
                    long entry = 0x14 + i * 16;
                    if (entry + 16 > data.Length) return null;

                    ushort keyOffset = BitConverter.ToUInt16(data, (int)entry);
                    uint length = BitConverter.ToUInt32(data, (int)entry + 4);
                    uint dataOffset = BitConverter.ToUInt32(data, (int)entry + 12);

                    string name = ReadKey(data, keyTable + keyOffset);
                    if (name != key) continue;

                    long start = dataTable + dataOffset;
                    if (start < 0 || start + length > data.Length) return null;

                    string value = Encoding.UTF8.GetString(data, (int)start, (int)length);
                    int nul = value.IndexOf('\0');
                    return nul >= 0 ? value.Substring(0, nul) : value;
                }
            }
            catch (Exception)
            {
                // Absichtlich breit: korrupte Datei = kein Name, mehr nicht.
            }

            return null;
        }

        private static string ReadKey(byte[] data, long offset)
        {
            if (offset < 0 || offset >= data.Length) return null;

            int end = (int)offset;
            while (end < data.Length && data[end] != 0) end++;
            return Encoding.ASCII.GetString(data, (int)offset, end - (int)offset);
        }
    }
}
```

- [ ] **Step 5: Testlauf — grün**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~SfoReaderTests"`
Expected: PASS (3 Tests).

- [ ] **Step 6: Commit**

```bash
git add src/ScreenshotGallery.Core/Ps4/SfoReader.cs tests
git commit -m "Add the param.sfo title reader with a real PS4 fixture"
```

---

### Task 5: IFtpClient + PhotoRepository (Pfadmodell, Laden, Löschen)

**Files:**
- Create: `src/ScreenshotGallery.Core/Ftp/IFtpClient.cs`
- Create: `src/ScreenshotGallery.Core/Ps4/Ps4Paths.cs`
- Create: `src/ScreenshotGallery.Core/Ps4/GameFolder.cs`
- Create: `src/ScreenshotGallery.Core/Ps4/PhotoRepository.cs`
- Test: `tests/ScreenshotGallery.Core.Tests/Ps4PathsTests.cs`
- Test: `tests/ScreenshotGallery.Core.Tests/PhotoRepositoryTests.cs` (mit `FakeFtpClient`)

**Interfaces:**
- Produces:
  - `IFtpClient { Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct); Task<byte[]> DownloadAsync(string path, CancellationToken ct); Task DeleteAsync(string path, CancellationToken ct); }`
  - `static class Ps4Paths`: `PhotoRoot`, `PhotoPath(Screenshot)`, `DatPath(Screenshot)`, `ThumbnailPath(Screenshot)`, `SfoPath(string titleId)`, `IconPath(string titleId)`
  - `GameFolder { string TitleId; string Name; byte[] IconBytes; IReadOnlyList<Screenshot> Screenshots; }`
  - `PhotoRepository(IFtpClient ftp)`: `Task<IReadOnlyList<GameFolder>> LoadAsync(CancellationToken ct)` (neueste zuerst je Spiel, Spiele alphabetisch), `Task<byte[]> DownloadThumbnailAsync(Screenshot, ct)`, `Task<byte[]> DownloadFullAsync(Screenshot, ct)`, `Task DeleteAsync(Screenshot, ct)` (`.jpg` muss gelingen, `.dat`/Thumbnail tolerieren Fehlen)

- [ ] **Step 1: Failing Tests**

`tests/ScreenshotGallery.Core.Tests/Ps4PathsTests.cs`:

```csharp
using System;
using ScreenshotGallery.Core.Ps4;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class Ps4PathsTests
    {
        private static readonly Screenshot Shot = new Screenshot(
            "CUSA00411", "104", "20260801_084933_00470650.jpg", 461788, DateTime.MinValue);

        [Fact]
        public void All_five_paths_match_the_verified_ps4_layout()
        {
            Assert.Equal("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg",
                Ps4Paths.PhotoPath(Shot));
            Assert.Equal("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.dat",
                Ps4Paths.DatPath(Shot));
            Assert.Equal("/user/av_contents/thumbnails/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg.jpg",
                Ps4Paths.ThumbnailPath(Shot));
            Assert.Equal("/system_data/priv/appmeta/CUSA00411/param.sfo", Ps4Paths.SfoPath("CUSA00411"));
            Assert.Equal("/user/appmeta/CUSA00411/icon0.png", Ps4Paths.IconPath("CUSA00411"));
        }
    }
}
```

`tests/ScreenshotGallery.Core.Tests/PhotoRepositoryTests.cs`:

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

    public class PhotoRepositoryTests
    {
        private static FtpEntry Dir(string name) { return new FtpEntry(name, true, 512); }
        private static FtpEntry File(string name, long size) { return new FtpEntry(name, false, size); }

        /// <summary>Baut die am 2026-08-01 verifizierte Struktur nach.</summary>
        private static FakeFtpClient NewPs4()
        {
            var ftp = new FakeFtpClient();
            ftp.Listings["/user/av_contents/photo/NPXS20001"] =
                new List<FtpEntry> { Dir("CUSA00411") };
            ftp.Listings["/user/av_contents/photo/NPXS20001/CUSA00411"] =
                new List<FtpEntry> { Dir("104"), Dir("3d5") };
            ftp.Listings["/user/av_contents/photo/NPXS20001/CUSA00411/104"] = new List<FtpEntry>
            {
                File("20260801_084933_00470650.jpg", 461788),
                File("20260801_084933_00470650.dat", 592)
            };
            ftp.Listings["/user/av_contents/photo/NPXS20001/CUSA00411/3d5"] = new List<FtpEntry>
            {
                File("20260801_103432_00907244.jpg", 469114),
                File("20260801_103432_00907244.dat", 592)
            };
            // param.sfo/icon0.png fehlen absichtlich → Fallback-Pfad.
            return ftp;
        }

        [Fact]
        public async Task Loading_collects_jpgs_across_hash_folders_newest_first()
        {
            var repo = new PhotoRepository(NewPs4());
            var games = await repo.LoadAsync(CancellationToken.None);

            var game = Assert.Single(games);
            Assert.Equal(2, game.Screenshots.Count);
            Assert.Equal("20260801_103432_00907244.jpg", game.Screenshots[0].FileName);
            Assert.Equal("104", game.Screenshots[1].HashDir);
            Assert.All(game.Screenshots, s => Assert.EndsWith(".jpg", s.FileName));
        }

        [Fact]
        public async Task A_missing_sfo_falls_back_to_the_title_id()
        {
            var repo = new PhotoRepository(NewPs4());
            var games = await repo.LoadAsync(CancellationToken.None);
            Assert.Equal("CUSA00411", games[0].Name);
            Assert.Null(games[0].IconBytes);
        }

        [Fact]
        public async Task A_present_sfo_and_icon_are_used()
        {
            var ftp = NewPs4();
            ftp.Files["/system_data/priv/appmeta/CUSA00411/param.sfo"] =
                System.IO.File.ReadAllBytes(System.IO.Path.Combine("TestData", "param.sfo"));
            ftp.Files["/user/appmeta/CUSA00411/icon0.png"] = new byte[] { 1, 2, 3 };

            var repo = new PhotoRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);

            Assert.Equal("Grand Theft Auto V", games[0].Name);
            Assert.Equal(new byte[] { 1, 2, 3 }, games[0].IconBytes);
        }

        [Fact]
        public async Task Delete_removes_jpg_dat_and_thumbnail()
        {
            var ftp = NewPs4();
            var repo = new PhotoRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);
            var shot = games[0].Screenshots[1];

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

            var repo = new PhotoRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);

            await repo.DeleteAsync(games[0].Screenshots[1], CancellationToken.None);
            Assert.Contains(ftp.Deleted, p => p.EndsWith(".jpg") && !p.EndsWith(".jpg.jpg"));
        }

        [Fact]
        public async Task A_failing_jpg_delete_throws()
        {
            var ftp = NewPs4();
            ftp.FailDeletes.Add("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg");

            var repo = new PhotoRepository(ftp);
            var games = await repo.LoadAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.DeleteAsync(games[0].Screenshots[1], CancellationToken.None));
        }
    }
}
```

- [ ] **Step 2: Testlauf — muss scheitern**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~PhotoRepositoryTests|FullyQualifiedName~Ps4PathsTests"`
Expected: FAIL.

- [ ] **Step 3: Implementieren**

`src/ScreenshotGallery.Core/Ftp/IFtpClient.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenshotGallery.Core.Ftp
{
    /// <summary>Naht zwischen Pfadlogik und Draht — im Test ein Dictionary.</summary>
    public interface IFtpClient
    {
        Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct);
        Task<byte[]> DownloadAsync(string path, CancellationToken ct);
        Task DeleteAsync(string path, CancellationToken ct);
    }
}
```

`src/ScreenshotGallery.Core/Ps4/Ps4Paths.cs`:

```csharp
namespace ScreenshotGallery.Core.Ps4
{
    /// <summary>
    /// Alle PS4-Pfade an EINER Stelle — verifiziert am 2026-08-01 gegen
    /// GoldHEN 9.00 (siehe Spec, "Verifizierte PS4-Fakten").
    /// </summary>
    public static class Ps4Paths
    {
        public const string PhotoRoot = "/user/av_contents/photo/NPXS20001";
        public const string ThumbnailRoot = "/user/av_contents/thumbnails/photo/NPXS20001";

        public static string TitleDir(string titleId)
        {
            return PhotoRoot + "/" + titleId;
        }

        public static string PhotoPath(Screenshot s)
        {
            return PhotoRoot + "/" + s.TitleId + "/" + s.HashDir + "/" + s.FileName;
        }

        public static string DatPath(Screenshot s)
        {
            var name = s.FileName;
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(0, dot);
            return PhotoRoot + "/" + s.TitleId + "/" + s.HashDir + "/" + name + ".dat";
        }

        /// <summary>Die PS4 hängt ans Thumbnail schlicht ein zweites ".jpg" an.</summary>
        public static string ThumbnailPath(Screenshot s)
        {
            return ThumbnailRoot + "/" + s.TitleId + "/" + s.HashDir + "/" + s.FileName + ".jpg";
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

`src/ScreenshotGallery.Core/Ps4/GameFolder.cs`:

```csharp
using System.Collections.Generic;

namespace ScreenshotGallery.Core.Ps4
{
    /// <summary>Ein Spiel samt seinen Screenshots — eine Gruppe in der Gallery.</summary>
    public sealed class GameFolder
    {
        public GameFolder(string titleId, string name, byte[] iconBytes, IReadOnlyList<Screenshot> screenshots)
        {
            TitleId = titleId;
            Name = name;
            IconBytes = iconBytes;
            Screenshots = screenshots;
        }

        public string TitleId { get; }
        public string Name { get; }
        public byte[] IconBytes { get; }
        public IReadOnlyList<Screenshot> Screenshots { get; }
    }
}
```

`src/ScreenshotGallery.Core/Ps4/PhotoRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class PhotoRepository
    {
        /// <summary>Max. gleichzeitige FTP-Zugriffe beim Laden (Spec: 3).</summary>
        private const int LoadParallelism = 3;

        private readonly IFtpClient _ftp;

        public PhotoRepository(IFtpClient ftp)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            _ftp = ftp;
        }

        public async Task<IReadOnlyList<GameFolder>> LoadAsync(CancellationToken ct)
        {
            var titleDirs = await _ftp.ListAsync(Ps4Paths.PhotoRoot, ct).ConfigureAwait(false);

            var gate = new SemaphoreSlim(LoadParallelism);
            var tasks = titleDirs
                .Where(e => e.IsDirectory)
                .Select(e => LoadGameGatedAsync(e.Name, gate, ct))
                .ToList();

            var games = await Task.WhenAll(tasks).ConfigureAwait(false);

            return games
                .Where(g => g.Screenshots.Count > 0)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
            var shots = new List<Screenshot>();

            var hashDirs = await _ftp.ListAsync(Ps4Paths.TitleDir(titleId), ct).ConfigureAwait(false);
            foreach (var hash in hashDirs.Where(e => e.IsDirectory))
            {
                var files = await _ftp.ListAsync(Ps4Paths.TitleDir(titleId) + "/" + hash.Name, ct)
                    .ConfigureAwait(false);
                foreach (var file in files)
                {
                    if (file.IsDirectory) continue;
                    if (!file.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) continue;

                    shots.Add(new Screenshot(titleId, hash.Name, file.Name, file.Size,
                        Screenshot.ParseTimestamp(file.Name, DateTime.MinValue)));
                }
            }

            shots.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));   // neueste zuerst

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

            return new GameFolder(titleId, string.IsNullOrEmpty(name) ? titleId : name, icon, shots);
        }

        public Task<byte[]> DownloadThumbnailAsync(Screenshot shot, CancellationToken ct)
        {
            return _ftp.DownloadAsync(Ps4Paths.ThumbnailPath(shot), ct);
        }

        public Task<byte[]> DownloadFullAsync(Screenshot shot, CancellationToken ct)
        {
            return _ftp.DownloadAsync(Ps4Paths.PhotoPath(shot), ct);
        }

        /// <summary>
        /// Löscht .jpg, .dat und Thumbnail. Das JPEG ist die Wahrheit: scheitert
        /// es, scheitert der Löschvorgang. .dat/Thumbnail dürfen fehlen —
        /// verwaiste Reste sind auf der PS4 normal (Spec).
        /// </summary>
        public async Task DeleteAsync(Screenshot shot, CancellationToken ct)
        {
            await _ftp.DeleteAsync(Ps4Paths.PhotoPath(shot), ct).ConfigureAwait(false);

            try { await _ftp.DeleteAsync(Ps4Paths.DatPath(shot), ct).ConfigureAwait(false); }
            catch (Exception) { }
            try { await _ftp.DeleteAsync(Ps4Paths.ThumbnailPath(shot), ct).ConfigureAwait(false); }
            catch (Exception) { }
        }
    }
}
```

- [ ] **Step 4: Testlauf — grün**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~PhotoRepositoryTests|FullyQualifiedName~Ps4PathsTests"`
Expected: PASS (7 Tests).

- [ ] **Step 5: Commit**

```bash
git add src/ScreenshotGallery.Core tests
git commit -m "Add the photo repository over an abstract ftp client"
```

---

### Task 6: ThumbnailCache

**Files:**
- Create: `src/ScreenshotGallery.Core/Caching/ThumbnailCache.cs`
- Test: `tests/ScreenshotGallery.Core.Tests/ThumbnailCacheTests.cs`

**Interfaces:**
- Produces: `ThumbnailCache(string rootDirectory)`: `byte[] TryGet(string titleId, string fileName)` (null bei Fehlen/0 Bytes), `void Put(string titleId, string fileName, byte[] data)`, `void Remove(string titleId, string fileName)`. Voll-JPEG-Cache = zweite Instanz mit Root `…\cache\full`.

- [ ] **Step 1: Failing Tests**

```csharp
using System;
using System.IO;
using ScreenshotGallery.Core.Caching;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class ThumbnailCacheTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "sg-cache-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public void Put_then_get_round_trips()
        {
            var cache = new ThumbnailCache(_root);
            cache.Put("CUSA00411", "a.jpg", new byte[] { 1, 2, 3 });
            Assert.Equal(new byte[] { 1, 2, 3 }, cache.TryGet("CUSA00411", "a.jpg"));
        }

        [Fact]
        public void A_miss_yields_null()
        {
            var cache = new ThumbnailCache(_root);
            Assert.Null(cache.TryGet("CUSA00411", "fehlt.jpg"));
        }

        [Fact]
        public void A_zero_byte_file_counts_as_a_miss()
        {
            // Ein abgebrochener Download hinterlässt 0 Bytes — die dürfen nie
            // als "Bild" zurückkommen, sonst zeigt die Gallery ewig Müll.
            var cache = new ThumbnailCache(_root);
            Directory.CreateDirectory(Path.Combine(_root, "CUSA00411"));
            File.WriteAllBytes(Path.Combine(_root, "CUSA00411", "leer.jpg"), new byte[0]);
            Assert.Null(cache.TryGet("CUSA00411", "leer.jpg"));
        }

        [Fact]
        public void Remove_deletes_the_entry()
        {
            var cache = new ThumbnailCache(_root);
            cache.Put("CUSA00411", "a.jpg", new byte[] { 1 });
            cache.Remove("CUSA00411", "a.jpg");
            Assert.Null(cache.TryGet("CUSA00411", "a.jpg"));
            cache.Remove("CUSA00411", "a.jpg");     // doppelt: kein Wurf
        }
    }
}
```

- [ ] **Step 2: Testlauf — muss scheitern**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~ThumbnailCacheTests"`
Expected: FAIL.

- [ ] **Step 3: Implementieren** — `src/ScreenshotGallery.Core/Caching/ThumbnailCache.cs`:

```csharp
using System;
using System.IO;

namespace ScreenshotGallery.Core.Caching
{
    /// <summary>
    /// Disk-Cache: root\TitleID\Dateiname. Bewusst dumm — kein Verfall, keine
    /// Größengrenze. Thumbnails sind ~24 KB, ein voller Cache ist billiger als
    /// jede Verwaltung (YAGNI; die Spec sieht keinen Verfall vor).
    /// </summary>
    public sealed class ThumbnailCache
    {
        private readonly string _root;

        public ThumbnailCache(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory))
                throw new ArgumentException("Cache-Wurzel fehlt.", nameof(rootDirectory));
            _root = rootDirectory;
        }

        private string PathFor(string titleId, string fileName)
        {
            return Path.Combine(_root, titleId, fileName);
        }

        public byte[] TryGet(string titleId, string fileName)
        {
            try
            {
                var path = PathFor(titleId, fileName);
                if (!File.Exists(path)) return null;
                var data = File.ReadAllBytes(path);
                return data.Length == 0 ? null : data;
            }
            catch (IOException)
            {
                return null;     // parallel beschrieben o. ä. — dann eben Fehltreffer
            }
        }

        public void Put(string titleId, string fileName, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            var path = PathFor(titleId, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
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
    }
}
```

- [ ] **Step 4: Testlauf — grün**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~ThumbnailCacheTests"`
Expected: PASS (4 Tests).

- [ ] **Step 5: Commit**

```bash
git add src/ScreenshotGallery.Core/Caching tests
git commit -m "Add the plain disk thumbnail cache"
```

---

### Task 7: SettingsStore

**Files:**
- Create: `src/ScreenshotGallery.Core/Config/AppSettings.cs`
- Create: `src/ScreenshotGallery.Core/Config/SettingsStore.cs`
- Test: `tests/ScreenshotGallery.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `AppSettings { string Ip; int Port; }` (Defaults `10.10.10.236`/`2121`); `static AppSettings SettingsStore.Load(string path)`, `static void SettingsStore.Save(string path, AppSettings settings)`.

- [ ] **Step 1: Failing Tests**

```csharp
using System;
using System.IO;
using ScreenshotGallery.Core.Config;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class SettingsStoreTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(),
            "sg-settings-" + Guid.NewGuid().ToString("N") + ".json");

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Fact]
        public void Save_then_load_round_trips()
        {
            SettingsStore.Save(_path, new AppSettings { Ip = "192.168.0.7", Port = 1337 });
            var loaded = SettingsStore.Load(_path);
            Assert.Equal("192.168.0.7", loaded.Ip);
            Assert.Equal(1337, loaded.Port);
        }

        [Fact]
        public void A_missing_file_yields_the_ps4_defaults()
        {
            var loaded = SettingsStore.Load(_path);
            Assert.Equal("10.10.10.236", loaded.Ip);
            Assert.Equal(2121, loaded.Port);
        }

        [Fact]
        public void A_corrupt_file_yields_the_defaults_instead_of_throwing()
        {
            File.WriteAllText(_path, "{ kaputt !!!");
            var loaded = SettingsStore.Load(_path);
            Assert.Equal("10.10.10.236", loaded.Ip);
            Assert.Equal(2121, loaded.Port);
        }
    }
}
```

- [ ] **Step 2: Testlauf — muss scheitern**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: FAIL.

- [ ] **Step 3: Implementieren**

`src/ScreenshotGallery.Core/Config/AppSettings.cs`:

```csharp
namespace ScreenshotGallery.Core.Config
{
    public sealed class AppSettings
    {
        public string Ip { get; set; } = "10.10.10.236";
        public int Port { get; set; } = 2121;
    }
}
```

Achtung LangVersion 7.3: Property-Initializer wie oben sind erlaubt; `init`-Setter u. ä. nicht.

`src/ScreenshotGallery.Core/Config/SettingsStore.cs`:

```csharp
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ScreenshotGallery.Core.Config
{
    /// <summary>
    /// Zwei Felder, eine Datei. Handgeschriebenes Mini-JSON statt NuGet
    /// (Spec-Entscheidung): der Regex-Leser toleriert alles, was er nicht
    /// versteht, und fällt auf die Defaults zurück.
    /// </summary>
    public static class SettingsStore
    {
        private static readonly Regex IpPattern =
            new Regex("\"Ip\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
        private static readonly Regex PortPattern =
            new Regex("\"Port\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        public static AppSettings Load(string path)
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(path)) return settings;
                var text = File.ReadAllText(path);

                var ip = IpPattern.Match(text);
                if (ip.Success && ip.Groups[1].Value.Length > 0)
                    settings.Ip = ip.Groups[1].Value;

                var port = PortPattern.Match(text);
                int parsed;
                if (port.Success && int.TryParse(port.Groups[1].Value, out parsed)
                    && parsed > 0 && parsed <= 65535)
                    settings.Port = parsed;
            }
            catch (Exception)
            {
                // Korrupte Settings sind ein Neuanfang, kein Absturz.
            }
            return settings;
        }

        public static void Save(string path, AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "{\"Ip\":\"" + settings.Ip + "\",\"Port\":" + settings.Port + "}");
        }
    }
}
```

- [ ] **Step 4: Testlauf — grün**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: PASS (3 Tests).

- [ ] **Step 5: Commit**

```bash
git add src/ScreenshotGallery.Core/Config tests
git commit -m "Add the two-field settings store"
```

---

### Task 8: FtpClient (der echte Draht)

**Files:**
- Create: `src/ScreenshotGallery.Core/Ftp/FtpClient.cs`
- Test: `tests/ScreenshotGallery.Core.Tests/FtpClientTests.cs` (nur der URI-Bau — der Draht selbst wird in Task 14 gegen die echte PS4 gefahren)

**Interfaces:**
- Produces: `FtpClient : IFtpClient` mit `FtpClient(string host, int port)`; `internal static Uri BuildUri(string host, int port, string path)`.

- [ ] **Step 1: Failing Test** (dafür braucht das Testprojekt Zugriff auf `internal`: in `src/ScreenshotGallery.Core/Properties/AssemblyInfo.cs` anlegen mit `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ScreenshotGallery.Core.Tests")]`):

```csharp
using ScreenshotGallery.Core.Ftp;
using Xunit;

namespace ScreenshotGallery.Core.Tests
{
    public class FtpClientTests
    {
        [Fact]
        public void The_uri_keeps_the_path_and_escapes_spaces()
        {
            var uri = FtpClient.BuildUri("10.10.10.236", 2121,
                "/user/av_contents/photo/NPXS20001/CUSA00411/104/mit leerzeichen.jpg");

            Assert.Equal("ftp://10.10.10.236:2121/user/av_contents/photo/NPXS20001/CUSA00411/104/mit%20leerzeichen.jpg",
                uri.AbsoluteUri);
        }

        [Fact]
        public void A_hash_in_a_name_does_not_truncate_the_path()
        {
            var uri = FtpClient.BuildUri("h", 21, "/a/b#c.jpg");
            Assert.EndsWith("/a/b%23c.jpg", uri.AbsoluteUri);
        }
    }
}
```

- [ ] **Step 2: Testlauf — muss scheitern**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q --filter "FullyQualifiedName~FtpClientTests"`
Expected: FAIL.

- [ ] **Step 3: Implementieren** — `src/ScreenshotGallery.Core/Ftp/FtpClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenshotGallery.Core.Ftp
{
    /// <summary>
    /// Dünner Draht über FtpWebRequest (auf net48 der richtige Weg; die
    /// Deprecation gilt .NET 6+). GoldHEN: anonym, passiv, binär.
    /// Bewusst OHNE eigene Wiederholungslogik — die App meldet Fehler und
    /// der Anwender drückt erneut Verbinden (Spec, Fehlerbehandlung).
    /// </summary>
    public sealed class FtpClient : IFtpClient
    {
        /// <summary>Listen ist billig: 8 s. Transfers: 30 s (Spec).</summary>
        private const int ListTimeoutMs = 8000;
        private const int TransferTimeoutMs = 30000;

        private readonly string _host;
        private readonly int _port;

        public FtpClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        internal static Uri BuildUri(string host, int port, string path)
        {
            var builder = new StringBuilder("ftp://").Append(host).Append(':').Append(port);
            foreach (var segment in path.Split('/'))
            {
                if (segment.Length == 0) continue;
                builder.Append('/').Append(Uri.EscapeDataString(segment));
            }
            return new Uri(builder.ToString());
        }

        private FtpWebRequest Create(string path, string method, int timeoutMs)
        {
            var request = (FtpWebRequest)WebRequest.Create(BuildUri(_host, _port, path));
            request.Method = method;
            request.Credentials = new NetworkCredential("anonymous", "anonymous");
            request.UsePassive = true;
            request.UseBinary = true;
            request.KeepAlive = true;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            return request;
        }

        public async Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            var request = Create(path, WebRequestMethods.Ftp.ListDirectoryDetails, ListTimeoutMs);
            using (ct.Register(request.Abort))
            using (var response = await request.GetResponseAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                var listing = await reader.ReadToEndAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return UnixListingParser.Parse(listing);
            }
        }

        public async Task<byte[]> DownloadAsync(string path, CancellationToken ct)
        {
            var request = Create(path, WebRequestMethods.Ftp.DownloadFile, TransferTimeoutMs);
            using (ct.Register(request.Abort))
            using (var response = await request.GetResponseAsync().ConfigureAwait(false))
            using (var stream = response.GetResponseStream())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, 81920, ct).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        public async Task DeleteAsync(string path, CancellationToken ct)
        {
            var request = Create(path, WebRequestMethods.Ftp.DeleteFile, TransferTimeoutMs);
            using (ct.Register(request.Abort))
            using (await request.GetResponseAsync().ConfigureAwait(false))
            {
            }
        }
    }
}
```

- [ ] **Step 4: Testlauf — grün, gesamte Suite**

Run: `dotnet test ScreenshotGallery.sln --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ScreenshotGallery.Core tests
git commit -m "Add the FtpWebRequest-based ftp client"
```

---

### Task 9: MainForm — Toolbar, Verbinden-Flow, Gruppen erscheinen

**Files:**
- Modify: `src/ScreenshotGallery.App/MainForm.cs` (Rumpf aus Task 1 ausbauen)

**Interfaces:**
- Consumes: `GalleryControl` (UIFramework), `PhotoRepository`, `FtpClient`, `SettingsStore`, `ThumbnailCache`.
- Produces: laufende App, die nach „Verbinden" Gruppen mit Platzhalter-Kacheln zeigt. Interne Struktur für Task 10–13: Felder `_gallery` (GalleryControl), `_repository`, `_thumbnails`/`_fullImages` (ThumbnailCache), `_loadCts` (CancellationTokenSource), `_status` (SkinLabel); `Screenshot`-Objekte hängen in `GalleryItem.Tag`, `GameFolder` in `GalleryItemGroup.Tag`.

- [ ] **Step 1: MainForm ausbauen** — vollständiger Stand nach diesem Task:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScreenshotGallery.Core.Caching;
using ScreenshotGallery.Core.Config;
using ScreenshotGallery.Core.Ftp;
using ScreenshotGallery.Core.Ps4;
using UIFramework.Controls;
using UIFramework.Core.Controls;
using UIFramework.Gallery;

namespace ScreenshotGallery.App
{
    internal sealed class MainForm : SkinnedForm
    {
        private static readonly string DataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotGallery");
        private static readonly string SettingsPath = Path.Combine(DataRoot, "settings.json");

        private readonly SkinTextBox _ip = new SkinTextBox();
        private readonly SkinTextBox _port = new SkinTextBox();
        private readonly SkinButton _connect = new SkinButton { Text = "Verbinden", AutoSize = true };
        private readonly SkinButton _refresh = new SkinButton { Text = "Aktualisieren", AutoSize = true, Enabled = false };
        private readonly SkinLabel _status = new SkinLabel { Text = "Getrennt." };
        private readonly GalleryControl _gallery = new GalleryControl { Dock = DockStyle.Fill };

        private readonly ThumbnailCache _thumbnails = new ThumbnailCache(Path.Combine(DataRoot, "cache"));
        private readonly ThumbnailCache _fullImages = new ThumbnailCache(Path.Combine(DataRoot, "cache", "full"));

        private PhotoRepository _repository;
        private CancellationTokenSource _loadCts;

        public MainForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "PS4 Screenshot Gallery";
            ClientSize = new Size(1100, 700);
            StartPosition = FormStartPosition.CenterScreen;

            var settings = SettingsStore.Load(SettingsPath);
            _ip.Text = settings.Ip;
            _port.Text = settings.Port.ToString();

            var toolbar = new SkinPanel { Dock = DockStyle.Top, Height = 44 };
            var ipLabel = new SkinLabel { Text = "IP:", Location = new Point(8, 12), AutoSize = true };
            _ip.SetBounds(40, 8, 130, 28);
            var portLabel = new SkinLabel { Text = "Port:", Location = new Point(180, 12), AutoSize = true };
            _port.SetBounds(218, 8, 60, 28);
            _connect.Location = new Point(290, 8);
            _refresh.Location = new Point(380, 8);
            _status.Location = new Point(490, 12);
            _status.AutoSize = true;
            toolbar.Controls.Add(ipLabel);
            toolbar.Controls.Add(_ip);
            toolbar.Controls.Add(portLabel);
            toolbar.Controls.Add(_port);
            toolbar.Controls.Add(_connect);
            toolbar.Controls.Add(_refresh);
            toolbar.Controls.Add(_status);

            _gallery.Gallery.ImageSize = new Size(192, 108);
            _gallery.Gallery.ItemCheckMode = ItemCheckMode.Multiple;

            Controls.Add(_gallery);
            Controls.Add(toolbar);       // Dock=Top NACH Dock=Fill hinzufügen: WinForms dockt in umgekehrter Add-Reihenfolge

            _connect.Click += async (s, e) => await ConnectAsync();
            _refresh.Click += async (s, e) => await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            int port;
            if (!int.TryParse(_port.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                _status.Text = "Ungültiger Port.";
                return;
            }
            var ip = _ip.Text.Trim();
            if (ip.Length == 0)
            {
                _status.Text = "IP fehlt.";
                return;
            }

            // Laufende Ladung/Queue stoppen (Spec: Stornieren bei Aktualisieren).
            if (_loadCts != null) _loadCts.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            SettingsStore.Save(SettingsPath, new AppSettings { Ip = ip, Port = port });
            _repository = new PhotoRepository(new FtpClient(ip, port));

            _connect.Enabled = false;
            _refresh.Enabled = false;
            _status.Text = "Verbinde mit " + ip + ":" + port + " …";
            _gallery.Gallery.Groups.Clear();

            try
            {
                var games = await _repository.LoadAsync(ct);
                if (ct.IsCancellationRequested) return;

                int total = 0;
                foreach (var game in games)
                {
                    var group = new GalleryItemGroup
                    {
                        Caption = game.Name + " (" + game.TitleId + ")",
                        CaptionImage = ToImage(game.IconBytes),
                        Tag = game
                    };
                    foreach (var shot in game.Screenshots)
                    {
                        group.Items.Add(new GalleryItem
                        {
                            Caption = shot.Timestamp.ToString("dd.MM.yyyy HH:mm"),
                            Description = FormatSize(shot.Size),
                            Tag = shot
                        });
                        total++;
                    }
                    _gallery.Gallery.Groups.Add(group);
                }

                _status.Text = games.Count + " Spiele, " + total + " Screenshots.";
            }
            catch (Exception ex)
            {
                _status.Text = "Fehler: " + Root(ex).Message;
            }
            finally
            {
                _connect.Enabled = true;
                _refresh.Enabled = true;
            }
        }

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        private static Image ToImage(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                using (var stream = new MemoryStream(data))
                {
                    // Kopie ziehen: Image.FromStream verlangt einen offenen Stream
                    // für die Lebenszeit des Bildes — die Kopie entkoppelt das.
                    using (var direct = Image.FromStream(stream))
                    {
                        return new Bitmap(direct);
                    }
                }
            }
            catch (Exception)
            {
                return null;     // korrupte Bytes: dann eben ohne Icon/Bild
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_loadCts != null) _loadCts.Cancel();
            base.OnFormClosed(e);
        }
    }
}
```

- [ ] **Step 2: Bauen und von Hand fahren** (PS4 an):

```bash
dotnet build src/ScreenshotGallery.App/ScreenshotGallery.App.csproj --nologo -v q
./src/ScreenshotGallery.App/bin/Debug/net48/PS4ScreenshotGallery.exe
```

Prüfen: Fenster dunkel geskinnt; Verbinden → Status „1 Spiele, N Screenshots.", Gruppe „Grand Theft Auto V (CUSA00411)" mit Icon und Platzhalter-Kacheln (Bilder kommen in Task 10). Falsche IP eintragen → roter Faden: Status zeigt Fehlertext, App bleibt bedienbar.

- [ ] **Step 3: Commit**

```bash
git add src/ScreenshotGallery.App
git commit -m "Build the main form with the connect flow and game groups"
```

---

### Task 10: ThumbnailLoader — Bilder erscheinen lazy

**Files:**
- Create: `src/ScreenshotGallery.App/ThumbnailLoader.cs`
- Modify: `src/ScreenshotGallery.App/MainForm.cs` (Loader anschließen)

**Interfaces:**
- Produces: `ThumbnailLoader(PhotoRepository repo, ThumbnailCache cache)`: `void Enqueue(GalleryItem item, Screenshot shot, CancellationToken ct)`, `int Parallelism = 3` fest.

- [ ] **Step 1: Loader schreiben** — `src/ScreenshotGallery.App/ThumbnailLoader.cs`:

```csharp
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ScreenshotGallery.Core.Caching;
using ScreenshotGallery.Core.Ps4;
using UIFramework.Gallery;

namespace ScreenshotGallery.App
{
    /// <summary>
    /// Bedient ItemImageNeeded: Cache zuerst, sonst FTP (max. 3 parallel,
    /// Spec). item.Image darf von hier aus gesetzt werden — der Setter des
    /// GalleryControls marshallt die Invalidierung selbst auf den UI-Thread.
    /// Ein gescheitertes Thumbnail bleibt Platzhalter; kein Dialog, kein
    /// Absturz (Spec: stiller Fallback).
    /// </summary>
    internal sealed class ThumbnailLoader
    {
        private readonly PhotoRepository _repository;
        private readonly ThumbnailCache _cache;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(3);

        public ThumbnailLoader(PhotoRepository repository, ThumbnailCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public void Enqueue(GalleryItem item, Screenshot shot, CancellationToken ct)
        {
            // async void bewusst NICHT: die Task läuft frei, Fehler fängt sie selbst.
            var _ = LoadAsync(item, shot, ct);
        }

        private async Task LoadAsync(GalleryItem item, Screenshot shot, CancellationToken ct)
        {
            try
            {
                var cached = _cache.TryGet(shot.TitleId, shot.FileName);
                if (cached != null)
                {
                    item.Image = ToImage(cached);
                    return;
                }

                await _gate.WaitAsync(ct).ConfigureAwait(false);
                byte[] data;
                try
                {
                    data = await _repository.DownloadThumbnailAsync(shot, ct).ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }

                if (ct.IsCancellationRequested || data == null || data.Length == 0) return;

                _cache.Put(shot.TitleId, shot.FileName, data);
                item.Image = ToImage(data);
            }
            catch (Exception)
            {
                // Platzhalter bleibt stehen — bewusst still (Spec).
            }
        }

        private static Image ToImage(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var direct = Image.FromStream(stream))
            {
                return new Bitmap(direct);
            }
        }
    }
}
```

- [ ] **Step 2: In MainForm anschließen** — Feld + Verdrahtung:

```csharp
        private ThumbnailLoader _thumbnailLoader;
```

Im Konstruktor nach dem Gallery-Setup:

```csharp
            _gallery.Gallery.ItemImageNeeded += (s, e) =>
            {
                var shot = e.Item.Tag as Screenshot;
                if (shot == null || _thumbnailLoader == null || _loadCts == null) return;
                _thumbnailLoader.Enqueue(e.Item, shot, _loadCts.Token);
            };
```

In `ConnectAsync` direkt nach dem Erzeugen von `_repository`:

```csharp
            _thumbnailLoader = new ThumbnailLoader(_repository, _thumbnails);
```

- [ ] **Step 3: Fahren** — App starten, verbinden: Kacheln füllen sich sichtbar von oben; scrollen lädt nach; ZWEITER Start (App zu, wieder auf, verbinden): Bilder erscheinen sofort (Cache-Treffer, im Explorer unter `%LocalAppData%\ScreenshotGallery\cache\CUSA00411\` nachsehen — Dateien vorhanden).

- [ ] **Step 4: Commit**

```bash
git add src/ScreenshotGallery.App
git commit -m "Load thumbnails lazily through the disk cache and ftp queue"
```

---

### Task 11: Kontextmenü — Herunterladen und Zwischenablage

**Files:**
- Modify: `src/ScreenshotGallery.App/MainForm.cs`

**Interfaces:**
- Consumes: `PopupMenu` (UIFramework.Controls, Muster: `GridForm._rowMenu` im UIFramework-Demo — vor dem Implementieren dort die exakte Entry-API ablesen: `E:\Projects\UIFramework\src\UIFramework.Demo\GridForm.cs` und `src\UIFramework.Controls\Menus\MenuEntry.cs`).

- [ ] **Step 1: Menü + Aktionen** — Felder und Verdrahtung in MainForm:

```csharp
        private readonly PopupMenu _itemMenu = new PopupMenu();
```

Im Konstruktor (nach dem Gallery-Setup); die Entry-API vorher aus `MenuEntry.cs` ablesen — hier das Muster mit `MenuEntry`-Objekten und `Show(control, location)` wie im GridForm-Kontextmenü:

```csharp
            _gallery.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                if (!_gallery.Gallery.CheckedItems.GetEnumerator().MoveNext()) return;
                ShowItemMenu(e.Location);
            };
            _gallery.Gallery.ItemDoubleClick += (s, e) => OpenPreview(e.Item);
```

`ShowItemMenu` baut das Menü bei jedem Öffnen frisch (der Aktiv-Zustand hängt an der Auswahl):

```csharp
        private void ShowItemMenu(Point location)
        {
            var selected = new List<GalleryItem>(_gallery.Gallery.CheckedItems);
            _itemMenu.Entries.Clear();
            _itemMenu.Entries.Add(new MenuEntry("Herunterladen…",
                async () => await DownloadSelectedAsync(selected)));
            _itemMenu.Entries.Add(new MenuEntry("In Zwischenablage kopieren",
                async () => await CopyToClipboardAsync(selected))
                { Enabled = selected.Count == 1 });
            _itemMenu.Entries.Add(new MenuEntry("Vorschau öffnen",
                () => OpenPreview(selected[0])) { Enabled = selected.Count == 1 });
            _itemMenu.Entries.Add(MenuEntry.Separator());
            _itemMenu.Entries.Add(new MenuEntry("Löschen…",
                async () => await DeleteSelectedAsync(selected)));
            _itemMenu.Show(_gallery, location);
        }
```

**WICHTIG:** Die `MenuEntry`-Konstruktor-Signatur oben ist eine ANNAHME nach dem Demo-Muster. Erster Schritt dieses Tasks ist, `MenuEntry.cs` und die GridForm-Verwendung zu LESEN und die vier Aufrufe an die echte API anzupassen (Text/Action/Enabled/Separator existieren dort in irgendeiner Form — die Semantik bleibt exakt die geplante).

Download:

```csharp
        private async Task DownloadSelectedAsync(List<GalleryItem> selected)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var failures = new List<string>();
                int done = 0;
                foreach (var item in selected)
                {
                    var shot = (Screenshot)item.Tag;
                    try
                    {
                        var data = await GetFullImageBytesAsync(shot);
                        File.WriteAllBytes(Path.Combine(dialog.SelectedPath, shot.FileName), data);
                        done++;
                        _status.Text = "Heruntergeladen: " + done + " von " + selected.Count + " …";
                    }
                    catch (Exception ex)
                    {
                        failures.Add(shot.FileName + " — " + Root(ex).Message);
                    }
                }

                _status.Text = done + " Screenshot(s) heruntergeladen.";
                if (failures.Count > 0)
                    MessageBox.Show(this, "Fehlgeschlagen:\n" + string.Join("\n", failures),
                        "Herunterladen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Voll-JPEG mit Cache — auch die Vorschau (Task 13) läuft hierüber.</summary>
        private async Task<byte[]> GetFullImageBytesAsync(Screenshot shot)
        {
            var cached = _fullImages.TryGet(shot.TitleId, shot.FileName);
            if (cached != null) return cached;

            var data = await _repository.DownloadFullAsync(shot, _loadCts.Token);
            _fullImages.Put(shot.TitleId, shot.FileName, data);
            return data;
        }
```

Zwischenablage:

```csharp
        private async Task CopyToClipboardAsync(List<GalleryItem> selected)
        {
            if (selected.Count != 1) return;
            var shot = (Screenshot)selected[0].Tag;
            try
            {
                var data = await GetFullImageBytesAsync(shot);
                using (var stream = new MemoryStream(data))
                using (var image = Image.FromStream(stream))
                {
                    Clipboard.SetImage(image);      // kopiert als DIB — Dispose danach ist ok
                }
                _status.Text = "In Zwischenablage kopiert: " + shot.FileName;
            }
            catch (Exception ex)
            {
                _status.Text = "Zwischenablage fehlgeschlagen: " + Root(ex).Message;
            }
        }
```

`OpenPreview` bleibt in diesem Task ein Stummel (Task 13 füllt ihn):

```csharp
        private void OpenPreview(GalleryItem item)
        {
            // Task 13.
        }
```

- [ ] **Step 2: Fahren** — Rechtsklick auf Auswahl: Menü im Skin; „Herunterladen…" mit 2 markierten Bildern in einen Temp-Ordner → beide JPEGs da und öffenbar; „In Zwischenablage" bei genau 1 Bild → in Paint einfügbar, bei 2 markierten ist der Eintrag deaktiviert.

- [ ] **Step 3: Commit**

```bash
git add src/ScreenshotGallery.App
git commit -m "Add the context menu with download and clipboard actions"
```

---

### Task 12: Löschen

**Files:**
- Modify: `src/ScreenshotGallery.App/MainForm.cs`

- [ ] **Step 1: Implementieren**

```csharp
        private async Task DeleteSelectedAsync(List<GalleryItem> selected)
        {
            var confirmed = MessageBox.Show(this,
                selected.Count + " Screenshot(s) werden auf der PS4 gelöscht.\n\n" +
                "Hinweis: Die PS4-eigene Galerie-Datenbank wird per FTP nicht aktualisiert — " +
                "verwaiste Einträge räumt die Konsole beim nächsten Scan selbst weg.\n\n" +
                "Fortfahren?",
                "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirmed != DialogResult.Yes) return;

            var failures = new List<string>();
            foreach (var item in selected)
            {
                var shot = (Screenshot)item.Tag;
                try
                {
                    await _repository.DeleteAsync(shot, _loadCts.Token);
                    _thumbnails.Remove(shot.TitleId, shot.FileName);
                    _fullImages.Remove(shot.TitleId, shot.FileName);
                    RemoveFromGallery(item);
                }
                catch (Exception ex)
                {
                    failures.Add(shot.FileName + " — " + Root(ex).Message);
                }
            }

            _status.Text = (selected.Count - failures.Count) + " Screenshot(s) gelöscht.";
            if (failures.Count > 0)
                MessageBox.Show(this, "Nicht gelöscht:\n" + string.Join("\n", failures),
                    "Löschen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void RemoveFromGallery(GalleryItem item)
        {
            foreach (var group in _gallery.Gallery.Groups)
            {
                if (group.Items.Remove(item))
                {
                    if (group.Items.Count == 0) _gallery.Gallery.Groups.Remove(group);
                    return;
                }
            }
        }
```

- [ ] **Step 2: Fahren — der einzige destruktive Pfad, deshalb mit Wegwerf-Material:** Auf der PS4 vorher 1–2 neue Screenshots knipsen (Share-Taste). Dann: einen davon löschen → Bestätigungsdialog mit DB-Hinweis; Item verschwindet; per `curl ftp://…` verifizieren, dass `.jpg`, `.dat` UND Thumbnail weg sind; die GTA-V-Originale NICHT anfassen. Abbrechen-Pfad fahren (Nein im Dialog → nichts passiert).

- [ ] **Step 3: Commit**

```bash
git add src/ScreenshotGallery.App
git commit -m "Add the delete flow with the ps4 database hint"
```

---

### Task 13: PreviewForm — Vollbild-Vorschau mit Blättern

**Files:**
- Create: `src/ScreenshotGallery.App/PreviewForm.cs`
- Modify: `src/ScreenshotGallery.App/MainForm.cs` (`OpenPreview` füllen)

- [ ] **Step 1: PreviewForm** — `src/ScreenshotGallery.App/PreviewForm.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScreenshotGallery.Core.Ps4;
using UIFramework.Core.Controls;
using UIFramework.Core.Rendering;
using UIFramework.Core.Skinning;

namespace ScreenshotGallery.App
{
    /// <summary>
    /// Vollbild-Vorschau: zeigt das volle JPEG eingepasst, Pfeiltasten blättern
    /// durch dieselbe Gruppe, Esc schließt (Spec). Die Bytes liefert der
    /// Besitzer (MainForm, Cache-first) über den Delegaten — dieses Fenster
    /// kennt weder FTP noch Cache.
    /// </summary>
    internal sealed class PreviewForm : SkinnedForm
    {
        private readonly IReadOnlyList<Screenshot> _shots;
        private readonly Func<Screenshot, Task<byte[]>> _loadBytes;
        private readonly string _gameName;
        private readonly PreviewCanvas _canvas = new PreviewCanvas { Dock = DockStyle.Fill };
        private int _index;

        public PreviewForm(IReadOnlyList<Screenshot> shots, int startIndex,
            string gameName, Func<Screenshot, Task<byte[]>> loadBytes)
        {
            _shots = shots;
            _index = startIndex;
            _gameName = gameName;
            _loadBytes = loadBytes;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1280, 720);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            Controls.Add(_canvas);
            KeyDown += OnPreviewKeyDown;
            Shown += async (s, e) => await ShowCurrentAsync();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode == Keys.Left && _index > 0) { _index--; var _ = ShowCurrentAsync(); }
            if (e.KeyCode == Keys.Right && _index < _shots.Count - 1) { _index++; var _ = ShowCurrentAsync(); }
        }

        private async Task ShowCurrentAsync()
        {
            var shot = _shots[_index];
            Text = _gameName + " — " + shot.Timestamp.ToString("dd.MM.yyyy HH:mm") +
                   "  (" + (_index + 1) + "/" + _shots.Count + ")";
            _canvas.SetImage(null);
            try
            {
                var data = await _loadBytes(shot);
                using (var stream = new MemoryStream(data))
                using (var direct = Image.FromStream(stream))
                {
                    _canvas.SetImage(new Bitmap(direct));
                }
            }
            catch (Exception ex)
            {
                Text = "Fehler: " + ex.Message;
            }
        }

        /// <summary>Skin-Fläche + eingepasstes Bild; kein PictureBox (unskinbar).</summary>
        private sealed class PreviewCanvas : SkinnedControl
        {
            private Image _image;

            protected override string ElementKey
            {
                get { return ElementKeys.Panel; }
            }

            protected override bool ShowFocusRing
            {
                get { return false; }
            }

            public void SetImage(Image image)
            {
                var old = _image;
                _image = image;
                if (old != null) old.Dispose();
                Invalidate();
            }

            protected override void PaintContent(Graphics g, ElementAppearance appearance)
            {
                if (_image == null) return;

                var zone = ClientRectangle;
                double scale = Math.Min((double)zone.Width / _image.Width,
                    (double)zone.Height / _image.Height);
                int width = Math.Max(1, (int)(_image.Width * scale));
                int height = Math.Max(1, (int)(_image.Height * scale));
                var target = new Rectangle(zone.Left + (zone.Width - width) / 2,
                    zone.Top + (zone.Height - height) / 2, width, height);

                SkinPainter.DrawScaledImage(g, _image, target, true);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _image != null) _image.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
```

- [ ] **Step 2: OpenPreview in MainForm füllen**

```csharp
        private void OpenPreview(GalleryItem item)
        {
            var shot = item.Tag as Screenshot;
            if (shot == null || _repository == null) return;

            foreach (var group in _gallery.Gallery.Groups)
            {
                var shots = new List<Screenshot>();
                int start = -1;
                foreach (var candidate in group.Items)
                {
                    if (ReferenceEquals(candidate, item)) start = shots.Count;
                    shots.Add((Screenshot)candidate.Tag);
                }
                if (start < 0) continue;

                var game = (GameFolder)group.Tag;
                new PreviewForm(shots, start, game.Name, GetFullImageBytesAsync).Show(this);
                return;
            }
        }
```

- [ ] **Step 3: Fahren** — Doppelklick öffnet Vorschau (Titel: Name + Zeit + Zähler); Pfeil rechts/links blättert (zweiter Aufruf desselben Bilds kommt aus dem Cache, spürbar sofort); Esc schließt; Enter in der Gallery öffnet ebenfalls (läuft über `ItemDoubleClick`).

- [ ] **Step 4: Commit**

```bash
git add src/ScreenshotGallery.App
git commit -m "Add the full-size preview window with group navigation"
```

---

### Task 14: Abschlussfahrt gegen die echte PS4 + README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Gesamte Suite + Release-Build**

```bash
dotnet test ScreenshotGallery.sln --nologo -v q
dotnet build ScreenshotGallery.sln -c Release --nologo -v q
```

Expected: PASS / Build ohne Warnungen.

- [ ] **Step 2: Komplette Fahrprobe** (PS4 an, GoldHEN-FTP läuft):

1. Start → Verbinden: Gruppe(n) mit Namen + Icon, Thumbnails füllen sich.
2. Falsche IP → Fehlertext im Status, danach richtige IP → funktioniert ohne Neustart.
3. Strg/Umschalt-Mehrfachauswahl; Strg+A; Tastaturnavigation; Mausrad.
4. Herunterladen (2 Bilder) → Dateien im Zielordner öffnen.
5. Zwischenablage (1 Bild) → in Paint einfügen.
6. Vorschau + Blättern + Esc.
7. Wegwerf-Screenshot löschen → per `curl` verifizieren (jpg/dat/thumb weg), PS4-Galerie am Gerät ansehen (Eintrag darf bis zum Rescan hängen — das ist der dokumentierte Hinweis).
8. Aktualisieren → Stand stimmt mit der Konsole überein.
9. App schließen: Prozess endet sauber (kein hängender FTP-Thread), ExitCode 0.

- [ ] **Step 3: README schreiben** — kurz: was es ist, Screenshot, Voraussetzungen (PS4 + GoldHEN-FTP, UIFramework-Repo daneben ausgecheckt), Build (`dotnet build -c Release`), Datenpfade (`%LocalAppData%\ScreenshotGallery`), der Galerie-DB-Hinweis.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Document the tool and finish the first release"
```

---

## Plan-Selbstreview (durchgeführt beim Schreiben)

- **Spec-Abdeckung:** verifizierte Pfade (Task 5, exakt getestet), Toolbar/Settings (7, 9), Gruppen mit Name+Icon (5, 9), Lazy-Thumbnails mit Cache + max. 3 parallel (6, 10), Kontextmenü mit allen vier Aktionen (11–13), Löschen inkl. `.dat`/Thumbnail + DB-Hinweis + Teilfehler-Meldung (5, 12), Vorschau mit Blättern/Esc (13), Fehlerbehandlung (Timeouts in 8, stille Fallbacks in 5/10, Statusmeldungen in 9), alle Spec-Tests (2–8), Fahrprobe (14).
- **Offene Echtwelt-Naht, bewusst:** Die exakte `MenuEntry`-API wird in Task 11 aus dem UIFramework abgelesen (dort dokumentiert als Pflicht-Leseschritt) — die Alternative, sie hier zu raten, produzierte mit hoher Wahrscheinlichkeit falschen Plan-Code.
- **Typ-Konsistenz:** `IFtpClient` (5) = Implementierung (8); `GetFullImageBytesAsync` (11) wird in 13 als Delegat gereicht; `Screenshot` in `GalleryItem.Tag` und `GameFolder` in `GalleryItemGroup.Tag` werden in 9 gesetzt und in 11–13 gelesen; `_loadCts` aus 9 versorgt 10–12.
- **Reihenfolge:** App-Tasks (9–13) setzen das fertige GalleryControl voraus — erst den UIFramework-Plan ausführen.

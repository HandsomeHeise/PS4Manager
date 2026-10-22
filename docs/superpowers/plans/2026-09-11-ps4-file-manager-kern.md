# PS4 File Manager — Kern (Plan 1 von 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Den UI-freien Kern des PS4 File Managers bauen: `Ps4.Core` aus der Gallery herauslösen, die FTP-Schicht erweitern und `FileManager.Core` mit Zonen, PS4-Wissen, Übertragungswarteschlange, Rückschreiben und virtuellen Dateien für Explorer — vollständig headless getestet.

**Architecture:** `Ps4.Core` (FTP + Titel-Infos) wird von Gallery und File Manager geteilt. `FileManager.Core` enthält alles Testbare des File Managers; jeder Schreibweg läuft durch den `ZoneGuardFtpClient`. Tests laufen gegen einen In-Memory-FTP (`InMemoryFtpClient`) bzw. für den echten `FtpClient` gegen einen Loopback-FTP-Server im Testprojekt.

**Tech Stack:** C# 7.3, .NET Framework 4.8, `FtpWebRequest`, xunit 2.9.2, COM-Interop (`System.Runtime.InteropServices.ComTypes`) — kein NuGet außer den Testpaketen.

**Spec:** `docs/superpowers/specs/2026-09-11-ps4-file-manager-design.md` (Ausführende lesen sie vollständig, bevor sie beginnen).

**Folgepläne:** Plan 2 = `E:\Projects\UIFramework\docs\superpowers\plans\2026-09-11-explorer-bausteine.md` (unabhängig). Plan 3 (`FileManager.App`) wird erst nach Plan 1 und 2 geschrieben.

## Global Constraints

- Zielplattform `net48`, `LangVersion 7.3` (kommt aus `Directory.Build.props`) — **keine** C#-8-Features: kein `using var`, keine Switch-Ausdrücke, keine Nullable-Referenztypen, keine Default-Interface-Methoden, kein `is not`, keine Ranges.
- `Task.IsCompletedSuccessfully` gibt es auf net48 **nicht** — `task.Status == TaskStatus.RanToCompletion` verwenden.
- `Ps4.Core` und `FileManager.Core` setzen `<UseWindowsForms>false</UseWindowsForms>` — kein Verweis auf WinForms im Kern (Spec „Solution-Aufbau").
- Keine NuGet-Pakete in `src/`. Tests nur mit `Microsoft.NET.Test.Sdk` 17.11.1, `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2 (wie das bestehende Testprojekt).
- Testnamen englisch mit Unterstrichen (`A_name_with_spaces_survives`), Kommentare und Doku-Kommentare **deutsch**, alle für Anwender sichtbaren Texte deutsch.
- Keine eigene Wiederholungslogik im FTP-Client (Spec/Gallery-Regel).
- Schreibende Zugriffe auf die echte Konsole (10.10.10.236:2121) **nur** in Task 2, **nur** nach ausdrücklichem OK des Anwenders und **nur** unterhalb eines Wegwerf-Ordners `/data/_fm_probe_<Zeitstempel>`.
- Gearbeitet wird auf dem Branch `file-manager-kern` (von `main` abzweigen), nie direkt auf `main`.
- `docs/superpowers/` ist per `.gitignore` ausgenommen — Spec und Plan werden nie committet.
- Jede Commit-Nachricht endet mit:
  ```
  Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1
  ```
- Befehle laufen im Repo-Wurzelverzeichnis `E:\Projects\PS4\ScreenshotGallery` (Git Bash). Das UIFramework muss daneben liegen (`E:\Projects\UIFramework`), sonst baut die Gallery-App nicht.
- Testlauf gesamt: `dotnet test ScreenshotGallery.sln`. Ausgangsstand vor Task 1: **61/61 grün**.

---

## File Structure

```
src/
  Ps4.Core/                               (Task 1, neu)
    Ps4.Core.csproj
    Properties/AssemblyInfo.cs            InternalsVisibleTo("Ps4.Core.Tests")
    Ftp/FtpClient.cs                      (verschoben, Task 6 erweitert)
    Ftp/FtpEntry.cs                       (verschoben, Task 3 erweitert: Kind, Modified, LinkTarget)
    Ftp/FtpTransfer.cs                    (verschoben, unverändert)
    Ftp/IFtpClient.cs                     (verschoben, Task 6 erweitert)
    Ftp/UnixListingParser.cs              (verschoben, Task 3 erweitert: Datum, Links)
    Ftp/FtpException.cs                   (Task 4) FtpException, FtpErrorKind
    Ftp/FtpErrors.cs                      (Task 4) Klassifizieren, Klartext, Übersetzen
    Titles/SfoReader.cs                   (verschoben)
    Titles/TitlePaths.cs                  (Task 1) SfoPath/IconPath aus Ps4Paths
    Titles/TitleId.cs                     (Task 7)
    Titles/TitleInfo.cs                   (Task 7)
    Titles/TitleInfoLoader.cs             (Task 7)
  ScreenshotGallery.Core/                 (Task 1: nur Verweis + usings; Ps4Paths verliert 2 Methoden)
  ScreenshotGallery.App/                  (Task 1: nur Verweis + using)
  FileManager.Core/                       (Task 8, neu)
    FileManager.Core.csproj
    Properties/AssemblyInfo.cs            InternalsVisibleTo("FileManager.Core.Tests")
    Paths/RemotePath.cs                   (Task 8)
    Paths/NavigationHistory.cs            (Task 8)
    Zones/Zone.cs, ZonePolicy.cs          (Task 9)
    Zones/ZoneViolationException.cs       (Task 9)
    Zones/ZoneGuardFtpClient.cs           (Task 9)
    Browsing/FtpGates.cs                  (Task 10)
    Browsing/DirectoryCache.cs            (Task 10)
    Ps4/TitleCatalog.cs                   (Task 11)
    Ps4/UserNameReader.cs                 (Task 11)
    Ps4/QuickAccessEntry.cs               (Task 11) QuickAccessEntry, QuickAccessKind
    Ps4/QuickAccessBuilder.cs             (Task 11)
    Config/FileManagerSettings.cs         (Task 12)
    Config/FileManagerSettingsStore.cs    (Task 12)
    Transfers/UniqueName.cs               (Task 13)
    Transfers/RemoteItem.cs               (Task 13)
    Transfers/TransferStep.cs             (Task 13) TransferStep, StepKind, StepState
    Transfers/TransferJob.cs              (Task 13) TransferJob, TransferKind, JobState
    Transfers/Conflicts.cs                (Task 13) IConflictResolver, ConflictInfo, ConflictDecision, ConflictChoice
    Transfers/IUserPrompts.cs             (Task 14) IUserPrompts, DeleteSummary
    Transfers/FileOperations.cs           (Task 14)
    Transfers/IClock.cs                   (Task 15) IClock, SystemClock
    Transfers/TransferQueue.cs            (Task 15)
    Editing/IFileChangeSource.cs          (Task 16) + FileSystemChangeSource
    Editing/EditSession.cs                (Task 16)
    Editing/EditSessionManager.cs         (Task 16)
    Shell/ShellFormats.cs                 (Task 17)
    Shell/FileDescriptorWriter.cs         (Task 17)
    Shell/FtpComStream.cs                 (Task 17)
    Shell/FormatEnumerator.cs             (Task 17)
    Shell/IDataObjectAsyncCapability.cs   (Task 17)
    Shell/VirtualFileDataObject.cs        (Task 17)
    Shell/ShellInterop.cs                 (Task 17) DoDragDrop/OleSetClipboard, DropSource
tests/
  Ps4.Core.Tests/                         (Task 1, neu) — FTP-, Parser-, SFO-Tests ziehen hierher um
    LoopbackFtpServer.cs                  (Task 5) Test-FTP-Server auf 127.0.0.1
  ScreenshotGallery.Core.Tests/           (bleibt; FakeFtpClient wächst in Task 6 um Stubs)
  FileManager.Core.Tests/                 (Task 8, neu)
    Support/InMemoryFtpClient.cs          (Task 9)
    Support/TestClock.cs                  (Task 15)
    Support/TempDir.cs                    (Task 13)
```

---

### Task 1: `Ps4.Core` herauslösen — ohne Verhaltensänderung

**Files:**
- Create: `src/Ps4.Core/Ps4.Core.csproj`, `src/Ps4.Core/Properties/AssemblyInfo.cs`, `src/Ps4.Core/Titles/TitlePaths.cs`
- Move: `src/ScreenshotGallery.Core/Ftp/*.cs` → `src/Ps4.Core/Ftp/`; `src/ScreenshotGallery.Core/Ps4/SfoReader.cs` → `src/Ps4.Core/Titles/SfoReader.cs`
- Modify: `src/ScreenshotGallery.Core/Ps4/Ps4Paths.cs`, `src/ScreenshotGallery.Core/Ps4/MediaRepository.cs`, `src/ScreenshotGallery.Core/ScreenshotGallery.Core.csproj`, `src/ScreenshotGallery.App/ScreenshotGallery.App.csproj`, `src/ScreenshotGallery.App/MainForm.cs`, `ScreenshotGallery.sln`
- Create: `tests/Ps4.Core.Tests/Ps4.Core.Tests.csproj`, `tests/Ps4.Core.Tests/TitlePathsTests.cs`, `tests/Ps4.Core.Tests/TestData/param.sfo` (Kopie)
- Move: `tests/ScreenshotGallery.Core.Tests/{FtpClientTests,FtpTransferTests,UnixListingParserTests,SfoReaderTests}.cs` → `tests/Ps4.Core.Tests/`
- Modify: `tests/ScreenshotGallery.Core.Tests/Ps4PathsTests.cs`, `tests/ScreenshotGallery.Core.Tests/MediaRepositoryTests.cs`

**Interfaces:**
- Produces: Namensräume `Ps4.Core.Ftp` (`FtpClient`, `FtpEntry`, `FtpTransfer`, `IFtpClient`, `UnixListingParser`) und `Ps4.Core.Titles` (`SfoReader`, `TitlePaths.SfoPath(string titleId)`, `TitlePaths.IconPath(string titleId)`). Signaturen unverändert gegenüber heute.

- [ ] **Step 1: Branch anlegen**

```bash
git checkout -b file-manager-kern
```

- [ ] **Step 2: Projekt `Ps4.Core` anlegen**

`src/Ps4.Core/Ps4.Core.csproj` (WinForms ausdrücklich aus: `Directory.Build.props` schaltet es für alle Projekte ein, der geteilte Kern soll aber keine UI kennen):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <UseWindowsForms>false</UseWindowsForms>
  </PropertyGroup>
</Project>
```

`src/Ps4.Core/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ps4.Core.Tests")]
```

- [ ] **Step 3: Dateien verschieben und Namensräume umstellen**

```bash
mkdir -p src/Ps4.Core/Ftp src/Ps4.Core/Titles
for f in FtpClient FtpEntry FtpTransfer IFtpClient UnixListingParser; do
  git mv src/ScreenshotGallery.Core/Ftp/$f.cs src/Ps4.Core/Ftp/$f.cs
done
git mv src/ScreenshotGallery.Core/Ps4/SfoReader.cs src/Ps4.Core/Titles/SfoReader.cs
sed -i 's/^namespace ScreenshotGallery\.Core\.Ftp$/namespace Ps4.Core.Ftp/' src/Ps4.Core/Ftp/*.cs
sed -i 's/^namespace ScreenshotGallery\.Core\.Ps4$/namespace Ps4.Core.Titles/' src/Ps4.Core/Titles/SfoReader.cs
rmdir src/ScreenshotGallery.Core/Ftp
```

Prüfen: `grep -rn "namespace" src/Ps4.Core` zeigt nur `Ps4.Core.Ftp` und `Ps4.Core.Titles`.

- [ ] **Step 4: Den Test für `TitlePaths` schreiben (schlägt fehl)**

`tests/Ps4.Core.Tests/Ps4.Core.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\Ps4.Core\Ps4.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="TestData\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`tests/Ps4.Core.Tests/TitlePathsTests.cs` (der Test zieht aus `Ps4PathsTests` hierher um):

```csharp
using Ps4.Core.Titles;
using Xunit;

namespace Ps4.Core.Tests
{
    public class TitlePathsTests
    {
        [Fact]
        public void The_appmeta_paths_are_shared_by_both_kinds()
        {
            Assert.Equal("/system_data/priv/appmeta/CUSA00411/param.sfo", TitlePaths.SfoPath("CUSA00411"));
            Assert.Equal("/user/appmeta/CUSA00411/icon0.png", TitlePaths.IconPath("CUSA00411"));
        }
    }
}
```

In `tests/ScreenshotGallery.Core.Tests/Ps4PathsTests.cs` die Methode `The_appmeta_paths_are_shared_by_both_kinds` (Zeilen 50–55) **löschen**.

- [ ] **Step 5: Tests verschieben**

```bash
mkdir -p tests/Ps4.Core.Tests/TestData
for f in FtpClientTests FtpTransferTests UnixListingParserTests SfoReaderTests; do
  git mv tests/ScreenshotGallery.Core.Tests/$f.cs tests/Ps4.Core.Tests/$f.cs
done
cp tests/ScreenshotGallery.Core.Tests/TestData/param.sfo tests/Ps4.Core.Tests/TestData/param.sfo
sed -i 's/^using ScreenshotGallery\.Core\.Ftp;$/using Ps4.Core.Ftp;/; s/^using ScreenshotGallery\.Core\.Ps4;$/using Ps4.Core.Titles;/; s/^namespace ScreenshotGallery\.Core\.Tests$/namespace Ps4.Core.Tests/' tests/Ps4.Core.Tests/*.cs
```

`param.sfo` wird **kopiert**, nicht verschoben: `MediaRepositoryTests.A_present_sfo_and_icon_are_used` liest ihn weiterhin.

- [ ] **Step 6: In die Solution aufnehmen und Test laufen lassen (rot)**

```bash
dotnet sln ScreenshotGallery.sln add src/Ps4.Core/Ps4.Core.csproj --solution-folder src
dotnet sln ScreenshotGallery.sln add tests/Ps4.Core.Tests/Ps4.Core.Tests.csproj --solution-folder tests
dotnet build tests/Ps4.Core.Tests/Ps4.Core.Tests.csproj
```

Expected: Build-Fehler `CS0103: Der Name "TitlePaths" ist im aktuellen Kontext nicht vorhanden` (bzw. englisch „The name 'TitlePaths' does not exist").

- [ ] **Step 7: `TitlePaths` anlegen, `Ps4Paths` verkleinern**

`src/Ps4.Core/Titles/TitlePaths.cs`:

```csharp
namespace Ps4.Core.Titles
{
    /// <summary>
    /// Wo die Konsole Name und Icon eines Titels ablegt — von beiden Apps
    /// gebraucht, darum nicht mehr in den Medienpfaden der Gallery.
    /// Verifiziert mit der Gallery am 2026-08-01.
    /// </summary>
    public static class TitlePaths
    {
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

In `src/ScreenshotGallery.Core/Ps4/Ps4Paths.cs` die Methoden `SfoPath` und `IconPath` (Zeilen 51–59) **löschen**.

- [ ] **Step 8: Gallery auf `Ps4.Core` umstellen**

`src/ScreenshotGallery.Core/ScreenshotGallery.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Ps4.Core\Ps4.Core.csproj" />
  </ItemGroup>
</Project>
```

In `src/ScreenshotGallery.Core/Ps4/MediaRepository.cs` die Zeile `using ScreenshotGallery.Core.Ftp;` ersetzen durch:

```csharp
using Ps4.Core.Ftp;
using Ps4.Core.Titles;
```

und in `LoadGameAsync` `Ps4Paths.SfoPath(titleId)` → `TitlePaths.SfoPath(titleId)`, `Ps4Paths.IconPath(titleId)` → `TitlePaths.IconPath(titleId)`.

In `src/ScreenshotGallery.App/ScreenshotGallery.App.csproj` in die erste `ItemGroup` zusätzlich:

```xml
    <ProjectReference Include="..\Ps4.Core\Ps4.Core.csproj" />
```

In `src/ScreenshotGallery.App/MainForm.cs` `using ScreenshotGallery.Core.Ftp;` → `using Ps4.Core.Ftp;`.

In `tests/ScreenshotGallery.Core.Tests/MediaRepositoryTests.cs` `using ScreenshotGallery.Core.Ftp;` → `using Ps4.Core.Ftp;`.

- [ ] **Step 9: Alles bauen und testen**

```bash
dotnet build ScreenshotGallery.sln -c Release 2>&1 | tail -3
dotnet test ScreenshotGallery.sln 2>&1 | grep -E "Bestanden|Passed|Fehler|Failed"
```

Expected: Build `0 Warnung(en), 0 Fehler`. Tests: `Ps4.Core.Tests` **18** erfolgreich, `ScreenshotGallery.Core.Tests` **43** erfolgreich — Summe 61, keine Fehler. Weicht die Summe ab, ist ein Test verloren gegangen: nicht weitermachen, sondern suchen.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "Extract the shared FTP and title code into Ps4.Core

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 2: Schreibende Konsolenprüfungen (Gate vor der FTP-Erweiterung)

Diese Aufgabe erzeugt **keinen** Repo-Code. Sie beantwortet mit einem Wegwerf-Programm, das genau wie der spätere `FtpClient` über `FtpWebRequest` auf net48 spricht, die offenen Punkte aus dem Spec-Abschnitt „Vor der FTP-Erweiterung an der Konsole zu prüfen". Die Ergebnisse entscheiden über Task 6 und 15.

**Files:**
- Create (außerhalb des Repos): `%TEMP%\fm-probe\fm-probe.csproj`, `%TEMP%\fm-probe\Program.cs`
- Modify: `docs/superpowers/specs/2026-09-11-ps4-file-manager-design.md` (Befunde eintragen)

**Interfaces:**
- Produces: die Entscheidung `TransferQueue.AppendResumesUploads` (Task 15, Standard `true`) und die Freigabe für `FtpClient.RenameAsync` mit absolutem `RenameTo` (Task 6).

- [ ] **Step 1: Anwender fragen — PFLICHT**

Vor jedem Schreibzugriff den Anwender fragen: „Ich möchte jetzt die schreibenden Prüfungen an der Konsole laufen lassen: nur in einem neuen Ordner `/data/_fm_probe_<Zeitstempel>`, der am Ende wieder gelöscht wird. Ist die Konsole an und darf ich?" Ohne ausdrückliches Ja: diese Aufgabe **nicht** ausführen, Plan anhalten.

- [ ] **Step 2: Wegwerf-Programm anlegen**

`%TEMP%\fm-probe\fm-probe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>7.3</LangVersion>
  </PropertyGroup>
</Project>
```

`%TEMP%\fm-probe\Program.cs`:

```csharp
using System;
using System.IO;
using System.Net;
using System.Text;

internal static class Program
{
    private const string Host = "10.10.10.236";
    private const int Port = 2121;
    private static readonly string Base = "/data/_fm_probe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

    private static FtpWebRequest Req(string path, string method)
    {
        var b = new StringBuilder("ftp://").Append(Host).Append(':').Append(Port);
        foreach (var s in path.Split('/'))
            if (s.Length > 0) b.Append('/').Append(Uri.EscapeDataString(s));
        var r = (FtpWebRequest)WebRequest.Create(new Uri(b.ToString()));
        r.Method = method;
        r.Credentials = new NetworkCredential("anonymous", "anonymous");
        r.UsePassive = true; r.UseBinary = true; r.KeepAlive = true;
        r.Timeout = 10000; r.ReadWriteTimeout = 10000;
        return r;
    }

    private static string Run(string label, Func<string> action)
    {
        try { var result = action(); Console.WriteLine("[OK]   " + label + " -> " + result); return result; }
        catch (WebException ex)
        {
            var resp = ex.Response as FtpWebResponse;
            Console.WriteLine("[FAIL] " + label + " -> " + (resp != null ? (int)resp.StatusCode + " " + resp.StatusDescription.Trim() : ex.Status.ToString()));
            return null;
        }
    }

    private static string Simple(string path, string method)
    {
        using (var resp = (FtpWebResponse)Req(path, method).GetResponse()) return (int)resp.StatusCode + " " + resp.StatusDescription.Trim();
    }

    private static string Upload(string path, string method, string text)
    {
        var r = Req(path, method);
        var data = Encoding.UTF8.GetBytes(text);
        using (var s = r.GetRequestStream()) s.Write(data, 0, data.Length);
        using (var resp = (FtpWebResponse)r.GetResponse()) return (int)resp.StatusCode + " " + resp.StatusDescription.Trim();
    }

    private static string Download(string path, long offset)
    {
        var r = Req(path, WebRequestMethods.Ftp.DownloadFile);
        r.ContentOffset = offset;
        using (var resp = r.GetResponse())
        using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return "'" + reader.ReadToEnd() + "'";
    }

    private static string List(string path)
    {
        using (var resp = Req(path, WebRequestMethods.Ftp.ListDirectoryDetails).GetResponse())
        using (var ms = new MemoryStream())
        {
            resp.GetResponseStream().CopyTo(ms);
            var bytes = ms.ToArray();
            return "\n" + Encoding.UTF8.GetString(bytes) + "\n  HEX(letzte 80 B): " + BitConverter.ToString(bytes, Math.Max(0, bytes.Length - 80));
        }
    }

    private static string Rename(string from, string renameTo)
    {
        var r = Req(from, WebRequestMethods.Ftp.Rename);
        r.RenameTo = renameTo;
        using (var resp = (FtpWebResponse)r.GetResponse()) return (int)resp.StatusCode + " " + resp.StatusDescription.Trim();
    }

    private static string Size(string path)
    {
        using (var resp = (FtpWebResponse)Req(path, WebRequestMethods.Ftp.GetFileSize).GetResponse()) return resp.ContentLength.ToString();
    }

    private static int Main()
    {
        Console.WriteLine("Basis: " + Base);
        Run("01 MKD Basis", () => Simple(Base, WebRequestMethods.Ftp.MakeDirectory));
        Run("02 STOR a.txt 'hello'", () => Upload(Base + "/a.txt", WebRequestMethods.Ftp.UploadFile, "hello"));
        Run("03 RETR a.txt", () => Download(Base + "/a.txt", 0));
        Run("04 APPE a.txt ' world'", () => Upload(Base + "/a.txt", WebRequestMethods.Ftp.AppendFile, " world"));
        Run("05 RETR a.txt (erwartet 'hello world')", () => Download(Base + "/a.txt", 0));
        Run("06 SIZE a.txt (erwartet 11)", () => Size(Base + "/a.txt"));
        Run("07 RETR a.txt ab Offset 6 (erwartet 'world')", () => Download(Base + "/a.txt", 6));
        Run("08 RNFR/RNTO relativ a.txt -> b.txt", () => Rename(Base + "/a.txt", "b.txt"));
        Run("09 MKD sub", () => Simple(Base + "/sub", WebRequestMethods.Ftp.MakeDirectory));
        Run("10 RNFR/RNTO absolut b.txt -> sub/c.txt", () => Rename(Base + "/b.txt", Base + "/sub/c.txt"));
        Run("11 LIST sub (erwartet c.txt)", () => List(Base + "/sub"));
        Run("12 STOR d.txt 'neu'", () => Upload(Base + "/d.txt", WebRequestMethods.Ftp.UploadFile, "neu"));
        Run("13 RNTO auf existierende Datei d.txt -> sub/c.txt", () => Rename(Base + "/d.txt", Base + "/sub/c.txt"));
        Run("14 RETR sub/c.txt (überschrieben? 'neu' vs 'hello world')", () => Download(Base + "/sub/c.txt", 0));
        Run("15 RMD sub (nicht leer, erwartet Fehler)", () => Simple(Base + "/sub", WebRequestMethods.Ftp.RemoveDirectory));
        Run("16 STOR 'Ärger ü.txt'", () => Upload(Base + "/Ärger ü.txt", WebRequestMethods.Ftp.UploadFile, "umlaut"));
        Run("17 LIST Basis (Name korrekt?)", () => List(Base));
        Run("18 RETR 'Ärger ü.txt'", () => Download(Base + "/Ärger ü.txt", 0));
        Run("19 LIST /user/home", () => List("/user/home"));
        Console.Write("Benutzer-ID aus 19 für username.dat eingeben: ");
        var id = Console.ReadLine();
        Run("20 username.dat", () =>
        {
            using (var resp = Req("/user/home/" + id + "/username.dat", WebRequestMethods.Ftp.DownloadFile).GetResponse())
            using (var ms = new MemoryStream())
            {
                resp.GetResponseStream().CopyTo(ms);
                var b = ms.ToArray();
                return "HEX " + BitConverter.ToString(b) + " | UTF8 '" + Encoding.UTF8.GetString(b).Replace("\0", "\\0") + "'";
            }
        });
        Console.WriteLine("--- Aufräumen ---");
        Run("21 DELE Ärger ü.txt", () => Simple(Base + "/Ärger ü.txt", WebRequestMethods.Ftp.DeleteFile));
        Run("22 DELE d.txt (falls noch da)", () => Simple(Base + "/d.txt", WebRequestMethods.Ftp.DeleteFile));
        Run("23 DELE sub/c.txt", () => Simple(Base + "/sub/c.txt", WebRequestMethods.Ftp.DeleteFile));
        Run("24 RMD sub", () => Simple(Base + "/sub", WebRequestMethods.Ftp.RemoveDirectory));
        Run("25 RMD Basis", () => Simple(Base, WebRequestMethods.Ftp.RemoveDirectory));
        Run("26 LIST /data (Basis muss weg sein)", () => List("/data"));
        return 0;
    }
}
```

- [ ] **Step 3: Ausführen**

```bash
cd "$TEMP/fm-probe" && dotnet run 2>&1 | tee probe-output.txt
```

Bei Schritt 20 die Benutzer-ID aus der Ausgabe von 19 eintippen (z. B. `194450ad`). Danach zurück ins Repo: `cd /e/Projects/PS4/ScreenshotGallery`.

- [ ] **Step 4: Gegen die Erwartung prüfen**

| Nr. | Erwartung | Weicht ab → Konsequenz |
|---|---|---|
| 02–03 | OK, `'hello'` | **STOPP**, Anwender fragen (ohne STOR kein File Manager) |
| 04–05 | `'hello world'` | Task 15: `AppendResumesUploads` bekommt Standard `false`; Uploads beginnen nach Abbruch neu (Spec erlaubt das) |
| 06 | `11` | **STOPP** (Fortsetzen von Uploads und Stale-Check hängen daran) |
| 07 | `'world'` | **STOPP** (Download-Fortsetzen hängt daran) |
| 08, 10–11 | OK, `c.txt` in `sub` | **STOPP** — Verschieben/Umbenennen ist so nicht baubar; Plan mit dem Anwender neu fassen |
| 13–14 | beides ist zulässig — Ergebnis notieren | Überschreibt `RNTO`: `InMemoryFtpClient.RenameAsync` (Task 9) muss dann ebenfalls überschreiben. Die Konfliktprüfung vor dem Umbenennen gibt es in beiden Fällen |
| 15 | FAIL (5xx) | Notieren. OK hieße: `RMD` löscht rekursiv → Löschplan (Task 14) bleibt trotzdem bottom-up |
| 16–18 | Name in LIST identisch, Inhalt `'umlaut'` | **STOPP** — Kodierungsfrage mit dem Anwender klären |
| 20 | Name + `\0` (17 Byte) | Weicht das Format ab: `UserNameReader` (Task 11) entsprechend anpassen; im Zweifel Rückfall auf die ID |
| 25–26 | Basis weg | Rest per Hand aufräumen (nur den Probe-Ordner!) |

- [ ] **Step 5: Befunde in die Spec schreiben**

Im Spec-Abschnitt „Vor der FTP-Erweiterung an der Konsole zu prüfen" unter der Liste einen Unterabschnitt `### Befunde (2026-09-11)` mit einer Zeile pro Punkt 1–6 ergänzen (Ergebnis wörtlich aus `probe-output.txt`, plus Konsequenz aus der Tabelle). Kein Commit (Spec ist ignoriert).

- [ ] **Step 6: Bei einem STOPP-Fall**

Anhalten, dem Anwender Befund und Tabelle zeigen, Plan nicht fortsetzen, bis entschieden ist. Sonst weiter mit Task 3.

---

### Task 3: `FtpEntry` mit Sorte und Datum, Parser liest Datum und Links

**Files:**
- Modify: `src/Ps4.Core/Ftp/FtpEntry.cs`, `src/Ps4.Core/Ftp/UnixListingParser.cs`
- Test: `tests/Ps4.Core.Tests/UnixListingParserTests.cs`

**Interfaces:**
- Consumes: Task 1 (`Ps4.Core.Ftp`).
- Produces:
  - `enum FtpEntryKind { File, Directory, Link }`
  - `FtpEntry(string name, bool isDirectory, long size)` (bleibt), neu `FtpEntry(string name, FtpEntryKind kind, long size, DateTime modified, string linkTarget)`; Properties `Name`, `Kind`, `IsDirectory`, `Size`, `Modified` (`DateTime.MinValue` = unbekannt), `LinkTarget` (nur bei `Link`, sonst null).
  - `UnixListingParser.Parse(string listing)` (bleibt, nutzt `DateTime.Today`), neu `UnixListingParser.Parse(string listing, DateTime today)`.

- [ ] **Step 1: Failing tests schreiben**

An `tests/Ps4.Core.Tests/UnixListingParserTests.cs` innerhalb der Klasse anhängen (und oben `using System;` ergänzen):

```csharp
        private static readonly DateTime Today = new DateTime(2026, 9, 11);

        [Fact]
        public void The_date_of_a_real_goldhen_line_is_read()
        {
            // Wörtlich von der Konsole (LIST /, 2026-09-11) — GoldHEN liefert nur das Datum.
            var entry = UnixListingParser.Parse("drwxrwxrwx 24 0 0 1024 Sep  9 2026 data\r\n", Today).Single();
            Assert.Equal(FtpEntryKind.Directory, entry.Kind);
            Assert.Equal(new DateTime(2026, 9, 9), entry.Modified);
        }

        [Fact]
        public void Sizes_above_4_gb_survive()
        {
            // Wörtlich aus /data/pkg.
            var entry = UnixListingParser.Parse(
                "-rwxrwxrwx 1 0 0 11873615872 Sep 11 2026 Call.of.Duty.Black.Ops.6.CUSA23827.MWIII.Shared.Content.Pack.3-CyB1K.pkg\r\n",
                Today).Single();
            Assert.Equal(11873615872L, entry.Size);
            Assert.Equal(FtpEntryKind.File, entry.Kind);
        }

        [Fact]
        public void A_time_instead_of_a_year_means_this_year()
        {
            var entry = UnixListingParser.Parse("-rw-r--r-- 1 0 0 10 Aug 10 12:34 x.txt\n", Today).Single();
            Assert.Equal(new DateTime(2026, 8, 10, 12, 34, 0), entry.Modified);
        }

        [Fact]
        public void A_time_that_would_lie_in_the_future_belongs_to_last_year()
        {
            var entry = UnixListingParser.Parse("-rw-r--r-- 1 0 0 10 Dec 30 23:00 x.txt\n",
                new DateTime(2026, 1, 5)).Single();
            Assert.Equal(new DateTime(2025, 12, 30, 23, 0, 0), entry.Modified);
        }

        [Fact]
        public void A_link_keeps_its_name_and_target()
        {
            var entry = UnixListingParser.Parse("lrwxrwxrwx 1 0 0 9 Sep  1 2026 usb -> /mnt/usb0\n", Today).Single();
            Assert.Equal(FtpEntryKind.Link, entry.Kind);
            Assert.Equal("usb", entry.Name);
            Assert.Equal("/mnt/usb0", entry.LinkTarget);
            Assert.False(entry.IsDirectory);
        }

        [Fact]
        public void An_unreadable_date_keeps_the_entry_with_min_value()
        {
            var entry = UnixListingParser.Parse("-rw-r--r-- 1 0 0 10 Foo  1 2026 x.txt\n", Today).Single();
            Assert.Equal(DateTime.MinValue, entry.Modified);
            Assert.Equal("x.txt", entry.Name);
        }

        [Fact]
        public void Device_files_are_skipped()
        {
            Assert.Empty(UnixListingParser.Parse("crw-rw-rw- 1 0 0 0 Sep 11 2026 null\n", Today));
        }

        [Fact]
        public void The_old_constructor_still_means_file_or_directory()
        {
            var dir = new FtpEntry("x", true, 1);
            Assert.Equal(FtpEntryKind.Directory, dir.Kind);
            Assert.Equal(DateTime.MinValue, dir.Modified);
            Assert.Null(dir.LinkTarget);
            Assert.Equal(FtpEntryKind.File, new FtpEntry("y", false, 1).Kind);
        }
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~UnixListingParserTests`
Expected: Build-Fehler — `FtpEntryKind` und `Parse(string, DateTime)` existieren nicht.

- [ ] **Step 3: `FtpEntry` erweitern**

`src/Ps4.Core/Ftp/FtpEntry.cs` vollständig:

```csharp
using System;

namespace Ps4.Core.Ftp
{
    public enum FtpEntryKind { File, Directory, Link }

    /// <summary>
    /// Ein Eintrag aus einem LIST-Ergebnis. Modified trägt bei GoldHEN nur das
    /// Datum (die Konsole liefert keine Uhrzeit, Spec „LIST-Format");
    /// DateTime.MinValue heißt „nicht lesbar".
    /// </summary>
    public sealed class FtpEntry
    {
        public FtpEntry(string name, bool isDirectory, long size)
            : this(name, isDirectory ? FtpEntryKind.Directory : FtpEntryKind.File, size, DateTime.MinValue, null)
        {
        }

        public FtpEntry(string name, FtpEntryKind kind, long size, DateTime modified, string linkTarget)
        {
            Name = name;
            Kind = kind;
            Size = size;
            Modified = modified;
            LinkTarget = kind == FtpEntryKind.Link ? linkTarget : null;
        }

        public string Name { get; }
        public FtpEntryKind Kind { get; }
        public bool IsDirectory { get { return Kind == FtpEntryKind.Directory; } }
        public long Size { get; }
        public DateTime Modified { get; }
        public string LinkTarget { get; }
    }
}
```

- [ ] **Step 4: Parser erweitern**

`src/Ps4.Core/Ftp/UnixListingParser.cs` vollständig:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Parst GoldHENs Unix-artiges LIST-Format:
    ///     drwxr-xr-x 2 0 0 512 Aug  1 2026 CUSA00411
    ///     -rwxrwx--- 1 0 0 592 Aug  1 2026 name mit leerzeichen.dat
    ///     lrwxrwxrwx 1 0 0 9 Sep  1 2026 usb -> /mnt/usb0
    /// Feldaufbau: Rechte, Links, UID, GID, Größe, Monat, Tag, Jahr/Zeit, Name.
    /// Der Name ist ab Feld 9 ALLES (Leerzeichen inklusive) — deshalb Split
    /// mit Obergrenze 9 statt naivem Zerhacken.
    ///
    /// Datum: GoldHEN schreibt immer „Mon TT JJJJ". Die ls-übliche Form
    /// „Mon TT HH:MM" (ohne Jahr) wird trotzdem verstanden: laufendes Jahr,
    /// oder Vorjahr, wenn das Datum sonst in der Zukunft läge.
    /// </summary>
    public static class UnixListingParser
    {
        private static readonly string[] Months =
            { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        public static IReadOnlyList<FtpEntry> Parse(string listing)
        {
            return Parse(listing, DateTime.Today);
        }

        public static IReadOnlyList<FtpEntry> Parse(string listing, DateTime today)
        {
            var entries = new List<FtpEntry>();
            if (string.IsNullOrEmpty(listing)) return entries;

            foreach (var raw in listing.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = raw.Split(new[] { ' ' }, 9, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 9) continue;                       // "total 12", Müll
                if (fields[0].Length < 10) continue;                   // kein Rechte-Feld

                FtpEntryKind kind;
                switch (fields[0][0])
                {
                    case 'd': kind = FtpEntryKind.Directory; break;
                    case '-': kind = FtpEntryKind.File; break;
                    case 'l': kind = FtpEntryKind.Link; break;
                    default: continue;                                 // Geräte, Pipes, Sockets
                }

                long size;
                if (!long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out size)) continue;

                string name = fields[8];
                string target = null;
                if (kind == FtpEntryKind.Link)
                {
                    int arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrow >= 0)
                    {
                        target = name.Substring(arrow + 4);
                        name = name.Substring(0, arrow);
                    }
                }
                if (name == "." || name == "..") continue;

                entries.Add(new FtpEntry(name, kind, size, ParseDate(fields[5], fields[6], fields[7], today), target));
            }

            return entries;
        }

        internal static DateTime ParseDate(string month, string day, string yearOrTime, DateTime today)
        {
            int m = Array.FindIndex(Months, x => string.Equals(x, month, StringComparison.OrdinalIgnoreCase)) + 1;
            if (m == 0) return DateTime.MinValue;

            int d;
            if (!int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out d)) return DateTime.MinValue;

            try
            {
                int colon = yearOrTime.IndexOf(':');
                if (colon < 0)
                {
                    int year;
                    if (!int.TryParse(yearOrTime, NumberStyles.None, CultureInfo.InvariantCulture, out year))
                        return DateTime.MinValue;
                    return new DateTime(year, m, d);
                }

                int hour, minute;
                if (!int.TryParse(yearOrTime.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out hour) ||
                    !int.TryParse(yearOrTime.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out minute))
                    return DateTime.MinValue;

                var candidate = new DateTime(today.Year, m, d, hour, minute, 0);
                if (candidate > today.Date.AddDays(1)) candidate = candidate.AddYears(-1);
                return candidate;
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;                              // 31. Februar o. ä.
            }
        }
    }
}
```

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~UnixListingParserTests`
Expected: 14 bestanden (6 alte + 8 neue).

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün (69).

- [ ] **Step 6: Commit**

```bash
git add src/Ps4.Core/Ftp tests/Ps4.Core.Tests/UnixListingParserTests.cs
git commit -m "Read dates and symlinks from GoldHEN listings

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 4: `FtpException` und Fehlerübersetzung

**Files:**
- Create: `src/Ps4.Core/Ftp/FtpException.cs`, `src/Ps4.Core/Ftp/FtpErrors.cs`
- Test: `tests/Ps4.Core.Tests/FtpErrorsTests.cs`

**Interfaces:**
- Produces:
  - `enum FtpErrorKind { Transient, Permanent }`
  - `FtpException(string message, FtpErrorKind kind, int statusCode)` und `(…, Exception inner)`; Properties `Kind`, `StatusCode` (0 = keine FTP-Antwort), `IsTransient`.
  - `FtpErrors.Classify(WebExceptionStatus status, int ftpStatusCode) : FtpErrorKind`
  - `FtpErrors.Describe(int statusCode, string serverText) : string`
  - `FtpErrors.Translate(Exception ex, CancellationToken ct) : Exception` — liefert die Ausnahme, die über dem Draht geworfen wird (`OperationCanceledException` bei Anwender-Abbruch, `FtpException` für Netz/Server, lokale Dateifehler unverändert).

- [ ] **Step 1: Failing tests schreiben**

`tests/Ps4.Core.Tests/FtpErrorsTests.cs`:

```csharp
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    public class FtpErrorsTests
    {
        [Theory]
        [InlineData(550, FtpErrorKind.Permanent)]
        [InlineData(553, FtpErrorKind.Permanent)]
        [InlineData(451, FtpErrorKind.Permanent)]
        [InlineData(452, FtpErrorKind.Permanent)]
        [InlineData(502, FtpErrorKind.Permanent)]
        [InlineData(421, FtpErrorKind.Transient)]
        [InlineData(425, FtpErrorKind.Transient)]
        [InlineData(426, FtpErrorKind.Transient)]
        public void Ftp_codes_are_classified(int code, FtpErrorKind expected)
        {
            Assert.Equal(expected, FtpErrors.Classify(WebExceptionStatus.ProtocolError, code));
        }

        [Theory]
        [InlineData(WebExceptionStatus.ConnectFailure)]
        [InlineData(WebExceptionStatus.Timeout)]
        [InlineData(WebExceptionStatus.RequestCanceled)]
        [InlineData(WebExceptionStatus.ReceiveFailure)]
        [InlineData(WebExceptionStatus.ConnectionClosed)]
        public void Transport_failures_without_an_ftp_answer_are_transient(WebExceptionStatus status)
        {
            Assert.Equal(FtpErrorKind.Transient, FtpErrors.Classify(status, 0));
        }

        [Fact]
        public void Known_codes_get_plain_german_text()
        {
            Assert.Equal("Nicht vorhanden oder kein Zugriff", FtpErrors.Describe(550, "550 Requested action not taken."));
            Assert.Equal("Name nicht erlaubt", FtpErrors.Describe(553, "x"));
            Assert.Equal("Fehler auf der Konsole", FtpErrors.Describe(451, "x"));
            Assert.Equal("Kein Platz mehr auf der Konsole", FtpErrors.Describe(452, "x"));
        }

        [Fact]
        public void Unknown_codes_keep_code_and_server_text()
        {
            Assert.Equal("FTP-Fehler 502: 502 Command not implemented.",
                FtpErrors.Describe(502, "502 Command not implemented.\r\n"));
        }

        [Fact]
        public void User_cancellation_wins_over_everything()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var result = FtpErrors.Translate(new WebException("abgebrochen", WebExceptionStatus.RequestCanceled), cts.Token);
                Assert.IsAssignableFrom<OperationCanceledException>(result);
            }
        }

        [Fact]
        public void An_aborted_request_without_user_cancellation_is_a_transient_timeout()
        {
            // Unsere Frist bricht die Anfrage per request.Abort() ab — das ist
            // eine Zeitüberschreitung, kein Abbruch durch den Anwender.
            var result = FtpErrors.Translate(new WebException("x", WebExceptionStatus.RequestCanceled), CancellationToken.None);
            var ftp = Assert.IsType<FtpException>(result);
            Assert.Equal(FtpErrorKind.Transient, ftp.Kind);
            Assert.Equal(0, ftp.StatusCode);
            Assert.Contains("Zeitüberschreitung", ftp.Message);
        }

        [Fact]
        public void A_refused_connection_says_so()
        {
            var ftp = Assert.IsType<FtpException>(FtpErrors.Translate(
                new WebException("x", WebExceptionStatus.ConnectFailure), CancellationToken.None));
            Assert.True(ftp.IsTransient);
            Assert.Contains("GoldHEN", ftp.Message);
        }

        [Fact]
        public void The_idle_timeout_of_a_copy_is_transient()
        {
            var ftp = Assert.IsType<FtpException>(FtpErrors.Translate(new OperationCanceledException(), CancellationToken.None));
            Assert.True(ftp.IsTransient);
        }

        [Fact]
        public void Socket_and_io_failures_are_transient_and_keep_the_cause()
        {
            var socket = new SocketException(10054);
            var ftp = Assert.IsType<FtpException>(FtpErrors.Translate(socket, CancellationToken.None));
            Assert.True(ftp.IsTransient);
            Assert.Same(socket, ftp.InnerException);

            Assert.True(Assert.IsType<FtpException>(FtpErrors.Translate(new IOException("weg"), CancellationToken.None)).IsTransient);
        }

        [Fact]
        public void Local_file_problems_pass_through_unchanged()
        {
            var missing = new FileNotFoundException("lokal");
            Assert.Same(missing, FtpErrors.Translate(missing, CancellationToken.None));
            var denied = new UnauthorizedAccessException("lokal");
            Assert.Same(denied, FtpErrors.Translate(denied, CancellationToken.None));
        }

        [Fact]
        public void An_ftp_exception_passes_through_and_foreign_exceptions_stay_untouched()
        {
            var own = new FtpException("x", FtpErrorKind.Permanent, 550);
            Assert.Same(own, FtpErrors.Translate(own, CancellationToken.None));
            var foreign = new InvalidOperationException("x");
            Assert.Same(foreign, FtpErrors.Translate(foreign, CancellationToken.None));
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~FtpErrorsTests`
Expected: Build-Fehler — `FtpErrors`, `FtpException`, `FtpErrorKind` fehlen.

- [ ] **Step 3: Implementieren**

`src/Ps4.Core/Ftp/FtpException.cs`:

```csharp
using System;

namespace Ps4.Core.Ftp
{
    public enum FtpErrorKind
    {
        /// <summary>Verbindung/Zeit — ein erneuter Versuch kann gelingen.</summary>
        Transient,
        /// <summary>Der Server hat die Aktion abgelehnt — ein erneuter Versuch ändert nichts.</summary>
        Permanent
    }

    /// <summary>
    /// Das Einzige, was oberhalb von FtpClient an Netz-/Serverfehlern ankommt.
    /// Die Nachricht ist deutscher Klartext für den Anwender; die ursprüngliche
    /// Ausnahme bleibt als InnerException erhalten (die Gallery zeigt weiterhin
    /// die innerste Ursache).
    /// </summary>
    public sealed class FtpException : Exception
    {
        public FtpException(string message, FtpErrorKind kind, int statusCode)
            : this(message, kind, statusCode, null)
        {
        }

        public FtpException(string message, FtpErrorKind kind, int statusCode, Exception inner)
            : base(message, inner)
        {
            Kind = kind;
            StatusCode = statusCode;
        }

        public FtpErrorKind Kind { get; }

        /// <summary>FTP-Antwortcode; 0, wenn gar keine FTP-Antwort kam (Transportfehler).</summary>
        public int StatusCode { get; }

        public bool IsTransient { get { return Kind == FtpErrorKind.Transient; } }
    }
}
```

`src/Ps4.Core/Ftp/FtpErrors.cs`:

```csharp
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Übersetzt, was FtpWebRequest wirft, in die eine Unterscheidung, die
    /// darüber gebraucht wird: transient (Verbindung — Auftrag unterbrechen,
    /// später fortsetzen) oder permanent (Server sagt nein — Datei als
    /// fehlgeschlagen notieren, weitermachen). Spec „Fehlerbehandlung".
    /// </summary>
    public static class FtpErrors
    {
        public static FtpErrorKind Classify(WebExceptionStatus status, int ftpStatusCode)
        {
            if (ftpStatusCode > 0)
            {
                // 421 Dienst schließt, 425/426 Datenverbindung kaputt — das
                // sind Verbindungs-, keine Sachfehler.
                return ftpStatusCode == 421 || ftpStatusCode == 425 || ftpStatusCode == 426
                    ? FtpErrorKind.Transient
                    : FtpErrorKind.Permanent;
            }
            return FtpErrorKind.Transient;
        }

        public static string Describe(int statusCode, string serverText)
        {
            switch (statusCode)
            {
                case 550: return "Nicht vorhanden oder kein Zugriff";
                case 553: return "Name nicht erlaubt";
                case 451: return "Fehler auf der Konsole";
                case 452: return "Kein Platz mehr auf der Konsole";
                case 421:
                case 425:
                case 426: return "Verbindung zur PS4 unterbrochen";
            }
            var text = string.IsNullOrWhiteSpace(serverText) ? "" : ": " + serverText.Trim();
            return "FTP-Fehler " + statusCode + text;
        }

        public static Exception Translate(Exception ex, CancellationToken ct)
        {
            // Hat der Anwender abgebrochen, ist jede Folgeausnahme (meist eine
            // WebException aus request.Abort()) nur ein Symptom davon.
            if (ct.IsCancellationRequested) return new OperationCanceledException(ct);

            if (ex is FtpException) return ex;
            if (IsLocal(ex)) return ex;

            var web = ex as WebException;
            if (web != null)
            {
                var response = web.Response as FtpWebResponse;
                int code = response != null ? (int)response.StatusCode : 0;
                string message = code > 0
                    ? Describe(code, response.StatusDescription)
                    : DescribeTransport(web.Status);
                return new FtpException(message, Classify(web.Status, code), code, ex);
            }

            // Unsere Untätigkeits-Frist (FtpTransfer) — nicht der Anwender.
            if (ex is OperationCanceledException)
                return new FtpException("Zeitüberschreitung — die PS4 antwortet nicht", FtpErrorKind.Transient, 0, ex);

            if (ex is IOException || ex is SocketException || ex is TimeoutException)
                return new FtpException("Verbindung zur PS4 unterbrochen", FtpErrorKind.Transient, 0, ex);

            return ex;
        }

        /// <summary>
        /// Lokale Dateifehler (Quelle weg, Ziel gesperrt) sind keine
        /// Verbindungsprobleme — sie dürfen keinen Auftrag „unterbrechen".
        /// FileNotFound/DirectoryNotFound/PathTooLong erben von IOException,
        /// darum VOR dem IOException-Zweig prüfen.
        /// </summary>
        private static bool IsLocal(Exception ex)
        {
            return ex is FileNotFoundException
                || ex is DirectoryNotFoundException
                || ex is PathTooLongException
                || ex is UnauthorizedAccessException;
        }

        internal static string DescribeTransport(WebExceptionStatus status)
        {
            switch (status)
            {
                case WebExceptionStatus.Timeout:
                case WebExceptionStatus.RequestCanceled:
                    return "Zeitüberschreitung — die PS4 antwortet nicht";
                case WebExceptionStatus.ConnectFailure:
                    return "Keine Verbindung zur PS4 — läuft der FTP-Server von GoldHEN?";
                case WebExceptionStatus.NameResolutionFailure:
                    return "Adresse der PS4 nicht auflösbar";
                default:
                    return "Verbindung zur PS4 unterbrochen (" + status + ")";
            }
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~FtpErrorsTests`
Expected: alle bestanden (13 Theorie-Fälle + 9 Facts = 22 Testfälle).

- [ ] **Step 5: Commit**

```bash
git add src/Ps4.Core/Ftp/FtpException.cs src/Ps4.Core/Ftp/FtpErrors.cs tests/Ps4.Core.Tests/FtpErrorsTests.cs
git commit -m "Classify FTP failures as transient or permanent with plain German text

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 5: Loopback-FTP-Server für die Tests des echten `FtpClient`

Bisher hatte `FtpClient` nur Tests für den URI-Bau — der Draht selbst war ungetestet. Dieser Server im Testprojekt spricht FTP auf 127.0.0.1 und bildet GoldHENs erhobenes Verhalten nach. Diese Aufgabe liefert den Server plus Rauchtests des **bestehenden** Verhaltens (die sind sofort grün — hier wird Testinfrastruktur gebaut, keine Produktlogik).

**Files:**
- Create: `tests/Ps4.Core.Tests/LoopbackFtpServer.cs`, `tests/Ps4.Core.Tests/LoopbackFtpServerTests.cs`

**Interfaces:**
- Produces (nur Testprojekt): `internal sealed class LoopbackFtpServer : IDisposable` mit `Host` (`"127.0.0.1"`), `Port`, `AddDirectory(string)`, `AddFile(string, byte[])`, `AddFileWithRawName(string directory, byte[] nameBytes, byte[] data)`, `GetFile(string) : byte[]` (null = fehlt), `DirectoryExists(string) : bool`, `CommandsSnapshot() : string[]`, `CutDataAfterBytes` (int, 0 = nie; kappt RETR/STOR/APPE nach so vielen Bytes hart und antwortet 426).
- **Befund aus Task 2 (Controller-Entscheidung):** GoldHEN ist bytetransparent — Namen liegen als genau die Bytes auf der Konsole, die der Client schickt. `FtpWebRequest` schickt nach GoldHENs `504` auf `OPTS utf8 on` die Pfade in `Encoding.Default` (hier Windows-1252). Der Server bildet das nach: Steuerzeilen und Listings laufen über **Latin-1** (jedes Byte = ein Zeichen, also verlustfrei), nicht UTF-8. Außerdem überschreibt GoldHENs `RNTO` eine existierende Zieldatei kommentarlos — der Server tut das auch.

- [ ] **Step 1: Server schreiben**

`tests/Ps4.Core.Tests/LoopbackFtpServer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Ps4.Core.Tests
{
    /// <summary>
    /// Minimaler FTP-Server auf 127.0.0.1 mit Dateisystem im Speicher, für die
    /// Tests des echten FtpClient (FtpWebRequest). Verhält sich, soweit an der
    /// Konsole erhoben (Spec „Verifizierte Fakten" und Befunde Task 2), wie
    /// GoldHEN v2.2: USER genügt (230), PASS ist überflüssig (202), kein
    /// UTF-8-Modus (OPTS → 504), LIST mit „.“/„..“ und Datum ohne Uhrzeit, RMD
    /// nur auf leere Ordner, RNTO überschreibt eine existierende Zieldatei.
    ///
    /// Bytetransparent wie GoldHEN: Steuerzeilen und Listings laufen über
    /// Latin-1 (jedes Byte = ein Zeichen). Ein Name ist hier also genau die
    /// Bytefolge, die FtpWebRequest geschickt hat.
    ///
    /// Jede Steuerzeile landet in den Commands — so prüfen die Tests, was
    /// FtpWebRequest wirklich auf den Draht schreibt.
    /// </summary>
    internal sealed class LoopbackFtpServer : IDisposable
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private readonly TcpListener _listener;
        private readonly object _lock = new object();
        private readonly HashSet<string> _dirs = new HashSet<string>(StringComparer.Ordinal) { "/" };
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly List<string> _commands = new List<string>();
        private volatile bool _stopped;

        public LoopbackFtpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(AcceptLoopAsync);
        }

        public string Host { get { return "127.0.0.1"; } }
        public int Port { get; }
        public int CutDataAfterBytes { get; set; }

        public void AddDirectory(string path)
        {
            lock (_lock) AddDirectoryUnlocked(Normalize(path));
        }

        public void AddFile(string path, byte[] data)
        {
            lock (_lock)
            {
                var p = Normalize(path);
                AddDirectoryUnlocked(Parent(p));
                _files[p] = data;
            }
        }

        /// <summary>Eine Datei, deren Name als rohe Bytes vorliegt — z. B. UTF-8 von einem anderen FTP-Client.</summary>
        public void AddFileWithRawName(string directory, byte[] nameBytes, byte[] data)
        {
            AddFile(Normalize(directory) + "/" + Latin1.GetString(nameBytes), data);
        }

        public byte[] GetFile(string path)
        {
            lock (_lock)
            {
                byte[] data;
                return _files.TryGetValue(Normalize(path), out data) ? data : null;
            }
        }

        public bool DirectoryExists(string path)
        {
            lock (_lock) return _dirs.Contains(Normalize(path));
        }

        public string[] CommandsSnapshot()
        {
            lock (_lock) return _commands.ToArray();
        }

        public void Dispose()
        {
            _stopped = true;
            _listener.Stop();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stopped)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) { return; }                       // Listener gestoppt
                var ignored = Task.Run(() => SessionAsync(client));
            }
        }

        private async Task SessionAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Latin1);
                var control = new StreamWriter(stream, Latin1) { NewLine = "\r\n", AutoFlush = true };
                string cwd = "/";
                string renameFrom = null;
                long restOffset = 0;
                TcpListener pasv = null;

                try
                {
                    await control.WriteLineAsync("220 Loopback FTP (GoldHEN-artig)").ConfigureAwait(false);
                    while (!_stopped)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) return;
                        lock (_lock) _commands.Add(line);

                        int space = line.IndexOf(' ');
                        var verb = (space < 0 ? line : line.Substring(0, space)).ToUpperInvariant();
                        var arg = space < 0 ? "" : line.Substring(space + 1);
                        var target = Resolve(cwd, arg);

                        switch (verb)
                        {
                            case "USER": await control.WriteLineAsync("230 User logged in, proceed.").ConfigureAwait(false); break;
                            case "PASS": await control.WriteLineAsync("202 Command not implemented, superfluous at this site.").ConfigureAwait(false); break;
                            case "OPTS": await control.WriteLineAsync("504 Command not implemented for that parameter.").ConfigureAwait(false); break;
                            case "TYPE":
                            case "NOOP": await control.WriteLineAsync("200 Command okay.").ConfigureAwait(false); break;
                            case "PWD": await control.WriteLineAsync("257 \"" + cwd + "\" is the current directory.").ConfigureAwait(false); break;
                            case "CWD":
                                if (DirectoryExists(target)) { cwd = target; await control.WriteLineAsync("250 Requested file action okay, completed.").ConfigureAwait(false); }
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            case "PASV":
                                if (pasv != null) pasv.Stop();
                                pasv = new TcpListener(IPAddress.Loopback, 0);
                                pasv.Start();
                                int p = ((IPEndPoint)pasv.LocalEndpoint).Port;
                                await control.WriteLineAsync("227 Entering Passive Mode (127,0,0,1," + (p / 256) + "," + (p % 256) + ").").ConfigureAwait(false);
                                break;
                            case "REST":
                                restOffset = long.Parse(arg, CultureInfo.InvariantCulture);
                                await control.WriteLineAsync("350 Requested file action pending further information.").ConfigureAwait(false);
                                break;
                            case "LIST":
                            {
                                var path = arg.Length == 0 ? cwd : target;
                                var listing = BuildListing(path);
                                if (listing == null) { await Fail(control).ConfigureAwait(false); break; }
                                await SendAsync(control, pasv, Latin1.GetBytes(listing), 0, false).ConfigureAwait(false);
                                pasv = null;
                                break;
                            }
                            case "RETR":
                            {
                                var data = GetFile(target);
                                if (data == null) { await Fail(control).ConfigureAwait(false); break; }
                                await SendAsync(control, pasv, data, restOffset, true).ConfigureAwait(false);
                                restOffset = 0;
                                pasv = null;
                                break;
                            }
                            case "STOR":
                            case "APPE":
                            {
                                bool parentOk;
                                lock (_lock) parentOk = _dirs.Contains(Parent(target)) && !_dirs.Contains(target);
                                if (!parentOk) { await Fail(control).ConfigureAwait(false); break; }
                                await ReceiveAsync(control, pasv, target, verb == "APPE").ConfigureAwait(false);
                                pasv = null;
                                break;
                            }
                            case "SIZE":
                            {
                                var data = GetFile(target);
                                if (data == null) await Fail(control).ConfigureAwait(false);
                                else await control.WriteLineAsync("213 " + data.Length).ConfigureAwait(false);
                                break;
                            }
                            case "MDTM":
                                if (GetFile(target) == null) await Fail(control).ConfigureAwait(false);
                                else await control.WriteLineAsync("213 20260911000000").ConfigureAwait(false);
                                break;
                            case "MKD":
                            {
                                bool ok;
                                lock (_lock)
                                {
                                    ok = _dirs.Contains(Parent(target)) && !_dirs.Contains(target) && !_files.ContainsKey(target);
                                    if (ok) _dirs.Add(target);
                                }
                                if (ok) await control.WriteLineAsync("257 \"" + target + "\" created.").ConfigureAwait(false);
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "RMD":
                            {
                                bool ok;
                                lock (_lock)
                                {
                                    ok = target != "/" && _dirs.Contains(target) && !HasChildrenUnlocked(target);
                                    if (ok) _dirs.Remove(target);
                                }
                                if (ok) await control.WriteLineAsync("250 Requested file action okay, completed.").ConfigureAwait(false);
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "DELE":
                            {
                                bool ok;
                                lock (_lock) ok = _files.Remove(target);
                                if (ok) await control.WriteLineAsync("250 Requested file action okay, completed.").ConfigureAwait(false);
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "RNFR":
                            {
                                bool exists;
                                lock (_lock) exists = _files.ContainsKey(target) || (_dirs.Contains(target) && target != "/");
                                if (exists) { renameFrom = target; await control.WriteLineAsync("350 Requested file action pending further information.").ConfigureAwait(false); }
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "RNTO":
                            {
                                bool ok = renameFrom != null && MoveUnlockedSafe(renameFrom, target);
                                renameFrom = null;
                                if (ok) await control.WriteLineAsync("250 Requested file action okay, completed.").ConfigureAwait(false);
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "QUIT":
                                await control.WriteLineAsync("221 Service closing control connection.").ConfigureAwait(false);
                                return;
                            default:
                                await control.WriteLineAsync("502 Command not implemented.").ConfigureAwait(false);
                                break;
                        }
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
                finally
                {
                    if (pasv != null) pasv.Stop();
                }
            }
        }

        private static Task Fail(StreamWriter control)
        {
            return control.WriteLineAsync("550 Requested action not taken. File unavailable.");
        }

        private async Task SendAsync(StreamWriter control, TcpListener pasv, byte[] data, long offset, bool mayCut)
        {
            if (pasv == null) { await control.WriteLineAsync("425 Use PASV first.").ConfigureAwait(false); return; }
            await control.WriteLineAsync("150 File status okay; about to open data connection.").ConfigureAwait(false);
            bool cut = false;
            using (var dataClient = await pasv.AcceptTcpClientAsync().ConfigureAwait(false))
            {
                pasv.Stop();
                var s = dataClient.GetStream();
                int start = (int)Math.Min(offset, data.Length);
                int count = data.Length - start;
                int limit = CutDataAfterBytes;
                if (mayCut && limit > 0 && limit < count)
                {
                    await s.WriteAsync(data, start, limit).ConfigureAwait(false);
                    dataClient.Client.LingerState = new LingerOption(true, 0);  // hart kappen (RST)
                    cut = true;
                }
                else
                {
                    await s.WriteAsync(data, start, count).ConfigureAwait(false);
                }
            }
            await control.WriteLineAsync(cut ? "426 Connection closed; transfer aborted." : "226 Closing data connection.").ConfigureAwait(false);
        }

        private async Task ReceiveAsync(StreamWriter control, TcpListener pasv, string target, bool append)
        {
            if (pasv == null) { await control.WriteLineAsync("425 Use PASV first.").ConfigureAwait(false); return; }
            await control.WriteLineAsync("150 File status okay; about to open data connection.").ConfigureAwait(false);
            bool cut = false;
            byte[] received;
            using (var dataClient = await pasv.AcceptTcpClientAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                pasv.Stop();
                var s = dataClient.GetStream();
                var chunk = new byte[8192];
                int limit = CutDataAfterBytes;
                while (true)
                {
                    int read = await s.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false);
                    if (read == 0) break;
                    buffer.Write(chunk, 0, read);
                    if (limit > 0 && buffer.Length >= limit)
                    {
                        buffer.SetLength(limit);
                        dataClient.Client.LingerState = new LingerOption(true, 0);
                        cut = true;
                        break;
                    }
                }
                received = buffer.ToArray();
            }

            lock (_lock)
            {
                byte[] existing;
                _files[target] = append && _files.TryGetValue(target, out existing)
                    ? existing.Concat(received).ToArray()
                    : received;
            }
            await control.WriteLineAsync(cut ? "426 Connection closed; transfer aborted." : "226 Closing data connection.").ConfigureAwait(false);
        }

        private string BuildListing(string path)
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                if (_files.ContainsKey(path))
                {
                    sb.Append(FileLine(Name(path), _files[path].Length));
                    return sb.ToString();
                }
                if (!_dirs.Contains(path)) return null;

                sb.Append("drwxrwxrwx 1 0 0 512 Sep 11 2026 .\r\n");
                sb.Append("drwxrwxrwx 1 0 0 512 Sep 11 2026 ..\r\n");
                foreach (var d in _dirs.Where(d => d != "/" && Parent(d) == path).OrderBy(d => d, StringComparer.Ordinal))
                    sb.Append("drwxrwxrwx 1 0 0 512 Sep 11 2026 ").Append(Name(d)).Append("\r\n");
                foreach (var f in _files.Keys.Where(f => Parent(f) == path).OrderBy(f => f, StringComparer.Ordinal))
                    sb.Append(FileLine(Name(f), _files[f].Length));
                return sb.ToString();
            }
        }

        private static string FileLine(string name, long size)
        {
            return "-rwxrwxrwx 1 0 0 " + size + " Sep 11 2026 " + name + "\r\n";
        }

        private bool MoveUnlockedSafe(string from, string to)
        {
            lock (_lock)
            {
                if (!_dirs.Contains(Parent(to))) return false;
                if (_dirs.Contains(to)) return false;
                if (_files.ContainsKey(to) && !_files.ContainsKey(from)) return false;
                if (_files.ContainsKey(from))
                {
                    // GoldHEN überschreibt eine existierende Zieldatei kommentarlos (Task 2 Nr. 13).
                    _files[to] = _files[from];
                    _files.Remove(from);
                    return true;
                }
                if (!_dirs.Contains(from)) return false;
                foreach (var d in _dirs.Where(d => d == from || d.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                {
                    _dirs.Remove(d);
                    _dirs.Add(to + d.Substring(from.Length));
                }
                foreach (var f in _files.Keys.Where(f => f.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                {
                    _files[to + f.Substring(from.Length)] = _files[f];
                    _files.Remove(f);
                }
                return true;
            }
        }

        private bool HasChildrenUnlocked(string dir)
        {
            return _dirs.Any(d => d != dir && Parent(d) == dir) || _files.Keys.Any(f => Parent(f) == dir);
        }

        private void AddDirectoryUnlocked(string path)
        {
            if (path == "/") return;
            AddDirectoryUnlocked(Parent(path));
            _dirs.Add(path);
        }

        private static string Resolve(string cwd, string arg)
        {
            if (string.IsNullOrEmpty(arg)) return cwd;
            return Normalize(arg.StartsWith("/", StringComparison.Ordinal) ? arg : cwd + "/" + arg);
        }

        internal static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var segment in (path ?? "").Split('/'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(segment);
            }
            return "/" + string.Join("/", parts);
        }

        private static string Parent(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? "/" : path.Substring(0, slash);
        }

        private static string Name(string path)
        {
            return path.Substring(path.LastIndexOf('/') + 1);
        }
    }
}
```

- [ ] **Step 2: Rauchtests des bestehenden Clients schreiben**

`tests/Ps4.Core.Tests/LoopbackFtpServerTests.cs`:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    /// <summary>Der bestehende FtpClient gegen den Loopback-Server — beweist den Server selbst.</summary>
    public class LoopbackFtpServerTests
    {
        [Fact]
        public async Task The_client_lists_a_directory()
        {
            using (var server = new LoopbackFtpServer())
            {
                server.AddDirectory("/data/GoldHEN/plugins");
                server.AddFile("/data/GoldHEN/plugins.ini", new byte[338]);

                var entries = await new FtpClient(server.Host, server.Port).ListAsync("/data/GoldHEN", CancellationToken.None);

                Assert.Equal(new[] { "plugins", "plugins.ini" }, entries.Select(e => e.Name).OrderBy(n => n).ToArray());
                Assert.True(entries.Single(e => e.Name == "plugins").IsDirectory);
                Assert.Equal(338, entries.Single(e => e.Name == "plugins.ini").Size);
            }
        }

        [Fact]
        public async Task The_client_downloads_bytes()
        {
            using (var server = new LoopbackFtpServer())
            {
                var data = Enumerable.Range(0, 100000).Select(i => (byte)(i % 251)).ToArray();
                server.AddFile("/data/a.bin", data);

                var result = await new FtpClient(server.Host, server.Port).DownloadAsync("/data/a.bin", CancellationToken.None);

                Assert.Equal(data, result);
            }
        }

        [Fact]
        public async Task The_client_deletes_a_file()
        {
            using (var server = new LoopbackFtpServer())
            {
                server.AddFile("/data/a.txt", new byte[] { 1 });

                await new FtpClient(server.Host, server.Port).DeleteAsync("/data/a.txt", CancellationToken.None);

                Assert.Null(server.GetFile("/data/a.txt"));
                Assert.Contains(server.CommandsSnapshot(), c => c.StartsWith("DELE"));
            }
        }
    }
}
```

- [ ] **Step 3: Laufen lassen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~LoopbackFtpServerTests`
Expected: 3 bestanden. Scheitert ein Test, liegt es am Server (der Client ist gegen GoldHEN bewährt): mit `server.CommandsSnapshot()` die gesendeten Befehle ausgeben und den Server anpassen, **nicht** den Client.

- [ ] **Step 4: Commit**

```bash
git add tests/Ps4.Core.Tests/LoopbackFtpServer.cs tests/Ps4.Core.Tests/LoopbackFtpServerTests.cs
git commit -m "Add a loopback FTP server to test FtpClient on the wire

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 6: `IFtpClient` erweitern und im `FtpClient` umsetzen

**Voraussetzung:** Task 2 ohne STOPP abgeschlossen (insbesondere Nr. 08/10 — absolutes `RenameTo`).

**Files:**
- Modify: `src/Ps4.Core/Ftp/IFtpClient.cs`, `src/Ps4.Core/Ftp/FtpClient.cs`, `tests/ScreenshotGallery.Core.Tests/MediaRepositoryTests.cs` (nur `FakeFtpClient`)
- Test: `tests/Ps4.Core.Tests/FtpClientLoopbackTests.cs`

**Interfaces:**
- Consumes: `FtpErrors.Translate` (Task 4), `LoopbackFtpServer` (Task 5), `FtpTransfer.CopyAsync` (bestehend).
- Produces — `IFtpClient` vollständig:

```csharp
Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct);
Task<byte[]> DownloadAsync(string path, CancellationToken ct);
Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct);
Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct);
Stream OpenRead(string path, long offset);
Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct);
Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct);
Task<long> GetFileSizeAsync(string path, CancellationToken ct);
Task MakeDirectoryAsync(string path, CancellationToken ct);
Task RemoveDirectoryAsync(string path, CancellationToken ct);
Task RenameAsync(string fromPath, string toPath, CancellationToken ct);
Task DeleteAsync(string path, CancellationToken ct);
```

  Verträge: `progress` meldet immer die in **diesem** Aufruf übertragenen Bytes ab 0 (der Aufrufer addiert einen Versatz selbst). Netz-/Serverfehler kommen als `FtpException`, Anwender-Abbruch als `OperationCanceledException`, lokale Dateifehler unverändert. `OpenRead` ist synchron (für den Shell-Stream, läuft nie auf dem UI-Thread); der zurückgegebene Stream schließt beim `Dispose` auch die FTP-Antwort.

- [ ] **Step 1: Failing tests schreiben**

`tests/Ps4.Core.Tests/FtpClientLoopbackTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    public sealed class FtpClientLoopbackTests : IDisposable
    {
        private readonly LoopbackFtpServer _server = new LoopbackFtpServer();
        private readonly FtpClient _client;
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "ps4core-tests-" + Guid.NewGuid().ToString("N"));

        public FtpClientLoopbackTests()
        {
            _client = new FtpClient(_server.Host, _server.Port);
            Directory.CreateDirectory(_temp);
        }

        public void Dispose()
        {
            _server.Dispose();
            try { Directory.Delete(_temp, true); } catch (Exception) { }
        }

        private static byte[] Pattern(int length)
        {
            return Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        }

        private string LocalFile(string name, byte[] content)
        {
            var path = Path.Combine(_temp, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private sealed class Collect : IProgress<long>
        {
            public readonly List<long> Reports = new List<long>();
            public void Report(long value) { Reports.Add(value); }
        }

        [Fact]
        public async Task Upload_stores_the_file_and_reports_progress()
        {
            _server.AddDirectory("/data");
            var data = Pattern(200000);
            var progress = new Collect();

            await _client.UploadFromFileAsync(LocalFile("a.bin", data), "/data/a.bin", progress, CancellationToken.None);

            Assert.Equal(data, _server.GetFile("/data/a.bin"));
            Assert.Equal(200000, progress.Reports.Last());
        }

        [Fact]
        public async Task Append_sends_only_the_rest_of_the_local_file()
        {
            var data = Pattern(3000);
            _server.AddFile("/data/a.bin", data.Take(1000).ToArray());
            var progress = new Collect();

            await _client.AppendFromFileAsync(LocalFile("a.bin", data), 1000, "/data/a.bin", progress, CancellationToken.None);

            Assert.Equal(data, _server.GetFile("/data/a.bin"));
            Assert.Equal(2000, progress.Reports.Last());
            Assert.Contains(_server.CommandsSnapshot(), c => c.StartsWith("APPE"));
        }

        [Fact]
        public async Task Download_with_offset_continues_the_partial_local_file()
        {
            var data = Pattern(5000);
            _server.AddFile("/data/a.bin", data);
            var part = LocalFile("a.bin.part", data.Take(2000).ToArray());

            await _client.DownloadToFileAsync("/data/a.bin", part, 2000, null, CancellationToken.None);

            Assert.Equal(data, File.ReadAllBytes(part));
            Assert.Contains("REST 2000", _server.CommandsSnapshot());
        }

        [Fact]
        public async Task Download_with_offset_refuses_a_partial_file_of_the_wrong_length()
        {
            _server.AddFile("/data/a.bin", Pattern(5000));
            var part = LocalFile("a.bin.part", Pattern(10));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _client.DownloadToFileAsync("/data/a.bin", part, 2000, null, CancellationToken.None));
        }

        [Fact]
        public void OpenRead_streams_from_an_offset()
        {
            var data = Pattern(50000);
            _server.AddFile("/data/a.bin", data);

            byte[] read;
            using (var stream = _client.OpenRead("/data/a.bin", 10))
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                read = buffer.ToArray();
            }

            Assert.Equal(data.Skip(10).ToArray(), read);
        }

        [Fact]
        public async Task GetFileSize_reports_the_size()
        {
            _server.AddFile("/data/a.bin", Pattern(1234));
            Assert.Equal(1234, await _client.GetFileSizeAsync("/data/a.bin", CancellationToken.None));
        }

        [Fact]
        public async Task Directories_are_created_and_removed()
        {
            _server.AddDirectory("/data");

            await _client.MakeDirectoryAsync("/data/neu", CancellationToken.None);
            Assert.True(_server.DirectoryExists("/data/neu"));

            await _client.RemoveDirectoryAsync("/data/neu", CancellationToken.None);
            Assert.False(_server.DirectoryExists("/data/neu"));
        }

        [Fact]
        public async Task Rename_moves_a_file_into_another_directory()
        {
            _server.AddFile("/data/a/x.txt", new byte[] { 7 });
            _server.AddDirectory("/data/b");

            await _client.RenameAsync("/data/a/x.txt", "/data/b/y.txt", CancellationToken.None);

            Assert.Null(_server.GetFile("/data/a/x.txt"));
            Assert.Equal(new byte[] { 7 }, _server.GetFile("/data/b/y.txt"));
        }

        [Fact]
        public async Task A_missing_file_is_a_permanent_ftp_exception_in_plain_text()
        {
            _server.AddDirectory("/data");
            var ex = await Assert.ThrowsAsync<FtpException>(() => _client.DownloadAsync("/data/fehlt.bin", CancellationToken.None));
            Assert.Equal(FtpErrorKind.Permanent, ex.Kind);
            Assert.Equal(550, ex.StatusCode);
            Assert.Equal("Nicht vorhanden oder kein Zugriff", ex.Message);
        }

        [Fact]
        public async Task Removing_a_non_empty_directory_is_refused_permanently()
        {
            _server.AddFile("/data/voll/x.txt", new byte[] { 1 });
            var ex = await Assert.ThrowsAsync<FtpException>(() => _client.RemoveDirectoryAsync("/data/voll", CancellationToken.None));
            Assert.Equal(FtpErrorKind.Permanent, ex.Kind);
        }

        [Fact]
        public async Task A_cut_data_connection_is_transient()
        {
            _server.AddFile("/data/a.bin", Pattern(500000));
            _server.CutDataAfterBytes = 1000;

            var ex = await Assert.ThrowsAsync<FtpException>(() =>
                _client.DownloadToFileAsync("/data/a.bin", Path.Combine(_temp, "a.part"), null, CancellationToken.None));
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public async Task A_refused_connection_is_transient()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int unused = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var ex = await Assert.ThrowsAsync<FtpException>(() =>
                new FtpClient("127.0.0.1", unused).ListAsync("/", CancellationToken.None));
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public async Task User_cancellation_surfaces_as_OperationCanceledException()
        {
            _server.AddDirectory("/data");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.ListAsync("/data", cts.Token));
            }
        }

        [Fact]
        public async Task A_non_ascii_name_round_trips_through_list_and_delete()
        {
            // Befund Task 2: GoldHEN speichert, was FtpWebRequest schickt (Encoding.Default).
            _server.AddDirectory("/data");
            await _client.UploadFromFileAsync(LocalFile("u.txt", new byte[] { 1 }), "/data/Ärger ü.txt", null, CancellationToken.None);

            var listed = (await _client.ListAsync("/data", CancellationToken.None)).Single().Name;
            Assert.Equal("Ärger ü.txt", listed);

            await _client.DeleteAsync("/data/" + listed, CancellationToken.None);
            Assert.Empty(await _client.ListAsync("/data", CancellationToken.None));
        }

        [Fact]
        public async Task A_utf8_named_file_from_another_tool_stays_reachable()
        {
            // FileZilla & Co. legen Namen als UTF-8 ab. Wir zeigen sie als Mojibake,
            // erreichen sie aber byte-genau.
            _server.AddFileWithRawName("/data", System.Text.Encoding.UTF8.GetBytes("Ärger.txt"), new byte[] { 9 });

            var listed = (await _client.ListAsync("/data", CancellationToken.None)).Single().Name;

            Assert.Equal(new byte[] { 9 }, await _client.DownloadAsync("/data/" + listed, CancellationToken.None));
        }

        [Fact]
        public async Task A_missing_local_upload_source_is_a_local_error_not_a_connection_loss()
        {
            _server.AddDirectory("/data");
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _client.UploadFromFileAsync(Path.Combine(_temp, "gibtsnicht.bin"), "/data/x.bin", null, CancellationToken.None));
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~FtpClientLoopbackTests`
Expected: Build-Fehler — `UploadFromFileAsync`, `AppendFromFileAsync`, `OpenRead`, `GetFileSizeAsync`, `MakeDirectoryAsync`, `RemoveDirectoryAsync`, `RenameAsync` und die Offset-Überladung fehlen.

- [ ] **Step 3: `IFtpClient` erweitern**

`src/Ps4.Core/Ftp/IFtpClient.cs` vollständig:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Naht zwischen Logik und Draht — im Test ein Dictionary bzw. ein
    /// In-Memory-Dateisystem. Verträge für alle Methoden:
    /// Netz-/Serverfehler kommen als FtpException (Transient/Permanent),
    /// ein Abbruch durch den Aufrufer als OperationCanceledException, lokale
    /// Dateifehler unverändert. progress meldet immer die in DIESEM Aufruf
    /// übertragenen Bytes ab 0.
    /// </summary>
    public interface IFtpClient
    {
        Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct);
        Task<byte[]> DownloadAsync(string path, CancellationToken ct);

        /// <summary>
        /// Lädt streamend in eine Datei — für Clips, die als byte[] den Speicher
        /// sprengen würden. progress meldet die bisher übertragenen Bytes (die
        /// Gesamtgröße kennt der Aufrufer aus dem Listing).
        /// </summary>
        Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct);

        /// <summary>
        /// Wie oben, aber ab offset (REST): die lokale Datei muss dann genau
        /// offset Bytes lang sein und wird fortgeschrieben.
        /// </summary>
        Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct);

        /// <summary>Synchron: für den Shell-Stream von Explorer, nie auf dem UI-Thread.</summary>
        Stream OpenRead(string path, long offset);

        Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct);

        /// <summary>APPE: hängt die lokale Datei ab localOffset an die entfernte an (Fortsetzen).</summary>
        Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct);

        Task<long> GetFileSizeAsync(string path, CancellationToken ct);
        Task MakeDirectoryAsync(string path, CancellationToken ct);

        /// <summary>Nur leere Ordner — rekursives Löschen ist Sache des Aufrufers.</summary>
        Task RemoveDirectoryAsync(string path, CancellationToken ct);

        /// <summary>RNFR/RNTO mit absolutem Zielpfad (an der Konsole verifiziert, Task 2).</summary>
        Task RenameAsync(string fromPath, string toPath, CancellationToken ct);

        Task DeleteAsync(string path, CancellationToken ct);
    }
}
```

- [ ] **Step 4: `FtpClient` umsetzen**

`src/Ps4.Core/Ftp/FtpClient.cs` vollständig:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Dünner Draht über FtpWebRequest (auf net48 der richtige Weg; die
    /// Deprecation gilt .NET 6+). GoldHEN: anonym, passiv, binär.
    /// Bewusst OHNE eigene Wiederholungslogik — Fehler gehen als FtpException
    /// (transient/permanent, deutscher Klartext) nach oben, die App entscheidet.
    ///
    /// Wichtig: FtpWebRequest.Timeout wirkt nur auf synchrone GetResponse()-Aufrufe;
    /// GetResponseAsync() ignoriert es. Darum: verlinkte CTS mit CancelAfter() für
    /// die Frist; request.Abort() auf dem Timeout-Token registrieren.
    /// </summary>
    public sealed class FtpClient : IFtpClient
    {
        /// <summary>Listen ist billig: 8 s. Transfers: 30 s Untätigkeit (Spec).</summary>
        private const int ListTimeoutMs = 8000;
        private const int TransferTimeoutMs = 30000;

        /// <summary>
        /// Die Kodierung, in der FtpWebRequest Pfade auf den Draht schreibt:
        /// Es fragt „OPTS utf8 on", GoldHEN antwortet 504, danach nimmt .NET
        /// Encoding.Default (auf deutschem Windows Windows-1252; Befund Task 2).
        /// GoldHEN speichert genau diese Bytes. LIST muss darum mit DERSELBEN
        /// Kodierung gelesen werden — sonst trifft ein gelisteter Name mit
        /// Umlaut beim nächsten RETR/DELE die Datei nicht mehr. Namen, die ein
        /// anderer Client als UTF-8 abgelegt hat, erscheinen so als Mojibake,
        /// bleiben aber byte-genau erreichbar.
        /// </summary>
        internal static readonly Encoding PathEncoding = Encoding.Default;

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

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            return Guard(async () =>
            {
                var request = Create(path, WebRequestMethods.Ftp.ListDirectoryDetails, ListTimeoutMs);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(ListTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(response.GetResponseStream(), PathEncoding))
                    {
                        var listing = await reader.ReadToEndAsync().ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        return UnixListingParser.Parse(listing);
                    }
                }
            }, ct);
        }

        public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
        {
            return Guard(async () =>
            {
                var request = Create(path, WebRequestMethods.Ftp.DownloadFile, TransferTimeoutMs);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TransferTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                    using (var stream = response.GetResponseStream())
                    using (var buffer = new MemoryStream())
                    {
                        await stream.CopyToAsync(buffer, 81920, timeout.Token).ConfigureAwait(false);
                        return buffer.ToArray();
                    }
                }
            }, ct);
        }

        public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct)
        {
            return DownloadToFileAsync(path, localPath, 0, progress, ct);
        }

        public async Task DownloadToFileAsync(string path, string localPath, long offset,
            IProgress<long> progress, CancellationToken ct)
        {
            if (offset > 0)
            {
                var existing = new FileInfo(localPath);
                if (!existing.Exists || existing.Length != offset)
                    throw new InvalidOperationException(
                        "Die lokale Teildatei passt nicht zum Versatz " + offset + ": " + localPath);
            }

            await Guard(async () =>
            {
                var request = Create(path, WebRequestMethods.Ftp.DownloadFile, TransferTimeoutMs);
                request.ContentOffset = offset;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    // Anders als DownloadAsync ist das hier KEINE Gesamtfrist: jeder
                    // gelesene Block schiebt sie neu an (siehe FtpTransfer).
                    timeout.CancelAfter(TransferTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                    using (var stream = response.GetResponseStream())
                    using (var file = new FileStream(localPath, offset > 0 ? FileMode.Append : FileMode.Create,
                        FileAccess.Write, FileShare.None, FtpTransfer.BufferSize, true))
                    {
                        await FtpTransfer.CopyAsync(stream, file, progress,
                            () => timeout.CancelAfter(TransferTimeoutMs), timeout.Token).ConfigureAwait(false);
                    }
                }
            }, ct).ConfigureAwait(false);
        }

        public Stream OpenRead(string path, long offset)
        {
            var request = Create(path, WebRequestMethods.Ftp.DownloadFile, TransferTimeoutMs);
            request.ContentOffset = offset;
            try
            {
                // Synchron: hier greift request.Timeout/ReadWriteTimeout von selbst.
                var response = request.GetResponse();
                return new ResponseStream(response);
            }
            catch (Exception ex)
            {
                var translated = FtpErrors.Translate(ex, CancellationToken.None);
                if (ReferenceEquals(translated, ex)) throw;
                throw translated;
            }
        }

        public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            return UploadCoreAsync(localPath, 0, remotePath, WebRequestMethods.Ftp.UploadFile, progress, ct);
        }

        public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath,
            IProgress<long> progress, CancellationToken ct)
        {
            return UploadCoreAsync(localPath, localOffset, remotePath, WebRequestMethods.Ftp.AppendFile, progress, ct);
        }

        private async Task UploadCoreAsync(string localPath, long localOffset, string remotePath, string method,
            IProgress<long> progress, CancellationToken ct)
        {
            // Die lokale Datei VOR dem Netz öffnen: fehlt sie oder ist sie
            // gesperrt, bleibt das ein lokaler Fehler — kein „Verbindungsabbruch".
            using (var file = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                FtpTransfer.BufferSize, true))
            {
                file.Seek(localOffset, SeekOrigin.Begin);
                await Guard(async () =>
                {
                    var request = Create(remotePath, method, TransferTimeoutMs);
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(TransferTimeoutMs);
                        using (timeout.Token.Register(request.Abort))
                        {
                            using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                            {
                                await FtpTransfer.CopyAsync(file, stream, progress,
                                    () => timeout.CancelAfter(TransferTimeoutMs), timeout.Token).ConfigureAwait(false);
                            }
                            using (await request.GetResponseAsync().ConfigureAwait(false))
                            {
                            }
                        }
                    }
                }, ct).ConfigureAwait(false);
            }
        }

        public Task<long> GetFileSizeAsync(string path, CancellationToken ct)
        {
            return Guard(async () =>
            {
                var request = Create(path, WebRequestMethods.Ftp.GetFileSize, ListTimeoutMs);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(ListTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                    {
                        return response.ContentLength;
                    }
                }
            }, ct);
        }

        public Task MakeDirectoryAsync(string path, CancellationToken ct)
        {
            return SimpleAsync(path, WebRequestMethods.Ftp.MakeDirectory, null, ct);
        }

        public Task RemoveDirectoryAsync(string path, CancellationToken ct)
        {
            return SimpleAsync(path, WebRequestMethods.Ftp.RemoveDirectory, null, ct);
        }

        public Task RenameAsync(string fromPath, string toPath, CancellationToken ct)
        {
            // Absoluter Zielpfad: FtpWebRequest schickt ihn unverändert als RNTO
            // (sonst hängte es ihn an das Quellverzeichnis). An der Konsole
            // geprüft in Task 2 (Nr. 08/10).
            return SimpleAsync(fromPath, WebRequestMethods.Ftp.Rename, r => r.RenameTo = toPath, ct);
        }

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            return SimpleAsync(path, WebRequestMethods.Ftp.DeleteFile, null, ct);
        }

        private Task SimpleAsync(string path, string method, Action<FtpWebRequest> configure, CancellationToken ct)
        {
            return Guard(async () =>
            {
                var request = Create(path, method, TransferTimeoutMs);
                if (configure != null) configure(request);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TransferTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (await request.GetResponseAsync().ConfigureAwait(false))
                    {
                    }
                }
            }, ct);
        }

        private static async Task<T> Guard<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var translated = FtpErrors.Translate(ex, ct);
                if (ReferenceEquals(translated, ex)) throw;
                throw translated;
            }
        }

        private static Task Guard(Func<Task> operation, CancellationToken ct)
        {
            return Guard(async () =>
            {
                await operation().ConfigureAwait(false);
                return 0;
            }, ct);
        }

        /// <summary>Der Antwortstrom plus die Antwort selbst — Dispose schließt beides.</summary>
        private sealed class ResponseStream : Stream
        {
            private readonly WebResponse _response;
            private readonly Stream _inner;

            public ResponseStream(WebResponse response)
            {
                _response = response;
                _inner = response.GetResponseStream();
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                try
                {
                    return _inner.Read(buffer, offset, count);
                }
                catch (Exception ex)
                {
                    var translated = FtpErrors.Translate(ex, CancellationToken.None);
                    if (ReferenceEquals(translated, ex)) throw;
                    throw translated;
                }
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                    _response.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
```

- [ ] **Step 5: Gallery-Fake nachziehen**

In `tests/ScreenshotGallery.Core.Tests/MediaRepositoryTests.cs` in der Klasse `FakeFtpClient` nach `DeleteAsync` anfügen (die Gallery braucht keinen der neuen Befehle — Stubs genügen):

```csharp
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
```

- [ ] **Step 6: Grün prüfen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~FtpClientLoopbackTests`
Expected: 16 bestanden. (Die beiden Umlaut-Tests setzen voraus, dass `Encoding.Default` Ä/ü darstellen kann — auf dem deutschen Windows dieser Maschine ist das Windows-1252.) Scheitert `Rename_moves_a_file_into_another_directory`, mit `_server.CommandsSnapshot()` den tatsächlich gesendeten RNTO-Pfad ansehen und mit dem Ergebnis von Task 2 Nr. 10 vergleichen — abweichendes Verhalten ist ein STOPP (Anwender fragen), kein Grund, den Test zu lockern.

Run: `dotnet test ScreenshotGallery.sln` und `dotnet build ScreenshotGallery.sln -c Release`
Expected: alles grün, `0 Warnung(en)`.

- [ ] **Step 7: Commit**

```bash
git add src/Ps4.Core/Ftp tests/Ps4.Core.Tests/FtpClientLoopbackTests.cs tests/ScreenshotGallery.Core.Tests/MediaRepositoryTests.cs
git commit -m "Teach FtpClient to upload, append, resume, rename and manage folders

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 7: Titel-Infos in `Ps4.Core.Titles`

**Files:**
- Create: `src/Ps4.Core/Titles/TitleId.cs`, `src/Ps4.Core/Titles/TitleInfo.cs`, `src/Ps4.Core/Titles/TitleInfoLoader.cs`
- Test: `tests/Ps4.Core.Tests/TitleInfoLoaderTests.cs`, `tests/Ps4.Core.Tests/StubFtpClient.cs`

**Interfaces:**
- Consumes: `IFtpClient` (Task 6), `FtpException` (Task 4), `SfoReader`, `TitlePaths` (Task 1).
- Produces:
  - `TitleId.IsMatch(string name) : bool` — `^[A-Z]{4}[0-9]{5}$`
  - `TitleInfo(string id, string name, byte[] iconPng)`; Properties `Id`, `Name` (fällt auf `Id` zurück), `IconPng` (null = keins), `HasRealName`
  - `TitleInfoLoader.LoadAsync(IFtpClient ftp, string titleId, CancellationToken ct) : Task<TitleInfo>` — fehlende/kaputte Dateien = stiller Fallback; transienter Verbindungsfehler und Abbruch werden **durchgereicht**.

- [ ] **Step 1: Failing tests schreiben**

`tests/Ps4.Core.Tests/StubFtpClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace Ps4.Core.Tests
{
    /// <summary>Nur DownloadAsync lebt: Pfad → Bytes; fehlend → 550. Alles andere wirft.</summary>
    internal sealed class StubFtpClient : IFtpClient
    {
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
        public Exception FailWith;

        public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailWith != null) throw FailWith;
            byte[] data;
            if (!Files.TryGetValue(path, out data))
                throw new FtpException("Nicht vorhanden oder kein Zugriff", FtpErrorKind.Permanent, 550);
            return Task.FromResult(data);
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
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
```

`tests/Ps4.Core.Tests/TitleInfoLoaderTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using Ps4.Core.Titles;
using Xunit;

namespace Ps4.Core.Tests
{
    public class TitleInfoLoaderTests
    {
        [Theory]
        [InlineData("CUSA00411", true)]
        [InlineData("PPSA01234", true)]
        [InlineData("NPXS20001", true)]
        [InlineData("cusa00411", false)]
        [InlineData("CUSA0041", false)]
        [InlineData("CUSA004111", false)]
        [InlineData("CUSA00411 ", false)]
        [InlineData("GoldHEN", false)]
        [InlineData("CUSA٠٠٤١١", false)]   // arabisch-indische Ziffern: \d würde sie nehmen
        [InlineData(null, false)]
        public void Title_ids_are_recognised(string name, bool expected)
        {
            Assert.Equal(expected, TitleId.IsMatch(name));
        }

        [Fact]
        public async Task A_title_with_sfo_and_icon_is_resolved()
        {
            var ftp = new StubFtpClient();
            ftp.Files["/system_data/priv/appmeta/CUSA00411/param.sfo"] = File.ReadAllBytes(Path.Combine("TestData", "param.sfo"));
            ftp.Files["/user/appmeta/CUSA00411/icon0.png"] = new byte[] { 1, 2, 3 };

            var info = await TitleInfoLoader.LoadAsync(ftp, "CUSA00411", CancellationToken.None);

            Assert.Equal("CUSA00411", info.Id);
            Assert.Equal("Grand Theft Auto V", info.Name);
            Assert.True(info.HasRealName);
            Assert.Equal(new byte[] { 1, 2, 3 }, info.IconPng);
        }

        [Fact]
        public async Task Missing_files_fall_back_to_the_id_silently()
        {
            var info = await TitleInfoLoader.LoadAsync(new StubFtpClient(), "CUSA00411", CancellationToken.None);

            Assert.Equal("CUSA00411", info.Name);
            Assert.False(info.HasRealName);
            Assert.Null(info.IconPng);
        }

        [Fact]
        public async Task A_connection_loss_is_not_mistaken_for_a_missing_name()
        {
            var ftp = new StubFtpClient { FailWith = new FtpException("weg", FtpErrorKind.Transient, 0) };
            await Assert.ThrowsAsync<FtpException>(() => TitleInfoLoader.LoadAsync(ftp, "CUSA00411", CancellationToken.None));
        }

        [Fact]
        public async Task Cancellation_is_passed_through()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => TitleInfoLoader.LoadAsync(new StubFtpClient(), "CUSA00411", cts.Token));
            }
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~TitleInfoLoaderTests`
Expected: Build-Fehler — `TitleId`, `TitleInfoLoader` fehlen.

- [ ] **Step 3: Implementieren**

`src/Ps4.Core/Titles/TitleId.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Ps4.Core.Titles
{
    /// <summary>
    /// Vier Großbuchstaben, fünf Ziffern: CUSA (Spiele), PPSA, PCAS, NPXS
    /// (Systemanwendungen) … Bewusst [0-9] statt \d — \d nähme in .NET auch
    /// andere Unicode-Ziffern.
    /// </summary>
    public static class TitleId
    {
        private static readonly Regex Pattern =
            new Regex("^[A-Z]{4}[0-9]{5}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsMatch(string name)
        {
            return name != null && Pattern.IsMatch(name);
        }
    }
}
```

`src/Ps4.Core/Titles/TitleInfo.cs`:

```csharp
namespace Ps4.Core.Titles
{
    public sealed class TitleInfo
    {
        public TitleInfo(string id, string name, byte[] iconPng)
        {
            Id = id;
            Name = string.IsNullOrEmpty(name) ? id : name;
            IconPng = iconPng;
        }

        public string Id { get; }

        /// <summary>Anzeigename; fehlt er auf der Konsole, die Title-ID.</summary>
        public string Name { get; }

        /// <summary>icon0.png als Bytes; null, wenn keins da ist.</summary>
        public byte[] IconPng { get; }

        public bool HasRealName { get { return Name != Id; } }
    }
}
```

`src/Ps4.Core/Titles/TitleInfoLoader.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace Ps4.Core.Titles
{
    /// <summary>
    /// Name und Icon eines Titels von der Konsole. Wie in der Gallery ist eine
    /// fehlende oder kaputte Datei ein stiller Fallback auf die ID — aber ein
    /// Verbindungsabbruch ist KEIN fehlender Name: der geht durch, damit ein
    /// Aufrufer ihn nicht als „dieser Titel hat keinen Namen" zwischenspeichert.
    /// </summary>
    public static class TitleInfoLoader
    {
        public static async Task<TitleInfo> LoadAsync(IFtpClient ftp, string titleId, CancellationToken ct)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));

            var sfo = await TryDownloadAsync(ftp, TitlePaths.SfoPath(titleId), ct).ConfigureAwait(false);
            var icon = await TryDownloadAsync(ftp, TitlePaths.IconPath(titleId), ct).ConfigureAwait(false);
            return new TitleInfo(titleId, SfoReader.ReadTitle(sfo), icon);
        }

        private static async Task<byte[]> TryDownloadAsync(IFtpClient ftp, string path, CancellationToken ct)
        {
            try
            {
                return await ftp.DownloadAsync(path, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (FtpException ex) when (ex.IsTransient)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/Ps4.Core.Tests --filter FullyQualifiedName~TitleInfoLoaderTests`
Expected: 14 Testfälle bestanden.

- [ ] **Step 5: Commit**

```bash
git add src/Ps4.Core/Titles tests/Ps4.Core.Tests/TitleInfoLoaderTests.cs tests/Ps4.Core.Tests/StubFtpClient.cs
git commit -m "Resolve title names and icons in the shared core

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 8: `FileManager.Core` anlegen — Pfade und Verlauf

**Files:**
- Create: `src/FileManager.Core/FileManager.Core.csproj`, `src/FileManager.Core/Properties/AssemblyInfo.cs`, `src/FileManager.Core/Paths/RemotePath.cs`, `src/FileManager.Core/Paths/NavigationHistory.cs`
- Create: `tests/FileManager.Core.Tests/FileManager.Core.Tests.csproj`, `tests/FileManager.Core.Tests/RemotePathTests.cs`, `tests/FileManager.Core.Tests/NavigationHistoryTests.cs`
- Modify: `ScreenshotGallery.sln`

**Interfaces:**
- Produces (Namensraum `FileManager.Core.Paths`):
  - `RemotePath.Root` (`"/"`), `Normalize(string) : string`, `Combine(string dir, string name) : string`, `Parent(string) : string`, `Name(string) : string`, `IsSameOrBelow(string path, string ancestor) : bool`, `IsStrictlyBelow(string path, string ancestor) : bool`, `Canonical(string) : string` (normalisiert und bildet `/user/data…` auf `/data…` ab — **nur** für Zonen).
  - `NavigationHistory`: `Current` (string, anfangs null), `CanGoBack`, `CanGoForward`, `Navigate(string path)`, `GoBack() : string`, `GoForward() : string`.

- [ ] **Step 1: Projekte anlegen**

`src/FileManager.Core/FileManager.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <UseWindowsForms>false</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ps4.Core\Ps4.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/FileManager.Core/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FileManager.Core.Tests")]
```

`tests/FileManager.Core.Tests/FileManager.Core.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\FileManager.Core\FileManager.Core.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln ScreenshotGallery.sln add src/FileManager.Core/FileManager.Core.csproj --solution-folder src
dotnet sln ScreenshotGallery.sln add tests/FileManager.Core.Tests/FileManager.Core.Tests.csproj --solution-folder tests
```

- [ ] **Step 2: Failing tests schreiben**

`tests/FileManager.Core.Tests/RemotePathTests.cs`:

```csharp
using FileManager.Core.Paths;
using Xunit;

namespace FileManager.Core.Tests
{
    public class RemotePathTests
    {
        [Theory]
        [InlineData(null, "/")]
        [InlineData("", "/")]
        [InlineData("/", "/")]
        [InlineData("/data/", "/data")]
        [InlineData("data/x", "/data/x")]
        [InlineData("//data//x", "/data/x")]
        [InlineData("/data/./x", "/data/x")]
        [InlineData("/data/../system", "/system")]
        [InlineData("/../..", "/")]
        [InlineData("/data/a b/c", "/data/a b/c")]
        public void Normalize_folds_the_path(string input, string expected)
        {
            Assert.Equal(expected, RemotePath.Normalize(input));
        }

        [Fact]
        public void Combine_parent_and_name_work_together()
        {
            Assert.Equal("/data/GoldHEN/plugins.ini", RemotePath.Combine("/data/GoldHEN/", "plugins.ini"));
            Assert.Equal("/x", RemotePath.Combine("/", "x"));
            Assert.Equal("/data/GoldHEN", RemotePath.Parent("/data/GoldHEN/plugins.ini"));
            Assert.Equal("/", RemotePath.Parent("/data"));
            Assert.Equal("/", RemotePath.Parent("/"));
            Assert.Equal("plugins.ini", RemotePath.Name("/data/GoldHEN/plugins.ini"));
            Assert.Equal("", RemotePath.Name("/"));
        }

        [Fact]
        public void Below_respects_segment_boundaries()
        {
            Assert.True(RemotePath.IsSameOrBelow("/data", "/data"));
            Assert.True(RemotePath.IsSameOrBelow("/data/x", "/data"));
            Assert.False(RemotePath.IsSameOrBelow("/database", "/data"));
            Assert.True(RemotePath.IsSameOrBelow("/anything", "/"));

            Assert.False(RemotePath.IsStrictlyBelow("/data", "/data"));
            Assert.True(RemotePath.IsStrictlyBelow("/data/x", "/data"));
            Assert.False(RemotePath.IsStrictlyBelow("/mnt/usb00/x", "/mnt/usb0"));
            Assert.False(RemotePath.IsStrictlyBelow("/", "/"));
            Assert.True(RemotePath.IsStrictlyBelow("/data", "/"));
        }

        [Theory]
        [InlineData("/user/data", "/data")]
        [InlineData("/user/data/pkg/a.pkg", "/data/pkg/a.pkg")]
        [InlineData("/user/database/x", "/user/database/x")]
        [InlineData("/user/data/../home", "/user/home")]
        [InlineData("/data/x", "/data/x")]
        public void Canonical_maps_user_data_onto_data(string input, string expected)
        {
            Assert.Equal(expected, RemotePath.Canonical(input));
        }
    }
}
```

`tests/FileManager.Core.Tests/NavigationHistoryTests.cs`:

```csharp
using System;
using FileManager.Core.Paths;
using Xunit;

namespace FileManager.Core.Tests
{
    public class NavigationHistoryTests
    {
        [Fact]
        public void The_first_navigation_has_nothing_to_go_back_to()
        {
            var history = new NavigationHistory();
            Assert.Null(history.Current);

            history.Navigate("/data");

            Assert.Equal("/data", history.Current);
            Assert.False(history.CanGoBack);
            Assert.False(history.CanGoForward);
        }

        [Fact]
        public void Back_and_forward_walk_the_trail()
        {
            var history = new NavigationHistory();
            history.Navigate("/data");
            history.Navigate("/data/GoldHEN");
            history.Navigate("/data/GoldHEN/plugins");

            Assert.Equal("/data/GoldHEN", history.GoBack());
            Assert.Equal("/data", history.GoBack());
            Assert.False(history.CanGoBack);
            Assert.Equal("/data/GoldHEN", history.GoForward());
            Assert.True(history.CanGoForward);
        }

        [Fact]
        public void Navigating_after_going_back_drops_the_forward_trail()
        {
            var history = new NavigationHistory();
            history.Navigate("/a");
            history.Navigate("/b");
            history.GoBack();

            history.Navigate("/c");

            Assert.False(history.CanGoForward);
            Assert.Equal("/a", history.GoBack());
        }

        [Fact]
        public void Navigating_to_the_current_path_adds_nothing()
        {
            var history = new NavigationHistory();
            history.Navigate("/a");
            history.Navigate("/a/");          // gleicher Pfad, anders geschrieben

            Assert.False(history.CanGoBack);
        }

        [Fact]
        public void Going_back_without_history_throws()
        {
            Assert.Throws<InvalidOperationException>(() => new NavigationHistory().GoBack());
            Assert.Throws<InvalidOperationException>(() => new NavigationHistory().GoForward());
        }
    }
}
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests`
Expected: Build-Fehler — `RemotePath`, `NavigationHistory` fehlen.

- [ ] **Step 4: Implementieren**

`src/FileManager.Core/Paths/RemotePath.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace FileManager.Core.Paths
{
    /// <summary>
    /// Unix-Pfade der Konsole. Groß-/Kleinschreibung zählt (Ordinal) — das
    /// PS4-Dateisystem unterscheidet sie.
    /// </summary>
    public static class RemotePath
    {
        public const string Root = "/";

        private const string UserData = "/user/data";
        private const string Data = "/data";

        /// <summary>Führt „/“ voran, faltet „//“, „.“ und „..“, entfernt ein Schluss-„/“. „..“ über die Wurzel bleibt „/“.</summary>
        public static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var segment in (path ?? "").Split('/'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(segment);
            }
            return "/" + string.Join("/", parts);
        }

        public static string Combine(string dir, string name)
        {
            return Normalize(Normalize(dir) + "/" + name);
        }

        public static string Parent(string path)
        {
            var p = Normalize(path);
            int slash = p.LastIndexOf('/');
            return slash <= 0 ? Root : p.Substring(0, slash);
        }

        public static string Name(string path)
        {
            var p = Normalize(path);
            return p == Root ? "" : p.Substring(p.LastIndexOf('/') + 1);
        }

        public static bool IsSameOrBelow(string path, string ancestor)
        {
            var p = Normalize(path);
            var a = Normalize(ancestor);
            if (a == Root) return true;
            return p == a || p.StartsWith(a + "/", StringComparison.Ordinal);
        }

        public static bool IsStrictlyBelow(string path, string ancestor)
        {
            var p = Normalize(path);
            return p != Normalize(ancestor) && IsSameOrBelow(p, ancestor);
        }

        /// <summary>
        /// /user/data und /data sind auf der Konsole derselbe Ordner (Spec,
        /// „Verifizierte Fakten"). Nur für Zonenentscheidungen — angezeigt wird
        /// der Pfad, den der Anwender gewählt hat.
        /// </summary>
        public static string Canonical(string path)
        {
            var p = Normalize(path);
            if (IsSameOrBelow(p, UserData)) return Data + p.Substring(UserData.Length);
            return p;
        }
    }
}
```

`src/FileManager.Core/Paths/NavigationHistory.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace FileManager.Core.Paths
{
    /// <summary>Zurück/Vor wie im Explorer: Navigieren nach einem Zurück kappt den Vor-Stapel.</summary>
    public sealed class NavigationHistory
    {
        private readonly Stack<string> _back = new Stack<string>();
        private readonly Stack<string> _forward = new Stack<string>();

        public string Current { get; private set; }
        public bool CanGoBack { get { return _back.Count > 0; } }
        public bool CanGoForward { get { return _forward.Count > 0; } }

        public void Navigate(string path)
        {
            var target = RemotePath.Normalize(path);
            if (target == Current) return;
            if (Current != null) _back.Push(Current);
            _forward.Clear();
            Current = target;
        }

        public string GoBack()
        {
            if (!CanGoBack) throw new InvalidOperationException("Kein Zurück vorhanden.");
            _forward.Push(Current);
            Current = _back.Pop();
            return Current;
        }

        public string GoForward()
        {
            if (!CanGoForward) throw new InvalidOperationException("Kein Vor vorhanden.");
            _back.Push(Current);
            Current = _forward.Pop();
            return Current;
        }
    }
}
```

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests`
Expected: alle bestanden.

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core tests/FileManager.Core.Tests ScreenshotGallery.sln
git commit -m "Add FileManager.Core with remote paths and navigation history

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 9: Zonen und der `ZoneGuardFtpClient` (plus In-Memory-FTP für alle weiteren Tests)

**Files:**
- Create: `src/FileManager.Core/Zones/Zone.cs`, `src/FileManager.Core/Zones/ZonePolicy.cs`, `src/FileManager.Core/Zones/ZoneViolationException.cs`, `src/FileManager.Core/Zones/ZoneGuardFtpClient.cs`
- Create: `tests/FileManager.Core.Tests/Support/InMemoryFtpClient.cs`
- Test: `tests/FileManager.Core.Tests/ZonePolicyTests.cs`, `tests/FileManager.Core.Tests/ZoneGuardFtpClientTests.cs`

**Interfaces:**
- Consumes: `RemotePath` (Task 8), `IFtpClient`, `FtpException` (Task 4/6).
- Produces:
  - `enum Zone { Free = 0, Warn = 1, Locked = 2 }` (Zahlwert = Strenge)
  - `ZonePolicy`: `ExpertMode` (bool), `Classify(string path) : Zone` (roh), `Effective(string path) : Zone` (Expertenmodus angewandt), `Strictest(IEnumerable<string> paths) : Zone`, `static Describe(Zone) : string` („frei"/„Warnung"/„gesperrt").
  - `ZoneViolationException(string path)` mit `Path`.
  - `ZoneGuardFtpClient(IFtpClient inner, ZonePolicy policy) : IFtpClient` — Lesen reicht durch; `UploadFromFileAsync`, `AppendFromFileAsync`, `MakeDirectoryAsync`, `RemoveDirectoryAsync`, `DeleteAsync` prüfen ihren Pfad, `RenameAsync` beide; `Effective == Locked` → `ZoneViolationException`, **bevor** der innere Client gerufen wird.
  - Testprojekt: `InMemoryFtpClient` (siehe Code) — `AddDir`, `AddFile(path, byte[])`, `AddFile(path, string)`, `File(path)`, `HasDir(path)`, `Modified(path)`, `ChangeRemotely(path, byte[])`, `CallsSnapshot()`, `WriteCallCount`, `Failure` (Func<string verb, string path, Exception>), `InterruptAfterBytes` (long), `RenameOverwrites` (bool).

- [ ] **Step 1: In-Memory-FTP schreiben**

`tests/FileManager.Core.Tests/Support/InMemoryFtpClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace FileManager.Core.Tests.Support
{
    /// <summary>
    /// Dateisystem der Konsole im Speicher, mit Protokoll jedes Aufrufs
    /// („VERB /pfad"). Verhält sich wie GoldHEN, soweit erhoben: fehlende
    /// Pfade → permanente 550, RMD nur auf leere Ordner, RNTO überschreibt
    /// eine existierende Zieldatei kommentarlos (Befund Task 2 Nr. 13; mit
    /// RenameOverwrites = false stattdessen 550).
    /// </summary>
    internal sealed class InMemoryFtpClient : IFtpClient
    {
        private static readonly string[] WriteVerbs = { "STOR", "APPE", "MKD", "RMD", "RNFR", "DELE" };
        private const int Chunk = 64 * 1024;

        private readonly object _lock = new object();
        private readonly HashSet<string> _dirs = new HashSet<string>(StringComparer.Ordinal) { "/" };
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _modified = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly List<string> _calls = new List<string>();

        /// <summary>(Verb, Pfad) → Ausnahme, die statt der Ausführung fliegt; null = normal.</summary>
        public Func<string, string, Exception> Failure;

        /// <summary>Transfers brechen nach so vielen Bytes (je Aufruf) transient ab; 0 = nie.</summary>
        public long InterruptAfterBytes;

        /// <summary>Wie GoldHEN (Befund Task 2 Nr. 13): Standard true.</summary>
        public bool RenameOverwrites = true;

        public DateTime Today = new DateTime(2026, 9, 11);

        public InMemoryFtpClient AddDir(string path)
        {
            lock (_lock) AddDirUnlocked(Norm(path));
            return this;
        }

        public InMemoryFtpClient AddFile(string path, byte[] data)
        {
            lock (_lock)
            {
                var p = Norm(path);
                AddDirUnlocked(Parent(p));
                _files[p] = data;
                _modified[p] = Today;
            }
            return this;
        }

        public InMemoryFtpClient AddFile(string path, string text)
        {
            return AddFile(path, Encoding.UTF8.GetBytes(text));
        }

        public byte[] File(string path)
        {
            lock (_lock)
            {
                byte[] data;
                return _files.TryGetValue(Norm(path), out data) ? data : null;
            }
        }

        public bool HasDir(string path)
        {
            lock (_lock) return _dirs.Contains(Norm(path));
        }

        public DateTime Modified(string path)
        {
            lock (_lock) return _modified[Norm(path)];
        }

        /// <summary>Simuliert eine Änderung auf der Konsole durch jemand anderen.</summary>
        public void ChangeRemotely(string path, byte[] data)
        {
            lock (_lock)
            {
                var p = Norm(path);
                _files[p] = data;
                _modified[p] = _modified[p].AddDays(1);
            }
        }

        public string[] CallsSnapshot()
        {
            lock (_lock) return _calls.ToArray();
        }

        public int WriteCallCount
        {
            get { lock (_lock) return _calls.Count(c => WriteVerbs.Contains(c.Substring(0, c.IndexOf(' ')))); }
        }

        private void Record(string verb, string path)
        {
            lock (_lock) _calls.Add(verb + " " + path);
            var failure = Failure;
            var ex = failure != null ? failure(verb, Norm(path)) : null;
            if (ex != null) throw ex;
        }

        private static FtpException NotFound()
        {
            return new FtpException("Nicht vorhanden oder kein Zugriff", FtpErrorKind.Permanent, 550);
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var p = Norm(path);
            Record("LIST", p);
            lock (_lock)
            {
                if (!_dirs.Contains(p)) throw NotFound();
                var entries = new List<FtpEntry>();
                foreach (var d in _dirs.Where(d => d != "/" && Parent(d) == p).OrderBy(d => d, StringComparer.Ordinal))
                    entries.Add(new FtpEntry(Name(d), FtpEntryKind.Directory, 512, Today, null));
                foreach (var f in _files.Keys.Where(f => Parent(f) == p).OrderBy(f => f, StringComparer.Ordinal))
                    entries.Add(new FtpEntry(Name(f), FtpEntryKind.File, _files[f].Length, _modified[f], null));
                return Task.FromResult<IReadOnlyList<FtpEntry>>(entries);
            }
        }

        public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Record("RETR", Norm(path));
            var data = File(path);
            if (data == null) throw NotFound();
            return Task.FromResult(data);
        }

        public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct)
        {
            return DownloadToFileAsync(path, localPath, 0, progress, ct);
        }

        public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Record("RETR", Norm(path) + "@" + offset);
            var data = File(path);
            if (data == null) throw NotFound();
            if (offset > 0 && (!System.IO.File.Exists(localPath) || new FileInfo(localPath).Length != offset))
                throw new InvalidOperationException("Teildatei passt nicht zum Versatz.");

            using (var target = new FileStream(localPath, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
            {
                CopyChunked(data, offset, target, progress, ct);
            }
            return Task.FromResult(0);
        }

        public Stream OpenRead(string path, long offset)
        {
            Record("RETR", Norm(path) + "@" + offset);
            var data = File(path);
            if (data == null) throw NotFound();
            return new MemoryStream(data, (int)offset, data.Length - (int)offset, false);
        }

        public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            return StoreAsync("STOR", localPath, 0, remotePath, progress, ct);
        }

        public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            return StoreAsync("APPE", localPath, localOffset, remotePath, progress, ct);
        }

        private Task StoreAsync(string verb, string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var local = System.IO.File.ReadAllBytes(localPath);   // lokaler Fehler bleibt lokal, wie im echten Client
            var p = Norm(remotePath);
            Record(verb, verb == "APPE" ? p + "@" + localOffset : p);
            lock (_lock)
            {
                if (!_dirs.Contains(Parent(p)) || _dirs.Contains(p)) throw NotFound();
            }

            using (var received = new MemoryStream())
            {
                // Wie ein echter Server: was bis zum Abbruch ankam, liegt auf der Konsole.
                Exception interrupted = null;
                try { CopyChunked(local, localOffset, received, progress, ct); }
                catch (Exception ex) when (ex is FtpException || ex is OperationCanceledException) { interrupted = ex; }

                lock (_lock)
                {
                    byte[] existing;
                    var bytes = received.ToArray();
                    _files[p] = verb == "APPE" && _files.TryGetValue(p, out existing) ? existing.Concat(bytes).ToArray() : bytes;
                    _modified[p] = Today;
                }
                if (interrupted != null) throw interrupted;
            }
            return Task.FromResult(0);
        }

        private void CopyChunked(byte[] data, long offset, Stream target, IProgress<long> progress, CancellationToken ct)
        {
            long done = 0;
            for (long pos = offset; pos < data.Length; pos += Chunk)
            {
                ct.ThrowIfCancellationRequested();
                int count = (int)Math.Min(Chunk, data.Length - pos);
                if (InterruptAfterBytes > 0 && done + count > InterruptAfterBytes)
                {
                    int partial = (int)(InterruptAfterBytes - done);
                    target.Write(data, (int)pos, partial);
                    if (progress != null) progress.Report(done + partial);
                    throw new FtpException("Verbindung zur PS4 unterbrochen", FtpErrorKind.Transient, 426);
                }
                target.Write(data, (int)pos, count);
                done += count;
                if (progress != null) progress.Report(done);
            }
        }

        public Task<long> GetFileSizeAsync(string path, CancellationToken ct)
        {
            Record("SIZE", Norm(path));
            var data = File(path);
            if (data == null) throw NotFound();
            return Task.FromResult((long)data.Length);
        }

        public Task MakeDirectoryAsync(string path, CancellationToken ct)
        {
            var p = Norm(path);
            Record("MKD", p);
            lock (_lock)
            {
                if (!_dirs.Contains(Parent(p)) || _dirs.Contains(p) || _files.ContainsKey(p)) throw NotFound();
                _dirs.Add(p);
            }
            return Task.FromResult(0);
        }

        public Task RemoveDirectoryAsync(string path, CancellationToken ct)
        {
            var p = Norm(path);
            Record("RMD", p);
            lock (_lock)
            {
                bool empty = !_dirs.Any(d => d != p && Parent(d) == p) && !_files.Keys.Any(f => Parent(f) == p);
                if (p == "/" || !_dirs.Contains(p) || !empty) throw NotFound();
                _dirs.Remove(p);
            }
            return Task.FromResult(0);
        }

        public Task RenameAsync(string fromPath, string toPath, CancellationToken ct)
        {
            var from = Norm(fromPath);
            var to = Norm(toPath);
            Record("RNFR", from + " -> " + to);
            lock (_lock)
            {
                if (!_dirs.Contains(Parent(to))) throw NotFound();
                bool targetExists = _files.ContainsKey(to) || _dirs.Contains(to);
                if (targetExists && !(RenameOverwrites && _files.ContainsKey(to) && _files.ContainsKey(from))) throw NotFound();

                if (_files.ContainsKey(from))
                {
                    _files[to] = _files[from];
                    _modified[to] = _modified[from];
                    _files.Remove(from);
                    _modified.Remove(from);
                }
                else if (_dirs.Contains(from) && from != "/")
                {
                    foreach (var d in _dirs.Where(d => d == from || d.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                    {
                        _dirs.Remove(d);
                        _dirs.Add(to + d.Substring(from.Length));
                    }
                    foreach (var f in _files.Keys.Where(f => f.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                    {
                        _files[to + f.Substring(from.Length)] = _files[f];
                        _modified[to + f.Substring(from.Length)] = _modified[f];
                        _files.Remove(f);
                        _modified.Remove(f);
                    }
                }
                else throw NotFound();
            }
            return Task.FromResult(0);
        }

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            var p = Norm(path);
            Record("DELE", p);
            lock (_lock)
            {
                if (!_files.Remove(p)) throw NotFound();
                _modified.Remove(p);
            }
            return Task.FromResult(0);
        }

        private void AddDirUnlocked(string path)
        {
            if (path == "/") return;
            AddDirUnlocked(Parent(path));
            _dirs.Add(path);
        }

        private static string Norm(string path) { return FileManager.Core.Paths.RemotePath.Normalize(path); }
        private static string Parent(string path) { return FileManager.Core.Paths.RemotePath.Parent(path); }
        private static string Name(string path) { return FileManager.Core.Paths.RemotePath.Name(path); }
    }
}
```

Hinweis: Task 2 Nr. 13 hat ergeben, dass GoldHENs `RNTO` ein existierendes Ziel **überschreibt** — darum ist `RenameOverwrites` standardmäßig `true`. Die Konfliktprüfung vor dem Umbenennen (Tasks 14/15) bleibt trotzdem Pflicht, gerade weil der Server nichts verhindert.

- [ ] **Step 2: Failing tests schreiben**

`tests/FileManager.Core.Tests/ZonePolicyTests.cs`:

```csharp
using FileManager.Core.Zones;
using Xunit;

namespace FileManager.Core.Tests
{
    public class ZonePolicyTests
    {
        [Theory]
        // frei
        [InlineData("/data/x", Zone.Free)]
        [InlineData("/data/GoldHEN/plugins.ini", Zone.Free)]
        [InlineData("/user/data/pkg/a.pkg", Zone.Free)]
        [InlineData("/mnt/usb0/x", Zone.Free)]
        [InlineData("/mnt/usb7/a/b", Zone.Free)]
        [InlineData("/user/av_contents/photo/x.jpg", Zone.Free)]
        // Warnung
        [InlineData("/user/home/194450ad/savedata/x", Zone.Warn)]
        [InlineData("/user/home/194450ad", Zone.Warn)]
        [InlineData("/user/app/CUSA00411", Zone.Warn)]
        [InlineData("/user/patch/x", Zone.Warn)]
        [InlineData("/user/addcont/x", Zone.Warn)]
        [InlineData("/user/appmeta/x", Zone.Warn)]
        [InlineData("/mnt/ext0/user/app/CUSA23827", Zone.Warn)]
        // gesperrt — die Wurzeln der Zonen selbst gehören dazu
        [InlineData("/", Zone.Locked)]
        [InlineData("/data", Zone.Locked)]
        [InlineData("/user/data", Zone.Locked)]
        [InlineData("/mnt/usb0", Zone.Locked)]
        [InlineData("/mnt/usb8/x", Zone.Locked)]
        [InlineData("/user/av_contents", Zone.Locked)]
        [InlineData("/user/home", Zone.Locked)]
        [InlineData("/system/x", Zone.Locked)]
        [InlineData("/system_ex/x", Zone.Locked)]
        [InlineData("/system_data/priv/x", Zone.Locked)]
        [InlineData("/update/PS4UPDATE.PUP", Zone.Locked)]
        [InlineData("/preinst/x", Zone.Locked)]
        [InlineData("/preinst2/x", Zone.Locked)]
        [InlineData("/dev/x", Zone.Locked)]
        [InlineData("/mnt/sandbox/x", Zone.Locked)]
        [InlineData("/mnt/pfs/x", Zone.Locked)]
        [InlineData("/mnt/disc/x", Zone.Locked)]
        [InlineData("/mnt/ext0/x", Zone.Locked)]
        [InlineData("/mnt/ext0/system_data/x", Zone.Locked)]
        [InlineData("/user/settings/x", Zone.Locked)]
        [InlineData("/user/license/x", Zone.Locked)]
        [InlineData("/user/system/x", Zone.Locked)]
        [InlineData("/mini-syscore.elf", Zone.Locked)]
        public void The_zone_table_of_the_spec_holds(string path, Zone expected)
        {
            Assert.Equal(expected, new ZonePolicy().Classify(path));
        }

        [Theory]
        [InlineData("/data/../system/x", Zone.Locked)]
        [InlineData("//system/x", Zone.Locked)]
        [InlineData("/data/./x", Zone.Free)]
        [InlineData("/data/x/../../update/y", Zone.Locked)]
        [InlineData("data/x", Zone.Free)]
        [InlineData("/user/data/../system_data/x", Zone.Locked)]
        public void Tricks_with_the_path_do_not_get_through(string path, Zone expected)
        {
            Assert.Equal(expected, new ZonePolicy().Classify(path));
        }

        [Fact]
        public void Expert_mode_turns_locked_into_warn_and_leaves_the_rest()
        {
            var policy = new ZonePolicy { ExpertMode = true };
            Assert.Equal(Zone.Warn, policy.Effective("/system_data/x"));
            Assert.Equal(Zone.Free, policy.Effective("/data/x"));
            Assert.Equal(Zone.Warn, policy.Effective("/user/home/x"));
            Assert.Equal(Zone.Locked, policy.Classify("/system_data/x"));
        }

        [Fact]
        public void The_strictest_zone_wins()
        {
            var policy = new ZonePolicy();
            Assert.Equal(Zone.Free, policy.Strictest(new[] { "/data/a", "/mnt/usb0/b" }));
            Assert.Equal(Zone.Warn, policy.Strictest(new[] { "/data/a", "/user/home/x/y" }));
            Assert.Equal(Zone.Locked, policy.Strictest(new[] { "/data/a", "/user/home/x/y", "/system/z" }));
            Assert.Equal(Zone.Free, policy.Strictest(new string[0]));
        }

        [Fact]
        public void Zones_have_german_names()
        {
            Assert.Equal("frei", ZonePolicy.Describe(Zone.Free));
            Assert.Equal("Warnung", ZonePolicy.Describe(Zone.Warn));
            Assert.Equal("gesperrt", ZonePolicy.Describe(Zone.Locked));
        }
    }
}
```

`tests/FileManager.Core.Tests/ZoneGuardFtpClientTests.cs`:

```csharp
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
        public void The_violation_explains_itself_in_german()
        {
            var ex = new ZoneViolationException("/system/x");
            Assert.Contains("Gesperrter Bereich", ex.Message);
            Assert.Contains("/system/x", ex.Message);
            Assert.Contains("Expertenmodus", ex.Message);
        }
    }
}
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~Zone"`
Expected: Build-Fehler — `ZonePolicy`, `Zone`, `ZoneGuardFtpClient`, `ZoneViolationException` fehlen.

- [ ] **Step 4: Implementieren**

`src/FileManager.Core/Zones/Zone.cs`:

```csharp
namespace FileManager.Core.Zones
{
    /// <summary>Der Zahlwert ist die Strenge — Max() ergibt die strengste Zone.</summary>
    public enum Zone
    {
        Free = 0,
        Warn = 1,
        Locked = 2
    }
}
```

`src/FileManager.Core/Zones/ZonePolicy.cs`:

```csharp
using System.Collections.Generic;
using FileManager.Core.Paths;

namespace FileManager.Core.Zones
{
    /// <summary>
    /// Freigabeliste nach Spec „Zonen": Was nicht ausdrücklich frei oder
    /// Warnzone ist, ist gesperrt. Eine Regel trifft nur, was UNTERHALB ihrer
    /// Wurzel liegt — /data selbst zu löschen ist gesperrt, darin zu arbeiten
    /// frei. Lesen ist immer erlaubt; die Zonen gelten nur für Schreibzugriffe.
    /// Pfadbasiert, weil die Rechteangaben des Servers nicht stimmen (/system
    /// meldet 0777).
    /// </summary>
    public sealed class ZonePolicy
    {
        private static readonly string[] FreeRoots =
        {
            "/data",
            "/mnt/usb0", "/mnt/usb1", "/mnt/usb2", "/mnt/usb3",
            "/mnt/usb4", "/mnt/usb5", "/mnt/usb6", "/mnt/usb7",
            "/user/av_contents"
        };

        private static readonly string[] WarnRoots =
        {
            "/user/home", "/user/app", "/user/patch", "/user/addcont", "/user/appmeta", "/mnt/ext0/user"
        };

        /// <summary>Stuft „gesperrt" zu „Warnung" herab; Warnungen bleiben.</summary>
        public bool ExpertMode { get; set; }

        public Zone Classify(string path)
        {
            var p = RemotePath.Canonical(path);
            foreach (var root in FreeRoots)
                if (RemotePath.IsStrictlyBelow(p, root)) return Zone.Free;
            foreach (var root in WarnRoots)
                if (RemotePath.IsStrictlyBelow(p, root)) return Zone.Warn;
            return Zone.Locked;
        }

        public Zone Effective(string path)
        {
            var zone = Classify(path);
            return ExpertMode && zone == Zone.Locked ? Zone.Warn : zone;
        }

        public Zone Strictest(IEnumerable<string> paths)
        {
            var result = Zone.Free;
            foreach (var path in paths)
            {
                var zone = Effective(path);
                if (zone > result) result = zone;
            }
            return result;
        }

        public static string Describe(Zone zone)
        {
            switch (zone)
            {
                case Zone.Free: return "frei";
                case Zone.Warn: return "Warnung";
                default: return "gesperrt";
            }
        }
    }
}
```

`src/FileManager.Core/Zones/ZoneViolationException.cs`:

```csharp
using System;

namespace FileManager.Core.Zones
{
    public sealed class ZoneViolationException : Exception
    {
        public ZoneViolationException(string path)
            : base("Gesperrter Bereich: " + path + " — Schreiben ist hier nicht erlaubt (der Expertenmodus hebt die Sperre auf).")
        {
            Path = path;
        }

        public string Path { get; }
    }
}
```

`src/FileManager.Core/Zones/ZoneGuardFtpClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace FileManager.Core.Zones
{
    /// <summary>
    /// Die zweite, harte Hälfte des Zonenschutzes (Spec „Durchsetzung
    /// doppelt"): Jeder Schreibaufruf wird geprüft, BEVOR er den Draht
    /// erreicht. Warnzonen lässt er durch — deren Bestätigung holt die
    /// Auftragsplanung ein. Alle Schreibwege der App laufen über diese Klasse.
    /// </summary>
    public sealed class ZoneGuardFtpClient : IFtpClient
    {
        private readonly IFtpClient _inner;
        private readonly ZonePolicy _policy;

        public ZoneGuardFtpClient(IFtpClient inner, ZonePolicy policy)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            _inner = inner;
            _policy = policy;
        }

        private void Check(string path)
        {
            if (_policy.Effective(path) == Zone.Locked) throw new ZoneViolationException(path);
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct) { return _inner.ListAsync(path, ct); }
        public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { return _inner.DownloadAsync(path, ct); }

        public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct)
        {
            return _inner.DownloadToFileAsync(path, localPath, progress, ct);
        }

        public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct)
        {
            return _inner.DownloadToFileAsync(path, localPath, offset, progress, ct);
        }

        public Stream OpenRead(string path, long offset) { return _inner.OpenRead(path, offset); }
        public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { return _inner.GetFileSizeAsync(path, ct); }

        public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            Check(remotePath);
            return _inner.UploadFromFileAsync(localPath, remotePath, progress, ct);
        }

        public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            Check(remotePath);
            return _inner.AppendFromFileAsync(localPath, localOffset, remotePath, progress, ct);
        }

        public Task MakeDirectoryAsync(string path, CancellationToken ct)
        {
            Check(path);
            return _inner.MakeDirectoryAsync(path, ct);
        }

        public Task RemoveDirectoryAsync(string path, CancellationToken ct)
        {
            Check(path);
            return _inner.RemoveDirectoryAsync(path, ct);
        }

        public Task RenameAsync(string fromPath, string toPath, CancellationToken ct)
        {
            Check(fromPath);
            Check(toPath);
            return _inner.RenameAsync(fromPath, toPath, ct);
        }

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            Check(path);
            return _inner.DeleteAsync(path, ct);
        }
    }
}
```

Die Prüfungen werfen **synchron**, bevor ein Task entsteht. Das ist gewollt (kein Draht-Aufruf, nicht einmal ein angefangener) und für Aufrufer unschädlich, weil alle Schreibmethoden mit `await` gerufen werden; `Assert.ThrowsAsync` fängt auch synchron geworfene Ausnahmen des Delegaten.

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~Zone"`
Expected: alle bestanden. Schlägt ein Test mit „No exception was thrown" fehl, fehlt der `Check`-Aufruf in der betreffenden Methode.

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Zones tests/FileManager.Core.Tests
git commit -m "Guard every write to the console with path-based zones

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 10: Verbindungsbudget und Verzeichnis-Cache

**Files:**
- Create: `src/FileManager.Core/Browsing/FtpGates.cs`, `src/FileManager.Core/Browsing/DirectoryCache.cs`
- Modify: `tests/FileManager.Core.Tests/Support/InMemoryFtpClient.cs` (`ListGate`)
- Test: `tests/FileManager.Core.Tests/DirectoryCacheTests.cs`

**Interfaces:**
- Consumes: `IFtpClient`, `RemotePath`, `InMemoryFtpClient` (Task 9).
- Produces:
  - `FtpGates`: Konstanten `TransferSlots` (1), `BrowseSlots` (2), `ShellSlots` (1), `ConnectionLimit` (4); Properties `Transfer`, `Browse` (`SemaphoreSlim`); `static Task<T> RunAsync<T>(SemaphoreSlim gate, Func<Task<T>> operation, CancellationToken ct)`.
  - `DirectoryCache(IFtpClient ftp, FtpGates gates)`: `ListAsync(string path, CancellationToken ct) : Task<IReadOnlyList<FtpEntry>>`, `TryGetCached(string path, out IReadOnlyList<FtpEntry> entries) : bool`, `Invalidate(string path)`, `InvalidateBelow(string path)`, `Clear()`.

**Abweichung von der Spec, bewusst:** Das „Shell-Tor" ist **kein** Semaphor. Explorer gibt die `IStream`s virtueller Dateien nicht verlässlich zurück (COM-`Release` ist aus .NET nicht beobachtbar); ein nie freigegebener Semaphor würde jeden weiteren Drag blockieren. Stattdessen ist der Platz im Verbindungslimit (`ConnectionLimit = 4`) für die Shell eingeplant, und `FtpComStream` (Task 17) schließt seine Verbindung am Dateiende und bei Fehlern selbst. Die App setzt `ServicePointManager.DefaultConnectionLimit = FtpGates.ConnectionLimit` (Plan 3).

- [ ] **Step 1: `InMemoryFtpClient` um ein Listing-Tor erweitern**

In `tests/FileManager.Core.Tests/Support/InMemoryFtpClient.cs` das Feld ergänzen

```csharp
        /// <summary>Solange gesetzt und nicht erfüllt, hängt jedes LIST — für Nebenläufigkeitstests.</summary>
        public TaskCompletionSource<bool> ListGate;
```

und `ListAsync` ersetzen durch:

```csharp
        public async Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var p = Norm(path);
            Record("LIST", p);
            var gate = ListGate;
            if (gate != null) await gate.Task.ConfigureAwait(false);
            lock (_lock)
            {
                if (!_dirs.Contains(p)) throw NotFound();
                var entries = new List<FtpEntry>();
                foreach (var d in _dirs.Where(d => d != "/" && Parent(d) == p).OrderBy(d => d, StringComparer.Ordinal))
                    entries.Add(new FtpEntry(Name(d), FtpEntryKind.Directory, 512, Today, null));
                foreach (var f in _files.Keys.Where(f => Parent(f) == p).OrderBy(f => f, StringComparer.Ordinal))
                    entries.Add(new FtpEntry(Name(f), FtpEntryKind.File, _files[f].Length, _modified[f], null));
                return entries;
            }
        }
```

- [ ] **Step 2: Failing tests schreiben**

`tests/FileManager.Core.Tests/DirectoryCacheTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Tests.Support;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public class DirectoryCacheTests
    {
        private static int Lists(InMemoryFtpClient ftp, string path)
        {
            return ftp.CallsSnapshot().Count(c => c == "LIST " + path);
        }

        [Fact]
        public void The_connection_budget_adds_up()
        {
            var gates = new FtpGates();
            Assert.Equal(4, FtpGates.ConnectionLimit);
            Assert.Equal(FtpGates.ConnectionLimit, gates.Transfer.CurrentCount + gates.Browse.CurrentCount + FtpGates.ShellSlots);
        }

        [Fact]
        public async Task A_second_listing_comes_from_the_cache()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            var cache = new DirectoryCache(ftp, new FtpGates());

            await cache.ListAsync("/data", CancellationToken.None);
            var again = await cache.ListAsync("/data/", CancellationToken.None);

            Assert.Equal("a.txt", again.Single().Name);
            Assert.Equal(1, Lists(ftp, "/data"));
            IReadOnlyList<FtpEntry> cached;
            Assert.True(cache.TryGetCached("/data", out cached));
        }

        [Fact]
        public async Task Concurrent_listings_of_one_folder_share_one_request()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            ftp.ListGate = new TaskCompletionSource<bool>();
            var cache = new DirectoryCache(ftp, new FtpGates());

            var first = cache.ListAsync("/data", CancellationToken.None);
            var second = cache.ListAsync("/data", CancellationToken.None);
            ftp.ListGate.SetResult(true);
            await Task.WhenAll(first, second);

            Assert.Equal(1, Lists(ftp, "/data"));
        }

        [Fact]
        public async Task A_failed_listing_is_not_cached()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            int calls = 0;
            ftp.Failure = (verb, path) => verb == "LIST" && calls++ == 0
                ? new FtpException("weg", FtpErrorKind.Transient, 0)
                : null;
            var cache = new DirectoryCache(ftp, new FtpGates());

            await Assert.ThrowsAsync<FtpException>(() => cache.ListAsync("/data", CancellationToken.None));
            var entries = await cache.ListAsync("/data", CancellationToken.None);

            Assert.Single(entries);
            IReadOnlyList<FtpEntry> ignored;
            Assert.True(cache.TryGetCached("/data", out ignored));
        }

        [Fact]
        public async Task Invalidate_forces_a_fresh_listing()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            var cache = new DirectoryCache(ftp, new FtpGates());
            await cache.ListAsync("/data", CancellationToken.None);

            ftp.AddFile("/data/b.txt", "b");
            cache.Invalidate("/data");
            var entries = await cache.ListAsync("/data", CancellationToken.None);

            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public async Task InvalidateBelow_drops_the_subtree_and_keeps_siblings()
        {
            var ftp = new InMemoryFtpClient().AddDir("/data/a/b").AddDir("/data/c");
            var cache = new DirectoryCache(ftp, new FtpGates());
            await cache.ListAsync("/data/a", CancellationToken.None);
            await cache.ListAsync("/data/a/b", CancellationToken.None);
            await cache.ListAsync("/data/c", CancellationToken.None);

            cache.InvalidateBelow("/data/a");

            IReadOnlyList<FtpEntry> ignored;
            Assert.False(cache.TryGetCached("/data/a", out ignored));
            Assert.False(cache.TryGetCached("/data/a/b", out ignored));
            Assert.True(cache.TryGetCached("/data/c", out ignored));
        }

        [Fact]
        public async Task A_cancelled_waiter_does_not_cancel_the_shared_listing()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            ftp.ListGate = new TaskCompletionSource<bool>();
            var cache = new DirectoryCache(ftp, new FtpGates());

            using (var cts = new CancellationTokenSource())
            {
                var impatient = cache.ListAsync("/data", cts.Token);
                var patient = cache.ListAsync("/data", CancellationToken.None);
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => impatient);

                ftp.ListGate.SetResult(true);
                Assert.Single(await patient);
            }
            Assert.Equal(1, Lists(ftp, "/data"));
        }

        [Fact]
        public async Task At_most_two_listings_run_at_once()
        {
            var ftp = new InMemoryFtpClient().AddDir("/a").AddDir("/b").AddDir("/c");
            ftp.ListGate = new TaskCompletionSource<bool>();
            var cache = new DirectoryCache(ftp, new FtpGates());

            var all = new[] { "/a", "/b", "/c" }.Select(p => cache.ListAsync(p, CancellationToken.None)).ToArray();
            await Task.Delay(100);
            Assert.Equal(2, ftp.CallsSnapshot().Count(c => c.StartsWith("LIST")));

            ftp.ListGate.SetResult(true);
            await Task.WhenAll(all);
            Assert.Equal(3, ftp.CallsSnapshot().Count(c => c.StartsWith("LIST")));
        }
    }
}
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~DirectoryCacheTests`
Expected: Build-Fehler — `FtpGates`, `DirectoryCache` fehlen.

- [ ] **Step 4: Implementieren**

`src/FileManager.Core/Browsing/FtpGates.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileManager.Core.Browsing
{
    /// <summary>
    /// Das Verbindungsbudget des File Managers (Spec „Verbindungslimit"):
    /// eine Übertragung, zwei fürs Durchsuchen (Listings, Titel-Infos), eine
    /// für Explorer beim Lesen virtueller Dateien. Anders als in der Gallery
    /// geht die Rechnung auf: die App setzt
    /// ServicePointManager.DefaultConnectionLimit = ConnectionLimit.
    ///
    /// Die Shell hat bewusst KEINEN Semaphor — siehe FtpComStream.
    /// </summary>
    public sealed class FtpGates
    {
        public const int TransferSlots = 1;
        public const int BrowseSlots = 2;
        public const int ShellSlots = 1;
        public const int ConnectionLimit = TransferSlots + BrowseSlots + ShellSlots;

        public SemaphoreSlim Transfer { get; } = new SemaphoreSlim(TransferSlots, TransferSlots);
        public SemaphoreSlim Browse { get; } = new SemaphoreSlim(BrowseSlots, BrowseSlots);

        public static async Task<T> RunAsync<T>(SemaphoreSlim gate, Func<Task<T>> operation, CancellationToken ct)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
```

`src/FileManager.Core/Browsing/DirectoryCache.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Browsing
{
    /// <summary>
    /// Listings pro Verbindung, geteilt von Baum und Liste. Gleichzeitige
    /// Anfragen nach demselben Ordner teilen sich EIN LIST; ein Wartender, der
    /// abbricht, bricht nur sein Warten ab, nicht das gemeinsame LIST (das ist
    /// durch die 8-s-Frist des FtpClient begrenzt). Fehlschläge werden nicht
    /// gemerkt. FTP meldet fremde Änderungen nicht — invalidiert wird nach
    /// eigenen Operationen und per F5 (Spec „Verzeichnis-Cache").
    /// </summary>
    public sealed class DirectoryCache
    {
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly object _lock = new object();
        private readonly Dictionary<string, Task<IReadOnlyList<FtpEntry>>> _entries =
            new Dictionary<string, Task<IReadOnlyList<FtpEntry>>>(StringComparer.Ordinal);

        public DirectoryCache(IFtpClient ftp, FtpGates gates)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            _ftp = ftp;
            _gates = gates;
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            var key = RemotePath.Normalize(path);
            Task<IReadOnlyList<FtpEntry>> task;
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out task))
                {
                    task = LoadAsync(key);
                    _entries[key] = task;
                    task.ContinueWith(t => Forget(key, t), CancellationToken.None,
                        TaskContinuationOptions.NotOnRanToCompletion, TaskScheduler.Default);
                }
            }
            return WaitAsync(task, ct);
        }

        public bool TryGetCached(string path, out IReadOnlyList<FtpEntry> entries)
        {
            Task<IReadOnlyList<FtpEntry>> task;
            lock (_lock)
            {
                if (_entries.TryGetValue(RemotePath.Normalize(path), out task) && task.Status == TaskStatus.RanToCompletion)
                {
                    entries = task.Result;
                    return true;
                }
            }
            entries = null;
            return false;
        }

        public void Invalidate(string path)
        {
            lock (_lock) _entries.Remove(RemotePath.Normalize(path));
        }

        public void InvalidateBelow(string path)
        {
            lock (_lock)
            {
                foreach (var key in _entries.Keys.Where(k => RemotePath.IsSameOrBelow(k, path)).ToList())
                    _entries.Remove(key);
            }
        }

        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        private Task<IReadOnlyList<FtpEntry>> LoadAsync(string key)
        {
            return FtpGates.RunAsync(_gates.Browse, () => _ftp.ListAsync(key, CancellationToken.None), CancellationToken.None);
        }

        private void Forget(string key, Task<IReadOnlyList<FtpEntry>> failed)
        {
            lock (_lock)
            {
                Task<IReadOnlyList<FtpEntry>> current;
                if (_entries.TryGetValue(key, out current) && current == failed) _entries.Remove(key);
            }
        }

        private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct)
        {
            if (task.IsCompleted || !ct.CanBeCanceled) return await task.ConfigureAwait(false);

            var cancelled = new TaskCompletionSource<bool>();
            using (ct.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
                    throw new OperationCanceledException(ct);
            }
            return await task.ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~DirectoryCacheTests`
Expected: 8 bestanden. Ist `At_most_two_listings_run_at_once` wackelig (100 ms zu kurz auf einer langsamen Maschine), die Wartezeit durch eine Schleife ersetzen, die bis 2 s wartet, bis zwei LIST-Aufrufe protokolliert sind — **nicht** die Erwartung ändern.

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Browsing tests/FileManager.Core.Tests
git commit -m "Share directory listings between tree and list within a connection budget

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 11: PS4-Wissen — Titelkatalog, Benutzername, Schnellzugriff

**Files:**
- Create: `src/FileManager.Core/Ps4/TitleCatalog.cs`, `src/FileManager.Core/Ps4/UserNameReader.cs`, `src/FileManager.Core/Ps4/QuickAccessEntry.cs`, `src/FileManager.Core/Ps4/QuickAccessBuilder.cs`
- Modify: `tests/FileManager.Core.Tests/FileManager.Core.Tests.csproj` (TestData), Create `tests/FileManager.Core.Tests/TestData/param.sfo` (Kopie)
- Test: `tests/FileManager.Core.Tests/TitleCatalogTests.cs`, `tests/FileManager.Core.Tests/QuickAccessBuilderTests.cs`

**Interfaces:**
- Consumes: `TitleInfoLoader`, `TitleInfo` (Task 7), `FtpGates`, `DirectoryCache` (Task 10), `FtpException` (Task 4).
- Produces (Namensraum `FileManager.Core.Ps4`):
  - `TitleCatalog(IFtpClient ftp, FtpGates gates)`: `TryGet(string titleId, out TitleInfo info) : bool`, `GetAsync(string titleId, CancellationToken ct) : Task<TitleInfo>` — Ergebnisse (auch der ID-Rückfall bei fehlender Datei) für die Sitzung gemerkt; Abbruch und Verbindungsfehler nicht.
  - `UserNameReader.Parse(byte[] data) : string` (null = unbrauchbar).
  - `enum QuickAccessKind { GoldHen, Plugins, Cheats, Patches, PkgFolder, Captures, Games, SaveData, Usb, ExtendedStorage, Pinned }`
  - `QuickAccessEntry(string label, string path, QuickAccessKind kind)` mit `Label`, `Path`, `Kind`.
  - `QuickAccessBuilder(DirectoryCache listings, IFtpClient ftp)`: `BuildAsync(IEnumerable<string> pinned, CancellationToken ct) : Task<IReadOnlyList<QuickAccessEntry>>`.

- [ ] **Step 1: Testdaten bereitstellen**

```bash
mkdir -p tests/FileManager.Core.Tests/TestData
cp tests/Ps4.Core.Tests/TestData/param.sfo tests/FileManager.Core.Tests/TestData/param.sfo
```

In `tests/FileManager.Core.Tests/FileManager.Core.Tests.csproj` ergänzen:

```xml
  <ItemGroup>
    <None Include="TestData\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Failing tests schreiben**

`tests/FileManager.Core.Tests/TitleCatalogTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Ps4;
using FileManager.Core.Tests.Support;
using Ps4.Core.Ftp;
using Ps4.Core.Titles;
using Xunit;

namespace FileManager.Core.Tests
{
    public class TitleCatalogTests
    {
        private static InMemoryFtpClient WithGta()
        {
            return new InMemoryFtpClient()
                .AddFile("/system_data/priv/appmeta/CUSA00411/param.sfo", File.ReadAllBytes(Path.Combine("TestData", "param.sfo")))
                .AddFile("/user/appmeta/CUSA00411/icon0.png", new byte[] { 1, 2, 3 });
        }

        [Fact]
        public async Task A_title_is_resolved_once_per_session()
        {
            var ftp = WithGta();
            var catalog = new TitleCatalog(ftp, new FtpGates());

            var first = await catalog.GetAsync("CUSA00411", CancellationToken.None);
            var second = await catalog.GetAsync("CUSA00411", CancellationToken.None);

            Assert.Equal("Grand Theft Auto V", first.Name);
            Assert.Same(first, second);
            Assert.Equal(1, ftp.CallsSnapshot().Count(c => c.EndsWith("param.sfo")));
            TitleInfo known;
            Assert.True(catalog.TryGet("CUSA00411", out known));
        }

        [Fact]
        public async Task A_missing_title_is_remembered_as_its_id()
        {
            var ftp = new InMemoryFtpClient();
            var catalog = new TitleCatalog(ftp, new FtpGates());

            var info = await catalog.GetAsync("CUSA99999", CancellationToken.None);
            await catalog.GetAsync("CUSA99999", CancellationToken.None);

            Assert.Equal("CUSA99999", info.Name);
            Assert.Equal(1, ftp.CallsSnapshot().Count(c => c.EndsWith("param.sfo")));
        }

        [Fact]
        public async Task A_connection_loss_is_not_remembered()
        {
            var ftp = WithGta();
            bool broken = true;
            ftp.Failure = (verb, path) => broken ? new FtpException("weg", FtpErrorKind.Transient, 0) : null;
            var catalog = new TitleCatalog(ftp, new FtpGates());

            await Assert.ThrowsAsync<FtpException>(() => catalog.GetAsync("CUSA00411", CancellationToken.None));
            TitleInfo ignored;
            Assert.False(catalog.TryGet("CUSA00411", out ignored));

            broken = false;
            Assert.Equal("Grand Theft Auto V", (await catalog.GetAsync("CUSA00411", CancellationToken.None)).Name);
        }

        [Fact]
        public async Task A_cancelled_lookup_is_not_remembered()
        {
            var catalog = new TitleCatalog(WithGta(), new FtpGates());
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.GetAsync("CUSA00411", cts.Token));
            }
            TitleInfo ignored;
            Assert.False(catalog.TryGet("CUSA00411", out ignored));
        }
    }
}
```

`tests/FileManager.Core.Tests/QuickAccessBuilderTests.cs`:

```csharp
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Ps4;
using FileManager.Core.Tests.Support;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public class QuickAccessBuilderTests
    {
        /// <summary>Nachbau der am 2026-09-11 erhobenen Konsole (Spec „Dateisystem").</summary>
        private static InMemoryFtpClient RealConsole()
        {
            var name = new byte[17];
            Encoding.UTF8.GetBytes("Spieler_1").CopyTo(name, 0);
            return new InMemoryFtpClient()
                .AddDir("/data/GoldHEN/plugins").AddDir("/data/GoldHEN/cheats").AddDir("/data/GoldHEN/patches")
                .AddFile("/data/GoldHEN/plugins.ini", "[default]")
                .AddDir("/data/pkg")
                .AddDir("/user/av_contents/photo")
                .AddDir("/user/app")                                      // leer — Spiele liegen auf ext0
                .AddDir("/user/home/194450ad/savedata")
                .AddFile("/user/home/194450ad/username.dat", name)
                .AddDir("/mnt/ext0/user/app/CUSA23827")
                .AddDir("/mnt/usb0")                                      // existiert immer, leer
                .AddFile("/mnt/usb1/backup.zip", "x");
        }

        private static Task<System.Collections.Generic.IReadOnlyList<QuickAccessEntry>> Build(InMemoryFtpClient ftp, params string[] pinned)
        {
            return new QuickAccessBuilder(new DirectoryCache(ftp, new FtpGates()), ftp).BuildAsync(pinned, CancellationToken.None);
        }

        [Fact]
        public async Task The_real_console_yields_the_expected_entries_in_order()
        {
            var entries = await Build(RealConsole());

            Assert.Equal(new[]
            {
                "GoldHEN|/data/GoldHEN",
                "Plugins|/data/GoldHEN/plugins",
                "Cheats|/data/GoldHEN/cheats",
                "Patches|/data/GoldHEN/patches",
                "PKG-Ablage|/data/pkg",
                "Aufnahmen|/user/av_contents",
                "Spiele (erweiterter Speicher)|/mnt/ext0/user/app",
                "Spielstände (Spieler_1)|/user/home/194450ad/savedata",
                "USB (usb1)|/mnt/usb1",
                "Erweiterter Speicher|/mnt/ext0"
            }, entries.Select(e => e.Label + "|" + e.Path).ToArray());
        }

        [Fact]
        public async Task Internal_games_show_up_when_the_folder_is_not_empty()
        {
            var ftp = RealConsole().AddDir("/user/app/CUSA00411");
            var entries = await Build(ftp);
            Assert.Contains(entries, e => e.Label == "Spiele (intern)" && e.Path == "/user/app" && e.Kind == QuickAccessKind.Games);
        }

        [Fact]
        public async Task Without_goldhen_there_are_no_goldhen_entries()
        {
            var ftp = new InMemoryFtpClient().AddDir("/data/pkg");
            var entries = await Build(ftp);
            Assert.DoesNotContain(entries, e => e.Kind == QuickAccessKind.GoldHen || e.Kind == QuickAccessKind.Plugins);
            Assert.Contains(entries, e => e.Kind == QuickAccessKind.PkgFolder);
        }

        [Fact]
        public async Task A_user_without_a_readable_name_is_shown_by_id()
        {
            var ftp = new InMemoryFtpClient().AddDir("/user/home/1a2b3c4d/savedata");
            var entries = await Build(ftp);
            Assert.Contains(entries, e => e.Label == "Spielstände (1a2b3c4d)");
        }

        [Fact]
        public async Task Pinned_folders_come_last_named_after_their_folder()
        {
            var entries = await Build(RealConsole(), "/data/InsulinBO3", "/");
            Assert.Equal("InsulinBO3|/data/InsulinBO3", entries[entries.Count - 2].Label + "|" + entries[entries.Count - 2].Path);
            Assert.Equal("/|/", entries.Last().Label + "|" + entries.Last().Path);
            Assert.Equal(QuickAccessKind.Pinned, entries.Last().Kind);
        }

        [Fact]
        public async Task A_connection_loss_fails_loudly_instead_of_yielding_an_empty_list()
        {
            var ftp = RealConsole();
            ftp.Failure = (verb, path) => new FtpException("weg", FtpErrorKind.Transient, 0);
            await Assert.ThrowsAsync<FtpException>(() => Build(ftp));
        }

        [Theory]
        [InlineData(new byte[] { 0x53, 0x70, 0x69, 0x65, 0x6C, 0x65, 0x72, 0x00, 0x00 }, "Spieler")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00 }, null)]
        [InlineData(new byte[] { 0xFF, 0xFE, 0x00 }, null)]
        [InlineData(new byte[] { 0x41, 0x07, 0x42 }, null)]
        [InlineData(new byte[0], null)]
        public void The_user_name_is_read_defensively(byte[] data, string expected)
        {
            Assert.Equal(expected, UserNameReader.Parse(data));
        }
    }
}
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~TitleCatalogTests|FullyQualifiedName~QuickAccessBuilderTests"`
Expected: Build-Fehler — Typen fehlen.

- [ ] **Step 4: Implementieren**

`src/FileManager.Core/Ps4/TitleCatalog.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using Ps4.Core.Ftp;
using Ps4.Core.Titles;

namespace FileManager.Core.Ps4
{
    /// <summary>
    /// Titelnamen und Icons pro Verbindung (Spec „PS4-Wissen"): geladen erst,
    /// wenn ein Spielordner sichtbar wird, über das Durchsuchen-Tor, für die
    /// Sitzung gemerkt. Der ID-Rückfall bei fehlender Datei wird gemerkt, ein
    /// Verbindungsabbruch oder Abbruch (Navigieren weg) nicht.
    /// </summary>
    public sealed class TitleCatalog
    {
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly ConcurrentDictionary<string, TitleInfo> _known =
            new ConcurrentDictionary<string, TitleInfo>(StringComparer.Ordinal);

        public TitleCatalog(IFtpClient ftp, FtpGates gates)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            _ftp = ftp;
            _gates = gates;
        }

        public bool TryGet(string titleId, out TitleInfo info)
        {
            return _known.TryGetValue(titleId, out info);
        }

        public async Task<TitleInfo> GetAsync(string titleId, CancellationToken ct)
        {
            TitleInfo info;
            if (_known.TryGetValue(titleId, out info)) return info;

            return await FtpGates.RunAsync(_gates.Browse, async () =>
            {
                TitleInfo again;
                if (_known.TryGetValue(titleId, out again)) return again;
                var loaded = await TitleInfoLoader.LoadAsync(_ftp, titleId, ct).ConfigureAwait(false);
                _known[titleId] = loaded;
                return loaded;
            }, ct).ConfigureAwait(false);
        }
    }
}
```

`src/FileManager.Core/Ps4/UserNameReader.cs`:

```csharp
using System;
using System.Text;

namespace FileManager.Core.Ps4
{
    /// <summary>
    /// username.dat unter /user/home/&lt;id&gt;: der Name als UTF-8, mit NUL
    /// aufgefüllt (17 Byte an der Konsole — 16 Zeichen Online-ID + NUL;
    /// Befund Task 2 Nr. 20). Alles Unplausible ergibt null — dann zeigt der
    /// Schnellzugriff die ID.
    /// </summary>
    public static class UserNameReader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string Parse(byte[] data)
        {
            if (data == null || data.Length == 0) return null;

            int end = Array.IndexOf(data, (byte)0);
            if (end < 0) end = data.Length;

            string name;
            try
            {
                name = StrictUtf8.GetString(data, 0, end).Trim();
            }
            catch (DecoderFallbackException)
            {
                return null;
            }

            if (name.Length == 0) return null;
            foreach (var c in name)
                if (char.IsControl(c)) return null;
            return name;
        }
    }
}
```

`src/FileManager.Core/Ps4/QuickAccessEntry.cs`:

```csharp
namespace FileManager.Core.Ps4
{
    public enum QuickAccessKind
    {
        GoldHen, Plugins, Cheats, Patches, PkgFolder, Captures, Games, SaveData, Usb, ExtendedStorage, Pinned
    }

    public sealed class QuickAccessEntry
    {
        public QuickAccessEntry(string label, string path, QuickAccessKind kind)
        {
            Label = label;
            Path = path;
            Kind = kind;
        }

        public string Label { get; }
        public string Path { get; }
        public QuickAccessKind Kind { get; }
    }
}
```

`src/FileManager.Core/Ps4/QuickAccessBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Ps4
{
    /// <summary>
    /// Baut den Schnellzugriff aus dem, was auf DIESER Konsole existiert
    /// (Spec „Schnellzugriff"). Fehlt ein Ort, fehlt sein Eintrag still; bricht
    /// dagegen die Verbindung ab, scheitert der Aufbau laut — sonst stünde
    /// ein leerer Schnellzugriff da, als wäre die Konsole leer.
    /// </summary>
    public sealed class QuickAccessBuilder
    {
        private readonly DirectoryCache _listings;
        private readonly IFtpClient _ftp;

        public QuickAccessBuilder(DirectoryCache listings, IFtpClient ftp)
        {
            if (listings == null) throw new ArgumentNullException(nameof(listings));
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            _listings = listings;
            _ftp = ftp;
        }

        public async Task<IReadOnlyList<QuickAccessEntry>> BuildAsync(IEnumerable<string> pinned, CancellationToken ct)
        {
            var result = new List<QuickAccessEntry>();

            var data = await TryListAsync("/data", ct).ConfigureAwait(false);
            if (HasDir(data, "GoldHEN"))
            {
                result.Add(new QuickAccessEntry("GoldHEN", "/data/GoldHEN", QuickAccessKind.GoldHen));
                var goldhen = await TryListAsync("/data/GoldHEN", ct).ConfigureAwait(false);
                if (HasDir(goldhen, "plugins")) result.Add(new QuickAccessEntry("Plugins", "/data/GoldHEN/plugins", QuickAccessKind.Plugins));
                if (HasDir(goldhen, "cheats")) result.Add(new QuickAccessEntry("Cheats", "/data/GoldHEN/cheats", QuickAccessKind.Cheats));
                if (HasDir(goldhen, "patches")) result.Add(new QuickAccessEntry("Patches", "/data/GoldHEN/patches", QuickAccessKind.Patches));
            }
            if (HasDir(data, "pkg")) result.Add(new QuickAccessEntry("PKG-Ablage", "/data/pkg", QuickAccessKind.PkgFolder));

            var user = await TryListAsync("/user", ct).ConfigureAwait(false);
            if (HasDir(user, "av_contents")) result.Add(new QuickAccessEntry("Aufnahmen", "/user/av_contents", QuickAccessKind.Captures));

            if (HasDir(user, "app") && NotEmpty(await TryListAsync("/user/app", ct).ConfigureAwait(false)))
                result.Add(new QuickAccessEntry("Spiele (intern)", "/user/app", QuickAccessKind.Games));

            var mnt = await TryListAsync("/mnt", ct).ConfigureAwait(false);
            bool ext0 = HasDir(mnt, "ext0");
            if (ext0)
            {
                var extUser = await TryListAsync("/mnt/ext0/user", ct).ConfigureAwait(false);
                if (HasDir(extUser, "app") && NotEmpty(await TryListAsync("/mnt/ext0/user/app", ct).ConfigureAwait(false)))
                    result.Add(new QuickAccessEntry("Spiele (erweiterter Speicher)", "/mnt/ext0/user/app", QuickAccessKind.Games));
            }

            if (HasDir(user, "home"))
            {
                var homes = await TryListAsync("/user/home", ct).ConfigureAwait(false) ?? new FtpEntry[0];
                foreach (var home in homes.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal))
                {
                    var content = await TryListAsync("/user/home/" + home.Name, ct).ConfigureAwait(false);
                    if (!HasDir(content, "savedata")) continue;
                    var name = await ReadUserNameAsync(home.Name, ct).ConfigureAwait(false) ?? home.Name;
                    result.Add(new QuickAccessEntry("Spielstände (" + name + ")", "/user/home/" + home.Name + "/savedata", QuickAccessKind.SaveData));
                }
            }

            for (int i = 0; i < 8; i++)
            {
                var usb = "usb" + i;
                if (HasDir(mnt, usb) && NotEmpty(await TryListAsync("/mnt/" + usb, ct).ConfigureAwait(false)))
                    result.Add(new QuickAccessEntry("USB (" + usb + ")", "/mnt/" + usb, QuickAccessKind.Usb));
            }

            if (ext0) result.Add(new QuickAccessEntry("Erweiterter Speicher", "/mnt/ext0", QuickAccessKind.ExtendedStorage));

            foreach (var pin in pinned ?? Enumerable.Empty<string>())
            {
                var path = RemotePath.Normalize(pin);
                var label = path == RemotePath.Root ? RemotePath.Root : RemotePath.Name(path);
                result.Add(new QuickAccessEntry(label, path, QuickAccessKind.Pinned));
            }

            return result;
        }

        /// <summary>Fehlt ein Ort, ist das null; ein Verbindungsabbruch geht durch.</summary>
        private async Task<IReadOnlyList<FtpEntry>> TryListAsync(string path, CancellationToken ct)
        {
            try
            {
                return await _listings.ListAsync(path, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (FtpException ex) when (ex.IsTransient) { throw; }
            catch (Exception) { return null; }
        }

        private async Task<string> ReadUserNameAsync(string id, CancellationToken ct)
        {
            try
            {
                return UserNameReader.Parse(await _ftp.DownloadAsync("/user/home/" + id + "/username.dat", ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { throw; }
            catch (FtpException ex) when (ex.IsTransient) { throw; }
            catch (Exception) { return null; }
        }

        private static bool HasDir(IReadOnlyList<FtpEntry> entries, string name)
        {
            return entries != null && entries.Any(e => e.IsDirectory && e.Name == name);
        }

        private static bool NotEmpty(IReadOnlyList<FtpEntry> entries)
        {
            return entries != null && entries.Count > 0;
        }
    }
}
```

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~TitleCatalogTests|FullyQualifiedName~QuickAccessBuilderTests"`
Expected: alle bestanden. Hat Task 2 Nr. 20 ein anderes Format für `username.dat` ergeben, zuerst `The_user_name_is_read_defensively` an den Befund anpassen (Rot), dann `UserNameReader` (Grün).

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Ps4 tests/FileManager.Core.Tests
git commit -m "Know the console: title names, user names and quick access

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 12: Einstellungen des File Managers

**Files:**
- Create: `src/FileManager.Core/Config/FileManagerSettings.cs`, `src/FileManager.Core/Config/FileManagerSettingsStore.cs`
- Test: `tests/FileManager.Core.Tests/FileManagerSettingsStoreTests.cs`

**Interfaces:**
- Produces (Namensraum `FileManager.Core.Config`):
  - `FileManagerSettings` mit `Ip` (""), `Port` (2121), `ExpertMode` (false), `ViewMode` („Details" | „Tiles", Standard „Details"), `LastPath` („/data"), `PinnedFolders` (`List<string>`), `ColumnWidths` (`Dictionary<string,int>`), `TreeWidth` (260).
  - `FileManagerSettingsStore.Load(string path) : FileManagerSettings` (fehlend/korrupt → Standardwerte, nie Ausnahme), `Save(string path, FileManagerSettings settings)` (atomar über `.tmp`).

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/FileManagerSettingsStoreTests.cs`:

```csharp
using System;
using System.IO;
using FileManager.Core.Config;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class FileManagerSettingsStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fm-settings-" + Guid.NewGuid().ToString("N"));
        private string SettingsPath { get { return Path.Combine(_dir, "settings.json"); } }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        [Fact]
        public void A_missing_file_yields_the_defaults()
        {
            var s = FileManagerSettingsStore.Load(SettingsPath);
            Assert.Equal("", s.Ip);
            Assert.Equal(2121, s.Port);
            Assert.False(s.ExpertMode);
            Assert.Equal("Details", s.ViewMode);
            Assert.Equal("/data", s.LastPath);
            Assert.Empty(s.PinnedFolders);
            Assert.Empty(s.ColumnWidths);
            Assert.Equal(260, s.TreeWidth);
        }

        [Fact]
        public void Everything_survives_a_round_trip()
        {
            var s = new FileManagerSettings
            {
                Ip = "10.10.10.236", Port = 2121, ExpertMode = true, ViewMode = "Tiles",
                LastPath = "/data/GoldHEN", TreeWidth = 300
            };
            s.PinnedFolders.Add("/data/InsulinBO3");
            s.ColumnWidths["Name"] = 320;

            FileManagerSettingsStore.Save(SettingsPath, s);
            var loaded = FileManagerSettingsStore.Load(SettingsPath);

            Assert.Equal("10.10.10.236", loaded.Ip);
            Assert.True(loaded.ExpertMode);
            Assert.Equal("Tiles", loaded.ViewMode);
            Assert.Equal("/data/GoldHEN", loaded.LastPath);
            Assert.Equal(300, loaded.TreeWidth);
            Assert.Equal(new[] { "/data/InsulinBO3" }, loaded.PinnedFolders.ToArray());
            Assert.Equal(320, loaded.ColumnWidths["Name"]);
            Assert.False(File.Exists(SettingsPath + ".tmp"));
        }

        [Fact]
        public void A_corrupt_file_is_a_fresh_start()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SettingsPath, "{ das ist kein json");
            Assert.Equal(2121, FileManagerSettingsStore.Load(SettingsPath).Port);
        }

        [Fact]
        public void Missing_fields_keep_their_defaults()
        {
            // DataContractJsonSerializer ruft keinen Konstruktor auf — ohne
            // OnDeserializing wären Port 0 und PinnedFolders null.
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SettingsPath, "{\"Ip\":\"10.10.10.236\"}");

            var s = FileManagerSettingsStore.Load(SettingsPath);

            Assert.Equal("10.10.10.236", s.Ip);
            Assert.Equal(2121, s.Port);
            Assert.NotNull(s.PinnedFolders);
            Assert.NotNull(s.ColumnWidths);
            Assert.Equal("/data", s.LastPath);
        }

        [Fact]
        public void Nonsense_values_are_repaired()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SettingsPath, "{\"Port\":70000,\"ViewMode\":\"Riesig\",\"TreeWidth\":5,\"LastPath\":null,\"PinnedFolders\":null}");

            var s = FileManagerSettingsStore.Load(SettingsPath);

            Assert.Equal(2121, s.Port);
            Assert.Equal("Details", s.ViewMode);
            Assert.Equal(260, s.TreeWidth);
            Assert.Equal("/data", s.LastPath);
            Assert.NotNull(s.PinnedFolders);
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~FileManagerSettingsStoreTests`
Expected: Build-Fehler — Typen fehlen.

- [ ] **Step 3: Implementieren**

`src/FileManager.Core/Config/FileManagerSettings.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FileManager.Core.Config
{
    /// <summary>
    /// Die Einstellungen des File Managers (Spec „Einstellungen"). Getrennt
    /// von der Gallery — die kennt nur Ip/Port.
    /// </summary>
    [DataContract]
    public sealed class FileManagerSettings
    {
        public const string DetailsView = "Details";
        public const string TilesView = "Tiles";

        public FileManagerSettings()
        {
            ApplyDefaults();
        }

        [DataMember] public string Ip { get; set; }
        [DataMember] public int Port { get; set; }
        [DataMember] public bool ExpertMode { get; set; }
        [DataMember] public string ViewMode { get; set; }
        [DataMember] public string LastPath { get; set; }
        [DataMember] public List<string> PinnedFolders { get; set; }
        [DataMember] public Dictionary<string, int> ColumnWidths { get; set; }
        [DataMember] public int TreeWidth { get; set; }

        /// <summary>
        /// DataContractJsonSerializer ruft KEINEN Konstruktor auf — ohne diesen
        /// Haken hätten in der Datei fehlende Felder 0/null statt ihrer Standards.
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            Ip = "";
            Port = 2121;
            ExpertMode = false;
            ViewMode = DetailsView;
            LastPath = "/data";
            PinnedFolders = new List<string>();
            ColumnWidths = new Dictionary<string, int>();
            TreeWidth = 260;
        }

        internal void Repair()
        {
            if (Ip == null) Ip = "";
            if (Port <= 0 || Port > 65535) Port = 2121;
            if (ViewMode != DetailsView && ViewMode != TilesView) ViewMode = DetailsView;
            if (string.IsNullOrEmpty(LastPath)) LastPath = "/data";
            if (PinnedFolders == null) PinnedFolders = new List<string>();
            if (ColumnWidths == null) ColumnWidths = new Dictionary<string, int>();
            if (TreeWidth < 120) TreeWidth = 260;
        }
    }
}
```

`src/FileManager.Core/Config/FileManagerSettingsStore.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace FileManager.Core.Config
{
    /// <summary>
    /// JSON über DataContractJsonSerializer (im Framework enthalten, kein
    /// NuGet). Korrupte Datei = Neuanfang, nie ein Absturz (Gallery-Regel).
    /// Gespeichert wird über eine .tmp-Datei, damit ein Absturz beim Schreiben
    /// keine halbe Einstellungsdatei hinterlässt.
    /// </summary>
    public static class FileManagerSettingsStore
    {
        public static FileManagerSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new FileManagerSettings();
                using (var stream = File.OpenRead(path))
                {
                    var loaded = Serializer().ReadObject(stream) as FileManagerSettings ?? new FileManagerSettings();
                    loaded.Repair();
                    return loaded;
                }
            }
            catch (Exception)
            {
                return new FileManagerSettings();
            }
        }

        public static void Save(string path, FileManagerSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                Serializer().WriteObject(stream, settings);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(FileManagerSettings),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~FileManagerSettingsStoreTests`
Expected: 5 bestanden. Meldet der Build, dass `System.Runtime.Serialization.Json` fehlt, in `src/FileManager.Core/FileManager.Core.csproj` eine `ItemGroup` mit `<Reference Include="System.Runtime.Serialization" />` ergänzen.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Config tests/FileManager.Core.Tests/FileManagerSettingsStoreTests.cs src/FileManager.Core/FileManager.Core.csproj
git commit -m "Persist the file manager settings as JSON with safe defaults

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 13: Auftragsmodell — Schritte, Aufträge, Konflikte, Namensvergabe

**Files:**
- Create: `src/FileManager.Core/Transfers/UniqueName.cs`, `LocalName.cs`, `RemoteItem.cs`, `TransferStep.cs`, `TransferJob.cs`, `Conflicts.cs` (alle unter `src/FileManager.Core/Transfers/`)
- Create: `tests/FileManager.Core.Tests/Support/TempDir.cs`
- Test: `tests/FileManager.Core.Tests/UniqueNameTests.cs`, `tests/FileManager.Core.Tests/TransferJobTests.cs`

**Interfaces:**
- Consumes: `RemotePath` (Task 8), `FtpEntry` (Task 3).
- Produces (Namensraum `FileManager.Core.Transfers`):
  - `UniqueName.For(string name, bool isDirectory, Func<string, bool> exists) : string` — erster freier Name `name (2).ext`, `name (3).ext` …; Ordner und Namen ohne Endung bekommen das Suffix ans Ende.
  - `LocalName.Sanitize(string name) : string` — macht einen PS4-Namen unter Windows speicherbar (verbotene Zeichen → `_`, keine Punkte/Leerzeichen am Ende, reservierte Gerätenamen wie `CON` bekommen `_` davor). Die Konsole erlaubt Namen, die Windows ablehnt — ohne das scheitert ein Download an einem `:` im Namen.
  - `RemoteItem(string path, bool isDirectory, long size, DateTime modified)`; `Path`, `Name`, `IsDirectory`, `Size`, `Modified`; `static RemoteItem From(string directory, FtpEntry entry)`.
  - `enum StepKind { CreateRemoteFolder, UploadFile, CreateLocalFolder, DownloadFile, DeleteRemoteFile, RemoveRemoteFolder, RenameRemote }`, `enum StepState { Pending, Done, Skipped, Failed }`
  - `TransferStep(StepKind kind, string remotePath, string localPath, string targetRemotePath, long size)`; Properties `Kind`, `RemotePath`, `LocalPath`, `TargetRemotePath` (die drei mit `internal set` — „Beide behalten" ändert das Ziel), `Size`, `IsDirectory` (internal set), `OverwriteWithoutAsking` (internal set), `State`, `Transferred`, `Error` (je internal set), `CountsAsItem`, `IsTransfer`; intern `Started`.
  - `enum TransferKind { Upload, Download, Delete, CreateFolder, Rename, Move }`, `enum JobState { Waiting, Running, Completed, CompletedWithErrors, Cancelled, Interrupted }`
  - `TransferJob(TransferKind kind, string title, IReadOnlyList<TransferStep> steps)`; `Kind`, `Title`, `Steps`, `State`, `CurrentIndex`, `InterruptReason`, `BytesPerSecond` (je internal set), `TotalBytes`, `DoneBytes`, `Remaining` (`TimeSpan?`), `Failures`, `Summary`, `IsFinished`, `RemoteFoldersToRefresh` und `RemoteSubtreesGone` (für die Cache-Invalidierung nach dem Auftrag: `DirectoryCache.Invalidate` bzw. `InvalidateBelow`); intern `RememberedChoice` (`ConflictChoice?`), `Cancellation` (`CancellationTokenSource`).
  - `enum ConflictChoice { Replace, Skip, KeepBoth }`; `ConflictInfo(string targetPath, bool targetIsRemote, long existingSize, long newSize, string keepBothName)`; `ConflictDecision(ConflictChoice choice, bool applyToAll)`; `interface IConflictResolver { Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct); }`
  - Testprojekt: `TempDir : IDisposable` mit `Path`, `File(string relative, string content) : string`, `Sub(string relative) : string`.

- [ ] **Step 1: Test-Hilfe `TempDir`**

`tests/FileManager.Core.Tests/Support/TempDir.cs`:

```csharp
using System;
using System.IO;

namespace FileManager.Core.Tests.Support
{
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fm-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string relative, string content)
        {
            var full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllText(full, content);
            return full;
        }

        public string Sub(string relative)
        {
            var full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(full);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (Exception) { }
        }
    }
}
```

- [ ] **Step 2: Failing tests schreiben**

`tests/FileManager.Core.Tests/UniqueNameTests.cs`:

```csharp
using System.Collections.Generic;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    public class UniqueNameTests
    {
        private static string For(string name, bool isDirectory, params string[] taken)
        {
            var set = new HashSet<string>(taken);
            return UniqueName.For(name, isDirectory, set.Contains);
        }

        [Fact]
        public void A_free_name_stays()
        {
            Assert.Equal("a.txt", For("a.txt", false));
        }

        [Fact]
        public void The_number_goes_before_the_extension()
        {
            Assert.Equal("plugins (2).ini", For("plugins.ini", false, "plugins.ini"));
            Assert.Equal("plugins (3).ini", For("plugins.ini", false, "plugins.ini", "plugins (2).ini"));
        }

        [Fact]
        public void Only_the_last_extension_counts()
        {
            Assert.Equal("archiv.tar (2).gz", For("archiv.tar.gz", false, "archiv.tar.gz"));
        }

        [Fact]
        public void Names_without_extension_and_dot_files_get_the_number_at_the_end()
        {
            Assert.Equal("README (2)", For("README", false, "README"));
            Assert.Equal(".config (2)", For(".config", false, ".config"));
        }

        [Fact]
        public void Folders_never_split_at_a_dot()
        {
            Assert.Equal("Neuer Ordner (2)", For("Neuer Ordner", true, "Neuer Ordner"));
            Assert.Equal("v1.2 (2)", For("v1.2", true, "v1.2"));
        }

        [Fact]
        public void Case_matters_like_on_the_console()
        {
            Assert.Equal("A.txt", For("A.txt", false, "a.txt"));
        }

        [Theory]
        [InlineData("plugins.ini", "plugins.ini")]
        [InlineData("a:b?.txt", "a_b_.txt")]
        [InlineData("x<y>|z\"", "x_y__z_")]
        [InlineData("name. ", "name")]
        [InlineData("CON", "_CON")]
        [InlineData("con.txt", "_con.txt")]
        [InlineData("COM1", "_COM1")]
        [InlineData("CONSOLE.txt", "CONSOLE.txt")]
        [InlineData("...", "_")]
        public void Console_names_become_windows_safe(string name, string expected)
        {
            Assert.Equal(expected, LocalName.Sanitize(name));
        }
    }
}
```

`tests/FileManager.Core.Tests/TransferJobTests.cs`:

```csharp
using System;
using System.Linq;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    public class TransferJobTests
    {
        private static TransferStep Upload(long size) { return new TransferStep(StepKind.UploadFile, "/data/x", "C:\\x", null, size); }

        [Fact]
        public void Bytes_count_only_transfer_steps()
        {
            var job = new TransferJob(TransferKind.Upload, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/d", null, null, 0),
                Upload(1000), Upload(3000)
            });
            job.Steps[1].State = StepState.Done;
            job.Steps[2].Transferred = 500;

            Assert.Equal(4000, job.TotalBytes);
            Assert.Equal(1500, job.DoneBytes);
        }

        [Fact]
        public void The_remaining_time_follows_the_rate()
        {
            var job = new TransferJob(TransferKind.Upload, "t", new[] { Upload(10000) });
            Assert.Null(job.Remaining);
            job.Steps[0].Transferred = 4000;
            job.BytesPerSecond = 2000;
            Assert.Equal(TimeSpan.FromSeconds(3), job.Remaining);
        }

        [Fact]
        public void The_summary_counts_items_and_names_failures_and_skips()
        {
            var job = new TransferJob(TransferKind.Upload, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/d", null, null, 0),
                Upload(1), Upload(1), Upload(1), Upload(1)
            });
            job.Steps[1].State = StepState.Done;
            job.Steps[2].State = StepState.Done;
            job.Steps[3].State = StepState.Failed;
            job.Steps[4].State = StepState.Skipped;

            Assert.Equal("2 von 4 übertragen, 1 fehlgeschlagen, 1 übersprungen", job.Summary);
            Assert.Single(job.Failures);
        }

        [Fact]
        public void Deleting_speaks_of_deleted_files_and_ignores_folder_steps()
        {
            var job = new TransferJob(TransferKind.Delete, "t", new[]
            {
                new TransferStep(StepKind.DeleteRemoteFile, "/data/a", null, null, 1),
                new TransferStep(StepKind.RemoveRemoteFolder, "/data", null, null, 0)
            });
            job.Steps[0].State = StepState.Done;
            job.Steps[1].State = StepState.Done;

            Assert.Equal("1 von 1 gelöscht", job.Summary);
        }

        [Fact]
        public void A_job_of_only_folder_steps_counts_those()
        {
            var job = new TransferJob(TransferKind.CreateFolder, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/Neuer Ordner", null, null, 0)
            });
            job.Steps[0].State = StepState.Done;
            Assert.Equal("1 von 1 erledigt", job.Summary);
        }

        [Fact]
        public void A_job_names_the_listings_it_makes_stale()
        {
            var upload = new TransferJob(TransferKind.Upload, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/mods", null, null, 0),
                new TransferStep(StepKind.UploadFile, "/data/mods/a.txt", "C:\\a.txt", null, 1)
            });
            Assert.Equal(new[] { "/data", "/data/mods" }, upload.RemoteFoldersToRefresh.OrderBy(p => p).ToArray());
            Assert.Empty(upload.RemoteSubtreesGone);

            var move = new TransferJob(TransferKind.Move, "t", new[]
            {
                new TransferStep(StepKind.RenameRemote, "/data/a", null, "/data/b/a", 0) { IsDirectory = true }
            });
            Assert.Equal(new[] { "/data", "/data/b" }, move.RemoteFoldersToRefresh.OrderBy(p => p).ToArray());
            Assert.Equal(new[] { "/data/a" }, move.RemoteSubtreesGone.ToArray());

            var download = new TransferJob(TransferKind.Download, "t", new[]
            {
                new TransferStep(StepKind.DownloadFile, "/data/x", "C:\\x", null, 1)
            });
            Assert.Empty(download.RemoteFoldersToRefresh);
        }

        [Fact]
        public void Finished_means_completed_or_cancelled_but_not_interrupted()
        {
            var job = new TransferJob(TransferKind.Upload, "t", new[] { Upload(1) });
            Assert.False(job.IsFinished);
            job.State = JobState.Interrupted;
            Assert.False(job.IsFinished);
            job.State = JobState.CompletedWithErrors;
            Assert.True(job.IsFinished);
            job.State = JobState.Cancelled;
            Assert.True(job.IsFinished);
        }
    }
}
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~UniqueNameTests|FullyQualifiedName~TransferJobTests"`
Expected: Build-Fehler — Typen fehlen.

- [ ] **Step 4: Implementieren**

`src/FileManager.Core/Transfers/UniqueName.cs`:

```csharp
using System;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// „Beide behalten" wie im Explorer: name (2).ext, name (3).ext …
    /// Groß-/Kleinschreibung zählt, wie auf der Konsole. Ordner werden nie an
    /// einem Punkt geteilt; Namen mit führendem Punkt gelten als endungslos.
    /// </summary>
    public static class UniqueName
    {
        public static string For(string name, bool isDirectory, Func<string, bool> exists)
        {
            if (!exists(name)) return name;

            int dot = isDirectory ? -1 : name.LastIndexOf('.');
            string stem = dot > 0 ? name.Substring(0, dot) : name;
            string extension = dot > 0 ? name.Substring(dot) : "";

            for (int i = 2; ; i++)
            {
                var candidate = stem + " (" + i + ")" + extension;
                if (!exists(candidate)) return candidate;
            }
        }
    }
}
```

`src/FileManager.Core/Transfers/LocalName.cs`:

```csharp
using System;
using System.IO;
using System.Linq;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// Die Konsole erlaubt Dateinamen, die Windows ablehnt (':', '?', ein Punkt
    /// am Ende, „CON" …). Für jede lokale Kopie — Download, Arbeitskopie zum
    /// Öffnen, virtuelle Datei für Explorer — wird der Name hier entschärft.
    /// </summary>
    public static class LocalName
    {
        private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static string Sanitize(string name)
        {
            var chars = (name ?? "").Select(c => Array.IndexOf(Invalid, c) >= 0 ? '_' : c).ToArray();
            var result = new string(chars).TrimEnd(' ', '.');
            if (result.Length == 0) return "_";

            int dot = result.IndexOf('.');
            var stem = dot >= 0 ? result.Substring(0, dot) : result;
            if (Reserved.Any(r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase))) result = "_" + result;
            return result;
        }
    }
}
```

`src/FileManager.Core/Transfers/RemoteItem.cs`:

```csharp
using System;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Transfers
{
    /// <summary>Ein ausgewählter Eintrag auf der Konsole — mit vollem Pfad statt nur Namen.</summary>
    public sealed class RemoteItem
    {
        public RemoteItem(string path, bool isDirectory, long size, DateTime modified)
        {
            Path = RemotePath.Normalize(path);
            IsDirectory = isDirectory;
            Size = size;
            Modified = modified;
        }

        public string Path { get; }
        public string Name { get { return RemotePath.Name(Path); } }
        public bool IsDirectory { get; }
        public long Size { get; }
        public DateTime Modified { get; }

        public static RemoteItem From(string directory, FtpEntry entry)
        {
            return new RemoteItem(RemotePath.Combine(directory, entry.Name), entry.IsDirectory, entry.Size, entry.Modified);
        }
    }
}
```

`src/FileManager.Core/Transfers/TransferStep.cs`:

```csharp
namespace FileManager.Core.Transfers
{
    public enum StepKind
    {
        CreateRemoteFolder, UploadFile, CreateLocalFolder, DownloadFile, DeleteRemoteFile, RemoveRemoteFolder, RenameRemote
    }

    public enum StepState { Pending, Done, Skipped, Failed }

    /// <summary>
    /// Eine Datei- oder Ordneroperation eines Auftrags. Zielpfade sind
    /// änderbar, weil „Beide behalten" das Ziel erst bei der Ausführung
    /// umbenennt: Upload → RemotePath, Download → LocalPath,
    /// Umbenennen/Verschieben → TargetRemotePath.
    /// </summary>
    public sealed class TransferStep
    {
        public TransferStep(StepKind kind, string remotePath, string localPath, string targetRemotePath, long size)
        {
            Kind = kind;
            RemotePath = remotePath;
            LocalPath = localPath;
            TargetRemotePath = targetRemotePath;
            Size = size;
            State = StepState.Pending;
        }

        public StepKind Kind { get; }
        public string RemotePath { get; internal set; }
        public string LocalPath { get; internal set; }
        public string TargetRemotePath { get; internal set; }
        public long Size { get; }

        /// <summary>Nur für RenameRemote relevant: verschiebt einen Ordner.</summary>
        public bool IsDirectory { get; internal set; }

        /// <summary>Rückschreiben einer geöffneten Datei: ersetzt ohne Konfliktfrage.</summary>
        public bool OverwriteWithoutAsking { get; internal set; }

        public StepState State { get; internal set; }
        public long Transferred { get; internal set; }
        public string Error { get; internal set; }

        /// <summary>
        /// Die Übertragung hat begonnen — eine Teildatei auf dem Ziel ist dann
        /// UNSERE (kein Konflikt beim Fortsetzen), auch wenn noch 0 Byte ankamen.
        /// </summary>
        internal bool Started { get; set; }

        public bool IsTransfer
        {
            get { return Kind == StepKind.UploadFile || Kind == StepKind.DownloadFile; }
        }

        /// <summary>Was in der Bilanz als „Element" zählt — Ordner-Hilfsschritte nicht.</summary>
        public bool CountsAsItem
        {
            get { return IsTransfer || Kind == StepKind.DeleteRemoteFile || Kind == StepKind.RenameRemote; }
        }
    }
}
```

`src/FileManager.Core/Transfers/TransferJob.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FileManager.Core.Paths;

namespace FileManager.Core.Transfers
{
    public enum TransferKind { Upload, Download, Delete, CreateFolder, Rename, Move }

    /// <summary>
    /// Die Phase „wird ermittelt…" (Spec) ist die Laufzeit von
    /// FileOperations.Plan*Async — erst danach existiert ein Auftrag.
    /// </summary>
    public enum JobState { Waiting, Running, Completed, CompletedWithErrors, Cancelled, Interrupted }

    public sealed class TransferJob
    {
        public TransferJob(TransferKind kind, string title, IReadOnlyList<TransferStep> steps)
        {
            Kind = kind;
            Title = title;
            Steps = steps;
            State = JobState.Waiting;
        }

        public TransferKind Kind { get; }
        public string Title { get; }
        public IReadOnlyList<TransferStep> Steps { get; }

        public JobState State { get; internal set; }
        public int CurrentIndex { get; internal set; }
        public string InterruptReason { get; internal set; }
        public double BytesPerSecond { get; internal set; }

        internal ConflictChoice? RememberedChoice { get; set; }
        internal CancellationTokenSource Cancellation { get; set; }

        public long TotalBytes
        {
            get { return Steps.Where(s => s.IsTransfer).Sum(s => s.Size); }
        }

        public long DoneBytes
        {
            get
            {
                return Steps.Where(s => s.IsTransfer)
                    .Sum(s => s.State == StepState.Done ? s.Size : s.Transferred);
            }
        }

        public TimeSpan? Remaining
        {
            get
            {
                if (BytesPerSecond <= 0) return null;
                return TimeSpan.FromSeconds(Math.Max(0, TotalBytes - DoneBytes) / BytesPerSecond);
            }
        }

        public IReadOnlyList<TransferStep> Failures
        {
            get { return Steps.Where(s => s.State == StepState.Failed).ToList(); }
        }

        public bool IsFinished
        {
            get
            {
                return State == JobState.Completed || State == JobState.CompletedWithErrors || State == JobState.Cancelled;
            }
        }

        /// <summary>Ordner, deren Listing nach dem Auftrag veraltet ist (DirectoryCache.Invalidate).</summary>
        public IReadOnlyCollection<string> RemoteFoldersToRefresh
        {
            get
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach (var step in Steps)
                {
                    switch (step.Kind)
                    {
                        case StepKind.UploadFile:
                        case StepKind.CreateRemoteFolder:
                        case StepKind.DeleteRemoteFile:
                        case StepKind.RemoveRemoteFolder:
                            result.Add(RemotePath.Parent(step.RemotePath));
                            break;
                        case StepKind.RenameRemote:
                            result.Add(RemotePath.Parent(step.RemotePath));
                            result.Add(RemotePath.Parent(step.TargetRemotePath));
                            break;
                    }
                }
                return result;
            }
        }

        /// <summary>Teilbäume, die es danach nicht mehr gibt — gelöschte oder verschobene Ordner (DirectoryCache.InvalidateBelow).</summary>
        public IReadOnlyCollection<string> RemoteSubtreesGone
        {
            get
            {
                return Steps
                    .Where(s => s.Kind == StepKind.RemoveRemoteFolder || (s.Kind == StepKind.RenameRemote && s.IsDirectory))
                    .Select(s => RemotePath.Normalize(s.RemotePath))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }

        /// <summary>„118 von 120 übertragen, 2 fehlgeschlagen" (Spec „Fehler").</summary>
        public string Summary
        {
            get
            {
                var counted = Steps.Where(s => s.CountsAsItem).ToList();
                if (counted.Count == 0) counted = Steps.ToList();

                int done = counted.Count(s => s.State == StepState.Done);
                int failed = counted.Count(s => s.State == StepState.Failed);
                int skipped = counted.Count(s => s.State == StepState.Skipped);

                string verb;
                switch (Kind)
                {
                    case TransferKind.Upload:
                    case TransferKind.Download: verb = "übertragen"; break;
                    case TransferKind.Delete: verb = "gelöscht"; break;
                    default: verb = "erledigt"; break;
                }

                var text = done + " von " + counted.Count + " " + verb;
                if (failed > 0) text += ", " + failed + " fehlgeschlagen";
                if (skipped > 0) text += ", " + skipped + " übersprungen";
                return text;
            }
        }
    }
}
```

`src/FileManager.Core/Transfers/Conflicts.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace FileManager.Core.Transfers
{
    public enum ConflictChoice { Replace, Skip, KeepBoth }

    public sealed class ConflictInfo
    {
        public ConflictInfo(string targetPath, bool targetIsRemote, long existingSize, long newSize, string keepBothName)
        {
            TargetPath = targetPath;
            TargetIsRemote = targetIsRemote;
            ExistingSize = existingSize;
            NewSize = newSize;
            KeepBothName = keepBothName;
        }

        public string TargetPath { get; }
        public bool TargetIsRemote { get; }
        public long ExistingSize { get; }
        public long NewSize { get; }

        /// <summary>Der Name, den „Beide behalten" vergäbe — der Dialog zeigt ihn an.</summary>
        public string KeepBothName { get; }
    }

    public sealed class ConflictDecision
    {
        public ConflictDecision(ConflictChoice choice, bool applyToAll)
        {
            Choice = choice;
            ApplyToAll = applyToAll;
        }

        public ConflictChoice Choice { get; }

        /// <summary>„Für alle weiteren Konflikte dieses Auftrags übernehmen".</summary>
        public bool ApplyToAll { get; }
    }

    /// <summary>Die App zeigt den Konfliktdialog; die Warteschlange wartet so lange.</summary>
    public interface IConflictResolver
    {
        Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct);
    }
}
```

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~UniqueNameTests|FullyQualifiedName~TransferJobTests"`
Expected: 22 Testfälle bestanden (13 Facts + 9 Theorie-Fälle).

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Transfers tests/FileManager.Core.Tests
git commit -m "Model transfer jobs, steps, conflicts and Explorer-style unique names

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 14: Aufträge planen — `FileOperations` mit Zonen- und Löschbestätigung

**Files:**
- Create: `src/FileManager.Core/Transfers/IUserPrompts.cs`, `src/FileManager.Core/Transfers/FileOperations.cs`
- Test: `tests/FileManager.Core.Tests/FileOperationsTests.cs`

**Interfaces:**
- Consumes: `IFtpClient`, `FtpGates` (Task 10), `ZonePolicy`, `Zone`, `ZoneViolationException` (Task 9), `RemotePath` (Task 8), alles aus Task 13.
- Produces:
  - `DeleteSummary(int fileCount, int folderCount, long totalBytes, Zone zone, IReadOnlyList<string> topLevelNames)` mit gleichnamigen Properties.
  - `interface IUserPrompts { Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct); Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct); }`
  - `FileOperations(IFtpClient ftp, FtpGates gates, ZonePolicy policy, IUserPrompts prompts)`:
    - `PlanUploadAsync(IReadOnlyList<string> localPaths, string remoteDir, CancellationToken ct) : Task<TransferJob>`
    - `PlanDownloadAsync(IReadOnlyList<RemoteItem> items, string localDir, CancellationToken ct) : Task<TransferJob>`
    - `PlanOpenForEdit(RemoteItem file, string localFilePath) : TransferJob` (ein Download ohne Konfliktfrage)
    - `PlanWriteBackAsync(string localFilePath, string remotePath, CancellationToken ct) : Task<TransferJob>` (ein Upload ohne Konfliktfrage)
    - `PlanDeleteAsync(IReadOnlyList<RemoteItem> items, CancellationToken ct) : Task<TransferJob>`
    - `PlanCreateFolderAsync(string parentDir, CancellationToken ct) : Task<TransferJob>`
    - `PlanRenameAsync(RemoteItem item, string newName, CancellationToken ct) : Task<TransferJob>`
    - `PlanMoveAsync(IReadOnlyList<RemoteItem> items, string targetDir, CancellationToken ct) : Task<TransferJob>`
    - `PlanCopyWithinPs4() : TransferJob` — wirft immer `NotSupportedException(CopyWithinPs4Message)`.
    - Konstanten `CopyWithinPs4Message`, `NewFolderName` („Neuer Ordner").
    - Verträge: `null` = Anwender hat abgelehnt oder es gibt nichts zu tun. `Locked` → `ZoneViolationException`, **ohne** zu fragen. `Warn` → genau eine Frage pro Auftrag (Löschen: eine gemeinsame Frage über `ConfirmDeleteAsync` mit `Zone = Warn`). Listings zum Planen laufen über das Durchsuchen-Tor. Links werden beim rekursiven Herunterladen übersprungen (keine Schleifen) und beim Löschen als Datei entfernt.

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/FileOperationsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Xunit;

namespace FileManager.Core.Tests
{
    internal sealed class RecordingPrompts : IUserPrompts
    {
        public bool Answer = true;
        public readonly List<string> WarnActions = new List<string>();
        public readonly List<DeleteSummary> Deletes = new List<DeleteSummary>();

        public Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct)
        {
            WarnActions.Add(action);
            return Task.FromResult(Answer);
        }

        public Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct)
        {
            Deletes.Add(summary);
            return Task.FromResult(Answer);
        }
    }

    public sealed class FileOperationsTests : IDisposable
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly ZonePolicy _policy = new ZonePolicy();
        private readonly RecordingPrompts _prompts = new RecordingPrompts();
        private readonly TempDir _temp = new TempDir();
        private readonly FileOperations _ops;

        public FileOperationsTests()
        {
            _ops = new FileOperations(_ftp, new FtpGates(), _policy, _prompts);
        }

        public void Dispose()
        {
            _temp.Dispose();
        }

        private static string Describe(TransferStep s)
        {
            return s.Kind + " " + (s.Kind == StepKind.RenameRemote ? s.RemotePath + " -> " + s.TargetRemotePath : s.RemotePath);
        }

        [Fact]
        public async Task Uploading_a_folder_creates_folders_before_their_files()
        {
            _ftp.AddDir("/data");
            var mods = _temp.Sub("mods");
            _temp.File("mods/a.txt", "aaa");
            _temp.File("mods/sub/b.txt", "bb");

            var job = await _ops.PlanUploadAsync(new[] { mods }, "/data", CancellationToken.None);

            Assert.Equal(new[]
            {
                "CreateRemoteFolder /data/mods",
                "UploadFile /data/mods/a.txt",
                "CreateRemoteFolder /data/mods/sub",
                "UploadFile /data/mods/sub/b.txt"
            }, job.Steps.Select(Describe).ToArray());
            Assert.Equal(5, job.TotalBytes);
            Assert.Equal(TransferKind.Upload, job.Kind);
            Assert.Equal("mods nach /data hochladen", job.Title);
            Assert.Empty(_prompts.WarnActions);
        }

        [Fact]
        public async Task Uploading_into_a_warn_zone_asks_once_and_respects_no()
        {
            _ftp.AddDir("/user/home/194450ad/savedata");
            var a = _temp.File("a.bin", "a");
            var b = _temp.File("b.bin", "b");
            _prompts.Answer = false;

            var job = await _ops.PlanUploadAsync(new[] { a, b }, "/user/home/194450ad/savedata", CancellationToken.None);

            Assert.Null(job);
            Assert.Equal(new[] { "Hochladen" }, _prompts.WarnActions.ToArray());
        }

        [Fact]
        public async Task Uploading_into_a_locked_zone_is_refused_without_asking()
        {
            var a = _temp.File("a.bin", "a");
            await Assert.ThrowsAsync<ZoneViolationException>(
                () => _ops.PlanUploadAsync(new[] { a }, "/system_data", CancellationToken.None));
            Assert.Empty(_prompts.WarnActions);
            Assert.Equal(0, _ftp.WriteCallCount);
        }

        [Fact]
        public async Task Downloading_a_folder_mirrors_it_locally()
        {
            _ftp.AddFile("/data/GoldHEN/plugins.ini", "x").AddFile("/data/GoldHEN/plugins/a.prx", "yy");
            var target = _temp.Sub("ziel");

            var job = await _ops.PlanDownloadAsync(
                new[] { new RemoteItem("/data/GoldHEN", true, 0, DateTime.MinValue) }, target, CancellationToken.None);

            Assert.Equal(new[]
            {
                StepKind.CreateLocalFolder + " " + Path.Combine(target, "GoldHEN"),
                StepKind.DownloadFile + " " + Path.Combine(target, "GoldHEN", "plugins.ini"),
                StepKind.CreateLocalFolder + " " + Path.Combine(target, "GoldHEN", "plugins"),
                StepKind.DownloadFile + " " + Path.Combine(target, "GoldHEN", "plugins", "a.prx")
            }, job.Steps.Select(s => s.Kind + " " + s.LocalPath).ToArray());
            Assert.Equal(3, job.TotalBytes);
        }

        [Fact]
        public async Task Downloaded_names_are_made_windows_safe()
        {
            _ftp.AddFile("/data/a:b.txt", "x");
            var target = _temp.Sub("ziel");

            var job = await _ops.PlanDownloadAsync(
                new[] { new RemoteItem("/data/a:b.txt", false, 1, DateTime.MinValue) }, target, CancellationToken.None);

            Assert.Equal(Path.Combine(target, "a_b.txt"), job.Steps.Single().LocalPath);
            Assert.Equal("/data/a:b.txt", job.Steps.Single().RemotePath);
        }

        [Fact]
        public async Task Deleting_a_tree_removes_files_first_and_the_deepest_folder_first()
        {
            _ftp.AddFile("/data/alt/a.txt", "12345").AddFile("/data/alt/sub/b.txt", "12").AddFile("/data/c.txt", "1");

            var job = await _ops.PlanDeleteAsync(new[]
            {
                new RemoteItem("/data/alt", true, 0, DateTime.MinValue),
                new RemoteItem("/data/c.txt", false, 1, DateTime.MinValue)
            }, CancellationToken.None);

            Assert.Equal(new[]
            {
                "DeleteRemoteFile /data/alt/a.txt",
                "DeleteRemoteFile /data/alt/sub/b.txt",
                "RemoveRemoteFolder /data/alt/sub",
                "RemoveRemoteFolder /data/alt",
                "DeleteRemoteFile /data/c.txt"
            }, job.Steps.Select(Describe).ToArray());

            var summary = Assert.Single(_prompts.Deletes);
            Assert.Equal(3, summary.FileCount);
            Assert.Equal(2, summary.FolderCount);
            Assert.Equal(8, summary.TotalBytes);
            Assert.Equal(Zone.Free, summary.Zone);
            Assert.Equal(new[] { "alt", "c.txt" }, summary.TopLevelNames.ToArray());
        }

        [Fact]
        public async Task Deleting_always_asks_and_no_means_no()
        {
            _ftp.AddFile("/data/a.txt", "a");
            _prompts.Answer = false;

            var job = await _ops.PlanDeleteAsync(new[] { new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue) }, CancellationToken.None);

            Assert.Null(job);
            Assert.Single(_prompts.Deletes);
        }

        [Fact]
        public async Task Deleting_in_a_warn_zone_asks_one_combined_question()
        {
            _ftp.AddFile("/user/home/194450ad/savedata/CUSA00411/sdimg_x", "s");

            await _ops.PlanDeleteAsync(new[] { new RemoteItem("/user/home/194450ad/savedata/CUSA00411", true, 0, DateTime.MinValue) },
                CancellationToken.None);

            Assert.Equal(Zone.Warn, Assert.Single(_prompts.Deletes).Zone);
            Assert.Empty(_prompts.WarnActions);
        }

        [Fact]
        public async Task Deleting_in_a_locked_zone_is_refused_without_asking()
        {
            _ftp.AddFile("/system_data/priv/x", "x");
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanDeleteAsync(new[] { new RemoteItem("/system_data/priv/x", false, 1, DateTime.MinValue) }, CancellationToken.None));
            Assert.Empty(_prompts.Deletes);
        }

        [Fact]
        public async Task A_new_folder_gets_a_free_name()
        {
            _ftp.AddDir("/data/Neuer Ordner");

            var job = await _ops.PlanCreateFolderAsync("/data", CancellationToken.None);

            Assert.Equal("CreateRemoteFolder /data/Neuer Ordner (2)", Describe(job.Steps.Single()));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("a/b")]
        [InlineData(".")]
        [InlineData("..")]
        public async Task Invalid_names_are_refused(string name)
        {
            _ftp.AddFile("/data/a.txt", "a");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _ops.PlanRenameAsync(new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue), name, CancellationToken.None));
        }

        [Fact]
        public async Task Renaming_onto_an_existing_name_is_refused()
        {
            _ftp.AddFile("/data/a.txt", "a").AddFile("/data/b.txt", "b");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ops.PlanRenameAsync(new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue), "b.txt", CancellationToken.None));
            Assert.Contains("gibt es hier schon", ex.Message);
        }

        [Fact]
        public async Task Renaming_to_the_same_name_is_nothing_to_do_and_a_real_rename_is_one_step()
        {
            _ftp.AddFile("/data/a.txt", "a");
            var item = new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue);

            Assert.Null(await _ops.PlanRenameAsync(item, "a.txt", CancellationToken.None));
            var job = await _ops.PlanRenameAsync(item, "c.txt", CancellationToken.None);
            Assert.Equal("RenameRemote /data/a.txt -> /data/c.txt", Describe(job.Steps.Single()));
        }

        [Fact]
        public async Task A_folder_cannot_move_into_itself()
        {
            _ftp.AddDir("/data/a/b");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ops.PlanMoveAsync(new[] { new RemoteItem("/data/a", true, 0, DateTime.MinValue) }, "/data/a/b", CancellationToken.None));
        }

        [Fact]
        public async Task Moving_renames_into_the_target_and_skips_items_already_there()
        {
            _ftp.AddFile("/data/a.txt", "a").AddDir("/data/sub").AddDir("/data/ziel");

            var job = await _ops.PlanMoveAsync(new[]
            {
                new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue),
                new RemoteItem("/data/sub", true, 0, DateTime.MinValue),
                new RemoteItem("/data/ziel/x", false, 1, DateTime.MinValue)
            }, "/data/ziel", CancellationToken.None);

            Assert.Equal(new[]
            {
                "RenameRemote /data/a.txt -> /data/ziel/a.txt",
                "RenameRemote /data/sub -> /data/ziel/sub"
            }, job.Steps.Select(Describe).ToArray());
            Assert.True(job.Steps[1].IsDirectory);
        }

        [Fact]
        public async Task Moving_out_of_a_locked_zone_is_refused()
        {
            _ftp.AddFile("/system_data/x", "x").AddDir("/data");
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanMoveAsync(new[] { new RemoteItem("/system_data/x", false, 1, DateTime.MinValue) }, "/data", CancellationToken.None));
        }

        [Fact]
        public void Copying_within_the_console_says_what_to_do_instead()
        {
            var ex = Assert.Throws<NotSupportedException>(() => _ops.PlanCopyWithinPs4());
            Assert.Equal("Kopieren innerhalb der PS4 kommt später. Verschieben geht mit Strg+X.", ex.Message);
        }

        [Fact]
        public async Task Open_and_write_back_never_ask_about_conflicts()
        {
            _ftp.AddFile("/data/GoldHEN/plugins.ini", "x");
            var local = _temp.File("open/plugins.ini", "neu");

            var open = _ops.PlanOpenForEdit(new RemoteItem("/data/GoldHEN/plugins.ini", false, 1, DateTime.MinValue), local);
            var back = await _ops.PlanWriteBackAsync(local, "/data/GoldHEN/plugins.ini", CancellationToken.None);

            Assert.True(open.Steps.Single().OverwriteWithoutAsking);
            Assert.Equal(StepKind.DownloadFile, open.Steps.Single().Kind);
            Assert.True(back.Steps.Single().OverwriteWithoutAsking);
            Assert.Equal(StepKind.UploadFile, back.Steps.Single().Kind);
            Assert.Equal(3, back.TotalBytes);
        }

        [Fact]
        public async Task Writing_back_into_a_locked_file_is_refused()
        {
            var local = _temp.File("open/x.dat", "neu");
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanWriteBackAsync(local, "/system_data/priv/x.dat", CancellationToken.None));
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~FileOperationsTests`
Expected: Build-Fehler — `IUserPrompts`, `DeleteSummary`, `FileOperations` fehlen.

- [ ] **Step 3: Implementieren**

`src/FileManager.Core/Transfers/IUserPrompts.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Zones;

namespace FileManager.Core.Transfers
{
    public sealed class DeleteSummary
    {
        public DeleteSummary(int fileCount, int folderCount, long totalBytes, Zone zone, IReadOnlyList<string> topLevelNames)
        {
            FileCount = fileCount;
            FolderCount = folderCount;
            TotalBytes = totalBytes;
            Zone = zone;
            TopLevelNames = topLevelNames;
        }

        public int FileCount { get; }
        public int FolderCount { get; }
        public long TotalBytes { get; }

        /// <summary>Warn → der Löschdialog trägt zugleich die Zonenwarnung (ein Dialog, nicht zwei).</summary>
        public Zone Zone { get; }

        public IReadOnlyList<string> TopLevelNames { get; }
    }

    /// <summary>Die Rückfragen, die der Kern stellt; die App zeigt sie als Dialoge.</summary>
    public interface IUserPrompts
    {
        /// <summary>Warnzone: eine Frage pro Auftrag. true = fortfahren.</summary>
        Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct);

        /// <summary>„endgültig löschen: 37 Dateien, 1,2 GB" — die PS4 hat keinen Papierkorb.</summary>
        Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct);
    }
}
```

`src/FileManager.Core/Transfers/FileOperations.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// Plant aus einer Anwenderaktion einen Auftrag (Spec „Operationen und
    /// Aufträge"). Hier sitzt die erste Hälfte des Zonenschutzes: gesperrt
    /// wird abgelehnt, ohne zu fragen; Warnzone kostet genau eine Bestätigung
    /// pro Auftrag. Ausgeführt wird erst in der TransferQueue.
    /// </summary>
    public sealed class FileOperations
    {
        public const string CopyWithinPs4Message = "Kopieren innerhalb der PS4 kommt später. Verschieben geht mit Strg+X.";
        public const string NewFolderName = "Neuer Ordner";

        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly ZonePolicy _policy;
        private readonly IUserPrompts _prompts;

        public FileOperations(IFtpClient ftp, FtpGates gates, ZonePolicy policy, IUserPrompts prompts)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (prompts == null) throw new ArgumentNullException(nameof(prompts));
            _ftp = ftp;
            _gates = gates;
            _policy = policy;
            _prompts = prompts;
        }

        public async Task<TransferJob> PlanUploadAsync(IReadOnlyList<string> localPaths, string remoteDir, CancellationToken ct)
        {
            var steps = new List<TransferStep>();
            foreach (var local in localPaths)
            {
                var trimmed = local.TrimEnd('\\', '/');
                var target = RemotePath.Combine(remoteDir, Path.GetFileName(trimmed));
                if (Directory.Exists(trimmed)) AddLocalTree(trimmed, target, steps);
                else if (File.Exists(trimmed)) steps.Add(new TransferStep(StepKind.UploadFile, target, trimmed, null, new FileInfo(trimmed).Length));
                else throw new FileNotFoundException("Lokale Datei nicht gefunden: " + trimmed, trimmed);
            }

            if (!await ApproveAsync("Hochladen", steps.Select(s => s.RemotePath), ct).ConfigureAwait(false)) return null;
            var names = localPaths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).ToList();
            return new TransferJob(TransferKind.Upload, Items(names) + " nach " + RemotePath.Normalize(remoteDir) + " hochladen", steps);
        }

        public async Task<TransferJob> PlanDownloadAsync(IReadOnlyList<RemoteItem> items, string localDir, CancellationToken ct)
        {
            var steps = new List<TransferStep>();
            foreach (var item in items)
            {
                var local = Path.Combine(localDir, LocalName.Sanitize(item.Name));
                if (item.IsDirectory) await AddRemoteTreeForDownloadAsync(item.Path, local, steps, ct).ConfigureAwait(false);
                else steps.Add(new TransferStep(StepKind.DownloadFile, item.Path, local, null, item.Size));
            }
            return new TransferJob(TransferKind.Download, Items(items.Select(i => i.Name).ToList()) + " herunterladen", steps);
        }

        public TransferJob PlanOpenForEdit(RemoteItem file, string localFilePath)
        {
            var step = new TransferStep(StepKind.DownloadFile, file.Path, localFilePath, null, file.Size) { OverwriteWithoutAsking = true };
            return new TransferJob(TransferKind.Download, file.Name + " öffnen", new[] { step });
        }

        public async Task<TransferJob> PlanWriteBackAsync(string localFilePath, string remotePath, CancellationToken ct)
        {
            var target = RemotePath.Normalize(remotePath);
            if (!await ApproveAsync("Zurückschreiben", new[] { target }, ct).ConfigureAwait(false)) return null;
            var step = new TransferStep(StepKind.UploadFile, target, localFilePath, null, new FileInfo(localFilePath).Length)
            {
                OverwriteWithoutAsking = true
            };
            return new TransferJob(TransferKind.Upload, RemotePath.Name(target) + " zurückschreiben", new[] { step });
        }

        public async Task<TransferJob> PlanDeleteAsync(IReadOnlyList<RemoteItem> items, CancellationToken ct)
        {
            var steps = new List<TransferStep>();
            foreach (var item in items)
            {
                if (item.IsDirectory) await AddRemoteTreeForDeleteAsync(item.Path, steps, ct).ConfigureAwait(false);
                else steps.Add(new TransferStep(StepKind.DeleteRemoteFile, item.Path, null, null, item.Size));
            }

            var paths = steps.Select(s => s.RemotePath).ToList();
            var zone = _policy.Strictest(paths);
            if (zone == Zone.Locked) throw new ZoneViolationException(paths.First(p => _policy.Effective(p) == Zone.Locked));

            var summary = new DeleteSummary(
                steps.Count(s => s.Kind == StepKind.DeleteRemoteFile),
                steps.Count(s => s.Kind == StepKind.RemoveRemoteFolder),
                steps.Where(s => s.Kind == StepKind.DeleteRemoteFile).Sum(s => s.Size),
                zone,
                items.Select(i => i.Name).ToList());
            if (!await _prompts.ConfirmDeleteAsync(summary, ct).ConfigureAwait(false)) return null;

            return new TransferJob(TransferKind.Delete, Items(items.Select(i => i.Name).ToList()) + " löschen", steps);
        }

        public async Task<TransferJob> PlanCreateFolderAsync(string parentDir, CancellationToken ct)
        {
            var entries = await ListGatedAsync(parentDir, ct).ConfigureAwait(false);
            var name = UniqueName.For(NewFolderName, true, n => entries.Any(e => e.Name == n));
            var path = RemotePath.Combine(parentDir, name);
            if (!await ApproveAsync("Neuer Ordner", new[] { path }, ct).ConfigureAwait(false)) return null;
            return new TransferJob(TransferKind.CreateFolder, "Ordner „" + name + "“ anlegen",
                new[] { new TransferStep(StepKind.CreateRemoteFolder, path, null, null, 0) });
        }

        public async Task<TransferJob> PlanRenameAsync(RemoteItem item, string newName, CancellationToken ct)
        {
            ValidateName(newName);
            var parent = RemotePath.Parent(item.Path);
            var target = RemotePath.Combine(parent, newName);
            if (target == item.Path) return null;

            var entries = await ListGatedAsync(parent, ct).ConfigureAwait(false);
            if (entries.Any(e => e.Name == newName))
                throw new InvalidOperationException("Ein Element mit dem Namen „" + newName + "“ gibt es hier schon.");

            if (!await ApproveAsync("Umbenennen", new[] { item.Path, target }, ct).ConfigureAwait(false)) return null;
            var step = new TransferStep(StepKind.RenameRemote, item.Path, null, target, item.Size) { IsDirectory = item.IsDirectory };
            return new TransferJob(TransferKind.Rename, "„" + item.Name + "“ in „" + newName + "“ umbenennen", new[] { step });
        }

        public async Task<TransferJob> PlanMoveAsync(IReadOnlyList<RemoteItem> items, string targetDir, CancellationToken ct)
        {
            var target = RemotePath.Normalize(targetDir);
            var steps = new List<TransferStep>();
            foreach (var item in items)
            {
                if (item.IsDirectory && RemotePath.IsSameOrBelow(target, item.Path))
                    throw new InvalidOperationException("Ein Ordner kann nicht in sich selbst verschoben werden.");
                var destination = RemotePath.Combine(target, item.Name);
                if (destination == item.Path) continue;
                steps.Add(new TransferStep(StepKind.RenameRemote, item.Path, null, destination, item.Size) { IsDirectory = item.IsDirectory });
            }
            if (steps.Count == 0) return null;

            var paths = steps.Select(s => s.RemotePath).Concat(steps.Select(s => s.TargetRemotePath));
            if (!await ApproveAsync("Verschieben", paths, ct).ConfigureAwait(false)) return null;
            return new TransferJob(TransferKind.Move, Items(steps.Select(s => RemotePath.Name(s.RemotePath)).ToList()) + " nach " + target + " verschieben", steps);
        }

        public TransferJob PlanCopyWithinPs4()
        {
            throw new NotSupportedException(CopyWithinPs4Message);
        }

        private async Task<bool> ApproveAsync(string action, IEnumerable<string> paths, CancellationToken ct)
        {
            var list = paths.ToList();
            var zone = _policy.Strictest(list);
            if (zone == Zone.Locked) throw new ZoneViolationException(list.First(p => _policy.Effective(p) == Zone.Locked));
            if (zone == Zone.Warn)
                return await _prompts.ConfirmWarnZoneAsync(action, list.Where(p => _policy.Effective(p) == Zone.Warn).ToList(), ct).ConfigureAwait(false);
            return true;
        }

        private static void AddLocalTree(string localDir, string remoteDir, List<TransferStep> steps)
        {
            steps.Add(new TransferStep(StepKind.CreateRemoteFolder, remoteDir, localDir, null, 0));
            foreach (var file in Directory.GetFiles(localDir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                steps.Add(new TransferStep(StepKind.UploadFile, RemotePath.Combine(remoteDir, Path.GetFileName(file)), file, null, new FileInfo(file).Length));
            foreach (var sub in Directory.GetDirectories(localDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                AddLocalTree(sub, RemotePath.Combine(remoteDir, Path.GetFileName(sub)), steps);
        }

        private async Task AddRemoteTreeForDownloadAsync(string remoteDir, string localDir, List<TransferStep> steps, CancellationToken ct)
        {
            steps.Add(new TransferStep(StepKind.CreateLocalFolder, remoteDir, localDir, null, 0));
            var entries = await ListGatedAsync(remoteDir, ct).ConfigureAwait(false);
            foreach (var file in entries.Where(e => e.Kind == FtpEntryKind.File).OrderBy(e => e.Name, StringComparer.Ordinal))
                steps.Add(new TransferStep(StepKind.DownloadFile, RemotePath.Combine(remoteDir, file.Name),
                    Path.Combine(localDir, LocalName.Sanitize(file.Name)), null, file.Size));
            foreach (var dir in entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal))
                await AddRemoteTreeForDownloadAsync(RemotePath.Combine(remoteDir, dir.Name),
                    Path.Combine(localDir, LocalName.Sanitize(dir.Name)), steps, ct).ConfigureAwait(false);
        }

        /// <summary>Nachordnung: erst die Dateien, dann die Unterordner (rekursiv), zuletzt der Ordner selbst — RMD geht nur auf leere Ordner.</summary>
        private async Task AddRemoteTreeForDeleteAsync(string remoteDir, List<TransferStep> steps, CancellationToken ct)
        {
            var entries = await ListGatedAsync(remoteDir, ct).ConfigureAwait(false);
            foreach (var file in entries.Where(e => !e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal))
                steps.Add(new TransferStep(StepKind.DeleteRemoteFile, RemotePath.Combine(remoteDir, file.Name), null, null, file.Size));
            foreach (var dir in entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal))
                await AddRemoteTreeForDeleteAsync(RemotePath.Combine(remoteDir, dir.Name), steps, ct).ConfigureAwait(false);
            steps.Add(new TransferStep(StepKind.RemoveRemoteFolder, remoteDir, null, null, 0));
        }

        private Task<IReadOnlyList<FtpEntry>> ListGatedAsync(string path, CancellationToken ct)
        {
            return FtpGates.RunAsync(_gates.Browse, () => _ftp.ListAsync(path, ct), ct);
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Der Name darf nicht leer sein.", nameof(name));
            if (name.IndexOf('/') >= 0) throw new ArgumentException("Der Name darf keinen Schrägstrich enthalten.", nameof(name));
            if (name == "." || name == "..") throw new ArgumentException("Dieser Name ist nicht erlaubt.", nameof(name));
        }

        private static string Items(IReadOnlyList<string> names)
        {
            return names.Count == 1 ? names[0] : names.Count + " Elemente";
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~FileOperationsTests`
Expected: alle bestanden (inkl. 5 Theorie-Fälle).

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Transfers tests/FileManager.Core.Tests/FileOperationsTests.cs
git commit -m "Plan uploads, downloads, deletes, renames and moves with zone checks

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 15: Die Übertragungswarteschlange

**Files:**
- Create: `src/FileManager.Core/Transfers/IClock.cs`, `src/FileManager.Core/Transfers/RateMeter.cs`, `src/FileManager.Core/Transfers/TransferQueue.cs`
- Create: `tests/FileManager.Core.Tests/Support/TestClock.cs`, `tests/FileManager.Core.Tests/Support/ScriptedResolver.cs`
- Test: `tests/FileManager.Core.Tests/TransferQueueTests.cs`, `tests/FileManager.Core.Tests/RateMeterTests.cs`

**Interfaces:**
- Consumes: alles aus Task 13/14, `IFtpClient`, `FtpException` (Task 4), `FtpGates` (Task 10), `ZoneGuardFtpClient` (Task 9, in einem Test), `UniqueName`.
- Produces:
  - `interface IClock { DateTime UtcNow { get; } }`, `SystemClock.Instance`
  - `internal sealed class RateMeter(IClock clock)`: `Sample(long doneBytes)`, `BytesPerSecond` (gleitend über 3 s)
  - `TransferQueue(IFtpClient ftp, FtpGates gates, IConflictResolver resolver, IClock clock)`:
    - `AppendResumesUploads` (bool, Standard `true`; **auf `false` setzen, falls Task 2 Nr. 04–05 fehlschlug**)
    - `event EventHandler<TransferJob> JobChanged` (feuert auf dem Arbeits-Thread; Fortschritt höchstens alle 200 ms)
    - `Jobs : IReadOnlyList<TransferJob>`, `Enqueue(TransferJob job)`, `Task CancelAsync(TransferJob job)`, `Resume(TransferJob job)`, `ClearFinished()`, `Task WhenIdle()`
  - Verhalten (Spec „Operationen und Aufträge"): ein Auftrag zur Zeit, eine Datei zur Zeit (Transfer-Tor). Permanenter Fehler/Zonenverletzung/lokaler Fehler → Schritt `Failed`, weiter. Transienter Fehler → Auftrag `Interrupted`, Stand bleibt. Abbrechen → angefangene Datei verwerfen (entfernt per `DELE`, lokal `.part` löschen), `Cancelled`. Fortsetzen: Download per REST ab `.part`-Länge; Upload per `SIZE` + `APPE` (oder neu per `STOR`, wenn `AppendResumesUploads == false`). Downloads schreiben nach `<ziel>.part` und benennen am Ende um.

- [ ] **Step 1: Test-Hilfen**

`tests/FileManager.Core.Tests/Support/TestClock.cs`:

```csharp
using System;
using FileManager.Core.Transfers;

namespace FileManager.Core.Tests.Support
{
    internal sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow + by;
        }
    }
}
```

`tests/FileManager.Core.Tests/Support/ScriptedResolver.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;

namespace FileManager.Core.Tests.Support
{
    /// <summary>Antwortet der Reihe nach; ist Hold gesetzt, wartet jede Antwort darauf.</summary>
    internal sealed class ScriptedResolver : IConflictResolver
    {
        public readonly Queue<ConflictDecision> Answers = new Queue<ConflictDecision>();
        public readonly List<ConflictInfo> Asked = new List<ConflictInfo>();
        public TaskCompletionSource<bool> Hold;

        public async Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct)
        {
            lock (Asked) Asked.Add(conflict);
            var hold = Hold;
            if (hold != null) await hold.Task.ConfigureAwait(false);
            lock (Answers) return Answers.Dequeue();
        }

        public ScriptedResolver Then(ConflictChoice choice, bool applyToAll = false)
        {
            Answers.Enqueue(new ConflictDecision(choice, applyToAll));
            return this;
        }
    }
}
```

- [ ] **Step 2: Failing tests schreiben**

`tests/FileManager.Core.Tests/RateMeterTests.cs`:

```csharp
using System;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    public class RateMeterTests
    {
        [Fact]
        public void The_rate_is_bytes_over_the_last_three_seconds()
        {
            var clock = new TestClock();
            var meter = new RateMeter(clock);

            meter.Sample(0);
            Assert.Equal(0, meter.BytesPerSecond);

            clock.Advance(TimeSpan.FromSeconds(1));
            meter.Sample(7000000);
            Assert.Equal(7000000, meter.BytesPerSecond, 0);

            clock.Advance(TimeSpan.FromSeconds(5));      // alte Proben fallen aus dem Fenster
            meter.Sample(8000000);
            clock.Advance(TimeSpan.FromSeconds(1));
            meter.Sample(9000000);
            Assert.Equal(1000000, meter.BytesPerSecond, 0);
        }
    }
}
```

`tests/FileManager.Core.Tests/TransferQueueTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class TransferQueueTests : IDisposable
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly ScriptedResolver _resolver = new ScriptedResolver();
        private readonly TempDir _temp = new TempDir();
        private readonly TransferQueue _queue;

        public TransferQueueTests()
        {
            _ftp.AddDir("/data");
            _queue = new TransferQueue(_ftp, new FtpGates(), _resolver, new TestClock());
        }

        public void Dispose()
        {
            _temp.Dispose();
        }

        private static byte[] Pattern(int length)
        {
            return Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        }

        private string LocalBytes(string name, byte[] data)
        {
            var path = Path.Combine(_temp.Path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
            return path;
        }

        private static TransferStep Upload(string local, string remote)
        {
            return new TransferStep(StepKind.UploadFile, remote, local, null, new FileInfo(local).Length);
        }

        private async Task<TransferJob> Run(TransferKind kind, params TransferStep[] steps)
        {
            var job = new TransferJob(kind, "Test", steps);
            _queue.Enqueue(job);
            await _queue.WhenIdle();
            return job;
        }

        [Fact]
        public async Task An_upload_creates_the_folder_and_stores_the_file()
        {
            var a = _temp.File("mods/a.txt", "aaa");

            var job = await Run(TransferKind.Upload,
                new TransferStep(StepKind.CreateRemoteFolder, "/data/mods", null, null, 0),
                Upload(a, "/data/mods/a.txt"));

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("aaa", Encoding.UTF8.GetString(_ftp.File("/data/mods/a.txt")));
            Assert.Equal("1 von 1 übertragen", job.Summary);
            Assert.Equal(3, job.DoneBytes);
        }

        [Fact]
        public async Task An_existing_folder_is_merged_not_recreated()
        {
            _ftp.AddDir("/data/mods");
            var a = _temp.File("mods/a.txt", "aaa");

            await Run(TransferKind.Upload,
                new TransferStep(StepKind.CreateRemoteFolder, "/data/mods", null, null, 0),
                Upload(a, "/data/mods/a.txt"));

            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("MKD"));
        }

        [Fact]
        public async Task A_failing_file_is_noted_and_the_job_goes_on()
        {
            _ftp.Failure = (verb, path) => verb == "STOR" && path == "/data/b.txt"
                ? new FtpException("Name nicht erlaubt", FtpErrorKind.Permanent, 553) : null;

            var job = await Run(TransferKind.Upload,
                Upload(_temp.File("a.txt", "a"), "/data/a.txt"),
                Upload(_temp.File("b.txt", "b"), "/data/b.txt"),
                Upload(_temp.File("c.txt", "c"), "/data/c.txt"));

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.NotNull(_ftp.File("/data/c.txt"));
            var failure = Assert.Single(job.Failures);
            Assert.Equal("Name nicht erlaubt", failure.Error);
            Assert.Equal("2 von 3 übertragen, 1 fehlgeschlagen", job.Summary);
        }

        [Fact]
        public async Task A_connection_loss_interrupts_and_resume_appends_the_rest()
        {
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;

            var job = await Run(TransferKind.Upload, Upload(big, "/data/big.bin"));

            Assert.Equal(JobState.Interrupted, job.State);
            Assert.Equal("Verbindung zur PS4 unterbrochen", job.InterruptReason);
            Assert.Equal(100000, _ftp.File("/data/big.bin").Length);

            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(data, _ftp.File("/data/big.bin"));
            Assert.Contains("SIZE /data/big.bin", _ftp.CallsSnapshot());
            Assert.Contains("APPE /data/big.bin@100000", _ftp.CallsSnapshot());
            Assert.Empty(_resolver.Asked);                      // die Teildatei ist unsere — kein Konflikt
        }

        [Fact]
        public async Task Without_append_a_resumed_upload_starts_over()
        {
            _queue.AppendResumesUploads = false;
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;
            var job = await Run(TransferKind.Upload, Upload(big, "/data/big.bin"));

            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(data, _ftp.File("/data/big.bin"));
            Assert.Equal(2, _ftp.CallsSnapshot().Count(c => c == "STOR /data/big.bin"));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("APPE"));
        }

        [Fact]
        public async Task A_download_resumes_from_the_part_file()
        {
            var data = Pattern(300000);
            _ftp.AddFile("/data/big.bin", data);
            var target = Path.Combine(_temp.Path, "big.bin");
            _ftp.InterruptAfterBytes = 100000;

            var job = await Run(TransferKind.Download, new TransferStep(StepKind.DownloadFile, "/data/big.bin", target, null, data.Length));

            Assert.Equal(JobState.Interrupted, job.State);
            Assert.Equal(100000, new FileInfo(target + ".part").Length);
            Assert.False(File.Exists(target));

            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(data, File.ReadAllBytes(target));
            Assert.False(File.Exists(target + ".part"));
            Assert.Contains("RETR /data/big.bin@100000", _ftp.CallsSnapshot());
        }

        [Fact]
        public async Task Cancelling_a_running_upload_deletes_the_half_file()
        {
            var big = LocalBytes("big.bin", Pattern(3000000));
            var job = new TransferJob(TransferKind.Upload, "Test", new[] { Upload(big, "/data/big.bin") });
            _queue.JobChanged += (s, j) =>
            {
                if (j.State == JobState.Running && j.DoneBytes > 0) _queue.CancelAsync(j);
            };

            _queue.Enqueue(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Cancelled, job.State);
            Assert.Null(_ftp.File("/data/big.bin"));
            Assert.Contains("DELE /data/big.bin", _ftp.CallsSnapshot());
        }

        [Fact]
        public async Task Cancelling_an_interrupted_download_deletes_the_part_file()
        {
            _ftp.AddFile("/data/big.bin", Pattern(300000));
            var target = Path.Combine(_temp.Path, "big.bin");
            _ftp.InterruptAfterBytes = 100000;
            var job = await Run(TransferKind.Download, new TransferStep(StepKind.DownloadFile, "/data/big.bin", target, null, 300000));

            await _queue.CancelAsync(job);

            Assert.Equal(JobState.Cancelled, job.State);
            Assert.False(File.Exists(target + ".part"));
        }

        [Fact]
        public async Task A_conflict_asks_and_keep_both_picks_a_free_name()
        {
            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Then(ConflictChoice.KeepBoth);

            await Run(TransferKind.Upload, Upload(_temp.File("a.txt", "neu!"), "/data/a.txt"));

            var asked = Assert.Single(_resolver.Asked);
            Assert.Equal("a (2).txt", asked.KeepBothName);
            Assert.Equal(3, asked.ExistingSize);
            Assert.Equal(4, asked.NewSize);
            Assert.Equal("alt", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Equal("neu!", Encoding.UTF8.GetString(_ftp.File("/data/a (2).txt")));
        }

        [Fact]
        public async Task Apply_to_all_asks_only_once()
        {
            _ftp.AddFile("/data/a.txt", "x").AddFile("/data/b.txt", "y");
            _resolver.Then(ConflictChoice.Skip, applyToAll: true);

            var job = await Run(TransferKind.Upload,
                Upload(_temp.File("a.txt", "1"), "/data/a.txt"),
                Upload(_temp.File("b.txt", "2"), "/data/b.txt"));

            Assert.Single(_resolver.Asked);
            Assert.Equal("0 von 2 übertragen, 2 übersprungen", job.Summary);
            Assert.Equal("x", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
        }

        [Fact]
        public async Task Replace_overwrites()
        {
            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Then(ConflictChoice.Replace);

            await Run(TransferKind.Upload, Upload(_temp.File("a.txt", "neu"), "/data/a.txt"));

            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
        }

        [Fact]
        public async Task Write_back_overwrites_without_asking()
        {
            _ftp.AddFile("/data/plugins.ini", "alt");
            var step = Upload(_temp.File("plugins.ini", "neu"), "/data/plugins.ini");
            step.OverwriteWithoutAsking = true;

            await Run(TransferKind.Upload, step);

            Assert.Empty(_resolver.Asked);
            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/plugins.ini")));
        }

        [Fact]
        public async Task A_local_conflict_on_download_can_keep_both()
        {
            _ftp.AddFile("/data/x.txt", "remote");
            var target = _temp.File("x.txt", "lokal");
            _resolver.Then(ConflictChoice.KeepBoth);

            await Run(TransferKind.Download, new TransferStep(StepKind.DownloadFile, "/data/x.txt", target, null, 6));

            Assert.Equal("lokal", File.ReadAllText(target));
            Assert.Equal("remote", File.ReadAllText(Path.Combine(_temp.Path, "x (2).txt")));
        }

        [Fact]
        public async Task Moving_onto_an_existing_file_can_replace_it()
        {
            _ftp.AddFile("/data/a.txt", "neu").AddFile("/data/ziel/a.txt", "alt");
            _resolver.Then(ConflictChoice.Replace);

            await Run(TransferKind.Move, new TransferStep(StepKind.RenameRemote, "/data/a.txt", null, "/data/ziel/a.txt", 3));

            Assert.Null(_ftp.File("/data/a.txt"));
            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/ziel/a.txt")));
        }

        [Fact]
        public async Task Moving_a_folder_onto_an_existing_name_fails_the_step()
        {
            _ftp.AddDir("/data/mods").AddDir("/data/ziel/mods");
            var step = new TransferStep(StepKind.RenameRemote, "/data/mods", null, "/data/ziel/mods", 0) { IsDirectory = true };

            var job = await Run(TransferKind.Move, step);

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Contains("bereits", step.Error);
            Assert.Empty(_resolver.Asked);
        }

        [Fact]
        public async Task Delete_steps_run_in_order()
        {
            _ftp.AddFile("/data/alt/a.txt", "a");

            var job = await Run(TransferKind.Delete,
                new TransferStep(StepKind.DeleteRemoteFile, "/data/alt/a.txt", null, null, 1),
                new TransferStep(StepKind.RemoveRemoteFolder, "/data/alt", null, null, 0));

            Assert.Equal(JobState.Completed, job.State);
            Assert.False(_ftp.HasDir("/data/alt"));
        }

        [Fact]
        public async Task A_zone_violation_during_execution_is_a_failed_step()
        {
            var guarded = new TransferQueue(new ZoneGuardFtpClient(_ftp, new ZonePolicy()), new FtpGates(), _resolver, new TestClock());
            _ftp.AddDir("/system_data");
            var job = new TransferJob(TransferKind.Upload, "t", new[] { Upload(_temp.File("x", "x"), "/system_data/x") });

            guarded.Enqueue(job);
            await guarded.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.StartsWith("Gesperrter Bereich", job.Steps[0].Error);
            Assert.Equal(0, _ftp.WriteCallCount);
        }

        [Fact]
        public async Task Jobs_run_one_after_another_and_a_cancelled_waiting_job_is_skipped()
        {
            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Hold = new TaskCompletionSource<bool>();
            _resolver.Then(ConflictChoice.Replace);
            var first = new TransferJob(TransferKind.Upload, "1", new[] { Upload(_temp.File("a.txt", "neu"), "/data/a.txt") });
            var second = new TransferJob(TransferKind.Upload, "2", new[] { Upload(_temp.File("b.txt", "b"), "/data/b.txt") });
            var third = new TransferJob(TransferKind.Upload, "3", new[] { Upload(_temp.File("c.txt", "c"), "/data/c.txt") });

            _queue.Enqueue(first);
            _queue.Enqueue(second);
            _queue.Enqueue(third);
            await _queue.CancelAsync(second);
            _resolver.Hold.SetResult(true);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, first.State);
            Assert.Equal(JobState.Cancelled, second.State);
            Assert.Equal(JobState.Completed, third.State);
            Assert.Null(_ftp.File("/data/b.txt"));
            var stores = _ftp.CallsSnapshot().Where(c => c.StartsWith("STOR")).ToArray();
            Assert.Equal(new[] { "STOR /data/a.txt", "STOR /data/c.txt" }, stores);
        }

        [Fact]
        public async Task ClearFinished_keeps_interrupted_jobs()
        {
            _ftp.InterruptAfterBytes = 10;
            var interrupted = await Run(TransferKind.Upload, Upload(LocalBytes("x.bin", Pattern(100)), "/data/x.bin"));
            _ftp.InterruptAfterBytes = 0;
            var done = await Run(TransferKind.Upload, Upload(_temp.File("y.txt", "y"), "/data/y.txt"));

            _queue.ClearFinished();

            Assert.Equal(new[] { interrupted }, _queue.Jobs.ToArray());
            Assert.Equal(JobState.Completed, done.State);
        }
    }
}
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~TransferQueueTests|FullyQualifiedName~RateMeterTests"`
Expected: Build-Fehler — `IClock`, `RateMeter`, `TransferQueue` fehlen.

- [ ] **Step 4: Implementieren**

`src/FileManager.Core/Transfers/IClock.cs`:

```csharp
using System;

namespace FileManager.Core.Transfers
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        private SystemClock()
        {
        }

        public DateTime UtcNow { get { return DateTime.UtcNow; } }
    }
}
```

`src/FileManager.Core/Transfers/RateMeter.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace FileManager.Core.Transfers
{
    /// <summary>Übertragungsrate, gleitend über die letzten drei Sekunden.</summary>
    internal sealed class RateMeter
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

        private readonly IClock _clock;
        private readonly Queue<KeyValuePair<DateTime, long>> _samples = new Queue<KeyValuePair<DateTime, long>>();

        public RateMeter(IClock clock)
        {
            _clock = clock;
        }

        public double BytesPerSecond { get; private set; }

        public void Sample(long doneBytes)
        {
            var now = _clock.UtcNow;
            _samples.Enqueue(new KeyValuePair<DateTime, long>(now, doneBytes));
            while (_samples.Count > 1 && now - _samples.Peek().Key > Window) _samples.Dequeue();

            var first = _samples.Peek();
            var seconds = (now - first.Key).TotalSeconds;
            if (seconds > 0) BytesPerSecond = (doneBytes - first.Value) / seconds;
        }
    }
}
```

`src/FileManager.Core/Transfers/TransferQueue.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// Führt Aufträge aus (Spec „Operationen und Aufträge"): einer zur Zeit,
    /// darin eine Datei zur Zeit. Mehr Parallelität bringt bei ~7 MB/s nichts,
    /// und die FTP-Überlastung aus 271477f soll sich nicht wiederholen.
    ///
    /// Fehlerpolitik: permanent (Server sagt nein, Zone, lokal) → die Datei
    /// scheitert, der Auftrag läuft weiter. Transient (Verbindung) → der
    /// Auftrag wird unterbrochen und bleibt fortsetzbar. Keine automatische
    /// Wiederholung (Gallery-Regel).
    /// </summary>
    public sealed class TransferQueue
    {
        private const string PartSuffix = ".part";
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly IConflictResolver _resolver;
        private readonly IClock _clock;
        private readonly object _lock = new object();
        private readonly List<TransferJob> _jobs = new List<TransferJob>();
        private readonly Queue<TransferJob> _pending = new Queue<TransferJob>();
        private Task _worker = Task.FromResult(0);
        private bool _running;

        public TransferQueue(IFtpClient ftp, FtpGates gates, IConflictResolver resolver, IClock clock)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            _ftp = ftp;
            _gates = gates;
            _resolver = resolver;
            _clock = clock;
            AppendResumesUploads = true;
        }

        /// <summary>Befund Task 2 Nr. 04–05. false: ein unterbrochener Upload beginnt neu (STOR).</summary>
        public bool AppendResumesUploads { get; set; }

        /// <summary>Feuert auf dem Arbeits-Thread — die App marshallt selbst auf den UI-Thread.</summary>
        public event EventHandler<TransferJob> JobChanged;

        public IReadOnlyList<TransferJob> Jobs
        {
            get { lock (_lock) return _jobs.ToList(); }
        }

        public void Enqueue(TransferJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            lock (_lock)
            {
                if (_jobs.Contains(job)) throw new InvalidOperationException("Der Auftrag steht schon in der Warteschlange.");
                _jobs.Add(job);
                job.State = JobState.Waiting;
                _pending.Enqueue(job);
                StartWorkerUnlocked();
            }
            Raise(job);
        }

        public void Resume(TransferJob job)
        {
            lock (_lock)
            {
                if (job.State != JobState.Interrupted) return;
                job.InterruptReason = null;
                job.State = JobState.Waiting;
                _pending.Enqueue(job);
                StartWorkerUnlocked();
            }
            Raise(job);
        }

        public async Task CancelAsync(TransferJob job)
        {
            bool discard;
            lock (_lock)
            {
                if (job.IsFinished) return;
                if (job.State == JobState.Running)
                {
                    // Der Arbeits-Thread verwirft die angefangene Datei selbst.
                    var cts = job.Cancellation;
                    if (cts != null) cts.Cancel();
                    return;
                }
                discard = job.State == JobState.Interrupted;
                job.State = JobState.Cancelled;          // Wartende überspringt der Arbeits-Thread
            }
            if (discard && job.CurrentIndex < job.Steps.Count)
                await DiscardPartialAsync(job.Steps[job.CurrentIndex]).ConfigureAwait(false);
            Raise(job);
        }

        public void ClearFinished()
        {
            lock (_lock) _jobs.RemoveAll(j => j.IsFinished);
        }

        public Task WhenIdle()
        {
            lock (_lock) return _worker;
        }

        private void StartWorkerUnlocked()
        {
            if (_running) return;
            _running = true;
            _worker = Task.Run(WorkAsync);
        }

        private async Task WorkAsync()
        {
            while (true)
            {
                TransferJob job;
                lock (_lock)
                {
                    if (_pending.Count == 0)
                    {
                        _running = false;
                        return;
                    }
                    job = _pending.Dequeue();
                    if (job.State != JobState.Waiting) continue;
                    job.Cancellation = new CancellationTokenSource();
                    job.State = JobState.Running;
                }
                await RunJobAsync(job).ConfigureAwait(false);
            }
        }

        private async Task RunJobAsync(TransferJob job)
        {
            var cts = job.Cancellation;
            var index = new Dictionary<string, Dictionary<string, FtpEntry>>(StringComparer.Ordinal);
            var rate = new RateMeter(_clock);
            Raise(job);

            try
            {
                for (int i = job.CurrentIndex; i < job.Steps.Count; i++)
                {
                    job.CurrentIndex = i;
                    var step = job.Steps[i];
                    if (step.State != StepState.Pending) continue;

                    try
                    {
                        await FtpGates.RunAsync(_gates.Transfer, async () =>
                        {
                            await ExecuteAsync(job, step, index, rate, cts.Token).ConfigureAwait(false);
                            return 0;
                        }, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
                    catch (FtpException ex) when (ex.IsTransient) { throw; }
                    catch (Exception ex)
                    {
                        step.State = StepState.Failed;
                        step.Error = ex.Message;
                    }
                    Raise(job);
                }

                job.CurrentIndex = job.Steps.Count;
                job.State = job.Steps.Any(s => s.State == StepState.Failed) ? JobState.CompletedWithErrors : JobState.Completed;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                if (job.CurrentIndex < job.Steps.Count)
                    await DiscardPartialAsync(job.Steps[job.CurrentIndex]).ConfigureAwait(false);
                job.State = JobState.Cancelled;
            }
            catch (FtpException ex) when (ex.IsTransient)
            {
                job.InterruptReason = ex.Message;
                job.State = JobState.Interrupted;
            }
            finally
            {
                lock (_lock) job.Cancellation = null;
                cts.Dispose();
                job.BytesPerSecond = 0;
            }
            Raise(job);
        }

        private async Task ExecuteAsync(TransferJob job, TransferStep step,
            Dictionary<string, Dictionary<string, FtpEntry>> index, RateMeter rate, CancellationToken ct)
        {
            switch (step.Kind)
            {
                case StepKind.CreateRemoteFolder:
                {
                    var parent = await IndexAsync(RemotePath.Parent(step.RemotePath), index, ct).ConfigureAwait(false);
                    var name = RemotePath.Name(step.RemotePath);
                    if (!parent.ContainsKey(name))
                    {
                        await _ftp.MakeDirectoryAsync(step.RemotePath, ct).ConfigureAwait(false);
                        parent[name] = new FtpEntry(name, FtpEntryKind.Directory, 0, DateTime.MinValue, null);
                        index[RemotePath.Normalize(step.RemotePath)] = new Dictionary<string, FtpEntry>(StringComparer.Ordinal);
                    }
                    step.State = StepState.Done;
                    break;
                }
                case StepKind.CreateLocalFolder:
                    Directory.CreateDirectory(step.LocalPath);
                    step.State = StepState.Done;
                    break;
                case StepKind.UploadFile:
                    await UploadAsync(job, step, index, rate, ct).ConfigureAwait(false);
                    break;
                case StepKind.DownloadFile:
                    await DownloadAsync(job, step, rate, ct).ConfigureAwait(false);
                    break;
                case StepKind.DeleteRemoteFile:
                    await _ftp.DeleteAsync(step.RemotePath, ct).ConfigureAwait(false);
                    step.State = StepState.Done;
                    break;
                case StepKind.RemoveRemoteFolder:
                    await _ftp.RemoveDirectoryAsync(step.RemotePath, ct).ConfigureAwait(false);
                    step.State = StepState.Done;
                    break;
                case StepKind.RenameRemote:
                    await RenameAsync(job, step, index, ct).ConfigureAwait(false);
                    break;
            }
        }

        private async Task UploadAsync(TransferJob job, TransferStep step,
            Dictionary<string, Dictionary<string, FtpEntry>> index, RateMeter rate, CancellationToken ct)
        {
            var parentPath = RemotePath.Parent(step.RemotePath);
            var parent = await IndexAsync(parentPath, index, ct).ConfigureAwait(false);
            var name = RemotePath.Name(step.RemotePath);

            FtpEntry existing;
            if (!step.Started && !step.OverwriteWithoutAsking && parent.TryGetValue(name, out existing))
            {
                var keepBoth = UniqueName.For(name, false, parent.ContainsKey);
                var choice = await DecideAsync(job,
                    new ConflictInfo(step.RemotePath, true, existing.Size, step.Size, keepBoth), ct).ConfigureAwait(false);
                if (choice == ConflictChoice.Skip)
                {
                    step.State = StepState.Skipped;
                    return;
                }
                if (choice == ConflictChoice.KeepBoth)
                {
                    step.RemotePath = RemotePath.Combine(parentPath, keepBoth);
                    name = keepBoth;
                }
            }

            long offset = 0;
            if (step.Started && AppendResumesUploads)
            {
                try { offset = await _ftp.GetFileSizeAsync(step.RemotePath, ct).ConfigureAwait(false); }
                catch (FtpException ex) when (!ex.IsTransient) { offset = 0; }
                if (offset > step.Size) offset = 0;
            }

            step.Started = true;
            step.Transferred = offset;
            var progress = new StepProgress(this, job, step, rate, offset);
            if (offset > 0)
                await _ftp.AppendFromFileAsync(step.LocalPath, offset, step.RemotePath, progress, ct).ConfigureAwait(false);
            else
                await _ftp.UploadFromFileAsync(step.LocalPath, step.RemotePath, progress, ct).ConfigureAwait(false);

            parent[name] = new FtpEntry(name, FtpEntryKind.File, step.Size, DateTime.MinValue, null);
            step.Transferred = step.Size;
            step.State = StepState.Done;
        }

        private async Task DownloadAsync(TransferJob job, TransferStep step, RateMeter rate, CancellationToken ct)
        {
            if (!step.Started && !step.OverwriteWithoutAsking && File.Exists(step.LocalPath))
            {
                var dir = Path.GetDirectoryName(step.LocalPath);
                var keepBoth = UniqueName.For(Path.GetFileName(step.LocalPath), false, n => File.Exists(Path.Combine(dir, n)));
                var choice = await DecideAsync(job,
                    new ConflictInfo(step.LocalPath, false, new FileInfo(step.LocalPath).Length, step.Size, keepBoth), ct).ConfigureAwait(false);
                if (choice == ConflictChoice.Skip)
                {
                    step.State = StepState.Skipped;
                    return;
                }
                if (choice == ConflictChoice.KeepBoth) step.LocalPath = Path.Combine(dir, keepBoth);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(step.LocalPath));
            var part = step.LocalPath + PartSuffix;
            long offset = 0;
            if (step.Started && File.Exists(part)) offset = new FileInfo(part).Length;
            if (offset > step.Size) offset = 0;
            if (offset == 0 && File.Exists(part)) File.Delete(part);

            step.Started = true;
            step.Transferred = offset;
            await _ftp.DownloadToFileAsync(step.RemotePath, part, offset, new StepProgress(this, job, step, rate, offset), ct)
                .ConfigureAwait(false);

            if (File.Exists(step.LocalPath)) File.Delete(step.LocalPath);
            File.Move(part, step.LocalPath);
            step.Transferred = step.Size;
            step.State = StepState.Done;
        }

        private async Task RenameAsync(TransferJob job, TransferStep step,
            Dictionary<string, Dictionary<string, FtpEntry>> index, CancellationToken ct)
        {
            var parentPath = RemotePath.Parent(step.TargetRemotePath);
            var parent = await IndexAsync(parentPath, index, ct).ConfigureAwait(false);
            var name = RemotePath.Name(step.TargetRemotePath);

            FtpEntry existing;
            if (parent.TryGetValue(name, out existing))
            {
                if (step.IsDirectory || existing.IsDirectory)
                    throw new InvalidOperationException("Im Ziel gibt es bereits ein Element namens „" + name + "“.");

                var keepBoth = UniqueName.For(name, false, parent.ContainsKey);
                var choice = await DecideAsync(job,
                    new ConflictInfo(step.TargetRemotePath, true, existing.Size, step.Size, keepBoth), ct).ConfigureAwait(false);
                if (choice == ConflictChoice.Skip)
                {
                    step.State = StepState.Skipped;
                    return;
                }
                if (choice == ConflictChoice.KeepBoth)
                {
                    step.TargetRemotePath = RemotePath.Combine(parentPath, keepBoth);
                    name = keepBoth;
                }
                else
                {
                    await _ftp.DeleteAsync(step.TargetRemotePath, ct).ConfigureAwait(false);
                    parent.Remove(name);
                }
            }

            await _ftp.RenameAsync(step.RemotePath, step.TargetRemotePath, ct).ConfigureAwait(false);
            parent[name] = new FtpEntry(name, step.IsDirectory ? FtpEntryKind.Directory : FtpEntryKind.File, step.Size, DateTime.MinValue, null);
            Dictionary<string, FtpEntry> source;
            if (index.TryGetValue(RemotePath.Parent(step.RemotePath), out source)) source.Remove(RemotePath.Name(step.RemotePath));
            step.State = StepState.Done;
        }

        /// <summary>Namen eines Zielordners, einmal pro Auftrag gelistet. Fehlt der Ordner (noch), ist er leer.</summary>
        private async Task<Dictionary<string, FtpEntry>> IndexAsync(string dir,
            Dictionary<string, Dictionary<string, FtpEntry>> index, CancellationToken ct)
        {
            var key = RemotePath.Normalize(dir);
            Dictionary<string, FtpEntry> entries;
            if (index.TryGetValue(key, out entries)) return entries;

            entries = new Dictionary<string, FtpEntry>(StringComparer.Ordinal);
            try
            {
                foreach (var entry in await _ftp.ListAsync(key, ct).ConfigureAwait(false)) entries[entry.Name] = entry;
            }
            catch (FtpException ex) when (!ex.IsTransient)
            {
                // Ordner existiert nicht — dann gibt es auch keine Konflikte.
            }
            index[key] = entries;
            return entries;
        }

        private async Task<ConflictChoice> DecideAsync(TransferJob job, ConflictInfo info, CancellationToken ct)
        {
            if (job.RememberedChoice.HasValue) return job.RememberedChoice.Value;
            var decision = await _resolver.ResolveAsync(info, ct).ConfigureAwait(false);
            if (decision.ApplyToAll) job.RememberedChoice = decision.Choice;
            return decision.Choice;
        }

        /// <summary>Verwirft eine angefangene Datei — best effort, ein Fehler hier ändert am Abbruch nichts.</summary>
        private async Task DiscardPartialAsync(TransferStep step)
        {
            if (!step.Started) return;
            try
            {
                if (step.Kind == StepKind.UploadFile)
                    await _ftp.DeleteAsync(step.RemotePath, CancellationToken.None).ConfigureAwait(false);
                else if (step.Kind == StepKind.DownloadFile && File.Exists(step.LocalPath + PartSuffix))
                    File.Delete(step.LocalPath + PartSuffix);
            }
            catch (Exception)
            {
                // Rest bleibt liegen — auf der PS4 sichtbar, lokal als .part.
            }
            step.Started = false;
            step.Transferred = 0;
        }

        private void Raise(TransferJob job)
        {
            var handler = JobChanged;
            if (handler != null) handler(this, job);
        }

        /// <summary>Direkter IProgress (kein Progress&lt;T&gt;): läuft synchron im Transfer, meldet höchstens alle 200 ms.</summary>
        private sealed class StepProgress : IProgress<long>
        {
            private readonly TransferQueue _queue;
            private readonly TransferJob _job;
            private readonly TransferStep _step;
            private readonly RateMeter _rate;
            private readonly long _baseline;
            private DateTime _lastRaise = DateTime.MinValue;

            public StepProgress(TransferQueue queue, TransferJob job, TransferStep step, RateMeter rate, long baseline)
            {
                _queue = queue;
                _job = job;
                _step = step;
                _rate = rate;
                _baseline = baseline;
            }

            public void Report(long value)
            {
                _step.Transferred = _baseline + value;
                _rate.Sample(_job.DoneBytes);
                _job.BytesPerSecond = _rate.BytesPerSecond;

                var now = _queue._clock.UtcNow;
                if (now - _lastRaise < ProgressInterval) return;
                _lastRaise = now;
                _queue.Raise(_job);
            }
        }
    }
}
```

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~TransferQueueTests|FullyQualifiedName~RateMeterTests"`
Expected: alle bestanden. Zum Test `Cancelling_a_running_upload_deletes_the_half_file`: Die `TestClock` steht still, also feuert der Fortschritt genau einmal (erster Bericht) — dort bricht der Test ab. Scheitert er mit `Completed`, prüft `StepProgress.Report` die 200-ms-Drossel falsch herum.

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün.

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Transfers tests/FileManager.Core.Tests
git commit -m "Run transfer jobs one at a time with conflicts, cancel and resume

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 16: Öffnen mit Rückschreiben — `EditSessionManager`

**Files:**
- Create: `src/FileManager.Core/Editing/IFileChangeSource.cs` (mit `FileSystemChangeSource`), `src/FileManager.Core/Editing/EditSession.cs`, `src/FileManager.Core/Editing/EditSessionManager.cs`
- Test: `tests/FileManager.Core.Tests/EditSessionManagerTests.cs`

**Interfaces:**
- Consumes: `IFtpClient`, `FtpException`, `FtpGates` (Task 10), `RemotePath` (Task 8), `IClock`, `LocalName` (Task 13/15), `InMemoryFtpClient`, `TestClock`, `TempDir`.
- Produces (Namensraum `FileManager.Core.Editing`):
  - `interface IFileChangeSource : IDisposable { event EventHandler<string> Changed; void Watch(string directory); }` — `Changed` liefert den vollen Pfad; `FileSystemChangeSource` ist die Produktion (FileSystemWatcher, reagiert auf Changed/Created/Renamed — Editoren speichern gern über eine Umbenennung).
  - `EditSession`: `RemotePath`, `LocalPath`, `BaseSize`, `BaseModified`, `HasPendingChange`.
  - `EditSessionManager(string workRoot, IFtpClient ftp, FtpGates gates, IFileChangeSource changes, IClock clock) : IDisposable`:
    - `const long AskAboveBytes` (200 MB), `static TimeSpan Quiet` (1 s), `static bool NeedsSizeConfirmation(long size)`, `static void CleanWorkRoot(string workRoot)`
    - `string PrepareLocalPath(string remotePath)` — eigener Unterordner je Datei, Windows-sicherer Name
    - `EditSession Register(string remotePath, string localPath, long size, DateTime modified)` — nach erfolgreichem Download
    - `event EventHandler<EditSession> ChangeDetected`, `void Tick()` — die App ruft `Tick` im Takt (z. B. 250 ms); angekündigt wird nach 1 s Ruhe und sobald die Datei wieder lesbar ist, einmal pro Änderungsschub
    - `Task<bool> RemoteChangedAsync(EditSession session, CancellationToken ct)` — frisches Listing (am Cache vorbei), vergleicht Größe und Datum mit der Grundlage; fehlt die Datei → `true`
    - `Task CompleteWriteBackAsync(EditSession session, CancellationToken ct)` — neue Grundlage nach dem Hochladen
    - `void Discard(EditSession session)`, `IReadOnlyList<EditSession> PendingSessions`
  - Ablauf in der App (Plan 3): Doppelklick → `NeedsSizeConfirmation` → `PrepareLocalPath` → `FileOperations.PlanOpenForEdit` → Warteschlange → nach `Completed`: `Register` + ShellExecute. `ChangeDetected` → Frage → `RemoteChangedAsync` (ggf. Zusatzwarnung) → `FileOperations.PlanWriteBackAsync` → Warteschlange → nach `Completed`: `CompleteWriteBackAsync`. Beim Beenden: `PendingSessions` nicht leer → Rückfrage. Beim Start: `CleanWorkRoot`.

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/EditSessionManagerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using FileManager.Core.Tests.Support;
using Xunit;

namespace FileManager.Core.Tests
{
    internal sealed class FakeChangeSource : IFileChangeSource
    {
        public string Watched;
        public event EventHandler<string> Changed;
        public void Watch(string directory) { Watched = directory; }
        public void Raise(string path) { var h = Changed; if (h != null) h(this, path); }
        public void Dispose() { }
    }

    public sealed class EditSessionManagerTests : IDisposable
    {
        private readonly TempDir _temp = new TempDir();
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly FakeChangeSource _changes = new FakeChangeSource();
        private readonly TestClock _clock = new TestClock();
        private readonly EditSessionManager _manager;
        private readonly List<EditSession> _announced = new List<EditSession>();

        public EditSessionManagerTests()
        {
            _ftp.AddFile("/data/GoldHEN/plugins.ini", "[default]");
            _manager = new EditSessionManager(Path.Combine(_temp.Path, "open"), _ftp, new FtpGates(), _changes, _clock);
            _manager.ChangeDetected += (s, e) => _announced.Add(e);
        }

        public void Dispose()
        {
            _manager.Dispose();
            _temp.Dispose();
        }

        private EditSession OpenPluginsIni()
        {
            var local = _manager.PrepareLocalPath("/data/GoldHEN/plugins.ini");
            File.WriteAllText(local, "[default]");
            return _manager.Register("/data/GoldHEN/plugins.ini", local, 9, _ftp.Modified("/data/GoldHEN/plugins.ini"));
        }

        [Fact]
        public void The_work_root_is_watched()
        {
            Assert.Equal(Path.Combine(_temp.Path, "open"), _changes.Watched);
            Assert.True(Directory.Exists(_changes.Watched));
        }

        [Fact]
        public void A_change_is_announced_after_one_quiet_second_and_only_once()
        {
            var session = OpenPluginsIni();
            _changes.Raise(session.LocalPath);

            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _manager.Tick();
            Assert.Empty(_announced);

            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _manager.Tick();
            _manager.Tick();

            Assert.Same(session, Assert.Single(_announced));
            Assert.True(session.HasPendingChange);
            Assert.Equal(new[] { session }, _manager.PendingSessions.ToArray());
        }

        [Fact]
        public void A_burst_of_saves_is_one_announcement()
        {
            var session = OpenPluginsIni();
            _changes.Raise(session.LocalPath);
            _clock.Advance(TimeSpan.FromMilliseconds(600));
            _changes.Raise(session.LocalPath);

            _clock.Advance(TimeSpan.FromMilliseconds(600));
            _manager.Tick();
            Assert.Empty(_announced);

            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _manager.Tick();
            Assert.Single(_announced);
        }

        [Fact]
        public void A_file_still_locked_by_the_editor_waits()
        {
            var session = OpenPluginsIni();
            _changes.Raise(session.LocalPath);
            _clock.Advance(TimeSpan.FromSeconds(2));

            using (new FileStream(session.LocalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                _manager.Tick();
                Assert.Empty(_announced);
            }

            _manager.Tick();
            Assert.Single(_announced);
        }

        [Fact]
        public void Changes_to_unknown_files_are_ignored()
        {
            OpenPluginsIni();
            _changes.Raise(Path.Combine(_temp.Path, "open", "fremd.txt"));
            _clock.Advance(TimeSpan.FromSeconds(2));
            _manager.Tick();
            Assert.Empty(_announced);
        }

        [Fact]
        public async Task The_console_side_is_checked_before_writing_back()
        {
            var session = OpenPluginsIni();
            Assert.False(await _manager.RemoteChangedAsync(session, CancellationToken.None));

            _ftp.ChangeRemotely("/data/GoldHEN/plugins.ini", new byte[] { 1, 2, 3 });
            Assert.True(await _manager.RemoteChangedAsync(session, CancellationToken.None));

            await _ftp.DeleteAsync("/data/GoldHEN/plugins.ini", CancellationToken.None);
            Assert.True(await _manager.RemoteChangedAsync(session, CancellationToken.None));
        }

        [Fact]
        public async Task Completing_a_write_back_moves_the_baseline_and_clears_the_change()
        {
            var session = OpenPluginsIni();
            _changes.Raise(session.LocalPath);
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();

            _ftp.ChangeRemotely("/data/GoldHEN/plugins.ini", new byte[] { 1, 2, 3, 4 });   // unser eigener Upload
            await _manager.CompleteWriteBackAsync(session, CancellationToken.None);

            Assert.False(session.HasPendingChange);
            Assert.Equal(4, session.BaseSize);
            Assert.False(await _manager.RemoteChangedAsync(session, CancellationToken.None));
            Assert.Empty(_manager.PendingSessions);
        }

        [Fact]
        public void Discard_clears_the_change_but_keeps_watching()
        {
            var session = OpenPluginsIni();
            _changes.Raise(session.LocalPath);
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();

            _manager.Discard(session);
            Assert.False(session.HasPendingChange);

            _changes.Raise(session.LocalPath);
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();
            Assert.Equal(2, _announced.Count);
        }

        [Fact]
        public void Each_file_gets_its_own_folder_and_a_windows_safe_name()
        {
            var first = _manager.PrepareLocalPath("/data/a:b.txt");
            var second = _manager.PrepareLocalPath("/data/a:b.txt");

            Assert.Equal("a_b.txt", Path.GetFileName(first));
            Assert.NotEqual(Path.GetDirectoryName(first), Path.GetDirectoryName(second));
            Assert.True(Directory.Exists(Path.GetDirectoryName(first)));
            Assert.StartsWith(Path.Combine(_temp.Path, "open"), first);
        }

        [Fact]
        public void CleanWorkRoot_removes_the_leftovers_of_the_last_session()
        {
            var root = _temp.Sub("alt");
            _temp.File("alt/1a2b/x.txt", "x");

            EditSessionManager.CleanWorkRoot(root);

            Assert.True(Directory.Exists(root));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }

        [Fact]
        public void Only_files_above_200_mb_need_a_confirmation()
        {
            Assert.False(EditSessionManager.NeedsSizeConfirmation(200L * 1024 * 1024));
            Assert.True(EditSessionManager.NeedsSizeConfirmation(200L * 1024 * 1024 + 1));
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~EditSessionManagerTests`
Expected: Build-Fehler — `FileManager.Core.Editing` fehlt.

- [ ] **Step 3: Implementieren**

`src/FileManager.Core/Editing/IFileChangeSource.cs`:

```csharp
using System;
using System.IO;

namespace FileManager.Core.Editing
{
    /// <summary>Naht vor dem FileSystemWatcher — der Test löst Änderungen von Hand aus.</summary>
    public interface IFileChangeSource : IDisposable
    {
        /// <summary>Voller Pfad der geänderten Datei. Kann auf einem fremden Thread feuern.</summary>
        event EventHandler<string> Changed;

        void Watch(string directory);
    }

    /// <summary>
    /// Produktion. Changed, Created UND Renamed: viele Editoren speichern über
    /// eine Temp-Datei und benennen sie dann um — ein reines Changed verpasste das.
    /// Nur per Fahrprobe geprüft (Zeitverhalten des Watchers).
    /// </summary>
    public sealed class FileSystemChangeSource : IFileChangeSource
    {
        private FileSystemWatcher _watcher;

        public event EventHandler<string> Changed;

        public void Watch(string directory)
        {
            if (_watcher != null) _watcher.Dispose();
            _watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += (s, e) => Raise(e.FullPath);
            _watcher.Created += (s, e) => Raise(e.FullPath);
            _watcher.Renamed += (s, e) => Raise(e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }

        private void Raise(string path)
        {
            var handler = Changed;
            if (handler != null) handler(this, path);
        }

        public void Dispose()
        {
            if (_watcher != null) _watcher.Dispose();
            _watcher = null;
        }
    }
}
```

`src/FileManager.Core/Editing/EditSession.cs`:

```csharp
using System;

namespace FileManager.Core.Editing
{
    /// <summary>Eine geöffnete Datei: wo sie auf der PS4 liegt, wo die Arbeitskopie liegt, und womit verglichen wird.</summary>
    public sealed class EditSession
    {
        internal EditSession(string remotePath, string localPath, long baseSize, DateTime baseModified)
        {
            RemotePath = remotePath;
            LocalPath = localPath;
            BaseSize = baseSize;
            BaseModified = baseModified;
        }

        public string RemotePath { get; }
        public string LocalPath { get; }
        public long BaseSize { get; internal set; }
        public DateTime BaseModified { get; internal set; }
        public bool HasPendingChange { get; internal set; }

        internal DateTime? LastChangeUtc { get; set; }
    }
}
```

`src/FileManager.Core/Editing/EditSessionManager.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace FileManager.Core.Editing
{
    /// <summary>
    /// Öffnen mit Rückschreiben (Spec): Arbeitskopien unter workRoot werden
    /// beobachtet; nach einer Sekunde Ruhe und sobald der Editor die Datei
    /// freigibt, meldet ChangeDetected — einmal pro Änderungsschub. Die Uhr
    /// läuft über IClock und den Takt der App (Tick), damit das testbar ist.
    /// Hochgeladen wird NICHT hier, sondern als Auftrag über FileOperations
    /// und die Warteschlange — samt Zonenprüfung.
    /// </summary>
    public sealed class EditSessionManager : IDisposable
    {
        public const long AskAboveBytes = 200L * 1024 * 1024;
        public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);

        private readonly string _workRoot;
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly IFileChangeSource _changes;
        private readonly IClock _clock;
        private readonly object _lock = new object();
        private readonly List<EditSession> _sessions = new List<EditSession>();

        public EditSessionManager(string workRoot, IFtpClient ftp, FtpGates gates, IFileChangeSource changes, IClock clock)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            _workRoot = workRoot;
            _ftp = ftp;
            _gates = gates;
            _changes = changes;
            _clock = clock;

            Directory.CreateDirectory(workRoot);
            _changes.Changed += OnChanged;
            _changes.Watch(workRoot);
        }

        public event EventHandler<EditSession> ChangeDetected;

        public static bool NeedsSizeConfirmation(long size)
        {
            return size > AskAboveBytes;
        }

        /// <summary>Beim Start: Arbeitskopien der letzten Sitzung weg (Spec).</summary>
        public static void CleanWorkRoot(string workRoot)
        {
            if (!Directory.Exists(workRoot)) return;
            foreach (var dir in Directory.GetDirectories(workRoot))
            {
                try { Directory.Delete(dir, true); } catch (Exception) { /* noch offen — beim nächsten Start */ }
            }
            foreach (var file in Directory.GetFiles(workRoot))
            {
                try { File.Delete(file); } catch (Exception) { }
            }
        }

        public IReadOnlyList<EditSession> PendingSessions
        {
            get { lock (_lock) return _sessions.Where(s => s.HasPendingChange).ToList(); }
        }

        public string PrepareLocalPath(string remotePath)
        {
            var dir = Path.Combine(_workRoot, Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, LocalName.Sanitize(RemotePath.Name(remotePath)));
        }

        public EditSession Register(string remotePath, string localPath, long size, DateTime modified)
        {
            var session = new EditSession(RemotePath.Normalize(remotePath), localPath, size, modified);
            lock (_lock) _sessions.Add(session);
            return session;
        }

        private void OnChanged(object sender, string fullPath)
        {
            lock (_lock)
            {
                var session = _sessions.FirstOrDefault(s => string.Equals(s.LocalPath, fullPath, StringComparison.OrdinalIgnoreCase));
                if (session != null) session.LastChangeUtc = _clock.UtcNow;
            }
        }

        public void Tick()
        {
            var ready = new List<EditSession>();
            var now = _clock.UtcNow;
            lock (_lock)
            {
                foreach (var session in _sessions)
                {
                    if (!session.LastChangeUtc.HasValue) continue;
                    if (now - session.LastChangeUtc.Value < Quiet) continue;
                    if (!IsReadable(session.LocalPath)) continue;
                    session.LastChangeUtc = null;
                    session.HasPendingChange = true;
                    ready.Add(session);
                }
            }

            var handler = ChangeDetected;
            if (handler == null) return;
            foreach (var session in ready) handler(this, session);
        }

        public async Task<bool> RemoteChangedAsync(EditSession session, CancellationToken ct)
        {
            var entry = await FindRemoteAsync(session, ct).ConfigureAwait(false);
            return entry == null || entry.Size != session.BaseSize || entry.Modified != session.BaseModified;
        }

        public async Task CompleteWriteBackAsync(EditSession session, CancellationToken ct)
        {
            var entry = await FindRemoteAsync(session, ct).ConfigureAwait(false);
            session.BaseSize = entry != null ? entry.Size : new FileInfo(session.LocalPath).Length;
            session.BaseModified = entry != null ? entry.Modified : DateTime.MinValue;
            session.HasPendingChange = false;
        }

        public void Discard(EditSession session)
        {
            session.HasPendingChange = false;
        }

        /// <summary>Frisches Listing am Verzeichnis-Cache vorbei — der Cache könnte veraltet sein.</summary>
        private async Task<FtpEntry> FindRemoteAsync(EditSession session, CancellationToken ct)
        {
            IReadOnlyList<FtpEntry> entries;
            try
            {
                entries = await FtpGates.RunAsync(_gates.Browse,
                    () => _ftp.ListAsync(RemotePath.Parent(session.RemotePath), ct), ct).ConfigureAwait(false);
            }
            catch (FtpException ex) when (!ex.IsTransient)
            {
                return null;                                      // Ordner weg
            }
            var name = RemotePath.Name(session.RemotePath);
            return entries.FirstOrDefault(e => e.Name == name);
        }

        private static bool IsReadable(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public void Dispose()
        {
            _changes.Changed -= OnChanged;
            _changes.Dispose();
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~EditSessionManagerTests`
Expected: 11 bestanden.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Editing tests/FileManager.Core.Tests/EditSessionManagerTests.cs
git commit -m "Watch opened files and offer to write changes back to the console

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 17: Virtuelle Dateien für Explorer (`FileManager.Core/Shell`)

**Files:**
- Create: `src/FileManager.Core/Shell/ShellFormats.cs`, `VirtualFile.cs`, `FileDescriptorWriter.cs`, `FtpComStream.cs`, `FormatEnumerator.cs`, `IDataObjectAsyncCapability.cs`, `VirtualFileDataObject.cs`, `ShellInterop.cs` (alle unter `src/FileManager.Core/Shell/`)
- Test: `tests/FileManager.Core.Tests/VirtualFileDataObjectTests.cs`

**Interfaces:**
- Consumes: `IFtpClient` (Task 6), `RemoteItem`, `LocalName` (Task 13), `RemotePath` (Task 8), `InMemoryFtpClient`.
- Produces (Namensraum `FileManager.Core.Shell`):
  - `VirtualFileDataObject(IFtpClient ftp, IReadOnlyList<RemoteItem> items) : System.Runtime.InteropServices.ComTypes.IDataObject, IDataObjectAsyncCapability`; `event EventHandler<int> OperationEnded` (HRESULT, wenn Explorer im Async-Modus fertig ist).
  - `ShellInterop.StartDrag(VirtualFileDataObject data) : int` (gewählter Drop-Effekt; ruft `ole32!DoDragDrop` direkt — STA/UI-Thread), `ShellInterop.SetClipboard(VirtualFileDataObject data)` (`ole32!OleSetClipboard`, **nie** `OleFlushClipboard`).
  - intern: `ShellFormats` (registrierte Formate), `VirtualFile`, `FileDescriptorWriter.Write(IReadOnlyList<VirtualFile>) : byte[]`, `FtpComStream : IStream`, `FormatEnumerator : IEnumFORMATETC`.

**Abweichung von der Spec, bewusst:** Die Spec nennt `Clipboard.SetDataObject(obj, copy: false)` und für das Ziehen implizit `Control.DoDragDrop`. Beide WinForms-Wege wickeln das Objekt in ein eigenes `DataObject`, das `IDataObjectAsyncCapability` **nicht** weiterreicht — Explorer kopierte dann synchron, und bei einem 18-GB-PKG hinge unser UI-Thread in `DoDragDrop`. Darum ruft `ShellInterop` `ole32` direkt. Die Absicht der Spec (nie ein Flush beim Beenden) bleibt: `OleFlushClipboard` wird nirgends aufgerufen. Die App muss beim Start `Application.OleRequired()` aufrufen (Plan 3).

**KORRIGIERT nach dem Review (2026-09-11):** Die ursprüngliche Annahme „verwaltete COM-Objekte sind agil, Explorer ruft auf seinem eigenen Thread" gilt nur innerhalb eines Prozesses. Über Prozessgrenzen wird das Datenobjekt auf dem Thread gemarshallt, der `DoDragDrop`/`OleSetClipboard` aufruft (unser STA-UI-Thread); Explorers `GetData` und jedes `IStream::Read` kommen dort an. Plan 3 muss das Datenobjekt deshalb von einem eigenen STA-Thread mit Nachrichtenschleife veröffentlichen (oder vorauslesen) — siehe Übergabe unten.

**Ohne Fenster nicht prüfbar** (Fahrprobe in Plan 3): das tatsächliche Ziehen nach Explorer, der Kopierdialog, Einfügen per Strg+V in Explorer, Async-Modus.

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/VirtualFileDataObjectTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using FileManager.Core.Shell;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    public class VirtualFileDataObjectTests
    {
        private sealed class Descriptor
        {
            public uint Flags;
            public uint Attributes;
            public long Size;
            public long WriteTime;
            public string Name;
        }

        private static FORMATETC Format(short cf, TYMED tymed, int lindex = -1)
        {
            return new FORMATETC { cfFormat = cf, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = lindex, tymed = tymed, ptd = IntPtr.Zero };
        }

        /// <summary>Holt FileGroupDescriptorW, wie Explorer es täte, und zerlegt die Bytes.</summary>
        private static List<Descriptor> ReadDescriptors(IDataObject data)
        {
            var format = Format(ShellFormats.FileGroupDescriptorW, TYMED.TYMED_HGLOBAL);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            try
            {
                Assert.Equal(TYMED.TYMED_HGLOBAL, medium.tymed);
                int count = Marshal.ReadInt32(medium.unionmember);
                var bytes = new byte[4 + count * FileDescriptorWriter.DescriptorSize];
                Marshal.Copy(medium.unionmember, bytes, 0, bytes.Length);
                var result = new List<Descriptor>();
                for (int i = 0; i < count; i++)
                {
                    int o = 4 + i * FileDescriptorWriter.DescriptorSize;
                    var nameBytes = new byte[520];
                    Array.Copy(bytes, o + 72, nameBytes, 0, 520);
                    var name = Encoding.Unicode.GetString(nameBytes);
                    result.Add(new Descriptor
                    {
                        Flags = BitConverter.ToUInt32(bytes, o),
                        Attributes = BitConverter.ToUInt32(bytes, o + 36),
                        WriteTime = BitConverter.ToUInt32(bytes, o + 56) | ((long)BitConverter.ToUInt32(bytes, o + 60) << 32),
                        Size = ((long)BitConverter.ToUInt32(bytes, o + 64) << 32) | BitConverter.ToUInt32(bytes, o + 68),
                        Name = name.Substring(0, name.IndexOf('\0'))
                    });
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(medium.unionmember);
            }
        }

        private static byte[] ReadContents(IDataObject data, int index)
        {
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, index);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            Assert.Equal(TYMED.TYMED_ISTREAM, medium.tymed);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            Marshal.Release(medium.unionmember);
            return ReadAll(stream);
        }

        private static byte[] ReadAll(IStream stream)
        {
            var result = new List<byte>();
            var buffer = new byte[70000];
            var readPtr = Marshal.AllocCoTaskMem(4);
            try
            {
                while (true)
                {
                    stream.Read(buffer, buffer.Length, readPtr);
                    int n = Marshal.ReadInt32(readPtr);
                    if (n == 0) break;
                    result.AddRange(buffer.Take(n));
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(readPtr);
            }
            return result.ToArray();
        }

        private static byte[] Pattern(int length)
        {
            return Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        }

        private static InMemoryFtpClient Ps4Console()
        {
            return new InMemoryFtpClient()
                .AddFile("/data/a.txt", "abc")
                .AddFile("/data/mods/x.prx", Pattern(150000))
                .AddFile("/data/mods/sub/y.ini", "y");
        }

        private static RemoteItem File(string path, long size) { return new RemoteItem(path, false, size, new DateTime(2026, 9, 11)); }
        private static RemoteItem Dir(string path) { return new RemoteItem(path, true, 0, new DateTime(2026, 9, 11)); }

        [Fact]
        public void The_descriptor_lists_files_and_folders_with_relative_names()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3), Dir("/data/mods") });

            var d = ReadDescriptors(data);

            // Ordinal sortiert: "sub" < "x.prx".
            Assert.Equal(new[] { "a.txt", "mods", "mods\\sub", "mods\\sub\\y.ini", "mods\\x.prx" }, d.Select(x => x.Name).ToArray());
            Assert.Equal(0x10u, d[1].Attributes & 0x10u);                // Ordner
            Assert.Equal(0u, d[0].Attributes & 0x10u);
            Assert.Equal(150000, d[4].Size);
            Assert.NotEqual(0u, d[0].Flags & 0x4000u);                  // FD_PROGRESSUI
            Assert.NotEqual(0u, d[0].Flags & 0x40u);                    // FD_FILESIZE
            Assert.Equal(new DateTime(2026, 9, 11).ToFileTimeUtc(), d[0].WriteTime);
        }

        [Fact]
        public void Sizes_above_4_gb_are_split_into_high_and_low()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/pkg/riesig.pkg", 18143969280L) });
            Assert.Equal(18143969280L, ReadDescriptors(data).Single().Size);
        }

        [Fact]
        public void Folders_are_expanded_only_when_explorer_asks()
        {
            var ftp = Ps4Console();
            var data = new VirtualFileDataObject(ftp, new[] { Dir("/data/mods") });
            Assert.DoesNotContain(ftp.CallsSnapshot(), c => c.StartsWith("LIST"));

            ReadDescriptors(data);

            Assert.Contains("LIST /data/mods", ftp.CallsSnapshot());
        }

        [Fact]
        public void File_contents_stream_straight_from_the_console()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3), Dir("/data/mods") });
            ReadDescriptors(data);

            Assert.Equal(Pattern(150000), ReadContents(data, 4));
            Assert.Equal("y", Encoding.UTF8.GetString(ReadContents(data, 3)));
            Assert.Equal("abc", Encoding.UTF8.GetString(ReadContents(data, 0)));
        }

        [Fact]
        public void The_stream_reports_its_size_and_can_start_over()
        {
            var ftp = Ps4Console();
            var data = new VirtualFileDataObject(ftp, new[] { File("/data/mods/x.prx", 150000) });
            ReadDescriptors(data);
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, 0);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            Marshal.Release(medium.unionmember);

            STATSTG stat;
            stream.Stat(out stat, 1);
            Assert.Equal(150000, stat.cbSize);

            var first = ReadAll(stream);
            stream.Seek(0, 0, IntPtr.Zero);
            var second = ReadAll(stream);

            Assert.Equal(first, second);
            Assert.Equal(2, ftp.CallsSnapshot().Count(c => c == "RETR /data/mods/x.prx@0"));
        }

        [Fact]
        public void Contents_of_a_folder_entry_are_refused()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { Dir("/data/mods") });
            ReadDescriptors(data);
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, 0);
            STGMEDIUM medium;
            var ex = Assert.Throws<COMException>(() => data.GetData(ref format, out medium));
            Assert.Equal(unchecked((int)0x80040068), ex.ErrorCode);     // DV_E_LINDEX
        }

        [Fact]
        public void Only_the_three_formats_are_offered()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3) });

            var text = Format(1, TYMED.TYMED_HGLOBAL);                   // CF_TEXT
            Assert.Equal(unchecked((int)0x80040064), data.QueryGetData(ref text));   // DV_E_FORMATETC
            var descriptor = Format(ShellFormats.FileGroupDescriptorW, TYMED.TYMED_HGLOBAL);
            Assert.Equal(0, data.QueryGetData(ref descriptor));

            var formats = new FORMATETC[5];
            var fetched = new int[1];
            data.EnumFormatEtc(DATADIR.DATADIR_GET).Next(5, formats, fetched);
            Assert.Equal(3, fetched[0]);
            Assert.Equal(new[] { ShellFormats.FileGroupDescriptorW, ShellFormats.FileContents, ShellFormats.PreferredDropEffect },
                formats.Take(3).Select(f => f.cfFormat).ToArray());
        }

        [Fact]
        public void The_preferred_drop_effect_is_copy()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3) });
            var format = Format(ShellFormats.PreferredDropEffect, TYMED.TYMED_HGLOBAL);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            try
            {
                Assert.Equal(1, Marshal.ReadInt32(medium.unionmember));  // DROPEFFECT_COPY
            }
            finally
            {
                Marshal.FreeHGlobal(medium.unionmember);
            }
        }

        [Fact]
        public void Async_mode_is_offered_and_the_end_is_reported()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3) });
            IDataObjectAsyncCapability async = data;
            int? ended = null;
            data.OperationEnded += (s, hr) => ended = hr;

            bool mode;
            async.GetAsyncMode(out mode);
            Assert.True(mode);
            async.StartOperation(IntPtr.Zero);
            bool inOperation;
            async.InOperation(out inOperation);
            Assert.True(inOperation);
            async.EndOperation(0, IntPtr.Zero, 1);

            Assert.Equal(0, ended);
        }

        [Fact]
        public void Names_windows_cannot_store_are_made_safe()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a:b?.txt", "x");
            var data = new VirtualFileDataObject(ftp, new[] { File("/data/a:b?.txt", 1) });
            Assert.Equal("a_b_.txt", ReadDescriptors(data).Single().Name);
            Assert.Equal("x", Encoding.UTF8.GetString(ReadContents(data, 0)));
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~VirtualFileDataObjectTests`
Expected: Build-Fehler — `FileManager.Core.Shell` fehlt.

- [ ] **Step 3: Formate, Beschreibung, Aufzählung**

`src/FileManager.Core/Shell/ShellFormats.cs`:

```csharp
using System.Runtime.InteropServices;

namespace FileManager.Core.Shell
{
    /// <summary>Die registrierten Zwischenablage-Formate der Shell (IDs ab 0xC000 — als short negativ, das ist korrekt).</summary>
    internal static class ShellFormats
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterClipboardFormat(string format);

        public static readonly short FileGroupDescriptorW = Register("FileGroupDescriptorW");
        public static readonly short FileContents = Register("FileContents");
        public static readonly short PreferredDropEffect = Register("Preferred DropEffect");

        private static short Register(string name)
        {
            return unchecked((short)RegisterClipboardFormat(name));
        }
    }
}
```

`src/FileManager.Core/Shell/VirtualFile.cs`:

```csharp
using System;

namespace FileManager.Core.Shell
{
    /// <summary>Ein Eintrag, wie Explorer ihn sieht: relativer Windows-Name, Größe, Datum — und woher die Bytes kommen.</summary>
    internal sealed class VirtualFile
    {
        public VirtualFile(string relativeName, bool isDirectory, long size, DateTime modified, string remotePath)
        {
            RelativeName = relativeName;
            IsDirectory = isDirectory;
            Size = size;
            Modified = modified;
            RemotePath = remotePath;
        }

        public string RelativeName { get; }
        public bool IsDirectory { get; }
        public long Size { get; }
        public DateTime Modified { get; }
        public string RemotePath { get; }
    }
}
```

`src/FileManager.Core/Shell/FileDescriptorWriter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// FILEGROUPDESCRIPTORW von Hand gebaut statt per Struct-Marshaling: UINT
    /// cItems, dann je Eintrag 592 Byte FILEDESCRIPTORW —
    /// dwFlags@0, clsid@4, sizel@20, pointl@28, dwFileAttributes@36,
    /// ftCreationTime@40, ftLastAccessTime@48, ftLastWriteTime@56,
    /// nFileSizeHigh@64, nFileSizeLow@68, cFileName[260]@72.
    /// </summary>
    internal static class FileDescriptorWriter
    {
        public const int DescriptorSize = 592;

        private const uint FdAttributes = 0x00000004;
        private const uint FdWritesTime = 0x00000020;
        private const uint FdFileSize = 0x00000040;
        private const uint FdProgressUi = 0x00004000;
        private const uint FdUnicode = 0x80000000;
        private const uint FileAttributeDirectory = 0x10;
        private const uint FileAttributeNormal = 0x80;
        private const int MaxNameChars = 259;

        public static byte[] Write(IReadOnlyList<VirtualFile> files)
        {
            var buffer = new byte[4 + files.Count * DescriptorSize];
            Put(buffer, 0, (uint)files.Count);

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                int o = 4 + i * DescriptorSize;

                uint flags = FdAttributes | FdProgressUi | FdUnicode;
                if (!file.IsDirectory) flags |= FdFileSize;
                if (file.Modified != DateTime.MinValue) flags |= FdWritesTime;
                Put(buffer, o, flags);
                Put(buffer, o + 36, file.IsDirectory ? FileAttributeDirectory : FileAttributeNormal);

                if (file.Modified != DateTime.MinValue)
                {
                    long fileTime = file.Modified.ToFileTimeUtc();
                    Put(buffer, o + 56, (uint)(fileTime & 0xFFFFFFFF));
                    Put(buffer, o + 60, (uint)(fileTime >> 32));
                }

                if (!file.IsDirectory)
                {
                    Put(buffer, o + 64, (uint)(file.Size >> 32));
                    Put(buffer, o + 68, (uint)(file.Size & 0xFFFFFFFF));
                }

                if (file.RelativeName.Length > MaxNameChars)
                    throw new PathTooLongException("Pfad für Explorer zu lang: " + file.RelativeName);
                Encoding.Unicode.GetBytes(file.RelativeName).CopyTo(buffer, o + 72);   // Rest bleibt 0 = NUL-Ende
            }
            return buffer;
        }

        private static void Put(byte[] buffer, int offset, uint value)
        {
            BitConverter.GetBytes(value).CopyTo(buffer, offset);
        }
    }
}
```

`src/FileManager.Core/Shell/FormatEnumerator.cs`:

```csharp
using System.Runtime.InteropServices.ComTypes;

namespace FileManager.Core.Shell
{
    internal sealed class FormatEnumerator : IEnumFORMATETC
    {
        private readonly FORMATETC[] _formats;
        private int _index;

        public FormatEnumerator(FORMATETC[] formats)
        {
            _formats = formats;
        }

        public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
        {
            int n = 0;
            while (n < celt && _index < _formats.Length) rgelt[n++] = _formats[_index++];
            if (pceltFetched != null && pceltFetched.Length > 0) pceltFetched[0] = n;
            return n == celt ? 0 : 1;                                   // S_OK / S_FALSE
        }

        public int Skip(int celt)
        {
            _index += celt;
            return _index <= _formats.Length ? 0 : 1;
        }

        public int Reset()
        {
            _index = 0;
            return 0;
        }

        public void Clone(out IEnumFORMATETC newEnum)
        {
            newEnum = new FormatEnumerator(_formats) { _index = _index };
        }
    }
}
```

- [ ] **Step 4: Stream, Async-Schnittstelle, Datenobjekt, Interop**

`src/FileManager.Core/Shell/FtpComStream.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Ps4.Core.Ftp;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// IStream über eine Datei auf der Konsole — Explorer liest daraus beim
    /// Kopieren. Die FTP-Verbindung wird erst beim ersten Read geöffnet und am
    /// Dateiende bzw. bei einem Fehler SELBST geschlossen: ein COM-Release ist
    /// aus .NET nicht beobachtbar, darauf zu warten hieße, Verbindungen bis zur
    /// Garbage Collection festzuhalten (Grund, warum die Shell kein Semaphor
    /// hat, siehe FtpGates).
    /// </summary>
    internal sealed class FtpComStream : IStream
    {
        private const int StgEAccessDenied = unchecked((int)0x80030005);
        private const int ENotImpl = unchecked((int)0x80004001);

        private readonly IFtpClient _ftp;
        private readonly string _remotePath;
        private readonly long _size;
        private readonly string _name;
        private Stream _inner;
        private long _position;
        private bool _atEnd;

        public FtpComStream(IFtpClient ftp, string remotePath, long size, string name)
        {
            _ftp = ftp;
            _remotePath = remotePath;
            _size = size;
            _name = name;
        }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int total = ReadCore(pv, cb);
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, total);
        }

        private int ReadCore(byte[] buffer, int count)
        {
            if (_atEnd) return 0;
            int total = 0;
            try
            {
                if (_inner == null) _inner = _ftp.OpenRead(_remotePath, _position);
                while (total < count)
                {
                    int n = _inner.Read(buffer, total, count - total);
                    if (n == 0)
                    {
                        CloseInner();
                        _atEnd = true;
                        break;
                    }
                    total += n;
                }
            }
            catch (Exception)
            {
                CloseInner();
                throw;
            }
            _position += total;
            return total;
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long target;
            switch (dwOrigin)
            {
                case 0: target = dlibMove; break;                 // STREAM_SEEK_SET
                case 1: target = _position + dlibMove; break;     // STREAM_SEEK_CUR
                default: target = _size + dlibMove; break;        // STREAM_SEEK_END
            }
            if (target != _position)
            {
                CloseInner();                                     // beim nächsten Read ab target neu öffnen (REST)
                _position = target;
                _atEnd = target >= _size;
            }
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, target);
        }

        public void Stat(out STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new STATSTG
            {
                type = 2,                                         // STGTY_STREAM
                cbSize = _size,
                grfMode = 0,                                      // STGM_READ
                pwcsName = (grfStatFlag & 1) != 0 ? null : _name  // STATFLAG_NONAME
            };
        }

        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
        {
            var buffer = new byte[81920];
            long done = 0;
            while (done < cb)
            {
                int n = ReadCore(buffer, (int)Math.Min(buffer.Length, cb - done));
                if (n == 0) break;
                pstm.Write(buffer, n, IntPtr.Zero);
                done += n;
            }
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt64(pcbRead, done);
            if (pcbWritten != IntPtr.Zero) Marshal.WriteInt64(pcbWritten, done);
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten) { throw new COMException("Nur lesbar.", StgEAccessDenied); }
        public void SetSize(long libNewSize) { throw new COMException("Nur lesbar.", StgEAccessDenied); }
        public void Commit(int grfCommitFlags) { }
        public void Revert() { }
        public void LockRegion(long libOffset, long cb, int dwLockType) { throw new COMException("", ENotImpl); }
        public void UnlockRegion(long libOffset, long cb, int dwLockType) { throw new COMException("", ENotImpl); }
        public void Clone(out IStream ppstm) { throw new COMException("", ENotImpl); }

        private void CloseInner()
        {
            if (_inner == null) return;
            try { _inner.Dispose(); } catch (Exception) { }
            _inner = null;
        }
    }
}
```

`src/FileManager.Core/Shell/IDataObjectAsyncCapability.cs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// Shell-Schnittstelle (früher IAsyncOperation): bietet ein Datenobjekt
    /// sie an, kopiert Explorer in einem eigenen Hintergrund-Thread und
    /// DoDragDrop kehrt sofort zurück. Reihenfolge der Methoden = VTable.
    /// </summary>
    [ComImport]
    [Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDataObjectAsyncCapability
    {
        void SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool fDoOpAsync);
        void GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool pfIsOpAsync);
        void StartOperation(IntPtr pbcReserved);
        void InOperation([MarshalAs(UnmanagedType.Bool)] out bool pfInAsyncOp);
        void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects);
    }
}
```

`src/FileManager.Core/Shell/VirtualFileDataObject.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// Dateien der Konsole als „virtuelle Dateien" für Explorer (Spec
    /// „Virtuelle Dateien"): FileGroupDescriptorW beschreibt sie, FileContents
    /// liefert je Datei einen IStream direkt vom FTP-Server. Ordner werden erst
    /// ermittelt, wenn Explorer die Beschreibung abruft — also beim Loslassen,
    /// nicht beim Ziehbeginn.
    /// </summary>
    public sealed class VirtualFileDataObject : IDataObject, IDataObjectAsyncCapability
    {
        private const int DvEFormatEtc = unchecked((int)0x80040064);
        private const int DvELIndex = unchecked((int)0x80040068);
        private const int OleEAdviseNotSupported = unchecked((int)0x80040003);
        private const int ENotImpl = unchecked((int)0x80004001);
        private const int DataSSameFormatEtc = 0x00040130;
        private const int DropEffectCopy = 1;

        private readonly IFtpClient _ftp;
        private readonly IReadOnlyList<RemoteItem> _items;
        private readonly object _lock = new object();
        private List<VirtualFile> _files;
        private bool _async = true;
        private bool _inOperation;

        public VirtualFileDataObject(IFtpClient ftp, IReadOnlyList<RemoteItem> items)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (items == null) throw new ArgumentNullException(nameof(items));
            _ftp = ftp;
            _items = items;
        }

        /// <summary>HRESULT von Explorer, wenn ein asynchrones Kopieren endet (0 = Erfolg).</summary>
        public event EventHandler<int> OperationEnded;

        private IReadOnlyList<VirtualFile> Files
        {
            get
            {
                lock (_lock)
                {
                    if (_files == null) _files = Expand();
                    return _files;
                }
            }
        }

        private List<VirtualFile> Expand()
        {
            var result = new List<VirtualFile>();
            foreach (var item in _items)
            {
                var name = LocalName.Sanitize(item.Name);
                result.Add(new VirtualFile(name, item.IsDirectory, item.Size, item.Modified, item.Path));
                if (item.IsDirectory) ExpandDirectory(item.Path, name, result);
            }
            return result;
        }

        private void ExpandDirectory(string remoteDir, string relative, List<VirtualFile> result)
        {
            // Läuft auf Explorers Thread (verwaltete COM-Objekte sind agil) — synchron ist hier richtig.
            var entries = _ftp.ListAsync(remoteDir, CancellationToken.None).GetAwaiter().GetResult();
            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (entry.Kind == FtpEntryKind.Link) continue;           // keine Schleifen
                var path = RemotePath.Combine(remoteDir, entry.Name);
                var name = relative + "\\" + LocalName.Sanitize(entry.Name);
                result.Add(new VirtualFile(name, entry.IsDirectory, entry.Size, entry.Modified, path));
                if (entry.IsDirectory) ExpandDirectory(path, name, result);
            }
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            medium = new STGMEDIUM();

            if (format.cfFormat == ShellFormats.FileGroupDescriptorW && (format.tymed & TYMED.TYMED_HGLOBAL) != 0)
            {
                medium.tymed = TYMED.TYMED_HGLOBAL;
                medium.unionmember = ToHGlobal(FileDescriptorWriter.Write(Files));
                return;
            }

            if (format.cfFormat == ShellFormats.FileContents && (format.tymed & TYMED.TYMED_ISTREAM) != 0)
            {
                var files = Files;
                if (format.lindex < 0 || format.lindex >= files.Count || files[format.lindex].IsDirectory)
                    throw new COMException("Kein Dateiinhalt an diesem Index.", DvELIndex);
                var file = files[format.lindex];
                var stream = new FtpComStream(_ftp, file.RemotePath, file.Size, Path.GetFileName(file.RelativeName));
                medium.tymed = TYMED.TYMED_ISTREAM;
                medium.unionmember = Marshal.GetComInterfaceForObject(stream, typeof(IStream));
                return;
            }

            if (format.cfFormat == ShellFormats.PreferredDropEffect && (format.tymed & TYMED.TYMED_HGLOBAL) != 0)
            {
                medium.tymed = TYMED.TYMED_HGLOBAL;
                medium.unionmember = ToHGlobal(BitConverter.GetBytes(DropEffectCopy));
                return;
            }

            throw new COMException("Format nicht angeboten.", DvEFormatEtc);
        }

        public int QueryGetData(ref FORMATETC format)
        {
            if (format.cfFormat == ShellFormats.FileGroupDescriptorW && (format.tymed & TYMED.TYMED_HGLOBAL) != 0) return 0;
            if (format.cfFormat == ShellFormats.FileContents && (format.tymed & TYMED.TYMED_ISTREAM) != 0) return 0;
            if (format.cfFormat == ShellFormats.PreferredDropEffect && (format.tymed & TYMED.TYMED_HGLOBAL) != 0) return 0;
            return DvEFormatEtc;
        }

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            if (direction != DATADIR.DATADIR_GET) throw new COMException("", ENotImpl);
            return new FormatEnumerator(new[]
            {
                Describe(ShellFormats.FileGroupDescriptorW, TYMED.TYMED_HGLOBAL),
                Describe(ShellFormats.FileContents, TYMED.TYMED_ISTREAM),
                Describe(ShellFormats.PreferredDropEffect, TYMED.TYMED_HGLOBAL)
            });
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
        {
            throw new COMException("", ENotImpl);
        }

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            formatOut.ptd = IntPtr.Zero;
            return DataSSameFormatEtc;
        }

        /// <summary>Explorer meldet „Performed DropEffect"/„Paste Succeeded" — angenommen, freigegeben, ignoriert.</summary>
        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
        {
            if (release) ShellInterop.ReleaseStgMedium(ref medium);
        }

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            connection = 0;
            return OleEAdviseNotSupported;
        }

        public void DUnadvise(int connection)
        {
            throw new COMException("", OleEAdviseNotSupported);
        }

        public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
        {
            enumAdvise = null;
            return OleEAdviseNotSupported;
        }

        public void SetAsyncMode(bool fDoOpAsync) { _async = fDoOpAsync; }
        public void GetAsyncMode(out bool pfIsOpAsync) { pfIsOpAsync = _async; }
        public void StartOperation(IntPtr pbcReserved) { _inOperation = true; }
        public void InOperation(out bool pfInAsyncOp) { pfInAsyncOp = _inOperation; }

        public void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects)
        {
            _inOperation = false;
            var handler = OperationEnded;
            if (handler != null) handler(this, hResult);
        }

        private static FORMATETC Describe(short cf, TYMED tymed)
        {
            return new FORMATETC { cfFormat = cf, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = tymed, ptd = IntPtr.Zero };
        }

        private static IntPtr ToHGlobal(byte[] data)
        {
            var handle = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, handle, data.Length);
            return handle;
        }
    }
}
```

`src/FileManager.Core/Shell/ShellInterop.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// OLE direkt statt über WinForms: WinForms wickelt Datenobjekte in ein
    /// eigenes DataObject, das IDataObjectAsyncCapability verschluckt — dann
    /// kopierte Explorer synchron und DoDragDrop hielte unseren UI-Thread für
    /// die ganze Kopie fest. Beide Aufrufe gehören auf den UI-Thread (STA,
    /// Application.OleRequired() beim Start). OleFlushClipboard wird bewusst
    /// NIE gerufen: das erzwänge beim Beenden das Herunterladen aller Inhalte.
    /// Nur per Fahrprobe prüfbar.
    /// </summary>
    public static class ShellInterop
    {
        public const int DropEffectCopy = 1;

        [DllImport("ole32.dll")]
        private static extern int DoDragDrop(IDataObject pDataObj, IDropSource pDropSource, int dwOkEffects, out int pdwEffect);

        [DllImport("ole32.dll")]
        private static extern int OleSetClipboard(IDataObject pDataObj);

        [DllImport("ole32.dll")]
        internal static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

        public static int StartDrag(VirtualFileDataObject data)
        {
            int effect;
            int hr = DoDragDrop(data, new DropSource(), DropEffectCopy, out effect);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            return effect;
        }

        public static void SetClipboard(VirtualFileDataObject data)
        {
            int hr = OleSetClipboard(data);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
    }

    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, int grfKeyState);

        [PreserveSig]
        int GiveFeedback(int dwEffect);
    }

    internal sealed class DropSource : IDropSource
    {
        private const int SOk = 0;
        private const int DragDropSDrop = 0x00040100;
        private const int DragDropSCancel = 0x00040101;
        private const int DragDropSUseDefaultCursors = 0x00040102;
        private const int MkLButton = 0x1;
        private const int MkRButton = 0x2;

        public int QueryContinueDrag(bool fEscapePressed, int grfKeyState)
        {
            if (fEscapePressed) return DragDropSCancel;
            if ((grfKeyState & (MkLButton | MkRButton)) == 0) return DragDropSDrop;
            return SOk;
        }

        public int GiveFeedback(int dwEffect)
        {
            return DragDropSUseDefaultCursors;
        }
    }
}
```

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter FullyQualifiedName~VirtualFileDataObjectTests`
Expected: 10 bestanden. Mögliche Stolpersteine:
- `Marshal.GetComInterfaceForObject` meldet, der Typ sei nicht COM-sichtbar → `[ComVisible(true)]` an `FtpComStream` und `VirtualFileDataObject` setzen (`using System.Runtime.InteropServices;`), nichts sonst ändern.
- `(IStream)Marshal.GetObjectForIUnknown(...)` schlägt fehl → prüfen, ob `FtpComStream` wirklich `System.Runtime.InteropServices.ComTypes.IStream` implementiert (nicht ein gleichnamiges Interface).

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Shell tests/FileManager.Core.Tests/VirtualFileDataObjectTests.cs
git commit -m "Offer console files to Explorer as virtual files streamed over FTP

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

---

### Task 18: Abschluss — Gesamtlauf, Gallery-Fahrprobe, Übergabe an Plan 3

**Files:**
- Modify: `docs/superpowers/specs/2026-09-11-ps4-file-manager-design.md` (Stand), `README.md` (nur Abschnitt „Bauen": neue Projekte erwähnen)

- [ ] **Step 1: Alles bauen und testen**

```bash
cd /e/Projects/PS4/ScreenshotGallery
dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5
dotnet test ScreenshotGallery.sln 2>&1 | grep -E "Bestanden|Passed|Fehler!|Failed!"
```

Expected: `0 Warnung(en)`, `0 Fehler`; drei Testprojekte, alle grün. Die Zahlen (je Projekt) in den Übergabebericht übernehmen.

- [ ] **Step 2: Gallery-Fahrprobe durch den Anwender — PFLICHT**

Die Gallery wurde angefasst (Herauslösen, neue Fehlerübersetzung im `FtpClient`), hat aber kein App-Testprojekt. Den Anwender bitten, `src/ScreenshotGallery.App/bin/Release/net48/PS4ScreenshotGallery.exe` zu starten und abzuhaken:

1. Verbinden mit 10.10.10.236:2121 → Spiele mit Namen und Icons, Anzahl Screenshots/Clips wie vorher.
2. Ein Bild in der Vorschau öffnen, blättern.
3. Einen Clip abspielen.
4. Ein Bild herunterladen; eins in die Zwischenablage kopieren.
5. Konsole aus bzw. falsche IP → roter Status mit verständlichem Text (jetzt die innerste Ursache wie vorher).
6. **Nicht** löschen, außer an einem frisch geknipsten Wegwerf-Screenshot.

Findet die Fahrprobe einen Fehler: `superpowers:systematic-debugging`, Fix mit Test, erneut Step 1.

- [ ] **Step 3: README ergänzen**

Im Abschnitt „Bauen" von `README.md` nach dem Testbefehl ergänzen:

```markdown
Die Solution enthält inzwischen drei Bibliotheken: `Ps4.Core` (FTP und
Titel-Infos, von beiden Apps geteilt), `ScreenshotGallery.Core` und
`FileManager.Core` (Kern des kommenden PS4 File Managers, noch ohne
Oberfläche).
```

- [ ] **Step 4: Spec-Stand nachtragen**

In `docs/superpowers/specs/2026-09-11-ps4-file-manager-design.md` die Kopfzeile **Status** ergänzen um: „Plan 1 (Kern) umgesetzt am <Datum>, Branch `file-manager-kern` — offen: Plan 2 (UIFramework), Plan 3 (App)". Kein Commit (ignoriert).

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "Mention the new core libraries in the README

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1"
```

- [ ] **Step 6: Branch abschließen**

REQUIRED SUB-SKILL: `superpowers:finishing-a-development-branch` — der Anwender entscheidet über Merge nach `main`.

---

## Übergabe an Plan 3 (`FileManager.App`)

Was die App aus diesem Plan übernehmen **muss** — beim Schreiben von Plan 3 als Anforderungen einarbeiten:

| Punkt | Woher |
|---|---|
| `ServicePointManager.DefaultConnectionLimit = FtpGates.ConnectionLimit` (4) in `Program.Main` | Task 10 |
| `Application.OleRequired()` beim Start, vor jedem `ShellInterop`-Aufruf | Task 17 |
| Alle Schreibwege über **einen** `ZoneGuardFtpClient` um den `FtpClient`; `ZonePolicy.ExpertMode` an die Einstellung koppeln | Task 9 |
| `TransferQueue.AppendResumesUploads` nach Befund Task 2 setzen | Task 2/15 |
| `TransferQueue.JobChanged` feuert auf dem Arbeits-Thread → per `BeginInvoke` auf den UI-Thread | Task 15 |
| Nach jedem fertigen Auftrag: `DirectoryCache.Invalidate` für `RemoteFoldersToRefresh`, `InvalidateBelow` für `RemoteSubtreesGone` | Task 10/13 |
| `EditSessionManager.CleanWorkRoot` beim Start; `Tick()` per Timer (250 ms); `FileSystemChangeSource` in der Produktion; Ablauf siehe Task-16-Interfaces | Task 16 |
| Ziehen nach Explorer: `ShellInterop.StartDrag(new VirtualFileDataObject(guardedFtp, auswahl))` im `ItemDrag`-Handler (Plan 2); Strg+C: `ShellInterop.SetClipboard(...)` | Task 17 |
| Hineinziehen/Strg+V aus Explorer: `CF_HDROP`-Pfade → `FileOperations.PlanUploadAsync` | Task 14 |
| Strg+C/Strg+V innerhalb der PS4 → `FileOperations.PlanCopyWithinPs4()` fangen und die Meldung zeigen | Task 14 |
| `IUserPrompts` und `IConflictResolver` als geskinnte Dialoge (Löschbestätigung zeigt bei `Zone.Warn` die Zonenwarnung mit) | Task 13/14 |
| Titelnamen: `TitleId.IsMatch` auf sichtbare Ordnernamen → `TitleCatalog.GetAsync` mit dem Navigations-Token | Task 7/11 |
| Einstellungen: `%LocalAppData%\PS4FileManager\settings.json` über `FileManagerSettingsStore` | Task 12 |

**Nachtrag aus der Umsetzung und dem Abschluss-Review (2026-09-11) — in Plan 3 früh einplanen:**

| Punkt | Warum |
|---|---|
| `VirtualFileDataObject` von einem **eigenen STA-Thread** mit Nachrichtenschleife veröffentlichen (Zwischenablage; für Drag prüfen, ob ein gemarshallter Proxy an `DoDragDrop` geht) — sonst laufen Ordner-Ermittlung und jedes `IStream::Read` auf dem UI-Thread; Thread-ID in der Fahrprobe loggen | Task 17 Review |
| `CloseOpenStreams()` nur auf dem veröffentlichenden Thread aufrufen; nach synchronem `DoDragDrop` und beim Ersetzen der Zwischenablage; beim Beenden `OleSetClipboard(null)`, falls wir sie noch besitzen (`OleIsCurrentClipboard`) | Task 17 / Abschluss-Review |
| `TransferQueue` im Konstruktor als `ZoneGuardFtpClient` typisieren, damit „jeder Schreibweg durch den Wächter" vom Compiler erzwungen wird | Abschluss-Review |
| Konfliktprüfung auf `/mnt/usb*` ohne Groß-/Kleinschreibung (FAT/exFAT) — sonst überschreibt STOR/RNTO still `README.TXT` mit `Readme.txt` | Abschluss-Review (Important #2) |
| Links: `RemoteItem` trägt die Eintragsart; Links nicht als Ordner öffnen bzw. Schreibzugriffe unter einem Link nach seinem Ziel beurteilen (Zonen-Umgehung über `/data/x -> /system`) | Abschluss-Review (Important #3) |
| Rückschreiben/Ersetzen: erst nach `name.part` hochladen, dann `RNTO` über das Ziel (GoldHEN überschreibt atomar) — sonst hinterlässt ein Abbruch eine halbe `plugins.ini` | Abschluss-Review |
| `EditSessionManager.Register`: `LastWriteTimeUtc`/`Length` merken und Watcher-Ereignisse ohne echte Änderung ignorieren (sonst Frage direkt nach dem Öffnen durch das `.part→name`-Umbenennen) | Abschluss-Review |
| Verschieben mit „Ersetzen": das vorherige `DELE` kann entfallen (RNTO überschreibt verifiziert) | Abschluss-Review |
| `DirectoryCache`-Schlüssel sind `Normalize()`d, nicht `Canonical()` — mit derselben Schreibweise invalidieren, mit der gelistet wurde; nach Invalidate neu anfordern | Task 10 Review |
| Upload-Walker: Laufwerkswurzel `C:\`, Junction-Schleifen (StackOverflow), synchrone Aufzählung — vor dem Verdrahten von Drops aus Explorer beheben | Task 14 Review |
| UTF-8-Namen anderer Clients erscheinen als Mojibake — Anzeige-Umdeutung nur für die Anzeige, Draht-Name bleibt | Task 2 Entscheidung |
| Datum in `FILEDESCRIPTOR`: `ToFileTimeUtc` auf Listing-Daten — in der Fahrprobe gegen Explorer prüfen; Jahre vor 1601 abfangen | Task 17 / Abschluss-Review |

---

## Self-Review (beim Schreiben erledigt)

- **Spec-Abdeckung:** Herauslösen (T1), Konsolenprüfungen (T2), FTP-Erweiterung inkl. Datum/Links/Fehlerklassen (T3–T6), Titel (T7), Pfade/Verlauf (T8), Zonen doppelt durchgesetzt (T9, T14), Verbindungsbudget/Cache (T10), Titelkatalog/Schnellzugriff/Benutzername (T11), Einstellungen (T12), Operationstabelle inkl. Kopier-Verweigerung (T14), Warteschlange mit Konflikten, Bilanz, Abbruch, Fortsetzen (T15), Rückschreiben (T16), virtuelle Dateien (T17). Oberfläche, Symbole, Dialoge, Tastatur, Fahrprobe der App → Plan 3.
- **Bewusste Abweichungen von der Spec** (im jeweiligen Task begründet): `SettingsStore` bleibt in der Gallery; kein Semaphor für die Shell (T10); OLE direkt statt WinForms-Zwischenablage (T17); zusätzlich `LocalName` für Windows-untaugliche PS4-Namen (T13) und `TransferJob.RemoteFoldersToRefresh/RemoteSubtreesGone`, damit die Cache-Invalidierung im testbaren Kern liegt.
- **Typen:** Namen und Signaturen stehen je Task im Interfaces-Block; spätere Tasks verwenden genau diese.


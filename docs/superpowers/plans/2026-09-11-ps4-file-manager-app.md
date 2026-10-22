# PS4 File Manager — App (Plan 3 von 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `PS4FileManager.exe` bauen — ein Explorer-artiger Dateimanager für die PS4 über GoldHENs FTP — und vorher die Pflichtpunkte der Plan-1-Übergabe im Kern erledigen.

**Architecture:** Drei Schichten. (1) Kern-Härtung in `FileManager.Core` (Uploads über `.part`, Links, USB-Schreibweise, Rückschreib-Grundlage, STA-Thread für Explorer). (2) Neue, headless getestete Präsentationslogik in `FileManager.Core/Presentation` (Zeilenmodell, Formate, Navigation, Sitzung, Befehlszustand, Einfüge-Entscheidung) — alles, was ohne Fenster prüfbar ist. (3) Die dünne WinForms-App `src/FileManager.App` auf den UIFramework-Bausteinen aus Plan 2; sie hat wie die Gallery kein Testprojekt und wird per Fahrprobe abgenommen.

**Tech Stack:** C# 7.3, .NET Framework 4.8, WinForms, UIFramework (Core, Controls, Grid, Gallery, Tree — per relativem Projektverweis), `FtpWebRequest` (über `Ps4.Core`), COM/OLE per P/Invoke, xunit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-11-ps4-file-manager-design.md` (vollständig lesen). Dazu die Übergabetabelle am Ende von `docs/superpowers/plans/2026-09-11-ps4-file-manager-kern.md` (Abschnitt „Übergabe an Plan 3") und das Plan-1-Ledger `docs/superpowers/plans/2026-09-11-ps4-file-manager-kern-ledger.md`.

## Global Constraints

- Zielplattform `net48`, `LangVersion 7.3` (aus `Directory.Build.props`) — **keine** C#-8-Features: kein `using var`, keine Switch-Ausdrücke, keine Nullable-Referenztypen, kein `is not`, keine Ranges, keine Default-Interface-Methoden. Ausdrucks-Properties (`=>`) sind erlaubt (C# 6).
- `Task.IsCompletedSuccessfully` gibt es auf net48 **nicht** — `task.Status == TaskStatus.RanToCompletion`.
- `Ps4.Core` und `FileManager.Core` bleiben `<UseWindowsForms>false</UseWindowsForms>` — kein WinForms im Kern.
- Keine NuGet-Pakete in `src/`. Tests nur mit `Microsoft.NET.Test.Sdk` 17.11.1, `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2.
- Testnamen englisch mit Unterstrichen, Kommentare und Doku-Kommentare **deutsch**, alle Anwendertexte **deutsch**.
- Keine eigene Wiederholungslogik im FTP-Weg (Spec).
- **Kein Agent greift auf die echte Konsole zu** (10.10.10.236:2121) — weder lesend noch schreibend. Alles Konsolenseitige ist Fahrprobe des Anwenders; destruktive Proben dort **nur** in einem Wegwerf-Ordner unter `/data`.
- **Kein Agent bedient den Desktop:** keine GUI-Automation, keine UI Automation, keine Maus-/Tastatur-Simulation, keine Screenshots, kein Starten der App oder anderer Fenster (auch nicht „kurz zum Prüfen"). Die Tests des Kerns laufen headless; Fensterprüfungen listet der Implementierer im Bericht als Fahrprobe-Punkte.
- Tests dürfen die **Zwischenablage nicht anfassen** (sie gehört dem Anwender, der nebenher arbeitet).
- Das UIFramework liegt als Nachbar-Repo unter `E:\Projects\UIFramework` (Branch `master`) und wird **nie verändert**.
- Gearbeitet wird auf dem Branch `file-manager-app` (von `main` abzweigen), nie direkt auf `main`.
- `docs/superpowers/` ist per `.gitignore` ausgenommen — Spec und Plan werden nie committet.
- Jede Commit-Nachricht endet mit:
  ```
  Co-Authored-By: <das Modell, das den Commit geschrieben hat> <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EbrmBF7GEDPrESmPe7ffj1
  ```
- Befehle laufen im Repo-Wurzelverzeichnis `E:\Projects\PS4\ScreenshotGallery` (Git Bash). Testlauf gesamt: `dotnet test ScreenshotGallery.sln`. Ausgangsstand vor Task 1: **338/338 grün** (Ps4.Core.Tests 89, ScreenshotGallery.Core.Tests 43, FileManager.Core.Tests 206), Release-Bau ohne Warnungen.
- Nach jedem Task: `dotnet build ScreenshotGallery.sln -c Release` ohne neue Warnungen.

## Entscheidungen dieses Plans (über die Spec hinaus, mit Begründung)

1. **Keine UIFramework-Änderung.** Grid nach dem Neubefüllen neu binden (`DataSource = null; …; DataSource = sorted` — Muster `ExplorerForm.Navigate`, in `offene-punkte.md` dokumentiert), Fortschritt als Textbalken `███████░░░ 78 %` in einer Grid-Zelle (UIFramework hat keinen Fortschrittsbalken), Dialoge als App-eigene kleine `SkinnedForm`s (UIFramework hat keine Meldungsbox).
2. **Verbindung im Ribbon ohne Textfeld:** `RibbonControl` kennt kein Eingabefeld. Die Gruppe „Verbindung" hat „Verbinden"/„Trennen" und einen kleinen Knopf mit dem Ziel („10.10.10.236:2121"), der einen Verbindungsdialog (IP, Port) öffnet — so steht die Adresse sichtbar im Ribbon wie im Spec-Entwurf.
3. **„Helles Design" als Umschalter in „Ansicht"** (nicht gespeichert): Die Fahrprobe verlangt beide Skins; Dark bleibt Standard.
4. **Uploads immer über `<name>.part` und RNTO** (Übergabe „Rückschreiben/Ersetzen"): ein Weg für neue Dateien, Ersetzen und Rückschreiben; das Fortsetzen hängt an die eigene `.part` an.
5. **Verknüpfungen (Links):** Die Liste navigiert über das aufgelöste Ziel; jeder Pfad aus Adressleiste, Einstellungen oder Schnellzugriff läuft durch `PathResolver`, damit Zonen nach dem echten Schreibziel urteilen (Übergabe „Links", Important #3). Links werden nicht nach Explorer angeboten und im Baum nicht als Ordner gezeigt.
6. **Spielordner (Title-ID-Namen) werden nicht umbenannt** — die PS4 findet ein Spiel nur unter seiner ID; die Namensspalte zeigt ohnehin den Spielnamen.
7. **Baum folgt der Liste nicht** (v1): Auswahl im Baum navigiert die Liste; Navigieren in der Liste verändert die Baumauswahl nicht.
8. **DPI wie `ExplorerForm` der UIFramework-Demo** (dort per Fahrprobe bestätigt): `app.manifest` mit PerMonitorV2, **kein** fensterweites `Scale()`. Größen kommen aus den Controls selbst (Adressleiste, Ribbon, Baum, Grid skalieren eigenständig; AutoSize-Knöpfe/-Labels messen ihren Text); die wenigen festen Maße laufen über `AppForm.Px(logisch)` = `DpiScale.Scale(logisch, DeviceDpi)`, gesetzt in `OnLoad`.
9. **Fehlertexte:** `FtpException.Message` ist schon deutscher Klartext — die App zeigt die Nachricht der äußeren Ausnahme, **nicht** die innerste Ursache (die wäre jetzt die englische `WebException`, anders als in der Gallery).
10. **Spielnamen kommen nachträglich:** Die Namensspalte zeigt erst die ID, dann den Namen; die Sortierung wird dafür nicht neu angewandt (die Liste springt nicht unter dem Anwender weg).
11. **Präsentationslogik im Kern** (`FileManager.Core/Presentation`): alles, was sich ohne Fenster prüfen lässt; die App bindet nur.
12. **Statuszeile einfarbig und einzeilig** (ein `SkinLabel` mit `StatusText.Line` statt mehrerer, kein Rot bei Fehlern — `SkinLabel` kennt keine Textfarbe, Entscheidung 1; Fehler beginnen mit „Fehler: " wie in der Gallery). **Lokale Dateinamen = Draht-Name** (über `LocalName.Sanitize`, byte-genau auch bei Mojibake); `DisplayNames` gilt nur für die Anzeige in der App.

---

## File Structure

```
src/
  Ps4.Core/Ftp/FtpClient.cs                    (Task 7: PathEncoding public)
  FileManager.Core/
    Paths/RemotePath.cs                        (Task 1: IsCaseInsensitive, NameComparer, ResolveLink)
    Paths/NavigationHistory.cs                 (Task 9: BackTarget, ForwardTarget)
    Transfers/RemoteItem.cs                    (Task 1: Kind, IsLink, LinkTarget)
    Transfers/TransferQueue.cs                 (Task 2: Wächter-Typ, .part+RNTO, Ersetzen ohne DELE, USB)
    Transfers/FileOperations.cs                (Task 3: Walker, USB, frühe Sperrprüfung)
    Editing/EditSession.cs, EditSessionManager.cs   (Task 4: lokaler Stand)
    Shell/FileDescriptorWriter.cs              (Task 5: Ortszeit, vor 1602 ohne Datum)
    Shell/VirtualFileDataObject.cs             (Task 5: StandardOleMarshalObject, keine Links)
    Shell/ShellInterop.cs                      (Task 6: DoDragDrop mit rohem Zeiger)
    Shell/ShellPublisher.cs                    (Task 6, neu)
    Presentation/DisplayNames.cs               (Task 7, neu)
    Presentation/SortValue.cs                  (Task 8, neu)
    Presentation/SizeFormat.cs                 (Task 8, neu)
    Presentation/FolderRow.cs                  (Task 8, neu)
    Presentation/PathResolver.cs               (Task 9, neu)
    Presentation/FolderBrowser.cs              (Task 9, neu) FolderBrowser, FolderView
    Presentation/ExplorerSession.cs            (Task 10, neu)
    Presentation/StatusText.cs                 (Task 11, neu)
    Presentation/TransferText.cs               (Task 11, neu)
    Presentation/CommandState.cs               (Task 11, neu)
    Presentation/PastePlanner.cs               (Task 12, neu) ClipMode, PasteAction, PastePlanner
  FileManager.App/                             (neu — PS4FileManager.exe)
    FileManager.App.csproj, app.manifest, Program.cs          (Task 13)
    AppPaths.cs, AppForm.cs, Ui.cs                            (Task 13)
    Shell/ShellFileInfo.cs                                    (Task 13)
    Views/NavigationStrip.cs                                  (Task 13)
    Views/EditBox.cs                                          (Task 14)
    Views/TransferPanel.cs                                    (Task 17)
    Dialogs/MessageDialog.cs                                  (Task 13 vorläufig, Task 14 endgültig)
    Dialogs/ConnectionDialog.cs, RenameDialog.cs, DialogPrompts.cs, DialogConflictResolver.cs (Task 14)
    MainForm.cs                    Aufbau, Ribbon, Liste, Befehls-/Statusanzeige, Schließen (Task 13)
    MainForm.Connection.cs         Verbinden/Trennen, Schnellzugriff (Task 15)
    MainForm.Browsing.cs           Baum, Navigation, Filter, Tastenkürzel (Task 15)
    MainForm.Titles.cs             Spielnamen und -symbole (Task 16)
    MainForm.Transfers.cs          Übertragungsleiste (Task 17)
    MainForm.Operations.cs         Neuer Ordner, Umbenennen, Löschen, Hoch-/Herunterladen (Task 18)
    MainForm.Clipboard.cs          Ziehen, Ablegen, Strg+C/X/V (Task 19)
    MainForm.Menus.cs              Kontextmenüs, Anheften, Pfad kopieren (Task 20)
    MainForm.Editing.cs            Öffnen mit Rückschreiben (Task 21)
  (Task 22: README, Spec-Stand, Fahrprobe, Abschluss)
tests/
  FileManager.Core.Tests/
    Support/InMemoryFtpClient.cs               (Task 1: AddLink)
    RemotePathTests.cs, RemoteItemTests.cs     (Task 1)
    TransferQueueTests.cs                      (Task 2)
    FileOperationsTests.cs                     (Task 3)
    EditSessionManagerTests.cs                 (Task 4)
    VirtualFileDataObjectTests.cs              (Task 5)
    ShellPublisherTests.cs                     (Task 6)
    DisplayNamesTests.cs                       (Task 7)
    FolderRowTests.cs                          (Task 8)
    FolderBrowserTests.cs, NavigationHistoryTests.cs (Task 9)
    ExplorerSessionTests.cs                    (Task 10)
    PresentationTextTests.cs                   (Task 11)
    PastePlannerTests.cs                       (Task 12)
```

---

## Phase A — Pflichtpunkte der Plan-1-Übergabe im Kern

### Task 1: Pfade für USB und Links, `RemoteItem` kennt die Eintragsart

**Files:**
- Modify: `src/FileManager.Core/Paths/RemotePath.cs`
- Modify: `src/FileManager.Core/Transfers/RemoteItem.cs`
- Modify: `tests/FileManager.Core.Tests/Support/InMemoryFtpClient.cs`
- Modify: `tests/FileManager.Core.Tests/RemotePathTests.cs`
- Create: `tests/FileManager.Core.Tests/RemoteItemTests.cs`

**Interfaces:**
- Consumes: `RemotePath.Normalize/Combine/IsSameOrBelow`, `FtpEntry(name, kind, size, modified, linkTarget)`, `FtpEntryKind`.
- Produces:
  - `RemotePath.IsCaseInsensitive(string path) : bool` — true für `/mnt/usb0` … `/mnt/usb7` und alles darunter.
  - `RemotePath.NameComparer(string directory) : StringComparer` — `OrdinalIgnoreCase` auf USB, sonst `Ordinal`.
  - `RemotePath.ResolveLink(string linkDirectory, string target) : string` — absolut bleibt absolut (normalisiert), relativ gilt ab `linkDirectory`.
  - `RemoteItem(string path, FtpEntryKind kind, long size, DateTime modified, string linkTarget)`; `RemoteItem.Kind : FtpEntryKind`, `IsDirectory` (jetzt abgeleitet), `IsLink : bool`, `LinkTarget : string` (null außer bei Links). Der alte 4-Argument-Konstruktor bleibt.
  - Test-Fake: `InMemoryFtpClient.AddLink(string path, string target)`; `ListAsync` liefert Links als `FtpEntryKind.Link` (Größe = Länge des Ziels, `LinkTarget` gesetzt); `DeleteAsync` auf einen Link entfernt den Link. Der Fake **folgt Links nicht** (das erledigt später `PathResolver`).

- [ ] **Step 1: Branch anlegen**

```bash
cd /e/Projects/PS4/ScreenshotGallery
git checkout main
git checkout -b file-manager-app
```

- [ ] **Step 2: Failing tests schreiben**

In `tests/FileManager.Core.Tests/RemotePathTests.cs` in der Klasse ergänzen:

```csharp
        [Theory]
        [InlineData("/mnt/usb0", true)]
        [InlineData("/mnt/usb7/Musik/a.mp3", true)]
        [InlineData("/mnt/usb8", false)]
        [InlineData("/mnt/usb", false)]
        [InlineData("/mnt/usb0x", false)]
        [InlineData("/data/a", false)]
        [InlineData("/mnt/ext0/user", false)]
        public void Only_usb_drives_ignore_case(string path, bool expected)
        {
            Assert.Equal(expected, RemotePath.IsCaseInsensitive(path));
        }

        [Fact]
        public void The_name_comparer_follows_the_drive()
        {
            Assert.True(RemotePath.NameComparer("/mnt/usb0").Equals("README.TXT", "Readme.txt"));
            Assert.False(RemotePath.NameComparer("/data").Equals("README.TXT", "Readme.txt"));
        }

        [Theory]
        [InlineData("/data", "/system", "/system")]
        [InlineData("/data/x", "../pkg", "/data/pkg")]
        [InlineData("/data", "GoldHEN/plugins", "/data/GoldHEN/plugins")]
        [InlineData("/data", "/mnt/usb0/", "/mnt/usb0")]
        public void A_link_target_resolves_against_the_folder_of_the_link(string dir, string target, string expected)
        {
            Assert.Equal(expected, RemotePath.ResolveLink(dir, target));
        }

        [Fact]
        public void A_link_without_target_is_refused()
        {
            Assert.Throws<ArgumentException>(() => RemotePath.ResolveLink("/data", ""));
        }
```

(Oben in der Datei `using System;` ergänzen — es fehlt dort bisher.)

`tests/FileManager.Core.Tests/RemoteItemTests.cs` anlegen:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class RemoteItemTests
    {
        [Fact]
        public void From_keeps_the_kind_and_the_link_target()
        {
            var item = RemoteItem.From("/data", new FtpEntry("sys", FtpEntryKind.Link, 7, new DateTime(2026, 9, 11), "/system"));

            Assert.Equal("/data/sys", item.Path);
            Assert.Equal(FtpEntryKind.Link, item.Kind);
            Assert.True(item.IsLink);
            Assert.False(item.IsDirectory);
            Assert.Equal("/system", item.LinkTarget);
        }

        [Fact]
        public void The_old_constructor_still_means_file_or_directory()
        {
            var dir = new RemoteItem("/data/a", true, 0, DateTime.MinValue);
            var file = new RemoteItem("/data/b", false, 3, DateTime.MinValue);

            Assert.Equal(FtpEntryKind.Directory, dir.Kind);
            Assert.True(dir.IsDirectory);
            Assert.Equal(FtpEntryKind.File, file.Kind);
            Assert.False(file.IsLink);
            Assert.Null(file.LinkTarget);
        }

        [Fact]
        public void A_link_target_on_something_else_is_dropped()
        {
            Assert.Null(new RemoteItem("/data/a", FtpEntryKind.File, 1, DateTime.MinValue, "/x").LinkTarget);
        }

        [Fact]
        public async Task The_fake_console_lists_links_with_their_target()
        {
            var ftp = new InMemoryFtpClient().AddDir("/data").AddLink("/data/sys", "/system");

            var entry = (await ftp.ListAsync("/data", CancellationToken.None)).Single();
            var item = RemoteItem.From("/data", entry);

            Assert.True(item.IsLink);
            Assert.Equal("/system", item.LinkTarget);

            await ftp.DeleteAsync("/data/sys", CancellationToken.None);
            Assert.Empty(await ftp.ListAsync("/data", CancellationToken.None));
        }
    }
}
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~RemotePathTests|FullyQualifiedName~RemoteItemTests"`
Expected: Build-Fehler — `IsCaseInsensitive`, `NameComparer`, `ResolveLink`, der 5-Argument-Konstruktor, `Kind`/`IsLink`/`LinkTarget` und `AddLink` fehlen.

- [ ] **Step 4: `RemotePath` erweitern**

In `src/FileManager.Core/Paths/RemotePath.cs` nach den Konstanten `UserData`/`Data`:

```csharp
        private static readonly string[] CaseInsensitiveRoots =
        {
            "/mnt/usb0", "/mnt/usb1", "/mnt/usb2", "/mnt/usb3",
            "/mnt/usb4", "/mnt/usb5", "/mnt/usb6", "/mnt/usb7"
        };
```

und am Ende der Klasse:

```csharp
        /// <summary>
        /// USB-Sticks sind FAT/exFAT: dort ist „README.TXT" dieselbe Datei wie
        /// „Readme.txt" (Abschluss-Review Plan 1, Important #2). Gilt für die
        /// Laufwerkswurzel und alles darunter.
        /// </summary>
        public static bool IsCaseInsensitive(string path)
        {
            var p = Normalize(path);
            foreach (var root in CaseInsensitiveRoots)
            {
                if (IsSameOrBelow(p, root)) return true;
            }
            return false;
        }

        /// <summary>Namensvergleich für Einträge eines Ordners — auf USB ohne Groß-/Kleinschreibung.</summary>
        public static StringComparer NameComparer(string directory)
        {
            return IsCaseInsensitive(directory) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        }

        /// <summary>Ziel einer Verknüpfung als absoluter Pfad: absolut bleibt absolut, relativ gilt ab dem Ordner der Verknüpfung.</summary>
        public static string ResolveLink(string linkDirectory, string target)
        {
            if (string.IsNullOrEmpty(target)) throw new ArgumentException("Die Verknüpfung hat kein Ziel.", nameof(target));
            return target.StartsWith("/", StringComparison.Ordinal) ? Normalize(target) : Combine(linkDirectory, target);
        }
```

- [ ] **Step 5: `RemoteItem` erweitern**

`src/FileManager.Core/Transfers/RemoteItem.cs` ersetzen durch:

```csharp
using System;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// Ein ausgewählter Eintrag auf der Konsole — mit vollem Pfad statt nur
    /// Namen und mit seiner Eintragsart: ein Link ist weder Datei noch Ordner
    /// (Plan-1-Übergabe „Links"; Zonen-Umgehung über /data/x -> /system).
    /// </summary>
    public sealed class RemoteItem
    {
        public RemoteItem(string path, bool isDirectory, long size, DateTime modified)
            : this(path, isDirectory ? FtpEntryKind.Directory : FtpEntryKind.File, size, modified, null)
        {
        }

        public RemoteItem(string path, FtpEntryKind kind, long size, DateTime modified, string linkTarget)
        {
            Path = RemotePath.Normalize(path);
            Kind = kind;
            Size = size;
            Modified = modified;
            LinkTarget = kind == FtpEntryKind.Link ? linkTarget : null;
        }

        public string Path { get; }
        public string Name { get { return RemotePath.Name(Path); } }
        public FtpEntryKind Kind { get; }
        public bool IsDirectory { get { return Kind == FtpEntryKind.Directory; } }
        public bool IsLink { get { return Kind == FtpEntryKind.Link; } }
        public string LinkTarget { get; }
        public long Size { get; }
        public DateTime Modified { get; }

        public static RemoteItem From(string directory, FtpEntry entry)
        {
            return new RemoteItem(RemotePath.Combine(directory, entry.Name), entry.Kind, entry.Size, entry.Modified, entry.LinkTarget);
        }
    }
}
```

- [ ] **Step 6: Links im Test-Fake**

In `tests/FileManager.Core.Tests/Support/InMemoryFtpClient.cs`:

Feld neben `_modified`:

```csharp
        private readonly Dictionary<string, string> _links = new Dictionary<string, string>(StringComparer.Ordinal);
```

Methode nach `AddFile(string path, string text)`:

```csharp
        /// <summary>Eine Verknüpfung (LIST zeigt „l…  name -> ziel"). Der Fake folgt ihr nicht.</summary>
        public InMemoryFtpClient AddLink(string path, string target)
        {
            lock (_lock)
            {
                var p = Norm(path);
                AddDirUnlocked(Parent(p));
                _links[p] = target;
            }
            return this;
        }
```

In `ListAsync` nach der Datei-Schleife (vor `return entries;`):

```csharp
                foreach (var l in _links.Keys.Where(l => Parent(l) == p).OrderBy(l => l, StringComparer.Ordinal))
                    entries.Add(new FtpEntry(Name(l), FtpEntryKind.Link, _links[l].Length, Today, _links[l]));
```

In `DeleteAsync` die Zeile `if (!_files.Remove(p)) throw NotFound();` ersetzen durch:

```csharp
                if (!_files.Remove(p) && !_links.Remove(p)) throw NotFound();
```

- [ ] **Step 7: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~RemotePathTests|FullyQualifiedName~RemoteItemTests"`
Expected: PASS.

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün (338 + 17 neue Testfälle).

- [ ] **Step 8: Commit**

```bash
git add src/FileManager.Core/Paths/RemotePath.cs src/FileManager.Core/Transfers/RemoteItem.cs tests/FileManager.Core.Tests/Support/InMemoryFtpClient.cs tests/FileManager.Core.Tests/RemotePathTests.cs tests/FileManager.Core.Tests/RemoteItemTests.cs
git commit -m "Know USB case rules, link targets and the entry kind of a remote item"
```

---

### Task 2: Warteschlange — nur mit Wächter, Uploads über `.part`, Ersetzen ohne DELE, USB-Schreibweise

**Files:**
- Modify: `src/FileManager.Core/Transfers/TransferQueue.cs`
- Modify: `tests/FileManager.Core.Tests/TransferQueueTests.cs`

**Interfaces:**
- Consumes: `RemotePath.NameComparer` (Task 1), `ZoneGuardFtpClient`, `ZonePolicy`.
- Produces:
  - Konstruktor `TransferQueue(ZoneGuardFtpClient ftp, FtpGates gates, IConflictResolver resolver, IClock clock)` — der erste Parameter ist jetzt der Wächter selbst (Übergabe: „jeder Schreibweg durch den Wächter vom Compiler erzwungen").
  - Verhalten `UploadFile`: Konfliktprüfung gegen den **Zielnamen**; übertragen wird nach `<Ziel>.part` (STOR, beim Fortsetzen SIZE + APPE auf die `.part`), danach **RNTO `<Ziel>.part` → `<Ziel>`**. Ist die `.part` beim Fortsetzen schon vollständig (Abbruch erst beim Umbenennen), wird nur noch umbenannt. Verwerfen löscht die `.part` (nur wenn Bytes übertragen wurden — Regel aus Plan 1 bleibt).
  - Verhalten `RenameRemote` mit „Ersetzen": **kein** `DELE` vorab (RNTO überschreibt, Befund Task 2 Nr. 13).
  - Namensindex je Zielordner mit `RemotePath.NameComparer`: auf `/mnt/usbN` sind „README.TXT" und „Readme.txt" ein Konflikt; eine reine Schreibweisen-Umbenennung derselben Datei („a.txt" → „A.TXT") ist dort **kein** Konflikt.

- [ ] **Step 1: Bestehende Tests an die neuen Regeln anpassen**

In `tests/FileManager.Core.Tests/TransferQueueTests.cs`:

1. Konstruktor der Testklasse — Zeile `_queue = new TransferQueue(_ftp, new FtpGates(), _resolver, new TestClock());` ersetzen durch:
   ```csharp
            _queue = new TransferQueue(new ZoneGuardFtpClient(_ftp, new ZonePolicy()), new FtpGates(), _resolver, new TestClock());
   ```
   Danach per Suche prüfen, dass kein weiterer `new TransferQueue(` im Repo einen ungeschützten Client übergibt (`grep -rn "new TransferQueue(" src tests`) — `A_zone_violation_during_execution_is_a_failed_step` übergibt bereits einen Wächter und bleibt.

2. `A_connection_loss_interrupts_and_resume_appends_the_rest` — die Zeilen nach dem ersten `Run` bzw. nach `Resume` so ändern:
   ```csharp
            Assert.Equal(JobState.Interrupted, job.State);
            Assert.Equal("Verbindung zur PS4 unterbrochen", job.InterruptReason);
            Assert.Equal(100000, _ftp.File("/data/big.bin.part").Length);
            Assert.Null(_ftp.File("/data/big.bin"));             // der echte Name bleibt unberührt

            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(data, _ftp.File("/data/big.bin"));
            Assert.Null(_ftp.File("/data/big.bin.part"));
            Assert.Contains("SIZE /data/big.bin.part", _ftp.CallsSnapshot());
            Assert.Contains("APPE /data/big.bin.part@100000", _ftp.CallsSnapshot());
            Assert.Contains("RNFR /data/big.bin.part -> /data/big.bin", _ftp.CallsSnapshot());
            Assert.Empty(_resolver.Asked);                      // die Teildatei ist unsere — kein Konflikt
   ```

3. `Without_append_a_resumed_upload_starts_over` — `"STOR /data/big.bin"` → `"STOR /data/big.bin.part"`.

4. `Cancelling_a_running_upload_deletes_the_half_file` — die drei Asserts am Ende:
   ```csharp
            Assert.Equal(JobState.Cancelled, job.State);
            Assert.Null(_ftp.File("/data/big.bin"));
            Assert.Null(_ftp.File("/data/big.bin.part"));
            Assert.Contains("DELE /data/big.bin.part", _ftp.CallsSnapshot());
   ```

5. `Jobs_run_one_after_another_and_a_cancelled_waiting_job_is_skipped` — erwartete STORs:
   ```csharp
            Assert.Equal(new[] { "STOR /data/a.txt.part", "STOR /data/c.txt.part" }, stores);
   ```

6. `Cancelling_a_resumed_job_waiting_behind_another_discards_its_partial_upload` — nach dem ersten `Run`: `Assert.Equal(100000, _ftp.File("/data/big.bin.part").Length);` statt `…"/data/big.bin")…`; am Ende zusätzlich `Assert.Null(_ftp.File("/data/big.bin.part"));` (die Zeile `Assert.Null(_ftp.File("/data/big.bin"));` bleibt).

7. `A_transient_failure_before_any_bytes_leaves_the_target_untouched_and_resume_uploads_fresh` und `A_transient_failure_before_any_bytes_leaves_the_target_untouched_on_cancel` — in beiden die Fehlerbedingung `path == "/data/plugins.ini"` durch `path == "/data/plugins.ini.part"` ersetzen (der STOR geht jetzt an die `.part`). Die übrigen Asserts bleiben.

8. `Moving_onto_an_existing_file_can_replace_it` — am Ende ergänzen:
   ```csharp
            Assert.DoesNotContain("DELE /data/ziel/a.txt", _ftp.CallsSnapshot());   // RNTO überschreibt selbst
   ```

9. `A_failing_file_is_noted_and_the_job_goes_on` — die Fehlerbedingung `path == "/data/b.txt"` durch `path == "/data/b.txt.part"` ersetzen (der STOR geht jetzt an die `.part`).

- [ ] **Step 2: Neue Tests schreiben**

Am Ende der Klasse (vor der schließenden Klammer) ergänzen:

```csharp
        // --- Plan 3, Übergabe aus Plan 1 --------------------------------

        [Fact]
        public void The_queue_only_accepts_the_zone_guard()
        {
            var parameter = typeof(TransferQueue).GetConstructors().Single().GetParameters()[0];
            Assert.Equal(typeof(ZoneGuardFtpClient), parameter.ParameterType);
        }

        [Fact]
        public async Task An_upload_goes_to_a_part_file_and_is_renamed_into_place()
        {
            await Run(TransferKind.Upload, Upload(_temp.File("a.txt", "aaa"), "/data/a.txt"));

            var calls = _ftp.CallsSnapshot();
            Assert.Contains("STOR /data/a.txt.part", calls);
            Assert.Contains("RNFR /data/a.txt.part -> /data/a.txt", calls);
            Assert.Equal("aaa", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Null(_ftp.File("/data/a.txt.part"));
        }

        [Fact]
        public async Task Replacing_swaps_the_new_file_in_with_one_rename()
        {
            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Then(ConflictChoice.Replace);

            await Run(TransferKind.Upload, Upload(_temp.File("a.txt", "neu"), "/data/a.txt"));

            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("DELE"));
        }

        [Fact]
        public async Task A_connection_loss_while_renaming_resumes_with_the_rename_only()
        {
            bool failedOnce = false;
            _ftp.Failure = (verb, path) =>
            {
                if (verb == "RNFR" && !failedOnce)
                {
                    failedOnce = true;
                    return new FtpException("Verbindung zur PS4 unterbrochen", FtpErrorKind.Transient, 426);
                }
                return null;
            };

            var job = await Run(TransferKind.Upload, Upload(_temp.File("a.txt", "aaa"), "/data/a.txt"));
            Assert.Equal(JobState.Interrupted, job.State);
            Assert.Equal(3, _ftp.File("/data/a.txt.part").Length);

            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("aaa", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Single(_ftp.CallsSnapshot(), c => c.StartsWith("STOR"));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("APPE"));
        }

        [Fact]
        public async Task On_usb_a_name_differing_only_in_case_is_a_conflict()
        {
            _ftp.AddFile("/mnt/usb0/README.TXT", "alt");
            _resolver.Then(ConflictChoice.Skip);

            await Run(TransferKind.Upload, Upload(_temp.File("Readme.txt", "neu"), "/mnt/usb0/Readme.txt"));

            var asked = Assert.Single(_resolver.Asked);
            Assert.Equal("Readme (2).txt", asked.KeepBothName);
        }

        [Fact]
        public async Task On_the_internal_disk_names_differing_in_case_are_different_files()
        {
            _ftp.AddFile("/data/README.TXT", "alt");

            await Run(TransferKind.Upload, Upload(_temp.File("Readme.txt", "neu"), "/data/Readme.txt"));

            Assert.Empty(_resolver.Asked);
            Assert.Equal("alt", Encoding.UTF8.GetString(_ftp.File("/data/README.TXT")));
        }

        [Fact]
        public async Task On_usb_a_case_only_rename_is_not_a_conflict()
        {
            _ftp.AddFile("/mnt/usb0/a.txt", "a");

            var job = await Run(TransferKind.Rename,
                new TransferStep(StepKind.RenameRemote, "/mnt/usb0/a.txt", null, "/mnt/usb0/A.TXT", 1));

            Assert.Equal(JobState.Completed, job.State);
            Assert.Empty(_resolver.Asked);
            Assert.Contains("RNFR /mnt/usb0/a.txt -> /mnt/usb0/A.TXT", _ftp.CallsSnapshot());
        }
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~TransferQueueTests"`
Expected: FAIL — u. a. `The_queue_only_accepts_the_zone_guard` (Parameter ist noch `IFtpClient`), die `.part`-Erwartungen, `Moving_onto_an_existing_file_can_replace_it` (DELE), die USB-Fälle.

- [ ] **Step 4: Implementieren**

In `src/FileManager.Core/Transfers/TransferQueue.cs`:

1. `using FileManager.Core.Zones;` ergänzen.

2. Feld und Konstruktor:
   ```csharp
        private readonly ZoneGuardFtpClient _ftp;
   ```
   ```csharp
        /// <summary>
        /// Verlangt den Wächter selbst (Abschluss-Review Plan 1): so erzwingt der
        /// Compiler, dass jeder Schreibweg der Warteschlange durch den Zonenschutz läuft.
        /// </summary>
        public TransferQueue(ZoneGuardFtpClient ftp, FtpGates gates, IConflictResolver resolver, IClock clock)
   ```
   (Rumpf unverändert.)

3. In `ExecuteAsync`, Fall `CreateRemoteFolder`, die Zeile mit `index[RemotePath.Normalize(step.RemotePath)] = …` ersetzen durch:
   ```csharp
                        index[RemotePath.Normalize(step.RemotePath)] = new Dictionary<string, FtpEntry>(RemotePath.NameComparer(step.RemotePath));
   ```

4. `UploadAsync` **ab der Zeile `long offset = 0;` (einschließlich — sie steht direkt über dem Kommentar „Erst wenn im vorigen Versuch …")** bis vor `parent[name] = …` ersetzen durch (sonst gäbe es `offset` zweimal, CS0128):
   ```csharp
            // Erst nach <name>.part, dann per RNTO über das Ziel (Abschluss-Review
            // Plan 1): GoldHEN überschreibt beim Umbenennen kommentarlos (Befund
            // Task 2 Nr. 13) — ein Abbruch hinterlässt so nie eine halbe Datei
            // unter dem echten Namen, und Fortsetzen hängt an die eigene .part an.
            var part = step.RemotePath + PartSuffix;
            long offset = 0;
            // Erst wenn im vorigen Versuch wirklich Bytes ankamen, ist die
            // .part auf dem Ziel UNSERE — Connect/Login/STOR können scheitern,
            // bevor auch nur ein Byte ging (Review Runde 1 Nr. 3).
            if (step.Started && step.Transferred > 0 && AppendResumesUploads)
            {
                try { offset = await _ftp.GetFileSizeAsync(part, ct).ConfigureAwait(false); }
                catch (FtpException ex) when (!ex.IsTransient) { offset = 0; }
                if (offset > step.Size) offset = 0;
            }

            step.Started = true;
            step.Transferred = offset;
            var progress = new StepProgress(this, job, step, rate, offset);
            if (offset == 0)
                await _ftp.UploadFromFileAsync(step.LocalPath, part, progress, ct).ConfigureAwait(false);
            else if (offset < step.Size)
                await _ftp.AppendFromFileAsync(step.LocalPath, offset, part, progress, ct).ConfigureAwait(false);
            // offset == Size: die .part ist schon vollständig — der Abbruch kam erst beim Umbenennen.

            await _ftp.RenameAsync(part, step.RemotePath, ct).ConfigureAwait(false);
   ```
   (Danach bleiben `parent[name] = …`, `step.Transferred = step.Size;`, `step.State = StepState.Done;`.)

5. `RenameAsync` (die private Methode für `RenameRemote`) ersetzen durch:
   ```csharp
        private async Task RenameAsync(TransferJob job, TransferStep step,
            Dictionary<string, Dictionary<string, FtpEntry>> index, CancellationToken ct)
        {
            var parentPath = RemotePath.Parent(step.TargetRemotePath);
            var parent = await IndexAsync(parentPath, index, ct).ConfigureAwait(false);
            var name = RemotePath.Name(step.TargetRemotePath);

            FtpEntry existing;
            if (parent.TryGetValue(name, out existing) && !IsSameEntry(step, parentPath, existing))
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
                    // Ersetzen ohne DELE vorab: RNTO überschreibt an der Konsole
                    // kommentarlos (Befund Task 2 Nr. 13, Abschluss-Review Plan 1).
                    parent.Remove(name);
                }
            }

            await _ftp.RenameAsync(step.RemotePath, step.TargetRemotePath, ct).ConfigureAwait(false);
            // Erst die Quelle austragen, dann das Ziel eintragen: bei einer reinen
            // Schreibweisen-Umbenennung auf USB sind beide derselbe Schlüssel.
            Dictionary<string, FtpEntry> source;
            if (index.TryGetValue(RemotePath.Parent(step.RemotePath), out source)) source.Remove(RemotePath.Name(step.RemotePath));
            parent[name] = new FtpEntry(name, step.IsDirectory ? FtpEntryKind.Directory : FtpEntryKind.File, step.Size, DateTime.MinValue, null);
            step.State = StepState.Done;
        }

        /// <summary>
        /// Auf FAT/exFAT (USB) findet der Index bei „a.txt" → „A.TXT" den
        /// Eintrag selbst — das ist kein Konflikt, sondern die Umbenennung.
        /// </summary>
        private static bool IsSameEntry(TransferStep step, string targetParent, FtpEntry existing)
        {
            return RemotePath.Parent(step.RemotePath) == targetParent
                && string.Equals(existing.Name, RemotePath.Name(step.RemotePath), StringComparison.Ordinal);
        }
   ```

6. In `IndexAsync` die Zeile `entries = new Dictionary<string, FtpEntry>(StringComparer.Ordinal);` ersetzen durch:
   ```csharp
            entries = new Dictionary<string, FtpEntry>(RemotePath.NameComparer(key));
   ```

7. In `DiscardPartialAsync` die Upload-Zeile ersetzen durch:
   ```csharp
                if (step.Kind == StepKind.UploadFile && step.Transferred > 0)
                    await _ftp.DeleteAsync(step.RemotePath + PartSuffix, CancellationToken.None).ConfigureAwait(false);
   ```
   und im Kommentar darüber „die fremde, unberührte Zieldatei" durch „eine fremde Datei gleichen .part-Namens" ersetzen.

8. Den Klassenkommentar um einen Satz ergänzen: „Hochgeladen wird immer erst nach `<name>.part` und dann per RNTO umbenannt."

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~TransferQueueTests"`
Expected: PASS (alle bestehenden und die 7 neuen).

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün.

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Transfers/TransferQueue.cs tests/FileManager.Core.Tests/TransferQueueTests.cs
git commit -m "Upload through .part files, require the zone guard and respect USB case rules in the queue"
```

---

### Task 3: Dateioperationen — sicherer Upload-Walker, USB-Schreibweise, gesperrt vor dem Listen

**Files:**
- Modify: `src/FileManager.Core/Transfers/FileOperations.cs`
- Modify: `tests/FileManager.Core.Tests/FileOperationsTests.cs`

**Interfaces:**
- Consumes: `RemotePath.NameComparer` (Task 1), `RemoteItem.IsLink` (Task 1), `InMemoryFtpClient.AddLink` (Task 1).
- Produces (Signaturen unverändert, Verhalten neu):
  - `PlanUploadAsync` läuft per `Task.Run` (die lokale Aufzählung blockiert nie den Aufrufer), lehnt Laufwerkswurzeln mit `ArgumentException` („Ganze Laufwerke lassen sich nicht hochladen: …") ab und folgt in Unterordnern keinen Junctions/Symlinks (Reparse-Points werden übersprungen).
  - `PlanCreateFolderAsync`/`PlanRenameAsync` vergleichen Namen mit `RemotePath.NameComparer(parent)`; eine reine Schreibweisen-Umbenennung auf USB ist erlaubt.
  - `PlanDeleteAsync`, `PlanRenameAsync`, `PlanCreateFolderAsync` werfen `ZoneViolationException` für eine gesperrte Auswahl bzw. ein gesperrtes Ziel **bevor** sie die Konsole listen.
  - Links: Löschen entfernt den Link selbst (DELE), nie sein Ziel; beim rekursiven Löschen/Herunterladen wird keinem Link gefolgt (bestehendes Verhalten, jetzt mit Tests).

- [ ] **Step 1: Failing tests schreiben**

In `tests/FileManager.Core.Tests/FileOperationsTests.cs` oben `using System.Diagnostics;` ergänzen (fehlt bisher; für `Process`) und in der Klasse `FileOperationsTests` ergänzen:

```csharp
        // --- Plan 3, Übergabe aus Plan 1 --------------------------------

        private static void Junction(string link, string target)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using (var process = Process.Start(psi))
            {
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }
        }

        [Fact]
        public async Task A_drive_root_cannot_be_uploaded()
        {
            _ftp.AddDir("/data");
            var root = Path.GetPathRoot(_temp.Path);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _ops.PlanUploadAsync(new[] { root }, "/data", CancellationToken.None));
            Assert.StartsWith("Ganze Laufwerke lassen sich nicht hochladen", ex.Message);
        }

        [Fact]
        public async Task A_junction_inside_an_uploaded_folder_is_not_followed()
        {
            _ftp.AddDir("/data");
            var mods = _temp.Sub("mods");
            _temp.File("mods/a.txt", "aaa");
            Junction(Path.Combine(mods, "schleife"), mods);

            var job = await _ops.PlanUploadAsync(new[] { mods }, "/data", CancellationToken.None);

            Assert.Equal(new[] { "CreateRemoteFolder /data/mods", "UploadFile /data/mods/a.txt" },
                job.Steps.Select(Describe).ToArray());
        }

        [Fact]
        public async Task A_new_folder_on_usb_avoids_a_name_differing_only_in_case()
        {
            _ftp.AddDir("/mnt/usb0/neuer ordner");

            var job = await _ops.PlanCreateFolderAsync("/mnt/usb0", CancellationToken.None);

            Assert.Equal("/mnt/usb0/Neuer Ordner (2)", job.Steps.Single().RemotePath);
        }

        [Fact]
        public async Task On_usb_renaming_only_the_case_is_allowed()
        {
            _ftp.AddFile("/mnt/usb0/a.txt", "a");

            var job = await _ops.PlanRenameAsync(new RemoteItem("/mnt/usb0/a.txt", false, 1, DateTime.MinValue), "A.txt", CancellationToken.None);

            Assert.Equal("/mnt/usb0/A.txt", job.Steps.Single().TargetRemotePath);
        }

        [Fact]
        public async Task On_usb_renaming_onto_a_name_differing_in_case_is_refused()
        {
            _ftp.AddFile("/mnt/usb0/a.txt", "a").AddFile("/mnt/usb0/B.txt", "b");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ops.PlanRenameAsync(new RemoteItem("/mnt/usb0/a.txt", false, 1, DateTime.MinValue), "b.txt", CancellationToken.None));
        }

        [Fact]
        public async Task A_locked_selection_is_refused_before_listing()
        {
            _ftp.AddFile("/system/x/y.bin", "y");

            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanDeleteAsync(new[] { new RemoteItem("/system/x", true, 0, DateTime.MinValue) }, CancellationToken.None));
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanRenameAsync(new RemoteItem("/system/x", true, 0, DateTime.MinValue), "z", CancellationToken.None));
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanCreateFolderAsync("/system", CancellationToken.None));

            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("LIST"));
        }

        [Fact]
        public async Task Deleting_a_link_removes_the_link_not_its_target()
        {
            _ftp.AddFile("/data/echt/a.txt", "a").AddLink("/data/verweis", "/data/echt");
            var link = RemoteItem.From("/data", (await _ftp.ListAsync("/data", CancellationToken.None)).Single(e => e.Name == "verweis"));

            var job = await _ops.PlanDeleteAsync(new[] { link }, CancellationToken.None);

            Assert.Equal(new[] { "DeleteRemoteFile /data/verweis" }, job.Steps.Select(Describe).ToArray());
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c == "LIST /data/verweis" || c == "LIST /data/echt");
        }

        [Fact]
        public async Task A_link_inside_a_deleted_folder_is_not_followed()
        {
            _ftp.AddFile("/data/mods/a.txt", "a").AddLink("/data/mods/schleife", "/data/mods");

            var job = await _ops.PlanDeleteAsync(new[] { new RemoteItem("/data/mods", true, 0, DateTime.MinValue) }, CancellationToken.None);

            Assert.Equal(new[]
            {
                "DeleteRemoteFile /data/mods/a.txt",
                "DeleteRemoteFile /data/mods/schleife",
                "RemoveRemoteFolder /data/mods"
            }, job.Steps.Select(Describe).ToArray());
        }

        [Fact]
        public async Task A_link_inside_a_downloaded_folder_is_skipped()
        {
            _ftp.AddFile("/data/mods/a.txt", "a").AddLink("/data/mods/schleife", "/data/mods");

            var job = await _ops.PlanDownloadAsync(new[] { new RemoteItem("/data/mods", true, 0, DateTime.MinValue) }, _temp.Path, CancellationToken.None);

            Assert.DoesNotContain(job.Steps, s => s.RemotePath.EndsWith("schleife", StringComparison.Ordinal));
        }
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~FileOperationsTests"`
Expected: FAIL — Laufwerkswurzel wird nicht abgelehnt (bzw. anders), Junction wird verfolgt (StackOverflow oder Pfad-zu-lang — falls der Testlauf dabei abstürzt, ist das der erwartete Rot-Befund; im Bericht so vermerken), USB-Fälle, gesperrte Auswahl listet vorher. Die drei Link-Tests dürfen schon grün sein (sie sichern bestehendes Verhalten).

- [ ] **Step 3: Implementieren**

In `src/FileManager.Core/Transfers/FileOperations.cs`:

1. `PlanUploadAsync` ersetzen durch:
   ```csharp
        public Task<TransferJob> PlanUploadAsync(IReadOnlyList<string> localPaths, string remoteDir, CancellationToken ct)
        {
            // Die lokale Aufzählung großer Ordner blockiert — nie auf dem Thread
            // des Aufrufers, der meist der UI-Thread ist (Task-14-Review Plan 1).
            return Task.Run(() => PlanUploadCoreAsync(localPaths, remoteDir, ct), ct);
        }

        private async Task<TransferJob> PlanUploadCoreAsync(IReadOnlyList<string> localPaths, string remoteDir, CancellationToken ct)
        {
            var steps = new List<TransferStep>();
            foreach (var local in localPaths)
            {
                if (IsDriveRoot(local)) throw new ArgumentException("Ganze Laufwerke lassen sich nicht hochladen: " + local, nameof(localPaths));
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

        /// <summary>„C:\" würde zu „C:" gekürzt — und das ist das aktuelle Verzeichnis von C:, nicht das Laufwerk (Task-14-Review Plan 1).</summary>
        private static bool IsDriveRoot(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            return !string.IsNullOrEmpty(root)
                && string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
   ```

2. In `PlanDeleteAsync` als erste Anweisung:
   ```csharp
            // Erst die Auswahl selbst prüfen (Task-14-Review Plan 1): eine gesperrte
            // Auswahl scheitert sofort, statt vorher ihren ganzen Inhalt zu listen.
            var lockedItem = items.FirstOrDefault(i => _policy.Effective(i.Path) == Zone.Locked);
            if (lockedItem != null) throw new ZoneViolationException(lockedItem.Path);
   ```

3. `PlanCreateFolderAsync` ersetzen durch:
   ```csharp
        public async Task<TransferJob> PlanCreateFolderAsync(string parentDir, CancellationToken ct)
        {
            var probe = RemotePath.Combine(parentDir, NewFolderName);
            if (_policy.Effective(probe) == Zone.Locked) throw new ZoneViolationException(probe);

            var entries = await ListGatedAsync(parentDir, ct).ConfigureAwait(false);
            var comparer = RemotePath.NameComparer(parentDir);
            var name = UniqueName.For(NewFolderName, true, n => entries.Any(e => comparer.Equals(e.Name, n)));
            var path = RemotePath.Combine(parentDir, name);
            if (!await ApproveAsync("Neuer Ordner", new[] { path }, ct).ConfigureAwait(false)) return null;
            return new TransferJob(TransferKind.CreateFolder, "Ordner „" + name + "“ anlegen",
                new[] { new TransferStep(StepKind.CreateRemoteFolder, path, null, null, 0) });
        }
   ```

4. In `PlanRenameAsync` nach `if (target == item.Path) return null;` einfügen:
   ```csharp
            if (_policy.Effective(item.Path) == Zone.Locked) throw new ZoneViolationException(item.Path);
            if (_policy.Effective(target) == Zone.Locked) throw new ZoneViolationException(target);
   ```
   und die Kollisionsprüfung ersetzen durch:
   ```csharp
            var entries = await ListGatedAsync(parent, ct).ConfigureAwait(false);
            var comparer = RemotePath.NameComparer(parent);
            // Auf USB trifft „A.txt" auch „a.txt" — ist das der Eintrag selbst, ist es keine Kollision.
            if (entries.Any(e => comparer.Equals(e.Name, newName) && e.Name != item.Name))
                throw new InvalidOperationException("Ein Element mit dem Namen „" + newName + "“ gibt es hier schon.");
   ```

5. In `AddLocalTree` die Unterordner-Schleife ersetzen durch:
   ```csharp
            foreach (var sub in Directory.GetDirectories(localDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                // Junctions/Symlinks nicht verfolgen: eine Schleife liefe sonst bis
                // zum StackOverflow (Task-14-Review Plan 1).
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                AddLocalTree(sub, RemotePath.Combine(remoteDir, Path.GetFileName(sub)), steps);
            }
   ```

6. Über `AddRemoteTreeForDeleteAsync` den Doku-Kommentar ergänzen: „Links sind keine Ordner: sie werden als Eintrag gelöscht (DELE), nie betreten."

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~FileOperationsTests"`
Expected: PASS.

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Transfers/FileOperations.cs tests/FileManager.Core.Tests/FileOperationsTests.cs
git commit -m "Harden upload planning, USB name clashes and early zone refusals in file operations"
```

---

### Task 4: Rückschreiben fragt nur nach echten Änderungen

**Files:**
- Modify: `src/FileManager.Core/Editing/EditSession.cs`
- Modify: `src/FileManager.Core/Editing/EditSessionManager.cs`
- Modify: `tests/FileManager.Core.Tests/EditSessionManagerTests.cs`

**Interfaces:**
- Consumes: `EditSessionManager.Register/Tick/ChangeDetected` (Plan 1).
- Produces (Signaturen unverändert): `Register` merkt sich `LastWriteTimeUtc` und Länge der Arbeitskopie; `Tick` kündigt nur an, wenn sich die Datei gegenüber diesem Stand wirklich geändert hat, und merkt sich dann den neuen Stand. Watcher-Ereignisse ohne Änderung (z. B. das `.part → name`-Umbenennen am Ende des Downloads, Übergabe „EditSession-Grundlage") lösen keine Frage aus. Eine gelöschte Arbeitskopie beendet das Warten (kein ewiges Hängen in `Tick`, Task-16-Minor Plan 1).

- [ ] **Step 1: Bestehende Tests auf echtes Speichern umstellen**

In `tests/FileManager.Core.Tests/EditSessionManagerTests.cs`:

1. Feld und Hilfsmethode in der Klasse ergänzen:
   ```csharp
        private DateTime _saved = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Speichert wie ein Editor: neuer Inhalt, neue Schreibzeit (ausdrücklich
        /// gesetzt — NTFS-Zeitstempel sind für schnelle Folgen zu grob), dann das
        /// Ereignis des Watchers.
        /// </summary>
        private void Save(EditSession session, string text)
        {
            File.WriteAllText(session.LocalPath, text);
            _saved = _saved.AddSeconds(1);
            File.SetLastWriteTimeUtc(session.LocalPath, _saved);
            _changes.Raise(session.LocalPath);
        }
   ```

2. In diesen Tests jedes `_changes.Raise(session.LocalPath);` durch `Save(session, …)` ersetzen, jeweils mit einem **anderen** Text je Aufruf innerhalb eines Tests:
   - `A_change_is_announced_after_one_quiet_second_and_only_once` → `Save(session, "[geändert]");`
   - `A_burst_of_saves_is_one_announcement` → erstes `Save(session, "[eins]");`, zweites `Save(session, "[zwei]");`
   - `A_file_still_locked_by_the_editor_waits` → `Save(session, "[geändert]");`
   - `Completing_a_write_back_moves_the_baseline_and_clears_the_change` → `Save(session, "[geändert]");`
   - `Discard_clears_the_change_but_keeps_watching` → erstes `Save(session, "[eins]");`, zweites `Save(session, "[zwei]");`
   `Changes_to_unknown_files_are_ignored` bleibt unverändert.

- [ ] **Step 2: Neue Tests schreiben**

```csharp
        [Fact]
        public void A_touch_without_a_real_change_is_not_announced()
        {
            var session = OpenPluginsIni();
            _changes.Raise(session.LocalPath);                 // z. B. das .part→Name-Umbenennen

            _clock.Advance(TimeSpan.FromSeconds(2));
            _manager.Tick();

            Assert.Empty(_announced);
            Assert.False(session.HasPendingChange);
        }

        [Fact]
        public void The_same_state_is_not_announced_twice()
        {
            var session = OpenPluginsIni();
            Save(session, "[geändert]");
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();

            _changes.Raise(session.LocalPath);                 // Ereignis ohne neuen Inhalt
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();

            Assert.Single(_announced);
        }

        [Fact]
        public void A_deleted_working_copy_stops_waiting()
        {
            var session = OpenPluginsIni();
            Save(session, "[geändert]");
            File.Delete(session.LocalPath);

            _clock.Advance(TimeSpan.FromSeconds(2));
            _manager.Tick();
            File.WriteAllText(session.LocalPath, "[default]");   // Datei taucht wieder auf — kein Nachzügler
            _manager.Tick();

            Assert.Empty(_announced);
        }
```

- [ ] **Step 3: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~EditSessionManagerTests"`
Expected: FAIL — `A_touch_without_a_real_change_is_not_announced` und `The_same_state_is_not_announced_twice` (heute kündigt jedes Ereignis an); `A_deleted_working_copy_stops_waiting` scheitert, weil der gemerkte Änderungszeitpunkt beim Wiederauftauchen der Datei noch gesetzt ist.

- [ ] **Step 4: Implementieren**

In `src/FileManager.Core/Editing/EditSession.cs` nach `LastChangeUtc`:

```csharp
        /// <summary>
        /// Stand der Arbeitskopie beim letzten Abgleich — Watcher-Ereignisse ohne
        /// echte Änderung (z. B. das .part→Name-Umbenennen des Downloads) lösen
        /// so keine Frage aus (Plan-1-Übergabe).
        /// </summary>
        internal DateTime LocalWriteUtc { get; set; }
        internal long LocalLength { get; set; }
```

In `src/FileManager.Core/Editing/EditSessionManager.cs`:

1. `Register` ersetzen durch:
   ```csharp
        public EditSession Register(string remotePath, string localPath, long size, DateTime modified)
        {
            var session = new EditSession(RemotePath.Normalize(remotePath), localPath, size, modified);
            Stamp(session);
            lock (_lock) _sessions.Add(session);
            return session;
        }
   ```

2. In `Tick` den Schleifenrumpf ersetzen durch:
   ```csharp
                foreach (var session in _sessions)
                {
                    if (!session.LastChangeUtc.HasValue) continue;
                    if (now - session.LastChangeUtc.Value < Quiet) continue;
                    if (!File.Exists(session.LocalPath))
                    {
                        session.LastChangeUtc = null;              // gelöscht — nichts zurückzuschreiben
                        continue;
                    }
                    if (!IsReadable(session.LocalPath)) continue;
                    session.LastChangeUtc = null;
                    if (!ChangedSinceStamp(session)) continue;     // nur berührt, nicht geändert
                    Stamp(session);
                    session.HasPendingChange = true;
                    ready.Add(session);
                }
   ```

3. Neue private Helfer nach `IsReadable`:
   ```csharp
        private static void Stamp(EditSession session)
        {
            var info = new FileInfo(session.LocalPath);
            session.LocalWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            session.LocalLength = info.Exists ? info.Length : -1;
        }

        private static bool ChangedSinceStamp(EditSession session)
        {
            var info = new FileInfo(session.LocalPath);
            if (!info.Exists) return false;               // zwischen Exists-Prüfung und hier weggespeichert — das nächste Ereignis entscheidet
            return info.LastWriteTimeUtc != session.LocalWriteUtc || info.Length != session.LocalLength;
        }
   ```

4. Klassenkommentar ergänzen: „Angekündigt wird nur, was sich gegenüber dem zuletzt gesehenen Stand (Schreibzeit, Länge) wirklich geändert hat."

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~EditSessionManagerTests"`
Expected: PASS.

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün.

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Editing/EditSession.cs src/FileManager.Core/Editing/EditSessionManager.cs tests/FileManager.Core.Tests/EditSessionManagerTests.cs
git commit -m "Announce write-backs only for real changes of the working copy"
```

---

### Task 5: Virtuelle Dateien — Ortszeit, alte Daten, keine Links, Standard-OLE-Marshaling

**Files:**
- Modify: `src/FileManager.Core/Shell/FileDescriptorWriter.cs`
- Modify: `src/FileManager.Core/Shell/VirtualFileDataObject.cs`
- Modify: `tests/FileManager.Core.Tests/VirtualFileDataObjectTests.cs`

**Interfaces:**
- Consumes: `RemoteItem.IsLink` (Task 1).
- Produces:
  - `FILEDESCRIPTOR.ftLastWriteTime` = `Modified.ToFileTime()` (Listing-Datum als **Ortszeit** — Explorer zeigt genau dieses Datum um 00:00); Daten mit Jahr ≤ 1601 bekommen kein `FD_WRITESTIME` (sonst wirft `ToFileTime` und die ganze Übergabe scheitert).
  - `VirtualFileDataObject` erbt `System.Runtime.InteropServices.StandardOleMarshalObject`: COM marshaliert es wie ein normales STA-Objekt — Aufrufe aus anderen Apartments laufen auf dem Thread, dem es gehört (Voraussetzung für Task 6).
  - Links in der Auswahl werden nicht angeboten (ihr gelistetes „Größe"-Feld ist die Länge des Zielpfads, ein Stream läse die Zieldatei abgeschnitten).

- [ ] **Step 1: Failing tests schreiben**

In `tests/FileManager.Core.Tests/VirtualFileDataObjectTests.cs`:

1. Im bestehenden Test `The_descriptor_lists_files_and_folders_with_relative_names` die Zeile
   `Assert.Equal(new DateTime(2026, 9, 11).ToFileTimeUtc(), d[0].WriteTime);` ersetzen durch
   `Assert.Equal(new DateTime(2026, 9, 11).ToFileTime(), d[0].WriteTime);`
   und per `grep -n "ToFileTimeUtc" tests/FileManager.Core.Tests` weitere Stellen genauso umstellen.

2. Neue Tests in der Klasse:
   ```csharp
        [Fact]
        public void A_date_before_1602_is_left_out()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { new RemoteItem("/data/a.txt", false, 3, new DateTime(1601, 1, 1)) });

            var d = ReadDescriptors(data).Single();

            Assert.Equal(0u, d.Flags & 0x20u);                          // kein FD_WRITESTIME
            Assert.Equal(0, d.WriteTime);
        }

        [Fact]
        public void Links_are_not_offered_to_explorer()
        {
            var link = new RemoteItem("/data/verweis", FtpEntryKind.Link, 7, new DateTime(2026, 9, 11), "/system");
            var data = new VirtualFileDataObject(Ps4Console(), new[] { link, File("/data/a.txt", 3) });

            Assert.Equal(new[] { "a.txt" }, ReadDescriptors(data).Select(x => x.Name).ToArray());
        }

        [Fact]
        public void The_data_object_uses_standard_ole_marshaling()
        {
            Assert.IsAssignableFrom<StandardOleMarshalObject>(new VirtualFileDataObject(Ps4Console(), new RemoteItem[0]));
        }
   ```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~VirtualFileDataObjectTests"`
Expected: FAIL — Schreibzeit noch UTC, 1601 wirft bzw. setzt das Flag, der Link erscheint im Descriptor, `StandardOleMarshalObject` ist nicht Basisklasse.

- [ ] **Step 3: Implementieren**

In `src/FileManager.Core/Shell/FileDescriptorWriter.cs` in der Schleife den Block von `uint flags = …` bis einschließlich des `if (file.Modified != DateTime.MinValue) { … }`-Blocks ersetzen durch:

```csharp
                // Listing-Daten sind Ortszeit der Konsole ohne Uhrzeit — als Ortszeit
                // übergeben, damit Explorer genau dieses Datum um 00:00 zeigt
                // (Plan-1-Übergabe). FILETIME beginnt 1601; ToFileTime rechnet in UTC
                // um und würde für den 1.1.1601 östlich von Greenwich werfen.
                bool hasTime = file.Modified != DateTime.MinValue && file.Modified.Year > 1601;

                uint flags = FdAttributes | FdProgressUi | FdUnicode;
                if (!file.IsDirectory) flags |= FdFileSize;
                if (hasTime) flags |= FdWritesTime;
                Put(buffer, o, flags);
                Put(buffer, o + 36, file.IsDirectory ? FileAttributeDirectory : FileAttributeNormal);

                if (hasTime)
                {
                    long fileTime = file.Modified.ToFileTime();
                    Put(buffer, o + 56, (uint)(fileTime & 0xFFFFFFFF));
                    Put(buffer, o + 60, (uint)(fileTime >> 32));
                }
```

In `src/FileManager.Core/Shell/VirtualFileDataObject.cs`:

1. Klassenkopf: `public sealed class VirtualFileDataObject : StandardOleMarshalObject, IDataObject, IDataObjectAsyncCapability` (`StandardOleMarshalObject` liegt in `System.Runtime.InteropServices`, das schon eingebunden ist).

2. Im Konstruktor `_items = items;` ersetzen durch:
   ```csharp
            // Links nicht anbieten: ihr „Größe"-Feld ist die Länge des Zielpfads,
            // ein Stream läse die Zieldatei abgeschnitten (Plan-1-Übergabe „Links").
            _items = items.Where(i => !i.IsLink).ToList();
   ```

3. Den Absatz „Fadenmodell …" im Klassenkommentar ersetzen durch:
   ```
   /// Fadenmodell: Prozessübergreifend marshaliert COM dieses Objekt auf den
   /// Thread, der DoDragDrop/OleSetClipboard aufgerufen bzw. es marshaliert hat —
   /// jeder Aufruf aus Explorer (GetData mit seiner rekursiven Ordnerermittlung,
   /// jedes IStream::Read eines hier ausgegebenen Streams) läuft dort, nicht auf
   /// einem Hintergrund-Thread von Explorer. Als StandardOleMarshalObject gilt
   /// das auch innerhalb des Prozesses. Die App veröffentlicht es deshalb über
   /// ShellPublisher von einem eigenen STA-Thread, den FTP blockieren darf.
   /// Weil ein blockierender COM-Aufruf auf einem STA-Thread eingehende
   /// Nachrichten pumpt, kann Explorer auf demselben Thread erneut in dieses
   /// Objekt hineinrufen (Reentrancy) — die Files-Eigenschaft wehrt das mit
   /// RPC_E_CALL_REJECTED ab, statt die Ordnerermittlung doppelt zu starten.
   ```
   und im Kommentar von `CloseOpenStreams` den Satz „(Plan 3 entscheidet die genaue Anbindung)" durch „(ShellPublisher erledigt beides)" ersetzen.

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~VirtualFileDataObjectTests"`
Expected: PASS.

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Shell/FileDescriptorWriter.cs src/FileManager.Core/Shell/VirtualFileDataObject.cs tests/FileManager.Core.Tests/VirtualFileDataObjectTests.cs
git commit -m "Offer local dates, skip links and use standard OLE marshaling for virtual files"
```

---

### Task 6: `ShellPublisher` — eigener STA-Thread für Zwischenablage und Ziehen

**Files:**
- Create: `src/FileManager.Core/Shell/ShellPublisher.cs`
- Modify: `src/FileManager.Core/Shell/ShellInterop.cs`
- Create: `tests/FileManager.Core.Tests/ShellPublisherTests.cs`

**Interfaces:**
- Consumes: `VirtualFileDataObject` (Task 5: StandardOleMarshalObject), `ShellFormats`, `ShellInterop.ReleaseStgMedium`, `DropSource`.
- Produces:
  - `public sealed class ShellPublisher : IDisposable` — startet im Konstruktor einen Hintergrund-Thread (STA, `OleInitialize`, Win32-Nachrichtenschleife).
  - `int ManagedThreadId { get; }`
  - `T Invoke<T>(Func<T> func)` (blockierend) und `Task<T> InvokeAsync<T>(Func<T> func)` — führen auf dem Publisher-Thread aus; Ausnahmen erreichen den Aufrufer (Stapel erhalten). Nach `Dispose`: `ObjectDisposedException`.
  - `VirtualFileDataObject Create(IFtpClient ftp, IReadOnlyList<RemoteItem> items)` — baut das Datenobjekt auf dem Publisher-Thread.
  - `Task SetClipboardAsync(VirtualFileDataObject data)` — gibt die Streams des bisherigen Inhalts frei und ruft `OleSetClipboard` auf dem Publisher-Thread.
  - `Task<bool> OwnsClipboardAsync(VirtualFileDataObject data)` — `OleIsCurrentClipboard`; gehört die Ablage nicht mehr uns, werden die Streams des alten Inhalts freigegeben.
  - `int DoDragDrop(VirtualFileDataObject data, int allowedEffects)` — **auf dem UI-Thread (STA) aufzurufen**: marshaliert einen Proxy dorthin, ruft OLE `DoDragDrop` mit dem rohen Proxy-Zeiger; danach — wenn kein asynchroner Kopiervorgang läuft — `CloseOpenStreams()` auf dem Publisher-Thread. Rückgabe: der ausgeführte Effekt (`0` = keiner).
  - `Dispose()` — gehört die Zwischenablage noch uns: `OleSetClipboard(null)` (nie `OleFlushClipboard`), Streams freigeben, Schleife beenden.
  - `internal IntPtr MarshalToCaller(VirtualFileDataObject data)` (roher `IDataObject*`-Proxy im aufrufenden Apartment; der Aufrufer gibt ihn mit `Marshal.Release` frei) und `internal bool IsAliveForTests`.
  - `ShellInterop.DropEffectMove = 2`; `internal static int ShellInterop.StartDrag(IntPtr dataObject, int allowedEffects)`.
- Warum ein roher Zeiger und keine Proxy-RCW: Die CLR würde einen Proxy auf ein eigenes verwaltetes Objekt beim Umwandeln in ein .NET-Objekt wieder zum Originalobjekt „auspacken" (IManagedObject) — dann liefen Explorers Aufrufe doch wieder auf dem UI-Thread.

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/ShellPublisherTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Shell;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    /// <summary>
    /// Headless: kein Ziehen (braucht die Maus), keine Zwischenablage (gehört
    /// dem Anwender). Geprüft wird das Fadenmodell — Aufrufe über den Proxy
    /// landen auf dem Publisher-Thread.
    /// </summary>
    public sealed class ShellPublisherTests : IDisposable
    {
        private readonly ShellPublisher _publisher = new ShellPublisher();

        public void Dispose()
        {
            _publisher.Dispose();
        }

        /// <summary>Protokolliert, auf welchem Thread gelistet wird.</summary>
        private sealed class ThreadRecordingFtpClient : IFtpClient
        {
            private readonly IFtpClient _inner;
            public readonly List<int> ListThreads = new List<int>();

            public ThreadRecordingFtpClient(IFtpClient inner) { _inner = inner; }

            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                lock (ListThreads) ListThreads.Add(Thread.CurrentThread.ManagedThreadId);
                return _inner.ListAsync(path, ct);
            }

            public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { return _inner.DownloadAsync(path, ct); }
            public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { return _inner.DownloadToFileAsync(path, localPath, progress, ct); }
            public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { return _inner.DownloadToFileAsync(path, localPath, offset, progress, ct); }
            public Stream OpenRead(string path, long offset) { return _inner.OpenRead(path, offset); }
            public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct) { return _inner.UploadFromFileAsync(localPath, remotePath, progress, ct); }
            public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct) { return _inner.AppendFromFileAsync(localPath, localOffset, remotePath, progress, ct); }
            public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { return _inner.GetFileSizeAsync(path, ct); }
            public Task MakeDirectoryAsync(string path, CancellationToken ct) { return _inner.MakeDirectoryAsync(path, ct); }
            public Task RemoveDirectoryAsync(string path, CancellationToken ct) { return _inner.RemoveDirectoryAsync(path, ct); }
            public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { return _inner.RenameAsync(fromPath, toPath, ct); }
            public Task DeleteAsync(string path, CancellationToken ct) { return _inner.DeleteAsync(path, ct); }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDataFunction(IntPtr self, ref FORMATETC format, out STGMEDIUM medium);

        /// <summary>IDataObject::GetData über die VTable des rohen Proxys — ohne RCW, die ihn auspacken würde.</summary>
        private static int CallGetData(IntPtr dataObject, ref FORMATETC format, out STGMEDIUM medium)
        {
            var vtable = Marshal.ReadIntPtr(dataObject);
            var slot = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);          // nach QueryInterface, AddRef, Release
            var getData = (GetDataFunction)Marshal.GetDelegateForFunctionPointer(slot, typeof(GetDataFunction));
            return getData(dataObject, ref format, out medium);
        }

        [Fact]
        public void Work_runs_on_the_publishers_sta_thread()
        {
            var info = _publisher.Invoke(() => Tuple.Create(Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.GetApartmentState()));

            Assert.Equal(_publisher.ManagedThreadId, info.Item1);
            Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, info.Item1);
            Assert.Equal(ApartmentState.STA, info.Item2);
        }

        [Fact]
        public async Task Async_work_runs_there_too()
        {
            var id = await _publisher.InvokeAsync(() => Thread.CurrentThread.ManagedThreadId);

            Assert.Equal(_publisher.ManagedThreadId, id);
        }

        [Fact]
        public void An_exception_reaches_the_caller()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _publisher.Invoke<int>(() => { throw new InvalidOperationException("kaputt"); }));

            Assert.Equal("kaputt", ex.Message);
        }

        [Fact]
        public void After_dispose_the_thread_ends_and_no_work_is_taken()
        {
            var publisher = new ShellPublisher();
            publisher.Dispose();

            Assert.False(publisher.IsAliveForTests);
            Assert.Throws<ObjectDisposedException>(() => publisher.Invoke(() => 1));
        }

        [Fact]
        public void A_drag_proxy_calls_back_on_the_publisher_thread()
        {
            var ftp = new ThreadRecordingFtpClient(new InMemoryFtpClient().AddFile("/data/mods/a.txt", "a"));
            var data = _publisher.Create(ftp, new[] { new RemoteItem("/data/mods", true, 0, DateTime.MinValue) });
            int hr = -1;
            int callerThread = 0;
            Exception error = null;

            var caller = new Thread(() =>
            {
                try
                {
                    callerThread = Thread.CurrentThread.ManagedThreadId;
                    var proxy = _publisher.MarshalToCaller(data);
                    try
                    {
                        var format = new FORMATETC
                        {
                            cfFormat = ShellFormats.FileGroupDescriptorW,
                            dwAspect = DVASPECT.DVASPECT_CONTENT,
                            lindex = -1,
                            tymed = TYMED.TYMED_HGLOBAL,
                            ptd = IntPtr.Zero
                        };
                        STGMEDIUM medium;
                        hr = CallGetData(proxy, ref format, out medium);
                        if (hr >= 0) ShellInterop.ReleaseStgMedium(ref medium);
                    }
                    finally
                    {
                        Marshal.Release(proxy);
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            caller.SetApartmentState(ApartmentState.STA);
            caller.Start();
            caller.Join();

            Assert.Null(error);
            Assert.Equal(0, hr);
            Assert.NotEqual(callerThread, _publisher.ManagedThreadId);
            Assert.Equal(new[] { _publisher.ManagedThreadId }, ftp.ListThreads.Distinct().ToArray());
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~ShellPublisherTests"`
Expected: Build-Fehler — `ShellPublisher` fehlt.

- [ ] **Step 3: `ShellInterop` ergänzen**

In `src/FileManager.Core/Shell/ShellInterop.cs`:

1. Neben `DropEffectCopy`: `public const int DropEffectMove = 2;`

2. Nach dem bestehenden `DoDragDrop`-Import:
   ```csharp
        [DllImport("ole32.dll", EntryPoint = "DoDragDrop")]
        private static extern int DoDragDropRaw(IntPtr pDataObj, IDropSource pDropSource, int dwOkEffects, out int pdwEffect);
   ```

3. Nach `StartDrag(VirtualFileDataObject data)`:
   ```csharp
        /// <summary>DoDragDrop mit einem rohen (Proxy-)Zeiger — siehe ShellPublisher.DoDragDrop.</summary>
        internal static int StartDrag(IntPtr dataObject, int allowedEffects)
        {
            int effect;
            int hr = DoDragDropRaw(dataObject, new DropSource(), allowedEffects, out effect);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            return effect;
        }
   ```

4. Im Klassenkommentar den Satz „Beide Aufrufe gehören auf den UI-Thread (STA, Application.OleRequired() beim Start)." ersetzen durch „Die App ruft beides über ShellPublisher: die Zwischenablage setzt dessen STA-Thread, DoDragDrop läuft auf dem UI-Thread mit einem Proxy auf das Objekt des Publisher-Threads." und den Absatz „Richtigstellung: … Read-Ahead o. Ä.; siehe VirtualFileDataObject-Klassenkommentar)." ersetzen durch „Explorers Rückrufe (GetData, jedes IStream::Read) laufen auf dem Thread, dem das Datenobjekt gehört — siehe VirtualFileDataObject und ShellPublisher."

- [ ] **Step 4: `ShellPublisher` schreiben**

`src/FileManager.Core/Shell/ShellPublisher.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// Eigener STA-Thread mit Nachrichtenschleife, der die virtuellen Dateien
    /// veröffentlicht (Plan-1-Übergabe): Explorers Rückrufe — die rekursive
    /// Ordnerermittlung in GetData und jedes IStream::Read — laufen auf dem
    /// Thread, dem das Datenobjekt gehört. Das ist dieser, nicht der UI-Thread;
    /// FTP darf ihn blockieren.
    ///
    /// Die Zwischenablage setzt dieser Thread selbst. Fürs Ziehen bekommt der
    /// UI-Thread (der die Maus hält) einen rohen Proxy und ruft DoDragDrop damit;
    /// COM leitet Explorers Aufrufe am UI-Thread vorbei hierher.
    /// </summary>
    public sealed class ShellPublisher : IDisposable
    {
        private const uint WmApp = 0x8000;
        private const uint WmQuit = 0x0012;
        private const uint PmNoRemove = 0x0000;
        private static readonly Guid IidIDataObject = new Guid("0000010e-0000-0000-C000-000000000046");

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public IntPtr Hwnd;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref Msg msg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Msg msg);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint elapseMs, IntPtr timerProc);

        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr reserved);

        [DllImport("ole32.dll")]
        private static extern void OleUninitialize();

        [DllImport("ole32.dll")]
        private static extern int OleSetClipboard(IDataObject data);

        [DllImport("ole32.dll")]
        private static extern int OleIsCurrentClipboard(IDataObject data);

        [DllImport("ole32.dll")]
        private static extern int CoMarshalInterThreadInterfaceInStream([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] object unknown, out IntPtr stream);

        [DllImport("ole32.dll")]
        private static extern int CoGetInterfaceAndReleaseStream(IntPtr stream, [In] ref Guid riid, out IntPtr result);

        private readonly Thread _thread;
        private readonly ConcurrentQueue<Action> _work = new ConcurrentQueue<Action>();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private uint _nativeThreadId;
        private Exception _startError;
        private volatile bool _disposed;
        private VirtualFileDataObject _clipboard;          // nur auf dem Publisher-Thread benutzt

        public ShellPublisher()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Shell-Publisher" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();
            if (_startError != null) throw new InvalidOperationException("Der Shell-Thread ließ sich nicht starten.", _startError);
        }

        public int ManagedThreadId { get { return _thread.ManagedThreadId; } }

        internal bool IsAliveForTests { get { return _thread.IsAlive; } }

        private void Run()
        {
            int hr = OleInitialize(IntPtr.Zero);
            if (hr < 0)
            {
                _startError = Marshal.GetExceptionForHR(hr);
                _ready.Set();
                return;
            }
            try
            {
                Msg msg;
                PeekMessage(out msg, IntPtr.Zero, 0, 0, PmNoRemove);    // Nachrichtenschlange anlegen, bevor jemand postet
                _nativeThreadId = GetCurrentThreadId();
                SetTimer(IntPtr.Zero, UIntPtr.Zero, 250, IntPtr.Zero);   // Herzschlag: eine verschluckte WM_APP wird spätestens beim nächsten WM_TIMER abgearbeitet
                _ready.Set();

                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.Hwnd == IntPtr.Zero && msg.Message == WmApp)
                    {
                        Drain();
                        continue;
                    }
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                    // Falls eine WM_APP-Nachricht in einer fremden Modalschleife unterging.
                    Drain();
                }
                Drain();
            }
            finally
            {
                OleUninitialize();
            }
        }

        private void Drain()
        {
            Action action;
            while (_work.TryDequeue(out action)) action();
        }

        private void Post(Action action)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShellPublisher));
            _work.Enqueue(action);
            if (!PostThreadMessage(_nativeThreadId, WmApp, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("Der Shell-Thread nimmt keine Aufträge mehr an.");
        }

        public T Invoke<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (Thread.CurrentThread == _thread) return func();

            T result = default(T);
            ExceptionDispatchInfo error = null;
            using (var done = new ManualResetEventSlim(false))
            {
                Post(() =>
                {
                    try { result = func(); }
                    catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
                    finally { done.Set(); }
                });
                done.Wait();
            }
            if (error != null) error.Throw();
            return result;
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        /// <summary>Baut das Datenobjekt auf diesem Thread — es gehört dann ihm.</summary>
        public VirtualFileDataObject Create(IFtpClient ftp, IReadOnlyList<RemoteItem> items)
        {
            return Invoke(() => new VirtualFileDataObject(ftp, items));
        }

        public Task SetClipboardAsync(VirtualFileDataObject data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return InvokeAsync(() =>
            {
                // Der bisherige Inhalt wird ersetzt: seine nie zu Ende gelesenen
                // Streams freigeben (Plan-1-Übergabe).
                if (_clipboard != null && !ReferenceEquals(_clipboard, data)) _clipboard.CloseOpenStreams();
                int hr = OleSetClipboard(data);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                _clipboard = data;
                return true;
            });
        }

        /// <summary>Gehört die Zwischenablage noch diesem Inhalt? Ein fremdes Kopieren ersetzt ihn still.</summary>
        public Task<bool> OwnsClipboardAsync(VirtualFileDataObject data)
        {
            return InvokeAsync(() =>
            {
                bool owns = data != null && OleIsCurrentClipboard(data) == 0;       // S_OK = ja, S_FALSE = nein
                if (!owns && _clipboard != null && ReferenceEquals(_clipboard, data))
                {
                    _clipboard.CloseOpenStreams();
                    _clipboard = null;
                }
                return owns;
            });
        }

        /// <summary>
        /// Zieht data per OLE. Aufzurufen auf dem UI-Thread (STA), der die Maus
        /// hält — er bekommt einen Proxy, Explorers Rückrufe gehen an diesen
        /// Thread. Lief danach kein asynchroner Kopiervorgang an (Ziel ohne
        /// IDataObjectAsyncCapability), gibt dieser Thread die offenen Streams
        /// frei (Plan-1-Übergabe).
        /// </summary>
        public int DoDragDrop(VirtualFileDataObject data, int allowedEffects)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("DoDragDrop braucht einen STA-Thread.");

            var proxy = MarshalToCaller(data);
            try
            {
                return ShellInterop.StartDrag(proxy, allowedEffects);
            }
            finally
            {
                Marshal.Release(proxy);
                Invoke(() =>
                {
                    bool inOperation;
                    data.InOperation(out inOperation);
                    if (!inOperation) data.CloseOpenStreams();
                    return true;
                });
            }
        }

        /// <summary>Roher IDataObject-Proxy im Apartment des Aufrufers; der Aufrufer gibt ihn mit Marshal.Release frei.</summary>
        internal IntPtr MarshalToCaller(VirtualFileDataObject data)
        {
            var stream = Invoke(() =>
            {
                var iid = IidIDataObject;
                IntPtr s;
                int hr = CoMarshalInterThreadInterfaceInStream(ref iid, data, out s);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                return s;
            });

            var iidCaller = IidIDataObject;
            IntPtr proxy;
            int hrCaller = CoGetInterfaceAndReleaseStream(stream, ref iidCaller, out proxy);
            if (hrCaller < 0) Marshal.ThrowExceptionForHR(hrCaller);
            return proxy;
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                Invoke(() =>
                {
                    // Beim Beenden die Zwischenablage freigeben, falls sie noch uns
                    // gehört — ohne OleFlushClipboard, das alles herunterlüde (Spec).
                    if (_clipboard != null)
                    {
                        if (OleIsCurrentClipboard(_clipboard) == 0) OleSetClipboard(null);
                        _clipboard.CloseOpenStreams();
                        _clipboard = null;
                    }
                    return true;
                });
            }
            finally
            {
                _disposed = true;
                PostThreadMessage(_nativeThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
                _thread.Join(TimeSpan.FromSeconds(5));
                _ready.Dispose();
            }
        }
    }
}
```

- [ ] **Step 5: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~ShellPublisherTests"`
Expected: PASS (5 Tests). Scheitert `A_drag_proxy_calls_back_on_the_publisher_thread` mit Threads ≠ Publisher, ist das Standard-Marshaling nicht wirksam (Task 5 prüfen) — **nicht** den Test abschwächen, sondern als BLOCKED melden.

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün.

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.Core/Shell/ShellPublisher.cs src/FileManager.Core/Shell/ShellInterop.cs tests/FileManager.Core.Tests/ShellPublisherTests.cs
git commit -m "Publish virtual files from a dedicated STA thread for clipboard and dragging"
```

Fahrprobe (Task 22): Ziehen nach Explorer und Einfügen per Strg+V — die UI bleibt während einer großen Kopie bedienbar.

---

## Phase B — Präsentationslogik im Kern (headless)

### Task 7: Anzeigenamen für UTF-8-Namen anderer Programme

**Files:**
- Modify: `src/Ps4.Core/Ftp/FtpClient.cs` (nur Sichtbarkeit von `PathEncoding`)
- Create: `src/FileManager.Core/Presentation/DisplayNames.cs`
- Create: `tests/FileManager.Core.Tests/DisplayNamesTests.cs`

**Interfaces:**
- Consumes: `FtpClient.PathEncoding` (bisher `internal`).
- Produces:
  - `public static readonly Encoding FtpClient.PathEncoding` (jetzt `public`, Wert unverändert `Encoding.Default`).
  - `public static class DisplayNames` in `FileManager.Core.Presentation` mit `string For(string wireName)`: Liest sich der Draht-Name (so dekodiert, wie `FtpClient` LIST liest) als gültiges UTF-8, wird der UTF-8-Text angezeigt; sonst bleibt der Name. Der Draht-Name bleibt für jede Operation maßgeblich (Übergabe „UTF-8-Namen anderer Clients").

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/DisplayNamesTests.cs`:

```csharp
using System.Text;
using FileManager.Core.Presentation;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class DisplayNamesTests
    {
        /// <summary>So kommt ein Name, den ein anderes Programm als UTF-8 abgelegt hat, aus unserem LIST.</summary>
        private static string AsListedFromUtf8(string name)
        {
            return FtpClient.PathEncoding.GetString(Encoding.UTF8.GetBytes(name));
        }

        [Fact]
        public void A_utf8_name_from_another_program_is_shown_decoded()
        {
            Assert.Equal("Ärger ü.txt", DisplayNames.For(AsListedFromUtf8("Ärger ü.txt")));
        }

        [Fact]
        public void A_name_we_uploaded_ourselves_stays_as_it_is()
        {
            Assert.Equal("Ärger ü.txt", DisplayNames.For("Ärger ü.txt"));
        }

        [Fact]
        public void Ascii_names_are_untouched()
        {
            Assert.Equal("plugins.ini", DisplayNames.For("plugins.ini"));
        }

        [Fact]
        public void Null_and_empty_pass_through()
        {
            Assert.Null(DisplayNames.For(null));
            Assert.Equal("", DisplayNames.For(""));
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~DisplayNamesTests"`
Expected: Build-Fehler — `PathEncoding` ist nicht öffentlich, `DisplayNames` fehlt.

- [ ] **Step 3: Implementieren**

In `src/Ps4.Core/Ftp/FtpClient.cs` die Deklaration `internal static readonly Encoding PathEncoding = Encoding.Default;` in `public static readonly Encoding PathEncoding = Encoding.Default;` ändern (Kommentar darüber bleibt; Satz ergänzen: „Öffentlich, damit die Anzeige UTF-8-Namen anderer Programme erkennen kann (FileManager.Core.Presentation.DisplayNames).").

`src/FileManager.Core/Presentation/DisplayNames.cs`:

```csharp
using System.Text;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Namen für die Anzeige (Spec-Entscheidung zu Befund 6): FtpClient liest
    /// LIST in Windows-1252, damit jeder Name byte-genau wieder trifft. Hat ein
    /// anderes Programm einen Namen als UTF-8 abgelegt, erscheint er so als
    /// „Ã„rger" — hier wird er für die Anzeige zurückgedeutet. Für jede
    /// Operation bleibt der Draht-Name maßgeblich.
    /// </summary>
    public static class DisplayNames
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string For(string wireName)
        {
            if (string.IsNullOrEmpty(wireName)) return wireName;

            bool ascii = true;
            foreach (var c in wireName)
            {
                if (c > 0x7F) { ascii = false; break; }
            }
            if (ascii) return wireName;

            var bytes = FtpClient.PathEncoding.GetBytes(wireName);
            if (FtpClient.PathEncoding.GetString(bytes) != wireName) return wireName;   // nicht verlustfrei rückwandelbar
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return wireName;                                                        // kein UTF-8 — Name wie gelistet
            }
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~DisplayNamesTests"` — PASS.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 5: Commit**

```bash
git add src/Ps4.Core/Ftp/FtpClient.cs src/FileManager.Core/Presentation/DisplayNames.cs tests/FileManager.Core.Tests/DisplayNamesTests.cs
git commit -m "Show UTF-8 names written by other programs decoded"
```

---

### Task 8: Zeilenmodell und Formate der Liste

**Files:**
- Create: `src/FileManager.Core/Presentation/SortValue.cs`
- Create: `src/FileManager.Core/Presentation/SizeFormat.cs`
- Create: `src/FileManager.Core/Presentation/FolderRow.cs`
- Create: `tests/FileManager.Core.Tests/FolderRowTests.cs`

**Interfaces:**
- Consumes: `DisplayNames.For` (Task 7), `RemoteItem.From` (Task 1), `Ps4.Core.Titles.TitleId.IsMatch`.
- Produces:
  - `public sealed class SortValue : IComparable` — `SortValue(string text, IComparable key)`, `Text`, `Key`, `ToString()` = `Text`, `CompareTo` vergleicht die Schlüssel (null zuerst). Das Grid zeichnet `ToString()` und sortiert über `Comparer.Default` — so zeigt eine Zelle „412 KB" und sortiert nach Bytes (Spec „Sortierung mit Rohwerten").
  - `public static class SizeFormat` — `Bytes(long)`: `< 1024` → „338 B", `< 1 MiB` → aufgerundete „KB", `< 1 GiB` → „1,5 MB", sonst „11,1 GB" (deutsches Komma, fest `de-DE`); `Rate(double bytesPerSecond)` → „850 KB/s" / „6,9 MB/s", `""` bei ≤ 0; `Date(DateTime)` → „11.09.2026", `""` bei `DateTime.MinValue`.
  - `public sealed class FolderRow` — `FolderRow(string directory, FtpEntry entry, string typeName)`; `Entry`, `Item : RemoteItem`, `DisplayName`, `TypeName`, `TitleId` (nur Ordner mit Title-ID-Namen, sonst null), `TitleName { get; set; }` (setzt der UI-Thread nachträglich), `IsFolder`, `IsLink`, `IsTitleFolder`, `ShownName` (Spielname, falls bekannt, sonst Anzeigename), `GroupRank` (Ordner 0, sonst 1); Zellen `NameCell`, `ModifiedCell`, `TypeCell`, `SizeCell` (Ordner: leer, Schlüssel −1), `TitleIdCell`.

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/FolderRowTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileManager.Core.Presentation;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class FolderRowTests
    {
        private static FtpEntry File(string name, long size)
        {
            return new FtpEntry(name, FtpEntryKind.File, size, new DateTime(2026, 9, 11), null);
        }

        private static FtpEntry Dir(string name)
        {
            return new FtpEntry(name, FtpEntryKind.Directory, 512, new DateTime(2026, 9, 7), null);
        }

        [Theory]
        [InlineData(0L, "0 B")]
        [InlineData(338L, "338 B")]
        [InlineData(1024L, "1 KB")]
        [InlineData(1025L, "2 KB")]
        [InlineData(1572864L, "1,5 MB")]
        [InlineData(11873615872L, "11,1 GB")]
        public void Sizes_read_like_in_explorer(long bytes, string expected)
        {
            Assert.Equal(expected, SizeFormat.Bytes(bytes));
        }

        [Fact]
        public void Rates_and_dates_are_german()
        {
            Assert.Equal("6,9 MB/s", SizeFormat.Rate(6.9 * 1024 * 1024));
            Assert.Equal("850 KB/s", SizeFormat.Rate(850 * 1024));
            Assert.Equal("", SizeFormat.Rate(0));
            Assert.Equal("11.09.2026", SizeFormat.Date(new DateTime(2026, 9, 11)));
            Assert.Equal("", SizeFormat.Date(DateTime.MinValue));
        }

        [Fact]
        public void Sizes_sort_by_bytes_not_by_text()
        {
            var cells = new List<SortValue>
            {
                new FolderRow("/data", File("gross.pkg", 2048), "PKG").SizeCell,
                new FolderRow("/data", File("klein.ini", 338), "Konfiguration").SizeCell
            };

            cells.Sort((a, b) => System.Collections.Comparer.Default.Compare(a, b));

            Assert.Equal(new[] { "338 B", "2 KB" }, cells.Select(c => c.ToString()).ToArray());
        }

        [Fact]
        public void A_game_folder_is_recognised_and_shows_its_name_once_known()
        {
            var row = new FolderRow("/mnt/ext0/user/app", Dir("CUSA00411"), "Dateiordner");

            Assert.True(row.IsTitleFolder);
            Assert.Equal("CUSA00411", row.TitleId);
            Assert.Equal("CUSA00411", row.ShownName);

            row.TitleName = "Grand Theft Auto V";

            Assert.Equal("Grand Theft Auto V", row.ShownName);
            Assert.Equal("Grand Theft Auto V", row.NameCell.ToString());
            Assert.Equal("CUSA00411", row.TitleIdCell.ToString());
        }

        [Fact]
        public void A_file_named_like_a_title_is_no_game_folder()
        {
            Assert.Null(new FolderRow("/data", File("CUSA00411", 1), "Datei").TitleId);
        }

        [Fact]
        public void Folders_rank_before_files_and_have_no_size()
        {
            var folder = new FolderRow("/data", Dir("GoldHEN"), "Dateiordner");
            var file = new FolderRow("/data", File("a.txt", 3), "Textdokument");

            Assert.Equal(0, folder.GroupRank);
            Assert.Equal(1, file.GroupRank);
            Assert.Equal("", folder.SizeCell.ToString());
            Assert.Equal("/data/GoldHEN", folder.Item.Path);
            Assert.Equal("07.09.2026", folder.ModifiedCell.ToString());
        }

        [Fact]
        public void A_utf8_name_is_shown_decoded_but_the_item_keeps_the_wire_name()
        {
            var wire = FtpClient.PathEncoding.GetString(Encoding.UTF8.GetBytes("Ärger.txt"));
            var row = new FolderRow("/data", File(wire, 1), "Textdokument");

            Assert.Equal("Ärger.txt", row.DisplayName);
            Assert.Equal("/data/" + wire, row.Item.Path);
        }

        [Fact]
        public void A_link_is_marked()
        {
            var row = new FolderRow("/data", new FtpEntry("sys", FtpEntryKind.Link, 7, DateTime.MinValue, "/system"), "Verknüpfung");

            Assert.True(row.IsLink);
            Assert.False(row.IsFolder);
            Assert.Equal("", row.ModifiedCell.ToString());
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~FolderRowTests"`
Expected: Build-Fehler — `SizeFormat`, `SortValue`, `FolderRow` fehlen.

- [ ] **Step 3: Implementieren**

`src/FileManager.Core/Presentation/SortValue.cs`:

```csharp
using System;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Zellwert fürs Grid: zeigt Text, sortiert nach Schlüssel (Spec „Sortierung
    /// per Spaltenkopf mit Rohwerten"). Das Grid zeichnet ToString() und
    /// vergleicht über Comparer.Default — also über CompareTo.
    /// </summary>
    public sealed class SortValue : IComparable
    {
        public SortValue(string text, IComparable key)
        {
            Text = text ?? "";
            Key = key;
        }

        public string Text { get; }
        public IComparable Key { get; }

        public int CompareTo(object obj)
        {
            var other = obj as SortValue;
            if (other == null) return 1;
            if (Key == null) return other.Key == null ? 0 : -1;
            if (other.Key == null) return 1;
            return Key.CompareTo(other.Key);
        }

        public override string ToString()
        {
            return Text;
        }
    }
}
```

`src/FileManager.Core/Presentation/SizeFormat.cs`:

```csharp
using System;
using System.Globalization;

namespace FileManager.Core.Presentation
{
    /// <summary>Größen, Raten und Daten wie im deutschen Explorer — fest de-DE, unabhängig von der Rechnereinstellung.</summary>
    public static class SizeFormat
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
        private const double Kib = 1024.0;
        private const double Mib = 1024.0 * 1024;
        private const double Gib = 1024.0 * 1024 * 1024;

        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(German) + " B";
            if (bytes < 1024L * 1024) return ((bytes + 1023) / 1024).ToString(German) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / Mib).ToString("0.0", German) + " MB";
            return (bytes / Gib).ToString("0.0", German) + " GB";
        }

        public static string Rate(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "";
            if (bytesPerSecond < Mib) return (bytesPerSecond / Kib).ToString("0", German) + " KB/s";
            return (bytesPerSecond / Mib).ToString("0.0", German) + " MB/s";
        }

        public static string Date(DateTime date)
        {
            return date == DateTime.MinValue ? "" : date.ToString("dd.MM.yyyy", German);
        }
    }
}
```

`src/FileManager.Core/Presentation/FolderRow.cs`:

```csharp
using System;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Eine Zeile der Liste (Details wie Kacheln): Draht-Eintrag, Anzeigename,
    /// Typname (liefert die App aus der Shell), Spiel-ID und — sobald geladen —
    /// Spielname (Spec „Titelnamen": Name statt ID, ID in eigener Spalte).
    /// TitleName setzt nur der UI-Thread.
    /// </summary>
    public sealed class FolderRow
    {
        public FolderRow(string directory, FtpEntry entry, string typeName)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            Entry = entry;
            Item = RemoteItem.From(directory, entry);
            DisplayName = DisplayNames.For(entry.Name);
            TypeName = typeName ?? "";
            TitleId = entry.IsDirectory && global::Ps4.Core.Titles.TitleId.IsMatch(entry.Name) ? entry.Name : null;
        }

        public FtpEntry Entry { get; }
        public RemoteItem Item { get; }
        public string DisplayName { get; }
        public string TypeName { get; }
        public string TitleId { get; }
        public string TitleName { get; set; }

        public bool IsFolder { get { return Entry.IsDirectory; } }
        public bool IsLink { get { return Entry.Kind == FtpEntryKind.Link; } }
        public bool IsTitleFolder { get { return TitleId != null; } }
        public string ShownName { get { return string.IsNullOrEmpty(TitleName) ? DisplayName : TitleName; } }
        public int GroupRank { get { return IsFolder ? 0 : 1; } }

        public SortValue NameCell { get { return new SortValue(ShownName, ShownName); } }
        public SortValue ModifiedCell { get { return new SortValue(SizeFormat.Date(Entry.Modified), Entry.Modified); } }
        public SortValue TypeCell { get { return new SortValue(TypeName, TypeName); } }
        public SortValue SizeCell { get { return IsFolder ? new SortValue("", -1L) : new SortValue(SizeFormat.Bytes(Entry.Size), Entry.Size); } }
        public SortValue TitleIdCell { get { return new SortValue(TitleId ?? "", TitleId ?? ""); } }
    }
}
```

(Hinweis: Innerhalb von `FolderRow` heißt eine Eigenschaft `TitleId`, und innerhalb von `FileManager.Core.*` bezeichnet `Ps4` den Namensraum `FileManager.Core.Ps4` — deshalb steht der Aufruf als `global::Ps4.Core.Titles.TitleId.IsMatch`.)

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~FolderRowTests"` — PASS.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Presentation/SortValue.cs src/FileManager.Core/Presentation/SizeFormat.cs src/FileManager.Core/Presentation/FolderRow.cs tests/FileManager.Core.Tests/FolderRowTests.cs
git commit -m "Model list rows with sortable cells, German sizes and game names"
```

---

### Task 9: Navigation — Links auflösen, Verlauf, Ordneransicht

**Files:**
- Modify: `src/FileManager.Core/Paths/NavigationHistory.cs`
- Create: `src/FileManager.Core/Presentation/PathResolver.cs`
- Create: `src/FileManager.Core/Presentation/FolderBrowser.cs`
- Modify: `tests/FileManager.Core.Tests/NavigationHistoryTests.cs`
- Create: `tests/FileManager.Core.Tests/FolderBrowserTests.cs`

**Interfaces:**
- Consumes: `DirectoryCache.ListAsync/Invalidate`, `ZonePolicy.Effective`, `RemotePath.ResolveLink` (Task 1), `InMemoryFtpClient.AddLink` (Task 1).
- Produces:
  - `NavigationHistory.BackTarget : string` / `ForwardTarget : string` — wohin Zurück/Vor ginge, ohne den Verlauf zu ändern (null, wenn nicht möglich).
  - `public static class PathResolver` — `const int MaxLinkHops = 8`; `Task<string> ResolveAsync(DirectoryCache cache, string path, CancellationToken ct)`: ersetzt jede Verknüpfung im Pfad durch ihr Ziel (listet dazu jeden Vorfahren über den Cache); mehr als 8 Sprünge → `InvalidOperationException("Zu viele verschachtelte Verknüpfungen: …")`; ein fehlender Vorfahr bricht die Auflösung still ab (das spätere LIST meldet ihn).
  - `public sealed class FolderView` — `Path`, `Entries : IReadOnlyList<FtpEntry>`, `Zone` (Zone fürs Schreiben **in** den Ordner).
  - `public sealed class FolderBrowser` — `FolderBrowser(DirectoryCache cache, ZonePolicy policy)`; `CurrentPath` (null vor der ersten Navigation), `CanGoBack`, `CanGoForward`, `CanGoUp`; `Task<FolderView> NavigateAsync(string path, CancellationToken ct)`, `BackAsync(ct)`, `ForwardAsync(ct)`, `UpAsync(ct)`, `RefreshAsync(ct)`; `static Zone ContentsZone(ZonePolicy policy, string folder)`. Der Verlauf ändert sich **erst nach** erfolgreichem Listen; eine abgebrochene oder gescheiterte Navigation lässt den Ort stehen. Aufrufer ist der UI-Thread (kein `ConfigureAwait(false)` in `FolderBrowser`, damit der Zustand nur dort wechselt).

- [ ] **Step 1: Failing tests schreiben**

In `tests/FileManager.Core.Tests/NavigationHistoryTests.cs` ergänzen:

```csharp
        [Fact]
        public void The_targets_show_where_back_and_forward_would_go_without_moving()
        {
            var history = new NavigationHistory();
            Assert.Null(history.BackTarget);
            history.Navigate("/data");
            history.Navigate("/data/pkg");

            Assert.Equal("/data", history.BackTarget);
            Assert.Null(history.ForwardTarget);
            Assert.Equal("/data/pkg", history.Current);

            history.GoBack();
            Assert.Equal("/data/pkg", history.ForwardTarget);
        }
```

`tests/FileManager.Core.Tests/FolderBrowserTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~FolderBrowserTests|FullyQualifiedName~NavigationHistoryTests"`
Expected: Build-Fehler — `BackTarget`, `ForwardTarget`, `PathResolver`, `FolderBrowser` fehlen.

- [ ] **Step 3: Implementieren**

In `src/FileManager.Core/Paths/NavigationHistory.cs` nach `CanGoForward`:

```csharp
        /// <summary>Wohin Zurück ginge — ohne den Verlauf zu ändern; null, wenn nicht möglich.</summary>
        public string BackTarget { get { return _back.Count > 0 ? _back.Peek() : null; } }

        /// <summary>Wohin Vor ginge — ohne den Verlauf zu ändern; null, wenn nicht möglich.</summary>
        public string ForwardTarget { get { return _forward.Count > 0 ? _forward.Peek() : null; } }
```

`src/FileManager.Core/Presentation/PathResolver.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Ersetzt Verknüpfungen im Pfad durch ihr Ziel (Plan-1-Übergabe „Links",
    /// Abschluss-Review Important #3): Zonen urteilen nach dem Pfad, in den
    /// wirklich geschrieben würde — „/data/x" → „/system" darf nicht als frei
    /// durchgehen. Listet dazu jeden Vorfahren einmal (über den Cache).
    /// </summary>
    public static class PathResolver
    {
        public const int MaxLinkHops = 8;

        public static async Task<string> ResolveAsync(DirectoryCache cache, string path, CancellationToken ct)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            var current = RemotePath.Normalize(path);
            for (int hop = 0; hop <= MaxLinkHops; hop++)
            {
                var next = await ResolveFirstLinkAsync(cache, current, ct).ConfigureAwait(false);
                if (next == null) return current;
                current = next;
            }
            throw new InvalidOperationException("Zu viele verschachtelte Verknüpfungen: " + RemotePath.Normalize(path));
        }

        /// <summary>Löst die erste Verknüpfung im Pfad auf; null, wenn keine (mehr) vorkommt.</summary>
        private static async Task<string> ResolveFirstLinkAsync(DirectoryCache cache, string path, CancellationToken ct)
        {
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var dir = RemotePath.Root;
            for (int i = 0; i < segments.Length; i++)
            {
                System.Collections.Generic.IReadOnlyList<FtpEntry> entries;
                try
                {
                    entries = await cache.ListAsync(dir, ct).ConfigureAwait(false);
                }
                catch (FtpException ex) when (!ex.IsTransient)
                {
                    return null;                                  // Vorfahr fehlt — das LIST des Ziels meldet es
                }

                var entry = entries.FirstOrDefault(e => e.Name == segments[i]);
                if (entry != null && entry.Kind == FtpEntryKind.Link && !string.IsNullOrEmpty(entry.LinkTarget))
                {
                    var target = RemotePath.ResolveLink(dir, entry.LinkTarget);
                    var rest = string.Join("/", segments.Skip(i + 1));
                    return rest.Length == 0 ? target : RemotePath.Combine(target, rest);
                }
                dir = RemotePath.Combine(dir, segments[i]);
            }
            return null;
        }
    }
}
```

`src/FileManager.Core/Presentation/FolderBrowser.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>Ein gelisteter Ordner, wie die Liste ihn zeigt.</summary>
    public sealed class FolderView
    {
        public FolderView(string path, IReadOnlyList<FtpEntry> entries, Zone zone)
        {
            Path = path;
            Entries = entries;
            Zone = zone;
        }

        public string Path { get; }
        public IReadOnlyList<FtpEntry> Entries { get; }

        /// <summary>Zone fürs Schreiben IN diesen Ordner (Anlegen, Hochladen, Einfügen).</summary>
        public Zone Zone { get; }
    }

    /// <summary>
    /// Wo die Liste gerade steht (Spec „Pfade und Verlauf"): Navigieren über
    /// aufgelöste Pfade, Zurück/Vor/Hoch, Aktualisieren. Der Verlauf ändert sich
    /// erst nach erfolgreichem Listen — eine abgebrochene oder gescheiterte
    /// Navigation lässt den Ort stehen. Aufrufer ist der UI-Thread; bewusst ohne
    /// ConfigureAwait(false), damit der Zustand nur dort wechselt.
    /// </summary>
    public sealed class FolderBrowser
    {
        private const string ContentProbe = "x";

        private readonly DirectoryCache _cache;
        private readonly ZonePolicy _policy;
        private readonly NavigationHistory _history = new NavigationHistory();

        public FolderBrowser(DirectoryCache cache, ZonePolicy policy)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            _cache = cache;
            _policy = policy;
        }

        public string CurrentPath { get { return _history.Current; } }
        public bool CanGoBack { get { return _history.CanGoBack; } }
        public bool CanGoForward { get { return _history.CanGoForward; } }
        public bool CanGoUp { get { return CurrentPath != null && CurrentPath != RemotePath.Root; } }

        /// <summary>Zone fürs Schreiben in einen Ordner: die eines gedachten Kindes (die Wurzeln selbst sind gesperrt, ihr Inhalt nicht).</summary>
        public static Zone ContentsZone(ZonePolicy policy, string folder)
        {
            return policy.Effective(RemotePath.Combine(folder, ContentProbe));
        }

        public async Task<FolderView> NavigateAsync(string path, CancellationToken ct)
        {
            var resolved = await PathResolver.ResolveAsync(_cache, path, ct);
            var entries = await _cache.ListAsync(resolved, ct);
            ct.ThrowIfCancellationRequested();
            _history.Navigate(resolved);
            return View(resolved, entries);
        }

        public async Task<FolderView> BackAsync(CancellationToken ct)
        {
            var target = _history.BackTarget;
            if (target == null) throw new InvalidOperationException("Kein Zurück vorhanden.");
            var entries = await _cache.ListAsync(target, ct);
            ct.ThrowIfCancellationRequested();
            if (_history.BackTarget == target) _history.GoBack();
            return View(target, entries);
        }

        public async Task<FolderView> ForwardAsync(CancellationToken ct)
        {
            var target = _history.ForwardTarget;
            if (target == null) throw new InvalidOperationException("Kein Vor vorhanden.");
            var entries = await _cache.ListAsync(target, ct);
            ct.ThrowIfCancellationRequested();
            if (_history.ForwardTarget == target) _history.GoForward();
            return View(target, entries);
        }

        public Task<FolderView> UpAsync(CancellationToken ct)
        {
            if (!CanGoUp) throw new InvalidOperationException("Schon ganz oben.");
            return NavigateAsync(RemotePath.Parent(CurrentPath), ct);
        }

        public async Task<FolderView> RefreshAsync(CancellationToken ct)
        {
            var path = CurrentPath;
            if (path == null) throw new InvalidOperationException("Noch kein Ordner offen.");
            _cache.Invalidate(path);
            var entries = await _cache.ListAsync(path, ct);
            ct.ThrowIfCancellationRequested();
            return View(path, entries);
        }

        private FolderView View(string path, IReadOnlyList<FtpEntry> entries)
        {
            return new FolderView(path, entries, ContentsZone(_policy, path));
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~FolderBrowserTests|FullyQualifiedName~NavigationHistoryTests"` — PASS.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Paths/NavigationHistory.cs src/FileManager.Core/Presentation/PathResolver.cs src/FileManager.Core/Presentation/FolderBrowser.cs tests/FileManager.Core.Tests/NavigationHistoryTests.cs tests/FileManager.Core.Tests/FolderBrowserTests.cs
git commit -m "Navigate folders through resolved links with a history that moves only on success"
```

---

### Task 10: `ExplorerSession` — eine verdrahtete Verbindung

**Files:**
- Create: `src/FileManager.Core/Presentation/ExplorerSession.cs`
- Create: `tests/FileManager.Core.Tests/ExplorerSessionTests.cs`

**Interfaces:**
- Consumes: `ZonePolicy`, `ZoneGuardFtpClient`, `FtpGates`, `DirectoryCache`, `TitleCatalog`, `QuickAccessBuilder`, `FileOperations`, `TransferQueue` (Task 2), `EditSessionManager`, `FolderBrowser` (Task 9).
- Produces: `public sealed class ExplorerSession : IDisposable`
  - `ExplorerSession(IFtpClient ftp, bool expertMode, IUserPrompts prompts, IConflictResolver resolver, string workRoot, IFileChangeSource changes, IClock clock)` — **ein** `ZoneGuardFtpClient` um `ftp` für alle Bausteine (Übergabe „alle Schreibwege über einen Wächter").
  - Eigenschaften: `Policy`, `Ftp : ZoneGuardFtpClient`, `Gates`, `Cache`, `Titles`, `QuickAccess`, `Operations`, `Queue`, `Editing`, `Browser`.
  - `bool ExpertMode { get; set; }` → `Policy.ExpertMode`.
  - `Task CheckAsync(CancellationToken ct)` — ein LIST der Wurzel über den Cache; scheitert laut (FtpException), wenn die Konsole nicht antwortet.
  - `IReadOnlyCollection<string> ApplyFinished(TransferJob job)` — `InvalidateBelow` für `RemoteSubtreesGone`, `Invalidate` für `RemoteFoldersToRefresh`, jeweils auch in der anderen Schreibweise von `/data` ≡ `/user/data` (Übergabe „Cache-Schlüssel"); liefert die Ordner, die die Ansicht neu anfordern sollte.
  - `Dispose()` — entsorgt `Editing` (Watcher).

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/ExplorerSessionTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Presentation;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using Xunit;
using System.Collections.Generic;

namespace FileManager.Core.Tests
{
    public sealed class ExplorerSessionTests : IDisposable
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly TempDir _temp = new TempDir();
        private readonly ExplorerSession _session;

        public ExplorerSessionTests()
        {
            _ftp.AddFile("/data/mods/a.txt", "a").AddDir("/system_data");
            _session = Create(false);
        }

        private ExplorerSession Create(bool expert)
        {
            return new ExplorerSession(_ftp, expert, new RecordingPrompts(), new ScriptedResolver(),
                Path.Combine(_temp.Path, "open"), new FakeChangeSource(), new TestClock());
        }

        public void Dispose()
        {
            _session.Dispose();
            _temp.Dispose();
        }

        [Fact]
        public async Task Every_write_of_the_queue_goes_through_the_zone_guard()
        {
            var step = new TransferStep(StepKind.UploadFile, "/system_data/x", _temp.File("x", "x"), null, 1);
            var job = new TransferJob(TransferKind.Upload, "t", new[] { step });

            _session.Queue.Enqueue(job);
            await _session.Queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal(0, _ftp.WriteCallCount);
        }

        [Fact]
        public void Expert_mode_is_the_policy_switch()
        {
            using (var expert = Create(true))
            {
                Assert.True(expert.Policy.ExpertMode);
                expert.ExpertMode = false;
                Assert.False(expert.Policy.ExpertMode);
            }
        }

        [Fact]
        public async Task A_finished_delete_invalidates_the_folder_and_the_subtree()
        {
            await _session.Cache.ListAsync("/data", CancellationToken.None);
            await _session.Cache.ListAsync("/data/mods", CancellationToken.None);
            var job = new TransferJob(TransferKind.Delete, "t", new[]
            {
                new TransferStep(StepKind.DeleteRemoteFile, "/data/mods/a.txt", null, null, 1),
                new TransferStep(StepKind.RemoveRemoteFolder, "/data/mods", null, null, 0)
            });

            var refresh = _session.ApplyFinished(job);

            IReadOnlyList<FtpEntry> cached;
            Assert.False(_session.Cache.TryGetCached("/data", out cached));
            Assert.False(_session.Cache.TryGetCached("/data/mods", out cached));
            Assert.Contains("/data", refresh);
        }

        [Fact]
        public async Task Both_spellings_of_user_data_are_invalidated()
        {
            _ftp.AddDir("/user/data/mods");      // der Fake kennt keinen Alias — hier nur als eigener Ordner für den Cache
            await _session.Cache.ListAsync("/user/data/mods", CancellationToken.None);
            var job = new TransferJob(TransferKind.CreateFolder, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/mods/neu", null, null, 0)
            });

            _session.ApplyFinished(job);

            IReadOnlyList<FtpEntry> cached;
            Assert.False(_session.Cache.TryGetCached("/user/data/mods", out cached));
        }

        [Fact]
        public async Task Check_fails_loudly_without_a_console()
        {
            _ftp.Failure = (verb, path) => verb == "LIST"
                ? new FtpException("Keine Verbindung zur PS4 — läuft der FTP-Server von GoldHEN?", FtpErrorKind.Transient, 0) : null;

            var ex = await Assert.ThrowsAsync<FtpException>(() => _session.CheckAsync(CancellationToken.None));
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public void All_parts_share_one_guarded_client()
        {
            Assert.NotNull(_session.Ftp);
            Assert.NotNull(_session.Browser);
            Assert.NotNull(_session.QuickAccess);
            Assert.NotNull(_session.Titles);
            Assert.NotNull(_session.Operations);
            Assert.NotNull(_session.Editing);
        }
    }
}
```

(Oben zusätzlich `using System.Collections.Generic;` ergänzen — `IReadOnlyList<FtpEntry>` wird verwendet. `RecordingPrompts`, `ScriptedResolver`, `FakeChangeSource`, `TempDir`, `TestClock` gibt es schon im Testprojekt.)

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~ExplorerSessionTests"`
Expected: Build-Fehler — `ExplorerSession` fehlt.

- [ ] **Step 3: Implementieren**

`src/FileManager.Core/Presentation/ExplorerSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Ps4;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Eine Verbindung zur Konsole, fertig verdrahtet (Plan-1-Übergabe): EIN
    /// Wächter um den FTP-Client für alle Bausteine, gemeinsame Tore, gemeinsamer
    /// Verzeichnis-Cache. Die App baut pro „Verbinden" eine neue Sitzung und
    /// entsorgt die alte.
    /// </summary>
    public sealed class ExplorerSession : IDisposable
    {
        private const string Data = "/data";
        private const string UserData = "/user/data";

        public ExplorerSession(IFtpClient ftp, bool expertMode, IUserPrompts prompts, IConflictResolver resolver,
            string workRoot, IFileChangeSource changes, IClock clock)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (prompts == null) throw new ArgumentNullException(nameof(prompts));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (workRoot == null) throw new ArgumentNullException(nameof(workRoot));
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (clock == null) throw new ArgumentNullException(nameof(clock));

            Policy = new ZonePolicy { ExpertMode = expertMode };
            Ftp = new ZoneGuardFtpClient(ftp, Policy);
            Gates = new FtpGates();
            Cache = new DirectoryCache(Ftp, Gates);
            Titles = new TitleCatalog(Ftp, Gates);
            QuickAccess = new QuickAccessBuilder(Cache, Ftp, Gates);
            Operations = new FileOperations(Ftp, Gates, Policy, prompts);
            Queue = new TransferQueue(Ftp, Gates, resolver, clock);
            Editing = new EditSessionManager(workRoot, Ftp, Gates, changes, clock);
            Browser = new FolderBrowser(Cache, Policy);
        }

        public ZonePolicy Policy { get; }
        public ZoneGuardFtpClient Ftp { get; }
        public FtpGates Gates { get; }
        public DirectoryCache Cache { get; }
        public TitleCatalog Titles { get; }
        public QuickAccessBuilder QuickAccess { get; }
        public FileOperations Operations { get; }
        public TransferQueue Queue { get; }
        public EditSessionManager Editing { get; }
        public FolderBrowser Browser { get; }

        public bool ExpertMode
        {
            get { return Policy.ExpertMode; }
            set { Policy.ExpertMode = value; }
        }

        /// <summary>Ein LIST der Wurzel — scheitert laut, wenn die Konsole nicht antwortet.</summary>
        public Task CheckAsync(CancellationToken ct)
        {
            return Cache.ListAsync(RemotePath.Root, ct);
        }

        /// <summary>
        /// Nach einem fertigen Auftrag: betroffene Ordner neu lesen lassen
        /// (Plan-1-Übergabe). Der Cache kennt Pfade so, wie gelistet wurde —
        /// /data und /user/data sind zwei Schlüssel für einen Ordner, deshalb
        /// werden beide Schreibweisen verworfen. Liefert die Ordner, die die
        /// Ansicht neu anfordern sollte.
        /// </summary>
        public IReadOnlyCollection<string> ApplyFinished(TransferJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            foreach (var gone in job.RemoteSubtreesGone)
            {
                foreach (var alias in Aliases(gone)) Cache.InvalidateBelow(alias);
            }
            foreach (var folder in job.RemoteFoldersToRefresh)
            {
                foreach (var alias in Aliases(folder)) Cache.Invalidate(alias);
            }
            return job.RemoteFoldersToRefresh;
        }

        private static IEnumerable<string> Aliases(string path)
        {
            var p = RemotePath.Normalize(path);
            yield return p;
            if (RemotePath.IsSameOrBelow(p, UserData)) yield return Data + p.Substring(UserData.Length);
            else if (RemotePath.IsSameOrBelow(p, Data)) yield return UserData + p.Substring(Data.Length);
        }

        public void Dispose()
        {
            Editing.Dispose();
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~ExplorerSessionTests"` — PASS.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Presentation/ExplorerSession.cs tests/FileManager.Core.Tests/ExplorerSessionTests.cs
git commit -m "Wire one console connection behind a single zone guard"
```

---

### Task 11: Statuszeile, Übertragungstexte, Befehlszustand

**Files:**
- Create: `src/FileManager.Core/Presentation/StatusText.cs`
- Create: `src/FileManager.Core/Presentation/TransferText.cs`
- Create: `src/FileManager.Core/Presentation/CommandState.cs`
- Create: `tests/FileManager.Core.Tests/PresentationTextTests.cs`

**Interfaces:**
- Consumes: `FolderRow` (Task 8), `SizeFormat` (Task 8), `TransferJob`, `JobState`, `Zone`, `ZonePolicy.Describe`.
- Produces:
  - `StatusText.Line(bool connected, int itemCount, IReadOnlyList<FolderRow> selection, Zone zone) : string` → „Getrennt" bzw. „Verbunden · 3 Elemente · 1 ausgewählt (412 KB) · Zone: frei" (Auswahlteil nur bei Auswahl; Größe nur, wenn Dateien dabei sind; „1 Element").
  - `TransferText.State(TransferJob)` → „Wartend", „Läuft", „Fertig", „Fertig mit Fehlern", „Abgebrochen", „Unterbrochen: <Grund>".
  - `TransferText.Bar(long done, long total)` → zehn Zeichen aus `█`/`░` plus „ 78 %" (abgerundet); `total ≤ 0` → „░░░░░░░░░░ 0 %".
  - `TransferText.Progress(TransferJob)` → fertig: `job.Summary`; sonst Balken über Bytes, bei Aufträgen ohne Bytes (Löschen, Umbenennen) über erledigte Schritte.
  - `TransferText.Rate(TransferJob)` → nur bei „Läuft": `SizeFormat.Rate`; `TransferText.Remaining(TransferJob)` → „noch 0:42" / „noch 1:02:03", sonst `""`.
  - `TransferText.Summary(IReadOnlyList<TransferJob>)` → läuft einer: „<Titel> <Balken> · <Rate>" (Rate nur, wenn bekannt); sonst „3 wartend" / „1 unterbrochen" / „keine laufenden".
  - Außerdem öffentlich: `TransferText.RemainingText(TimeSpan)` („noch 0:42", von den Tests genutzt) und `StatusText.Items(int)` („1 Element"/„N Elemente", Task 19).
  - `public sealed class CommandState` — `static For(bool connected, Zone folderZone, IReadOnlyList<FolderRow> selection, Func<string, Zone> zoneOf)`; Eigenschaften `CanUpload`, `CanNewFolder`, `CanPaste` (verbunden und Ordner nicht gesperrt), `CanDownload`, `CanCopy` (verbunden, Auswahl ohne Links), `CanOpen` (genau eine Datei, kein Link), `CanRename` (genau ein Eintrag, kein Spielordner, Eintrag nicht gesperrt), `CanDelete`, `CanCut` (Auswahl, kein Eintrag gesperrt). Die Durchsetzung bleibt im Kern — das hier graut nur aus (Spec „in gesperrten Ordnern sind Schreibbefehle ausgegraut").

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/PresentationTextTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class PresentationTextTests
    {
        private static FolderRow FileRow(string name, long size)
        {
            return new FolderRow("/data", new FtpEntry(name, FtpEntryKind.File, size, DateTime.MinValue, null), "Datei");
        }

        private static FolderRow DirRow(string dir, string name)
        {
            return new FolderRow(dir, new FtpEntry(name, FtpEntryKind.Directory, 512, DateTime.MinValue, null), "Dateiordner");
        }

        private static readonly FolderRow[] None = new FolderRow[0];

        [Fact]
        public void The_status_line_reads_like_the_spec()
        {
            Assert.Equal("Getrennt", StatusText.Line(false, 0, None, Zone.Locked));
            Assert.Equal("Verbunden · 3 Elemente · Zone: frei", StatusText.Line(true, 3, None, Zone.Free));
            Assert.Equal("Verbunden · 3 Elemente · 1 ausgewählt (412 KB) · Zone: frei",
                StatusText.Line(true, 3, new[] { FileRow("a.prx", 412 * 1024) }, Zone.Free));
            Assert.Equal("Verbunden · 1 Element · 1 ausgewählt · Zone: Warnung",
                StatusText.Line(true, 1, new[] { DirRow("/user/home", "1") }, Zone.Warn));
        }

        [Theory]
        [InlineData(0L, 100L, "░░░░░░░░░░ 0 %")]
        [InlineData(78L, 100L, "████████░░ 78 %")]
        [InlineData(100L, 100L, "██████████ 100 %")]
        [InlineData(5L, 0L, "░░░░░░░░░░ 0 %")]
        public void The_bar_is_ten_blocks_and_a_percentage(long done, long total, string expected)
        {
            Assert.Equal(expected, TransferText.Bar(done, total));
        }

        [Fact]
        public void States_read_german()
        {
            var job = new TransferJob(TransferKind.Delete, "x löschen", new[] { new TransferStep(StepKind.DeleteRemoteFile, "/data/x", null, null, 1) });
            Assert.Equal("Wartend", TransferText.State(job));
            Assert.Equal("░░░░░░░░░░ 0 %", TransferText.Progress(job));   // ohne Bytes: nach Schritten
            Assert.Equal("", TransferText.Rate(job));
            Assert.Equal("", TransferText.Remaining(job));
        }

        [Theory]
        [InlineData(42, "noch 0:42")]
        [InlineData(725, "noch 12:05")]
        [InlineData(3723, "noch 1:02:03")]
        public void Remaining_time_reads_like_a_clock(int seconds, string expected)
        {
            Assert.Equal(expected, TransferText.RemainingText(TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void The_summary_names_waiting_jobs_when_none_runs()
        {
            var a = new TransferJob(TransferKind.Upload, "a hochladen", new TransferStep[0]);
            var b = new TransferJob(TransferKind.Upload, "b hochladen", new TransferStep[0]);

            Assert.Equal("2 wartend", TransferText.Summary(new[] { a, b }));
            Assert.Equal("keine laufenden", TransferText.Summary(new TransferJob[0]));
        }

        [Fact]
        public void Write_commands_are_greyed_in_a_locked_folder()
        {
            var state = CommandState.For(true, Zone.Locked, None, p => Zone.Locked);

            Assert.False(state.CanUpload);
            Assert.False(state.CanNewFolder);
            Assert.False(state.CanPaste);
        }

        [Fact]
        public void Selection_commands_follow_the_selection()
        {
            var policy = new ZonePolicy();
            var file = FileRow("a.txt", 3);
            var game = DirRow("/user/app", "CUSA00411");
            var link = new FolderRow("/data", new FtpEntry("sys", FtpEntryKind.Link, 7, DateTime.MinValue, "/system"), "Verknüpfung");

            var one = CommandState.For(true, Zone.Free, new[] { file }, policy.Effective);
            Assert.True(one.CanOpen && one.CanRename && one.CanDelete && one.CanDownload && one.CanCopy && one.CanCut);

            var gameState = CommandState.For(true, Zone.Warn, new[] { game }, policy.Effective);
            Assert.False(gameState.CanRename);                          // Spielordner behalten ihre ID
            Assert.False(gameState.CanOpen);

            var linkState = CommandState.For(true, Zone.Free, new[] { link }, policy.Effective);
            Assert.False(linkState.CanDownload);
            Assert.False(linkState.CanCopy);
            Assert.True(linkState.CanDelete);                           // der Link selbst darf weg

            var disconnected = CommandState.For(false, Zone.Free, new[] { file }, policy.Effective);
            Assert.False(disconnected.CanDelete || disconnected.CanUpload || disconnected.CanOpen);
        }

        [Fact]
        public void A_locked_item_cannot_be_deleted_or_renamed()
        {
            var sys = DirRow("/", "system");

            var state = CommandState.For(true, Zone.Locked, new[] { sys }, new ZonePolicy().Effective);

            Assert.False(state.CanDelete);
            Assert.False(state.CanRename);
            Assert.False(state.CanCut);
            Assert.True(state.CanDownload);                             // Lesen ist überall erlaubt
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~PresentationTextTests"`
Expected: Build-Fehler — `StatusText`, `TransferText`, `CommandState` fehlen.

- [ ] **Step 3: Implementieren**

`src/FileManager.Core/Presentation/StatusText.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using FileManager.Core.Zones;

namespace FileManager.Core.Presentation
{
    /// <summary>Die Statuszeile (Spec): Verbindung, Elemente, Auswahl mit Größe, Zone.</summary>
    public static class StatusText
    {
        private const string Separator = " · ";

        public static string Line(bool connected, int itemCount, IReadOnlyList<FolderRow> selection, Zone zone)
        {
            if (!connected) return "Getrennt";
            var parts = new List<string> { "Verbunden", Items(itemCount) };
            if (selection != null && selection.Count > 0)
            {
                var files = selection.Where(r => !r.IsFolder && !r.IsLink).ToList();
                var chosen = selection.Count + " ausgewählt";
                if (files.Count > 0) chosen += " (" + SizeFormat.Bytes(files.Sum(r => r.Entry.Size)) + ")";
                parts.Add(chosen);
            }
            parts.Add("Zone: " + ZonePolicy.Describe(zone));
            return string.Join(Separator, parts);
        }

        public static string Items(int count)
        {
            return count == 1 ? "1 Element" : count + " Elemente";
        }
    }
}
```

`src/FileManager.Core/Presentation/TransferText.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileManager.Core.Transfers;

namespace FileManager.Core.Presentation
{
    /// <summary>Texte der Übertragungsleiste (Spec): Status, Fortschritt als Textbalken, Rate, Restzeit.</summary>
    public static class TransferText
    {
        private const int BarWidth = 10;
        private const char Full = '█';
        private const char Empty = '░';

        public static string State(TransferJob job)
        {
            switch (job.State)
            {
                case JobState.Waiting: return "Wartend";
                case JobState.Running: return "Läuft";
                case JobState.Completed: return "Fertig";
                case JobState.CompletedWithErrors: return "Fertig mit Fehlern";
                case JobState.Cancelled: return "Abgebrochen";
                case JobState.Interrupted: return "Unterbrochen: " + (job.InterruptReason ?? "Verbindung zur PS4 unterbrochen");
                default: return job.State.ToString();
            }
        }

        public static string Bar(long done, long total)
        {
            double fraction = total > 0 ? Math.Max(0, Math.Min(1, (double)done / total)) : 0;
            int filled = (int)Math.Round(fraction * BarWidth, MidpointRounding.AwayFromZero);
            int percent = (int)Math.Floor(fraction * 100);
            var bar = new StringBuilder(BarWidth + 6);
            bar.Append(Full, filled).Append(Empty, BarWidth - filled);
            return bar + " " + percent + " %";
        }

        public static string Progress(TransferJob job)
        {
            if (job.IsFinished) return job.Summary;
            if (job.TotalBytes > 0) return Bar(job.DoneBytes, job.TotalBytes);
            int done = job.Steps.Count(s => s.State != StepState.Pending);
            return Bar(done, job.Steps.Count);
        }

        public static string Rate(TransferJob job)
        {
            return job.State == JobState.Running ? SizeFormat.Rate(job.BytesPerSecond) : "";
        }

        public static string Remaining(TransferJob job)
        {
            var remaining = job.Remaining;
            return job.State == JobState.Running && remaining.HasValue ? RemainingText(remaining.Value) : "";
        }

        public static string RemainingText(TimeSpan remaining)
        {
            var whole = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));
            return whole.TotalHours >= 1
                ? "noch " + (int)whole.TotalHours + ":" + whole.Minutes.ToString("00") + ":" + whole.Seconds.ToString("00")
                : "noch " + whole.Minutes + ":" + whole.Seconds.ToString("00");
        }

        public static string Summary(IReadOnlyList<TransferJob> jobs)
        {
            var running = jobs.FirstOrDefault(j => j.State == JobState.Running);
            if (running != null)
            {
                var rate = Rate(running);
                return running.Title + " " + Progress(running) + (rate.Length > 0 ? " · " + rate : "");
            }
            int waiting = jobs.Count(j => j.State == JobState.Waiting);
            if (waiting > 0) return waiting + " wartend";
            int interrupted = jobs.Count(j => j.State == JobState.Interrupted);
            if (interrupted > 0) return interrupted + " unterbrochen";
            return "keine laufenden";
        }
    }
}
```

`src/FileManager.Core/Presentation/CommandState.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using FileManager.Core.Zones;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Welche Befehle gerade gehen (Spec „in gesperrten Ordnern sind
    /// Schreibbefehle ausgegraut"). Nur Ausgrauen — durchgesetzt wird im Kern.
    /// </summary>
    public sealed class CommandState
    {
        public bool CanUpload { get; private set; }
        public bool CanNewFolder { get; private set; }
        public bool CanPaste { get; private set; }
        public bool CanDownload { get; private set; }
        public bool CanCopy { get; private set; }
        public bool CanOpen { get; private set; }
        public bool CanRename { get; private set; }
        public bool CanDelete { get; private set; }
        public bool CanCut { get; private set; }

        public static CommandState For(bool connected, Zone folderZone, IReadOnlyList<FolderRow> selection, Func<string, Zone> zoneOf)
        {
            if (zoneOf == null) throw new ArgumentNullException(nameof(zoneOf));
            var rows = selection ?? new FolderRow[0];
            bool writableFolder = connected && folderZone != Zone.Locked;
            bool any = connected && rows.Count > 0;
            bool single = connected && rows.Count == 1;
            bool noneLocked = rows.All(r => zoneOf(r.Item.Path) != Zone.Locked);

            return new CommandState
            {
                CanUpload = writableFolder,
                CanNewFolder = writableFolder,
                CanPaste = writableFolder,
                CanDownload = any && rows.All(r => !r.IsLink),
                CanCopy = any && rows.All(r => !r.IsLink),
                CanOpen = single && !rows[0].IsFolder && !rows[0].IsLink,
                CanRename = single && !rows[0].IsTitleFolder && noneLocked,
                CanDelete = any && noneLocked,
                CanCut = any && noneLocked
            };
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~PresentationTextTests"` — PASS.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Presentation/StatusText.cs src/FileManager.Core/Presentation/TransferText.cs src/FileManager.Core/Presentation/CommandState.cs tests/FileManager.Core.Tests/PresentationTextTests.cs
git commit -m "Word the status line, transfer rows and command availability"
```

---

### Task 12: Einfügen und Ablegen entscheiden

**Files:**
- Create: `src/FileManager.Core/Presentation/PastePlanner.cs`
- Create: `tests/FileManager.Core.Tests/PastePlannerTests.cs`

**Interfaces:**
- Consumes: `FolderRow` (Task 8), `RemoteItem`, `Zone`, `RemotePath`.
- Produces:
  - `public enum ClipMode { None, Copy, Cut }`
  - `public enum PasteAction { Nothing, UploadFiles, MoveItems, RefuseCopyWithinPs4 }`
  - `public enum DropAction { None, Upload, Move }`
  - `PastePlanner.Decide(ClipMode ourMode, bool weOwnClipboard, bool clipboardHasFiles) : PasteAction` — gehört die Zwischenablage noch unserem Inhalt: Ausschneiden → Verschieben, Kopieren → Meldung „Kopieren innerhalb der PS4 kommt später" (Spec); sonst Dateien aus Explorer → Hochladen; sonst nichts.
  - `PastePlanner.ForDrop(bool internalDrag, bool hasFileDrop, Zone targetZone) : DropAction` — gesperrtes Ziel → keins; eigenes Ziehen → Verschieben; Dateien aus Explorer → Hochladen.
  - `PastePlanner.DropFolder(FolderRow row, string currentFolder) : string` — ein Ordner als Ziel, sonst der aktuelle Ordner (Links und Dateien sind kein Ziel).
  - `PastePlanner.IsUsefulMove(IReadOnlyList<RemoteItem> items, string targetFolder) : bool` — false, wenn alles schon dort liegt oder ein Ordner in sich selbst/seinen Unterordner ginge.

- [ ] **Step 1: Failing tests schreiben**

`tests/FileManager.Core.Tests/PastePlannerTests.cs`:

```csharp
using System;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class PastePlannerTests
    {
        [Theory]
        [InlineData(ClipMode.Cut, true, false, PasteAction.MoveItems)]
        [InlineData(ClipMode.Copy, true, false, PasteAction.RefuseCopyWithinPs4)]
        [InlineData(ClipMode.Copy, false, true, PasteAction.UploadFiles)]
        [InlineData(ClipMode.Cut, false, true, PasteAction.UploadFiles)]
        [InlineData(ClipMode.None, false, true, PasteAction.UploadFiles)]
        [InlineData(ClipMode.None, false, false, PasteAction.Nothing)]
        [InlineData(ClipMode.Cut, false, false, PasteAction.Nothing)]
        public void Paste_follows_who_owns_the_clipboard(ClipMode mode, bool ours, bool files, PasteAction expected)
        {
            Assert.Equal(expected, PastePlanner.Decide(mode, ours, files));
        }

        [Theory]
        [InlineData(true, false, Zone.Free, DropAction.Move)]
        [InlineData(false, true, Zone.Warn, DropAction.Upload)]
        [InlineData(true, false, Zone.Locked, DropAction.None)]
        [InlineData(false, true, Zone.Locked, DropAction.None)]
        [InlineData(false, false, Zone.Free, DropAction.None)]
        public void Drops_move_or_upload_but_never_into_locked_folders(bool own, bool files, Zone zone, DropAction expected)
        {
            Assert.Equal(expected, PastePlanner.ForDrop(own, files, zone));
        }

        [Fact]
        public void Only_folders_are_drop_targets()
        {
            var folder = new FolderRow("/data", new FtpEntry("pkg", FtpEntryKind.Directory, 512, DateTime.MinValue, null), "");
            var file = new FolderRow("/data", new FtpEntry("a.txt", FtpEntryKind.File, 1, DateTime.MinValue, null), "");
            var link = new FolderRow("/data", new FtpEntry("sys", FtpEntryKind.Link, 7, DateTime.MinValue, "/system"), "");

            Assert.Equal("/data/pkg", PastePlanner.DropFolder(folder, "/data"));
            Assert.Equal("/data", PastePlanner.DropFolder(file, "/data"));
            Assert.Equal("/data", PastePlanner.DropFolder(link, "/data"));
            Assert.Equal("/data", PastePlanner.DropFolder(null, "/data"));
        }

        [Fact]
        public void A_move_to_where_things_already_are_is_not_useful()
        {
            var a = new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue);
            var mods = new RemoteItem("/data/mods", true, 0, DateTime.MinValue);

            Assert.False(PastePlanner.IsUsefulMove(new[] { a }, "/data"));
            Assert.True(PastePlanner.IsUsefulMove(new[] { a }, "/data/pkg"));
            Assert.False(PastePlanner.IsUsefulMove(new[] { mods }, "/data/mods/sub"));
            Assert.False(PastePlanner.IsUsefulMove(new[] { mods }, "/data/mods"));
        }
    }
}
```

- [ ] **Step 2: Rot prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~PastePlannerTests"`
Expected: Build-Fehler — `PastePlanner` und die Enums fehlen.

- [ ] **Step 3: Implementieren**

`src/FileManager.Core/Presentation/PastePlanner.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;

namespace FileManager.Core.Presentation
{
    public enum ClipMode { None, Copy, Cut }

    public enum PasteAction { Nothing, UploadFiles, MoveItems, RefuseCopyWithinPs4 }

    public enum DropAction { None, Upload, Move }

    /// <summary>
    /// Was Strg+V und Ablegen bedeuten (Spec „Drag &amp; Drop / Zwischenablage"):
    /// Wer die Zwischenablage zuletzt gefüllt hat, gewinnt — wie im Explorer.
    /// Kopieren innerhalb der PS4 gibt es in v1 nicht.
    /// </summary>
    public static class PastePlanner
    {
        public static PasteAction Decide(ClipMode ourMode, bool weOwnClipboard, bool clipboardHasFiles)
        {
            if (weOwnClipboard && ourMode == ClipMode.Cut) return PasteAction.MoveItems;
            if (weOwnClipboard && ourMode == ClipMode.Copy) return PasteAction.RefuseCopyWithinPs4;
            if (clipboardHasFiles) return PasteAction.UploadFiles;
            return PasteAction.Nothing;
        }

        public static DropAction ForDrop(bool internalDrag, bool hasFileDrop, Zone targetZone)
        {
            if (targetZone == Zone.Locked) return DropAction.None;
            if (internalDrag) return DropAction.Move;
            if (hasFileDrop) return DropAction.Upload;
            return DropAction.None;
        }

        /// <summary>Ein Ordner unter dem Zeiger ist das Ziel, sonst der offene Ordner. Links und Dateien sind kein Ziel.</summary>
        public static string DropFolder(FolderRow row, string currentFolder)
        {
            return row != null && row.IsFolder ? row.Item.Path : currentFolder;
        }

        public static bool IsUsefulMove(IReadOnlyList<RemoteItem> items, string targetFolder)
        {
            var target = RemotePath.Normalize(targetFolder);
            if (items.Any(i => i.IsDirectory && RemotePath.IsSameOrBelow(target, i.Path))) return false;
            return items.Any(i => RemotePath.Parent(i.Path) != target);
        }
    }
}
```

- [ ] **Step 4: Grün prüfen**

Run: `dotnet test tests/FileManager.Core.Tests --filter "FullyQualifiedName~PastePlannerTests"` — PASS.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 5: Commit**

```bash
git add src/FileManager.Core/Presentation/PastePlanner.cs tests/FileManager.Core.Tests/PastePlannerTests.cs
git commit -m "Decide what paste and drop mean for the console"
```

---

## Phase C — Die App (`src/FileManager.App`, ohne Testprojekt — Abnahme per Fahrprobe)

Für alle App-Tasks gilt zusätzlich:
- Ein App-Task ist fertig, wenn `dotnet build ScreenshotGallery.sln -c Release` ohne neue Warnungen baut und `dotnet test ScreenshotGallery.sln` grün bleibt. Der Implementierer **startet die App nicht**; er listet im Bericht die Fahrprobe-Punkte des Tasks (stehen jeweils am Task-Ende), die der Anwender in Task 22 abhakt.
- UIFramework-Namensräume: `UIFramework.Core.Controls` (`SkinnedForm`), `UIFramework.Core.Skinning` (+ `.Skins`), `UIFramework.Core.Dpi`, `UIFramework.Controls` (Knöpfe, Labels, Panels, Textfelder, `CheckEdit`, `PopupMenu`/`MenuEntry`, Ribbon, `AddressBar`, `SkinSplitContainer`), `UIFramework.Grid`, `UIFramework.Gallery`, `UIFramework.Tree` — dort immer `using TreeNode = UIFramework.Tree.TreeNode;` (Namenskonflikt mit WinForms).
- Alle Bilder, die die App an Grid, Kacheln, Baum oder Ribbon gibt, gehören der App und werden erst **nach** `base.Dispose` freigegeben (UIFramework-Hausregel).
- Hintergrund-Ereignisse (`TransferQueue.JobChanged`, Rückfragen des Kerns) kommen nicht auf dem UI-Thread an — immer über `BeginInvoke` bzw. `Ui.OnUiAsync` zurück.

### Task 13: Projektgerüst, Shell-Symbole, Hauptfenster-Grundaufbau

**Files:**
- Create: `src/FileManager.App/FileManager.App.csproj`, `src/FileManager.App/app.manifest`, `src/FileManager.App/Program.cs`
- Create: `src/FileManager.App/AppPaths.cs`, `src/FileManager.App/AppForm.cs`, `src/FileManager.App/Ui.cs`
- Create: `src/FileManager.App/Shell/ShellFileInfo.cs`
- Create: `src/FileManager.App/Views/NavigationStrip.cs`
- Create: `src/FileManager.App/MainForm.cs`
- Modify: `ScreenshotGallery.sln`

**Interfaces:**
- Consumes: `FileManagerSettingsStore`/`FileManagerSettings`, `FtpGates.ConnectionLimit`, `EditSessionManager.CleanWorkRoot`, `ExplorerSession` (Task 10), `FolderRow`/`FolderBrowser`/`StatusText`/`CommandState` (Tasks 8–11).
- Produces (für die Folge-Tasks):
  - `AppPaths.Root`, `AppPaths.Settings` (`%LocalAppData%\PS4FileManager\settings.json`), `AppPaths.WorkRoot` (`…\open`).
  - `class AppForm : SkinnedForm` — `protected int Px(int logical)`.
  - `static class Ui` — `string Describe(Exception)`, `Task<T> OnUiAsync<T>(Control owner, Func<T> show)`, `bool IsTextInputFocused()`.
  - `sealed class ShellFileInfo : IDisposable` — `Image SmallIcon(string name, bool isFolder)`, `Image LargeIcon(string name, bool isFolder)`, `string TypeName(string name, bool isFolder)`, `Image LinkIcon()`.
  - `sealed class NavigationStrip : SkinPanel` — `Back`, `Forward`, `Up`, `Reload : SkinButton` (nicht „Refresh" — das verdeckte `Control.Refresh()`, Warnung CS0108), `Address : AddressBar`, `Filter : SkinTextBox`.
  - `MainForm` (partial) mit Feldern `_settings`, `_session`, `_currentPath`, `_rows`, `_list`, `_filtered`, `_sorted`, `_ribbon`, `_nav`, `_split`, `_tree`, `_quickRoot`, `_consoleRoot`, `_grid`, `_gallery`, `_statusBar`, `_status`, `_shell`, `_titleIcons` (Spiel-ID → großes Bild), `_commands : CommandState` und den Ribbon-Einträgen `_connectItem`, `_targetItem`, `_uploadItem`, `_uploadFolderItem`, `_downloadItem`, `_newFolderItem`, `_renameItem`, `_deleteItem`, `_detailsItem`, `_tilesItem`, `_expertItem`, `_lightItem`; Hilfen `RowAt(int)`, `CurrentSelection()`, `CurrentZone()`, `IsTilesView`, `Rebind()`, `ApplySort()`, `FillGallery()`, `IconFor(FolderRow, bool large)`, `UpdateCommands()`, `UpdateStatus()`, `SetStatus(string)`, `ShowError(Exception)`, `SetViewMode(string)`, `SetExpertMode(bool)`.
  - Ereignisse in `MainForm` für die Teilklassen: `SessionEnding : Action<ExplorerSession>` und `CollectingCloseWarnings : Action<List<string>>` (hier); `SessionStarted : Action<ExplorerSession>`, `FolderShown : Action`, `FileActivated : Action<FolderRow>` deklariert Task 15, wo sie ausgelöst werden.
  - `EndSession()` — löst `SessionEnding` aus und entsorgt die Sitzung.
  - Partielle Haken, die der Konstruktor ruft und je ein späterer Task implementiert: `WireConnection()` (T15), `WireBrowsing()` (T15), `WireTitles()` (T16), `AddTransferPanel()` + `WireTransfers()` (T17), `WireOperations()` (T18), `WireClipboard()` (T19), `WireMenus()` (T20), `WireEditing()` (T21).

- [ ] **Step 1: Projektdateien anlegen**

`src/FileManager.App/FileManager.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <AssemblyName>PS4FileManager</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ps4.Core\Ps4.Core.csproj" />
    <ProjectReference Include="..\FileManager.Core\FileManager.Core.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Core\UIFramework.Core.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Controls\UIFramework.Controls.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Grid\UIFramework.Grid.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Gallery\UIFramework.Gallery.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Tree\UIFramework.Tree.csproj" />
  </ItemGroup>
</Project>
```

`src/FileManager.App/app.manifest` (wortgleich zur UIFramework-Demo, nur der Name):

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="PS4FileManager.app" />

  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10/11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>

  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

In die Solution aufnehmen (unter den Lösungsordner `src`):

```bash
dotnet sln ScreenshotGallery.sln add --solution-folder src --include-references false src/FileManager.App/FileManager.App.csproj
```

Danach `git diff ScreenshotGallery.sln` ansehen: erwartet ist nur der neue Projekteintrag samt Konfigurationszeilen und Einordnung unter `src`. Schreibt das Werkzeug die ganze Datei um (BOM, Plattformmatrix — so geschehen in Plan 1 Task 1), ist das hinnehmbar; im Bericht vermerken.

- [ ] **Step 2: Infrastruktur**

`src/FileManager.App/Program.cs`:

```csharp
using System;
using System.Net;
using System.Windows.Forms;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using UIFramework.Core.Skinning;
using UIFramework.Core.Skinning.Skins;

namespace FileManager.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // OLE für Zwischenablage und Ziehen (Plan-1-Übergabe) — vor jedem Shell-Aufruf.
            Application.OleRequired();

            // Transfer 1 + Durchsuchen 2 + Shell 1 (Spec „Verbindungslimit").
            ServicePointManager.DefaultConnectionLimit = FtpGates.ConnectionLimit;

            // Arbeitskopien der letzten Sitzung weg (Spec „Öffnen mit Rückschreiben").
            EditSessionManager.CleanWorkRoot(AppPaths.WorkRoot);

            SkinManager.Current = new DarkSkin();      // Spec: Dark als Standard
            Application.Run(new MainForm());
        }
    }
}
```

`src/FileManager.App/AppPaths.cs`:

```csharp
using System;
using System.IO;

namespace FileManager.App
{
    /// <summary>Lokale Daten des File Managers (Spec „Solution-Aufbau").</summary>
    internal static class AppPaths
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS4FileManager");

        public static readonly string Settings = Path.Combine(Root, "settings.json");

        /// <summary>Arbeitskopien fürs Öffnen mit Rückschreiben.</summary>
        public static readonly string WorkRoot = Path.Combine(Root, "open");
    }
}
```

`src/FileManager.App/AppForm.cs`:

```csharp
using System.Drawing;
using System.Windows.Forms;
using UIFramework.Core.Controls;
using UIFramework.Core.Dpi;

namespace FileManager.App
{
    /// <summary>
    /// Basis aller Fenster (Plan-Entscheidung 8): wie ExplorerForm der
    /// UIFramework-Demo ohne fensterweites Scale(); feste Maße laufen über Px.
    /// </summary>
    internal class AppForm : SkinnedForm
    {
        public AppForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
        }

        /// <summary>Logische 96-dpi-Pixel in physische dieses Fensters. Erst ab OnLoad verlässlich.</summary>
        protected int Px(int logical)
        {
            return DpiScale.Scale(logical, DeviceDpi);
        }
    }
}
```

`src/FileManager.App/Ui.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileManager.App
{
    internal static class Ui
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        /// <summary>
        /// Text einer Ausnahme für den Anwender. FtpException und
        /// ZoneViolationException tragen schon deutschen Klartext — anders als
        /// in der Gallery zählt die äußere Nachricht, nicht die innerste Ursache
        /// (Plan-Entscheidung 9).
        /// </summary>
        public static string Describe(Exception ex)
        {
            var aggregate = ex as AggregateException;
            if (aggregate != null && aggregate.InnerExceptions.Count == 1) ex = aggregate.InnerExceptions[0];
            return ex.Message;
        }

        /// <summary>
        /// Führt show auf dem UI-Thread aus — der Kern ruft Rückfragen und
        /// Konfliktdialoge aus Hintergrund-Threads. Auf dem UI-Thread selbst
        /// läuft show sofort (modal).
        /// </summary>
        public static Task<T> OnUiAsync<T>(Control owner, Func<T> show)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action run = () =>
            {
                try { tcs.SetResult(show()); }
                catch (Exception ex) { tcs.SetException(ex); }
            };
            if (owner.InvokeRequired) owner.BeginInvoke(run);
            else run();
            return tcs.Task;
        }

        /// <summary>Hat ein Textfeld den Fokus? Dann gehören Entf, Rücktaste und Strg+C/X/V/A ihm.</summary>
        public static bool IsTextInputFocused()
        {
            return Control.FromHandle(GetFocus()) is TextBoxBase;
        }
    }
}
```

- [ ] **Step 3: Shell-Symbole und Typnamen**

`src/FileManager.App/Shell/ShellFileInfo.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FileManager.App.Shell
{
    /// <summary>
    /// Symbole und Typnamen wie im Explorer (Spec „Symbole/Typnamen"):
    /// SHGetFileInfo mit SHGFI_USEFILEATTRIBUTES — ohne Download, nur über die
    /// Endung, je Endung einmal geholt. Die Bilder gehören dieser Klasse.
    /// Nur auf dem UI-Thread benutzen.
    /// </summary>
    internal sealed class ShellFileInfo : IDisposable
    {
        private const uint FileAttributeNormal = 0x80;
        private const uint FileAttributeDirectory = 0x10;
        private const uint ShgfiIcon = 0x100;
        private const uint ShgfiTypeName = 0x400;
        private const uint ShgfiLargeIcon = 0x0;
        private const uint ShgfiSmallIcon = 0x1;
        private const uint ShgfiUseFileAttributes = 0x10;
        private const string FolderKey = "<ordner>";
        private const string NoExtensionKey = "<datei>";
        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileInfo
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);

        private readonly Dictionary<string, Image> _small = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Image> _large = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Image SmallIcon(string name, bool isFolder)
        {
            return IconFor(_small, Key(name, isFolder), isFolder, ShgfiSmallIcon);
        }

        public Image LargeIcon(string name, bool isFolder)
        {
            return IconFor(_large, Key(name, isFolder), isFolder, ShgfiLargeIcon);
        }

        /// <summary>Das Verknüpfungssymbol (wie eine .lnk-Datei).</summary>
        public Image LinkIcon()
        {
            return IconFor(_small, ".lnk", false, ShgfiSmallIcon);
        }

        public string TypeName(string name, bool isFolder)
        {
            var key = Key(name, isFolder);
            string type;
            if (_types.TryGetValue(key, out type)) return type;

            var info = new ShFileInfo();
            SHGetFileInfo(Probe(key, isFolder), isFolder ? FileAttributeDirectory : FileAttributeNormal,
                ref info, (uint)Marshal.SizeOf(typeof(ShFileInfo)), ShgfiTypeName | ShgfiUseFileAttributes);
            type = string.IsNullOrEmpty(info.szTypeName) ? (isFolder ? "Dateiordner" : "Datei") : info.szTypeName;
            _types[key] = type;
            return type;
        }

        private static Image IconFor(Dictionary<string, Image> cache, string key, bool isFolder, uint size)
        {
            Image image;
            if (cache.TryGetValue(key, out image)) return image;

            var info = new ShFileInfo();
            SHGetFileInfo(Probe(key, isFolder), isFolder ? FileAttributeDirectory : FileAttributeNormal,
                ref info, (uint)Marshal.SizeOf(typeof(ShFileInfo)), ShgfiIcon | size | ShgfiUseFileAttributes);
            if (info.hIcon != IntPtr.Zero)
            {
                using (var icon = Icon.FromHandle(info.hIcon)) image = icon.ToBitmap();
                DestroyIcon(info.hIcon);
            }
            cache[key] = image;                               // auch null merken — kein zweiter Versuch
            return image;
        }

        /// <summary>
        /// Ordner teilen sich ein Symbol, Dateien eines je Endung. Von Hand statt
        /// Path.GetExtension: PS4-Namen dürfen Zeichen enthalten, an denen die
        /// Pfadfunktionen von Windows werfen.
        /// </summary>
        private static string Key(string name, bool isFolder)
        {
            if (isFolder) return FolderKey;
            var n = name ?? "";
            int dot = n.LastIndexOf('.');
            if (dot <= 0 || dot == n.Length - 1) return NoExtensionKey;
            var extension = n.Substring(dot);
            return extension.IndexOfAny(InvalidNameChars) >= 0 ? NoExtensionKey : extension;
        }

        private static string Probe(string key, bool isFolder)
        {
            if (isFolder) return "ordner";
            return key == NoExtensionKey ? "datei" : "datei" + key;
        }

        public void Dispose()
        {
            foreach (var image in _small.Values.Concat(_large.Values))
            {
                if (image != null) image.Dispose();
            }
            _small.Clear();
            _large.Clear();
        }
    }
}
```

- [ ] **Step 4: Navigationszeile**

`src/FileManager.App/Views/NavigationStrip.cs`:

```csharp
using System;
using System.Windows.Forms;
using UIFramework.Controls;
using UIFramework.Core.Dpi;

namespace FileManager.App.Views
{
    /// <summary>
    /// Zurück/Vor/Hoch/Aktualisieren, Adressleiste, Filterfeld (Spec
    /// „Navigationszeile"). Aufbau wie ExplorerForm.BuildStrip: die Leiste
    /// setzt ihre Höhe selbst, der Streifen folgt ihr.
    /// </summary>
    internal sealed class NavigationStrip : SkinPanel
    {
        private const int FilterWidthLogical = 200;

        public NavigationStrip()
        {
            Dock = DockStyle.Top;
            Back = Button("←");
            Forward = Button("→");
            Up = Button("↑");
            Reload = Button("↻");
            Filter = new SkinTextBox { Dock = DockStyle.Right, Width = FilterWidthLogical, PlaceholderText = "Filter …" };
            Address = new AddressBar { Dock = DockStyle.Top, RootText = "PS4" };

            // Zuletzt Hinzugefügtes dockt zuerst: Knöpfe links, Filter rechts,
            // die Adressleiste nimmt die Breite dazwischen.
            Controls.Add(Address);
            Controls.Add(Filter);
            Controls.Add(Reload);
            Controls.Add(Up);
            Controls.Add(Forward);
            Controls.Add(Back);

            Address.SizeChanged += (s, e) => SyncHeight();
            SyncHeight();
        }

        public SkinButton Back { get; }
        public SkinButton Forward { get; }
        public SkinButton Up { get; }
        public SkinButton Reload { get; }
        public AddressBar Address { get; }
        public SkinTextBox Filter { get; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Filter.Width = DpiScale.Scale(FilterWidthLogical, DeviceDpi);
            SyncHeight();
        }

        private void SyncHeight()
        {
            Height = Address.Height + (Height - DisplayRectangle.Height);
        }

        private static SkinButton Button(string text)
        {
            return new SkinButton { Text = text, Dock = DockStyle.Left };
        }
    }
}
```

- [ ] **Step 5: Hauptfenster-Grundaufbau**

`src/FileManager.App/MainForm.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FileManager.App.Shell;
using FileManager.App.Views;
using FileManager.Core.Config;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using UIFramework.Controls;
using UIFramework.Core.Skinning;
using UIFramework.Core.Skinning.Skins;
using UIFramework.Gallery;
using UIFramework.Grid;
using UIFramework.Tree;
using TreeNode = UIFramework.Tree.TreeNode;

namespace FileManager.App
{
    /// <summary>
    /// Das Hauptfenster (Spec „Oberfläche"): Ribbon, Navigationszeile, links
    /// Baum, rechts Details oder Kacheln, unten Übertragungen und Status. Die
    /// Logik steckt im Kern (FileManager.Core.Presentation); die Teilklassen
    /// MainForm.*.cs binden je ein Thema an, eingehängt über die partiellen
    /// Wire*-Haken und die Ereignisse unten.
    /// </summary>
    internal sealed partial class MainForm : AppForm
    {
        private const int TileImageLogical = 64;

        // ---- Zustand ---------------------------------------------------------------
        private readonly FileManagerSettings _settings;
        private ExplorerSession _session;
        private string _currentPath;
        private CommandState _commands = CommandState.For(false, Zone.Locked, new FolderRow[0], p => Zone.Locked);
        private readonly List<FolderRow> _rows = new List<FolderRow>();
        private readonly ListDataSource<FolderRow> _list;
        private readonly FilteredSource _filtered;
        private readonly SortedSource _sorted;
        private string _sortKey = "Name";
        private SortDirection _sortDirection = SortDirection.Ascending;
        private string _filterText = "";
        private readonly ShellFileInfo _shell = new ShellFileInfo();
        private readonly Dictionary<string, Image> _titleIcons = new Dictionary<string, Image>(StringComparer.Ordinal);

        // ---- Controls ----------------------------------------------------------------
        private readonly RibbonControl _ribbon = new RibbonControl { Dock = DockStyle.Top };
        private readonly NavigationStrip _nav = new NavigationStrip();
        private readonly SkinSplitContainer _split = new SkinSplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 160,
            Panel2MinSize = 300
        };
        private readonly TreeControl _tree = new TreeControl { Dock = DockStyle.Fill };
        private readonly TreeNode _quickRoot = new TreeNode("Schnellzugriff");
        private readonly TreeNode _consoleRoot = new TreeNode("PS4");
        private readonly GridControl _grid = new GridControl { Dock = DockStyle.Fill };
        private readonly GalleryControl _gallery = new GalleryControl { Dock = DockStyle.Fill, Visible = false };
        private readonly SkinPanel _statusBar = new SkinPanel { Dock = DockStyle.Bottom };
        private readonly SkinLabel _status = new SkinLabel { Dock = DockStyle.Fill, AutoSize = false, Text = "Getrennt" };

        // ---- Ribbon (Spec: „Start" und „Ansicht"; Plan-Entscheidungen 2 und 3) ------
        private readonly RibbonItem _connectItem = Small("Verbinden");
        private readonly RibbonItem _targetItem = Small("Konsole wählen …");
        private readonly RibbonItem _uploadItem = Small("Hochladen …");
        private readonly RibbonItem _uploadFolderItem = Small("Ordner hochladen …");
        private readonly RibbonItem _downloadItem = Small("Herunterladen …");
        private readonly RibbonItem _newFolderItem = Small("Neuer Ordner");
        private readonly RibbonItem _renameItem = Small("Umbenennen");
        private readonly RibbonItem _deleteItem = Small("Löschen");
        private readonly RibbonItem _detailsItem = Toggle("Details");
        private readonly RibbonItem _tilesItem = Toggle("Kacheln");
        private readonly RibbonItem _expertItem = Toggle("Expertenmodus");
        private readonly RibbonItem _lightItem = Toggle("Helles Design");

        // ---- Ereignisse für die Teilklassen -------------------------------------------
        // (SessionStarted, FolderShown und FileActivated deklariert Task 15 dort,
        // wo sie ausgelöst werden — ein nie ausgelöstes Ereignis wäre Warnung CS0067.)
        /// <summary>Vor dem Entsorgen einer Sitzung (Trennen, neu Verbinden, Beenden).</summary>
        private event Action<ExplorerSession> SessionEnding;
        /// <summary>Beim Schließen: jeder Teil trägt ein, was dagegen spräche.</summary>
        private event Action<List<string>> CollectingCloseWarnings;

        partial void WireConnection();
        partial void WireBrowsing();
        partial void WireTitles();
        partial void AddTransferPanel();
        partial void WireTransfers();
        partial void WireOperations();
        partial void WireClipboard();
        partial void WireMenus();
        partial void WireEditing();

        public MainForm()
        {
            Text = "PS4 File Manager";
            ClientSize = new Size(1200, 760);
            StartPosition = FormStartPosition.CenterScreen;

            _settings = FileManagerSettingsStore.Load(AppPaths.Settings);

            _list = new ListDataSource<FolderRow>(_rows);
            _list.Map("Name", r => r.NameCell);
            _list.Map("Geändert", r => r.ModifiedCell);
            _list.Map("Typ", r => r.TypeCell);
            _list.Map("Größe", r => r.SizeCell);
            _list.Map("TitelId", r => r.TitleIdCell);
            _list.Map("Row", r => r);
            _list.Map("Rank", r => r.GroupRank);
            _list.MapImage("Name", r => IconFor(r, false));
            _filtered = new FilteredSource(_list, (source, i) => Matches((FolderRow)source.GetValue(i, "Row")));
            _sorted = new SortedSource(_filtered);

            BuildRibbon();
            BuildGrid();
            BuildGallery();
            BuildTree();

            _split.SplitterDistance = _settings.TreeWidth;
            _split.Panel1.Controls.Add(_tree);
            _split.Panel2.Controls.Add(_grid);
            _split.Panel2.Controls.Add(_gallery);
            _statusBar.Controls.Add(_status);

            // Zuletzt Hinzugefügtes dockt außen: Fill zuerst, dann die Übertragungen
            // (innen unten), die Statusleiste (ganz unten), die Navigation und
            // zuletzt das Ribbon (ganz oben).
            Controls.Add(_split);
            AddTransferPanel();
            Controls.Add(_statusBar);
            Controls.Add(_nav);
            Controls.Add(_ribbon);

            _grid.Selection.Changed += (s, e) => { UpdateCommands(); UpdateStatus(); };
            _gallery.Gallery.SelectionChanged += (s, e) => { UpdateCommands(); UpdateStatus(); };

            WireConnection();
            WireBrowsing();
            WireTitles();
            WireTransfers();
            WireOperations();
            WireClipboard();
            WireMenus();
            WireEditing();

            SetViewMode(_settings.ViewMode);
            _expertItem.Checked = _settings.ExpertMode;
            UpdateTargetText();
            UpdateCommands();
            UpdateStatus();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ClientSize = new Size(Px(1200), Px(760));
            CenterToScreen();
            _statusBar.Height = Px(28);
        }

        // ---- Aufbau --------------------------------------------------------------------

        private static RibbonItem Small(string text)
        {
            return new RibbonItem(text) { Size = RibbonItemSize.Small };
        }

        private static RibbonItem Toggle(string text)
        {
            return new RibbonItem(text) { Size = RibbonItemSize.Small, Kind = RibbonItemKind.ToggleButton };
        }

        private void BuildRibbon()
        {
            var connection = new RibbonGroup("Verbindung");
            connection.Items.Add(_connectItem);
            connection.Items.Add(_targetItem);

            var transfer = new RibbonGroup("Übertragen");
            transfer.Items.Add(_uploadItem);
            transfer.Items.Add(_uploadFolderItem);
            transfer.Items.Add(_downloadItem);

            var organize = new RibbonGroup("Organisieren");
            organize.Items.Add(_newFolderItem);
            organize.Items.Add(_renameItem);
            organize.Items.Add(_deleteItem);

            var start = new RibbonTab("Start");
            start.Groups.Add(connection);
            start.Groups.Add(transfer);
            start.Groups.Add(organize);

            var layout = new RibbonGroup("Layout");
            layout.Items.Add(_detailsItem);
            layout.Items.Add(_tilesItem);

            var protection = new RibbonGroup("Schutz");
            protection.Items.Add(_expertItem);

            var design = new RibbonGroup("Design");
            design.Items.Add(_lightItem);

            var view = new RibbonTab("Ansicht");
            view.Groups.Add(layout);
            view.Groups.Add(protection);
            view.Groups.Add(design);

            _ribbon.Tabs.Add(start);
            _ribbon.Tabs.Add(view);

            // ToggleButton: Checked steht beim Click schon auf dem NEUEN Zustand.
            _detailsItem.Click += (s, e) => SetViewMode(FileManagerSettings.DetailsView);
            _tilesItem.Click += (s, e) => SetViewMode(FileManagerSettings.TilesView);
            _expertItem.Click += (s, e) => SetExpertMode(_expertItem.Checked);
            _lightItem.Click += (s, e) => SkinManager.Current = _lightItem.Checked ? (ISkin)new LightSkin() : new DarkSkin();
        }

        private void BuildGrid()
        {
            _grid.Columns.Add(new GridColumn("Name", "Name") { Width = 320, ShowImage = true, EditorFactory = () => new SkinTextBox() });
            _grid.Columns.Add(new GridColumn("Geändert", "Geändert") { Width = 100, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Typ", "Typ") { Width = 160, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Größe", "Größe") { Width = 100, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("TitelId", "Title-ID") { Width = 100, ReadOnly = true });
            foreach (var column in _grid.Columns)
            {
                int width;
                if (_settings.ColumnWidths.TryGetValue(column.Key, out width)) column.Width = width;
            }

            _grid.EditTriggers = GridEditTrigger.F2;         // Doppelklick/Enter öffnen (Spec)
            _grid.TypeAheadColumnKey = "Name";
            _grid.DataSource = _sorted;

            _grid.HeaderClick += (s, column) =>
            {
                string key = _grid.Columns[column].Key;
                _sortDirection = key == _sortKey && _sortDirection == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending;
                _sortKey = key;
                ApplySort();
            };
        }

        private void BuildGallery()
        {
            _gallery.Gallery.ImageSize = new Size(TileImageLogical, TileImageLogical);
            _gallery.Gallery.ItemCheckMode = ItemCheckMode.Multiple;
            _gallery.Gallery.ShowGroupCaption = false;
        }

        private void BuildTree()
        {
            _quickRoot.HasChildrenHint = false;
            _consoleRoot.Tag = "/";
            _consoleRoot.HasChildrenHint = true;
            _consoleRoot.Image = _shell.SmallIcon("", true);
            _tree.Nodes.Add(_quickRoot);
            _tree.Nodes.Add(_consoleRoot);
        }

        // ---- Liste ---------------------------------------------------------------------

        private bool IsTilesView { get { return _gallery.Visible; } }

        private bool Matches(FolderRow row)
        {
            return _filterText.Length == 0
                || row.ShownName.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0
                || row.DisplayName.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Nach jedem Neubefüllen oder Filtern neu binden — GridControl zieht
        /// Bildlauf und Auswahl nur im DataSource-Setter nach (UIFramework
        /// offene-punkte.md „Neu befüllen am Ort verlangt Neubinden").
        /// </summary>
        private void Rebind()
        {
            _grid.DataSource = null;
            _filtered.Refresh();
            ApplySort();
            _grid.DataSource = _sorted;
            FillGallery();
        }

        private void ApplySort()
        {
            _sorted.Sort(_sortKey, _sortDirection, i => (int)_filtered.GetValue(i, "Rank"));
            _grid.Invalidate();
        }

        private FolderRow RowAt(int row)
        {
            return row < 0 || row >= _sorted.RowCount ? null : (FolderRow)_sorted.GetValue(row, "Row");
        }

        private List<FolderRow> CurrentSelection()
        {
            if (IsTilesView) return _gallery.Gallery.CheckedItems.Select(i => (FolderRow)i.Tag).ToList();
            return _grid.Selection.SelectedRows.Select(RowAt).Where(r => r != null).ToList();
        }

        private Zone CurrentZone()
        {
            return _session != null && _currentPath != null
                ? FolderBrowser.ContentsZone(_session.Policy, _currentPath)
                : Zone.Locked;
        }

        private void FillGallery()
        {
            _gallery.Gallery.Groups.Clear();
            var group = new GalleryItemGroup();
            var visible = Enumerable.Range(0, _filtered.RowCount)
                .Select(i => (FolderRow)_filtered.GetValue(i, "Row"))
                .OrderBy(r => r.GroupRank)
                .ThenBy(r => r.ShownName, StringComparer.CurrentCultureIgnoreCase);
            foreach (var row in visible)
            {
                group.Items.Add(new GalleryItem
                {
                    Caption = row.ShownName,
                    Description = row.IsFolder || row.IsLink ? "" : SizeFormat.Bytes(row.Entry.Size),
                    Image = IconFor(row, true),
                    Tag = row
                });
            }
            if (group.Items.Count > 0) _gallery.Gallery.Groups.Add(group);
        }

        private Image IconFor(FolderRow row, bool large)
        {
            Image title;
            if (row.TitleId != null && _titleIcons.TryGetValue(row.TitleId, out title)) return title;
            if (row.IsLink) return _shell.LinkIcon();
            return large ? _shell.LargeIcon(row.Entry.Name, row.IsFolder) : _shell.SmallIcon(row.Entry.Name, row.IsFolder);
        }

        // ---- Zustand anzeigen ------------------------------------------------------------

        private void UpdateCommands()
        {
            bool connected = _session != null && _currentPath != null;
            Func<string, Zone> zoneOf = _session != null ? (Func<string, Zone>)_session.Policy.Effective : p => Zone.Locked;
            _commands = CommandState.For(connected, CurrentZone(), CurrentSelection(), zoneOf);

            _uploadItem.Enabled = _commands.CanUpload;
            _uploadFolderItem.Enabled = _commands.CanUpload;
            _downloadItem.Enabled = _commands.CanDownload;
            _newFolderItem.Enabled = _commands.CanNewFolder;
            _renameItem.Enabled = _commands.CanRename;
            _deleteItem.Enabled = _commands.CanDelete;
            _connectItem.Text = _session != null ? "Trennen" : "Verbinden";
            _ribbon.Invalidate();                       // RibbonItem meldet Änderungen nicht selbst

            _nav.Back.Enabled = _session != null && _session.Browser.CanGoBack;
            _nav.Forward.Enabled = _session != null && _session.Browser.CanGoForward;
            _nav.Up.Enabled = _session != null && _session.Browser.CanGoUp;
            _nav.Reload.Enabled = connected;
        }

        private void UpdateStatus()
        {
            _status.Text = StatusText.Line(_session != null, _filtered.RowCount, CurrentSelection(), CurrentZone());
        }

        private void UpdateTargetText()
        {
            _targetItem.Text = string.IsNullOrWhiteSpace(_settings.Ip) ? "Konsole wählen …" : _settings.Ip + ":" + _settings.Port;
            _ribbon.Invalidate();
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }

        /// <summary>
        /// Fehler einer Anwenderaktion: Zonen-, Namens- und „geht nicht"-Fehler
        /// als Meldung (Spec „Locked → Meldung mit Pfad und Grund"), alles
        /// andere (Verbindung, Server) als Status wie in der Gallery.
        /// </summary>
        private void ShowError(Exception ex)
        {
            if (ex is OperationCanceledException) return;
            if (ex is ZoneViolationException || ex is NotSupportedException || ex is InvalidOperationException || ex is ArgumentException)
            {
                Dialogs.MessageDialog.Info(this, "PS4 File Manager", Ui.Describe(ex));
                return;
            }
            SetStatus("Fehler: " + Ui.Describe(ex));
        }

        private void SetViewMode(string mode)
        {
            bool tiles = mode == FileManagerSettings.TilesView;
            _settings.ViewMode = tiles ? FileManagerSettings.TilesView : FileManagerSettings.DetailsView;
            _gallery.Visible = tiles;
            _grid.Visible = !tiles;
            _detailsItem.Checked = !tiles;
            _tilesItem.Checked = tiles;
            _ribbon.Invalidate();
            UpdateCommands();
            UpdateStatus();
        }

        private void SetExpertMode(bool on)
        {
            _settings.ExpertMode = on;
            _expertItem.Checked = on;
            if (_session != null) _session.ExpertMode = on;
            UpdateCommands();
            UpdateStatus();
        }

        // ---- Schließen -------------------------------------------------------------------

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var warnings = new List<string>();
            var collect = CollectingCloseWarnings;
            if (collect != null) collect(warnings);
            if (warnings.Count > 0 && !e.Cancel)
            {
                bool leave = Dialogs.MessageDialog.Ask(this, "Beenden",
                    string.Join("\n", warnings) + "\n\nTrotzdem beenden?", "Beenden", "Weiterarbeiten");
                if (!leave) e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _settings.TreeWidth = _split.SplitterDistance;
            foreach (var column in _grid.Columns) _settings.ColumnWidths[column.Key] = column.Width;
            try { FileManagerSettingsStore.Save(AppPaths.Settings, _settings); }
            catch (Exception) { /* Einstellungen sind Komfort — ein Schreibfehler hält das Beenden nicht auf */ }

            EndSession();
            base.OnFormClosed(e);
        }

        /// <summary>Beendet die laufende Sitzung (Trennen, neu Verbinden, Beenden).</summary>
        private void EndSession()
        {
            var session = _session;
            if (session == null) return;
            var ending = SessionEnding;
            if (ending != null) ending(session);
            _session = null;
            _currentPath = null;
            session.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            // ERST base.Dispose: Baum, Grid und Kacheln halten die Bilder noch
            // (UIFramework-Hausregel, Muster ExplorerForm).
            base.Dispose(disposing);
            if (disposing)
            {
                _shell.Dispose();
                foreach (var image in _titleIcons.Values) image.Dispose();
                _titleIcons.Clear();
            }
        }
    }
}
```

Hinweis: `Dialogs.MessageDialog` entsteht in Task 14. Damit dieser Task allein baut, legt Step 5 zusätzlich eine **vorläufige** `src/FileManager.App/Dialogs/MessageDialog.cs` an, die Task 14 vollständig ersetzt:

```csharp
using System.Windows.Forms;

namespace FileManager.App.Dialogs
{
    /// <summary>Vorläufig (Task 13) — Task 14 ersetzt diese Datei durch den geskinnten Dialog.</summary>
    internal static class MessageDialog
    {
        public static void Info(IWin32Window owner, string title, string message)
        {
            MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static bool Ask(IWin32Window owner, string title, string message, string yes, string no)
        {
            return MessageBox.Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
    }
}
```

- [ ] **Step 6: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5`
Expected: `0 Warnung(en)`, `0 Fehler`.

Run: `dotnet test ScreenshotGallery.sln`
Expected: alles grün (keine neuen Tests).

Die App wird **nicht** gestartet.

- [ ] **Step 7: Commit**

```bash
git add ScreenshotGallery.sln src/FileManager.App
git commit -m "Scaffold the file manager app with ribbon, tree, list and status bar"
```

Fahrprobe-Punkte (Task 22): Fenster öffnet dunkel; Ribbon mit „Start"/„Ansicht"; Navigationszeile mit ←→↑↻, Adressleiste „PS4", Filterfeld; Baum mit „Schnellzugriff" und „PS4"; „Kacheln"/„Details" schalten um; „Helles Design" wechselt den Skin hin und zurück; bei 125 % Skalierung scharf und nichts abgeschnitten.

---

### Task 14: Geskinnte Dialoge, Rückfragen und Konflikte

**Files:**
- Create: `src/FileManager.App/Views/EditBox.cs`
- Modify (ersetzen): `src/FileManager.App/Dialogs/MessageDialog.cs`
- Create: `src/FileManager.App/Dialogs/ConnectionDialog.cs`, `src/FileManager.App/Dialogs/RenameDialog.cs`
- Create: `src/FileManager.App/Dialogs/DialogPrompts.cs`, `src/FileManager.App/Dialogs/DialogConflictResolver.cs`

**Interfaces:**
- Consumes: `AppForm.Px`, `Ui.OnUiAsync` (Task 13), `IUserPrompts`, `DeleteSummary`, `IConflictResolver`, `ConflictInfo`, `ConflictDecision`, `SizeFormat` (Task 8), `RemotePath`.
- Produces:
  - `EditBox : SkinTextBox` — `void FocusText()` (Fokus in den nativen Textkern, alles markiert; ein `Focus()` auf das äußere Control ließe die Tastatur ins Leere laufen).
  - `MessageDialog : AppForm` — `MessageDialog(string title, string message, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex, string checkText)`, `Choice : int`, `Checked : bool`; statisch `int Choose(IWin32Window owner, string title, string message, params string[] buttons)` (Standard = erster Knopf, Abbrechen = letzter), `void Info(owner, title, message)`, `bool Ask(owner, title, message, string yes, string no)`. Enter = Standardknopf (oder der fokussierte), Escape/Schließen = Abbrechen-Knopf.
  - `ConnectionDialog : AppForm` — `ConnectionDialog(string ip, int port)`, `Ip`, `Port` (gültig nach `DialogResult.OK`).
  - `RenameDialog : AppForm` — `RenameDialog(string currentName)`, `NewName`.
  - `DialogPrompts : IUserPrompts` — `DialogPrompts(Form owner)`; Warnzone („Fortfahren"/„Abbrechen", Standard Abbrechen) und Löschbestätigung („Löschen"/„Abbrechen", Standard Abbrechen, Zonenwarnung im selben Dialog) — aus jedem Thread aufrufbar.
  - `DialogConflictResolver : IConflictResolver` — `DialogConflictResolver(Form owner)`; Knöpfe „Ersetzen", „Überspringen", „Beide behalten" (Standard Überspringen), Haken „Für alle weiteren Konflikte dieses Auftrags".

- [ ] **Step 1: `EditBox`**

`src/FileManager.App/Views/EditBox.cs`:

```csharp
using UIFramework.Controls;

namespace FileManager.App.Views
{
    /// <summary>
    /// SkinTextBox, der seinen nativen Textkern selbst fokussieren kann — ein
    /// Focus() auf das äußere Control „gelingt", lässt die Tastatur aber ins
    /// Leere laufen (UIFramework, Befund G1 der Grid-Zellbearbeitung).
    /// </summary>
    internal sealed class EditBox : SkinTextBox
    {
        public void FocusText()
        {
            if (InnerTextBox == null) { Focus(); return; }
            InnerTextBox.Focus();
            InnerTextBox.SelectAll();
        }
    }
}
```

- [ ] **Step 2: `MessageDialog`**

`src/FileManager.App/Dialogs/MessageDialog.cs` vollständig ersetzen durch:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UIFramework.Controls;

namespace FileManager.App.Dialogs
{
    /// <summary>
    /// Kleiner geskinnter Dialog (Spec „Dialoge als kleine SkinnedForms";
    /// UIFramework hat keine Meldungsbox, Plan-Entscheidung 1): Text
    /// zeilenweise, optional ein Haken, Knöpfe rechts unten.
    /// </summary>
    internal sealed class MessageDialog : AppForm
    {
        private readonly SkinPanel _body = new SkinPanel { Dock = DockStyle.Fill };
        private readonly List<SkinLabel> _lines = new List<SkinLabel>();
        private readonly List<SkinButton> _buttons = new List<SkinButton>();
        private readonly CheckEdit _check;
        private readonly int _defaultIndex;
        private readonly int _cancelIndex;

        public MessageDialog(string title, string message, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex, string checkText)
        {
            if (buttons == null || buttons.Count == 0) throw new ArgumentException("Mindestens ein Knopf.", nameof(buttons));
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            _defaultIndex = defaultIndex;
            _cancelIndex = cancelIndex;
            Choice = cancelIndex;

            foreach (var line in (message ?? "").Replace("\r", "").Split('\n'))
            {
                var label = new SkinLabel { Text = line.Length == 0 ? " " : line };
                _lines.Add(label);
                _body.Controls.Add(label);
            }

            if (checkText != null)
            {
                _check = new CheckEdit { Text = checkText };
                _body.Controls.Add(_check);
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                int index = i;
                var button = new SkinButton { Text = buttons[i] };
                button.Click += (s, e) =>
                {
                    Choice = index;
                    DialogResult = index == _cancelIndex ? DialogResult.Cancel : DialogResult.OK;
                };
                _buttons.Add(button);
                _body.Controls.Add(button);
            }

            Controls.Add(_body);
        }

        /// <summary>Index des gewählten Knopfs; Escape oder Schließen ergibt den Abbrechen-Knopf.</summary>
        public int Choice { get; private set; }

        public bool Checked { get { return _check != null && _check.Checked; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            int margin = Px(16);
            int gap = Px(8);
            int y = margin;
            int width = Px(320);

            foreach (var label in _lines)
            {
                label.Size = label.GetPreferredSize(Size.Empty);
                label.Location = new Point(margin, y);
                y += label.Height + Px(2);
                width = Math.Max(width, label.Width);
            }

            if (_check != null)
            {
                _check.Size = _check.GetPreferredSize(Size.Empty);
                y += gap;
                _check.Location = new Point(margin, y);
                y += _check.Height;
                width = Math.Max(width, _check.Width);
            }

            y += margin;
            foreach (var button in _buttons) button.Size = button.GetPreferredSize(Size.Empty);
            int buttonsWidth = _buttons.Sum(b => b.Width) + gap * (_buttons.Count - 1);
            width = Math.Max(width, buttonsWidth);
            int x = margin + width - buttonsWidth;
            foreach (var button in _buttons)
            {
                button.Location = new Point(x, y);
                x += button.Width + gap;
            }

            ClientSize = new Size(width + 2 * margin, y + _buttons.Max(b => b.Height) + margin);
            if (Owner != null) CenterToParent(); else CenterToScreen();
            ActiveControl = _buttons[_defaultIndex];          // vor dem Anzeigen greift nur ActiveControl, nicht Focus()
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Choice = _cancelIndex;
                DialogResult = DialogResult.Cancel;
                return true;
            }
            if (keyData == Keys.Enter)
            {
                var focused = _buttons.FirstOrDefault(b => b.Focused);
                (focused ?? _buttons[_defaultIndex]).PerformClick();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        public static int Choose(IWin32Window owner, string title, string message, params string[] buttons)
        {
            using (var dialog = new MessageDialog(title, message, buttons, 0, buttons.Length - 1, null))
            {
                dialog.ShowDialog(owner);
                return dialog.Choice;
            }
        }

        public static void Info(IWin32Window owner, string title, string message)
        {
            Choose(owner, title, message, "OK");
        }

        public static bool Ask(IWin32Window owner, string title, string message, string yes, string no)
        {
            return Choose(owner, title, message, yes, no) == 0;
        }
    }
}
```

- [ ] **Step 3: Verbindungs- und Umbenennen-Dialog**

`src/FileManager.App/Dialogs/ConnectionDialog.cs`:

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using FileManager.App.Views;
using UIFramework.Controls;

namespace FileManager.App.Dialogs
{
    /// <summary>IP und Port der Konsole (Plan-Entscheidung 2: das Ribbon kennt kein Textfeld).</summary>
    internal sealed class ConnectionDialog : AppForm
    {
        private readonly SkinPanel _body = new SkinPanel { Dock = DockStyle.Fill };
        private readonly SkinLabel _ipLabel = new SkinLabel { Text = "IP-Adresse der PS4:" };
        private readonly EditBox _ip = new EditBox();
        private readonly SkinLabel _portLabel = new SkinLabel { Text = "Port (GoldHEN-FTP):" };
        private readonly EditBox _port = new EditBox();
        private readonly SkinLabel _error = new SkinLabel { Text = " " };
        private readonly SkinButton _ok = new SkinButton { Text = "Verbinden" };
        private readonly SkinButton _cancel = new SkinButton { Text = "Abbrechen" };

        public ConnectionDialog(string ip, int port)
        {
            Text = "Verbindung zur PS4";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            _ip.Text = ip ?? "";
            _port.Text = port.ToString();
            _ok.Click += (s, e) => Confirm();
            _cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            foreach (Control c in new Control[] { _ipLabel, _ip, _portLabel, _port, _error, _ok, _cancel }) _body.Controls.Add(c);
            Controls.Add(_body);
        }

        public string Ip { get; private set; }
        public int Port { get; private set; }

        private void Confirm()
        {
            var ip = _ip.Text.Trim();
            int port;
            if (ip.Length == 0) { _error.Text = "Bitte die IP-Adresse der PS4 eingeben."; return; }
            if (!int.TryParse(_port.Text.Trim(), out port) || port < 1 || port > 65535) { _error.Text = "Ungültiger Port."; return; }
            Ip = ip;
            Port = port;
            DialogResult = DialogResult.OK;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int margin = Px(16);
            int gap = Px(8);
            int fieldWidth = Px(240);
            foreach (var label in new[] { _ipLabel, _portLabel, _error }) label.Size = label.GetPreferredSize(Size.Empty);
            _ok.Size = _ok.GetPreferredSize(Size.Empty);
            _cancel.Size = _cancel.GetPreferredSize(Size.Empty);

            int y = margin;
            _ipLabel.Location = new Point(margin, y); y += _ipLabel.Height + Px(4);
            _ip.SetBounds(margin, y, fieldWidth, _ip.Height); y += _ip.Height + gap;
            _portLabel.Location = new Point(margin, y); y += _portLabel.Height + Px(4);
            _port.SetBounds(margin, y, Px(90), _port.Height); y += _port.Height + gap;
            _error.Location = new Point(margin, y); y += _error.Height + margin;

            int right = margin + fieldWidth;
            _cancel.Location = new Point(right - _cancel.Width, y);
            _ok.Location = new Point(_cancel.Left - gap - _ok.Width, y);
            ClientSize = new Size(right + margin, y + _ok.Height + margin);
            if (Owner != null) CenterToParent(); else CenterToScreen();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ip.FocusText();                                  // erst sichtbar lässt sich der Textkern fokussieren
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter) { Confirm(); return true; }
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessDialogKey(keyData);
        }
    }
}
```

`src/FileManager.App/Dialogs/RenameDialog.cs`:

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using FileManager.App.Views;
using UIFramework.Controls;

namespace FileManager.App.Dialogs
{
    /// <summary>Umbenennen in der Kachelansicht — dort gibt es keine Zellbearbeitung (Spec „Umbenennen").</summary>
    internal sealed class RenameDialog : AppForm
    {
        private readonly SkinPanel _body = new SkinPanel { Dock = DockStyle.Fill };
        private readonly SkinLabel _label;
        private readonly EditBox _name = new EditBox();
        private readonly SkinButton _ok = new SkinButton { Text = "Umbenennen" };
        private readonly SkinButton _cancel = new SkinButton { Text = "Abbrechen" };

        public RenameDialog(string currentName)
        {
            Text = "Umbenennen";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            _label = new SkinLabel { Text = "Neuer Name für „" + currentName + "“:" };
            _name.Text = currentName;
            _ok.Click += (s, e) => Confirm();
            _cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            foreach (Control c in new Control[] { _label, _name, _ok, _cancel }) _body.Controls.Add(c);
            Controls.Add(_body);
        }

        public string NewName { get; private set; }

        private void Confirm()
        {
            NewName = _name.Text;
            DialogResult = DialogResult.OK;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int margin = Px(16);
            int gap = Px(8);
            _label.Size = _label.GetPreferredSize(Size.Empty);
            _ok.Size = _ok.GetPreferredSize(Size.Empty);
            _cancel.Size = _cancel.GetPreferredSize(Size.Empty);
            int width = Math.Max(Px(320), _label.Width);

            int y = margin;
            _label.Location = new Point(margin, y); y += _label.Height + Px(4);
            _name.SetBounds(margin, y, width, _name.Height); y += _name.Height + margin;
            _cancel.Location = new Point(margin + width - _cancel.Width, y);
            _ok.Location = new Point(_cancel.Left - gap - _ok.Width, y);
            ClientSize = new Size(width + 2 * margin, y + _ok.Height + margin);
            if (Owner != null) CenterToParent(); else CenterToScreen();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _name.FocusText();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter) { Confirm(); return true; }
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessDialogKey(keyData);
        }
    }
}
```

- [ ] **Step 4: Rückfragen und Konflikte des Kerns**

`src/FileManager.App/Dialogs/DialogPrompts.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;

namespace FileManager.App.Dialogs
{
    /// <summary>
    /// Die Rückfragen des Kerns als Dialoge (Spec „Dialoge"). Der Kern ruft sie
    /// aus Hintergrund-Threads — Ui.OnUiAsync bringt sie auf den UI-Thread.
    /// Standardknopf ist jeweils „Abbrechen": Enter soll nichts zerstören.
    /// </summary>
    internal sealed class DialogPrompts : IUserPrompts
    {
        private const int MaxListed = 5;
        private readonly Form _owner;

        public DialogPrompts(Form owner)
        {
            _owner = owner;
        }

        public Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct)
        {
            var text = "„" + action + "“ betrifft einen geschützten Bereich der PS4 (Warnzone):\n"
                       + Listing(paths)
                       + "\nFehler hier können Spiele oder Spielstände beschädigen.";
            return Ui.OnUiAsync(_owner, () => Confirm("Warnzone", text, "Fortfahren"));
        }

        public Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct)
        {
            var parts = new List<string> { Count(summary.FileCount, "Datei", "Dateien") };
            if (summary.FolderCount > 0) parts.Add(Count(summary.FolderCount, "Ordner", "Ordner"));
            if (summary.TotalBytes > 0) parts.Add(SizeFormat.Bytes(summary.TotalBytes));

            var text = "Endgültig löschen: " + string.Join(", ", parts) + "\n"
                       + Listing(summary.TopLevelNames)
                       + "\nDie PS4 hat keinen Papierkorb.";
            if (summary.Zone == Zone.Warn)
                text += "\n\nAchtung: Das liegt in einem geschützten Bereich (Warnzone) —\nFehler hier können Spiele oder Spielstände beschädigen.";
            return Ui.OnUiAsync(_owner, () => Confirm("Endgültig löschen", text, "Löschen"));
        }

        private bool Confirm(string title, string text, string yes)
        {
            using (var dialog = new MessageDialog(title, text, new[] { yes, "Abbrechen" }, 1, 1, null))
            {
                dialog.ShowDialog(_owner);
                return dialog.Choice == 0;
            }
        }

        private static string Listing(IReadOnlyList<string> names)
        {
            var lines = names.Take(MaxListed).Select(n => "   " + n).ToList();
            if (names.Count > MaxListed) lines.Add("   … und " + (names.Count - MaxListed) + " weitere");
            return string.Join("\n", lines) + "\n";
        }

        private static string Count(int n, string one, string many)
        {
            return n == 1 ? "1 " + one : n + " " + many;
        }
    }
}
```

`src/FileManager.App/Dialogs/DialogConflictResolver.cs`:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace FileManager.App.Dialogs
{
    /// <summary>
    /// Konfliktdialog (Spec „Konflikte"): Ersetzen, Überspringen, Beide behalten,
    /// dazu „für alle weiteren". Die Warteschlange wartet auf die Antwort; der
    /// Dialog läuft auf dem UI-Thread (nie umgekehrt warten).
    /// </summary>
    internal sealed class DialogConflictResolver : IConflictResolver
    {
        private readonly Form _owner;

        public DialogConflictResolver(Form owner)
        {
            _owner = owner;
        }

        public Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct)
        {
            return Ui.OnUiAsync(_owner, () => Ask(conflict));
        }

        private ConflictDecision Ask(ConflictInfo conflict)
        {
            var name = conflict.TargetIsRemote ? RemotePath.Name(conflict.TargetPath) : Path.GetFileName(conflict.TargetPath);
            var where = conflict.TargetIsRemote ? "auf der PS4" : "auf dem PC";
            var text = "„" + name + "“ gibt es " + where + " schon.\n"
                       + "Vorhanden: " + SizeFormat.Bytes(conflict.ExistingSize) + " · Neu: " + SizeFormat.Bytes(conflict.NewSize) + "\n"
                       + "\n„Beide behalten“ legt die neue Datei als „" + conflict.KeepBothName + "“ an.";
            using (var dialog = new MessageDialog("Datei existiert bereits", text,
                new[] { "Ersetzen", "Überspringen", "Beide behalten" }, 1, 1,
                "Für alle weiteren Konflikte dieses Auftrags"))
            {
                dialog.ShowDialog(_owner);
                var choice = dialog.Choice == 0 ? ConflictChoice.Replace
                           : dialog.Choice == 2 ? ConflictChoice.KeepBoth
                           : ConflictChoice.Skip;
                return new ConflictDecision(choice, dialog.Checked);
            }
        }
    }
}
```

- [ ] **Step 5: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5` — `0 Warnung(en)`, `0 Fehler`.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 6: Commit**

```bash
git add src/FileManager.App/Views/EditBox.cs src/FileManager.App/Dialogs
git commit -m "Add skinned dialogs for messages, connection, renaming, zone prompts and conflicts"
```

Fahrprobe-Punkte (Task 22): Dialoge erscheinen geskinnt (beide Skins) und zentriert über dem Hauptfenster; Enter löst den Standardknopf aus (bei Löschen/Warnzone/Konflikt ist das der sichere Knopf), Escape bricht ab; Texte nicht abgeschnitten bei 125 %.

---

### Task 15: Verbinden, Baum, Liste, Navigation

**Files:**
- Create: `src/FileManager.App/MainForm.Connection.cs`
- Create: `src/FileManager.App/MainForm.Browsing.cs`

**Interfaces:**
- Consumes: alles aus Task 13/14; `ExplorerSession` (Task 10), `FolderBrowser`/`FolderView`/`PathResolver` (Task 9), `DisplayNames` (Task 7), `FtpClient`, `FileSystemChangeSource`, `SystemClock`.
- Produces (für die Folge-Tasks):
  - Ereignisse `SessionStarted : Action<ExplorerSession>` (Connection), `FolderShown : Action` und `FileActivated : Action<FolderRow>` (Browsing).
  - `Task ConnectAsync()`, `void Disconnect()`, `bool EditConnection()`, `bool ConfirmLeave(string action)`, `void TrySaveSettings()`, `Task LoadQuickAccessAsync(ExplorerSession session)`.
  - `Task NavigateToAsync(string path)`, `void NavigateTo(string path)`, `Task RefreshAsync(bool everything)` (everything = F5: ganzen Cache verwerfen), `void Activate(FolderRow row)`, `TreeNode FolderNode(string label, string path)`, `void ClearView()`.
  - Tastenkürzel-Tabelle: `void AddShortcut(Keys keys, Func<bool> action, bool listOnly)` — `action` liefert `true`, wenn sie die Taste genommen hat; `listOnly` = nur, wenn kein Textfeld den Fokus hat (Entf, Rücktaste, Strg+C/X/V/A gehören sonst dem Textfeld). `ProcessCmdKey` wertet die Tabelle aus — **nur hier** wird `ProcessCmdKey` überschrieben.
  - Partieller Haken `DecorateFolderNode(TreeNode node)` — Task 16 setzt dort Spielnamen/-symbole.
  - `void ReloadTreeFolders(IEnumerable<string> folders)` — liest schon geladene Baumknoten dieser Ordner neu (F5 für alle geladenen; Task 17 nach fertigen Aufträgen — Spec „Verzeichnis-Cache … für Baum und Liste gemeinsam").

- [ ] **Step 1: Verbinden und Trennen**

`src/FileManager.App/MainForm.Connection.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.Core.Config;
using FileManager.Core.Editing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using TreeNode = UIFramework.Tree.TreeNode;

namespace FileManager.App
{
    internal sealed partial class MainForm
    {
        private CancellationTokenSource _connectCts;

        /// <summary>Nach „Verbinden" — die Sitzung ist geprüft und aktiv.</summary>
        private event Action<ExplorerSession> SessionStarted;

        partial void WireConnection()
        {
            _connectItem.Click += async (s, e) =>
            {
                if (_session == null) await ConnectAsync();
                else Disconnect();
            };
            _targetItem.Click += async (s, e) =>
            {
                if (EditConnection()) await ConnectAsync();
            };
        }

        private bool EditConnection()
        {
            using (var dialog = new ConnectionDialog(_settings.Ip, _settings.Port))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                _settings.Ip = dialog.Ip;
                _settings.Port = dialog.Port;
                UpdateTargetText();
                TrySaveSettings();
                return true;
            }
        }

        /// <summary>Verbinden (Spec: danach letzter Pfad, sonst /data). Eine laufende Sitzung wird vorher beendet.</summary>
        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(_settings.Ip) && !EditConnection()) return;
            if (_session != null)
            {
                if (!ConfirmLeave("Neu verbinden")) return;
                EndSession();
                ClearView();
            }

            if (_connectCts != null) _connectCts.Cancel();
            var cts = new CancellationTokenSource();
            _connectCts = cts;

            var session = new ExplorerSession(new FtpClient(_settings.Ip, _settings.Port), _settings.ExpertMode,
                new DialogPrompts(this), new DialogConflictResolver(this),
                AppPaths.WorkRoot, new FileSystemChangeSource(), SystemClock.Instance);

            SetStatus("Verbinde mit " + _settings.Ip + ":" + _settings.Port + " …");
            _connectItem.Enabled = false;
            _ribbon.Invalidate();
            try
            {
                try
                {
                    await session.CheckAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    session.Dispose();
                    if (cts == _connectCts && !(ex is OperationCanceledException)) SetStatus("Fehler: " + Ui.Describe(ex));
                    return;
                }
                if (cts != _connectCts)
                {
                    session.Dispose();
                    return;
                }

                _session = session;
                _consoleRoot.HasChildrenHint = true;     // ein Aufklappen vor dem Verbinden hat den Hinweis gelöscht
                var started = SessionStarted;
                if (started != null) started(session);
                TrySaveSettings();
                UpdateCommands();

                await LoadQuickAccessAsync(session);
                await NavigateToAsync(string.IsNullOrEmpty(_settings.LastPath) ? "/data" : _settings.LastPath);
                if (_currentPath == null && _session == session) await NavigateToAsync("/data");
            }
            finally
            {
                if (cts == _connectCts)
                {
                    _connectItem.Enabled = true;
                    UpdateCommands();
                }
            }
        }

        private void Disconnect()
        {
            if (!ConfirmLeave("Trennen")) return;
            EndSession();
            ClearView();
            UpdateCommands();
            UpdateStatus();
        }

        /// <summary>Fragt, wenn laufende Übertragungen oder offene Änderungen verloren gingen.</summary>
        private bool ConfirmLeave(string action)
        {
            var warnings = new List<string>();
            var collect = CollectingCloseWarnings;
            if (collect != null) collect(warnings);
            return warnings.Count == 0
                || MessageDialog.Ask(this, action, string.Join("\n", warnings) + "\n\nTrotzdem fortfahren?", "Fortfahren", "Abbrechen");
        }

        private void TrySaveSettings()
        {
            try { FileManagerSettingsStore.Save(AppPaths.Settings, _settings); }
            catch (Exception ex) { SetStatus("Einstellungen nicht gespeichert: " + Ui.Describe(ex)); }
        }

        /// <summary>Schnellzugriff (Spec „PS4-Wissen"): nur Vorhandenes, dazu die angehefteten Ordner.</summary>
        private async Task LoadQuickAccessAsync(ExplorerSession session)
        {
            try
            {
                var entries = await session.QuickAccess.BuildAsync(_settings.PinnedFolders, CancellationToken.None);
                if (session != _session) return;
                _quickRoot.Children.ReplaceWith(entries.Select(e => FolderNode(e.Label, e.Path)).ToList());
                _quickRoot.HasChildrenHint = _quickRoot.Children.Count > 0;
                _quickRoot.Expand();
            }
            catch (Exception ex)
            {
                if (session == _session) SetStatus("Schnellzugriff: " + Ui.Describe(ex));
            }
        }
    }
}
```

- [ ] **Step 2: Baum, Liste, Navigation, Tasten**

`src/FileManager.App/MainForm.Browsing.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using Ps4.Core.Ftp;
using TreeNode = UIFramework.Tree.TreeNode;

namespace FileManager.App
{
    internal sealed partial class MainForm
    {
        private readonly Dictionary<Keys, Func<bool>> _shortcuts = new Dictionary<Keys, Func<bool>>();
        private readonly HashSet<Keys> _listOnlyKeys = new HashSet<Keys>();
        private CancellationTokenSource _navCts;
        private bool _clearingFilter;

        /// <summary>Ein Ordner wurde neu gezeigt (Navigation, Aktualisieren).</summary>
        private event Action FolderShown;

        /// <summary>Doppelklick/Enter auf eine Datei — Öffnen mit Rückschreiben (Task 21).</summary>
        private event Action<FolderRow> FileActivated;

        /// <summary>Task 16 setzt hier Spielname und -symbol eines Ordnerknotens.</summary>
        partial void DecorateFolderNode(TreeNode node);

        partial void WireBrowsing()
        {
            // „_ =": Go ist async Task — ein verworfener Aufruf wäre Warnung CS4014.
            _nav.Back.Click += (s, e) => { if (_session != null && _session.Browser.CanGoBack) _ = Go(ct => _session.Browser.BackAsync(ct)); };
            _nav.Forward.Click += (s, e) => { if (_session != null && _session.Browser.CanGoForward) _ = Go(ct => _session.Browser.ForwardAsync(ct)); };
            _nav.Up.Click += (s, e) => { if (_session != null && _session.Browser.CanGoUp) _ = Go(ct => _session.Browser.UpAsync(ct)); };
            _nav.Reload.Click += async (s, e) => await RefreshAsync(true);

            _nav.Address.SegmentClicked += (s, e) => NavigateTo(e.Path);
            _nav.Address.PathSubmitted += (s, e) => NavigateTo(e.Path);
            _nav.Filter.TextChanged += (s, e) =>
            {
                if (_clearingFilter) return;
                _filterText = _nav.Filter.Text.Trim();
                Rebind();
                UpdateCommands();
                UpdateStatus();
            };

            _grid.RowActivated += (s, row) => Activate(RowAt(row));
            _gallery.Gallery.ItemDoubleClick += (s, e) => Activate((FolderRow)e.Item.Tag);

            _tree.NodeExpanding += async (s, e) => await ExpandNodeAsync(e.Node);
            _tree.SelectedNodeChanged += (s, e) =>
            {
                var path = _tree.SelectedNode == null ? null : _tree.SelectedNode.Tag as string;
                if (path != null) NavigateTo(path);
            };

            SessionEnding += session =>
            {
                if (_navCts != null) _navCts.Cancel();
            };

            AddShortcut(Keys.Alt | Keys.Left, () => { _nav.Back.PerformClick(); return true; }, false);
            AddShortcut(Keys.Alt | Keys.Right, () => { _nav.Forward.PerformClick(); return true; }, false);
            AddShortcut(Keys.Alt | Keys.Up, () => { _nav.Up.PerformClick(); return true; }, false);
            AddShortcut(Keys.Back, () => { _nav.Up.PerformClick(); return true; }, true);
            AddShortcut(Keys.F5, () => { var _ = RefreshAsync(true); return true; }, false);
            AddShortcut(Keys.Control | Keys.L, () => { _nav.Address.BeginEdit(); return true; }, false);
            AddShortcut(Keys.Enter, () =>
            {
                // Details: das Grid meldet Enter selbst als RowActivated.
                if (!IsTilesView || !_gallery.ContainsFocus) return false;
                var selection = CurrentSelection();
                if (selection.Count == 1) Activate(selection[0]);
                return true;
            }, true);
        }

        // ---- Tasten ------------------------------------------------------------------------

        private void AddShortcut(Keys keys, Func<bool> action, bool listOnly)
        {
            _shortcuts[keys] = action;
            if (listOnly) _listOnlyKeys.Add(keys);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Func<bool> action;
            if (_shortcuts.TryGetValue(keyData, out action)
                && !(_listOnlyKeys.Contains(keyData) && Ui.IsTextInputFocused())
                && action())
            {
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- Navigation ----------------------------------------------------------------------

        private Task NavigateToAsync(string path)
        {
            return Go(ct => _session.Browser.NavigateAsync(path, ct));
        }

        private void NavigateTo(string path)
        {
            var _ = NavigateToAsync(path);
        }

        /// <summary>Aktualisieren; everything (F5, Spec) verwirft vorher den ganzen Verzeichnis-Cache.</summary>
        private Task RefreshAsync(bool everything)
        {
            if (_session == null || _currentPath == null) return Task.FromResult(0);
            if (everything)
            {
                _session.Cache.Clear();
                ReloadTreeFolders(LoadedFolderNodes(_tree.Nodes).Select(n => (string)n.Tag).ToList());
            }
            return Go(ct => _session.Browser.RefreshAsync(ct));
        }

        /// <summary>Eine Navigation; eine neuere bricht die ältere ab, eine alte Antwort zeigt nichts mehr an.</summary>
        private async Task Go(Func<CancellationToken, Task<FolderView>> navigation)
        {
            var session = _session;
            if (session == null) return;
            if (_navCts != null) _navCts.Cancel();
            var cts = new CancellationTokenSource();
            _navCts = cts;
            SetStatus("Lade …");
            try
            {
                var view = await navigation(cts.Token);
                if (cts != _navCts || session != _session) return;
                ShowFolder(view);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // Spec „Fehlerbehandlung": Listenfehler beim Durchsuchen → Status mit Klartext.
                if (cts == _navCts && session == _session)
                {
                    SetStatus("Fehler: " + Ui.Describe(ex));
                    UpdateCommands();
                }
            }
        }

        private void ShowFolder(FolderView view)
        {
            _currentPath = view.Path;
            _settings.LastPath = view.Path;
            _nav.Address.Path = view.Path;
            ClearFilter();

            _rows.Clear();
            _rows.AddRange(view.Entries.Select(e => new FolderRow(view.Path, e, TypeNameFor(e))));
            Rebind();

            var shown = FolderShown;
            if (shown != null) shown();
            UpdateCommands();
            UpdateStatus();
        }

        private void ClearFilter()
        {
            _clearingFilter = true;
            _nav.Filter.Text = "";
            _clearingFilter = false;
            _filterText = "";
        }

        private string TypeNameFor(FtpEntry entry)
        {
            if (entry.Kind == FtpEntryKind.Link) return "Verknüpfung → " + entry.LinkTarget;
            return _shell.TypeName(entry.Name, entry.IsDirectory);
        }

        /// <summary>Doppelklick/Enter (Spec): Ordner und Verknüpfungen öffnen, Dateien gehen an FileActivated.</summary>
        private void Activate(FolderRow row)
        {
            if (row == null) return;
            if (row.IsFolder || row.IsLink)
            {
                NavigateTo(row.Item.Path);                    // Links löst FolderBrowser über PathResolver auf
                return;
            }
            var activated = FileActivated;
            if (activated != null) activated(row);
        }

        private void ClearView()
        {
            if (_navCts != null) _navCts.Cancel();
            _currentPath = null;
            _rows.Clear();
            ClearFilter();
            Rebind();
            _nav.Address.Path = "/";
            _quickRoot.Children.Clear();
            _quickRoot.HasChildrenHint = false;
            _consoleRoot.Children.ReplaceWith(new TreeNode[0]);   // beendet auch einen hängenden Ladevorgang
            _consoleRoot.HasChildrenHint = true;
        }

        // ---- Baum ------------------------------------------------------------------------------

        private TreeNode FolderNode(string label, string path)
        {
            var node = new TreeNode(label)
            {
                Tag = RemotePath.Normalize(path),
                HasChildrenHint = true,
                Image = _shell.SmallIcon("", true)
            };
            DecorateFolderNode(node);
            return node;
        }

        /// <summary>Schon geladene Baumknoten dieser Ordner neu lesen (Spec „Verzeichnis-Cache": Baum und Liste teilen ihn).</summary>
        private void ReloadTreeFolders(IEnumerable<string> folders)
        {
            var wanted = new HashSet<string>(folders.Select(RemotePath.Canonical), StringComparer.Ordinal);
            foreach (var node in LoadedFolderNodes(_tree.Nodes).ToList())
            {
                if (wanted.Contains(RemotePath.Canonical((string)node.Tag)))
                {
                    var _ = ExpandNodeAsync(node);
                }
            }
        }

        private static IEnumerable<TreeNode> LoadedFolderNodes(IEnumerable<TreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Tag is string && node.Children.Count > 0) yield return node;
                foreach (var child in LoadedFolderNodes(node.Children)) yield return child;
            }
        }

        /// <summary>Kinder erst beim Aufklappen laden (Spec) — über den gemeinsamen Verzeichnis-Cache, Links aufgelöst, nur Ordner.</summary>
        private async Task ExpandNodeAsync(TreeNode node)
        {
            var path = node.Tag as string;
            var session = _session;
            if (path == null || session == null)
            {
                node.Children.ReplaceWith(new TreeNode[0]);
                return;
            }

            node.BeginLoading();
            try
            {
                var resolved = await PathResolver.ResolveAsync(session.Cache, path, CancellationToken.None);
                var entries = await session.Cache.ListAsync(resolved, CancellationToken.None);
                if (session != _session) return;
                node.Children.ReplaceWith(entries
                    .Where(x => x.IsDirectory)
                    .OrderBy(x => DisplayNames.For(x.Name), StringComparer.CurrentCultureIgnoreCase)
                    .Select(x => FolderNode(DisplayNames.For(x.Name), RemotePath.Combine(resolved, x.Name)))
                    .ToList());
            }
            catch (Exception ex)
            {
                if (session == _session) node.EndLoadingWithError(Ui.Describe(ex));
            }
        }
    }
}
```

- [ ] **Step 3: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5` — `0 Warnung(en)`, `0 Fehler`.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 4: Commit**

```bash
git add src/FileManager.App/MainForm.Connection.cs src/FileManager.App/MainForm.Browsing.cs
git commit -m "Connect to the console and browse it through tree, list and address bar"
```

Fahrprobe-Punkte (Task 22): „Konsole wählen …" fragt IP/Port, das Ribbon zeigt danach „10.10.10.236:2121"; „Verbinden" öffnet den letzten Ordner bzw. `/data`; Schnellzugriff zeigt GoldHEN, Plugins, Cheats, PKG-Ablage, Spiele, Spielstände (mit Benutzernamen), USB nur wenn belegt; „PS4" klappt `/` auf („Wird geladen…", dann Ordner); Auswahl im Baum navigiert; Ordner oben, Sortierung per Spaltenkopf (Größe numerisch, Datum als Datum); Doppelklick/Enter öffnet Ordner; Adressleiste: Segmentklick, Strg+L, Enter mit Pfad, falscher Pfad → „Fehler: Nicht vorhanden oder kein Zugriff"; ←→↑ und Alt+←/→/↑, Rücktaste; F5; Filter grenzt den Ordner ein; Tippen springt zum Namen; Konsole aus → Status „Fehler: Keine Verbindung zur PS4 — läuft der FTP-Server von GoldHEN?"; „Trennen" leert alles.

---

### Task 16: Spielnamen und Spielsymbole

**Files:**
- Create: `src/FileManager.App/MainForm.Titles.cs`

**Interfaces:**
- Consumes: `TitleCatalog.TryGet/GetAsync` (über `_session.Titles`), `TitleInfo`, `Ps4.Core.Titles.TitleId`, `FolderRow.TitleId/TitleName` (Task 8), `FolderShown`, `DecorateFolderNode` (Task 15), `_titleIcons`, `IconFor` (Task 13).
- Produces: `WireTitles()`, `DecorateFolderNode(TreeNode)`, `Task<TitleInfo> LoadTitleAsync(string titleId, CancellationToken ct)` (null bei Abbruch/Fehler — der Name bleibt dann die ID). Spielordner in Liste, Kacheln und Baum zeigen nach dem Laden Spielnamen und Spiel-Icon (Spec „Titelnamen"); die Title-ID steht in ihrer Spalte. Navigieren weg bricht offene Auflösungen ab (Navigations-Token). Die Sortierung wird dafür nicht neu angewandt (Plan-Entscheidung 10).

- [ ] **Step 1: Implementieren**

`src/FileManager.App/MainForm.Titles.cs`:

```csharp
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using Ps4.Core.Titles;
using TreeNode = UIFramework.Tree.TreeNode;

namespace FileManager.App
{
    internal sealed partial class MainForm
    {
        partial void WireTitles()
        {
            FolderShown += () =>
            {
                var ct = _navCts != null ? _navCts.Token : CancellationToken.None;
                foreach (var row in _rows.Where(r => r.IsTitleFolder).ToList())
                {
                    var _ = ApplyTitleAsync(row, ct);
                }
            };
        }

        partial void DecorateFolderNode(TreeNode node)
        {
            var name = RemotePath.Name(node.Tag as string ?? "");
            if (!TitleId.IsMatch(name)) return;
            var _ = DecorateNodeAsync(node, name);
        }

        private async Task ApplyTitleAsync(FolderRow row, CancellationToken ct)
        {
            var info = await LoadTitleAsync(row.TitleId, ct);
            if (info == null || !_rows.Contains(row)) return;      // inzwischen weg navigiert

            row.TitleName = info.HasRealName ? info.Name : null;
            _grid.Invalidate();
            var item = _gallery.Gallery.Groups.SelectMany(g => g.Items).FirstOrDefault(i => ReferenceEquals(i.Tag, row));
            if (item != null)
            {
                item.Caption = row.ShownName;
                item.Image = IconFor(row, true);
            }
        }

        private async Task DecorateNodeAsync(TreeNode node, string titleId)
        {
            var info = await LoadTitleAsync(titleId, CancellationToken.None);
            if (info == null) return;
            if (info.HasRealName) node.Text = info.Name;
            Image icon;
            if (_titleIcons.TryGetValue(titleId, out icon)) node.Image = icon;
        }

        /// <summary>
        /// Titel über den Katalog der Sitzung (Browse-Tor, höchstens zwei
        /// gleichzeitig; Ergebnisse für die Sitzung gemerkt). Das Icon wird einmal
        /// je Spiel dekodiert und gehört _titleIcons.
        /// </summary>
        private async Task<TitleInfo> LoadTitleAsync(string titleId, CancellationToken ct)
        {
            var session = _session;
            if (session == null) return null;

            TitleInfo info;
            if (!session.Titles.TryGet(titleId, out info))
            {
                try
                {
                    info = await session.Titles.GetAsync(titleId, ct);
                }
                catch (Exception)
                {
                    return null;                                   // Abbruch oder Verbindung — der Name bleibt die ID
                }
                if (session != _session) return null;
            }

            if (info.IconPng != null && !_titleIcons.ContainsKey(titleId))
            {
                var image = Decode(info.IconPng);
                if (image != null) _titleIcons[titleId] = image;
            }
            return info;
        }

        private static Image Decode(byte[] png)
        {
            try
            {
                using (var stream = new MemoryStream(png))
                using (var decoded = Image.FromStream(stream))
                {
                    return new Bitmap(decoded);
                }
            }
            catch (Exception)
            {
                return null;                                       // kaputtes Icon — Ordnersymbol bleibt
            }
        }
    }
}
```

- [ ] **Step 2: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5` — `0 Warnung(en)`, `0 Fehler`.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 3: Commit**

```bash
git add src/FileManager.App/MainForm.Titles.cs
git commit -m "Show game names and icons for title folders in list, tiles and tree"
```

Fahrprobe-Punkte (Task 22): In „Spiele (erweiterter Speicher)" erscheinen nach kurzer Zeit Spielnamen statt `CUSA…`, die Title-ID steht in ihrer Spalte; Kacheln zeigen die Spiel-Icons; im Baum tragen Spielordner Namen und Icon; schnelles Wegnavigieren lässt keine Fehlermeldung zurück.

---

### Task 17: Übertragungsleiste

**Files:**
- Create: `src/FileManager.App/Views/TransferPanel.cs`
- Create: `src/FileManager.App/MainForm.Transfers.cs`

**Interfaces:**
- Consumes: `TransferQueue.Jobs/JobChanged/CancelAsync/Resume/ClearFinished`, `TransferJob`, `TransferText` (Task 11), `ExplorerSession.ApplyFinished` (Task 10), `RefreshAsync(false)` (Task 15), `SessionStarted`/`SessionEnding`/`CollectingCloseWarnings`.
- Produces:
  - `TransferPanel : SkinPanel` — `Toggle`, `CancelJob`, `ResumeJob`, `ClearList`, `ShowFailures : SkinButton`, `Summary : SkinLabel`, `Grid : GridControl`, `bool Expanded { get; set; }`, `void ShowJobs(IReadOnlyList<TransferJob> jobs)`, `TransferJob SelectedJob`.
  - In `MainForm`: Ereignis `JobFinished : Action<TransferJob>` (einmal je fertigem Auftrag, auf dem UI-Thread, **nach** der Cache-Invalidierung) — Task 21 hängt sich an.
  - Verhalten: `JobChanged` (Arbeits-Thread) wird per `BeginInvoke` auf den UI-Thread gebracht (Übergabe Task 15); fertige Aufträge invalidieren den Cache und lesen den offenen Ordner neu, wenn er betroffen ist; fertige Aufträge bleiben bis „Liste leeren" stehen (Spec); ein neuer Auftrag klappt die Leiste auf; beim Trennen/Beenden werden laufende Aufträge abgebrochen (mit Rückfrage über `CollectingCloseWarnings`).

- [ ] **Step 1: `TransferPanel`**

`src/FileManager.App/Views/TransferPanel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using UIFramework.Controls;
using UIFramework.Core.Dpi;
using UIFramework.Grid;

namespace FileManager.App.Views
{
    /// <summary>
    /// Übertragungsleiste (Spec): Aufträge mit Status, Fortschritt als
    /// Textbalken, Rate, Restzeit; Abbrechen/Fortsetzen; einklappbar.
    /// </summary>
    internal sealed class TransferPanel : SkinPanel
    {
        private const int GridHeightLogical = 150;

        private readonly List<TransferJob> _jobs = new List<TransferJob>();
        private readonly ListDataSource<TransferJob> _source;
        private readonly SkinPanel _header = new SkinPanel { Dock = DockStyle.Top };
        private bool _expanded;

        public TransferPanel()
        {
            Dock = DockStyle.Bottom;
            Toggle = new SkinButton { Text = "▸ Übertragungen", Dock = DockStyle.Left };
            Summary = new SkinLabel { Dock = DockStyle.Fill, AutoSize = false, Text = "keine laufenden" };
            CancelJob = new SkinButton { Text = "Abbrechen", Dock = DockStyle.Right };
            ResumeJob = new SkinButton { Text = "Fortsetzen", Dock = DockStyle.Right };
            ShowFailures = new SkinButton { Text = "Fehler …", Dock = DockStyle.Right };
            ClearList = new SkinButton { Text = "Liste leeren", Dock = DockStyle.Right };
            Grid = new GridControl { Dock = DockStyle.Fill, Visible = false };

            Grid.Columns.Add(new GridColumn("Auftrag", "Auftrag") { Width = 340, ReadOnly = true });
            Grid.Columns.Add(new GridColumn("Status", "Status") { Width = 220, ReadOnly = true });
            Grid.Columns.Add(new GridColumn("Fortschritt", "Fortschritt") { Width = 240, ReadOnly = true });
            Grid.Columns.Add(new GridColumn("Rate", "Rate") { Width = 90, ReadOnly = true });
            Grid.Columns.Add(new GridColumn("Restzeit", "Restzeit") { Width = 100, ReadOnly = true });

            _source = new ListDataSource<TransferJob>(_jobs);
            _source.Map("Auftrag", j => j.Title);
            _source.Map("Status", j => TransferText.State(j));
            _source.Map("Fortschritt", j => TransferText.Progress(j));
            _source.Map("Rate", j => TransferText.Rate(j));
            _source.Map("Restzeit", j => TransferText.Remaining(j));
            _source.Map("Job", j => j);
            Grid.DataSource = _source;

            // Zuletzt Hinzugefügtes dockt zuerst: Umschalter links, Knöpfe rechts, Text dazwischen.
            _header.Controls.Add(Summary);
            _header.Controls.Add(ClearList);
            _header.Controls.Add(ShowFailures);
            _header.Controls.Add(ResumeJob);
            _header.Controls.Add(CancelJob);
            _header.Controls.Add(Toggle);
            Controls.Add(Grid);
            Controls.Add(_header);

            Toggle.Click += (s, e) => Expanded = !Expanded;
            Relayout();
        }

        public SkinButton Toggle { get; }
        public SkinButton CancelJob { get; }
        public SkinButton ResumeJob { get; }
        public SkinButton ShowFailures { get; }
        public SkinButton ClearList { get; }
        public SkinLabel Summary { get; }
        public GridControl Grid { get; }

        public bool Expanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Grid.Visible = value;
                Toggle.Text = (value ? "▾" : "▸") + " Übertragungen";
                Relayout();
            }
        }

        public TransferJob SelectedJob
        {
            get
            {
                int row = Grid.Selection.CurrentRow;
                return row >= 0 && row < _source.RowCount ? (TransferJob)_source.GetValue(row, "Job") : null;
            }
        }

        /// <summary>
        /// Neu binden nur, wenn sich die Anzahl ändert — sonst genügt Neuzeichnen,
        /// und die Auswahl (fürs Abbrechen/Fortsetzen) bleibt stehen.
        /// </summary>
        public void ShowJobs(IReadOnlyList<TransferJob> jobs)
        {
            bool sameCount = jobs.Count == _jobs.Count;
            _jobs.Clear();
            _jobs.AddRange(jobs);
            if (sameCount)
            {
                Grid.Invalidate();
            }
            else
            {
                Grid.DataSource = null;
                Grid.DataSource = _source;
            }
            Summary.Text = TransferText.Summary(jobs);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Relayout();
        }

        private void Relayout()
        {
            int headerChrome = _header.Height - _header.DisplayRectangle.Height;
            _header.Height = Toggle.GetPreferredSize(Size.Empty).Height + headerChrome;
            int chrome = Height - DisplayRectangle.Height;
            Height = _header.Height + (_expanded ? DpiScale.Scale(GridHeightLogical, DeviceDpi) : 0) + chrome;
        }
    }
}
```

- [ ] **Step 2: Anbindung**

`src/FileManager.App/MainForm.Transfers.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.App.Views;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace FileManager.App
{
    internal sealed partial class MainForm
    {
        private readonly TransferPanel _transfers = new TransferPanel();
        private readonly HashSet<TransferJob> _finishedSeen = new HashSet<TransferJob>();

        /// <summary>Einmal je fertigem Auftrag, auf dem UI-Thread, nach der Cache-Invalidierung.</summary>
        private event Action<TransferJob> JobFinished;

        partial void AddTransferPanel()
        {
            Controls.Add(_transfers);
        }

        partial void WireTransfers()
        {
            SessionStarted += session =>
            {
                session.Queue.JobChanged += OnJobChanged;
                ShowJobs();
            };
            SessionEnding += session =>
            {
                session.Queue.JobChanged -= OnJobChanged;
                foreach (var job in session.Queue.Jobs.Where(j => !j.IsFinished))
                {
                    var _ = session.Queue.CancelAsync(job);
                }
                _finishedSeen.Clear();
                _transfers.ShowJobs(new TransferJob[0]);
            };
            CollectingCloseWarnings += warnings =>
            {
                if (_session == null) return;
                int active = _session.Queue.Jobs.Count(j => j.State == JobState.Running || j.State == JobState.Waiting);
                if (active == 1) warnings.Add("1 Übertragung läuft noch und wird abgebrochen.");
                else if (active > 1) warnings.Add(active + " Übertragungen laufen noch und werden abgebrochen.");
            };

            _transfers.CancelJob.Click += async (s, e) =>
            {
                var job = SelectedOrRunningJob();
                if (job != null && _session != null) await _session.Queue.CancelAsync(job);
            };
            _transfers.ResumeJob.Click += (s, e) =>
            {
                var job = _transfers.SelectedJob;
                if (job != null && _session != null && job.State == JobState.Interrupted) _session.Queue.Resume(job);
            };
            _transfers.ClearList.Click += (s, e) =>
            {
                if (_session == null) return;
                _session.Queue.ClearFinished();
                ShowJobs();
            };
            _transfers.ShowFailures.Click += (s, e) => ShowFailuresOf(_transfers.SelectedJob);
            _transfers.Grid.Selection.Changed += (s, e) => UpdateTransferButtons();
            UpdateTransferButtons();
        }

        /// <summary>Arbeits-Thread der Warteschlange → UI-Thread (Übergabe Task 15).</summary>
        private void OnJobChanged(object sender, TransferJob job)
        {
            if (IsDisposed || !IsHandleCreated) return;
            var queue = (TransferQueue)sender;
            try
            {
                BeginInvoke((Action)(() => OnJobChangedOnUi(queue, job)));
            }
            catch (InvalidOperationException)
            {
                // Fenster schließt gerade — nichts mehr anzuzeigen.
            }
        }

        private void OnJobChangedOnUi(TransferQueue queue, TransferJob job)
        {
            var session = _session;
            if (session == null || session.Queue != queue) return;

            if (!_transfers.Expanded && job.State == JobState.Waiting) _transfers.Expanded = true;
            ShowJobs();

            if (!job.IsFinished || !_finishedSeen.Add(job)) return;

            var folders = session.ApplyFinished(job);
            ReloadTreeFolders(folders);
            if (_currentPath != null && folders.Any(f => RemotePath.Canonical(f) == RemotePath.Canonical(_currentPath)))
            {
                var _ = RefreshAsync(false);
            }
            if (job.State == JobState.CompletedWithErrors) SetStatus(job.Title + ": " + job.Summary);

            var finished = JobFinished;
            if (finished != null) finished(job);
        }

        private void ShowJobs()
        {
            _transfers.ShowJobs(_session != null ? _session.Queue.Jobs : (IReadOnlyList<TransferJob>)new TransferJob[0]);
            UpdateTransferButtons();
        }

        private TransferJob SelectedOrRunningJob()
        {
            var selected = _transfers.SelectedJob;
            if (selected != null && !selected.IsFinished) return selected;
            return _session == null ? null : _session.Queue.Jobs.FirstOrDefault(j => j.State == JobState.Running);
        }

        private void UpdateTransferButtons()
        {
            var job = _transfers.SelectedJob;
            _transfers.CancelJob.Enabled = SelectedOrRunningJob() != null;
            _transfers.ResumeJob.Enabled = job != null && job.State == JobState.Interrupted;
            _transfers.ShowFailures.Enabled = job != null && job.Failures.Count > 0;
        }

        /// <summary>Bilanz mit Grund je Datei (Spec „Permanenter Fehler … Bilanz").</summary>
        private void ShowFailuresOf(TransferJob job)
        {
            if (job == null || job.Failures.Count == 0) return;
            var lines = job.Failures.Take(20)
                .Select(f => RemotePath.Name(f.RemotePath ?? f.LocalPath ?? "") + " — " + f.Error)
                .ToList();
            if (job.Failures.Count > 20) lines.Add("… und " + (job.Failures.Count - 20) + " weitere");
            MessageDialog.Info(this, job.Title, job.Summary + "\n\n" + string.Join("\n", lines));
        }
    }
}
```

(Hinweis: `RemotePath.Name` auf einem lokalen Pfad liefert den Teil nach dem letzten `/` — für lokale Pfade in Fehlerlisten genügt das nicht immer; die Zeile zeigt dann den ganzen lokalen Pfad, was ebenfalls verständlich ist.)

- [ ] **Step 3: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5` — `0 Warnung(en)`, `0 Fehler`.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 4: Commit**

```bash
git add src/FileManager.App/Views/TransferPanel.cs src/FileManager.App/MainForm.Transfers.cs
git commit -m "Show the transfer queue with progress bars, cancel, resume and failure details"
```

Fahrprobe-Punkte (Task 22): Ein Auftrag klappt die Leiste auf; Fortschritt `███████░░░ 78 %`, Rate „6,9 MB/s", Restzeit „noch 0:42"; Kopfzeile fasst den laufenden Auftrag zusammen; „Abbrechen" verwirft die halbe Datei; Konsole mitten im Transfer aus → „Unterbrochen: …", „Fortsetzen" setzt fort; „Fehler …" listet fehlgeschlagene Dateien; „Liste leeren" lässt Unterbrochene stehen; nach Upload/Löschen zeigt der offene Ordner sofort den neuen Stand.

---

### Task 18: Neuer Ordner, Umbenennen, Löschen, Hoch- und Herunterladen

**Files:**
- Create: `src/FileManager.App/MainForm.Operations.cs`

**Interfaces:**
- Consumes: `FileOperations.PlanCreateFolderAsync/PlanRenameAsync/PlanDeleteAsync/PlanUploadAsync/PlanDownloadAsync/PlanMoveAsync` (über `_session.Operations`), `TransferQueue.Enqueue`, `PastePlanner.IsUsefulMove` (Task 12), `_commands` (Task 13), `RenameDialog`/`MessageDialog` (Task 14), `AddShortcut` (Task 15).
- Produces:
  - `Task RunPlanAsync(string busyText, Func<ExplorerSession, Task<TransferJob>> plan)` — Phase „wird ermittelt …" (Spec), `null` = abgelehnt, Fehler über `ShowError`; nur, wenn die Sitzung inzwischen nicht gewechselt hat.
  - `void UploadInto(IReadOnlyList<string> localPaths, string folder)`, `void MoveInto(IReadOnlyList<RemoteItem> items, string folder)` — für Ziehen/Einfügen (Task 19) und Menüs (Task 20).
  - `void NewFolder()`, `void RenameSelection()`, `void DeleteSelection()`, `void UploadFiles()`, `void UploadFolder()`, `void DownloadSelection()`, `bool ListHasFocus()` (Task 19); Kürzel Strg+Umschalt+N, Entf, F2 (beide Ansichten über `RenameSelection`, damit `CanRename` und die Spielordner-Regel auch in Details gelten).
  - Grid-Umbenennen: `MapSet("Name", …)` stößt die Umbenennung **verzögert** per `BeginInvoke` an — ein Dialog mitten im `CommitEdit` des Grids würde über den Fokusverlust des Editors ein zweites Commit auslösen. Bei Fehler bleibt der alte Name stehen (die Zeile ändert sich erst mit dem Neulesen).

- [ ] **Step 1: Implementieren**

`src/FileManager.App/MainForm.Operations.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace FileManager.App
{
    internal sealed partial class MainForm
    {
        private const string TitleFolderText = "Spielordner behalten ihre Title-ID — die PS4 findet ein Spiel nur unter diesem Namen.";

        private string _lastLocalFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        partial void WireOperations()
        {
            // Nicht direkt aus SetValue heraus: ein Dialog mitten im CommitEdit
            // löste über den Fokusverlust des Editors ein zweites Commit aus.
            _list.MapSet("Name", (row, value) => BeginInvoke((Action)(() => RenameRow(row, value as string))));

            _newFolderItem.Click += (s, e) => NewFolder();
            _renameItem.Click += (s, e) => RenameSelection();
            _deleteItem.Click += (s, e) => DeleteSelection();
            _uploadItem.Click += (s, e) => UploadFiles();
            _uploadFolderItem.Click += (s, e) => UploadFolder();
            _downloadItem.Click += (s, e) => DownloadSelection();

            AddShortcut(Keys.Control | Keys.Shift | Keys.N, () => { NewFolder(); return true; }, false);
            AddShortcut(Keys.Delete, () =>
            {
                if (!ListHasFocus()) return false;
                DeleteSelection();
                return true;
            }, true);
            AddShortcut(Keys.F2, () =>
            {
                // Beide Ansichten über RenameSelection: prüft CanRename und Spielordner, dann Zelle
                // bzw. Dialog. ProcessCmdKey läuft vor dem OnKeyDown des Grids.
                if (!ListHasFocus()) return false;
                RenameSelection();
                return true;
            }, true);
        }

        private bool ListHasFocus()
        {
            return IsTilesView ? _gallery.ContainsFocus : _grid.ContainsFocus;
        }

        /// <summary>
        /// Plant einen Auftrag (Phase „wird ermittelt …", Spec) und reiht ihn ein.
        /// null heißt: der Anwender hat eine Rückfrage abgelehnt.
        /// </summary>
        private async Task RunPlanAsync(string busyText, Func<ExplorerSession, Task<TransferJob>> plan)
        {
            var session = _session;
            if (session == null) return;
            SetStatus(busyText + " — wird ermittelt …");
            try
            {
                var job = await plan(session);
                if (session != _session) return;
                if (job == null)
                {
                    SetStatus("Abgebrochen.");
                    return;
                }
                session.Queue.Enqueue(job);
                SetStatus(job.Title + " — eingereiht.");
            }
            catch (Exception ex)
            {
                if (session == _session) ShowError(ex);
            }
        }

        private void UploadInto(IReadOnlyList<string> localPaths, string folder)
        {
            if (localPaths == null || localPaths.Count == 0 || folder == null) return;
            // Auch Ziele aus dem Schnellzugriff laufen durch PathResolver (Plan-Entscheidung 5).
            var _ = RunPlanAsync("Hochladen", async s => await s.Operations.PlanUploadAsync(localPaths,
                await PathResolver.ResolveAsync(s.Cache, folder, CancellationToken.None), CancellationToken.None));
        }

        private void MoveInto(IReadOnlyList<RemoteItem> items, string folder)
        {
            if (items == null || items.Count == 0 || folder == null) return;
            if (!PastePlanner.IsUsefulMove(items, folder)) return;
            var _ = RunPlanAsync("Verschieben", async s => await s.Operations.PlanMoveAsync(items,
                await PathResolver.ResolveAsync(s.Cache, folder, CancellationToken.None), CancellationToken.None));
        }

        private void NewFolder()
        {
            if (!_commands.CanNewFolder) return;
            var folder = _currentPath;
            var _ = RunPlanAsync("Neuer Ordner", s => s.Operations.PlanCreateFolderAsync(folder, CancellationToken.None));
        }

        private void RenameSelection()
        {
            var selection = CurrentSelection();
            if (selection.Count == 1 && selection[0].IsTitleFolder)
            {
                MessageDialog.Info(this, "Umbenennen", TitleFolderText);
                return;
            }
            if (!_commands.CanRename || selection.Count != 1) return;

            if (!IsTilesView)
            {
                int nameColumn = _grid.Columns.IndexOf(_grid.Columns.First(c => c.Key == "Name"));   // Spalten lassen sich umordnen
                _grid.BeginEdit(_grid.Selection.CurrentRow, nameColumn);
                return;
            }
            using (var dialog = new RenameDialog(selection[0].DisplayName))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) RenameRow(selection[0], dialog.NewName);
            }
        }

        private void RenameRow(FolderRow row, string newName)
        {
            if (row == null || newName == null) return;
            if (row.IsTitleFolder)
            {
                MessageDialog.Info(this, "Umbenennen", TitleFolderText);
                return;
            }
            var name = newName.Trim();
            if (name == row.DisplayName) return;
            var item = row.Item;
            var _ = RunPlanAsync("Umbenennen", s => s.Operations.PlanRenameAsync(item, name, CancellationToken.None));
        }

        private void DeleteSelection()
        {
            var items = CurrentSelection().Select(r => r.Item).ToList();
            if (!_commands.CanDelete || items.Count == 0) return;
            var _ = RunPlanAsync("Löschen", s => s.Operations.PlanDeleteAsync(items, CancellationToken.None));
        }

        private void UploadFiles()
        {
            if (!_commands.CanUpload) return;
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "Dateien auf die PS4 hochladen", InitialDirectory = _lastLocalFolder })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _lastLocalFolder = Path.GetDirectoryName(dialog.FileNames[0]);
                UploadInto(dialog.FileNames.ToList(), _currentPath);
            }
        }

        private void UploadFolder()
        {
            if (!_commands.CanUpload) return;
            using (var dialog = new FolderBrowserDialog { Description = "Ordner auf die PS4 hochladen", SelectedPath = _lastLocalFolder })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _lastLocalFolder = dialog.SelectedPath;
                UploadInto(new[] { dialog.SelectedPath }, _currentPath);
            }
        }

        private void DownloadSelection()
        {
            var items = CurrentSelection().Where(r => !r.IsLink).Select(r => r.Item).ToList();
            if (!_commands.CanDownload || items.Count == 0) return;
            using (var dialog = new FolderBrowserDialog { Description = "Herunterladen nach …", SelectedPath = _lastLocalFolder })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _lastLocalFolder = dialog.SelectedPath;
                var target = dialog.SelectedPath;
                var _ = RunPlanAsync("Herunterladen", s => s.Operations.PlanDownloadAsync(items, target, CancellationToken.None));
            }
        }
    }
}
```

- [ ] **Step 2: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5` — `0 Warnung(en)`, `0 Fehler`.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 3: Commit**

```bash
git add src/FileManager.App/MainForm.Operations.cs
git commit -m "Create folders, rename, delete, upload and download from the ribbon and keyboard"
```

Fahrprobe-Punkte (Task 22, **nur im Wegwerf-Ordner unter `/data`**): „Neuer Ordner" (auch Strg+Umschalt+N) legt „Neuer Ordner", dann „Neuer Ordner (2)" an; F2 in Details benennt in der Zelle um, in Kacheln per Dialog; ein ungültiger Name (`a/b`) meldet sich, der alte Name bleibt; ein Spielordner lässt sich nicht umbenennen (Meldung); Entf fragt „Endgültig löschen: …" mit Anzahl und Größe; Hochladen von Dateien und eines Ordners (mit Unterordnern); Herunterladen in einen PC-Ordner, Konflikt am PC fragt „Ersetzen/Überspringen/Beide behalten"; in `/` oder `/system` sind die Schreibbefehle ausgegraut; unter `/user/home` fragt die Warnzone einmal pro Auftrag; Expertenmodus hebt „gesperrt" zu „Warnung" auf.

---

### Task 19: Ziehen, Ablegen, Zwischenablage

**Files:**
- Create: `src/FileManager.App/MainForm.Clipboard.cs`

**Interfaces:**
- Consumes: `ShellPublisher` (Task 6), `ShellInterop.DropEffectCopy/DropEffectMove`, `VirtualFileDataObject`, `PastePlanner`/`ClipMode`/`PasteAction`/`DropAction` (Task 12), `FolderBrowser.ContentsZone` (Task 9), `FileOperations.PlanCopyWithinPs4`, `UploadInto`/`MoveInto`/`ListHasFocus` (Task 18), `AddShortcut` (Task 15); UIFramework `AllowItemDrag`, `ItemDrag`, `GetDropTarget`, `DropHighlight` an Grid, Kacheln und Baum.
- Produces:
  - Ein `ShellPublisher` für die Lebensdauer des Fensters (beim Schließen entsorgt: gibt die Zwischenablage frei, wenn sie noch uns gehört — Übergabe „OleSetClipboard(null) beim Beenden").
  - Hinaus: `ItemDrag` aus Grid, Kacheln und Baum → `ShellPublisher.DoDragDrop` (erlaubt Kopieren und Verschieben; Explorer kopiert, weil `Preferred DropEffect` = Kopieren). Links werden nicht gezogen.
  - Hinein: `CF_HDROP` aus Explorer auf Liste, Ordnerzeile/-kachel oder Baumknoten → Hochladen in den Zielordner; eigenes Ziehen auf einen Ordner → Verschieben. Gesperrte Ziele nehmen nichts an; Hervorhebung nur auf Ordnern.
  - `Task CopyAsync(ClipMode mode)` (Strg+C/Strg+X), `Task PasteAsync()` (Strg+V), `void SelectAll()` (Strg+A) — für Task 20.
  - Strg+V: gehört die Zwischenablage noch unserem Inhalt, bedeutet Ausgeschnittenes Verschieben und Kopiertes die Meldung `FileOperations.CopyWithinPs4Message` (über `PlanCopyWithinPs4`); sonst Dateien aus Explorer → Hochladen (Spec).

- [ ] **Step 1: Implementieren**

`src/FileManager.App/MainForm.Clipboard.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.Core.Presentation;
using FileManager.Core.Shell;
using FileManager.Core.Transfers;

namespace FileManager.App
{
    internal sealed partial class MainForm
    {
        private readonly ShellPublisher _publisher = new ShellPublisher();
        private VirtualFileDataObject _clipData;
        private List<RemoteItem> _clipItems;
        private ClipMode _clipMode = ClipMode.None;
        private List<RemoteItem> _internalDrag;

        partial void WireClipboard()
        {
            FormClosed += (s, e) => _publisher.Dispose();
            SessionEnding += session =>
            {
                _clipMode = ClipMode.None;
                _clipItems = null;
                _clipData = null;
            };

            _grid.AllowItemDrag = true;
            _grid.AllowDrop = true;
            _grid.ItemDrag += (s, e) => StartDrag(e.Rows.Select(RowAt).Where(r => r != null).ToList());
            _grid.DragEnter += OnGridDrag;
            _grid.DragOver += OnGridDrag;
            _grid.DragLeave += (s, e) => _grid.DropHighlight = -1;
            _grid.DragDrop += (s, e) =>
            {
                var folder = PastePlanner.DropFolder(RowAt(_grid.GetDropTarget(_grid.PointToClient(new Point(e.X, e.Y)))), _currentPath);
                _grid.DropHighlight = -1;
                Drop(e, folder);
            };

            _gallery.AllowItemDrag = true;
            _gallery.AllowDrop = true;
            _gallery.ItemDrag += (s, e) => StartDrag(e.Items.Select(i => (FolderRow)i.Tag).ToList());
            _gallery.DragEnter += OnGalleryDrag;
            _gallery.DragOver += OnGalleryDrag;
            _gallery.DragLeave += (s, e) => _gallery.DropHighlight = null;
            _gallery.DragDrop += (s, e) =>
            {
                var item = _gallery.GetDropTarget(_gallery.PointToClient(new Point(e.X, e.Y)));
                var folder = PastePlanner.DropFolder(item == null ? null : (FolderRow)item.Tag, _currentPath);
                _gallery.DropHighlight = null;
                Drop(e, folder);
            };

            _tree.AllowItemDrag = true;
            _tree.AllowDrop = true;
            _tree.ItemDrag += (s, e) =>
            {
                var path = e.Node.Tag as string;
                if (path == null || path == "/" || _session == null) return;
                StartDrag(_session, new List<RemoteItem> { new RemoteItem(path, true, 0, DateTime.MinValue) });
            };
            _tree.DragEnter += OnTreeDrag;
            _tree.DragOver += OnTreeDrag;
            _tree.DragLeave += (s, e) => _tree.DropHighlight = null;
            _tree.DragDrop += (s, e) =>
            {
                var node = _tree.GetDropTarget(_tree.PointToClient(new Point(e.X, e.Y)));
                _tree.DropHighlight = null;
                Drop(e, node == null ? null : node.Tag as string);
            };

            AddShortcut(Keys.Control | Keys.C, () => { if (!ListHasFocus()) return false; var _ = CopyAsync(ClipMode.Copy); return true; }, true);
            AddShortcut(Keys.Control | Keys.X, () => { if (!ListHasFocus()) return false; var _ = CopyAsync(ClipMode.Cut); return true; }, true);
            AddShortcut(Keys.Control | Keys.V, () => { if (!ListHasFocus() && !_tree.ContainsFocus) return false; var _ = PasteAsync(); return true; }, true);
            AddShortcut(Keys.Control | Keys.A, () => { if (!ListHasFocus()) return false; SelectAll(); return true; }, true);
        }

        // ---- Hinaus --------------------------------------------------------------------------

        private void StartDrag(IReadOnlyList<FolderRow> rows)
        {
            var session = _session;
            var items = rows.Where(r => !r.IsLink).Select(r => r.Item).ToList();
            if (session == null || items.Count == 0) return;
            StartDrag(session, items);
        }

        /// <summary>
        /// Virtuelle Dateien über den Shell-Thread (Übergabe Plan 1): Explorers
        /// Rückrufe laufen dort, nicht auf dem UI-Thread. _internalDrag markiert
        /// das eigene Ziehen für die eigenen Ablageziele.
        /// </summary>
        private void StartDrag(ExplorerSession session, List<RemoteItem> items)
        {
            _internalDrag = items;
            try
            {
                var data = _publisher.Create(session.Ftp, items);
                _publisher.DoDragDrop(data, ShellInterop.DropEffectCopy | ShellInterop.DropEffectMove);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                _internalDrag = null;
            }
        }

        // ---- Hinein --------------------------------------------------------------------------

        private DropAction DropActionFor(DragEventArgs e, string folder)
        {
            if (_session == null || folder == null) return DropAction.None;
            var zone = FolderBrowser.ContentsZone(_session.Policy, folder);
            var action = PastePlanner.ForDrop(_internalDrag != null, e.Data.GetDataPresent(DataFormats.FileDrop), zone);
            if (action == DropAction.Move && !PastePlanner.IsUsefulMove(_internalDrag, folder)) return DropAction.None;
            return action;
        }

        private static DragDropEffects EffectFor(DropAction action)
        {
            if (action == DropAction.Move) return DragDropEffects.Move;
            if (action == DropAction.Upload) return DragDropEffects.Copy;
            return DragDropEffects.None;
        }

        private void OnGridDrag(object sender, DragEventArgs e)
        {
            int row = _grid.GetDropTarget(_grid.PointToClient(new Point(e.X, e.Y)));
            var target = RowAt(row);
            var action = DropActionFor(e, PastePlanner.DropFolder(target, _currentPath));
            e.Effect = EffectFor(action);
            _grid.DropHighlight = action != DropAction.None && target != null && target.IsFolder ? row : -1;
        }

        private void OnGalleryDrag(object sender, DragEventArgs e)
        {
            var item = _gallery.GetDropTarget(_gallery.PointToClient(new Point(e.X, e.Y)));
            var target = item == null ? null : (FolderRow)item.Tag;
            var action = DropActionFor(e, PastePlanner.DropFolder(target, _currentPath));
            e.Effect = EffectFor(action);
            _gallery.DropHighlight = action != DropAction.None && target != null && target.IsFolder ? item : null;
        }

        private void OnTreeDrag(object sender, DragEventArgs e)
        {
            var node = _tree.GetDropTarget(_tree.PointToClient(new Point(e.X, e.Y)));
            var action = DropActionFor(e, node == null ? null : node.Tag as string);
            e.Effect = EffectFor(action);
            _tree.DropHighlight = action != DropAction.None ? node : null;
        }

        private void Drop(DragEventArgs e, string folder)
        {
            var action = DropActionFor(e, folder);
            if (action == DropAction.Upload) UploadInto((string[])e.Data.GetData(DataFormats.FileDrop), folder);
            else if (action == DropAction.Move) MoveInto(_internalDrag.ToList(), folder);
        }

        // ---- Zwischenablage ------------------------------------------------------------------

        private async Task CopyAsync(ClipMode mode)
        {
            var session = _session;
            var items = CurrentSelection().Where(r => !r.IsLink).Select(r => r.Item).ToList();
            if (session == null || items.Count == 0) return;
            if (mode == ClipMode.Cut && !_commands.CanCut) return;
            try
            {
                var data = _publisher.Create(session.Ftp, items);
                await _publisher.SetClipboardAsync(data);
                _clipData = data;
                _clipItems = items;
                _clipMode = mode;
                SetStatus(StatusText.Items(items.Count) + (mode == ClipMode.Cut
                    ? " ausgeschnitten — mit Strg+V in einen anderen Ordner verschieben."
                    : " kopiert — in Explorer mit Strg+V einfügen."));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async Task PasteAsync()
        {
            var session = _session;
            var folder = _currentPath;
            if (session == null || folder == null || !_commands.CanPaste) return;

            bool ours = _clipData != null && await _publisher.OwnsClipboardAsync(_clipData);
            bool files = !ours && Clipboard.ContainsFileDropList();
            switch (PastePlanner.Decide(_clipMode, ours, files))
            {
                case PasteAction.MoveItems:
                    MoveInto(_clipItems, folder);
                    _clipMode = ClipMode.None;               // wie im Explorer: Ausgeschnittenes wird einmal eingefügt
                    break;
                case PasteAction.RefuseCopyWithinPs4:
                    try { session.Operations.PlanCopyWithinPs4(); }
                    catch (NotSupportedException ex) { MessageDialog.Info(this, "Einfügen", ex.Message); }
                    break;
                case PasteAction.UploadFiles:
                    UploadInto(Clipboard.GetFileDropList().Cast<string>().ToList(), folder);
                    break;
                default:
                    SetStatus("Nichts zum Einfügen.");
                    break;
            }
        }

        private void SelectAll()
        {
            if (IsTilesView)
            {
                foreach (var item in _gallery.Gallery.Groups.SelectMany(g => g.Items)) item.Checked = true;
            }
            else if (_sorted.RowCount > 0)
            {
                _grid.Selection.Select(0);
                _grid.Selection.ExtendTo(_sorted.RowCount - 1);
            }
            UpdateCommands();
            UpdateStatus();
        }
    }
}
```

- [ ] **Step 2: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5` — `0 Warnung(en)`, `0 Fehler`.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 3: Commit**

```bash
git add src/FileManager.App/MainForm.Clipboard.cs
git commit -m "Drag, drop, cut, copy and paste between the console and Windows Explorer"
```

Fahrprobe-Punkte (Task 22, Schreibendes nur im Wegwerf-Ordner): Dateien und ein Ordner aus der Liste in einen Explorer-Ordner ziehen → Explorer zeigt seinen Kopierdialog, die App bleibt dabei bedienbar (Fenster verschieben, scrollen); großes PKG (> 1 GB) nach Explorer ziehen → Explorer kopiert im Hintergrund; Strg+C in der Liste, Strg+V im Explorer → dieselben Dateien; Datei aus Explorer auf eine Ordnerzeile, eine Ordnerkachel und einen Baumknoten ziehen → Upload in genau diesen Ordner; auf einen gesperrten Ordner (z. B. `/system` im Baum) → Verbotszeichen; innerhalb der App auf einen Ordner ziehen → Verschieben; Strg+X, anderer Ordner, Strg+V → verschoben; Strg+C und Strg+V in der PS4 → Meldung „Kopieren innerhalb der PS4 kommt später. Verschieben geht mit Strg+X."; Strg+A markiert alles; nach dem Beenden ist ein zuvor kopierter Zwischenablage-Inhalt weg (ohne Warten beim Beenden).

---

### Task 20: Kontextmenüs, Anheften, Pfad kopieren

**Files:**
- Create: `src/FileManager.App/MainForm.Menus.cs`

**Interfaces:**
- Consumes: `PopupMenu`/`MenuEntry` (`Show(Control owner, Point screenLocation)` erwartet einen **Bildschirm**-Punkt), `_commands` (Task 13), `Activate` (Task 15), `DownloadSelection`/`RenameSelection`/`DeleteSelection`/`NewFolder`/`UploadFiles`/`UploadFolder` (Task 18), `CopyAsync`/`PasteAsync` (Task 19), `RefreshAsync`, `NavigateTo`, `LoadQuickAccessAsync`, `TrySaveSettings` (Task 15), `GridControl.GetDropTarget`, `TreeControl.HitTest`.
- Produces: Kontextmenüs nach dem MouseUp-Muster der Gallery (Spec „Kontextmenü"):
  - auf Einträgen: Öffnen, Herunterladen nach …, Ausschneiden, Kopieren, Umbenennen, Löschen, Pfad kopieren, An Schnellzugriff anheften / Vom Schnellzugriff lösen (Ordner);
  - auf freier Fläche: Einfügen, Neuer Ordner, Hochladen …, Ordner hochladen …, Aktualisieren;
  - auf Baumknoten: Öffnen, Pfad kopieren, Anheften/Lösen.
  - Ein Rechtsklick auf eine nicht ausgewählte Grid-Zeile wählt sie vorher aus (das Grid wählt beim Rechtsdruck selbst nicht, UIFramework-Entscheidung).
  - `bool IsPinned(string path)`, `void TogglePin(string path)` (Einstellung `PinnedFolders`, Schnellzugriff neu aufbauen), `void CopyPath(string path)`.

- [ ] **Step 1: Implementieren**

`src/FileManager.App/MainForm.Menus.cs`:

```csharp
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using UIFramework.Controls;

namespace FileManager.App
{
    internal sealed partial class MainForm
    {
        private readonly PopupMenu _itemMenu = new PopupMenu();
        private readonly PopupMenu _backgroundMenu = new PopupMenu();
        private readonly PopupMenu _nodeMenu = new PopupMenu();

        partial void WireMenus()
        {
            _grid.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || _session == null) return;
                int row = _grid.GetDropTarget(e.Location);
                if (row >= 0 && !_grid.Selection.IsSelected(row)) _grid.Selection.Select(row);
                if (row >= 0) ShowItemMenu(_grid, e.Location);
                else ShowBackgroundMenu(_grid, e.Location);
            };
            _gallery.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || _session == null) return;
                // Die Gallery wählt beim Rechtsklick ein nicht angehaktes Element selbst an.
                if (_gallery.Gallery.CheckedItems.Any()) ShowItemMenu(_gallery, e.Location);
                else ShowBackgroundMenu(_gallery, e.Location);
            };
            _tree.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || _session == null) return;
                var node = _tree.HitTest(e.Location);
                var path = node == null ? null : node.Tag as string;
                if (path != null) ShowNodeMenu(path, e.Location);
            };

            // PopupMenu ist IDisposable (MenuController) — Muster Gallery-MainForm.
            FormClosed += (s, e) =>
            {
                _itemMenu.Dispose();
                _backgroundMenu.Dispose();
                _nodeMenu.Dispose();
            };
        }

        private void ShowItemMenu(Control owner, Point location)
        {
            UpdateCommands();
            var selection = CurrentSelection();
            var single = selection.Count == 1 ? selection[0] : null;

            _itemMenu.Items.Clear();
            Add(_itemMenu, "Öffnen", single != null && (single.IsFolder || single.IsLink || _commands.CanOpen), () => Activate(single));
            Add(_itemMenu, "Herunterladen nach …", _commands.CanDownload, DownloadSelection);
            _itemMenu.Items.Add(MenuEntry.Separator());
            Add(_itemMenu, "Ausschneiden", _commands.CanCut, () => { var _ = CopyAsync(ClipMode.Cut); });
            Add(_itemMenu, "Kopieren", _commands.CanCopy, () => { var _ = CopyAsync(ClipMode.Copy); });
            _itemMenu.Items.Add(MenuEntry.Separator());
            Add(_itemMenu, "Umbenennen", _commands.CanRename, RenameSelection);
            Add(_itemMenu, "Löschen", _commands.CanDelete, DeleteSelection);
            _itemMenu.Items.Add(MenuEntry.Separator());
            Add(_itemMenu, "Pfad kopieren", single != null, () => CopyPath(single.Item.Path));
            if (single != null && single.IsFolder)
            {
                var path = single.Item.Path;
                Add(_itemMenu, IsPinned(path) ? "Vom Schnellzugriff lösen" : "An Schnellzugriff anheften", true, () => TogglePin(path));
            }
            _itemMenu.Show(owner, owner.PointToScreen(location));
        }

        private void ShowBackgroundMenu(Control owner, Point location)
        {
            UpdateCommands();
            _backgroundMenu.Items.Clear();
            Add(_backgroundMenu, "Einfügen", _commands.CanPaste, () => { var _ = PasteAsync(); });
            Add(_backgroundMenu, "Neuer Ordner", _commands.CanNewFolder, NewFolder);
            _backgroundMenu.Items.Add(MenuEntry.Separator());
            Add(_backgroundMenu, "Hochladen …", _commands.CanUpload, UploadFiles);
            Add(_backgroundMenu, "Ordner hochladen …", _commands.CanUpload, UploadFolder);
            _backgroundMenu.Items.Add(MenuEntry.Separator());
            Add(_backgroundMenu, "Aktualisieren", _currentPath != null, () => { var _ = RefreshAsync(false); });
            _backgroundMenu.Show(owner, owner.PointToScreen(location));
        }

        private void ShowNodeMenu(string path, Point location)
        {
            _nodeMenu.Items.Clear();
            Add(_nodeMenu, "Öffnen", true, () => NavigateTo(path));
            Add(_nodeMenu, "Pfad kopieren", true, () => CopyPath(path));
            Add(_nodeMenu, IsPinned(path) ? "Vom Schnellzugriff lösen" : "An Schnellzugriff anheften", path != RemotePath.Root, () => TogglePin(path));
            _nodeMenu.Show(_tree, _tree.PointToScreen(location));
        }

        private static void Add(PopupMenu menu, string text, bool enabled, Action action)
        {
            var entry = new MenuEntry(text) { Enabled = enabled };
            entry.Click += (s, e) => action();
            menu.Items.Add(entry);
        }

        private bool IsPinned(string path)
        {
            return _settings.PinnedFolders.Contains(RemotePath.Normalize(path));
        }

        /// <summary>Angeheftete Ordner (Spec „Schnellzugriff … dazu angeheftete Ordner des Anwenders").</summary>
        private void TogglePin(string path)
        {
            var normalized = RemotePath.Normalize(path);
            if (!_settings.PinnedFolders.Remove(normalized)) _settings.PinnedFolders.Add(normalized);
            TrySaveSettings();
            if (_session != null)
            {
                var _ = LoadQuickAccessAsync(_session);
            }
        }

        private void CopyPath(string path)
        {
            try
            {
                Clipboard.SetText(path);
                SetStatus("Pfad kopiert: " + path);
            }
            catch (Exception ex)
            {
                ShowError(ex);                                    // Zwischenablage gerade von einem anderen Programm belegt
            }
        }
    }
}
```

- [ ] **Step 2: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5` — `0 Warnung(en)`, `0 Fehler`.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 3: Commit**

```bash
git add src/FileManager.App/MainForm.Menus.cs
git commit -m "Offer context menus, pinning to quick access and copying paths"
```

Fahrprobe-Punkte (Task 22): Rechtsklick auf eine Zeile wählt sie und zeigt das Eintragsmenü (ausgegraute Einträge passen zu Zone und Auswahl); Rechtsklick auf freie Fläche zeigt Einfügen/Neuer Ordner/Hochladen/Aktualisieren; Rechtsklick im Baum zeigt Öffnen/Pfad kopieren/Anheften; „An Schnellzugriff anheften" erscheint sofort unter „Schnellzugriff" und überlebt einen Neustart, „Vom Schnellzugriff lösen" entfernt ihn; „Pfad kopieren" legt `/data/GoldHEN/plugins` in die Zwischenablage; Menüs in beiden Skins.

---

### Task 21: Öffnen mit Rückschreiben

**Files:**
- Create: `src/FileManager.App/MainForm.Editing.cs`

**Interfaces:**
- Consumes: `EditSessionManager.NeedsSizeConfirmation/PrepareLocalPath/Register/Tick/ChangeDetected/RemoteChangedAsync/CompleteWriteBackAsync/Discard/PendingSessions` (über `_session.Editing`, Task 4), `FileOperations.PlanOpenForEdit/PlanWriteBackAsync`, `TransferQueue.Enqueue`, `FileActivated` (Task 15), `JobFinished` (Task 17), `SessionStarted`/`SessionEnding`/`CollectingCloseWarnings`, `MessageDialog` (Task 14).
- Produces (Spec „Öffnen mit Rückschreiben"):
  - Doppelklick/Enter auf eine Datei: über 200 MB erst fragen; laden als Auftrag nach `open\<zufall>\<name>`; danach mit dem Windows-Standardprogramm öffnen (`ShellExecute`, Verb „open"), ohne Zuordnung der „Öffnen mit"-Dialog (`OpenAs_RunDLL`); Grundlage (entfernter Pfad, Größe, Datum) bei `Register` gemerkt.
  - `Tick()` alle 250 ms per WinForms-Timer (UI-Thread) — `ChangeDetected` kommt damit auf dem UI-Thread an.
  - Geänderte Dateien kommen in eine Warteschlange und werden **nacheinander** gefragt („Auf die PS4 zurückschreiben?"; bei inzwischen geänderter Konsolenfassung zusätzlich „Trotzdem überschreiben?"). Ja → `PlanWriteBackAsync` (Zonenprüfung inklusive) → Auftrag (Upload über `.part` + RNTO, Task 2) → danach `CompleteWriteBackAsync`. Nein → `Discard`.
  - Beim Beenden/Trennen: nicht zurückgeschriebene Änderungen landen in `CollectingCloseWarnings`.

- [ ] **Step 1: Implementieren**

`src/FileManager.App/MainForm.Editing.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FileManager.App.Dialogs;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace FileManager.App
{
    internal sealed partial class MainForm
    {
        private const int ErrorNoAssociation = 1155;

        private sealed class PendingOpen
        {
            public RemoteItem Item;
            public string LocalPath;
        }

        private readonly Dictionary<TransferJob, PendingOpen> _openJobs = new Dictionary<TransferJob, PendingOpen>();
        private readonly Dictionary<TransferJob, EditSession> _writeBackJobs = new Dictionary<TransferJob, EditSession>();
        private readonly Queue<EditSession> _changed = new Queue<EditSession>();
        private readonly System.Windows.Forms.Timer _editTimer = new System.Windows.Forms.Timer { Interval = 250 };
        private bool _askingWriteBack;

        partial void WireEditing()
        {
            FileActivated += OpenFile;
            JobFinished += OnEditJobFinished;
            SessionStarted += session => session.Editing.ChangeDetected += OnChangeDetected;
            SessionEnding += session =>
            {
                session.Editing.ChangeDetected -= OnChangeDetected;
                _openJobs.Clear();
                _writeBackJobs.Clear();
                _changed.Clear();
            };
            CollectingCloseWarnings += warnings =>
            {
                if (_session == null) return;
                int pending = _session.Editing.PendingSessions.Count;
                if (pending == 1) warnings.Add("1 geänderte Datei wurde noch nicht auf die PS4 zurückgeschrieben.");
                else if (pending > 1) warnings.Add(pending + " geänderte Dateien wurden noch nicht auf die PS4 zurückgeschrieben.");
            };

            _editTimer.Tick += (s, e) =>
            {
                if (_session != null) _session.Editing.Tick();
            };
            _editTimer.Start();
            FormClosed += (s, e) => _editTimer.Dispose();
        }

        /// <summary>Doppelklick/Enter auf eine Datei: laden, mit dem Windows-Programm öffnen, beobachten.</summary>
        private void OpenFile(FolderRow row)
        {
            var session = _session;
            if (session == null || row.IsFolder || row.IsLink) return;
            if (EditSessionManager.NeedsSizeConfirmation(row.Entry.Size)
                && !MessageDialog.Ask(this, "Öffnen",
                    "„" + row.DisplayName + "“ ist " + SizeFormat.Bytes(row.Entry.Size) + " groß.\nZum Öffnen wird die Datei erst vollständig heruntergeladen.",
                    "Öffnen", "Abbrechen"))
            {
                return;
            }

            var local = session.Editing.PrepareLocalPath(row.Item.Path);
            var job = session.Operations.PlanOpenForEdit(row.Item, local);
            _openJobs[job] = new PendingOpen { Item = row.Item, LocalPath = local };
            session.Queue.Enqueue(job);
            SetStatus(job.Title + " — wird geladen …");
        }

        private async void OnEditJobFinished(TransferJob job)
        {
            var session = _session;
            if (session == null) return;

            PendingOpen open;
            if (_openJobs.TryGetValue(job, out open))
            {
                _openJobs.Remove(job);
                if (job.State != JobState.Completed)
                {
                    SetStatus(job.Title + ": " + job.Summary);
                    return;
                }
                session.Editing.Register(open.Item.Path, open.LocalPath, open.Item.Size, open.Item.Modified);
                ShellOpen(open.LocalPath);
                return;
            }

            EditSession edit;
            if (!_writeBackJobs.TryGetValue(job, out edit)) return;
            _writeBackJobs.Remove(job);
            if (job.State != JobState.Completed)
            {
                SetStatus(job.Title + ": " + job.Summary);         // Änderung bleibt offen — Warnung beim Beenden
                return;
            }
            try
            {
                await session.Editing.CompleteWriteBackAsync(edit, CancellationToken.None);
                SetStatus(RemotePath.Name(edit.RemotePath) + " auf die PS4 zurückgeschrieben.");
            }
            catch (Exception ex)
            {
                SetStatus("Fehler: " + Ui.Describe(ex));
            }
        }

        /// <summary>Windows-Standardprogramm; ohne Zuordnung der „Öffnen mit"-Dialog (Spec).</summary>
        private void ShellOpen(string localPath)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true, Verb = "open" })) { }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNoAssociation)
            {
                using (Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + localPath)) { }
            }
            catch (Exception ex)
            {
                SetStatus("Öffnen fehlgeschlagen: " + Ui.Describe(ex));
            }
        }

        /// <summary>
        /// Kommt aus Tick, also auf dem UI-Thread. In eine Warteschlange statt
        /// sofort fragen: melden mehrere Dateien im selben Takt, darf keine
        /// Rückfrage verloren gehen, während eine andere offen ist.
        /// </summary>
        private void OnChangeDetected(object sender, EditSession edit)
        {
            if (!_changed.Contains(edit)) _changed.Enqueue(edit);
            if (!_askingWriteBack)
            {
                var _ = AskQueuedWriteBacksAsync();
            }
        }

        private async Task AskQueuedWriteBacksAsync()
        {
            _askingWriteBack = true;
            try
            {
                while (_changed.Count > 0) await AskWriteBackAsync(_changed.Dequeue());
            }
            finally
            {
                _askingWriteBack = false;
            }
        }

        private async Task AskWriteBackAsync(EditSession edit)
        {
            var session = _session;
            if (session == null) return;
            var name = RemotePath.Name(edit.RemotePath);
            try
            {
                if (!MessageDialog.Ask(this, "Zurückschreiben",
                        "„" + name + "“ wurde geändert.\nAuf die PS4 zurückschreiben?", "Zurückschreiben", "Nein"))
                {
                    session.Editing.Discard(edit);
                    return;
                }

                if (await session.Editing.RemoteChangedAsync(edit, CancellationToken.None)
                    && !MessageDialog.Ask(this, "Zurückschreiben",
                        "„" + name + "“ wurde auf der PS4 inzwischen geändert.\nTrotzdem mit der Fassung vom PC überschreiben?", "Überschreiben", "Abbrechen"))
                {
                    session.Editing.Discard(edit);
                    return;
                }

                var job = await session.Operations.PlanWriteBackAsync(edit.LocalPath, edit.RemotePath, CancellationToken.None);
                if (session != _session) return;
                if (job == null)
                {
                    session.Editing.Discard(edit);              // Warnzone abgelehnt
                    return;
                }
                _writeBackJobs[job] = edit;
                session.Queue.Enqueue(job);
            }
            catch (Exception ex)
            {
                if (session == _session) ShowError(ex);
            }
        }
    }
}
```

- [ ] **Step 2: Bauen und Tests**

Run: `dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5` — `0 Warnung(en)`, `0 Fehler`.
Run: `dotnet test ScreenshotGallery.sln` — alles grün.

- [ ] **Step 3: Commit**

```bash
git add src/FileManager.App/MainForm.Editing.cs
git commit -m "Open console files in Windows programs and offer to write changes back"
```

Fahrprobe-Punkte (Task 22, **Kopie im Wegwerf-Ordner**, nicht die echte `plugins.ini`): Doppelklick auf eine `.ini` öffnet Notepad (bzw. das zugeordnete Programm); direkt nach dem Öffnen kommt **keine** Frage; speichern → nach ~1 s „… wurde geändert. Auf die PS4 zurückschreiben?"; „Zurückschreiben" → Datei auf der Konsole aktuell (Neu lesen zeigt die neue Größe); Datei auf der Konsole zwischendurch ändern (z. B. per zweitem Öffnen/Zurückschreiben) → Zusatzwarnung; Datei ohne Zuordnung (`.xyz`) → „Öffnen mit"-Dialog; Datei > 200 MB → Rückfrage vor dem Laden; mit ungespeicherter Rückschreib-Ablehnung beenden → keine Frage; mit gescheitertem Rückschreiben beenden → Frage „… noch nicht zurückgeschrieben"; nach Neustart ist `%LocalAppData%\PS4FileManager\open` leer.

---

### Task 22: Abschluss — Gesamtlauf, README, Fahrprobe, Übergabe

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-09-11-ps4-file-manager-design.md` (Kopfzeile **Status**; ignoriert, kein Commit)

- [ ] **Step 1: Alles bauen und testen**

```bash
cd /e/Projects/PS4/ScreenshotGallery
dotnet build ScreenshotGallery.sln -c Release 2>&1 | grep -E "Warnung|Warning|Fehler|Error" | tail -5
dotnet test ScreenshotGallery.sln 2>&1 | grep -E "Bestanden|Passed|Fehler!|Failed!"
```

Expected: `0 Warnung(en)`, `0 Fehler`; drei Testprojekte grün. Die Zahlen je Projekt in den Bericht übernehmen.

- [ ] **Step 2: README ergänzen**

In `README.md` im Abschnitt „Bauen" den Absatz

```markdown
Die Solution enthält inzwischen drei Bibliotheken: `Ps4.Core` (FTP und
Titel-Infos, von beiden Apps geteilt), `ScreenshotGallery.Core` und
`FileManager.Core` (Kern des kommenden PS4 File Managers, noch ohne
Oberfläche).
```

ersetzen durch

```markdown
Die Solution enthält zwei Apps und drei Bibliotheken: `Ps4.Core` (FTP und
Titel-Infos, von beiden Apps geteilt), `ScreenshotGallery.Core` und
`FileManager.Core` (Kern des PS4 File Managers). Der File Manager selbst
liegt nach dem Bauen unter
`src/FileManager.App/bin/Release/net48/PS4FileManager.exe`.
```

und am Ende der Datei einen Abschnitt anfügen:

```markdown
## PS4 File Manager

Die zweite App im Repository: ein Dateimanager für die PS4 über GoldHENs
FTP-Server, der sich wie der Windows Explorer bedient.

- **Verbinden:** im Ribbon „Konsole wählen …" (IP, Port 2121), dann
  „Verbinden". Danach öffnet sich der zuletzt benutzte Ordner.
- **Links** Schnellzugriff (GoldHEN, Plugins, Cheats, PKG-Ablage, Spiele,
  Spielstände, USB, angeheftete Ordner) und der Ordnerbaum der Konsole,
  **rechts** Details oder Kacheln. Spielordner zeigen den Spielnamen.
- **Übertragen wie im Explorer:** Ziehen und Strg+C/Strg+V in beide
  Richtungen mit dem Windows Explorer. Innerhalb der PS4 verschieben per
  Ziehen oder Strg+X/Strg+V — Kopieren innerhalb der PS4 gibt es noch nicht.
- **Öffnen mit Rückschreiben:** Doppelklick öffnet eine Datei im
  zugeordneten Windows-Programm; nach dem Speichern fragt der File Manager,
  ob die Änderung zurück auf die PS4 soll.
- **Schutzzonen:** frei (`/data`, USB-Sticks, Aufnahmen), Warnung (Spiele,
  Spielstände, Patches, Zusatzinhalte — mit Rückfrage), alles andere
  gesperrt. Der Expertenmodus („Ansicht") stuft „gesperrt" zu „Warnung"
  herab.
- **Die PS4 hat keinen Papierkorb** — Löschen ist endgültig.
- **Lokale Daten:** `%LocalAppData%\PS4FileManager\` (`settings.json`;
  Arbeitskopien in `open\`, beim nächsten Start geleert).
```

- [ ] **Step 3: Fahrprobe durch den Anwender — PFLICHT**

Der Implementierer startet nichts. Der Controller bittet den Anwender, `src/FileManager.App/bin/Release/net48/PS4FileManager.exe` zu starten und die Fahrprobe-Punkte der Tasks 13–21 abzuhaken (bei 125 % Skalierung und in beiden Skins; Schreibendes **nur in einem Wegwerf-Ordner unter `/data`**, z. B. `/data/_fm_test`). Zusätzlich aus der Plan-1-Übergabe:

1. Große Datei (PKG) nach Explorer ziehen: das Hauptfenster bleibt während der Kopie bedienbar (Shell-Thread statt UI-Thread). Den Thread-Nachweis erbringt headless `ShellPublisherTests.A_drag_proxy_calls_back_on_the_publisher_thread` (Task 6); die Fahrprobe prüft stattdessen die Bedienbarkeit — damit ist der Übergabepunkt „Thread-ID loggen" erledigt.
2. Datum einer nach Explorer gezogenen Datei = Datum in der Liste (00:00 Uhr).
3. Falls ein USB-Stick steckt: `README.TXT` und `Readme.txt` gelten als Konflikt.
4. Die Gallery läuft unverändert (kurz verbinden, ein Bild öffnen) — Plan 3 hat `Ps4.Core` angefasst (`PathEncoding` öffentlich).

Findet die Fahrprobe einen Fehler: `superpowers:systematic-debugging`, Fix (mit Test, wo headless möglich) in einem eigenen Commit, dann den Punkt erneut fahren lassen.

- [ ] **Step 4: Spec-Stand nachtragen**

In `docs/superpowers/specs/2026-09-11-ps4-file-manager-design.md` die Kopfzeile **Status** ersetzen durch: „Pläne 1 (Kern), 2 (UIFramework) und 3 (App) umgesetzt; App auf Branch `file-manager-app`, Fahrprobe <Datum> bestanden." Kein Commit (ignoriert).

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "Describe the PS4 File Manager in the README"
```

- [ ] **Step 6: Branch abschließen**

REQUIRED SUB-SKILL: `superpowers:finishing-a-development-branch` — der Anwender entscheidet über den Merge nach `main`.

---

## Self-Review (beim Schreiben erledigt)

- **Spec-Abdeckung (Oberfläche):** Ribbon „Start"/„Ansicht" (T13), Verbindung (T15, Entscheidung 2), Navigationszeile mit Adressleiste und Filter (T13/T15), Baum mit Schnellzugriff und PS4 samt Laden beim Aufklappen (T15), Details mit Name+Symbol, Geändert, Typ, Größe, Title-ID, Rohwert-Sortierung und Ordnern oben (T8/T13), Umbenennen per F2-Zelle bzw. Dialog (T18), Kacheln mit Spiel-Icons (T13/T16), Shell-Symbole/Typnamen (T13), Zone in der Statusleiste und Ausgrauen (T11/T13), Übertragungsleiste (T11/T17), Statusleiste (T11/T13), Tastatur (T15/T18/T19), Kontextmenüs (T20), Drag & Drop/Zwischenablage in beide Richtungen (T6/T12/T19), Dialoge (T14), Öffnen mit Rückschreiben (T4/T21), Einstellungen (T13 ff.), Fehlerbehandlung (T13 `ShowError`, T15 Status).
- **Plan-1-Übergabe:** Verbindungslimit/OleRequired/CleanWorkRoot (T13); ein Wächter für alle Schreibwege + Expertenmodus (T10, T13); `TransferQueue` typisiert (T2); `JobChanged` → UI-Thread (T17); Cache-Invalidierung inkl. beider `/data`-Schreibweisen (T10, T17); Tick/FileSystemChangeSource (T21); Ziehen/Zwischenablage über eigenen STA-Thread, `CloseOpenStreams`, `OleSetClipboard(null)` (T6, T19); `CF_HDROP` → Upload (T19); Kopieren in der PS4 → Meldung (T19); Prompts/Resolver als Dialoge (T14); Titel mit Navigations-Token (T16); USB-Schreibweise (T1–T3); Links (T1, T3, T5, T9, T11, T19); `.part` + RNTO (T2); EditSession-Grundlage (T4); Ersetzen ohne DELE (T2); Upload-Walker (T3); UTF-8-Anzeige (T7); FILEDESCRIPTOR-Datum (T5).
- **Bewusste Abweichungen/Entscheidungen:** oben unter „Entscheidungen dieses Plans" (1–11); zusätzlich: Rückfragen im Löschen-/Warnzonen-/Konfliktdialog haben den sicheren Knopf als Standard; die Gallery hat kein Tippen-zum-Springen (UIFramework bietet es dort nicht).
- **Typen:** Namen und Signaturen stehen je Task im Interfaces-Block; spätere Tasks verwenden genau diese (`ExplorerSession`, `FolderBrowser`, `FolderRow`, `CommandState`, `PastePlanner`, `ShellPublisher`, `RunPlanAsync`, `UploadInto`, `MoveInto`, `AddShortcut`, Ereignisse `SessionStarted`/`SessionEnding`/`FolderShown`/`FileActivated`/`JobFinished`/`CollectingCloseWarnings`).
- **Nicht headless prüfbar** (nur Fahrprobe): alles in `src/FileManager.App`, `FileSystemChangeSource`, echtes OLE-Ziehen und die Zwischenablage.

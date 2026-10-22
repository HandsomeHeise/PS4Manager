# PS4 File Manager — Explorer-Grundgerüst (Design)

**Datum:** 2026-09-11
**Status:** App auf Branch `file-manager-app` implementiert; automatisierte Prüfung grün, unabhängiges Review läuft; Fahrprobe durch Anwender noch offen.
**Bezug:** Zweite App neben der PS4 Screenshot Gallery im selben Repo
(`2026-08-01-ps4-screenshot-gallery-design.md`, `2026-08-15-video-support-design.md`).
**Framework-Seite:** `E:\Projects\UIFramework\docs\superpowers\specs\2026-09-11-explorer-bausteine-design.md`
(Teilprojekt 1a — Baum, Adressleiste, Split-Container, Grid-Erweiterungen, Drag & Drop).

## Ziel

Ein Dateimanager für die PS4 über GoldHENs FTP-Server, der sich wie der Windows Explorer
bedient: links Schnellzugriff und Ordnerbaum, rechts Detail- oder Kachelansicht,
Adressleiste, Drag & Drop und Strg+C/Strg+V **in beide Richtungen** mit dem echten
Windows Explorer. Der Unterschied zu WinSCP/FileZilla ist das **PS4-Wissen**: Spielordner
heißen wie ihr Spiel, bekannte Orte (GoldHEN, Plugins, Cheats, PKG-Ablage, Spielstände,
USB) stehen im Schnellzugriff, und gefährliche Bereiche sind geschützt.

## Einordnung

Das Gesamtvorhaben ist in Teilprojekte zerlegt; dieses Dokument beschreibt nur das erste.

| Nr. | Teilprojekt | Stand |
|---|---|---|
| **1a** | Explorer-Bausteine im UIFramework | eigene Spec (siehe oben) |
| **1b** | Explorer-Grundgerüst inkl. PS4-Wissen (**dieses Dokument**) | Entwurf |
| 3 | Remote-PKG-Installer (flatz' RPI, Port 12800, PS4 zieht PKG per HTTP vom PC) | später, eigene Spec |
| 4 | Spielstände sichern/zurückspielen (verschlüsselt — Rückspielen nur gleiche Konsole/Konto) | später, eigene Spec |

(Teilprojekt 2 „PS4-Wissen" ist in 1b aufgegangen.)

## Verifizierte Fakten (lesend per FTP am 2026-09-11, Konsole 10.10.10.236:2121)

**Server:** Banner `220 GoldHEN FTP server v2.2`. `FEAT`: MDTM, MLST
(`type*;size*;unique;modify*;unix.owner*;unix.group*;unix.mode*`), REST STREAM, SITE CHMOD,
SITE UMASK, SIZE.

- Implementiert (antworten auf fehlende Pfade mit 550 statt 502): `RNFR`, `DELE`, `RMD`,
  `MKD`; `SITE CHMOD` antwortet 451. `MLSD` liefert saubere Fakten.
- `OPTS UTF8 ON` → 504 (kein UTF-8-Modus). `HELP` → 502.
- **`LIST` verträgt keine Optionen:** `LIST -a /` → 550, und der Datenkanal bleibt danach
  offen. Nie Flags an LIST hängen.
- **`unix.mode` taugt nicht zur Schreibbarkeit:** `/system` meldet 0777, ist aber
  schreibgeschützt eingehängt.

**LIST-Format:** `drwxrwxrwx 24 0 0 1024 Sep  9 2026 data` — **nur Datum, keine Uhrzeit**
(auch für heutige Dateien). Größen über 4 GB kommen korrekt
(`11873615872 Sep 11 2026 Call.of.Duty…pkg`). In `/`, `/data`, `/mnt`, `/user` keine
Symlinks gesehen.

**Dateisystem:**

| Ort | Befund |
|---|---|
| `/` | u. a. `data`, `mnt`, `system`, `system_ex`, `system_data`, `system_tmp`, `update`, `preinst`, `preinst2`, `user`, `dev`, `hdd`, `host`, `hostapp`, `usb`, `adm`, `app_tmp`, `eap_user`, `eap_vsh`; Dateien `mini-syscore.elf`, `safemode.elf`, `SceSysAvControl.elf` |
| `/data` ≡ `/user/data` | identischer Inhalt (24 Einträge) — **ein und derselbe Ordner** |
| `/data/GoldHEN` | `plugins/`, `plugins.ini`, `cheats/`, `patches/`, `config.ini`, `UPDATE/`, `stats/`, `tmp/`, `insulin/`, `repo.ini`, Sicherungen `plugins.ini.*` |
| `/data/pkg` | PKG-Ablage des Anwenders, Dateien bis 18 GB |
| `/mnt/usb0`–`usb7` | existieren **immer**, leer (Größe 0) solange nichts steckt |
| `/mnt/ext0` | erweiterter Speicher: `user/{app,addcont,appmeta,bgft,patch}`, `system_data` |
| `/user/app` | leer — **Spiele liegen auf `/mnt/ext0/user/app`** |
| `/user/home/<id>` | z. B. `194450ad`: `savedata/`, `savedata_meta/`, `trophy/`, `username.dat` (17 B) |

Rate der GoldHEN-FTP (Messung vom 2026-08-15): ~7 MB/s.

## Getroffene Entscheidungen

| Frage | Entscheidung | Verworfen |
|---|---|---|
| Einsatzzwecke | Alle: Mods/Homebrew verwalten, Spielstände, PKGs, allgemeines Stöbern | — |
| PC↔PS4-Transfer | **Wie Explorer:** Drag & Drop und Strg+C/V in beide Richtungen, hinaus als virtuelle Dateien | Nur „Herunterladen nach…" hinaus; Zwei-Fenster-Commander |
| Schutz | **Drei Zonen** (frei / Warnung / gesperrt), pfadbasiert, Expertenmodus hebt Sperre zu Warnung auf | Nur Bestätigungen; Nur-Lesen außerhalb freier Bereiche; kein Schutz |
| Doppelklick auf Datei | **Windows-Standardprogramm mit Rückschreiben** (beobachten, fragen, hochladen) | Eingebaute Betrachter; Öffnen ohne Rückweg |
| Ort der App | **Selbes Repo**, zweite App; gemeinsamer Core herausgelöst | Eigenes Repo; Integration in die Gallery |
| FTP-Schicht | **`FtpWebRequest` erweitern** | Eigener Socket-Client (MLSD, eine Steuerverbindung) |
| Fehlende UI-Bausteine | **Sauber im UIFramework** (Teilprojekt 1a) | Standard-WinForms-Controls nachgefärbt |
| Spalte „Geändert" | Nur Datum (LIST kennt keine Uhrzeit); Listing hinter `IFtpClient` gekapselt, MLSD später einsetzbar | Sofort eigener MLSD-Lister |
| Kopieren innerhalb der PS4 | **Nicht in v1**, klare Meldung statt stillem Fehlen | Umweg über den PC |

## Solution-Aufbau

```
src/
  Ps4.Core/                 (neu, herausgelöst)  Ftp/*, Titles/*  — geteilt von beiden Apps
  ScreenshotGallery.Core/   (verkleinert)        Medien, Caches, SettingsStore — nutzt Ps4.Core
  ScreenshotGallery.App/    (unverändert bis auf usings)
  FileManager.Core/         (neu)                alle testbare File-Manager-Logik
  FileManager.App/          (neu)                PS4FileManager.exe, nur Oberfläche
tests/
  Ps4.Core.Tests/           (neu)                FTP-, Parser-, SFO-Tests ziehen hierher um
  ScreenshotGallery.Core.Tests/                  der Rest bleibt
  FileManager.Core.Tests/   (neu)
```

- Abhängigkeiten: `FileManager.App` → `FileManager.Core` → `Ps4.Core`;
  `ScreenshotGallery.Core` → `Ps4.Core`. UI-Verweise (UIFramework, WinForms-Controls) nur
  in den App-Projekten. Die UIFramework-Projektverweise folgen dem Muster der Gallery
  (`..\..\..\..\UIFramework\src\...`); `FileManager.App` braucht zusätzlich
  `UIFramework.Grid` und das neue `UIFramework.Tree`.
- **Bewusste Lehre aus der Gallery:** Deren App-Schicht hat bis heute kein Testprojekt.
  Hier gehört alles, was sich ohne Fenster prüfen lässt, nach `FileManager.Core` — auch die
  Shell-Datenobjekte für Drag & Drop (siehe unten). Die App bleibt dünn.
- **Präzisierung gegenüber dem Gespräch:** `SettingsStore` (Ip/Port, Regex-Mini-JSON)
  bleibt in `ScreenshotGallery.Core`; ihn zu teilen brächte nichts, weil jede App eigene
  Einstellungen führt. Nach `Ps4.Core` wandert, was beide Apps wirklich teilen: FTP und
  Titel-Infos.
- Lokale Daten des File Managers unter `%LocalAppData%\PS4FileManager\`: `settings.json`,
  `open\` (Arbeitskopien fürs Öffnen mit Rückschreiben).

### Schritt 1: `Ps4.Core` herauslösen — ohne Verhaltensänderung

- Nach `Ps4.Core` (Namensräume `Ps4.Core.Ftp`, `Ps4.Core.Titles`): `FtpClient`, `FtpEntry`,
  `FtpTransfer`, `IFtpClient`, `UnixListingParser`, `SfoReader`, und aus `Ps4Paths` die
  beiden Titel-Pfade `SfoPath`/`IconPath` (als `TitlePaths`). `Ps4Paths` behält die
  Medienpfade.
- Tests der verschobenen Klassen (`FtpClientTests`, `FtpTransferTests`,
  `UnixListingParserTests`, `SfoReaderTests` samt `TestData/param.sfo`) ziehen nach
  `Ps4.Core.Tests`; `InternalsVisibleTo` entsprechend.
- **Abnahme:** alle 61 Tests grün (Summe beider Testprojekte), Release-Bau ohne Warnungen,
  Gallery einmal kurz gegen die Konsole angefahren.

## `Ps4.Core` — FTP-Erweiterung

Weiter auf `FtpWebRequest` (net48), weiter anonym/passiv/binär, weiter ohne eigene
Wiederholungslogik. Die bestehende Frist-Mechanik (verlinkte CTS + `CancelAfter`,
`request.Abort` auf dem Token, blockweise nachgeschobene Frist in `FtpTransfer`) gilt für
alle neuen Befehle.

`IFtpClient` wächst um:

```csharp
Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct); // STOR
Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct); // APPE — Fortsetzen
Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct); // REST über ContentOffset; Überladung, alte bleibt
Stream OpenRead(string path, long offset);                        // synchron, für den Shell-Stream (läuft nie auf dem UI-Thread)
Task<long> GetFileSizeAsync(string path, CancellationToken ct);   // SIZE — Fortsetzen von Uploads
Task MakeDirectoryAsync(string path, CancellationToken ct);       // MKD
Task RemoveDirectoryAsync(string path, CancellationToken ct);     // RMD, nur leere Ordner
Task RenameAsync(string fromPath, string toPath, CancellationToken ct); // RNFR/RNTO (FtpWebRequest.RenameTo)
```

- **`FtpEntry`** bekommt `Modified` (`DateTime`, nur Datum; `DateTime.MinValue` wenn nicht
  lesbar) und eine Sorte `FtpEntryKind { File, Directory, Link }` (bestehendes
  `IsDirectory` bleibt als Ableitung). Bei `Link` zusätzlich `LinkTarget`.
- **`UnixListingParser`** liest das Datum (`Mon DD YYYY`; das ls-übliche `Mon DD HH:MM`
  ergibt das laufende Jahr, bzw. das Vorjahr, wenn das Datum sonst in der Zukunft läge —
  Monatsnamen invariant englisch) und nimmt `l`-Einträge als `Link` auf (`name -> ziel`
  zerlegt). Sonstige Sorten (`c`, `b`, `p`, `s`) bleiben draußen.
- **Fehler:** Eine `FtpException` (neu, in `Ps4.Core.Ftp`) trägt den FTP-Statuscode aus
  `FtpWebResponse.StatusCode` und eine Klasse `Transient | Permanent`: Transport-/Zeitfehler
  (`WebExceptionStatus` Connect/Receive/Send/Timeout/ConnectionClosed, `IOException`,
  Fristablauf) sind *transient*, FTP-Antworten 4xx/5xx *permanent*. `FtpClient` übersetzt;
  darüber wird nur noch diese Unterscheidung gebraucht.
- **Verbindungslimit** (`ServicePointManager.DefaultConnectionLimit`) im File Manager: **4**
  = Transfer-Tor 1 + Shell-Tor 1 (Explorer liest virtuelle Dateien) + Durchsuchen-Tor 2
  (Listings, Titel-Infos). Die Tore sind Semaphoren in `FileManager.Core`; das Limit ist so
  gewählt, dass ihre Summe aufgeht (anders als die offene Rechnung der Gallery).

### `Ps4.Core.Titles`

- `TitleId.IsMatch(name)`: `^[A-Z]{4}\d{5}$` (CUSA, PPSA, PCAS, NPXS, …).
- `TitleInfoLoader.LoadAsync(IFtpClient, titleId, ct)` → `TitleInfo { Id, Name, IconPng }`:
  SFO von `TitlePaths.SfoPath`, Icon von `TitlePaths.IconPath`; beide Fehlschläge still
  (Name fällt auf die ID zurück, Icon fehlt) — wie heute in `MediaRepository`. Die Gallery
  darf ihn später übernehmen; Pflicht ist das in 1b nicht.

## `FileManager.Core`

### Pfade und Verlauf

- `RemotePath` (statisch): `Normalize` (führt `/` voran, faltet `//`, `.`, `..`, entfernt
  Schluss-`/`; `..` über die Wurzel bleibt `/`), `Combine`, `Parent`, `Name`,
  `IsSameOrBelow`. `Canonical` bildet zusätzlich `/user/data` → `/data` ab — **nur** für
  Zonenentscheidungen, angezeigt wird der Pfad, den der Anwender gewählt hat.
- `NavigationHistory`: Zurück/Vor/Hoch, wie im Explorer (Navigieren nach Zurück kappt den
  Vor-Stapel).

### Zonen

`Zone { Free, Warn, Locked }`. `ZonePolicy.Classify(path)` arbeitet auf
`RemotePath.Canonical(RemotePath.Normalize(path))`. **Freigabeliste**: Was nicht
ausdrücklich frei oder Warnzone ist, ist gesperrt. Eine Regel `P/**` trifft alles
**unterhalb** von `P`, nicht `P` selbst — `/data` löschen oder umbenennen ist also
gesperrt, darin anlegen/ändern frei.

| Zone | Regeln |
|---|---|
| Frei | `/data/**`, `/mnt/usb0/**` … `/mnt/usb7/**`, `/user/av_contents/**` |
| Warnung | `/user/home/**`, `/user/app/**`, `/user/patch/**`, `/user/addcont/**`, `/user/appmeta/**`, `/mnt/ext0/user/**` |
| Gesperrt | alles andere — insbesondere `/` selbst, `/system*`, `/update`, `/preinst*`, `/dev`, `/mnt/sandbox`, `/mnt/pfs`, `/mnt/disc`, `/mnt/ext0/system_data`, `/user/settings`, `/user/license`, `/user/system`, Dateien in `/` |

- Welche Pfade eine Operation betrifft: Anlegen/Hochladen → der **neue** Pfad; Löschen,
  Umbenennen, Überschreiben → der **bestehende** Pfad (bei Ordnern rekursiv jeder
  enthaltene); Verschieben → Quelle **und** Ziel. Die strengste Zone gewinnt.
- **Lesen ist überall erlaubt.**
- Expertenmodus: `Locked` wird wie `Warn` behandelt. Warnungen bleiben.
- **Durchsetzung doppelt:** (1) Beim Planen eines Auftrags ermittelt der Core die
  strengste Zone; `Warn` verlangt eine Bestätigung pro Auftrag (nicht pro Datei) über
  `IUserPrompts`, `Locked` wird abgelehnt. (2) **`ZoneGuardFtpClient`**, ein Dekorator um
  `IFtpClient`, prüft jeden Schreibaufruf (`Upload*`, `Append*`, `MakeDirectory`,
  `RemoveDirectory`, `Rename`, `Delete`) und wirft bei `Locked` (ohne Expertenmodus), bevor
  der Aufruf den Draht erreicht. Alle Schreibwege der App laufen über diesen Dekorator —
  auch Rückschreiben und Einfügen.

### PS4-Wissen

- **Titelnamen:** `TitleCatalog` (pro Verbindung) löst Ordner mit Title-ID-Namen über
  `TitleInfoLoader` auf — erst wenn ein solcher Ordner sichtbar wird, über das
  Durchsuchen-Tor (höchstens zwei gleichzeitig), Ergebnisse für die Sitzung im Speicher.
  Ein Navigieren weg bricht ausstehende Auflösungen ab. Anzeige: Name statt ID, ID in
  eigener Spalte, Icon im Baum und in der Kachel.
- **Schnellzugriff:** `QuickAccessBuilder` prüft beim Verbinden die Kandidaten (je
  Elternordner **ein** Listing) und liefert nur Vorhandenes:
  GoldHEN (`/data/GoldHEN`), Plugins, Cheats, Patches (`/data/GoldHEN/{plugins,cheats,patches}`),
  PKG-Ablage (`/data/pkg`), Aufnahmen (`/user/av_contents`), Spiele (`/user/app` **und**
  `/mnt/ext0/user/app`, jeweils nur wenn nicht leer), Spielstände je Benutzer
  (`/user/home/<id>/savedata`, Anzeigename aus `username.dat` — Format noch zu prüfen,
  Rückfall auf die ID), USB-Laufwerke (`/mnt/usbN` nur wenn nicht leer), Erweiterter
  Speicher (`/mnt/ext0`). Dazu **angeheftete Ordner** des Anwenders (in den Einstellungen).

### Verzeichnis-Cache

`DirectoryCache` (pro Verbindung) hält Listings für Baum und Liste gemeinsam. Invalidiert
wird gezielt nach eigenen Operationen (betroffene Ordner) und komplett per F5. FTP meldet
fremde Änderungen nicht — das ist akzeptiert.

### Operationen und Aufträge

`FileOperations` plant aus einer Anwenderaktion einen **Auftrag** (`TransferJob`):

| Aktion | Umsetzung |
|---|---|
| Hochladen (Dateien/Ordner) | Ordner rekursiv: `MKD` fehlender Ordner, dann Dateien per `STOR` |
| Herunterladen | rekursiv; Dateien erst als `<name>.part`, am Ende umbenennen (Gallery-Muster) |
| Neuer Ordner | `MKD`; Name „Neuer Ordner", bei Kollision „Neuer Ordner (2)" |
| Umbenennen | `RNFR/RNTO` im selben Ordner |
| Verschieben in der PS4 | `RNFR/RNTO` in den Zielordner — keine Datenübertragung |
| Löschen | rekursiv: Inhalt listen, Dateien `DELE`, Ordner `RMD` von unten nach oben |
| Kopieren in der PS4 | **nicht in v1** — `NotSupported` mit Klartext „Kopieren innerhalb der PS4 kommt später. Verschieben geht mit Strg+X." |

- Ordneraufträge ermitteln zuerst Umfang und Gesamtgröße (Phase „wird ermittelt…"), damit
  der Fortschritt ehrlich ist.
- **Löschen** fragt immer über `IUserPrompts`: „*endgültig* löschen: 37 Dateien, 1,2 GB" —
  die PS4 hat keinen Papierkorb.
- **`TransferQueue`**: ein Auftrag zur Zeit, darin eine Datei zur Zeit (Transfer-Tor 1).
  Zustände: Wartend → Ermittelt → Läuft → Fertig | Fertig mit Fehlern | Abgebrochen |
  Unterbrochen. Fortschritt (Bytes, aktuelle Datei, Rate, Restzeit) über `IProgress<>`; die
  App marshallt auf den UI-Thread.
- **Konflikte:** Vor jeder schreibenden Datei prüft der Auftrag das Ziel (Listing des
  Zielordners, pro Auftrag gecacht). `IConflictResolver.ResolveAsync(ConflictInfo, ct)` →
  `Replace | Skip | KeepBoth` plus „für alle weiteren Konflikte dieses Auftrags".
  `KeepBoth` vergibt `name (2).ext`, `name (3).ext`, … (`UniqueName`, rein und getestet).
  Gleichnamige Ordner werden **zusammengeführt**; gefragt wird pro Datei.
- **Abbrechen durch den Anwender:** laufende Datei abbrechen, angefangene Datei verwerfen —
  lokal die `.part`, auf der PS4 die halbe Datei per `DELE` (best effort).
- **Transienter Fehler** (Verbindung): Auftrag → *Unterbrochen*, Stand bleibt stehen
  (Schrittindex, angefangene Datei). *Fortsetzen* setzt fort: Download per `REST` ab der
  `.part`-Länge; Upload per `SIZE` + `APPE` ab der entfernten Länge — **sofern `APPE` an der
  Konsole verifiziert ist**, sonst beginnt die Datei neu (mit Hinweis). Keine automatische
  Wiederholung.
- **Permanenter Fehler** einer einzelnen Datei (z. B. 550): wird notiert, der Auftrag läuft
  weiter. Am Ende Bilanz „118 von 120 übertragen, 2 fehlgeschlagen" mit Grund pro Datei.
- Die Warteschlange wird nicht über einen Neustart gerettet.

### Öffnen mit Rückschreiben

`EditSessionManager`:

1. Datei als Auftrag nach `%LocalAppData%\PS4FileManager\open\<zufall>\<name>` laden;
   über **200 MB** fragt die App vorher. Grundlage merken: entfernter Pfad, Größe, Datum.
2. Die App öffnet sie per `ShellExecute` (Verb „open"); ohne Zuordnung den
   „Öffnen mit"-Dialog (`OpenAs_RunDLL`).
3. Beobachtung über `IFileChangeSource` (Produktion: `FileSystemWatcher`). Nach einer
   Änderung **1 s Ruhe** abwarten (Uhr über `IClock`, im Test steuerbar) und bis die Datei
   wieder lesbar ist, dann `ChangeDetected`.
4. Die App fragt: „`plugins.ini` wurde geändert. Auf die PS4 zurückschreiben?" Ja →
   frisches Listing des Elternordners; weichen Größe/Datum von der Grundlage ab, zusätzliche
   Warnung „auf der PS4 inzwischen geändert". Dann Upload als Auftrag — durch den
   `ZoneGuardFtpClient` und ggf. mit Zonenwarnung.
5. Beim Beenden: offene, nicht zurückgeschriebene Änderungen → Rückfrage. `open\` wird beim
   nächsten Start geleert.

### Virtuelle Dateien für Explorer (`FileManager.Core/Shell`)

`VirtualFileDataObject : System.Runtime.InteropServices.ComTypes.IDataObject` plus
`IDataObjectAsyncCapability` (Shell-Schnittstelle, per `ComImport` selbst deklariert; kein
NuGet, kein WinForms-Bezug). Formate (per `RegisterClipboardFormat`):

- `FileGroupDescriptorW` — eine `FILEDESCRIPTORW` pro Datei **und** Ordner, relative Pfade
  für Unterordner, Größe 64-bit (`FD_FILESIZE`), Datum (`FD_WRITESTIME`), `FD_PROGRESSUI`.
  **Ordner werden erst beim Abruf dieses Formats rekursiv ermittelt** — also beim
  Loslassen, nicht beim Ziehbeginn.
- `FileContents` mit `lindex` → ein `IStream`, der über `IFtpClient.OpenRead` direkt vom
  Server liest (Shell-Tor 1).
- `Preferred DropEffect` = Kopieren; `Performed DropEffect`/`Paste Succeeded` werden
  angenommen und ignoriert.
- Mit `IDataObjectAsyncCapability` kopiert Explorer in einem **eigenen Hintergrund-Thread**
  und zeigt seinen Kopierdialog; auch ein 18-GB-PKG blockiert weder Explorer noch die App.
- Zwischenablage: `Clipboard.SetDataObject(obj, copy: false)` — **nie** `copy: true`, sonst
  erzwingt `OleFlushClipboard` beim Beenden das Herunterladen aller Inhalte. Nach dem
  Beenden der App ist die Zwischenablage-Kopie ungültig; das ist akzeptiert.

### Einstellungen

`FileManagerSettings` (Ip, Port, Expertenmodus, Ansicht, letzter Pfad, angeheftete Ordner,
Spaltenbreiten), gespeichert über `DataContractJsonSerializer` (im Framework enthalten,
kein NuGet). Korrupte Datei = Neuanfang mit Standardwerten, kein Absturz (Gallery-Regel).

## Oberfläche (`FileManager.App`, `MainForm : SkinnedForm`, Dark-Skin als Standard)

```
┌─ PS4 File Manager ───────────────────────────────────────────── _ □ × ┐
│ Start │ Ansicht                                                       │
│ [10.10.10.236:2121 ▸ Verbinden] [Hochladen][Herunterladen][Neuer     │
│  Ordner][Umbenennen][Löschen]            [Details|Kacheln][Expertenm.]│
├───────────────────────────────────────────────────────────────────────┤
│ [←][→][↑][⟳]  PS4 › data › GoldHEN › plugins          [Filter…     ] │
├──────────────────────┬────────────────────────────────────────────────┤
│ ★ Schnellzugriff     │ Name                  Geändert    Typ     Größe│
│   GoldHEN            │ 📁 insulin            07.09.2026  Ordner       │
│   Plugins …          │ ⚙ plugins.ini         08.09.2026  Konfig. 338 B│
│ 🖴 PS4               │                                                │
│   ▸ data / mnt / …   │                                                │
├──────────────────────┴────────────────────────────────────────────────┤
│ ▾ Übertragungen: InsulinGTAV.prx hochladen ███████░░ 78 % · 6,9 MB/s   │
├───────────────────────────────────────────────────────────────────────┤
│ Verbunden · 3 Elemente · 1 ausgewählt (412 KB) · Zone: frei            │
└───────────────────────────────────────────────────────────────────────┘
```

- **Ribbon** (`RibbonControl`) mit „Start" (Verbindung, Hochladen, Herunterladen, Neuer
  Ordner, Umbenennen, Löschen) und „Ansicht" (Details/Kacheln, Expertenmodus).
  Verbindungsfelder wie in der Gallery. Nach dem Verbinden: letzter Pfad, sonst `/data`.
- **Navigationszeile:** Zurück/Vor/Hoch/Aktualisieren als `SkinButton`s, `AddressBar`
  (1a), Filterfeld (`SkinTextBox` → `FilteredSource`, nur aktueller Ordner).
- **Links** `TreeControl` (1a) mit zwei Wurzeln: *Schnellzugriff* und *PS4* (`/`); Knoten
  laden ihre Kinder erst beim Aufklappen. **Rechts** wahlweise `GridControl` (Details) oder
  `GalleryControl` (Kacheln). Getrennt durch `SkinSplitContainer` (1a).
- **Details:** Name (Symbol + Text), Geändert (Datum), Typ, Größe, Title-ID. Sortierung per
  Spaltenkopf mit Rohwerten (Größe numerisch, Datum als Datum), **Ordner immer oben**
  (Gruppenrang, 1a). `EditTriggers = F2`; Doppelklick/Enter → `RowActivated`.
- **Umbenennen:** In der Detailansicht startet F2 die Zellbearbeitung der Namensspalte; die
  Datenquelle implementiert `IWritableGridDataSource`, und `SetValue` stößt die Umbenennung
  als Auftrag an (Zonenprüfung inklusive; bei Fehler springt der Name zurück). In der
  Kachelansicht (keine Zellbearbeitung) öffnet F2 einen kleinen Umbenennen-Dialog.
- **Kacheln:** große Symbole; Spielordner zeigen ihr Spiel-Icon.
- **Symbole/Typnamen:** `SHGetFileInfo` mit `SHGFI_USEFILEATTRIBUTES` über die Endung
  (Ordner über `FILE_ATTRIBUTE_DIRECTORY`), pro Endung gecacht — ohne Download, aussehend
  wie im Explorer.
- **Zone** in der Statusleiste; in gesperrten Ordnern sind Schreibbefehle ausgegraut
  (Durchsetzung bleibt im Core).
- **Übertragungsleiste** unten, einklappbar: Aufträge mit Status, Fortschritt, Rate,
  Restzeit, Abbrechen/Fortsetzen; fertige Aufträge bleiben bis „Liste leeren" stehen.
- **Statusleiste:** `SkinPanel` + `SkinLabel`s (Verbindung, Elemente, Auswahl mit Größe,
  Zone).
- **Tastatur:** Enter öffnen · Rücktaste/Alt+↑ hoch · Alt+←/→ Verlauf · F2 umbenennen ·
  Entf löschen · Strg+C/X/V · Strg+A · F5 · Strg+Umschalt+N neuer Ordner · Strg+L
  Adressleiste · Tippen springt zum Namen.
- **Kontextmenü** (`PopupMenu`, MouseUp-Muster der Gallery): auf Einträgen Öffnen,
  Herunterladen nach…, Ausschneiden, Kopieren, Umbenennen, Löschen, Pfad kopieren, An
  Schnellzugriff anheften (Ordner); auf freier Fläche Einfügen, Neuer Ordner, Hochladen…,
  Aktualisieren.
- **Drag & Drop / Zwischenablage:**
  - *Hinein:* `CF_HDROP` aus Explorer auf Liste, Ordnerzeile/-kachel oder Baumknoten
    ziehen, oder Strg+V → Upload-Auftrag in den Zielordner.
  - *Hinaus:* Ziehen (`ItemDrag` aus 1a → `DoDragDrop`) oder Strg+C → `VirtualFileDataObject`.
  - *Innen:* Ziehen auf einen Ordner bzw. Strg+X/Strg+V → Verschieben. Strg+C/Strg+V
    innerhalb der PS4 → Meldung (siehe Operationen).
- **Dialoge** als kleine `SkinnedForm`s: Konflikt, Zonenwarnung, Löschbestätigung,
  Rückschreiben. Für lokale Ordner-/Dateiauswahl die Standarddialoge (wie die Gallery).

## Fehlerbehandlung

- Verbindungs-/Listenfehler beim Durchsuchen → roter Status mit Klartext (Gallery-Muster),
  kein Absturz.
- FTP-Codes in Klartext: 550 → „Nicht vorhanden oder kein Zugriff", 553 → „Name nicht
  erlaubt", 451/452 → „Fehler auf der Konsole / kein Platz", sonst Code + Servertext.
- Aufträge: siehe *Operationen und Aufträge* (permanent → Bilanz, transient → Unterbrochen).
- Zonen: `Locked` → Meldung mit Pfad und Grund, kein Dialog zum Übergehen (dafür gibt es den
  Expertenmodus).

## Tests

**`Ps4.Core.Tests`** (xunit, wie bisher)
- Die umgezogenen Tests unverändert grün.
- Parser: Datum (`Mon DD YYYY`, `Mon DD HH:MM` mit Jahreswechsel-Regel), Links
  (`name -> ziel`), echte Zeilen aus `/data/pkg` (>4 GB) als Testdaten.
- `BuildUri` für die neuen Befehle; `FtpException`-Klassifizierung (transient/permanent).
- Upload-Kopierpfad: Fortschritt und blockweise nachgeschobene Frist (analog
  `FtpTransferTests`).

**`FileManager.Core.Tests`** — Fake-`IFtpClient` als Dateisystem im Speicher, der jeden
Aufruf protokolliert.
- **Zonen:** die Tabelle als Theorie-Testfälle; Umgehungsversuche (`/data/../system`,
  `//system`, `/user/data/x` ≙ `/data/x`, `/data` selbst gesperrt); Expertenmodus; je
  Operation: **gesperrt ⇒ null Schreibaufrufe am Fake** (über den `ZoneGuardFtpClient`).
- **Operationen:** rekursives Löschen in richtiger Reihenfolge; Hochladen mit Ordnerbau;
  Zusammenführen; Konflikte (alle drei Entscheidungen, „für alle"); `UniqueName`.
- **Warteschlange:** Bilanz bei Einzelfehlern; Abbrechen verwirft angefangene Dateien
  (lokal und entfernt); transienter Fehler ⇒ *Unterbrochen*; Fortsetzen mit richtigem
  Versatz (REST/APPE).
- **Rückschreiben:** Änderung erkannt, 1-s-Ruhe über Test-Uhr, Warnung bei geändertem
  entfernten Stand, Zonenprüfung greift.
- **Virtuelle Dateien:** `GetData(FileGroupDescriptorW)` wie Explorer abfragen und die
  Struktur zerlegen (inkl. Unterordner, >4 GB), `GetData(FileContents, lindex)` → `IStream`
  liefert die richtigen Bytes.
- **PS4-Wissen:** `TitleId.IsMatch`; `QuickAccessBuilder` gegen Fake-Dateisysteme (USB
  leer/belegt, Spiele intern/ext0, Benutzer mit/ohne `username.dat`).
- `RemotePath`, `NavigationHistory`, `DirectoryCache`-Invalidierung.

**Nur per Fahrprobe** (am Fenster, 120 dpi, beide Skins): Drag & Drop mit echtem Explorer
in beide Richtungen, Explorer-Kopierdialog bei großer Datei, Strg+C/Strg+V über
Programmgrenzen, Rückschreiben mit echtem Editor, Titelnamen/Icons, Zonen-Ausgrauung.
Destruktive Proben **nur in einem Wegwerf-Ordner unter `/data`**.

## Vor der FTP-Erweiterung an der Konsole zu prüfen

Schreibend, **nur in einem Wegwerf-Ordner unter `/data`** und erst nach Freigabe des Plans:

1. Hochladen per `STOR`; Fortsetzen per `APPE` (hängt die Datei wirklich an?).
2. `RNFR/RNTO` im selben Ordner und in einen anderen Ordner (absoluter Zielpfad).
3. `RNTO` auf eine schon existierende Datei (überschreibt? Fehler?).
4. `RMD` auf einen nicht leeren Ordner (erwartet: Fehler).
5. Inhalt und Kodierung von `username.dat` (lesend).
6. Dateinamen mit Umlauten (Hochladen, Listen, Löschen) — ohne UTF-8-Aushandlung.

Weicht ein Ergebnis ab, wird die betroffene Stelle dieser Spec angepasst, bevor gebaut wird.

### Befunde (2026-09-11)

Schreibprobe per Wegwerf-Programm (`FtpWebRequest`, net48) gegen 10.10.10.236:2121, Ordner
`/data/_fm_probe_20260911_121257` (am Ende entfernt — siehe Punkt „Aufräumen" unten). Volles
Protokoll in `task-2-report.md` neben diesem Task.

1. **STOR/APPE:** `02 STOR a.txt 'hello' -> 226 …`, `03 RETR a.txt -> 'hello'`,
   `04 APPE a.txt ' world' -> 226 …`, `05 RETR … -> 'hello world'`, `06 SIZE a.txt -> 11`,
   `07 RETR a.txt ab Offset 6 -> 'world'`. Alles wie erwartet — APPE hängt wirklich an, SIZE
   und REST stimmen. Kein Abweichen, kein STOPP. **Konsequenz:** `TransferQueue.AppendResumesUploads`
   bleibt bei Standard `true` (Task 15).
2. **RNFR/RNTO relativ und absolut:** `08 … a.txt -> b.txt -> 250 …`,
   `10 … b.txt -> <Basis>/sub/c.txt (absolut) -> 250 …`, `11 LIST sub` zeigt `c.txt`. Beide
   Formen funktionieren wie erwartet. Kein Abweichen, kein STOPP. **Konsequenz:**
   `FtpClient.RenameAsync` mit absolutem `RenameTo` ist freigegeben (Task 6).
3. **RNTO auf existierende Datei:** `13 RNTO d.txt -> sub/c.txt -> 250 …` (obwohl `sub/c.txt`
   schon existierte), `14 RETR sub/c.txt -> 'neu'` statt des vorherigen `'hello world'` — der
   Server überschreibt kommentarlos, kein Fehler. **Konsequenz:** `InMemoryFtpClient.RenameAsync`
   (Task 9) muss ebenfalls überschreiben; die Konfliktprüfung vor dem Umbenennen bleibt in
   `FileOperations` in jedem Fall nötig (Server verhindert nichts).
4. **RMD auf nicht leeren Ordner:** `15 RMD sub -> [FAIL] 550 Directory not empty.` — wie
   erwartet ein Fehler, keine rekursive Löschung durch den Server. Kein Abweichen, kein
   STOPP. **Konsequenz:** Der Löschplan (Task 14) bleibt bottom-up wie geplant, keine Änderung.
5. **`username.dat`:** `20 username.dat -> HEX 42-65-6E-75-74-7A-65-72-31-00-00-00-00-00-00-00-00
   | UTF8 'Benutzer1\0\0\0\0\0\0\0\0'` — 17 Byte gesamt: 9 Byte Name (`Benutzer1`, ASCII/UTF-8)
   gefolgt von 8 Nullbytes. Entspricht der Erwartung „Name + `\0` (17 Byte)". Kein Abweichen,
   kein STOPP. **Konsequenz:** `UserNameReader` (Task 11) liest einen 17-Byte-Puffer und
   dekodiert als ASCII/UTF-8 bis zum ersten `\0`.
6. **Umlaute im Dateinamen:** `16 STOR 'Ärger ü.txt' -> 226 …` (Upload mit UTF-8-kodiertem,
   prozent-escaptem Pfad gelang). `17 LIST Basis` liefert den Eintrag aber **nicht**
   UTF-8-identisch zurück: die Rohbytes enden auf
   `…-C4-72-67-65-72-20-FC-2E-74-78-74` (`Ärger ü.txt`) statt der gesendeten UTF-8-Sequenzen
   `C3-84` (Ä) und `C3-BC` (ü) — der Server liefert die Bytes für Ä/ü einzeln als Latin-1/CP1252
   (`0xC4`, `0xFC`), nicht als UTF-8-Mehrbyte-Sequenz. Mit `Encoding.UTF8.GetString` auf die
   LIST-Antwort ergibt das Ersatzzeichen (`�rger �.txt`) statt des korrekten Namens.
   `18 RETR 'Ärger ü.txt' -> 'umlaut'` gelang trotzdem, weil RETR mit demselben (UTF-8-kodierten)
   Pfad wie beim STOR angefragt wurde und der Server das Dateisystem intern konsistent
   behandelt — nur die **Anzeige** in LIST weicht ab.
   **STOPP ausgelöst** (Tabellenzeile 16–18): Der Name in LIST ist nicht byte-identisch zur
   UTF-8-Eingabe. Bevor mit dem UTF-8-Pfad weitergebaut wird, muss mit dem Anwender geklärt
   werden, welche Kodierung `UnixListingParser`/`FtpClient` für Namen mit Nicht-ASCII-Zeichen
   ansetzen (Latin-1/CP1252 statt UTF-8 wäre ein Kandidat, da die Bytes dann korrekt auf Ä/ü
   abbilden). Bis zur Klärung: **kein Bau von Task 6 ff. mit Nicht-ASCII-Namen**.
- **Aufräumen (25–26):** `25 RMD Basis -> 250 …`; `26 LIST /data` enthält den Ordner
  `_fm_probe_20260911_121257` nicht mehr — vollständig entfernt, keine Handnacharbeit nötig.
  (`22 DELE d.txt -> [FAIL] 550` ist unauffällig: `d.txt` existierte durch die Umbenennung in
  Punkt 3 zu diesem Zeitpunkt bereits nicht mehr unter diesem Namen.)

**Gesamtstatus:** Ein STOPP-Fall (Punkt 6, Umlaute/Kodierung in LIST). Alle anderen Punkte
(1–5) entsprechen der Erwartung, kein weiterer STOPP.

## Nicht-Ziele (v1)

Kopieren innerhalb der PS4 · rekursive Suche · MLSD/Uhrzeit in „Geändert" · Tabs/mehrere
Fenster · Aufklapplisten in der Adressleiste · Warteschlange über Neustart retten ·
mehrere Konsolen gleichzeitig · Vorschaubilder beliebiger Bilddateien (nur Spiel-Icons) ·
`SITE CHMOD` · automatische Wiederholung · PKG-Installation (Teilprojekt 3) ·
Spielstand-Werkzeuge (Teilprojekt 4).

## Reihenfolge

1. `Ps4.Core` herauslösen (ohne Verhaltensänderung; Abnahme siehe oben).
2. Konsolenprüfungen (siehe oben), dann FTP-Erweiterung und `FileManager.Core` — beide
   UI-frei, parallel zu 1a möglich.
3. UIFramework-Teilprojekt 1a (eigene Spec, eigener Plan).
4. `FileManager.App`, zuletzt die Fahrprobe.

### Entscheidung zu den Befunden (Controller, 2026-09-11)

- **Umlaute (Punkt 6):** Kein Fehler von GoldHEN. `FtpWebRequest` fragt `OPTS utf8 on`, GoldHEN
  antwortet `504`, danach schreibt .NET Pfade in `Encoding.Default` (hier Windows-1252:
  Ä=`C4`, ü=`FC`, €=`80`; per Wegwerf-Spike gegen einen lokalen Mitschnitt-Server belegt).
  GoldHEN speichert genau diese Bytes. **Folge:** `FtpClient` liest LIST mit derselben
  Kodierung (`FtpClient.PathEncoding = Encoding.Default`), damit jeder gelistete Name
  byte-genau wieder trifft. Namen, die andere Clients als UTF-8 abgelegt haben, erscheinen
  als Mojibake, bleiben aber erreichbar (Anzeige-Verbesserung später in Plan 3). **Grenze:**
  Lädt der File Manager Dateien mit Umlauten hoch, liegen deren Namen als Windows-1252-Bytes
  auf der Konsole; PS4-eigene Anwendungen, die UTF-8 erwarten, zeigen sie verstümmelt an.
- **RNTO (Punkt 3):** GoldHEN überschreibt eine existierende Zieldatei kommentarlos. Beide
  Test-Fakes bilden das nach; die Konfliktprüfung vor dem Umbenennen bleibt Pflicht.

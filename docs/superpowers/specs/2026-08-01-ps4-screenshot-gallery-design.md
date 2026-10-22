# PS4 Screenshot Gallery — Design

**Datum:** 2026-08-01
**Status:** Entwurf, vom User abgesegnet (Abschnitte 1–3 im Gespräch bestätigt)
**Abhängigkeit:** `GalleryControl` im UIFramework — Spec:
`E:\Projects\UIFramework\docs\superpowers\specs\2026-08-01-gallerycontrol-design.md`
(wird zuerst gebaut, diese App ist der erste Konsument)

## Ziel

Ein WinForms-Tool (.NET Framework 4.8), das per FTP auf die PS4 (GoldHEN 9.00) zugreift und
alle Screenshots als Gallery anzeigt — gruppiert nach Spiel (Title-ID), mit echtem Spielnamen
und Spiel-Icon als Gruppenkopf. Aktionen: Herunterladen auf den PC, in die Zwischenablage
kopieren, auf der PS4 löschen, Vollbild-Vorschau.

## Verifizierte PS4-Fakten (per FTP am 2026-08-01 erhoben)

FTP-Server: GoldHEN, anonym, Standardwerte `10.10.10.236:2121`, Unix-`LIST`-Format.

| Was | Pfad |
|---|---|
| Screenshots | `/user/av_contents/photo/NPXS20001/<TitleID>/<hash>/<name>.jpg` (~450 KB) |
| Metadaten daneben | `<name>.dat` (592 B, wird beim Löschen mitgelöscht, sonst ignoriert) |
| Thumbnails | `/user/av_contents/thumbnails/photo/NPXS20001/<TitleID>/<hash>/<name>.jpg.jpg` (~24 KB) |
| Spielname | `/system_data/priv/appmeta/<TitleID>/param.sfo` (Feld `TITLE`) |
| Spiel-Icon | `/user/appmeta/<TitleID>/icon0.png` (512×512) |

- `<hash>` ist ein dreistelliger Hex-Ordner (z. B. `104`); Foto- und Thumbnail-Zweig nutzen
  denselben Hash und Dateinamen (Thumbnail hängt ein zweites `.jpg` an).
- Dateiname: `JJJJMMTT_HHMMSS_XXXXXXXX.jpg` → Aufnahmezeitpunkt wird aus den ersten beiden
  Segmenten geparst (Fallback bei Abweichung: FTP-Änderungsdatum).
- Der Thumbnail-Zweig kann mehr Hash-Ordner enthalten als der Foto-Zweig (Reste gelöschter
  Bilder) — führend ist immer der Foto-Zweig.

## Solution-Aufbau

`E:\Projects\PS4\ScreenshotGallery` (eigenes git-Repo, net48, LangVersion 7.3,
`Directory.Build.props` nach UIFramework-Muster):

| Projekt | Inhalt | Referenzen |
|---|---|---|
| `src/ScreenshotGallery.Core` | FTP, Parser, Pfadmodell, Cache — keine UI | — |
| `src/ScreenshotGallery.App` | WinForms-Oberfläche | Core + ProjectReference auf `E:\Projects\UIFramework\src\{Core,Controls,Gallery}` |
| `tests/ScreenshotGallery.Core.Tests` | xunit | Core |

## Core-Komponenten

- **`FtpClient`** — dünner Wrapper um `FtpWebRequest` (kein NuGet): `ListAsync(path)`,
  `DownloadAsync(path)`, `DeleteAsync(path)`; anonym, Passiv-Modus, `KeepAlive`,
  Timeouts (Verbinden 8 s, Transfers 30 s). Kein Upload.
- **`UnixListingParser`** — parst GoldHENs `LIST`-Zeilen (`drwxr-xr-x … Name`) in
  `FtpEntry { Name, IsDirectory, Size }`; ignoriert `.` und `..`.
- **`SfoReader`** — liest aus `param.sfo` (Binärformat, Key-Tabelle) den `TITLE`-String
  (UTF-8). Liefert `null` bei unlesbarer/fehlender Datei.
- **`PhotoRepository`** — kennt die Pfadstruktur: liefert pro Title-ID die Screenshot-Liste
  (`Screenshot { TitleId, HashDir, FileName, Size, Timestamp }`), mappt auf Thumbnail- und
  `.dat`-Pfad, kapselt „alles zu einem Screenshot löschen" (`.jpg`, `.dat`, Thumbnail;
  fehlendes Thumbnail ist kein Fehler).
- **`ThumbnailCache`** — Disk-Cache unter `%LocalAppData%\ScreenshotGallery\cache\<TitleID>\`,
  Schlüssel = Dateiname; auch der Voll-JPEG-Cache der Vorschau liegt hier (Unterordner `full\`).
- **`SettingsStore`** — `%LocalAppData%\ScreenshotGallery\settings.json` (IP, Port);
  handgeschriebenes Mini-JSON (zwei Felder), kein NuGet.

## Oberfläche (`MainForm : SkinnedForm`, Dark-Skin als Standard)

- **Toolbar**: `SkinTextBox` IP + Port (vorbelegt aus Settings, Default `10.10.10.236:2121`),
  `SkinButton` „Verbinden" / „Aktualisieren", `SkinLabel` als Status (grau getrennt /
  grün verbunden / Fortschritt „Lade 3 von 9 …" / rot Fehler).
- **Mitte**: `GalleryControl`, `ItemCheckMode.Multiple`, `ImageSize` 192×108.
  Eine Gruppe pro Spiel: Caption „Spielname (Title-ID)", `CaptionImage` = `icon0.png`
  (verkleinert). Items: Thumbnail, Caption = Aufnahmezeit („01.08.2026 08:49"),
  Description = Dateigröße, sortiert neueste zuerst; Gruppen alphabetisch nach Name.
- **Kontextmenü** (`PopupMenu` aus dem Framework) auf der Auswahl:
  *Herunterladen…* (FolderBrowserDialog, lädt volle JPEGs, bestehende Dateien werden
  überschrieben), *In Zwischenablage kopieren* (nur bei genau 1 Item aktiv),
  *Vorschau öffnen*, *Löschen…*.
- **Vorschaufenster** (`PreviewForm : SkinnedForm`): öffnet per Doppelklick/Enter, zeigt das
  volle JPEG (per FTP geladen, dann gecacht), skaliert eingepasst; Pfeil links/rechts blättert
  durch dieselbe Gruppe, Esc schließt. Titelzeile: Spielname + Aufnahmezeit.

## Datenfluss

1. **Verbinden**: `LIST` auf den Foto-Wurzelpfad → Title-IDs. Pro Title-ID (parallel, max. 3):
   `param.sfo` → Name (Fallback: Title-ID), `icon0.png` (Fallback: ohne Icon), Hash-Ordner
   auflisten, `*.jpg` einsammeln. Gruppen + Items erscheinen sofort — ohne Bilder.
2. **Thumbnails**: Das Control fordert sichtbare Items per `ItemImageNeeded` an. Die App
   bedient aus dem Disk-Cache oder lädt per FTP nach (Download-Queue, max. 3 parallel),
   setzt `item.Image` — das Control zeichnet sich selbst neu.
3. **Aktualisieren**: verwirft die Gruppen und läuft Schritt 1–2 erneut; der Disk-Cache bleibt.
4. **Löschen**: Bestätigungsdialog mit Anzahl und dem Hinweis, dass die PS4-eigene
   Galerie-Datenbank per FTP nicht aktualisiert wird (verwaiste Einträge räumt die Konsole
   selbst beim nächsten Scan). Dann pro Screenshot `.jpg` + `.dat` + Thumbnail löschen,
   Item aus der Gallery entfernen, Cache-Eintrag löschen.
5. **Zwischenablage**: volles JPEG laden (Cache-first) → `Clipboard.SetImage`.

## Fehlerbehandlung

- Alle FTP-Operationen async (Task-basiert), UI bleibt bedienbar; Ergebnisse werden auf den
  UI-Thread marshallt.
- Verbindungs-/Listenfehler → roter Status mit Klartext, „Verbinden" bleibt möglich.
- Teilfehler beim Löschen/Herunterladen mehrerer Dateien: am Ende eine Meldung, **welche**
  Dateien scheiterten; erfolgreiche bleiben erledigt.
- Fehlendes `param.sfo`/`icon0.png`/Thumbnail: stiller Fallback, kein Fehler.
- Laufende Thumbnail-Queue wird bei Trennen/Aktualisieren/Beenden storniert.

## Nicht-Ziele (v1)

Videos (`/user/av_contents/video`), Upload/Import auf die PS4, Bearbeiten der PS4-Galerie-DB,
Verwaltung mehrerer Konsolen, Umbenennen, Diashow.

## Tests (`ScreenshotGallery.Core.Tests`, xunit)

- `UnixListingParser`: echte GoldHEN-Zeilen (Verzeichnis, Datei, `.`/`..`, Sonderrechte `-rwxrwx---`).
- `SfoReader`: gegen eine eingecheckte echte `param.sfo` (CUSA00411 → „Grand Theft Auto V")
  und gegen Müll-Bytes (→ `null`).
- Dateiname→Zeitstempel: gültig, ungültig (Fallback-Pfad).
- `PhotoRepository`-Pfadmapping: Foto ↔ Thumbnail ↔ `.dat`.
- `ThumbnailCache`: Schlüsselbildung, Treffer/Fehltreffer, korrupte Datei (0 Byte → Fehltreffer).
- UI wird per Auge verifiziert (echte PS4 vorhanden).

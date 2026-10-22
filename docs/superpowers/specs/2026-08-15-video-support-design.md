# PS4 Screenshot Gallery — Videounterstützung (Design)

**Datum:** 2026-08-15
**Status:** Entwurf, im Gespräch abgesegnet (Teil 1 und Teil 2 bestätigt)
**Bezug:** Erweitert `2026-08-01-ps4-screenshot-gallery-design.md`. Dort standen Videos
(`/user/av_contents/video`) ausdrücklich unter „Nicht-Ziele (v1)" — dieses Dokument hebt
das auf und beschreibt nur die Abweichungen; alles nicht Genannte bleibt wie in der v1-Spec.

## Ziel

Aufgenommene Videoclips der PS4 erscheinen gleichberechtigt neben den Screenshots: als
Kacheln in derselben Spielgruppe, mit Thumbnail und Play-Zeichen, herunterladbar, löschbar,
in die Zwischenablage übernehmbar und **im Tool selbst abspielbar**.

## Verifizierte PS4-Fakten (per FTP am 2026-08-15 erhoben, Konsole 10.10.10.236:2121)

Der Video-Zweig ist zum Foto-Zweig **strukturgleich**. Die ursprüngliche Annahme, Clips
hätten keine Thumbnails, ist widerlegt:

| Was | Pfad |
|---|---|
| Clip | `/user/av_contents/video/NPXS20001/<TitleID>/<hash>/<name>.mp4` |
| Metadaten daneben | `<name>.dat` (592 B — Größe und Rolle identisch zum Foto-Zweig) |
| Thumbnail | `/user/av_contents/thumbnails/video/NPXS20001/<TitleID>/<hash>/<name>.mp4.jpg` |

Konkret vorgefunden: `CUSA00411/2c7/20260810_212645_00367837.mp4` (15 454 876 B) mit
`20260810_212645_00367837.dat` (592 B) und dem Thumbnail
`20260810_212645_00367837.mp4.jpg` (26 540 B).

- Die Zwischenebene `NPXS20001` gilt im Video-Zweig genauso wie im Foto-Zweig.
- `<hash>` ist derselbe dreistellige Hex-Ordner; Clip- und Thumbnail-Zweig nutzen denselben
  Hash und denselben Dateinamen, das Thumbnail hängt wie beim Foto ein zweites `.jpg` an.
- Der Dateiname folgt derselben Regel `JJJJMMTT_HHMMSS_XXXXXXXX.<ext>`; `ParseTimestamp`
  greift damit unverändert.
- Das Thumbnail ist ein gültiges JPEG (heruntergeladen und geöffnet: 440×248, echtes
  Standbild aus dem Clip) — kein Sonderformat, keine Frame-Extraktion nötig.

### Abspielbarkeit (Wegwerf-Prototyp, 2026-08-15)

Der reale Clip wurde mit einer WPF-`MediaElement` in einem WinForms-`ElementHost` geöffnet:
`MediaOpened` OK, 1280×720, Dauer 19 s, Video und Audio vorhanden, `CanPause = true`,
`Position = 5 s` wurde exakt übernommen (Springen funktioniert). Nötige Verweise:
`PresentationCore`, `PresentationFramework`, `WindowsBase`, `WindowsFormsIntegration`,
`System.Xaml` — alle im .NET Framework 4.8 enthalten, **kein NuGet, kein COM-Interop**.
Der Prototyp bleibt außerhalb des Repos.

Übertragungsrate der GoldHEN-FTP im Test: 14,7 MB in rund 2 s (~7 MB/s). „Erst laden, dann
abspielen" ist damit für übliche Cliplängen praktikabel.

## Getroffene Entscheidungen

| Frage | Entscheidung | Verworfen |
|---|---|---|
| Abspielen | Eingebauter Player im Vorschaufenster (WPF-`MediaElement` im `ElementHost`) | Externer Standard-Player; nur Standbild |
| Darstellung | Clips gemischt in der Spielgruppe, nach Zeit sortiert, Play-Zeichen auf der Kachel | Eigene Videogruppe je Spiel; Filter-Umschalter in der Toolbar |
| Core-Modell | Ein Typ mit Sorte: `MediaItem` + `MediaKind` | Vererbungshierarchie; zweites Repository neben dem bestehenden |

Begründung zum Modell: Foto- und Video-Zweig legen dasselbe Schema ab (gleiche Hash-Ordner,
gleiche Namensregel, gleiche `.dat`, gleiche Thumbnail-Regel). Ein Modell mit einer Sorte
bildet das ab; die Alternativen behandeln als verschieden, was die Konsole gleich ablegt,
und verteilen die Verzweigung über mehr Stellen.

## Core-Änderungen

### Modell

- `Screenshot` → **`MediaItem`**: Felder `TitleId`, `HashDir`, `FileName`, `Size`,
  `Timestamp` unverändert, neu `MediaKind Kind`. `ParseTimestamp` bleibt wie sie ist.
- Neu **`MediaKind { Photo, Video }`**.
- `GameFolder.Screenshots` → **`GameFolder.Items`** (`IReadOnlyList<MediaItem>`).

### `Ps4Paths`

Die Sorte wird Parameter statt eines zweiten Methodensatzes — es tauscht nur ein Segment:

```
MediaRoot(Photo)     = /user/av_contents/photo/NPXS20001
MediaRoot(Video)     = /user/av_contents/video/NPXS20001
ThumbnailRoot(Photo) = /user/av_contents/thumbnails/photo/NPXS20001
ThumbnailRoot(Video) = /user/av_contents/thumbnails/video/NPXS20001
```

`PhotoPath` → `MediaPath(MediaItem)`; `DatPath(MediaItem)` und `ThumbnailPath(MediaItem)`
lesen die Wurzel aus `Kind`. `TitleDir` bekommt die Sorte als zusätzlichen Parameter.
`SfoPath` und `IconPath` bleiben unberührt.

### `PhotoRepository` → `MediaRepository`

- `LoadAsync` listet **beide** Wurzeln und **vereinigt** die Title-IDs. Ein Spiel kann Clips
  ohne Screenshots haben (oder umgekehrt) — ohne Vereinigung fiele es aus der Ansicht.
  Fehlt eine der beiden Wurzeln oder eine Title-ID in einem der Zweige, ist das kein Fehler,
  sondern der Normalfall (stiller Fallback wie bei `param.sfo`).
- Pro Spiel werden beide Bäume durchlaufen und in **eine** Liste gemischt, absteigend nach
  `Timestamp`. `param.sfo` und `icon0.png` wie bisher, einmal je Spiel.
- Das Parallelitätstor bleibt bei 3.
- **Dateifilter:** Foto-Zweig weiter `*.jpg`. Video-Zweig nimmt **alles außer `.dat`** statt
  einer Endungsliste — der Zweig wird allein von der Konsole gepflegt, und die einzige
  Begleitdatei ist die `.dat`. Eine Endungsliste würde bei abweichenden Containern (`.webm`
  o. ä.) still Clips verschlucken.
- `DeleteAsync` bleibt strukturell gleich: die Mediendatei ist die Wahrheit, `.dat` und
  Thumbnail dürfen fehlen; nur die Pfade sind sortenabhängig.

### Streamender Download und Untätigkeits-Timeout

- **`IFtpClient.DownloadToFileAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken ct)`**
  kommt hinzu. `DownloadAsync` (`byte[]`) bleibt für Thumbnails, `param.sfo`, `icon0.png`
  und Voll-JPEGs — alles klein. Ein Clip als `byte[]` wäre doppelter Speicher und bei langen
  Aufnahmen ein OOM-Kandidat.
- **Der Transfer-Timeout wird für den Streaming-Pfad von *gesamt* auf *untätig* umgestellt.**
  `FtpClient` setzt heute einmalig `CancelAfter(30 s)` über den ganzen Transfer; bei
  450-KB-JPEGs fällt das nie auf, ein längerer Clip würde mitten im Laden abgebrochen. Der
  Streaming-Pfad kopiert darum in einer eigenen Schleife und setzt die Frist nach **jedem**
  gelesenen Block neu: eine tote Verbindung fliegt weiterhin nach 30 s auf, ein langer
  gesunder Transfer läuft durch. Dieselbe Schleife meldet den Fortschritt.
  `DownloadAsync` bleibt unverändert bei der Gesamtfrist.
  Der Fortschritt wird als **bisher übertragene Bytes** gemeldet, nicht als Prozentwert:
  die Gesamtgröße steht bereits in `MediaItem.Size` aus dem Verzeichnislisting, den
  Prozentwert bildet die Oberfläche daraus. So braucht der FTP-Client keine Kenntnis der
  erwarteten Größe.
- **`ServicePointManager.DefaultConnectionLimit` von 3 auf 4.** Ein laufender Clip-Transfer
  belegt sonst dauerhaft einen der drei Kanäle und drosselt die Thumbnail-Warteschlange,
  während man zusieht. Die Tore in `MediaRepository` (3) und `ThumbnailLoader` (3) bleiben.

### `MediaFileCache`

Thumbnail- und Voll-JPEG-Cache bleiben unverändert. Clips brauchen einen **Pfad** statt
Bytes (der Player öffnet eine Datei), darum neu unter
`%LocalAppData%\ScreenshotGallery\cache\video\<TitleID>\<name>.mp4`:

- Geladen wird nach `<name>.mp4.part`, erst nach vollständigem Transfer umbenannt. Ohne das
  hinterlässt ein Abbruch eine halbe Datei, die als Treffer gilt und danach dauerhaft nicht
  abspielbar ist.
- **Obergrenze 4 GB**, anders als beim bewusst dummen Thumbnail-Cache: nach jedem
  erfolgreichen Download wird getrimmt, älteste Zugriffszeit zuerst, die gerade benötigte
  Datei ausgenommen. 19 s Clip sind 15 MB — eine Stunde Aufnahme wären knapp 3 GB;
  unbegrenztes Wachstum ist hier ein reales Plattenproblem, kein theoretisches.
- `Remove` beim Löschen auf der Konsole, wie bei den anderen Caches.

## Oberfläche

### Galerie und Play-Zeichen

Der `ThumbnailLoader` zeichnet nach dem Dekodieren ein Play-Zeichen in die Bitmap, wenn
`Kind == Video`: halbtransparenter dunkler Kreis mit weißem Dreieck, Größe relativ zur
kürzeren Kante, kantengeglättet. Das geschieht auf dem Original (440×248), bevor die Galerie
auf die Kachelgröße skaliert — das Symbol bleibt damit proportional, unabhängig von
`ImageSize`. Ein fehlgeschlagenes Thumbnail bleibt stiller Platzhalter wie bisher.

Sonst ändert sich an der Galerie nichts: Caption bleibt der Aufnahmezeitpunkt, Description
die Dateigröße, Sortierung neueste zuerst. Clips reihen sich an ihrer Stelle in die
Zeitleiste des Spiels ein. In `MainForm` werden die Casts `(Screenshot)item.Tag` zu
`(MediaItem)item.Tag`.

### Kontextmenü

- **Herunterladen…** — eine Schleife, die je Eintrag auf `Kind` verzweigt: Bilder wie heute
  über `byte[]`, Clips direkt per `DownloadToFileAsync` in den gewählten Ordner. Fortschritt
  in der Statuszeile („Clip 1 von 2 — 42 %"). Gemischte Auswahl braucht keinen Sonderfall.
  Teilfehler werden am Ende gesammelt gemeldet wie bisher.
- **In Zwischenablage kopieren** — Bilder wie heute per `Clipboard.SetImage`. Ein Clip kann
  kein Bild in der Zwischenablage sein; er wird in den Cache geladen und als
  `Clipboard.SetFileDropList` abgelegt, also einfügbar in Explorer, Chat oder Mail. Der
  Eintrag bleibt bei genau einer Auswahl aktiv.
- **Vorschau öffnen** und **Doppelklick** — unveränderter Einstieg; das Vorschaufenster
  entscheidet nach Sorte.
- **Löschen…** — unverändert, mit sortenabhängigen Pfaden; zusätzlich fliegt der Clip aus
  dem `MediaFileCache`.

### Vorschaufenster mit zwei Betriebsarten

Ein Fenster, eine Blätterlogik über die gemischte Gruppe. In der Client-Fläche liegen zwei
Kinder, von denen je nach Sorte eines sichtbar ist: die bestehende `PreviewCanvas` für
Bilder und ein neues `VideoView`.

`VideoView` besteht aus einem `ElementHost` (`Dock = Fill`) mit der WPF-`MediaElement`
(`LoadedBehavior = Manual`, `Stretch = Uniform`) und einer unten angedockten, geskinnten
Transportleiste als gewöhnliches WinForms-Control. Weil sich die beiden Rechtecke nicht
überlappen, entsteht kein Airspace-Problem — der Skin bricht nur dort, wo ohnehin Video
läuft.

Die Transportleiste enthält Play/Pause als `SkinButton`, die Zeitanzeige „0:05 / 0:19" und
einen neuen **`SeekBar : SkinnedControl`** (Rille, Fortschritt, Griff, Klick und Ziehen).
Das UIFramework hat keinen Positionsregler; `SkinScrollBar` ist ein Scrollbalken und wäre
zweckentfremdet. Ein `Timer` (250 ms) hält den Regler nach und wird beim Verlassen der
Video-Betriebsart und beim Schließen gestoppt.

Meldet `MediaElement.NaturalDuration` keine `TimeSpan` (Container ohne verwertbare
Dauerangabe), wird der Regler deaktiviert und die Zeitanzeige zeigt nur die verstrichene
Zeit — abspielen bleibt möglich, nur Springen nicht. Ein `MediaFailed` landet als
Klartextmeldung im Fenstertitel, auf demselben Weg wie ein fehlgeschlagenes Bild heute;
kein Dialog, kein Absturz.

Ablauf beim Öffnen eines Clips: Cache-Treffer → sofort abspielen; sonst laden mit
Fortschrittsanzeige, dann abspielen. Zwei Fallen, die der bestehende Code bereits einmal
hatte und die hier wieder auftreten:

- Die vorhandene `_generation`-Wache muss **den Download mit umschließen**. Wer während eines
  langen Transfers weiterblättert, darf nicht Minuten später den alten Clip gestartet
  bekommen.
- Beim Wegblättern und beim Schließen `Stop()` + `Close()` auf der `MediaElement`, sonst
  läuft der Ton des vorigen Clips weiter. `ElementHost` wird mit dem Fenster entsorgt.

Tastenbelegung: **Links/Rechts** blättert durch die Gruppe (für Bilder und Clips gleich),
**Leertaste** Play/Pause, **Umschalt+Links/Rechts** springt ±10 s, **Esc** schließt. Die
Pfeiltasten bei Videos aufs Springen umzubiegen würde die Bedienung sortenabhängig machen —
genau die Inkonsistenz, die bei gemischten Gruppen stört.

## Projektverweise

`ScreenshotGallery.App.csproj` bekommt `PresentationCore`, `PresentationFramework`,
`WindowsBase`, `WindowsFormsIntegration` und `System.Xaml` — alle im .NET Framework 4.8
enthalten, kein NuGet. `Program.cs` trägt `[STAThread]` bereits.

## Tests (`ScreenshotGallery.Core.Tests`, xunit)

Weiter reine Core-Tests im bestehenden Muster mit `FakeFtpClient`, plus Umbenennungen
(`PhotoRepositoryTests` → `MediaRepositoryTests`, `ScreenshotTests` → `MediaItemTests`):

- `Ps4Paths`: Medien-, `.dat`- und Thumbnail-Pfad für beide Sorten, geprüft gegen die am
  2026-08-15 real erhobenen Pfade.
- `MediaRepository`: gemischte, nach Zeit absteigend sortierte Liste; Spiel mit
  ausschließlich Clips; Spiel mit ausschließlich Screenshots; `.dat` taucht nicht als Clip
  auf; Löschen trifft Mediendatei, `.dat` und Thumbnail im richtigen Zweig.
- `ParseTimestamp` mit einem `.mp4`-Namen.
- `MediaFileCache`: `.part`-Umbenennung, Treffer/Fehltreffer, Trimmen aufs Budget (älteste
  Zugriffszeit zuerst, aktuelle Datei ausgenommen).
- `FakeFtpClient` bekommt `DownloadToFileAsync`.

Nicht per Unit-Test abgedeckt und ehrlich als Fahrprobe an der Konsole geführt: der
Untätigkeits-Timeout beim Streamen und der Player selbst (Abspielen, Pause, Springen,
Blättern zwischen Bild und Clip, Ton beim Wegblättern).

## Nicht-Ziele dieser Runde

Kein Streamen direkt von der Konsole in den Player (erst laden, dann abspielen), keine
Lautstärke- oder Vollbildsteuerung im Player, kein Filter-Umschalter in der Toolbar. Die
Nicht-Ziele der v1-Spec gelten unverändert weiter: kein Upload auf die PS4, kein Bearbeiten
der PS4-Galerie-Datenbank, keine Verwaltung mehrerer Konsolen, kein Umbenennen, keine
Diashow.

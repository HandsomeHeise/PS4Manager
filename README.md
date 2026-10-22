# PS4 Manager

`PS4Manager.exe` ist die Anwendung: ein Dashboard, das die Verbindung zu einer
jailbroken PS4 über den FTP-Server von **GoldHEN** besitzt und Werkzeuge als
eigene Fenster öffnet — den **File Manager** (Dateien wie im Windows Explorer),
**Screenshots & Videos** (Galerie mit Vorschau und Player), den
**GoldHEN Manager**, **PS4 Health**, den **Frame4 Debugger**, den
**PRX Manager**, den **Luma Script Manager**, die **Backup Timeline** und den
**PKG Manager**, die **Mod Library** und **Games**. Die Oberfläche verwendet
DevExpress WinForms 25.2 mit einem PS4-Dashboard, Ribbon und Hell-/Dunkel-Theme. Alle
FTP-Werkzeuge teilen sich **eine** FTP-Verbindung, **eine** Warteschlange und
**einen** Satz Einstellungen: Ein Download, der in einem Werkzeug beginnt, ist
auch in den anderen sichtbar.

- **Verbinden:** IP und Port (GoldHEN-FTP, Standard `2121`) im Dashboard
  eintragen, dann „Connect". Nur das Dashboard verbindet und trennt; die
  Werkzeugfenster fordern es lediglich an („Connect" im File Manager). Sie
  dürfen offen bleiben, während getrennt ist, und bedienen sich nach dem
  nächsten Verbinden selbst.
- **Werkzeuge:** Jede Kachel öffnet ein Fenster; ein zweiter Klick holt das
  vorhandene Fenster nach vorn statt ein zweites zu öffnen. Ein
  Werkzeugfenster zu schließen trennt die Konsole nicht und stoppt keine
  fremden Übertragungen.
- **Übertragungen:** Die Leiste unten zeigt den laufenden Auftrag mit
  Fortschritt, Rate und der Zahl der wartenden Aufträge. „Transfers …" springt
  in die Übertragungsleiste des File Managers, der die gemeinsame
  Warteschlange anzeigt.
- **Beenden:** Das Schließen des Dashboards schließt die Anwendung. Vorher
  erscheint die gesammelte Warnung, wenn Übertragungen laufen oder Dateien
  noch nicht auf die PS4 zurückgeschrieben sind.
- **Frame4-Werkzeuge:** Frame4 Debugger und PRX Manager nutzen die in den
  Einstellungen hinterlegten Frame4-Ports und sind auch ohne FTP-Sitzung
  erreichbar.

Einstellungen, Datenpfade und die Übernahme aus den früheren Einzel-Apps stehen
in [docs/ps4-manager-migration.md](docs/ps4-manager-migration.md).

## Werkzeuge

### Online-Mod-Bibliothek: Server

Unter `server/` liegt der separat betreibbare Katalog- und Update-Server mit
geschützter Admin-Oberfläche, exakter Spielversions-Kompatibilität, signierten
Paketen, Docker-Deployment sowie Backup und Restore. Einrichtung und Betrieb:
[server/OPERATIONS.md](server/OPERATIONS.md).

Die Mod Library im Client bietet kompatible Releases, Installation,
Installationsbelege, Wiederaufnahme, Rollback und Deinstallation auf der PS4.

### File Manager

- **Verbinden:** „Select console …" im File Manager bittet das Dashboard um die
  Verbindung; verbunden und getrennt wird immer dort.
- **Links** Schnellzugriff (GoldHEN, Plugins, Cheats, PKG-Ablage, Spiele,
  Spielstände, USB, angeheftete Ordner) und der Ordnerbaum der Konsole,
  **rechts** Details oder Kacheln. Spielordner zeigen den Spielnamen.
- **Übertragen wie im Explorer:** Ziehen und Strg+C/Strg+V in beide
  Richtungen mit dem Windows Explorer. Innerhalb der PS4 verschieben per
  Ziehen oder Strg+X/Strg+V — Kopieren innerhalb der PS4 gibt es noch nicht.
- **Öffnen mit Rückschreiben:** Doppelklick öffnet `.ini`-Dateien im
  eingebauten Editor. Andere Dateien werden im zugeordneten Windows-Programm
  geöffnet; nach dem Speichern fragt der File Manager, ob die Änderung zurück
  auf die PS4 soll.
- **Schutzzonen:** frei (`/data`, USB-Sticks, Aufnahmen), Warnung (Spiele,
  Spielstände, Patches, Zusatzinhalte — mit Rückfrage), alles andere
  gesperrt. Der Expertenmodus unter „View" stuft „gesperrt" zu „Warnung"
  herab.
- **Die PS4 hat keinen Papierkorb** — Löschen ist endgültig.

### GoldHEN Manager

- **Automatische Erkennung:** Das Werkzeug sucht `/data/GoldHEN`, die Ordner
  für Plugins, Cheats und Patches sowie vorhandene `plugins.ini`, `config.ini`
  und `plugins.txt`. Fehlende Ordner werden als nicht vorhanden angezeigt.
- **Plugins ein- und ausschalten:** Der Schalter ändert nur die erkannte Zeile
  der zuerst gefundenen Konfiguration. Ohne erkannte Konfiguration bleiben die
  Zustände unbekannt, der Schalter ist deaktiviert und die Datei lässt sich
  stattdessen als Text öffnen.
- **Cheats und Patches:** Dateien werden nach Titel-ID gruppiert und, soweit
  bekannt, mit dem Namen des Spiels angezeigt.
- **Dateiaktionen:** Dateien lassen sich hochladen, ersetzen, herunterladen,
  löschen und im eingebauten Editor öffnen. Die Aufträge laufen durch dieselbe
  Warteschlange wie in den anderen Werkzeugen.
- **Sicherung vor jeder Änderung:** Vor Umschalten, Ersetzen, Löschen oder
  Zurückschreiben legt der Manager eine lokale Kopie samt Manifest unter
  `%LocalAppData%\PS4Manager\backups\goldhen` an. Scheitert die Sicherung,
  unterbleibt die Änderung.

### PS4 Health

- **Statusprüfung:** Prüft die gemeinsame FTP-Sitzung, GoldHEN-Ordner,
  Konfigurationsdatei, Plugin-/Cheat-/Patch-Ordner, Plugin-Zustände,
  Transferstatus und offene Editor-Sitzungen.

### Frame4 Debugger und PRX Manager

- **Frame4 Debugger:** Prozessliste, Prozessdetails, Maps, Speicher lesen,
  AOB-Suche, Console-Info, UART-Log und Expertenaktionen.
- **PRX Manager:** Fokussiertes Fenster zum Auflisten von Prozessen und PRX-
  Modulen sowie zum Laden und Entladen einer PRX über Frame4.

### Luma Script Manager

- **Live-Lua:** Verbindet sich mit Lumas Control-Channel und führt Lua im
  laufenden Spiel aus. GTA V nutzt standardmäßig Port `9615`, Red Dead
  Redemption 2 Port `9616`.
- **Profile:** Die Spielauswahl setzt den passenden Port; Host und optionaler
  Token werden gespeichert. Der Host ist dieselbe PS4-Adresse wie im
  Dashboard.
- **Befehle:** Das Fenster unterstützt Connect/Disconnect, Token-Handshake,
  Script-Reload, Lua-Ausführung, Resource-Liste und eingehende Log-Zeilen.
- **Hinweis:** Luma bedient nur einen Control-Client gleichzeitig. Wenn VSCode
  noch verbunden ist, zuerst dort trennen.

### Backup Timeline

- **GoldHEN-Sicherungen:** Listet die lokalen Sicherungen unter
  `%LocalAppData%\PS4Manager\backups\goldhen` mit Quelle, Operation, Größe und
  Hash.
- **Restore:** Spielt eine ausgewählte Sicherung zurück auf den ursprünglichen
  PS4-Pfad. Vor dem Restore wird der aktuelle Remote-Stand erneut gesichert
  und der Upload gegen zwischenzeitliche Änderungen geschützt.

### PKG Manager

- **Remote-PKGs:** Scannt `/data/pkg`, `/mnt/usb0` und `/mnt/usb1` nach
  `.pkg`-Dateien und zeigt Name, Ablageort, Größe, Änderungsdatum und Pfad.
- **Transfers:** Lokale `.pkg`-Dateien lassen sich in die interne PKG-Ablage
  oder auf USB hochladen; ausgewählte Remote-PKGs lassen sich herunterladen
  oder löschen. Alle Aufträge laufen über dieselbe Warteschlange wie im File
  Manager.

### Auto Update

- **Updates-Fenster:** Das Dashboard-Ribbon öffnet „Updates". Dort werden
  Manifest-URL und Kanal (`stable` oder `preview`) gespeichert, dann kann nach
  einer neueren Version gesucht und das Update installiert werden.
- **Server-Dateien:** Der Server stellt eine HTTPS-`manifest.json` und ein
  ZIP-Paket bereit. Das ZIP enthält die neuen Programmdateien direkt an der
  Stelle, an die sie im Release-Ordner kopiert werden sollen.
- **Manifest-Format:** Erwartet werden `version`, `channel`, `url`, `sha256`,
  optional `notes` und optional `signature`. HTTPS und SHA-256 werden immer
  geprüft. Eine RSA-Signatur wird zusätzlich erzwungen, sobald ein öffentlicher
  Schlüssel in den Einstellungen hinterlegt ist.
- **Installation:** Die App lädt das Paket nach
  `%LocalAppData%\PS4Manager\updates`, prüft den Hash und startet dann den
  mitgelieferten `PS4Manager.Updater.exe`, der nach dem Beenden der App die
  Dateien austauscht und PS4 Manager wieder startet.

### Screenshots & Videos

- **Screenshots und Videos gemeinsam**: Beide Zweige der Konsole
  (`/user/av_contents/photo` und `/user/av_contents/video`) landen in
  derselben Spielgruppe, gemeinsam nach Aufnahmezeit sortiert. Clips sind
  am Play-Zeichen auf der Kachel zu erkennen; ihr Vorschaubild liefert die
  PS4 selbst mit.
- **Eingebauter Player**: Ein Doppelklick auf einen Clip lädt ihn und spielt
  ihn im Vorschaufenster ab — mit Play/Pause (Leertaste), Positionsregler,
  Zeitanzeige und ±10 s über Umschalt+Pfeil. Links/Rechts blättert wie bei
  den Bildern durch die Gruppe.
- **Mehrfachauswahl** der Kacheln über ein Kontextmenü: *Herunterladen…*,
  *In Zwischenablage kopieren*, *Vorschau öffnen* und *Löschen…* (entfernt
  Datei, `.dat`-Begleitdatei und Thumbnail von der PS4 — siehe Hinweis unten).
- **Lokaler Cache** für Vorschaubilder, Vollbilder und Clips.

## Voraussetzungen

- Eine PS4 mit installiertem **GoldHEN** und laufendem FTP-Server im
  selben Netzwerk.
- Für die Entwicklung: **DevExpress WinForms 25.2.4** inklusive gültiger
  Entwicklerlizenz. Ein separates UI-Repository wird nicht benötigt.
  Endnutzer erhalten die benötigten Laufzeit-DLLs mit der Client-ZIP.
- **.NET Framework 4.8** (Zielplattform, siehe `Directory.Build.props`) sowie
  ein aktuelles **.NET SDK**, um die `dotnet`-Kommandozeile zu nutzen.
- Für den eingebauten Player die Medienkomponenten von Windows (`wmp.dll`),
  auf einer normalen Windows-10/11-Installation vorhanden. Auf N-Editionen
  ohne Media Feature Pack bleibt das Vorschaufenster bei Clips schwarz und
  meldet den Fehler im Fenstertitel; Herunterladen und Löschen funktionieren
  davon unabhängig.

## Bauen

```bash
dotnet build -c Release
```

`Directory.Build.targets` verwendet standardmäßig die Installation unter
`C:\Program Files\DevExpress 25.2\Components\Bin\Framework` und den mitgelieferten
Lizenzgenerator aus den Offline-Paketen. Andere Installationen lassen sich über
`-p:DevExpressInstallDir=...`, `-p:DevExpressPackageVersion=...` und bei Bedarf
`-p:DevExpressLicenseGeneratorPath=...` auswählen. Der persönliche Lizenzschlüssel
bleibt in der vom DevExpress-Installer verwalteten Entwicklerumgebung und gehört
nicht ins Repository oder ins ZIP. DX1000/DX1001 weisen auf einen Trial-/fehlenden
Lizenzstatus hin; solche Builds sind nur zur lokalen Evaluierung bestimmt.

Tests laufen über:

```bash
dotnet test PS4Manager.sln
```

Die Solution enthält eine Anwendung — `PS4Manager.exe` —, den kleinen
Update-Helfer `PS4Manager.Updater.exe` und sieben
Bibliotheken: `PS4Manager.Core` (gemeinsame Sitzung, Einstellungen,
Migration), `FileManager.Core` (Dateimanager-Kern), `PS4Manager.Gallery.Core`
(Galerie: Medienerkennung, Vorschaubilder, Caches), `Ps4.Core` (FTP und
Titel-Infos) sowie `FileManager.App` (Fenster des File Managers) und
`PS4Manager.Gallery.App` (Fenster der Galerie) sowie `PS4Manager.UI`
(DevExpress-Komponenten und anwendungsspezifische Datenbindungen). Die UI-Projekte sind
Bibliotheken, keine Programme: `PS4FileManager.exe` und
die frühere `PS4ScreenshotGallery.exe` gibt es nicht mehr.

Nach dem Bauen liegt die Anwendung unter:

```text
src/PS4Manager.App/bin/Release/net48/PS4Manager.exe
```

## Lokale Daten

### Mod Library

#### Gemeinsame Meldungen und Desktop-Benachrichtigungen

Dialoge für Informationen und Entscheidungen folgen dem aktiven DevExpress-Theme.
Lange Meldungen bleiben scrollbar und kopierbar; die Plugin-Aktivierung bleibt eine
ausdrückliche Auswahl mit **Later** als sicherer Vorauswahl.

Ergebnisse der gemeinsamen Auftragswarteschlange erscheinen als eigene Karten unten
rechts über der Taskleiste, auch bei minimiertem Manager. Erfolg und Information
verschwinden nach sechs sichtbaren Sekunden (Pause bei Mauszeiger/Fokus); Warnungen
und Fehler bleiben schließbar. **Details** zeigt den gespeicherten vollständigen
Meldungstext samt Fehlerpfaden, auch nach dem Leeren oder Fortsetzen eines Auftrags.
**Open Transfers** öffnet auf Wunsch die Transferansicht der weiterhin aktuellen Sitzung.
Es gibt maximal drei sichtbare Karten, eine begrenzte Warteschlange und
bei vielen Ergebnissen einen Sammelhinweis. Mod-Abschlussdialoge erzeugen keine
zusätzliche gleichlautende Erfolgskarte. Beim Konsolenwechsel werden alte Karten entfernt.

Diese Karten sind keine nativen Windows-Benachrichtigungen: kein Eintrag im
Benachrichtigungscenter, keine automatische Übernahme von „Nicht stören“, kein Ton
und keine Anzeige nach dem Beenden des Managers. Keine Meldung bestätigt automatisch
eine Installation, Löschung oder Aktivierung.

Eine portable Client-ZIP lässt sich nach dem Release-Build mit
`scripts/package-client.ps1 -OutputPath C:\Ausgabe\PS4Manager.zip` erstellen.
Das Skript überschreibt keine vorhandene ZIP und prüft Größe und SHA256 aller 26
in `scripts/client-runtime.json` freigegebenen Laufzeitdateien. Zugangsdaten,
Entwickler-Lizenzdateien und Benutzereinstellungen werden nicht eingepackt.

Unter **Start → Settings** lässt sich Frame4 für Debugger und Trainer aktivieren.
**View** wechselt das Theme für alle Fenster. Die neue ZIP in einen eigenen Ordner
entpacken; die vorhandenen Daten unter `%LocalAppData%\PS4Manager` bleiben erhalten.

#### Verwendung

Das Dashboard öffnet die **Mod Library** als eigenes Fenster. Nach dem Verbinden
`Refresh games` wählen: Der Katalog zeigt ausschließlich Veröffentlichungen für
die erkannte CUSA-ID und exakte `APP_VER`. Fehlende oder ungültige Versionsdaten
sperren neue Installationen; Prozessnamen ersetzen keine Kompatibilitätsprüfung.
Extern installierte Titel werden auch bei geschlossenem Spiel über die dauerhaft
gespeicherten `appmeta/external`-Metadaten erkannt. Ein lesbarer Spielname bleibt
sichtbar, selbst wenn die Version fehlt; eine fehlende Version sperrt weiterhin
die Installation.

Vor Schreibzugriffen unter `Console profile` die richtige physische Konsole
bestätigen. Die Bestätigung gilt nur für diese Verbindung, nicht dauerhaft für
eine IP-Adresse. `Review install` lädt das Paket, prüft den fest eingebauten
Mod-Signaturschlüssel, SHA-256 und ZIP/Manifest und sichert vorhandene Zieldateien.
Der Bestätigungsdialog zeigt die konkreten Pfade. Zielordner müssen bereits
existieren; bei Bedarf vorher im File Manager anlegen.

Installieren, Aktualisieren und Wiederherstellen laufen in der gemeinsamen
Transfer-Warteschlange. HTTPS-Vorbereitung wird im Bibliotheksstatus angezeigt.
Vor jedem Schreibvorgang das Spiel schließen. Nach erfolgreicher PRX-Installation
(auch nach `Resume`) fragt ein Dialog nach der GoldHEN-Aktivierung. `Activate now`
sichert `plugins.ini` und aktiviert die installierten Plugins nur für die CUSA-ID
dieser Installation. `Later` lässt die Konfiguration unverändert; die Aktivierung
ist anschließend im GoldHEN Manager möglich. PRX-Dateien werden **nicht** direkt
geladen: danach das Spiel starten bzw. neu starten. Fehler bei der optionalen
Aktivierung lassen die erfolgreiche Installation bestehen. Konfigurationen, die
bereits zu einem installierten oder unfertigen Mod-Paket gehören, werden nicht
automatisch geändert. Nicht im Paket enthaltene
Lua-Dateien werden nicht erfunden oder nachgeladen. Nur erfolgreich installierte
`luma-script`-Pakete bieten einen ausdrücklich ausgelösten Skript-Reload an.

`Installed & recovery` zeigt lokale Installationen und unterbrochene Vorgänge.
`Resume` prüft den Remotezustand und erneut die Veröffentlichung; `Roll back`
stellt den unmittelbaren Vorgänger wieder her, `Uninstall` den ursprünglichen
Stand vor der ersten Installation. Rollback/Deinstallation funktionieren ohne
Katalogserver. Geänderte Nutzerdateien oder beschädigte Sicherungen blockieren
automatische Wiederherstellung. Unbekannte/angefangene Staging-Dateien werden
zur Prüfung behalten, nicht blind überschrieben. Nach einem Programmabbruch
wird nichts automatisch auf die Konsole geschrieben.

Originale werden durch Umbenennen der auf der Konsole aufbewahrten Originaldatei
wiederhergestellt. Fehlt diese Datei, wird nicht automatisch auf einen lokalen
FTP-Readback zurückgegriffen: Auch zwei identische Leseantworten beweisen nicht,
dass ein FTP-Server richtige Bytes geliefert hat. Unvollständige oder unbekannte
LIST-Zeilen stoppen Mod-Schreibvorgänge. Unterbrochene Mod-Aufträge werden nur
über `Installed & recovery`, nicht über das allgemeine Transfer-`Resume`, fortgesetzt.

Offline-Katalogdaten sind mit Zeitstempel lesbar, aber nicht installierbar.
Cache, Journale und inhaltsadressierte Sicherungen liegen unter
`%LocalAppData%\PS4Manager\mods`, getrennt nach lokalen Konsolenprofilen.
Sicherungen und versteckte `.ps4m-*`-Original-/Staging-Dateien auf der Konsole
bleiben erhalten; es gibt keine automatische Bereinigung. Eine Zuordnung zur
Konsole wird nicht an den Katalogserver gesendet. Reale GoldHEN-FTP-Installation
und Wiederherstellung benötigen weiterhin einen separat freigegebenen
Hardwaretest auf einem entbehrlichen Nicht-Spiel-Ziel.

**PS4 Manager** legt alles unter `%LocalAppData%\PS4Manager` ab:
`settings.json` (Endpunkt, Expertenmodus, Theme und die Ansicht des File
Managers), `open\` (Arbeitskopien fürs Öffnen mit Rückschreiben, beim Start
geleert), `cache\gallery\` (Vorschaubilder), `cache\gallery\full\`
(Vollbilder) und `cache\gallery\video\` (Clips, mit 4-GB-Grenze: der Cache
wirft nach jedem Download die am längsten ungenutzten Clips weg, bis die
Grenze wieder eingehalten ist) sowie `backups\goldhen\` (lokale Sicherungen
vor Änderungen durch den GoldHEN Manager) und `updates\` (heruntergeladene
Update-Pakete).

Beim ersten Start übernimmt er IP, Port und Ansicht aus den früheren
Einzel-Apps; was dabei übernommen wird und was nicht, steht in
[docs/ps4-manager-migration.md](docs/ps4-manager-migration.md). Jene Ordner
gibt es nicht mehr als Programme, ihre Dateien aber bleiben liegen:
`%LocalAppData%\ScreenshotGallery` (Einstellungen, Thumbnail-Cache) und
`%LocalAppData%\PS4FileManager` (Einstellungen, Arbeitskopien). Gelesen wird
davon nur `settings.json` — und auch das nur einmal, beim Umstieg.

## Hinweis zur PS4-Galerie-Datenbank

Screenshots, die über dieses Tool per FTP gelöscht werden, verschwinden
sofort aus der Ansicht dieses Tools und vom Dateisystem der Konsole —
**nicht aber sofort aus der PS4-eigenen Foto-Galerie-App**. Die Konsole
pflegt dafür eine eigene Datenbank, die nur beim nächsten internen Scan
(z. B. Neustart der Galerie-App oder der Konsole) aktualisiert wird.
Bis dahin können verwaiste Einträge in der PS4-Galerie sichtbar bleiben;
das ist erwartetes Verhalten und kein Fehler des Tools.

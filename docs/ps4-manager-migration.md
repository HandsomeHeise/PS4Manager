# Umstieg auf PS4 Manager

`PS4Manager.exe` fasst **PS4 File Manager** und **PS4 Screenshot Gallery** in
einer Anwendung zusammen: ein Dashboard, das die Verbindung zur Konsole
besitzt, und darin beide Werkzeuge als eigene Fenster. Die beiden früheren
Einzel-Programme gibt es nicht mehr; ihre Dateien liegen aber weiterhin auf
der Platte, und nichts davon wird beim Umstieg gelöscht oder verändert.

## Was beim ersten Start übernommen wird

`%LocalAppData%\PS4Manager\settings.json` entsteht beim ersten Start aus den
Alt-Dateien, in dieser Reihenfolge:

1. `%LocalAppData%\PS4FileManager\settings.json` (File Manager) — wenn
   vorhanden, gewinnt diese Datei: IP, Port, Expertenmodus sowie Ansicht
   (Details oder Kacheln), letzter Ordner, angeheftete Ordner, Spaltenbreiten
   und Baumbreite.
2. sonst `%LocalAppData%\ScreenshotGallery\settings.json` (Gallery) — daraus
   nur IP und Port.

Beide Alt-Dateien bleiben unverändert liegen. Der Import läuft nur, solange
`%LocalAppData%\PS4Manager\settings.json` **fehlt**; ist sie vorhanden, gilt
allein sie. Scheitert der erste Import (kein Schreibrecht, Platte voll),
startet der Manager mit Voreinstellungen und versucht es beim nächsten Start
erneut.

Eine Ausnahme: Scheitert der Import, und gelingt **danach** ein Speichern
(z. B. nach einer erfolgreichen Verbindung oder beim Beenden), liegt eine
Datei vor — der Import wird dann nicht mehr wiederholt, obwohl in ihr
`MigrationCompleted: false` steht. Wer die Alt-Daten dann noch übernehmen
will, löscht `%LocalAppData%\PS4Manager\settings.json` und startet den
Manager neu. (Die Datei trägt den Endpunkt ohnehin auch, sodass dabei nichts
verloren geht, was nicht in den Alt-Dateien steht.)

Wer von vorn beginnen will, löscht `%LocalAppData%\PS4Manager\settings.json`;
beim nächsten Start wird erneut aus den Alt-Dateien importiert.

## Was (noch) nicht übernommen wird

- **Der Galerie-Cache** (`%LocalAppData%\ScreenshotGallery\cache`) bleibt
  liegen, wird aber noch nicht weiterverwendet: Der Manager legt seinen eigenen
  Cache unter `%LocalAppData%\PS4Manager\cache\gallery` an, der beim ersten
  Besuch neu gefüllt wird. Den alten Cache als Lesebestand heranzuziehen und
  Einträge nach und nach zu übernehmen, gehört zum Plan „Abschluss der
  Datenmigration".
- **Favoriten, Suche und Filter der Galerie** gibt es noch nicht (eigener
  Plan) — entsprechend gibt es nichts zu übernehmen.
- **Backup & Restore und Diagnose** sind noch nicht gebaut; die zugehörigen
  Kacheln im Dashboard nennen nur ihren geplanten Umfang. Der GoldHEN Manager
  ist bereits verfügbar und legt vor jeder Änderung eine lokale
  Sicherheitskopie an.

## Datenpfade des Managers

Alles unter `%LocalAppData%\PS4Manager`:

- `settings.json` — Endpunkt, Expertenmodus, Theme und die Ansicht des File
  Managers (letzter Ordner, Spaltenbreiten, angeheftete Ordner).
- `open\` — Arbeitskopien fürs Öffnen mit Rückschreiben; beim Start geleert.
- `cache\gallery\` — Vorschaubilder.
- `cache\gallery\full\` — Vollbilder.
- `cache\gallery\video\` — Clips, mit derselben 4-GB-Grenze wie bisher.
- `backups\goldhen\` — Sicherheitskopien und Manifeste, die der GoldHEN
  Manager vor jeder Änderung anlegt.

Die früheren Einzel-Apps gibt es nicht mehr. Ihre Ordner
(`%LocalAppData%\PS4FileManager`, `%LocalAppData%\ScreenshotGallery`) bleiben
als Import-Quelle liegen; geschrieben wird nur noch nach
`%LocalAppData%\PS4Manager`.

## Was der Manager anders macht

- **Eine Verbindung.** Nur das Dashboard verbindet und trennt. Beide
  Werkzeugfenster konsumieren dieselbe Sitzung: eine FTP-Verbindung, ein
  Wächter um den FTP-Client, eine Warteschlange. Ein Auftrag, der in der
  Galerie beginnt, erscheint im File Manager und umgekehrt.
- **Eine Warnung beim Verlassen.** Schließen des Dashboards, Trennen und
  Neuverbinden fragen gesammelt nach: laufende Übertragungen und noch nicht
  zurückgeschriebene Dateien werden in einem Dialog genannt.
- **Fenster statt Neuaufbau.** Jede Kachel öffnet ein Fenster; ein zweiter
  Klick holt das vorhandene nach vorn. Ein Werkzeugfenster zu schließen trennt
  die Konsole nicht. Beim Schließen des Dashboards schließt die Anwendung.
- **Theme und Ansicht werden gespeichert.** Das Dashboard schreibt die
  Einstellungen beim Beenden und nach jeder erfolgreichen Verbindung; die
  Umschalter im File Manager (hell/dunkel, Details/Kacheln, Expertenmodus)
  wirken damit dauerhaft.

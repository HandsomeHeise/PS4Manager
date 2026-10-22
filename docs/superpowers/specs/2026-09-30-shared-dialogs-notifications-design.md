# Einheitliche Dialoge und Desktop-Benachrichtigungen

Stand: 30.09.2026. Richtung und schriftliche Spezifikation wurden im Chat bestätigt. Der nächste Schritt ist die Prüfung des Implementierungsplans.

## Ziel und Umfang

Der PS4 Manager soll Meldungen im aktiven UIFramework-Design anzeigen. Entscheidungen bleiben ausdrückliche Dialoge; Statusmeldungen erscheinen zusätzlich als eigene kleine Desktop-Fenster unten rechts. Die gemeinsame Lösung ist nicht auf die Mod Library beschränkt.

Erste Integration: Abschluss- und Fehlermeldungen der Mod Library sowie Ergebnisse der gemeinsamen TransferQueue, die auch andere Manager-Bereiche verwenden. Bestehende Rückfragen und Sicherheitsprüfungen bleiben erhalten. Andere Statusanzeigen werden nicht pauschal durch Popups ersetzt.

Eigene Desktop-Fenster sind keine nativen Windows-Toasts: kein Windows-Benachrichtigungscenter, keine Anzeige nach dem Beenden des Managers und keine automatische Übernahme der Windows-Einstellung „Nicht stören“. Es gibt zunächst keine Sounds, Hintergrunddienste oder zusätzliche Windows-Registrierung.

## Aufbau

Wir erweitern die bereits gemeinsam verwendete UI-Schicht in `FileManager.App`, statt ein neues UI-Paket einzuführen oder das separate UIFramework-Projekt zu ändern.

- `Dialogs/MessageDialog`: bestehende öffentliche Aufrufe bleiben kompatibel. Ergänzungen für Meldungsart, umbrochenen Text, begrenzte Fenstergröße und scrollbare Details.
- `Notifications`: gemeinsame Benachrichtigungsdaten, ein gestaltetes Fenster und ein zentraler, entsorgbarer Dienst für Positionierung, Warteschlange und Lebensdauer. Keine Kenntnis von FTP, Mods oder Konsolenschreibzugriffen.
- `PS4Manager.App`: das Dashboard besitzt genau einen Dienst und bindet die aktuelle TransferQueue an. Die Mod Library verwendet denselben Dienst und die gemeinsamen Dialoge.
- Testbare Zustands- und Anordnungslogik bleibt von echten Desktop-Fenstern getrennt; UI-Integration wird zusätzlich auf einem STA-Thread geprüft.

Alternativen: Native Windows-Toasts passen optisch nicht vollständig zum Framework und erfordern Windows-spezifische Integration. Getrennte Popups pro Fenster würden mehrfachen Code und doppelte Meldungen erzeugen. Daher die zentrale Lösung im bestehenden Client.

## Dialoggestaltung

Farben, Schrift, Abstände und Buttons folgen dem aktiven Skin, einschließlich Hell/Dunkel-Wechsel. Links stehen ein verständliches Statussymbol und der Text; Aktionen stehen rechts unten. Status wird nicht allein durch Farbe vermittelt.

Die verbleibenden normalen MessageBox-Aufrufe in ModLibraryForm und ModConsoleProfileDialog werden ersetzt. Die Abschlussmeldung nach der Plugin-Aktivierung erhält den gemeinsamen Dialog. Die Frage „Activate now / Later“ bleibt ausdrücklich modal; Later bleibt sichere Vorauswahl, Escape und Schließen bedeuten keine Zustimmung. Bestehende Auswahlindizes anderer Dialognutzer ändern sich nicht.

Lange Pfade und Fehlermeldungen dürfen weder Buttons aus dem Bildschirm drücken noch abgeschnitten werden: begrenzte Breite, Zeilenumbruch und bei Bedarf scrollbare/kopierbare Details. Texte bleiben entsprechend dem bisherigen Mod-Library-UI auf Englisch; keine allgemeine Übersetzungsänderung.

## Desktop-Benachrichtigungen

Eine kompakte Karte enthält Titel, Kurztext, Statussymbol, Schließen und optional „Details“. Richtwert: 360 logische Pixel Breite, maximal 220 Höhe, 12 Pixel Außenabstand. Maße werden an DPI und verfügbare Arbeitsfläche angepasst.

- Position unten rechts innerhalb der Monitor-Arbeitsfläche, ohne die Taskleiste zu überdecken. Ausgangspunkt ist der Monitor des Dashboard-Fensters; bei Minimierung dessen zuletzt gültiger Monitor, bei Entfernung der Hauptmonitor.
- Höchstens drei sichtbare Karten, bei kleinen Arbeitsflächen entsprechend weniger; weitere warten. Wartende Meldungen sind auf 50 begrenzt. Darüber wird ein einzelner, schließbarer Sammelhinweis mit Anzahl zusätzlicher Ergebnisse und Zugang zur Transferansicht geführt, statt unbegrenzt Fenster/Daten anzusammeln.
- Information und Erfolg verschwinden nach sechs Sekunden. Zeitmessung pausiert bei Mauszeiger oder Tastaturfokus innerhalb der Karte. Warnungen und Fehler bleiben bis zum Schließen sichtbar.
- Anzeigen aktiviert kein Fenster, holt den Manager nicht nach vorne und stiehlt keinen Tastaturfokus. Erst ein bewusster Klick auf „Details“ darf ein passendes Manager-Fenster öffnen. Interaktive Controls haben erkennbare Beschriftungen und Fokuszustände.
- Funktioniert auch bei minimiertem Manager. Beim Beenden werden Fenster, Timer und Abonnements entfernt. Kein neues Minimieren-in-den-Tray-Verhalten.
- Keine Animation in der ersten Version. Theme-, DPI-, Monitor- und Arbeitsflächenänderungen lösen eine erneute Anordnung aus.

## Ereignisse und sichere Zuordnung

Queue-Ereignisse kommen vom Worker-Thread; der Adapter erfasst die relevanten Ergebnisdaten beim Ereignis und übergibt sie sicher an den UI-Thread. Fortschrittsereignisse erzeugen keine Popups. Pro Auftragsversuch entsteht höchstens eine Ergebnismeldung; ein ausdrücklich fortgesetzter Versuch darf ein neues Ergebnis melden.

Erfolg wird nur bei tatsächlichem Erfolg angezeigt. Aufträge mit fehlgeschlagenen Schritten sowie unterbrochene Aufträge ergeben Warnung/Fehler mit einem Hinweis auf die Transferdetails. Benutzerseitiges Abbrechen wird nicht als Fehler gemeldet.

Beim Anbinden einer Queue werden alte Ergebnisse nicht erneut abgespielt. Beim Sitzungswechsel werden alte Abonnements entfernt; verspätete Meldungen und Aktionen dürfen nicht auf die neue Konsole umgedeutet werden. Ein Details-Klick für eine nicht mehr aktuelle Sitzung zeigt nur die gespeicherte Information und löst keinen Konsolenzugriff aus.

Die Mod-Library-Abschlussdialoge und allgemeinen Queue-Meldungen werden koordiniert: Für einen bereits ausdrücklich durch einen modalen Abschlussdialog behandelten Ausgang erscheint kein zweites gleichlautendes Popup. Das Installationsergebnis bleibt vom optionalen Aktivierungsergebnis getrennt. Eine gescheiterte Aktivierung darf nie als gescheiterte Installation dargestellt werden. Dafür werden strukturierte Ergebnisarten genutzt, nicht Vergleiche mit englischen Meldungstexten.

Benachrichtigungen bestätigen niemals Installationen, Löschungen, Überschreibungen oder Plugin-Aktivierungen. Ein Fehler beim Anzeigen darf den Auftrag oder die Installation nicht verändern. Bestehende Statuszeilen, Transferdetails und Wiederherstellungsdaten bleiben verfügbar.

## Prüfung und Abnahme

Automatisierte Tests prüfen sichere Dialogvorauswahl, Enter/Escape/Schließen, lange Texte, Meldungsarten, Fristen mit kontrollierter Zeit, begrenzte Warteschlange, Stapelpositionen, Deduplizierung, Wiederaufnahme, Sitzungswechsel, späte Worker-Ereignisse und vollständiges Entsorgen. Bestehende Aktivierungs- und Recovery-Tests müssen unverändert ihre Sicherheitszusagen erfüllen.

Visuelle Prüfung in hellem und dunklem Skin bei 100, 150 und 200 Prozent Skalierung; zusätzlich kleine Arbeitsfläche, minimiertes Hauptfenster, mehrere Monitore soweit verfügbar und Fokusverhalten. Nicht tatsächlich ausgeführte Desktop-Tests werden ausdrücklich als offen benannt. Für die Prüfung sind keine Installationen oder Löschungen auf der PS4 erforderlich.

Abnahme: keine Standard-MessageBox mehr in den genannten Mod-Library-Abläufen; gemeinsame Dialogoptik; Benachrichtigungen für Queue-Ergebnisse aus mindestens zwei Manager-Bereichen; keine Doppelmeldungen oder automatisch bestätigten Entscheidungen; erfolgreiche Release-Tests und aktualisierte, geprüfte Client-ZIP.

## Nächster Schritt

Nach Prüfung dieser Spezifikation folgt ein konkreter Implementierungsplan. Produktcode, UIFramework-Abhängigkeit, Server und PS4 wurden durch diese Spezifikation nicht verändert.

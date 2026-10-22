# Games: Spieleübersicht und lokale Trainer

Stand: 30.09.2026. Umfang und schriftliche Spezifikation wurden im Chat
bestätigt. Produktcode wird erst nach Freigabe des anschließenden
Implementierungsplans geändert.

## Ziel und Grenzen

Ein eigenes Games-Fenster im PS4 Manager vereint alle unterstützten Spiele mit
ihren jeweiligen Cheats. Spiele sind mit verständlichen Namen statt nur Title-IDs
auffindbar. Der Katalog lässt sich auch ohne laufendes Spiel oder Verbindung
durchsehen; Aktionen benötigen eine passende laufende Spielsitzung.

Erster Anbieter ist BO3 Zombies im lokalen/offline Spiel. CUSA02624 und der im
Offset-Katalog als md5-bf1ffcd3 geführte eboot-Build sind der Ausgangspunkt.
Dieser Forschungs-Buildschlüssel ist keine behauptete APP_VER. Weitere Spiele
werden erst mit eigenen geprüften Profilen aufgenommen, nicht als leere Dummies.

Enthalten sind Godmode, einmaliges Auffüllen der Munition der gehaltenen
Bloodhound/RK5 und einmaliges Hinzufügen einer RK5. Nicht enthalten sind
Unendlich-Munition-Schleifen, freie Waffenwahl, Gobblegums, Punkte, Online-Cheats,
automatische Aktivierung nach Spielstart, heruntergeladener ausführbarer
Trainercode, Serveränderungen und neue Mod-Library-Funktionen.

## Architekturentscheidung

Ein BO3-spezifisches Fenster wäre zunächst kürzer, würde weitere Spiele aber an
UI-Code koppeln. Ein generischer, ausführbarer Remote-Cheat-Katalog würde eine
neue Vertrauens- und Update-Infrastruktur erfordern. Gewählt wird ein kleiner
mitgelieferter Katalog mit typisierten, lokalen Spiel-Anbietern hinter einer
gemeinsamen Oberfläche. Kein allgemeiner Plugin-Loader oder Script-Interpreter.

Die vorhandene WinForms/UIFramework-Anwendung bleibt erhalten. Das Dashboard
bekommt den Eintrag Games und öffnet über ToolWindowRegistry höchstens ein
GamesForm. Die aktuelle ManagerSession liefert die Konsole; Frame4-Endpunkte
verwenden deren Host und die bestehenden Frame4-Porteinstellungen. Abweichende
manuelle Debugger-Hosts dürfen nicht still als Games-Ziel übernommen werden.

Vorgesehene Verantwortlichkeiten:

- Core/Games: unveränderliche Katalogeinträge, Kompatibilitätsprofile,
  Aktionsdefinitionen und Ergebnis-/Zustandsmodelle. Kein WinForms-Bezug.
- GamesController: Auswahl, Suchfilter, Installationsstatus, Probe, serialisierte
  Aktionen, Abbruch und Sitzungswechsel; injizierbare Dienste für Tests.
- BO3-Anbieter: ausschließlich die unterstützten lokalen Aktionen einschließlich
  Auflösung, Prüfungen und Rücklesung. Keine UI-Texterkennung als Steuerlogik.
- TrainerTransport: typisierter Adapter auf vorhandene Frame4-HTTP-Lese-/Schreib-
  und TCP-RPC-Funktionen. Die fehlende RPC-Installation wird im vorhandenen
  Paketclient ergänzt und mit Wire-Tests abgesichert.
- GamesForm: Darstellung und ausdrückliche Benutzerentscheidungen. Gemeinsame
  MessageDialog- und DesktopNotification-Dienste kommen vom Dashboard.

Die schon reparierte installierte Spieleerkennung wird über einen dünnen Adapter
auf ModGameDiscovery wiederverwendet. Die Suche nach installierten Spielen hängt
nicht vom laufenden eboot ab. Installationsstatus und laufende Prozessidentität
bleiben getrennte Informationen. Es gibt keine generelle Umstrukturierung der
Mod Library oder anderer Tools.

## Oberfläche

Das Fenster übernimmt Farben, Schrift, Fokuszustände, Abstände und Skalierung
des aktiven UIFramework-Skins; Hell/Dunkel funktioniert wie im übrigen Manager.
Die Bedienoberfläche bleibt entsprechend den bestehenden Tools auf Englisch.

Links stehen Suche, Only installed und die unterstützten Spiele mit Name,
kleinem Bild, Title-ID und Installationsstatus. Bildquelle ist nach Möglichkeit
das vorhandene Konsolen-App-Icon; ohne Bild gibt es eine lokale neutrale
Spielkachel mit Namen. Keine zusätzliche Bildsuche oder Cover-Downloadpflicht.
Ist der Installationsstatus unbekannt, wird dies ausdrücklich angezeigt, nicht
als Not installed interpretiert. Ohne Konsole bleibt der Gesamtkatalog nutzbar.

Rechts stehen Spielname, unterstützter Modus/Build und die verfügbaren Aktionen:

- Godmode: Schalter mit ausgelesenem Zustand und begrenztem Testhinweis.
- Refill ammo: einmalige Aktion für die gehaltene Bloodhound oder RK5.
- Give RK5: zusätzliche Waffe, keine automatische Ersetzung bestehender Waffen.

Im Kopf stehen aktuelle Konsole, laufendes Spiel, gelesene Version, Frame4- und
Kompatibilitätsstatus sowie Refresh. Bei fehlenden Voraussetzungen nennt der
deaktivierte Aktionsbereich den konkreten Grund, etwa Start BO3, Frame4 is not
available, Unsupported build, No active player oder Confirm offline Zombies.
Ein laufendes Spiel wird markiert, überschreibt aber nicht die manuelle Auswahl.

Experimentell, verfügbar und aktiv sind getrennte Zustände. Die erste BO3-
Unterstützung trägt einen Experimental-Hinweis: erfolgreiche Einzeltests sind
keine Zusage für jede Situation, Waffenklasse oder Schadensart. Unimplementierte
Cheats werden nicht als bedienbare Schalter angezeigt. Status wird nie allein
durch Farbe vermittelt. Kleinere Fenster scrollen Inhalte statt Buttons zu
verdecken; Tastaturbedienung und 100/150/200-Prozent-DPI werden geprüft.

## Identität und Ausführung

Das Öffnen, Auswählen, Suchen und Refresh lesen nur. Sie installieren keinen
RPC-Stub und verändern keine Spielwerte. Pro Konsole/Prozess ist im Games-Bereich
nur eine Aktion gleichzeitig zulässig; Doppelklicks werden nicht eingereiht.
Lesende Statusabfragen überlappen keine Aktion. Bei sichtbarem Fenster wird
höchstens alle zwei Sekunden eine kleine Zustandsprobe ausgeführt, ohne
vollständige Speicherabbilder oder erneute FTP-Katalogabfrage.

Vor jeder Mutation werden die aktuelle ManagerSession-Generation, Host,
Prozess-ID, Prozess-Title-ID, eboot-Modulbasis und das Profil erneut geprüft.
Versionstext allein reicht nicht: kleine erwartete Codebereiche bzw. maskierte
Signaturen an den verwendeten Profil-RVAs müssen stimmen. Relokationen werden
korrekt berücksichtigt, unbekannte Treffer führen nicht zu heuristischen
Schreibzugriffen. Es wird kein vollständiger Live-MD5 des Spiels vorausgesetzt.
Runtime-Adressen ergeben sich aus Modulbasis plus Katalog-RVA, nicht durch
doppeltes Addieren der IDA-Basis.

Spieler-, Waffen- und Inventarzeiger werden frisch aufgelöst und auf gültige
lesbare Bereiche, konsistente Felder und erwartete Grenzen geprüft. Namen,
Prozessnummern, Heap-Zeiger und im Forschungsdurchlauf beobachtete Waffenindizes
werden nicht als feste Konstanten übernommen. Waffen werden über die geladene
Definitionstabelle anhand interner Namen identifiziert.

Erste Version: Offline-Zombies wird ausdrücklich vom Nutzer für die aktuelle
Sitzung bestätigt; das ist keine behauptete automatische Online-Erkennung.
Verfügbare Profilprüfungen müssen dazu passen. Fehlende eindeutige Zuordnung
oder widersprechende Daten sperren die Aktionen. Gleichzeitige externe Trainer
werden als nicht unterstützt erklärt; die eigene Aktionssperre kann fremde
Programme und Konsolenthreads nicht synchronisieren.

Alle drei Aktionen verlangen unmittelbar vorher eine ausdrückliche Bestätigung,
dass die lokale Runde pausiert ist. Pause ist keine garantierte Suspendierung
aller Spielthreads. Die engen BO3-Pfade werden deshalb beibehalten; kein neuer
Hook und kein pauschaler Aufruf beliebiger GSC-Handler. Escape/Schließen und
Standardfokus bedeuten Abbrechen, nicht Zustimmung. Nach jeder Bestätigung
werden die relevanten Identitäts-/Zustandsprüfungen vor dem Zugriff wiederholt.

## Aktionen und Lebensdauer

Godmode setzt oder löscht ausschließlich Bit 0 im geprüften playerState-Feld
und erhält alle anderen Bits durch frisches Lesen. Ein Erfolg erfordert
Rücklesung. Bereits gesetztes Bit wird als aktiv, aber nicht von diesem
Controller aktiviert dargestellt; es wird nicht ungefragt übernommen oder
gelöscht. Deaktivieren ist eine ausdrückliche Aktion mit Pause-Bestätigung.
Es gibt keine fortlaufende Schreibschleife und keine automatische Neuaktivierung
nach Respawn, Rundenwechsel, Wiederverbindung oder Neustart.

Beim Schließen wird Godmode nicht automatisch entfernt. Ist es aktiv, zeigt ein
Dialog die sichere Auswahl Cancel oder Close and leave enabled; der Schalter
zum bewussten Ausschalten bleibt im Fenster. Beim Verbindungsverlust wird der
Zustand Unknown / may still be enabled, niemals Off. Alte Zeiger dürfen nicht
zur späteren Wiederherstellung benutzt werden. Erneutes Öffnen liest den
tatsächlichen Zustand; aktives Fremd-/Rest-Bit wird nicht als eigenes markiert.

Refill ammo ist keine Unlimited-ammo-Funktion. Nur die gehaltene Bloodhound/RK5
wird unterstützt. Kapazitäten und bestehende Werte werden über die geprüften
Definitionen/Funktionen bestimmt; Reserve und Magazin werden mit dem
Spiel-Munitionspfad bis zu den ermittelten zulässigen Grenzen ergänzt. Volle
Munition ist ein unverändernder Ausgang. Andere Waffen werden mit verständlichem
Hinweis abgewiesen. Der eigenständige Vollauffüll-Test steht gegenüber dem
bereits getesteten RK5-Startmunitionspfad noch aus und gehört zur Abnahme.

Give RK5 prüft geladene Definition, unveränderte Kontextbedingungen, Abwesenheit
der RK5 und freien Platz. Ohne eindeutig verifizierte passende Inventarsituation
keine Vergabe; insbesondere keine automatische dritte normale Schusswaffe oder
Ersetzung, um ungetestete Waffenlimit-Sonderfälle zu vermeiden. Es folgen einmal
der interne Vergabeaufruf und danach die Startmunition über den Spielpfad.
Ein zweiter Klick bei vorhandener RK5 meldet Already owned und vergibt nichts.
Die getestete Rücklesung war 15/105; dies ist Testevidenz, keine universell
festgeschriebene Munitionskonstante. Andere Inventarslots dürfen sich nicht ändern.

RPC wird erst für eine bestätigte benötigende Aktion installiert und innerhalb
einer unveränderten laufenden Trainer-Prozesssitzung wiederverwendet. Rückgaben
werden nach dem belegten Typ ausgewertet: bool nur AL, 32-Bit-Ergebnisse nur EAX,
Zeiger/64-Bit-Werte vollständig. Ein gesperrter RPC-Zugang bedeutet nicht, dass
der Prozess-Stub deinstalliert wurde. Solange der Payload keine verifizierte
Stop-/Freigabeoperation anbietet, wird aktiver Stub-Speicher nicht blind befreit.

Timeout/Abbruch nach Versand einer Mutation bedeutet Outcome unknown. Keine
automatische Wiederholung, insbesondere nicht für Vergabe und Ammo-Addition.
Bleibt die Identität gültig, wird lesend abgeglichen; bei weiterhin unklarem
Ergebnis bleiben Mutationen bis zu einer erfolgreichen erneuten Probe gesperrt.
Eine eingefügte Waffe mit fehlgeschlagener Munitionsinitialisierung ist ein
Teilerfolg, keine vollständig fehlgeschlagene Vergabe. Kein blindes Rollback
ganzer Speicherblöcke und keine automatische Entfernung der neuen Waffe.

## Rückmeldungen und Speicherung

Dialoge sind für Pause-/Offline-Bestätigungen und folgenreiche Entscheidungen;
Ergebnisse erscheinen in der Statuszeile und einmalig über den bestehenden
Notification-Dienst. Kein Popup pro Poll. Späte Ergebnisse tragen ihre ursprüngliche
Sitzungsidentität und dürfen weder den neuen Zustand überschreiben noch neue
Konsolenaktionen auslösen. Eine Toast-Aktion öffnet nur Details bzw. Games.

Gespeichert werden höchstens Fenster-/Suchpräferenzen und Katalog-Metadaten.
Aktivierte Cheats, Zustimmungsflags, Zeiger und RPC-Stubs werden nicht über
Manager-Neustarts als gültige Sitzung fortgeführt. Keine Accounts oder neue
Server-API für Trainer im ersten Stand.

## Nachweise und Prüfungen

Grundlage sind die BO3-Einträge im lokalen ps4-offsets-Katalog und die privaten
Testprotokolle vom 30.09.2026. Bestätigt: Damage-skip-Bit bei Zombie-Treffern in
zwei Runden; einzelne Reserveänderung; RK5-Vergabe und Initialisierung auf 15/105;
Nutzerrückmeldung RK5 als zweite Waffe und nach Feuerstoß 12/105. Dies beweist
weder alle Schadensarten noch beliebige Waffen oder allgemeine Thread-Sicherheit.

Automatisierte Tests mit gefälschtem Transport prüfen Katalog/Suche/Filter,
fehlende Verbindung, gemischte Installationsstatus, falsche Title-ID/Signaturen,
ungültige Zeiger, Prozess-/Sitzungswechsel auch nach Dialogbestätigung, fehlenden
Spieler, volle/ungeeignete Inventare, doppelte RK5 und nicht unterstützte Waffen.
Zusätzlich: Bit-Erhaltung, fremd aktiver Godmode, Rücklesefehler, AL/EAX-Auswertung,
RPC-Installations-/Antwortpakete, einmalige Vergabe, Teilerfolg, Timeout ohne Retry,
Cancellation, späte Antworten und fehlende automatische Schreibzugriffe.

STA-UI-Tests prüfen Dashboard-Integration, genau ein Fenster, Zustandsdarstellung,
disabled reasons, sichere Dialogvorauswahl und Schließverhalten. Bestehende
Frame4-, Sitzungs-, Dashboard-, Notification- und Mod-Library-Tests bleiben grün.
Visuell werden beide Skins, DPI, kleines Fenster, lange Texte und Tastaturfokus
geprüft. Produktcode wird nicht mit reinem Mock-Erfolg als live getestet bezeichnet.

Hardware-Abnahme erfolgt separat mit angekündigten Aktionen und frischer
Pause-Bestätigung: BO3 erkennen; Godmode bewusst an/aus; Bloodhound/RK5 auffüllen;
RK5 bei geeignetem Inventar vergeben, wechseln und schießen; Reconnect/Rundenwechsel
ohne automatische Wiederanwendung. Kein Rücksetzen von Schutz ohne ausdrückliche
Pausebestätigung. Der aktuelle Konsolenzustand wird beim Spezifizieren/Builden
nicht verändert. Andere Spiele brauchen eigene Profile und Abnahme.

## Übergabe

Nach Freigabe dieser Spezifikation folgt ein konkreter Implementierungsplan mit
Tests und Ausführungswahl. Erst dessen Freigabe startet Produktänderungen.
Die spätere Lieferung enthält die getestete Client-ZIP sowie eine ehrliche Liste
der durchgeführten bzw. noch offenen Hardware-/UI-Prüfungen. Diese Spezifikation
allein ist weder ein fertiges Games-Fenster noch ein getesteter Trainer-Release.

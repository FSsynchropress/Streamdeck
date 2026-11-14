# DIY Stream Deck – ESP32-S3 + Windows-App

Selbstgebautes Makro-Keypad mit 15 mechanischen Tasten (3 Reihen × 5 Spalten), ohne Beleuchtung.

**Aufgabenteilung:**
- **Firmware (ESP32-S3):** liest nur die Tasten ein und meldet Drücken/Loslassen über USB-Seriell (CDC). Kein HID, kein WLAN, keine Logik.
- **Windows-App:** empfängt die Tastenereignisse und führt alle Aktionen aus. Die App muss laufen, damit das Deck funktioniert.

Empfohlene Repo-Struktur:
```
/firmware   PlatformIO-Projekt für den ESP32-S3
/app        .NET-Projekt für Windows
CLAUDE.md
```

---

## Teil 1: Hardware & Firmware

### Hardware
- ESP32-S3-N16R8 Dev-Board (16 MB Flash, 8 MB Octal-PSRAM), angeschlossen über die **native USB-Buchse**
- 15 MX-kompatible Switches (aus Razer BlackWidow Chroma V2), eingebaute LEDs ungenutzt
- 3D-gedrucktes Gehäuse, frei verdrahtet

### Pinbelegung
Tasten nummeriert zeilenweise von oben links (0) nach unten rechts (14).

| Taste | GPIO | Taste | GPIO | Taste | GPIO |
|------:|-----:|------:|-----:|------:|-----:|
| 0 | 1 | 5 | 7 | 10 | 15 |
| 1 | 2 | 6 | 10 | 11 | 16 |
| 2 | 4 | 7 | 11 | 12 | 17 |
| 3 | 5 | 8 | 12 | 13 | 18 |
| 4 | 6 | 9 | 13 | 14 | 21 |

- Taster zwischen GPIO und GND → `INPUT_PULLUP`, gedrückt = LOW
- Entprellung in Software (ca. 20 ms), Abfrage z. B. jede Millisekunde
- Nicht verwenden: GPIO 0, 3, 45, 46 (Strapping), 19/20 (USB), 35–37 (PSRAM), 43/44 (UART)
- Onboard-RGB-LED (GPIO 48) als Statusanzeige: z. B. rot = keine App verbunden, grün = verbunden, kurz weiß bei Tastendruck

### Toolchain
- PlatformIO, Arduino-Framework, Board-Basis `esp32-s3-devkitc-1`
- 16 MB Flash, Octal-PSRAM (`board_build.arduino.memory_type = qio_opi`)
- USB-CDC über die native Buchse beim Start aktiv (`ARDUINO_USB_CDC_ON_BOOT=1`). Genaue Build-Flags gegen die aktuelle Doku prüfen.

### Serielles Protokoll (Version 1)
Zeilenbasierter ASCII-Text, jede Nachricht endet mit `\n`.

**Gerät → PC**
| Nachricht | Bedeutung |
|-----------|-----------|
| `HELLO STREAMDECK 1 15` | Kennung, Protokollversion, Anzahl Tasten (nach Start und als Antwort auf `HELLO?`) |
| `D <n>` | Taste n gedrückt |
| `U <n>` | Taste n losgelassen |
| `PONG` | Antwort auf `PING` |

**PC → Gerät**
| Nachricht | Bedeutung |
|-----------|-----------|
| `HELLO?` | Kennung anfordern (für die automatische Geräteerkennung) |
| `PING` | Verbindung prüfen, Gerät antwortet mit `PONG` |
| `LED <r> <g> <b>` | Onboard-LED setzen (0–255), optional |

- Das Gerät gilt als „App verbunden", solange mindestens alle 3 s ein `PING` kommt.
- Unbekannte Befehle werden ignoriert.
- Lange Tastendrücke, Doppelklicks usw. wertet ausschließlich die App aus (anhand von `D`/`U` und Zeitstempeln).

---

## Teil 2: Windows-App

### Technik
- .NET 8, VB.NET mit WinForms (alternativ C#/WPF, Entscheidung zu Projektbeginn)
- Serielle Kommunikation über `System.IO.Ports`
- Konfiguration als JSON in `%APPDATA%\StreamDeck\config.json`
- Sinnvolle Bibliotheken: NAudio (Sound, Audiogeräte, Lautstärke), obs-websocket-dotnet (OBS)

### Verhalten
- Läuft im Infobereich der Taskleiste (Tray-Icon mit Statusanzeige: verbunden/getrennt)
- Optional Autostart mit Windows
- Automatische Geräteerkennung: alle COM-Ports mit `HELLO?` abfragen und den Port nehmen, der mit `HELLO STREAMDECK` antwortet. Bei Ein-/Ausstecken automatisch neu verbinden.
- Alle 1 s `PING` senden, damit das Gerät die Verbindung erkennt
- Aktionen werden asynchron ausgeführt, damit ein langsamer Befehl keine anderen Tasten blockiert

### Oberfläche
- Raster 3×5 wie das echte Gerät, jede Taste mit Label, Beschreibung (`description`) und optionalem Icon
- Klick auf eine Taste öffnet den Inspektor: Reiter Kurz / Lang / Doppelt / Loslassen (`press`, `longPress`, `doublePress`, `release`), „Wiederholen beim Halten“ (`repeat`), Testen-Knopf
  - Kurz löst sofort aus; ist Lang belegt, erst beim Loslassen; ist Doppelt belegt, nach dem Zeitfenster `doublePressMs`
  - Loslassen läuft zusätzlich bei jedem Loslassen und beim Verbindungsabbruch (Push-to-Talk)
  - Wiederholen nur ohne Lang/Doppelt
- Aktionsleiste: Aktionstyp per Drag-and-drop auf eine Taste ziehen
- „Taste lernen“: nächster Druck am Gerät wählt die Taste in der Oberfläche aus (ohne Aktion)
- Tastendruck am Gerät wird im Raster live hervorgehoben
- Import/Export der Konfiguration
- „Druckvorlage“: Keycap-Einleger 16 × 16 mm pro Taste, je Seite ein Raster, drucken oder als PNG (300 dpi) speichern

### Aktionstypen
| Typ | Beschreibung |
|-----|--------------|
| `hotkey` | Tastenkombination senden (`SendInput`), z. B. Strg+Shift+M, F13–F24; Modifier auch allein und links/rechts (`RCtrl`, `LShift` …) |
| `text` | Text einfügen (über Zwischenablage + Strg+V oder Unicode-SendInput), optional Enter danach (`enter`) |
| `media` | Play/Pause, Nächster/Vorheriger Titel, Stopp, Mute, Lautstärke ± |
| `mic` | Mikrofon systemweit stumm/an/umschalten (`op`), alle Aufnahmegeräte oder eines (`device`) |
| `sound` | WAV/MP3 abspielen, wählbares Ausgabegerät und Lautstärke, erneuter Druck stoppt (`restart: true` = von vorn); `voice: false` = nicht in den Sprach-Mix |
| `timer` | Countdown (`seconds`), am Ende Hinweis + Ton (`file`, Standard `Sounds\alarm.mp3`); erneuter Druck bricht ab |
| `launch` | Programm, Datei oder Ordner öffnen, mit Argumenten |
| `close` | Programm schließen (Fenster wie mit X) oder mit `force` sofort beenden – z. B. als langer Druck |
| `url` | Webseite im Standardbrowser oder im gewählten Browser (`browser`) öffnen |
| `http` | HTTP-Anfrage im Hintergrund (`method`, `url`, `body`, `contentType`, `headers`), z. B. n8n-Webhook |
| `command` | Kommando oder PowerShell-Skript ausführen (versteckt oder sichtbar) |
| `obs` | OBS-Szene wechseln, Aufnahme/Stream starten/stoppen, Quelle ein-/ausblenden |
| `page` | Zu einer Seite/einem Ordner wechseln; `target` auch `@back`, `@first`, `@next`, `@prev` (Blättern überspringt Ordner) |
| `profile` | Profil wechseln; `target` auch `@next`/`@prev` (reihum) |
| `multi` | Mehrere Aktionen nacheinander mit Wartezeiten (`wait`) oder mit `parallel: true` gleichzeitig |
| `toggle` | Bei jedem Druck die nächste Aktion aus `actions` (Hotkey-Switch, Multi-Action-Switch) |
| `random` | Zufällig eine Aktion aus `actions` (nie zweimal dieselbe hintereinander) |
| `window` | Fenster (`path` = Programm, leer = aktives Fenster): `op` monitor (`monitor` 1…n), nextMonitor, maximize, minimize, restore, move (`x`/`y`/`width`/`height` in % des Arbeitsbereichs) |
| `plugin` | Eigenes Plugin `plugin` mit `args`: .NET-DLL (Klasse implementiert `IDeckPlugin` aus StreamDeck.Core) oder Skript `.ps1/.cmd/.bat/.py/.exe` im Ordner `%APPDATA%\StreamDeck\plugins` (Umgebungsvariable `STREAMDECK_KEY`) |

Smart Home: `http` hat Vorlagen für Philips Hue, Home Assistant und n8n; An/Aus auf einer Taste über `toggle` mit zwei HTTP-Aktionen.

Protokoll für die Fehlersuche: `%APPDATA%\StreamDeck\logs\streamdeck-JJJJ-MM-TT.log` (14 Tage, Tray-Menü „Protokollordner öffnen“).

### Profile teilen & Administratorrechte
- Profil-Kontextmenü: „Profil exportieren“ schreibt `<Name>.sdprofile.json` (`{"streamDeckProfile": 1, "profile": {…}}`), „Profil importieren“ fügt es hinzu (Namenskonflikt → „Name 2“). Pfade sind rechnerabhängig.
- Einstellung „Mit Administratorrechten ausführen“ (`runAsAdmin`): Start fragt per UAC und startet neu (`--wait-for <pid>` wartet auf die alte Instanz, `--no-elevate` überspringt). Autostart läuft dann über die geplante Aufgabe „DIY StreamDeck (Administrator)“ (bei Anmeldung, höchste Privilegien) statt über den Run-Schlüssel. Tray-Menü: „Als Administrator neu starten“.

### Seiten & Profile
- Mehrere **Seiten** pro Profil; eine Taste kann als „Zurück"-Taste dienen
- **Ordner** = Seite mit `"folder": true`: per Taste geöffnet, verschachtelbar, feste Zurück-Taste (`folderBackKey`, Standard 0), beim Blättern übersprungen; die Startseite ist nie ein Ordner
- Wechsel am Gerät oder durch Smart Profiles zeigen ein kurzes On-Screen-Overlay (`showOverlay`); Tray-Tooltip und Tray-Menü zeigen Profil/Seite, im Tray-Menü lässt sich das Profil wählen
- Mehrere **Profile**, optional automatisch umschalten je nach aktivem Programm (Prozessname des Vordergrundfensters)

### Sounds in Discord (Sprach-Mix)
- Discord & Co. hören nur ein Eingabegerät. Mit `voiceMix.enabled` nimmt die App das Mikrofon auf (`microphone`, leer = Standard-Kommunikationsgerät), mischt die Sounds dazu und spielt beides auf einem virtuellen Kabel ab (`output`, Standard `CABLE Input` von VB-Audio Virtual Cable); `micVolume` 0–400 % (über 100 = verstärken)
- Mikrofon wird im Kommunikationsmodus aufgenommen (wie Discord/Teams, Windows-Sprachaufbereitung; abschaltbar mit `communications: false`); Rauschsperre nur fürs Mikrofon (`noiseGate`, Schwelle `noiseGateDb` -80…-10)
- Sounds kommen mit Reserve in den Mix (`soundVolume` 0–100 %, Standard 70); ein Limiter (-3 dBFS) senkt Spitzen ab statt sie zu verzerren
- In Discord als Eingabegerät `CABLE Output` wählen, Rauschunterdrückung (Krisp) und automatische Verstärkung aus (sonst filtert Discord auch die Sounds)
- Umschaltbar in den Einstellungen und im Tray-Menü; einzelne Sounds mit `voice: false` ausnehmen; der Timer-Wecker geht nie in den Mix
- Code: `app/StreamDeck.App/VoiceMix.vb`

### Beispiel-Konfiguration
```json
{
  "activeProfile": "Standard",
  "profiles": [
    {
      "name": "Standard",
      "autoSwitchProcess": null,
      "pages": [
        {
          "name": "Start",
          "keys": {
            "0": { "label": "Mute",   "press": { "type": "media", "key": "mute" } },
            "1": { "label": "Tusch",  "press": { "type": "sound", "file": "C:\\Sounds\\tusch.mp3", "volume": 80 } },
            "2": { "label": "Rechner","press": { "type": "launch", "path": "calc.exe" } },
            "14": { "label": "Mehr",  "press": { "type": "page", "target": "Seite 2" } }
          }
        }
      ]
    }
  ]
}
```

### Installation & Updates
- Setup und Updates über **Velopack** (`app/StreamDeck.App/Updater.vb`), Quelle sind die GitHub-Releases (`Updater.RepoUrl`)
- Installiert nach `%LocalAppData%\DIY-StreamDeck`, ohne Adminrechte; die Konfiguration in `%APPDATA%` bleibt bei Updates erhalten
- Neue Version: `git tag v1.2.3` + `git push origin v1.2.3` → `.github/workflows/release.yml` testet, baut (`tools/build-release.ps1`) und veröffentlicht das Release
- Die App prüft 15 s nach dem Start und dann alle 6 h, lädt im Hintergrund und bietet die Installation im Tray-Menü an (sonst beim nächsten Start)

---

## Arbeitsweise mit GitHub
- Repo: `FSsynchropress/Streamdeck` (öffentlich), Hauptbranch `main`
- Keine direkten Commits auf `main`: jede Änderung auf einem eigenen Branch (`feature/…`, `fix/…`, `docs/…`), dann Pull Request per `gh pr create`
- Gemergt wird erst, wenn der Besitzer den PR auf GitHub freigibt
- Releases weiterhin nur über Tags auf `main` (siehe „Installation & Updates“)

---

## Vorgehen
1. Firmware: Tasten einlesen, entprellen, `D`/`U` seriell ausgeben, `HELLO?`/`PING` beantworten
2. App: Gerät finden, verbinden, Ereignisse im Log anzeigen
3. App: Konfiguration laden/speichern, erste Aktionstypen (`hotkey`, `media`, `launch`, `sound`)
4. App: Editor-Oberfläche mit Live-Hervorhebung, Tray-Betrieb, Autostart
5. App: Seiten, Profile, OBS, `multi` und die übrigen Aktionstypen

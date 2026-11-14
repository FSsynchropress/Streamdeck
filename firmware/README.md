# Stream Deck – Firmware (ESP32-S3)

Liest 15 Tasten, entprellt sie (20 ms) und meldet `D <n>` / `U <n>` über USB-CDC.
Protokoll und Pinbelegung: siehe [CLAUDE.md](../CLAUDE.md).

```
firmware/
  StreamDeck/            Sketch-Ordner (Arduino IDE) = src_dir (PlatformIO)
    StreamDeck.ino       Hauptprogramm: Pins, Seriell, LED, Diagnosebefehle
    Debouncer.h/.cpp     Entprellung (hardwareunabhängig)
    Protocol.h/.cpp      Befehls-Parser, Zeilenpuffer, Ausgabeformate
    Connection.h         "App verbunden" (PING-Timeout 3 s)
  test/
    test_main.cpp        Unit-Tests für Debouncer/Protocol/Connection (laufen auf dem PC)
    run_tests.bat        baut + startet die Tests mit Visual Studio (MSVC)
  platformio.ini
../tools/serial-test.ps1 Protokoll- und Hardwaretest über die serielle Schnittstelle
```

## Flashen

**Arduino IDE** (ESP32-Core 3.x): `firmware/StreamDeck/StreamDeck.ino` öffnen, dann unter *Werkzeuge*:

| Einstellung | Wert |
|---|---|
| Board | ESP32S3 Dev Module |
| USB CDC On Boot | **Enabled** |
| USB Mode | Hardware CDC and JTAG |
| Flash Size | 16MB (128Mb) |
| PSRAM | OPI PSRAM |
| Partition Scheme | 16M Flash (3MB APP/9.9MB FATFS) o. ä. |

Board an der **nativen USB-Buchse** anschließen (nicht „UART"/„COM"). Falls der Upload nicht startet:
BOOT gedrückt halten, RESET kurz drücken, BOOT loslassen.

**PlatformIO:** im Ordner `firmware` `pio run -t upload`, danach `pio device monitor`.

## Diagnose direkt im seriellen Monitor

Im seriellen Monitor (Zeilenende „Neue Zeile" oder „Both NL & CR") einfach eintippen:

| Befehl | Antwort |
|---|---|
| `HELP` | Befehlsübersicht |
| `HELLO?` | `HELLO STREAMDECK 1 15` |
| `PING` | `PONG` – danach ist die LED 3 s lang grün („App verbunden") |
| `LED 0 0 40` | Status-LED blau (solange verbunden) |
| `INFO` | Firmware-Version, Laufzeit, Verbindungsstatus, Pinbelegung |
| `STATE` | `STATE 000000000000000` – eine Stelle pro Taste, 1 = gedrückt |
| `SELFTEST` | prüft Verdrahtung: Pin dauerhaft auf GND? Kurzschluss zwischen zwei Tasten-Pins? → `TEST OK 15 KEYS` oder `TEST FAIL …` (**dabei keine Taste drücken**) |
| `KEYTEST` | jede Taste einmal drücken → `KEYTEST KEY 3 OK GPIO 5 EDGES 1 (4/15)` … `KEYTEST DONE ALL 15 OK`. `EDGES` = Rohflanken bis zum stabilen Druck (1 = sauber, viele = Taster prellt stark) |
| `LEDTEST` | LED rot → grün → blau → weiß → aus |
| `STOP` | KEYTEST / LEDTEST abbrechen |

Groß-/Kleinschreibung ist egal. Tastendrücke erscheinen immer als `D n` / `U n`.
Die Diagnoseantworten beginnen nie mit `D `, `U `, `HELLO` oder `PONG` – die Windows-App wird dadurch nicht gestört.

**LED:** rot = keine App (kein PING seit 3 s), grün = verbunden, weißer Blitz = Tastendruck, blau = KEYTEST läuft.

## Automatischer Test über die serielle Schnittstelle

Seriellen Monitor der Arduino IDE vorher **schließen** (der Port kann nur einmal geöffnet werden).

```bash
powershell -ExecutionPolicy Bypass -File tools\serial-test.ps1
```

Findet das Gerät per `HELLO?` selbst und prüft: Kennung, 10× PING/PONG mit Antwortzeit, Zeilenenden
`\n` / `\r\n` / `\r`, zwei Befehle in einem Paket, Ignorieren ungültiger Befehle und überlanger Zeilen,
LED-Befehl, INFO (CONNECTED nach PING), STATE, SELFTEST, HELP.

Zusätzliche Schalter:

- `-Port COM7` – Port vorgeben statt suchen
- `-KeyTest` – danach alle 15 Tasten interaktiv prüfen
- `-Timeout` – prüft, dass das Gerät nach 3 s ohne PING auf „getrennt" geht
- `-Monitor` – verhält sich wie die App (PING jede Sekunde) und zeigt Tastendrücke mit Haltedauer live an
- `-Verbose` – zeigt jede gesendete/empfangene Zeile

## Unit-Tests auf dem PC

```bash
firmware\test\run_tests.bat
```

Nimmt `g++`, wenn er im PATH liegt (z. B. [w64devkit](https://github.com/skeeto/w64devkit), portabel, ohne Installation),
sonst Visual Studio mit der Workload „Desktopentwicklung mit C++". Oder direkt:

```bash
g++ -std=c++17 -Ifirmware/StreamDeck firmware/test/test_main.cpp firmware/StreamDeck/Debouncer.cpp firmware/StreamDeck/Protocol.cpp -o firmware_tests && ./firmware_tests
```

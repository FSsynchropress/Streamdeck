// DIY Stream Deck – Firmware für ESP32-S3 (15 Tasten, USB-CDC)
//
// Die Firmware liest nur die Tasten und meldet D/U über USB-Seriell.
// Alle Aktionen führt die Windows-App aus. Protokoll: siehe CLAUDE.md.
//
// Zusätzlich gibt es Diagnosebefehle für den seriellen Monitor (HELP eingeben).
// Deren Antworten beginnen nie mit "D ", "U ", "HELLO" oder "PONG" und
// stören die App daher nicht.

#include <Arduino.h>
#include "Debouncer.h"
#include "Protocol.h"
#include "Connection.h"

// ---------------------------------------------------------------- Konfiguration
static const char*    FW_VERSION  = "1.0.0";
static const uint8_t  KEY_COUNT   = 15;
static const uint8_t  KEY_PINS[KEY_COUNT] = {
  1, 2, 4, 5, 6,       // Reihe 1: Taste 0..4
  7, 10, 11, 12, 13,   // Reihe 2: Taste 5..9
  15, 16, 17, 18, 21   // Reihe 3: Taste 10..14
};
static const uint8_t  LED_PIN       = 48;   // Onboard-RGB-LED (WS2812)
static const uint16_t DEBOUNCE_MS   = 40;
static const uint32_t PING_TIMEOUT  = 3000;
static const uint16_t FLASH_MS      = 60;   // weißes Aufblitzen bei Tastendruck
static const uint8_t  DIM           = 40;   // Grundhelligkeit der Status-LED

// ---------------------------------------------------------------- Zustand
static Debouncer         debouncer(KEY_COUNT, DEBOUNCE_MS);
static ConnectionMonitor connection(PING_TIMEOUT);
static proto::LineReader lineReader;

struct Rgb { uint8_t r, g, b; };
static const Rgb COLOR_DISCONNECTED = {DIM, 0, 0};
static const Rgb COLOR_CONNECTED    = {0, DIM, 0};
static const Rgb COLOR_FLASH        = {DIM, DIM, DIM};
static const Rgb COLOR_KEYTEST      = {0, 0, DIM};

static Rgb      appColor     = COLOR_CONNECTED;  // per "LED r g b" setzbar
static Rgb      shownColor   = {1, 2, 3};        // erzwingt erstes Schreiben
static uint32_t flashUntil   = 0;
static uint32_t lastScanMs   = 0;
static bool     wasConnected = false;

// KEYTEST: jede Taste einmal drücken
static bool     keyTestActive = false;
static uint32_t keyTestMask   = 0;

// LEDTEST: nicht blockierende Farbfolge
static const Rgb LEDTEST_SEQ[] = {{255, 0, 0}, {0, 255, 0}, {0, 0, 255}, {255, 255, 255}, {0, 0, 0}};
static const uint8_t LEDTEST_STEPS = sizeof(LEDTEST_SEQ) / sizeof(LEDTEST_SEQ[0]);
static int8_t   ledTestStep = -1;  // -1 = aus
static uint32_t ledTestNext = 0;

// ---------------------------------------------------------------- Hilfen
static void sendRaw(const char* s, size_t len) { Serial.write((const uint8_t*)s, len); }
static void sendLine(const char* s) { Serial.print(s); Serial.print('\n'); }

static void setLed(const Rgb& c) {
  if (c.r == shownColor.r && c.g == shownColor.g && c.b == shownColor.b) return;
  shownColor = c;
  rgbLedWrite(LED_PIN, c.r, c.g, c.b);
}

static uint32_t pressedMask() {
  uint32_t m = 0;
  for (uint8_t i = 0; i < KEY_COUNT; i++) if (debouncer.isPressed(i)) m |= 1UL << i;
  return m;
}

static uint8_t popCount(uint32_t v) {
  uint8_t n = 0;
  while (v) { n += v & 1; v >>= 1; }
  return n;
}

static void sendHello() {
  char buf[40];
  sendRaw(buf, proto::formatHello(buf, sizeof(buf), KEY_COUNT));
}

// ---------------------------------------------------------------- Diagnose
static void cmdHelp() {
  sendLine("# DIY Stream Deck - Befehle (Gross-/Kleinschreibung egal):");
  sendLine("#   HELLO?          Kennung -> HELLO STREAMDECK 1 15");
  sendLine("#   PING            Verbindung pruefen -> PONG (alle <3 s = App verbunden)");
  sendLine("#   LED r g b       Farbe der Status-LED (0-255) solange verbunden");
  sendLine("#   INFO            Firmware, Laufzeit, Verbindung, Pinbelegung");
  sendLine("#   STATE           aktueller Tastenzustand, z. B. STATE 000100000000000");
  sendLine("#   SELFTEST        Verdrahtung pruefen (dabei KEINE Taste druecken)");
  sendLine("#   KEYTEST         jede Taste einmal druecken, STOP bricht ab");
  sendLine("#   LEDTEST         LED rot/gruen/blau/weiss/aus");
  sendLine("#   STOP            KEYTEST/LEDTEST abbrechen");
}

static void cmdInfo() {
  const uint32_t now = millis();
  Serial.printf("INFO FW %s PROTO %u KEYS %u\n", FW_VERSION, (unsigned)proto::VERSION, (unsigned)KEY_COUNT);
  Serial.printf("INFO UPTIME %lu s APP %s DEBOUNCE %u ms\n", (unsigned long)(now / 1000),
                connection.isConnected(now) ? "CONNECTED" : "DISCONNECTED", (unsigned)DEBOUNCE_MS);
  Serial.print("INFO PINS");
  for (uint8_t i = 0; i < KEY_COUNT; i++) Serial.printf(" %u=GPIO%u", (unsigned)i, (unsigned)KEY_PINS[i]);
  Serial.print('\n');
}

static void cmdState() {
  char buf[40];
  sendRaw(buf, proto::formatState(buf, sizeof(buf), pressedMask(), KEY_COUNT));
}

// Prüft jeden Pin auf Dauer-LOW und Kurzschlüsse zwischen zwei Tasten-Pins.
static void cmdSelfTest() {
  uint16_t failures = 0;
  sendLine("TEST START");

  // 1) Ruhepegel: mit Pull-up muss jeder Pin HIGH sein
  uint32_t idleLow = 0;
  for (uint8_t i = 0; i < KEY_COUNT; i++) {
    if (digitalRead(KEY_PINS[i]) == LOW) {
      idleLow |= 1UL << i;
      failures++;
      Serial.printf("TEST FAIL KEY %u GPIO %u LOW (gedrueckt oder Schluss nach GND)\n",
                    (unsigned)i, (unsigned)KEY_PINS[i]);
    }
  }

  // 2) Kurzschluss zwischen Pins: einen Pin auf LOW ziehen, alle anderen müssen HIGH bleiben
  for (uint8_t i = 0; i < KEY_COUNT; i++) {
    if (idleLow & (1UL << i)) continue;
    pinMode(KEY_PINS[i], OUTPUT);
    digitalWrite(KEY_PINS[i], LOW);
    delayMicroseconds(100);
    for (uint8_t j = i + 1; j < KEY_COUNT; j++) {
      if (idleLow & (1UL << j)) continue;
      if (digitalRead(KEY_PINS[j]) == LOW) {
        failures++;
        Serial.printf("TEST FAIL SHORT KEY %u (GPIO %u) <-> KEY %u (GPIO %u)\n",
                      (unsigned)i, (unsigned)KEY_PINS[i], (unsigned)j, (unsigned)KEY_PINS[j]);
      }
    }
    pinMode(KEY_PINS[i], INPUT_PULLUP);
    delayMicroseconds(100);
  }

  if (failures == 0) {
    Serial.printf("TEST OK %u KEYS\n", (unsigned)KEY_COUNT);
  } else {
    Serial.printf("TEST FAIL %u\n", (unsigned)failures);
  }
}

static void keyTestReportMissing() {
  Serial.print("KEYTEST MISSING");
  for (uint8_t i = 0; i < KEY_COUNT; i++) {
    if (!(keyTestMask & (1UL << i))) Serial.printf(" %u", (unsigned)i);
  }
  Serial.print('\n');
}

static void cmdKeyTest() {
  keyTestActive = true;
  keyTestMask = 0;
  Serial.printf("KEYTEST START - bitte alle %u Tasten nacheinander druecken (STOP = Abbruch)\n",
                (unsigned)KEY_COUNT);
}

static void keyTestOnPress(uint8_t key) {
  const uint32_t bit = 1UL << key;
  const uint16_t edges = debouncer.bounceCount(key);
  if (keyTestMask & bit) {
    Serial.printf("KEYTEST KEY %u AGAIN EDGES %u\n", (unsigned)key, (unsigned)edges);
    return;
  }
  keyTestMask |= bit;
  Serial.printf("KEYTEST KEY %u OK GPIO %u EDGES %u (%u/%u)\n", (unsigned)key, (unsigned)KEY_PINS[key],
                (unsigned)edges, (unsigned)popCount(keyTestMask), (unsigned)KEY_COUNT);
  if (popCount(keyTestMask) == KEY_COUNT) {
    keyTestActive = false;
    Serial.printf("KEYTEST DONE ALL %u OK\n", (unsigned)KEY_COUNT);
  }
}

static void cmdStop() {
  if (keyTestActive) {
    keyTestActive = false;
    Serial.printf("KEYTEST STOPPED %u/%u\n", (unsigned)popCount(keyTestMask), (unsigned)KEY_COUNT);
    keyTestReportMissing();
  }
  if (ledTestStep >= 0) {
    ledTestStep = -1;
    sendLine("LEDTEST STOPPED");
  }
}

static void cmdLedTest() {
  ledTestStep = 0;
  ledTestNext = millis();
  sendLine("LEDTEST START");
}

// ---------------------------------------------------------------- Befehle
static void handleLine(const char* line) {
  const proto::Command cmd = proto::parse(line);
  switch (cmd.type) {
    case proto::CmdType::Hello:    sendHello(); break;
    case proto::CmdType::Ping:     connection.onPing(millis()); sendLine("PONG"); break;
    case proto::CmdType::Led:      appColor = {cmd.r, cmd.g, cmd.b}; break;
    case proto::CmdType::Help:     cmdHelp(); break;
    case proto::CmdType::Info:     cmdInfo(); break;
    case proto::CmdType::State:    cmdState(); break;
    case proto::CmdType::SelfTest: cmdSelfTest(); break;
    case proto::CmdType::KeyTest:  cmdKeyTest(); break;
    case proto::CmdType::Stop:     cmdStop(); break;
    case proto::CmdType::LedTest:  cmdLedTest(); break;
    default: break;  // unbekannte Befehle werden ignoriert
  }
}

static void readSerial() {
  int budget = 256;  // nicht ewig lesen, Tasten haben Vorrang
  while (Serial.available() > 0 && budget-- > 0) {
    if (lineReader.feed((char)Serial.read())) handleLine(lineReader.line());
  }
}

// ---------------------------------------------------------------- Tasten
static void scanKeys(uint32_t now) {
  for (uint8_t i = 0; i < KEY_COUNT; i++) {
    const bool raw = digitalRead(KEY_PINS[i]) == LOW;  // gedrückt = LOW
    if (!debouncer.update(i, raw, now)) continue;

    const bool pressed = debouncer.isPressed(i);
    char buf[12];
    sendRaw(buf, proto::formatKey(buf, sizeof(buf), pressed, i));

    if (pressed) {
      flashUntil = now + FLASH_MS;
      if (keyTestActive) keyTestOnPress(i);
    }
  }
}

// ---------------------------------------------------------------- LED
static void updateLed(uint32_t now) {
  if (ledTestStep >= 0) {
    if ((int32_t)(now - ledTestNext) >= 0) {
      if (ledTestStep >= LEDTEST_STEPS) {
        ledTestStep = -1;
        sendLine("LEDTEST DONE");
      } else {
        Serial.printf("LEDTEST STEP %d\n", (int)ledTestStep);
        setLed(LEDTEST_SEQ[ledTestStep++]);
        ledTestNext = now + 500;
      }
    }
    if (ledTestStep >= 0) return;
  }

  const bool connected = connection.isConnected(now);
  if (connected != wasConnected) {
    wasConnected = connected;
    if (!connected) appColor = COLOR_CONNECTED;  // App-Farbe zurücksetzen
  }

  if ((int32_t)(flashUntil - now) > 0)  setLed(COLOR_FLASH);
  else if (keyTestActive)               setLed(COLOR_KEYTEST);
  else if (connected)                   setLed(appColor);
  else                                  setLed(COLOR_DISCONNECTED);
}

// ---------------------------------------------------------------- Arduino
void setup() {
  for (uint8_t i = 0; i < KEY_COUNT; i++) pinMode(KEY_PINS[i], INPUT_PULLUP);

  // Baudrate ist bei USB-CDC bedeutungslos. Ohne angeschlossenen PC verwirft der
  // Core Ausgaben selbst (Standard-Timeout 100 ms), daher kein Blockieren.
  Serial.begin(115200);

  setLed(COLOR_DISCONNECTED);
  sendHello();
}

void loop() {
  readSerial();

  const uint32_t now = millis();
  if (now != lastScanMs) {  // Abfrage jede Millisekunde
    lastScanMs = now;
    scanKeys(now);
    updateLed(now);
  }
}

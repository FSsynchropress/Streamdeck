// Serielles Protokoll (Version 1) + Diagnosebefehle – reines C++, nativ testbar.
#pragma once
#include <stdint.h>
#include <stddef.h>

namespace proto {

static const uint8_t VERSION = 1;

enum class CmdType : uint8_t {
  None,       // leere Zeile
  Unknown,    // unbekannt oder ungültige Parameter -> wird ignoriert
  // --- Protokoll V1 (von der App benutzt) ---
  Hello,      // HELLO?
  Ping,       // PING
  Led,        // LED <r> <g> <b>
  // --- Diagnose (für den seriellen Monitor, die App ignoriert die Antworten) ---
  Help,       // HELP
  Info,       // INFO
  State,      // STATE
  SelfTest,   // SELFTEST
  KeyTest,    // KEYTEST
  Stop,       // STOP
  LedTest,    // LEDTEST
};

struct Command {
  CmdType type = CmdType::None;
  uint8_t r = 0, g = 0, b = 0;
};

// Wertet eine Zeile aus (ohne \n). Groß-/Kleinschreibung und Leerzeichen am Rand egal.
Command parse(const char* line);

// Sammelt Zeichen zu Zeilen. Versteht \n, \r und \r\n. Zu lange Zeilen werden verworfen.
class LineReader {
public:
  static const size_t MAX_LEN = 63;

  // true = eine vollständige, nicht leere Zeile liegt in line() bereit
  bool feed(char c);
  const char* line() const { return buf_; }

private:
  char buf_[MAX_LEN + 1] = {0};
  size_t len_ = 0;
  bool overflow_ = false;
};

// Formatiert Nachrichten Gerät -> PC (mit \n). Rückgabe: Länge ohne Nullterminator.
size_t formatHello(char* out, size_t size, uint8_t keyCount);
size_t formatKey(char* out, size_t size, bool pressed, uint8_t key);  // "D 3\n" / "U 3\n"
size_t formatState(char* out, size_t size, uint32_t mask, uint8_t keyCount);  // "STATE 0100...\n"

}  // namespace proto

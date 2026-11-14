#include "Protocol.h"
#include <stdio.h>
#include <string.h>
#include <ctype.h>

namespace proto {

namespace {

// Liest eine Ganzzahl 0..255 aus einem Token. Nur Ziffern erlaubt.
bool parseByte(const char* tok, uint8_t& out) {
  if (!tok || !*tok) return false;
  unsigned v = 0;
  for (const char* p = tok; *p; ++p) {
    if (!isdigit((unsigned char)*p)) return false;
    v = v * 10 + (unsigned)(*p - '0');
    if (v > 255) return false;
  }
  out = (uint8_t)v;
  return true;
}

}  // namespace

Command parse(const char* line) {
  Command cmd;
  if (!line) return cmd;

  // Kopie in Großbuchstaben, Ränder abschneiden
  char buf[LineReader::MAX_LEN + 1];
  size_t n = 0;
  while (*line && isspace((unsigned char)*line)) ++line;
  while (*line && n < sizeof(buf) - 1) buf[n++] = (char)toupper((unsigned char)*line++);
  while (n > 0 && isspace((unsigned char)buf[n - 1])) --n;
  buf[n] = '\0';
  if (n == 0) return cmd;  // None

  // In Tokens zerlegen (mehr als 4 sind immer ungültig)
  char* tok[5] = {nullptr};
  int count = 0;
  char* p = buf;
  while (*p) {
    while (*p == ' ' || *p == '\t') *p++ = '\0';
    if (!*p) break;
    if (count == 5) { count = 6; break; }
    tok[count++] = p;
    while (*p && *p != ' ' && *p != '\t') ++p;
  }

  cmd.type = CmdType::Unknown;
  const char* c = tok[0];

  if (count == 1) {
    if (!strcmp(c, "HELLO?"))        cmd.type = CmdType::Hello;
    else if (!strcmp(c, "PING"))     cmd.type = CmdType::Ping;
    else if (!strcmp(c, "HELP") || !strcmp(c, "?")) cmd.type = CmdType::Help;
    else if (!strcmp(c, "INFO"))     cmd.type = CmdType::Info;
    else if (!strcmp(c, "STATE"))    cmd.type = CmdType::State;
    else if (!strcmp(c, "SELFTEST")) cmd.type = CmdType::SelfTest;
    else if (!strcmp(c, "KEYTEST"))  cmd.type = CmdType::KeyTest;
    else if (!strcmp(c, "STOP"))     cmd.type = CmdType::Stop;
    else if (!strcmp(c, "LEDTEST"))  cmd.type = CmdType::LedTest;
  } else if (count == 4 && !strcmp(c, "LED")) {
    if (parseByte(tok[1], cmd.r) && parseByte(tok[2], cmd.g) && parseByte(tok[3], cmd.b)) {
      cmd.type = CmdType::Led;
    } else {
      cmd.r = cmd.g = cmd.b = 0;
    }
  }
  return cmd;
}

bool LineReader::feed(char c) {
  if (c == '\n' || c == '\r') {
    const bool ready = !overflow_ && len_ > 0;
    buf_[len_] = '\0';
    len_ = 0;
    overflow_ = false;
    return ready;  // Leere Zeilen (z. B. das \n nach \r) werden übersprungen
  }
  if (overflow_) return false;
  if (len_ >= MAX_LEN) {
    overflow_ = true;  // Rest der Zeile bis zum Zeilenende verwerfen
    return false;
  }
  buf_[len_++] = c;
  return false;
}

size_t formatHello(char* out, size_t size, uint8_t keyCount) {
  int n = snprintf(out, size, "HELLO STREAMDECK %u %u\n", (unsigned)VERSION, (unsigned)keyCount);
  return n < 0 ? 0 : (size_t)n;
}

size_t formatKey(char* out, size_t size, bool pressed, uint8_t key) {
  int n = snprintf(out, size, "%c %u\n", pressed ? 'D' : 'U', (unsigned)key);
  return n < 0 ? 0 : (size_t)n;
}

size_t formatState(char* out, size_t size, uint32_t mask, uint8_t keyCount) {
  if (size == 0) return 0;
  int n = snprintf(out, size, "STATE ");
  if (n < 0 || (size_t)n >= size) return 0;
  size_t pos = (size_t)n;
  for (uint8_t i = 0; i < keyCount && pos + 2 < size; i++) {
    out[pos++] = (mask & (1UL << i)) ? '1' : '0';
  }
  out[pos++] = '\n';
  out[pos] = '\0';
  return pos;
}

}  // namespace proto

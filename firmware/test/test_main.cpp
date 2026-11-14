// Unit-Tests für die hardwareunabhängigen Firmware-Teile (läuft auf dem PC).
// Bauen & starten: test\run_tests.bat (MSVC) oder
//   g++ -std=c++17 -I../StreamDeck test_main.cpp ../StreamDeck/Debouncer.cpp ../StreamDeck/Protocol.cpp
#include <cstdio>
#include <cstring>
#include <string>

#include "Debouncer.h"
#include "Protocol.h"
#include "Connection.h"

// ---------------------------------------------------------------- Mini-Testframework
static int g_failed = 0, g_checks = 0;
static const char* g_test = "";

#define CHECK(cond)                                                                     \
  do {                                                                                  \
    g_checks++;                                                                         \
    if (!(cond)) {                                                                      \
      g_failed++;                                                                       \
      std::printf("  FAIL [%s] %s:%d: %s\n", g_test, __FILE__, __LINE__, #cond);       \
    }                                                                                   \
  } while (0)

#define CHECK_EQ(a, b) CHECK((a) == (b))
#define CHECK_STR(a, b) CHECK(std::string(a) == std::string(b))
#define TEST(name) static void name()
#define RUN(name) do { g_test = #name; std::printf("- %s\n", #name); name(); } while (0)

using proto::CmdType;

// Speist eine ganze Zeichenkette ein und liefert die zuletzt fertige Zeile ("" = keine)
static std::string feedAll(proto::LineReader& r, const char* s, int* lines = nullptr) {
  std::string last;
  int n = 0;
  for (; *s; ++s) {
    if (r.feed(*s)) { last = r.line(); n++; }
  }
  if (lines) *lines = n;
  return last;
}

// ================================================================ Debouncer
TEST(debounce_press_reported_after_stable_time) {
  Debouncer d(15, 20);
  CHECK(!d.update(3, true, 1000));   // Flanke erkannt, Timer startet
  CHECK(!d.update(3, true, 1019));   // 19 ms: noch nicht
  CHECK(!d.isPressed(3));
  CHECK(d.update(3, true, 1020));    // 20 ms: jetzt gedrückt
  CHECK(d.isPressed(3));
  CHECK(!d.update(3, true, 1050));   // kein zweites Ereignis
}

TEST(debounce_release_reported_after_stable_time) {
  Debouncer d(15, 20);
  d.update(0, true, 0);
  d.update(0, true, 20);
  CHECK(d.isPressed(0));
  CHECK(!d.update(0, false, 100));
  CHECK(!d.update(0, false, 119));
  CHECK(d.update(0, false, 120));
  CHECK(!d.isPressed(0));
}

TEST(debounce_bouncing_is_filtered) {
  Debouncer d(15, 20);
  // Prellen: an/aus/an/aus/an im Abstand von 2 ms (t = 0, 2, 4, 6, 8)
  uint32_t t = 0;
  bool v = true;
  int events = 0;
  for (int i = 0; i < 5; i++, t += 2, v = !v) events += d.update(5, v, t);
  // letzte Flanke ("an") bei t = 8 -> stabil ab t = 28
  for (t = 9; t < 28; t++) events += d.update(5, true, t);
  CHECK_EQ(events, 0);
  CHECK(d.update(5, true, 28));
  CHECK(d.isPressed(5));
  CHECK_EQ(d.bounceCount(5), 5);  // 5 Rohflanken bis zum stabilen Druck
}

TEST(debounce_clean_press_has_one_edge) {
  Debouncer d(15, 20);
  d.update(4, true, 0);
  d.update(4, true, 20);
  CHECK_EQ(d.bounceCount(4), 1);
}

TEST(debounce_short_glitch_ignored) {
  Debouncer d(15, 20);
  d.update(1, true, 0);
  d.update(1, false, 5);  // 5-ms-Störimpuls
  int events = 0;
  for (uint32_t t = 5; t < 100; t++) events += d.update(1, false, t);
  CHECK_EQ(events, 0);
  CHECK(!d.isPressed(1));
}

TEST(debounce_keys_are_independent) {
  Debouncer d(15, 20);
  d.update(0, true, 0);
  d.update(14, true, 10);
  CHECK(d.update(0, true, 20));
  CHECK(!d.update(14, true, 20));
  CHECK(d.update(14, true, 30));
  CHECK(d.isPressed(0));
  CHECK(d.isPressed(14));
  CHECK(!d.isPressed(7));
}

TEST(debounce_millis_overflow) {
  Debouncer d(15, 20);
  const uint32_t start = 0xFFFFFFF0u;  // 16 ms vor Überlauf
  d.update(2, true, start);
  CHECK(!d.update(2, true, start + 10));
  CHECK(d.update(2, true, start + 20));  // läuft über 0 hinweg
  CHECK(d.isPressed(2));
}

TEST(debounce_out_of_range_key) {
  Debouncer d(15, 20);
  CHECK(!d.update(15, true, 0));
  CHECK(!d.update(15, true, 100));
  CHECK(!d.isPressed(15));
  CHECK(!d.isPressed(200));
  CHECK_EQ(d.keyCount(), 15);
}

// ================================================================ Parser
TEST(parse_protocol_commands) {
  CHECK(proto::parse("HELLO?").type == CmdType::Hello);
  CHECK(proto::parse("PING").type == CmdType::Ping);
  proto::Command c = proto::parse("LED 255 0 128");
  CHECK(c.type == CmdType::Led);
  CHECK_EQ(c.r, 255);
  CHECK_EQ(c.g, 0);
  CHECK_EQ(c.b, 128);
}

TEST(parse_is_lenient_with_case_and_whitespace) {
  CHECK(proto::parse("  ping \t").type == CmdType::Ping);
  CHECK(proto::parse("hello?").type == CmdType::Hello);
  CHECK(proto::parse("led  1   2  3 ").type == CmdType::Led);
}

TEST(parse_diagnostic_commands) {
  CHECK(proto::parse("HELP").type == CmdType::Help);
  CHECK(proto::parse("?").type == CmdType::Help);
  CHECK(proto::parse("INFO").type == CmdType::Info);
  CHECK(proto::parse("STATE").type == CmdType::State);
  CHECK(proto::parse("SELFTEST").type == CmdType::SelfTest);
  CHECK(proto::parse("keytest").type == CmdType::KeyTest);
  CHECK(proto::parse("STOP").type == CmdType::Stop);
  CHECK(proto::parse("LEDTEST").type == CmdType::LedTest);
}

TEST(parse_rejects_invalid_input) {
  CHECK(proto::parse("").type == CmdType::None);
  CHECK(proto::parse("   ").type == CmdType::None);
  CHECK(proto::parse(nullptr).type == CmdType::None);
  CHECK(proto::parse("HELLO").type == CmdType::Unknown);       // ohne ?
  CHECK(proto::parse("PING 1").type == CmdType::Unknown);      // Extra-Parameter
  CHECK(proto::parse("LED 256 0 0").type == CmdType::Unknown); // > 255
  CHECK(proto::parse("LED -1 0 0").type == CmdType::Unknown);
  CHECK(proto::parse("LED 1 2").type == CmdType::Unknown);     // zu wenige
  CHECK(proto::parse("LED 1 2 3 4").type == CmdType::Unknown); // zu viele
  CHECK(proto::parse("LED a b c").type == CmdType::Unknown);
  CHECK(proto::parse("LED 1 2 3 4 5 6").type == CmdType::Unknown);
  CHECK(proto::parse("FOO").type == CmdType::Unknown);
  CHECK(proto::parse("D 3").type == CmdType::Unknown);         // Echo der eigenen Ausgabe
}

// ================================================================ LineReader
TEST(linereader_lf_crlf_cr) {
  proto::LineReader r;
  int n = 0;
  CHECK_STR(feedAll(r, "PING\n", &n), "PING");
  CHECK_EQ(n, 1);
  CHECK_STR(feedAll(r, "HELLO?\r\n", &n), "HELLO?");
  CHECK_EQ(n, 1);  // \r\n ergibt genau eine Zeile
  CHECK_STR(feedAll(r, "INFO\r", &n), "INFO");
  CHECK_EQ(n, 1);
}

TEST(linereader_multiple_lines_in_one_chunk) {
  proto::LineReader r;
  int n = 0;
  std::string last = feedAll(r, "PING\nPING\nSTATE\n", &n);
  CHECK_EQ(n, 3);
  CHECK_STR(last, "STATE");
}

TEST(linereader_partial_line_waits) {
  proto::LineReader r;
  int n = 0;
  feedAll(r, "PI", &n);
  CHECK_EQ(n, 0);
  CHECK_STR(feedAll(r, "NG\n", &n), "PING");
  CHECK_EQ(n, 1);
}

TEST(linereader_overflow_discards_line_and_recovers) {
  proto::LineReader r;
  std::string longLine(200, 'X');
  longLine += "\n";
  int n = 0;
  feedAll(r, longLine.c_str(), &n);
  CHECK_EQ(n, 0);  // zu lange Zeile wird verworfen
  CHECK_STR(feedAll(r, "PING\n", &n), "PING");
  CHECK_EQ(n, 1);
}

TEST(linereader_max_length_line_is_accepted) {
  proto::LineReader r;
  std::string line(proto::LineReader::MAX_LEN, 'A');
  int n = 0;
  CHECK_STR(feedAll(r, (line + "\n").c_str(), &n), line);
  CHECK_EQ(n, 1);
}

// ================================================================ Ausgabeformate
TEST(format_hello) {
  char buf[64];
  size_t n = proto::formatHello(buf, sizeof(buf), 15);
  CHECK_STR(buf, "HELLO STREAMDECK 1 15\n");
  CHECK_EQ(n, std::strlen(buf));
}

TEST(format_key_events) {
  char buf[16];
  proto::formatKey(buf, sizeof(buf), true, 0);
  CHECK_STR(buf, "D 0\n");
  size_t n = proto::formatKey(buf, sizeof(buf), false, 14);
  CHECK_STR(buf, "U 14\n");
  CHECK_EQ(n, 5u);
}

TEST(format_state) {
  char buf[40];
  uint32_t mask = (1u << 0) | (1u << 3) | (1u << 14);
  size_t n = proto::formatState(buf, sizeof(buf), mask, 15);
  CHECK_STR(buf, "STATE 100100000000001\n");
  CHECK_EQ(n, std::strlen(buf));
}

TEST(format_state_small_buffer_is_safe) {
  char buf[10];
  size_t n = proto::formatState(buf, sizeof(buf), 0x7FFF, 15);
  CHECK(n < sizeof(buf));
  CHECK_EQ(n, std::strlen(buf));
  CHECK(buf[n - 1] == '\n');
}

// Die App erkennt Tastenereignisse an "D "/"U " und die Kennung an "HELLO STREAMDECK".
// Diagnoseausgaben dürfen damit nicht kollidieren.
TEST(format_diagnostic_prefixes_do_not_collide) {
  const char* diag[] = {"STATE ", "INFO ", "TEST ", "KEYTEST ", "LEDTEST ", "# "};
  for (const char* p : diag) {
    CHECK(std::strncmp(p, "D ", 2) != 0);
    CHECK(std::strncmp(p, "U ", 2) != 0);
    CHECK(std::strncmp(p, "HELLO", 5) != 0);
    CHECK(std::strncmp(p, "PONG", 4) != 0);
  }
}

// ================================================================ Verbindung
TEST(connection_initially_disconnected) {
  ConnectionMonitor c(3000);
  CHECK(!c.isConnected(0));
  CHECK(!c.isConnected(10000));
}

TEST(connection_ping_keeps_alive_for_3s) {
  ConnectionMonitor c(3000);
  c.onPing(5000);
  CHECK(c.isConnected(5000));
  CHECK(c.isConnected(8000));
  CHECK(!c.isConnected(8001));
  c.onPing(8500);
  CHECK(c.isConnected(9000));
}

TEST(connection_survives_millis_overflow) {
  ConnectionMonitor c(3000);
  c.onPing(0xFFFFFF00u);
  CHECK(c.isConnected(0x00000100u));  // 512 ms später, nach Überlauf
  CHECK(!c.isConnected(0x00000C00u)); // > 3 s später
}

TEST(connection_reset) {
  ConnectionMonitor c(3000);
  c.onPing(100);
  c.reset();
  CHECK(!c.isConnected(200));
}

// ================================================================ main
int main() {
  std::printf("StreamDeck Firmware - Unit-Tests\n");
  RUN(debounce_press_reported_after_stable_time);
  RUN(debounce_release_reported_after_stable_time);
  RUN(debounce_bouncing_is_filtered);
  RUN(debounce_clean_press_has_one_edge);
  RUN(debounce_short_glitch_ignored);
  RUN(debounce_keys_are_independent);
  RUN(debounce_millis_overflow);
  RUN(debounce_out_of_range_key);
  RUN(parse_protocol_commands);
  RUN(parse_is_lenient_with_case_and_whitespace);
  RUN(parse_diagnostic_commands);
  RUN(parse_rejects_invalid_input);
  RUN(linereader_lf_crlf_cr);
  RUN(linereader_multiple_lines_in_one_chunk);
  RUN(linereader_partial_line_waits);
  RUN(linereader_overflow_discards_line_and_recovers);
  RUN(linereader_max_length_line_is_accepted);
  RUN(format_hello);
  RUN(format_key_events);
  RUN(format_state);
  RUN(format_state_small_buffer_is_safe);
  RUN(format_diagnostic_prefixes_do_not_collide);
  RUN(connection_initially_disconnected);
  RUN(connection_ping_keeps_alive_for_3s);
  RUN(connection_survives_millis_overflow);
  RUN(connection_reset);

  std::printf("\n%d Pruefungen, %d fehlgeschlagen -> %s\n", g_checks, g_failed, g_failed ? "FEHLER" : "OK");
  return g_failed ? 1 : 0;
}

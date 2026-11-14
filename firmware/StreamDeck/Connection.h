// "App verbunden"-Erkennung: verbunden, solange spätestens alle 3 s ein PING kommt.
#pragma once
#include <stdint.h>

class ConnectionMonitor {
public:
  explicit ConnectionMonitor(uint32_t timeoutMs = 3000) : timeoutMs_(timeoutMs) {}

  void onPing(uint32_t nowMs) {
    lastPing_ = nowMs;
    seen_ = true;
  }

  bool isConnected(uint32_t nowMs) const {
    return seen_ && (uint32_t)(nowMs - lastPing_) <= timeoutMs_;
  }

  void reset() { seen_ = false; }

private:
  uint32_t timeoutMs_;
  uint32_t lastPing_ = 0;
  bool seen_ = false;
};

#include "Debouncer.h"

Debouncer::Debouncer(uint8_t keyCount, uint16_t stableMs)
    : count_(keyCount > MAX_KEYS ? MAX_KEYS : keyCount), stableMs_(stableMs) {
  reset();
}

void Debouncer::reset() {
  stable_ = 0;
  raw_ = 0;
  for (uint8_t i = 0; i < MAX_KEYS; i++) {
    lastChange_[i] = 0;
    bounces_[i] = 0;
    lastBounces_[i] = 0;
  }
}

bool Debouncer::update(uint8_t key, bool rawPressed, uint32_t nowMs) {
  if (key >= count_) return false;
  const uint32_t bit = 1UL << key;

  if (((raw_ & bit) != 0) != rawPressed) {
    // Rohwert hat sich geändert -> Stabilitäts-Timer neu starten
    if (rawPressed) raw_ |= bit; else raw_ &= ~bit;
    lastChange_[key] = nowMs;
    if (bounces_[key] < 0xFFFF) bounces_[key]++;
    return false;
  }

  const bool stablePressed = (stable_ & bit) != 0;
  if (stablePressed == rawPressed) return false;

  // Unsigned-Subtraktion: robust gegen millis()-Überlauf nach ~49 Tagen
  if ((uint32_t)(nowMs - lastChange_[key]) >= stableMs_) {
    if (rawPressed) stable_ |= bit; else stable_ &= ~bit;
    lastBounces_[key] = bounces_[key];
    bounces_[key] = 0;
    return true;
  }
  return false;
}

bool Debouncer::isPressed(uint8_t key) const {
  if (key >= count_) return false;
  return (stable_ & (1UL << key)) != 0;
}

uint16_t Debouncer::bounceCount(uint8_t key) const {
  if (key >= count_) return 0;
  return lastBounces_[key];
}

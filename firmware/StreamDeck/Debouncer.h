// Entprellung für bis zu 32 Tasten – reines C++, ohne Arduino-Abhängigkeit (nativ testbar).
#pragma once
#include <stdint.h>

class Debouncer {
public:
  static const uint8_t MAX_KEYS = 32;

  // keyCount: Anzahl Tasten, stableMs: so lange muss ein Rohwert stabil sein (ca. 20 ms)
  explicit Debouncer(uint8_t keyCount, uint16_t stableMs = 20);

  // Rohwert einer Taste einspeisen. Liefert true, wenn sich der entprellte Zustand geändert hat.
  bool update(uint8_t key, bool rawPressed, uint32_t nowMs);

  bool isPressed(uint8_t key) const;
  uint8_t keyCount() const { return count_; }

  // Anzahl Roh-Flanken, die zur letzten stabilen Änderung geführt haben (1 = sauber, >1 = Prellen)
  uint16_t bounceCount(uint8_t key) const;

  void reset();

private:
  uint8_t count_;
  uint16_t stableMs_;
  uint32_t stable_;           // Bitmaske: entprellter Zustand
  uint32_t raw_;              // Bitmaske: letzter Rohwert
  uint32_t lastChange_[MAX_KEYS];
  uint16_t bounces_[MAX_KEYS];
  uint16_t lastBounces_[MAX_KEYS];
};

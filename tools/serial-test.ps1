<#
.SYNOPSIS
  Testet die Stream-Deck-Firmware direkt über die serielle Schnittstelle (ohne App, ohne Python).

.DESCRIPTION
  Findet das Gerät automatisch (HELLO? an alle COM-Ports) und prüft das Protokoll:
  HELLO, PING/PONG, Zeilenenden, unbekannte Befehle, LED, STATE, SELFTEST und optional
  den Verbindungs-Timeout. Mit -KeyTest werden alle Tasten interaktiv geprüft, mit
  -Monitor verhält sich das Skript wie die App (PING jede Sekunde) und zeigt D/U live an.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\serial-test.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\serial-test.ps1 -Port COM7 -KeyTest
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\serial-test.ps1 -Monitor
#>
[CmdletBinding()]
param(
  [string]$Port,          # z. B. COM7; leer = automatisch suchen
  [switch]$KeyTest,       # danach alle 15 Tasten nacheinander drücken
  [switch]$Monitor,       # danach Live-Anzeige der Tastenereignisse (Strg+C beendet)
  [switch]$Timeout,       # zusätzlich den 3-s-Verbindungs-Timeout prüfen (dauert ~4 s)
  [int]$KeyTestSeconds = 120
)

$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0
$script:startTime = [DateTime]::Now
$script:otherLines = @()

# ------------------------------------------------------------------ Ausgabe
function Write-Pass([string]$msg) { $script:passed++; Write-Host "  [OK]   $msg" -ForegroundColor Green }
function Write-Fail([string]$msg) { $script:failed++; Write-Host "  [FAIL] $msg" -ForegroundColor Red }
function Write-Info([string]$msg) { Write-Host "         $msg" -ForegroundColor DarkGray }
function Write-Event([string]$line) {
  $ms = [int]([DateTime]::Now - $script:startTime).TotalMilliseconds
  Write-Host ("  {0,8} ms  <- {1}" -f $ms, $line) -ForegroundColor Cyan
}

# ------------------------------------------------------------------ Seriell
function Open-Port([string]$name) {
  $sp = New-Object System.IO.Ports.SerialPort $name, 115200
  $sp.NewLine = "`n"
  $sp.ReadTimeout = 50
  $sp.WriteTimeout = 500
  $sp.Encoding = [System.Text.Encoding]::ASCII
  # DTR/RTS bleiben aus: beim ESP32-S3 (USB-Serial-JTAG) können sie einen Reset auslösen.
  $sp.DtrEnable = $false
  $sp.RtsEnable = $false
  $sp.Open()
  return $sp
}

function Send([System.IO.Ports.SerialPort]$sp, [string]$text) {
  Write-Verbose "-> $text"
  $sp.Write("$text`n")
}

# Liest eine Zeile oder $null nach Timeout
function Read-LineOrNull([System.IO.Ports.SerialPort]$sp) {
  try { return $sp.ReadLine().TrimEnd("`r") } catch [System.TimeoutException] { return $null }
}

# Wartet auf eine Zeile, die zum Muster passt. D/U-Ereignisse werden angezeigt und übersprungen.
# Alle anderen gelesenen Zeilen landen in $script:otherLines.
function Wait-Line([System.IO.Ports.SerialPort]$sp, [string]$pattern, [int]$timeoutMs = 1000) {
  $script:otherLines = @()
  $deadline = [DateTime]::Now.AddMilliseconds($timeoutMs)
  while ([DateTime]::Now -lt $deadline) {
    $line = Read-LineOrNull $sp
    if ($null -eq $line -or $line -eq '') { continue }
    Write-Verbose "<- $line"
    if ($line -match '^[DU] \d+$') { Write-Event $line; continue }
    if ($line -match $pattern) { return $line }
    $script:otherLines += $line
  }
  return $null
}

# Sammelt alle Zeilen für eine Zeitspanne (ohne D/U)
function Collect-Lines([System.IO.Ports.SerialPort]$sp, [int]$durationMs) {
  $lines = @()
  $deadline = [DateTime]::Now.AddMilliseconds($durationMs)
  while ([DateTime]::Now -lt $deadline) {
    $line = Read-LineOrNull $sp
    if ($null -eq $line -or $line -eq '') { continue }
    Write-Verbose "<- $line"
    if ($line -match '^[DU] \d+$') { Write-Event $line; continue }
    $lines += $line
  }
  return ,$lines
}

function Find-Device {
  $ports = [System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object -Unique
  if (-not $ports) { throw "Keine COM-Ports gefunden. Ist das Board an der nativen USB-Buchse angeschlossen?" }
  Write-Host "Suche Stream Deck auf: $($ports -join ', ')"
  foreach ($p in $ports) {
    $sp = $null
    try {
      $sp = Open-Port $p
      Start-Sleep -Milliseconds 100
      $sp.DiscardInBuffer()
      for ($try = 0; $try -lt 2; $try++) {
        Send $sp 'HELLO?'
        $hello = Wait-Line $sp '^HELLO STREAMDECK' 700
        if ($hello) { Write-Host "Gefunden auf $p -> $hello" -ForegroundColor Green; return $sp }
      }
      $sp.Close()
    } catch {
      Write-Info "$p : $($_.Exception.Message)"
      if ($sp -and $sp.IsOpen) { $sp.Close() }
    }
  }
  throw "Kein Gerät hat mit 'HELLO STREAMDECK' geantwortet. Serieller Monitor der Arduino IDE noch offen?"
}

# ------------------------------------------------------------------ Tests
function Test-Protocol([System.IO.Ports.SerialPort]$sp) {
  Write-Host "`n== Protokoll V1 =="

  # 1) Kennung
  Send $sp 'HELLO?'
  $hello = Wait-Line $sp '^HELLO ' 1000
  if ($hello -eq 'HELLO STREAMDECK 1 15') { Write-Pass "HELLO? -> '$hello'" }
  elseif ($hello) { Write-Fail "HELLO? -> unerwartet '$hello' (erwartet 'HELLO STREAMDECK 1 15')" }
  else { Write-Fail 'HELLO? -> keine Antwort' }

  # 2) PING/PONG + Antwortzeit
  $times = @()
  $lost = 0
  for ($i = 0; $i -lt 10; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Send $sp 'PING'
    $r = Wait-Line $sp '^PONG$' 500
    $sw.Stop()
    if ($r) { $times += $sw.Elapsed.TotalMilliseconds } else { $lost++ }
  }
  if ($lost -eq 0) {
    $avg = ($times | Measure-Object -Average).Average
    $max = ($times | Measure-Object -Maximum).Maximum
    Write-Pass ("10x PING -> PONG (Antwortzeit avg {0:N1} ms, max {1:N1} ms)" -f $avg, $max)
  } else { Write-Fail "PING: $lost von 10 ohne PONG" }

  # 3) Zeilenenden \r\n und \r
  $sp.Write("PING`r`n")
  $lines = Collect-Lines $sp 300
  $pongs = @($lines | Where-Object { $_ -eq 'PONG' }).Count
  if ($pongs -eq 1) { Write-Pass 'PING mit \r\n -> genau ein PONG' } else { Write-Fail "PING mit \r\n -> $pongs PONG" }

  $sp.Write("PING`r")
  $r = Wait-Line $sp '^PONG$' 500
  if ($r) { Write-Pass 'PING mit \r -> PONG' } else { Write-Fail 'PING mit \r -> keine Antwort' }

  # 4) Zwei Befehle in einem Paket
  $sp.Write("PING`nPING`n")
  $lines = Collect-Lines $sp 300
  $pongs = @($lines | Where-Object { $_ -eq 'PONG' }).Count
  if ($pongs -eq 2) { Write-Pass 'Zwei PINGs in einem Paket -> zwei PONG' } else { Write-Fail "Zwei PINGs -> $pongs PONG" }

  # 5) Unbekannte / ungültige Befehle werden ignoriert
  foreach ($bad in @('FOO', 'LED 300 0 0', 'LED 1 2', 'PING 1', ('X' * 200))) {
    Send $sp $bad
  }
  $lines = Collect-Lines $sp 400
  if ($lines.Count -eq 0) { Write-Pass 'Unbekannte/ungueltige Befehle werden still ignoriert' }
  else { Write-Fail "Antwort auf ungueltige Befehle: $($lines -join ' | ')" }

  Send $sp 'PING'
  if (Wait-Line $sp '^PONG$' 500) { Write-Pass 'Geraet reagiert danach weiterhin (ueberlange Zeile verkraftet)' }
  else { Write-Fail 'Keine Antwort mehr nach ungueltigen Befehlen' }

  # 6) LED-Befehl (keine Antwort erwartet, die Farbe prüft man mit den Augen)
  Send $sp 'LED 0 0 60'
  $lines = Collect-Lines $sp 200
  if ($lines.Count -eq 0) { Write-Pass 'LED 0 0 60 angenommen (LED sollte kurz blau sein)' }
  else { Write-Fail "LED -> unerwartete Antwort: $($lines -join ' | ')" }
  Start-Sleep -Milliseconds 700
  Send $sp 'LED 0 40 0'
}

function Test-Diagnostics([System.IO.Ports.SerialPort]$sp) {
  Write-Host "`n== Diagnose =="

  Send $sp 'PING'
  [void](Wait-Line $sp '^PONG$' 500)
  Send $sp 'INFO'
  $info = @(Collect-Lines $sp 400 | Where-Object { $_ -like 'INFO *' })
  if ($info.Count -ge 3) {
    Write-Pass 'INFO'
    $info | ForEach-Object { Write-Info $_ }
    if ($info -match 'APP CONNECTED') { Write-Pass 'Geraet meldet App verbunden (nach PING)' }
    else { Write-Fail 'Geraet meldet nach PING nicht CONNECTED' }
  } else { Write-Fail "INFO -> $($info.Count) Zeilen" }

  Send $sp 'STATE'
  $state = Wait-Line $sp '^STATE ' 500
  if ($state -match '^STATE [01]{15}$') {
    if ($state -eq 'STATE 000000000000000') { Write-Pass 'STATE -> keine Taste gedrueckt' }
    else { Write-Fail "STATE -> $state (Taste gedrueckt oder klemmt?)" }
  } else { Write-Fail "STATE -> '$state'" }

  Send $sp 'SELFTEST'
  $result = Wait-Line $sp '^TEST (OK|FAIL \d+)' 2000
  $script:otherLines | Where-Object { $_ -like 'TEST FAIL*' } | ForEach-Object { Write-Info $_ }
  if ($result -like 'TEST OK*') { Write-Pass "SELFTEST -> $result (kein Pin auf GND, keine Kurzschluesse)" }
  elseif ($result) { Write-Fail "SELFTEST -> $result" }
  else { Write-Fail 'SELFTEST -> keine Antwort' }

  Send $sp 'HELP'
  $help = @(Collect-Lines $sp 400 | Where-Object { $_ -like '# *' })
  if ($help.Count -ge 5) { Write-Pass "HELP ($($help.Count) Zeilen)" } else { Write-Fail 'HELP unvollstaendig' }
}

function Test-ConnectionTimeout([System.IO.Ports.SerialPort]$sp) {
  Write-Host "`n== Verbindungs-Timeout (3 s ohne PING) =="
  Send $sp 'PING'
  [void](Wait-Line $sp '^PONG$' 500)
  Write-Info 'warte 3,5 s ohne PING (LED sollte auf rot wechseln) ...'
  [void](Collect-Lines $sp 3500)
  Send $sp 'INFO'
  $info = Collect-Lines $sp 400
  if ($info -match 'APP DISCONNECTED') { Write-Pass 'Nach 3,5 s ohne PING: DISCONNECTED' }
  else { Write-Fail 'Geraet meldet nach 3,5 s ohne PING noch CONNECTED' }
}

function Run-KeyTest([System.IO.Ports.SerialPort]$sp) {
  Write-Host "`n== Tastentest: bitte alle 15 Tasten nacheinander druecken (Strg+C bricht ab) =="
  Send $sp 'KEYTEST'
  $deadline = [DateTime]::Now.AddSeconds($KeyTestSeconds)
  $nextPing = [DateTime]::Now
  try {
    while ([DateTime]::Now -lt $deadline) {
      if ([DateTime]::Now -ge $nextPing) { Send $sp 'PING'; $nextPing = [DateTime]::Now.AddSeconds(1) }
      $line = Read-LineOrNull $sp
      if (-not $line -or $line -eq 'PONG') { continue }
      if ($line -match '^[DU] \d+$') { Write-Verbose "<- $line"; continue }
      if ($line -like 'KEYTEST KEY*') {
        $color = 'White'
        if ($line -match 'EDGES (\d+)' -and [int]$Matches[1] -gt 3) { $color = 'Yellow' }  # starkes Prellen
        Write-Host "  $line" -ForegroundColor $color
      } elseif ($line -like 'KEYTEST DONE*') {
        Write-Pass $line
        return
      } elseif ($line -like 'KEYTEST*') {
        Write-Info $line
      }
    }
    Write-Fail "Tastentest nach $KeyTestSeconds s nicht abgeschlossen"
  } finally {
    Send $sp 'STOP'
    foreach ($l in (Collect-Lines $sp 300)) { if ($l -like 'KEYTEST*') { Write-Info $l } }
  }
}

function Run-Monitor([System.IO.Ports.SerialPort]$sp) {
  Write-Host "`n== Live-Monitor (verhaelt sich wie die App: PING jede Sekunde). Strg+C beendet. =="
  $downAt = @{}
  $nextPing = [DateTime]::Now
  while ($true) {
    if ([DateTime]::Now -ge $nextPing) { Send $sp 'PING'; $nextPing = [DateTime]::Now.AddSeconds(1) }
    $line = Read-LineOrNull $sp
    if (-not $line -or $line -eq 'PONG') { continue }
    $ts = Get-Date -Format 'HH:mm:ss.fff'
    if ($line -match '^D (\d+)$') {
      $downAt[$Matches[1]] = [DateTime]::Now
      Write-Host "$ts  Taste $($Matches[1].PadLeft(2)) gedrueckt" -ForegroundColor Green
    } elseif ($line -match '^U (\d+)$') {
      $key = $Matches[1]
      $dur = ''
      if ($downAt.ContainsKey($key)) {
        $dur = ' ({0:N0} ms gehalten)' -f ([DateTime]::Now - $downAt[$key]).TotalMilliseconds
      }
      Write-Host "$ts  Taste $($key.PadLeft(2)) losgelassen$dur" -ForegroundColor DarkGreen
    } else {
      Write-Host "$ts  $line" -ForegroundColor DarkGray
    }
  }
}

# ------------------------------------------------------------------ Ablauf
$sp = $null
try {
  if ($Port) {
    $sp = Open-Port $Port
    Start-Sleep -Milliseconds 100
    $sp.DiscardInBuffer()
    Write-Host "Verwende $Port"
  } else {
    $sp = Find-Device
  }

  Test-Protocol $sp
  Test-Diagnostics $sp
  if ($Timeout) { Test-ConnectionTimeout $sp }
  if ($KeyTest) { Run-KeyTest $sp }

  Write-Host ''
  $color = 'Green'
  if ($script:failed -gt 0) { $color = 'Red' }
  Write-Host ("Ergebnis: {0} bestanden, {1} fehlgeschlagen" -f $script:passed, $script:failed) -ForegroundColor $color

  if ($Monitor) { Run-Monitor $sp }
} finally {
  if ($sp -and $sp.IsOpen) { $sp.Close() }
}

if ($script:failed -gt 0) { exit 1 } else { exit 0 }

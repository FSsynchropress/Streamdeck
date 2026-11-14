@echo off
rem Baut und startet die Firmware-Unit-Tests auf dem PC (g++ oder Visual Studio / MSVC).
setlocal
set "HERE=%~dp0"
set "SRC=%HERE%..\StreamDeck"
set "OUT=%HERE%build"
if not exist "%OUT%" mkdir "%OUT%"

rem Bevorzugt g++ (z. B. w64devkit/MinGW im PATH), sonst MSVC
where g++ >nul 2>&1
if not errorlevel 1 (
  g++ -std=c++17 -Wall -Wextra -Wpedantic -I"%SRC%" -o "%OUT%\firmware_tests.exe" ^
      "%HERE%test_main.cpp" "%SRC%\Debouncer.cpp" "%SRC%\Protocol.cpp"
  if errorlevel 1 exit /b 1
  "%OUT%\firmware_tests.exe"
  exit /b %errorlevel%
)

if not defined VCINSTALLDIR (
  for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath`) do set "VSDIR=%%i"
)
if not defined VCINSTALLDIR (
  if not defined VSDIR (
    echo Weder g++ noch Visual Studio gefunden.
    exit /b 2
  )
  call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul
)

cl /nologo /std:c++17 /EHsc /W4 /utf-8 /I"%SRC%" /Fo"%OUT%\\" /Fe"%OUT%\firmware_tests.exe" ^
   "%HERE%test_main.cpp" "%SRC%\Debouncer.cpp" "%SRC%\Protocol.cpp"
if errorlevel 1 exit /b 1

"%OUT%\firmware_tests.exe"
exit /b %errorlevel%

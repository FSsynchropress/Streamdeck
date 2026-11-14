# Baut die App und packt sie mit Velopack zu Setup + Update-Paketen (Ausgabe: app\Releases).
# Wird vom GitHub-Workflow benutzt, geht aber auch lokal:
#   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Version 1.0.1
# Voraussetzung: .NET 8 SDK und  dotnet tool install -g vpk --version 1.2.161
param(
    [Parameter(Mandatory)] [string] $Version
)
$ErrorActionPreference = 'Stop'
$app = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\app'))
$publish = Join-Path $app 'publish'
$releases = Join-Path $app 'Releases'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

# Eigenständig (self-contained): auf dem Ziel-PC muss kein .NET installiert sein.
dotnet publish "$app\StreamDeck.App\StreamDeck.App.vbproj" -c Release -r win-x64 --self-contained `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }

vpk pack --packId DIY-StreamDeck --packVersion $Version --packDir $publish `
    --mainExe StreamDeck.exe --packTitle "DIY Stream Deck" --packAuthors "DIY Stream Deck" `
    --icon "$app\StreamDeck.App\StreamDeck.ico" --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack fehlgeschlagen" }

Write-Host "Fertig: $releases"

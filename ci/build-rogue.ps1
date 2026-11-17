#Requires -Version 5.1
# Rogue Capri 1.0 release build (Windows x64, self-contained).
# Produces:
#   E:\Rogue Capri\Release\Rogue-Capri-Setup.exe   (installer, embeds the app)
#   E:\Rogue Capri\Release\RogueCapri.exe          (portable copy)
#   E:\Rogue Capri\Release\RogueCapri-1.0-win-x64.zip
#   E:\Rogue Capri\Release\ReleaseInfo.txt         (versions + sha256)
param([string]$InstallRoot = "E:\Rogue Capri")

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $Root "cross/Capri.Cross.csproj"))) { throw "Run this from the capri-desktop-main checkout." }

$Release = Join-Path $InstallRoot "Release"
$Staging = Join-Path $Release "app"
$Zip = Join-Path (Join-Path $Root "cross/installer") "app.zip"

function Step($msg) { Write-Host ""; Write-Host "== $msg ==" -ForegroundColor Cyan }

Step "0. Check E:"
$drive = Split-Path -Qualifier $InstallRoot
if (-not (Test-Path "$drive\")) { throw "Drive $drive is not available. Rogue Capri installs on E: by default." }

Step "1. Publish RogueCapri (win-x64, self-contained, Release)"
if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
dotnet publish (Join-Path $Root "cross/Capri.Cross.csproj") -c Release -r win-x64 --self-contained true -o $Staging
if ($LASTEXITCODE -ne 0) { throw "App publish failed." }
if (-not (Test-Path (Join-Path $Staging "RogueCapri.exe"))) { throw "RogueCapri.exe missing in publish output." }

Step "2. Zip the app into the installer project"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path (Join-Path $Staging "*") -DestinationPath $Zip -Force

Step "3. Publish Rogue-Capri-Setup.exe"
$SetupOut = Join-Path $Release "setup-out"
if (Test-Path $SetupOut) { Remove-Item $SetupOut -Recurse -Force }
dotnet publish (Join-Path $Root "cross/installer/RogueCapriSetup.csproj") -c Release -r win-x64 --self-contained true -o $SetupOut
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed." }
$SetupExe = Join-Path $Release "Rogue-Capri-Setup.exe"
Copy-Item (Join-Path $SetupOut "Rogue-Capri-Setup.exe") $SetupExe -Force
Remove-Item $SetupOut -Recurse -Force

Step "4. Release bundle"
Copy-Item (Join-Path $Staging "RogueCapri.exe") (Join-Path $Release "RogueCapri.exe") -Force
$ZipOut = Join-Path $Release "RogueCapri-1.0-win-x64.zip"
if (Test-Path $ZipOut) { Remove-Item $ZipOut -Force }
Compress-Archive -Path (Join-Path $Staging "*") -DestinationPath $ZipOut -Force

$sha = (Get-FileHash $SetupExe -Algorithm SHA256).Hash
$info = @(
  "Rogue Capri 1.0 (standalone build of Capri Desktop 0.9.0 + Capri Companion)",
  "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
  "dotnet SDK: $((dotnet --version))",
  "Rogue-Capri-Setup.exe SHA256: $sha",
  "Install: run Rogue-Capri-Setup.exe (default E:\Rogue Capri, silent: Rogue-Capri-Setup.exe /S [/D=path])"
)
$info | Out-File (Join-Path $Release "ReleaseInfo.txt") -Encoding UTF8

Step "Done"
Get-ChildItem $Release | Select-Object Name, @{n="MB";e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize

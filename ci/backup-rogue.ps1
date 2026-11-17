#Requires -Version 5.1
# Rogue Capri 1.0 backup: app config, Companion memory, Rogue Mode config,
# and important user files -> E:\Rogue Capri\Backup\rogue-backup-<timestamp>.zip
# Never overwrites an existing backup. Excludes caches and temp files.
param(
  [string]$InstallRoot = "E:\Rogue Capri",
  [string]$ExtraPath = ""
)

$ErrorActionPreference = "Stop"

function Fail($msg) { Write-Host "BACKUP FAILED: $msg" -ForegroundColor Red; exit 1 }

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = Join-Path $InstallRoot "Backup"
$backupFile = Join-Path $backupDir ("rogue-backup-" + $stamp + ".zip")

Write-Host "== Rogue Capri backup =="
Write-Host "Install root: $InstallRoot"

# 1. Locate the data root the app actually uses.
#    RogueCapriHome env wins, else the registry's InstallLocation, else
#    <install>\Data, else %AppData%\Capri (legacy).
$dataRoot = $null
$installLoc = $null
try {
  $installLoc = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RogueCapri" -ErrorAction SilentlyContinue).InstallLocation
} catch { }
if ($env:RogueCapriHome -and (Test-Path $env:RogueCapriHome)) { $dataRoot = $env:RogueCapriHome }
elseif ($installLoc -and (Test-Path (Join-Path $installLoc "Data"))) { $dataRoot = Join-Path $installLoc "Data" }
elseif (Test-Path (Join-Path $InstallRoot "Data")) { $dataRoot = Join-Path $InstallRoot "Data" }
elseif (Test-Path "$env:APPDATA\Capri") { $dataRoot = "$env:APPDATA\Capri" }
else { Fail "no Rogue Capri data folder found (looked in $env:RogueCapriHome, $installLoc\Data, $InstallRoot\Data, %AppData%\Capri)" }
Write-Host "Data root: $dataRoot"

# 2. Collect sources.
$sources = @()
if (Test-Path (Join-Path $dataRoot "cross.json"))  { $sources += (Join-Path $dataRoot "cross.json") }
if (Test-Path (Join-Path $dataRoot "companion-memory.json")) { $sources += (Join-Path $dataRoot "companion-memory.json") }
if (Test-Path (Join-Path $dataRoot "plugins"))     { $sources += (Join-Path $dataRoot "plugins") }
$rogueConfig = Join-Path $InstallRoot "Config"
if (Test-Path $rogueConfig) { $sources += $rogueConfig }
if ($ExtraPath -and (Test-Path $ExtraPath)) { $sources += $ExtraPath }

if ($sources.Count -eq 0) { Fail "nothing to back up" }

# 3. Stage without temp files / caches.
$stage = Join-Path $backupDir ("stage-" + $stamp)
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$exclude = @("*.tmp", "*.temp", "*.log", "cache", "Cache", "temp", "Temp")
try {
  foreach ($src in $sources) {
    $name = Split-Path $src -Leaf
    if ((Get-Item $src) -is [System.IO.DirectoryInfo]) {
      Copy-Item $src (Join-Path $stage $name) -Recurse -Force -ErrorAction SilentlyContinue
      Get-ChildItem (Join-Path $stage $name) -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object { $exclude -contains $_.Extension -or $exclude -contains $_.Name } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    } else {
      Copy-Item $src (Join-Path $stage $name) -Force
    }
  }

  # 4. Never overwrite: the timestamped name is unique; bail if it exists anyway.
  if (Test-Path $backupFile) { Fail "backup file already exists: $backupFile" }
  New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
  $inner = Join-Path $stage "rogue-capri"
  New-Item -ItemType Directory -Force -Path $inner | Out-Null
  foreach ($item in Get-ChildItem $stage -Force) { if ($item.Name -ne "rogue-capri") { Move-Item $item.FullName (Join-Path $inner $item.Name) -Force } }
  Compress-Archive -Path (Join-Path $inner "*") -DestinationPath $backupFile -Force

  # 5. Verify the archive is readable and non-empty.
  if (-not (Test-Path $backupFile)) { Fail "archive was not created" }
  $zip = [System.IO.Compression.ZipFile]::OpenRead($backupFile)
  try {
    $count = $zip.Entries.Count
    Write-Host "Archive entries: $count"
    $zip.Entries | ForEach-Object { Write-Host "  $($_.FullName)" }
    if ($count -eq 0) { Fail "archive is empty" }
  } finally { $zip.Dispose() }

  $size = (Get-Item $backupFile).Length
  if ($size -eq 0) { Fail "archive has zero size" }
  Write-Host ""
  Write-Host "BACKUP OK: $backupFile ($([math]::Round($size/1MB, 2)) MB)" -ForegroundColor Green
  exit 0
}
finally {
  Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

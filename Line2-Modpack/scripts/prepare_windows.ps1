#Requires -Version 5.1
<#
.SYNOPSIS
  Windows preflight for Unturned 2 Line 2. No game files are modified.
.EXAMPLE
  .\prepare_windows.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Unturned"
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$GameDir,
  [string]$WorkshopDir = "",
  [string]$ManagerData = "$env:USERPROFILE\Unturned2-ModpackManager",
  [int]$GunID = 32610,
  [int]$MagazineID = 32611,
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$manager = Join-Path $root "ModpackManager"
$logDir = Join-Path $ManagerData "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ("preflight-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")

function Write-Result([string]$message) {
  $message | Tee-Object -FilePath $log -Append
}
function Invoke-Python([string[]]$arguments) {
  # stdout and stderr are captured by Tee-Object for diagnostics.
  & python @arguments 2>&1 | Tee-Object -FilePath $log -Append
  if ($LASTEXITCODE -ne 0) {
    throw "Python returned $LASTEXITCODE. Review $log"
  }
}

Write-Result "Line 2 Windows preflight started: $(Get-Date -Format o)"
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
  throw "Python 3.10+ not on PATH. Install supported Python for Windows."
}
Invoke-Python @("--version")
if (-not (Test-Path (Join-Path $GameDir "Unturned.exe") -PathType Leaf)) {
  throw "Invalid Unturned installation: Unturned.exe not found."
}
if (-not (Test-Path (Join-Path $GameDir "Bundles\Items") -PathType Container)) {
  throw "Invalid Unturned installation: Bundles\Items not found."
}
if ([string]::IsNullOrWhiteSpace($WorkshopDir)) {
  $steamapps = Split-Path (Split-Path $GameDir -Parent) -Parent
  $candidate = Join-Path $steamapps "workshop\content\304930"
  if (Test-Path $candidate -PathType Container) { $WorkshopDir = $candidate }
}
Write-Result "Game: $GameDir"
Write-Result "Workshop: $WorkshopDir"
Write-Result "Python test suites: AIService, ModpackManager"

Invoke-Python @((Join-Path $root "run_tests.py"), "AIService")
Invoke-Python @((Join-Path $root "run_tests.py"), "ModpackManager")

if (-not $SkipBuild) {
  $target = Join-Path $ManagerData "staging\Auto32"
  if (Test-Path $target) {
    Write-Result "Auto32 staging already exists: $target (not overwritten)"
  } else {
    $arguments = @((Join-Path $manager "build_auto32.py"),
                   "--game", $GameDir, "--output", $target,
                   "--gun-id", "$GunID", "--magazine-id", "$MagazineID")
    if (-not [string]::IsNullOrWhiteSpace($WorkshopDir)) {
      $arguments += @("--scan-root", $WorkshopDir)
    }
    Invoke-Python $arguments
    Write-Result "Auto32 generated to staging: $target"
  }
}
Write-Result "SUCCESS: offline preflight finished. This does NOT install a game mod."
Write-Result "Next: inspect Auto32\BUILD_NOTES.txt, review IDs, then deploy using the manager."
Write-Result "Log: $log"

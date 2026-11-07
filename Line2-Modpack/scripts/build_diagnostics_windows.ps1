#Requires -Version 5.1
<#
Build/install Line 2 diagnostic module to the ORIGINAL Steam Unturned.
No changes to base game DLLs, NPCs, saves, zombies or workshop items.
Run only after closing Unturned.
#>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$GameDir,
 [switch]$BuildOnly
)
$ErrorActionPreference="Stop"
$GameDir=(Resolve-Path $GameDir).Path
if (-not (Test-Path (Join-Path $GameDir "Unturned.exe") -PathType Leaf)) {
 throw "Unturned.exe not found in selected directory."
}
$project=Join-Path $PSScriptRoot "..\ClientModule\Unturned2.Line2.Probe.csproj"
$manifest=Join-Path $PSScriptRoot "..\ClientModule\Unturned2.Line2.module"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
 throw "dotnet SDK not found on PATH; install a .NET SDK and .NET Framework 4.7.2 targeting pack."
}
Write-Host "Building diagnostic-only module (F8-F12) against Steam Unturned..."
& dotnet build $project --configuration Release "-p:UnturnedDir=$GameDir"
if ($LASTEXITCODE -ne 0) {throw "Build failed. Do not install DLL. Copy the full build error output."}
$assembly=Join-Path $PSScriptRoot "..\ClientModule\bin\Release\net472\Unturned2.Line2.Probe.dll"
if (-not (Test-Path $assembly -PathType Leaf)) {throw "Compiled DLL missing at $assembly"}
if ($BuildOnly) {
 Write-Host "Build succeeded: $assembly"
 exit 0
}
$module=Join-Path $GameDir "Modules\Unturned2.Line2"
if (Test-Path $module) {
 $backup=Join-Path $env:USERPROFILE ("Unturned2-Line2-module-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
 Copy-Item $module $backup -Recurse -Force
 Write-Host "Existing diagnostic module backed up to $backup"
} else {
 New-Item -ItemType Directory -Path $module -Force | Out-Null
}
Copy-Item $assembly (Join-Path $module "Unturned2.Line2.Probe.dll") -Force
Copy-Item $manifest (Join-Path $module "Unturned2.Line2.module") -Force
Write-Host "Installed to $module. Start Steam Unturned without BattlEye."
Write-Host "F8 geometry; F9 AI loopback; F10 NPC APIs; F11 zombies; F12 NPC snapshots."
Write-Host "Execution of the module in this game version has NOT yet been verified."

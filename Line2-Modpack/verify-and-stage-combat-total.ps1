# Windows PowerShell 5.1+ — compile the existing Line 2 Steam module,
# then stage a SELF-CONTAINED module folder. Does not modify the game's
# official DLLs and does not install anything without explicit -Install.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $UnturnedDir,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot 'ClientModule\Unturned2.Line2.Probe.csproj'
$manifest = Join-Path $projectRoot 'ClientModule\Unturned2.Line2.module'
$managed = Join-Path $UnturnedDir 'Unturned_Data\Managed'
$stage = Join-Path $projectRoot 'build\CombatTotal\Unturned2.Line2'
$target = Join-Path $UnturnedDir 'Modules\Unturned2.Line2'
$required = @(
    'Assembly-CSharp.dll', 'com.rlabrecque.steamworks.net.dll',
    'UnityEngine.CoreModule.dll', 'UnityEngine.PhysicsModule.dll',
    'UnityEngine.JSONSerializeModule.dll',
    'UnityEngine.AnimationModule.dll', 'UnityEngine.AudioModule.dll',
    'UnityEngine.IMGUIModule.dll', 'UnityEngine.InputLegacyModule.dll'
)

if ($env:OS -ne 'Windows_NT') {
    throw 'The Unturned Steam module must be compiled on Windows against the local game managed assemblies.'
}
if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf) -or
    -not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw 'Module source or manifest not found. Run the script from the Line2-Modpack repository folder.'
}
if (-not (Test-Path -LiteralPath $managed -PathType Container)) {
    throw "Unturned managed DLL folder not found: $managed"
}
$missing = @($required | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $managed $_) -PathType Leaf)
})
if ($missing.Count -gt 0) {
    throw ("Steam managed DLLs missing: " + ($missing -join ', ') +
        ". Check the installed Unturned version/path before building.")
}
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw 'dotnet CLI not found. Install the .NET SDK and .NET Framework 4.7.2 targeting pack.'
}

# Brain policy tests run independently of Steam to catch code regressions.
$tests = Join-Path $projectRoot 'BrainTests\BrainTests.csproj'
if (Test-Path -LiteralPath $tests) {
    & dotnet run --project $tests --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Headless NPC combat policy tests failed (exit code $LASTEXITCODE)."
    }
}

& dotnet build $projectFile --configuration Release --no-incremental "-p:UnturnedDir=$UnturnedDir"
if ($LASTEXITCODE -ne 0) {
    throw "Steam DLL compilation failed (exit code $LASTEXITCODE). No deployment performed."
}

$built = Join-Path $projectRoot 'ClientModule\bin\Release\net472\Unturned2.Line2.Probe.dll'
if (-not (Test-Path -LiteralPath $built -PathType Leaf)) {
    throw "Build exited successfully but its expected output is missing: $built"
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath $built -Destination (Join-Path $stage 'Unturned2.Line2.Probe.dll') -Force
Copy-Item -LiteralPath $manifest -Destination (Join-Path $stage 'Unturned2.Line2.module') -Force
$checksum = (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash
Write-Host "COMBAT_TOTAL_STAGED: $stage"
Write-Host "DLL_SHA256: $checksum"
Write-Host 'This is a compiled candidate, not a gameplay-validated release.'

if ($Install) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $existing = Join-Path $target 'Unturned2.Line2.Probe.dll'
    if (Test-Path -LiteralPath $existing -PathType Leaf) {
        $backup = "$existing.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $existing -Destination $backup -Force
        Write-Host "PREVIOUS_DLL_BACKUP: $backup"
    }
    Copy-Item -LiteralPath (Join-Path $stage 'Unturned2.Line2.Probe.dll') -Destination $existing -Force
    Copy-Item -LiteralPath (Join-Path $stage 'Unturned2.Line2.module') -Destination (Join-Path $target 'Unturned2.Line2.module') -Force
    Write-Host "COMBAT_TOTAL_INSTALLED: $target"
    Write-Host 'Launch singleplayer without BattlEye, then verify the Combat Total behavior and logs.'
}

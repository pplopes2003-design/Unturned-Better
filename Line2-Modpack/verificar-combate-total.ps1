# Read-only diagnosis of the LAST Steam Line 2 playtest.
# No file is modified and absent entries are "NOT OBSERVED", not proof of a bug.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$UnturnedDir)

$ErrorActionPreference = 'Stop'
$log = Join-Path $UnturnedDir 'Unturned2-Line2\probe.log'
if (-not (Test-Path -LiteralPath $log -PathType Leaf)) {
    throw "Line 2 probe.log not found at: $log"
}
$entries = Get-Content -LiteralPath $log -Tail 8000
$checks = [ordered]@{
    'Module initialized' = 'Module initialized'
    'NPC spawned' = 'NPC_PROTOTYPE_SPAWN_OK'
    'Six native anatomical hitboxes' = 'NPC_NATIVE_ANATOMY_OK'
    'Native firearm callback delivered' = 'NPC_GUN_EVENT_RECEIVED'
    'Gun ray rejected (diagnostic)' = 'NPC_GUN_RAY_UNRESOLVED'
    'NPC bullet damage committed' = 'NPC_NATIVE_GUN_DAMAGE'
    'NPC HP damaged' = 'NPC_DAMAGE'
    'NPC death' = 'NPC_PROTOTYPE_DIED'
    'Tactical movement observed' = 'NPC_MOVEMENT_AUDIT'
    'NPC ranged shot committed' = 'NPC_RANGED_SHOT:'
    'Sniper shot with native ammo' = 'sniper=True'
    'Sniper real impact feedback' = 'NPC_SNIPER_FEEDBACK:'
    'NPC reserved parked vehicle' = 'NPC_VEHICLE_APPROACH_RESERVED:'
    'NPC reached vehicle door' = 'NPC_VEHICLE_AT_DOOR:'
    'NPC vehicle claim released' = 'NPC_VEHICLE_RELEASE:'
    'Distant Combat Total NPC spawned' = 'NPC_DISTANT_COMBAT_SPAWN:'
    'Distant NPC vehicle mission selected' = 'NPC_VEHICLE_DEPLOYMENT_DEFERRED:'
    'Armored NPC deployment considered' = 'NPC_ARMORED_DEPLOYMENT_DEFERRED:'
    'NPC saw actual player vehicle' = 'NPC_VEHICLE_ESCAPE_OBSERVED:'
    'Visual suspicion observed' = 'NPC_VISUAL_SUSPICION:'
    'Native crouch performed' = 'NPC_STANCE_CHANGED'
    'Verified cover hold' = 'NPC_COVER_HOLD'
    'Medical use began' = 'NPC_MEDICAL_BEGIN'
    'Medical use completed' = 'NPC_MEDICAL_COMPLETED'
    'Medical use interrupted' = 'NPC_MEDICAL_INTERRUPTED'
    'Acid grenade hooked' = 'ACID_GRENADE_ARMED'
    'Acid initial native settings' = 'ACID_GRENADE_NATIVE_CONFIG'
    'Acid pulse applied' = 'ACID_GRENADE_PULSE:'
}
foreach ($name in $checks.Keys) {
    $marker = $checks[$name]
    $found = @($entries -match [regex]::Escape($marker)).Count -gt 0
    $result = if ($found) { 'OBSERVED' } else { 'NOT OBSERVED' }
    Write-Host ('[{0}] {1} ({2})' -f $result, $name, $marker)
}
Write-Host ''
Write-Host 'Important: missing log markers can mean the scenario was not tested.'
Write-Host 'An observed marker is evidence of execution, not a complete gameplay pass.'
Write-Host ("Source: " + $log)

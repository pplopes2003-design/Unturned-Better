# Unturned 2 — Development Build: stabilization gate (24 October 2026)

Branch: `feature/npc-ai-foundation`.

## Intended scope

This is an **integrated behavior validation build**, not a production release. No Unity Editor build, runtime test, deterministic performance benchmark or authoritative multiplayer test was available while making these GitHub changes. Do not mark any behavior "fixed in game" solely based on source edits.

## Verified in source

- **Building apertures**: existing geometric threshold detector checks capsule clearance plus supported floor on both sides; the motor preserves the crossing heading, uses a precise waypoint arrival threshold and records `MobileNPCThresholdTraverse`. It is not a generic door asset detector.
- **Global fallback navigation**: `MobileNPCGlobalRoutePlanner` is an incremental, 21x21/3m-grid, same-surface navigation horizon when native ASPFP is absent. It does not manufacture entrances, stairways or guaranteed whole-map routes. Queue has a shared per-frame budget and a bounded timeout. Long facade failures continue to local/threshold recovery.
- **Terrain recovery**: the motor's emergency recovery uses player-compatible physical collision layers, rejects furniture and vehicles and cannot use Nav-only helper layers as an invented floor.
- **Ranged presentation**: `MobileNPCRangedPresentation` mounts an item on the same right-hand hook convention as `PlayerEquipment`. `MobileNPCVisualAnimation` owns distinct runtime names for each gun's native clips, keeping locomotion's `Idle`/`Equip` states separate. Aim and pose diagnostics are present.
- **Gun firing**: selected gun metadata determines automatic eligibility; each committed shot consumes real ammo and uses aim, friend-lane and ballistic checks. `MobileNPCFireMode` and `MobileNPCBurst` record decisions on a Development Build.
- **Sprinters**: increased health and native seeker speed configured in `Zombie.cs` (subject to difficulty asset overrides). Navigation off-graph speed also adjusted.
- **Hordes**: native respawn reuse with an authored-point cluster preflight; runtime rechecks the regional spawn flag, nonempty point list and valid zombie type index before a revival. Sizes 10–20 / 20–30 are targeted, not guaranteed when the world changes mid-event. Logs `ZombieHordeSpawn`.

## Order of testing (save the log before starting another session)

1. **Unity compilation**: Console must have no red C# compile errors. Preserve the **first** compiler error and full stack if any. If missing scripts or GUID warnings occur, check the `.meta` files.
2. **Smoke test**: spawn one unarmed and one armed NPC; verify visual appearance, labels, ground contact, idle animation, locomotion and death. Monitor first 2 minutes of Player.log.
3. **Building portal**: place an NPC 4–12m from a real doorless entrance and a loot/goal inside. Look for `MobileNPCGeometryThreshold` then `MobileNPCThresholdTraverse`. Film whether it actually crosses. Try an off-axis facade and a blocked/raised-sill window.
4. **Macro nav**: goal beyond a building 20–40m away; record `MobileNPCGlobalRoute` (staged endpoint) and whether NPC continues toward the actual destination, without hitting the same wall or oscillating. Repeat with 10, 20 and 40 NPCs, checking server frame time.
5. **Vertical navigation and falling**: stairs and ladders only via verified connectors; test hills and indoor floors. Any `FALL DIAGNOSTIC`, `Recovered NPC` or movement on Nav-only layer warrants investigation.
6. **Weapon mount and aim**: equip Maplestrike/Zubeknakov and a semi-auto, examine orientation during idle/movement/shooting and log `MobileNPCWeaponMount`, `MobileNPCWeaponPose`, `MobileNPCMuzzleAudit`. Weapon model must point ahead; ballistic origin should still represent the authoritative shot.
7. **Automatic fire**: approach a visible hostile within 35m. Check `MobileNPCFireMode` and `MobileNPCBurst` versus actual observed muzzle effects and sound. Confirm ammo decreases correctly and shots respect native cyclic rate. Check semi-auto does **not** become fully automatic, reloads function, and teammates block unsafe shots.
8. **Zombies**: test new sprinter chase/survivability, sound investigation, player prioritization and pursuit across native zombie regions. Verify zombie dying, NPC damage and return-to-roam.
9. **Hordes**: normal map with enough valid nearby points and dead native zombie slots; separate full-moon test. Check `ZombieHordeSpawn` and sizes. Count live zombies spawned and measure frame time; never assume minimum size without sufficient slots.

## Known limitations — do not present as delivered

- Native ASPFP is not available in the public SDK environment; fallback local/global-horizon routing is approximate and does not guarantee reliable passage through every building.
- Runtime correctness of visual animation, rifle muzzle orientation, actual automatic fire, combat positioning, ragdolls and multi-NPC behavior remains unproven until Unity testing.
- Native zombie group size can be lower than its target due to limited legitimate spawnpoints/dead entity slots or changing world occupancy.
- The **in-menu multiplayer hosting workflow** is not yet functional: only the validated host-configuration model and architecture notes exist. This Development Build should not claim one-click multiplayer server startup.
- No actual Unity compile/test is available here; inspect Console and Player.log before labeling this build stable.

## Handoff log collection

Close the Development Build normally and attach `Player.log` before restarting. On Windows common location:
`%USERPROFILE%\\AppData\\LocalLow\\Smartly Dressed Games\\Unturned\\Player.log`.
If compilation fails, capture Unity Editor Console's first error and Editor.log. Include map, NPC count, equipped weapon ID, gameplay action and approximate location/time for a reproduction.

## Regressions to watch

Do not fix a blocked route by turning off physical collision checks, bypassing native netcode, teleporting the actor, treating a window as a walkable doorway, permitting free bullets, or making all weapons automatic. Leave unrelated gameplay systems unchanged during this build cycle.

# Initial Performance Audit

Date: 2026-10-10

## Scope

Initial source review only. No gameplay code or Unity settings were changed, and the project has not been profiled in a standalone build yet.

## Findings

- Zombie logic is primarily implemented in:
  - `Assets/Runtime/Assembly-CSharp/Unturned/Zombies/Zombie.cs`
  - `Assets/Runtime/Assembly-CSharp/Unturned/Managers/ZombieManager.cs`
  - `Assets/Runtime/Assembly-CSharp/Unturned/Managers/ZombieRegion.cs`
  - `Assets/Runtime/Assembly-CSharp/Unturned/Level/LevelZombies.cs`
- `ZombieManager` already contains a basic population guard: `canSpareWanderer` returns true only while `wanderingCount < 8` and `tickingZombies.Count < 50`. These are existing gameplay limits, not proposed changes.
- `ZombieRegion` checks active players when updating quest-boss behavior. This is another reason to avoid broad changes to zombie update cadence before profiling and understanding multiplayer authority.
- The startup scene successfully began loading asset bundles from the local Steam installation in the user's Unity Editor session.
- The Unity Console screenshot showed informational messages and a warning about a missing Brazilian Portuguese economy translation; no red errors were visible in that screenshot.

## Recommended next steps

1. Establish a reproducible baseline in a standalone development build, not only in the Unity Editor.
2. Record FPS/frame time, memory usage, and behavior with a normal population of zombies; repeat in a busier area if practical.
3. Use Unity Profiler to identify the dominant cost (CPU scripts, rendering, garbage collection, physics, or asset loading).
4. Make one small, isolated optimization on a feature branch, then compare the same scenario before and after.
5. Preserve server authority and network replication for future multiplayer compatibility.

## Guardrails

- Do not change existing zombie counts, AI tick frequency, or network behavior without evidence from profiling.
- Do not modify generated files under `Assets/Runtime/Assembly-CSharp/NetGen` as a first optimization step.
- Keep each change reversible and document how it was tested.
- Third-party assets must be reviewed for redistribution permissions before sharing a build with friends.

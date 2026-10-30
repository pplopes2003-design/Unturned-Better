# Unified development branch — 2026-10-30

**Build/test checkout:** `feature/npc-hybrid-integration`  
**Protected game baseline:** `main` (not modified by this consolidation).

## Reconciliation of all remote branches
- `feature/npc-ai-foundation`: direct ancestor of the unified branch; retains the full MobileNPC runtime and the latest portal/recovery changes from Oct 30.
- `backup/alife-survival-2026-10-16`: direct ancestor of the unified branch; all tracked changes already included.
- `perfomance-baseline`: direct ancestor of the unified branch; all tracked changes already included.
- `feature/hybrid-npc-navigation`: its six new C# modules, report analyzer, preflight and documentation were imported into the unified branch without overwriting existing NPC gameplay implementations. Branch commit histories differ, but its unique files are included.
- `chore/project-baseline`: the one exclusive file `PROJECT_NOTES.md` was copied. Other older implementations were not overlaid on newer versions.
- `main`: intentionally excluded. Its isolated commit on `MobileNPCHumanoidVitals.cs` is not imported, as the unified branch has a much newer, larger implementation of death/ragdoll.
- The original development and backup branches remain available; nothing was deleted.

## Build status
The unified branch is a **source consolidation**. The HybridNPC pathfinding prototypes currently coexist with the original MobileNPC navigation rather than replacing the live motor. Telemetry is not yet connected to the in-game menu or NPC event producers. Unity compilation, game runtime and performance have not been verified.

## Local work caveat
Uncommitted local modifications and commits not pushed to the remote cannot be observed here. In GitHub Desktop, first commit/push the local changes on their existing branch, then merge that branch into `feature/npc-hybrid-integration`. Resolve conflicts while preserving the newer implementation, rather than simply choosing 'ours' or 'theirs' for every file. Keep `main` unchanged.

## Subsequent work
Commit future NPC, zombie, performance and gameplay changes to `feature/npc-hybrid-integration`. Before validation, enable telemetry through live integration, connect the hybrid planner selectively to the existing motor, and compile/test in Unity. Do not claim the hybrid system has corrected navigation until run-time evidence demonstrates that.

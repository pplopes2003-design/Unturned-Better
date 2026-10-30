# Integrated development build — branch policy

## Single branch for testing
Use `feature/npc-hybrid-integration` as the staging branch for the next combined Unity Development Build. It was created from `feature/npc-ai-foundation`, preserving the full MobileNPC runtime, prefabs, zombie changes, firearm changes, etc., and receives the HybridNPC modules. The experimental `feature/hybrid-npc-navigation` branch is not a standalone playable build.

## Important repository comparison (2026-10-30)
- Compared `feature/npc-ai-foundation...feature/npc-hybrid-integration`: integration is 2 commits ahead and 0 behind.
- Compared `feature/npc-ai-foundation...main`: branches diverged. `main` has 1 unique commit changing `MobileNPCHumanoidVitals.cs`.
- The `main` variant of that file is approximately 8 KB while the foundation/integration variant is approximately 46 KB, including advanced death and ragdoll behavior. Do **not** blindly overwrite the full NPC implementation with `main`; manually evaluate individual intended changes.
- No connector can see uncommitted edits existing solely on a user's computer. Capture those by committing to a branch before merging, without discarding files or local changes.

## GitHub Desktop safe workflow
1. In your local copy of the Unity project, use GitHub Desktop to check **Current Branch** and the **Changes** tab. If there are pending modifications, preserve them by making a dedicated commit (e.g. `WIP: local game improvements`) on the existing branch, then Push origin. Never use Discard Changes.
2. Fetch origin and select `feature/npc-hybrid-integration`; Pull origin.
3. If you previously committed changes to another branch, choose Branch > Merge into Current Branch, and select the branch containing those changes; inspect each conflict (particularly `MobileNPCHumanoidVitals.cs`). If conflicts are unclear, stop rather than overwriting.
4. Open the same Unity project folder and build from the unified checkout only. Confirm branch and latest commit in GitHub Desktop prior to launch.
5. Do not merge this branch into `main` until Unity compilation and in-game tests have passed.

## Current technical status
This branch contains the complete old NPC system and the new HybridNPC prototypes, but the new path planner is **not yet connected** to the original motor, and navigation telemetry needs actual event hooks. Merely co-locating code does not make the hybrid algorithm active. This guide is for source consolidation, not proof of runtime functionality.

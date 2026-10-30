# Offline Zombie Survival - first test candidate

This branch starts at `perfomance-baseline`, NOT the NPC hybrid integration branch.
No NPC AI additions were carried over. `main` and `feature/npc-hybrid-integration` are unchanged.

## Features
- Already acquired living-player pursuit can continue up to 65m off POI navigation bounds when the NPC is grounded, slope <45 degrees, height change <=0.55m, and next short capsule sweep is clear. Native seeker resumes within known navigation geometry. Does not promise pathfinding around walls or crossing deep ravines; grounded off-nav following needs in-game testing.
- Sprinters: ~8m/s with Slow Movement enabled, ~9m/s otherwise; health 1.65x the base table (rather than 0.5x), melee damage 1.35x instead of 0.75x.
- Natural survival packs: initial region population generation groups authored safe zombie spawnpoints into attempted 8-18 member packs within a 25m radius. This respects region maxZombies and map-defined points; packs may be smaller where spawnpoints or capacity are insufficient. Existing ambient horde migration now seeks 8-18 idle existing zombies. No extra zombi entities are spawned in defiance of region caps.
- Dedicated server codepath intentionally excluded from new off-nav steering and clustering to focus on offline/local hosting.

## Unity 2022.3.62f3 test checklist
1. Switch/checkout branch `feature/offline-zombie-survival` and let Unity reimport changed C#.
2. Open Console. Stop on first compile error and share its full text (with file and line).
3. Enable Development Build; backup save. Start a Survival map with existing zombie spawnpoints, e.g. Washington.
4. Aggro one normal zombie near a POI edge; run 10m, then 30m, then 50m outside the nav region. Observe whether it leaves the POI and whether walls, slopes and gaps stop it properly.
5. Aggro a sprinter; compare speed, health and hit damage. Note that survivor armor may modify actual health damage.
6. On fresh region load, count local grouped zombies. Test large enough POIs and small POIs. Caps may result in packs below eight.
7. Observe ambient migrating groups during a session and monitor FPS with multiple zombies; no teleports should occur.
8. Save the `Player.log` and optional video of the POI crossing if following fails.

## Known limitations
This code has not been compiled in the local Unity Editor by the assistant. Off-nav steering is intentionally conservative (straight short terrain samples) and does not implement whole-map A*. Already hunting zombies outside regions with players now receive a bounded additional per-frame update (maximum 48 in the current code). Long-lived pursuit behavior and any despawn restrictions still require in-game verification. Workshop interoperability is a later milestone: preserving Unturned's asset bundle/UGC loading requires verification of its actual runtime compatibility and licensing before promising support. The original game Workshop content cannot be assumed to work in a standalone fork.

## Offline population balance
The initial survival zombie population target and the per-region map cap are increased by 50% in offline mode, with a hard 80-zombie cap per region and no more zombies than existing eligible map spawnpoints. Small POIs can still contain fewer. Sprinter speciality selection weight is multiplied by 0.45 (55% lower than the original configured weight), including maps that use a ZombieDifficultyAsset override. Other specialities retain their configured weights; the unused share favors normal zombies. This modifies offline survival initial generation and selection for respawns, but not the number of respawned entities per tick. No Unity compile/play test has been performed.

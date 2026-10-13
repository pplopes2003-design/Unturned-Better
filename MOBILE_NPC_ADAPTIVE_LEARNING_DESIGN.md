# Individual adaptive memory and experiential learning — 2026-10-13

**Canonical backlog:** `MOBILE_NPC_PENDING_TASKS.md` (LEARN-01 to LEARN-05). Historical handoff: `MOBILE_NPC_PROJECT_HANDOFF.md`.

## Why this is not a global NPC intelligence

A mobile NPC has its own experience component and learns only after server-observed outcomes. Sharing requires a local conversation between two living members of the same group at audible distance. If no listener heard a lesson, no other person obtains it; the corpse's experience is erased on death. This **does not** make the NPC brain a neural network, Q-learning across every action, nor make a single knowledge fact globally known. Online Bayesian-smoothed reward estimates constitute a lightweight contextual feedback policy, not trained deep learning.

## Current code integration (SOURCE ONLY; not compiled or played)

| Unit | What exists | What is still missing |
| --- | --- | --- |
| `MobileNPCAdaptiveMemory.cs` | Up to 48 local experience records, with sector-sized spatial contexts (20m), positive/negative evidence, bounded update and a smoothing prior | Full strategy categories, high-level planning reward, forgetting over real elapsed time, individual routes |
| `MobileNPCPopulationBootstrap.cs` | Attaches separate memory instance to each mobile NPC | Dedicated prefab/unit testing |
| `MobileNPCLooting.cs` | Records resolved pickup success/failure and a timed-out loot goal; region knowledge modestly influences next loot scoring | Distinguish item destroyed from failed route in all edge cases; integrated path failure cause |
| `MobileNPCHumanoidRangedCombat.cs` | Records fired-shot miss vs physical hit on validated player, zombie and hostile NPC; bounded contextual evidence scales shot delay modestly (0.85–1.3) | Confirm native actual damage/armor acceptance; learning weapon class, burst length, shot distance, zombie subtype |
| `MobileNPCGroupMember.cs` | Periodic single lesson, relaxed group-only, ~7m (4m crouched), reduced hearing behind geometry, no relayed rumors; receiver stores weaker evidence (0.35x), sender clears pending only after at least one recipient | Visible/audible conversation delivery, interruptions, more than latest unsent lesson, player overhearing, spoken trust and reputation |
| `MobileNPCPersistence.cs` | Version 3 snapshot stores/restores each living individual's bounded experiences. Old v1/v2 records have no experience and restore with empty model | Persist unsent conversation queue and permanent unique NPC identity across world saves |
| `MobileNPCHumanoidVitals.cs` | On death discards private experience before deactivating/retiring the actor; no inheritance by population replacement | Test native death/corpse/despawn and that no save races occur |

**Learning loop in source:** shot or loot attempt with resolved feedback -> per-NPC `RecordOwnOutcomeServer` -> bounded sector/context evidence -> `GetLootScoreAdjustment` or `GetShotCadenceMultiplier` affects a subsequent decision -> optional `TryGetUnspokenLessonServer` -> validated local spoken exchange -> `ReceiveSpokenLessonServer` updates listener with low weight -> save survivor's individual record. The reporter does not cause other agents in the same faction to learn automatically.

**No frequency-checked radio sharing:** `MobileNPCRadioOutbox` is still data-only; never transmit learned experience via it until a real, fully validated native radio channel and sender/receiver exists. Existing player-contact reports are separate from the learned policy. No global state is added here.

## Limits and feedback definition

- Fire feedback records **physical hit on the selected visible target**, not confirmed kill or effective damage through armor. A miss or hitting cover is a failure observation; firing consumes native finite ammo first.
- Loot feedback is a completed inventory pickup or selected loot goal abandoned after expiry. An item that simply disappears is not evidence of an impossible route.
- At least ~4 effective observations are required before gun pacing changes; a probabilistic smoothing prior avoids extreme policies. The NPC never gains perfect accuracy and still requires target visibility, faction hostility, hit raycast, ammo and server authority.
- Region preferences do not veto loot targets; they only modify their relative scores within an upper/lower bound.
- A single nearby listener gains one weak observation, not the speaker's entire learning history. `ReceiveSpokenLessonServer` does not create a new pending outbound lesson. Remote/absent/dead group members learn nothing.
- Killing the NPC clears `MobileNPCAdaptiveMemory` and dead NPCs are omitted from saves. Previously received information belongs to living listeners, not the deceased source.
- Persistence uses JSON `experience` inside individual `NPCRecord`, version 3. Prior v1/v2 remains readable by version gate. Validate a copy of saves before game use.

## Next development milestones

1. Add actual **outcome-aware tactics** for seek cover, retreat, flank, stalking/ambush vs players/zombies with measured gains/losses (not a reward at command start) and context-specific risks. Never bypass verified LOS, path, ammunition or friendly-fire checks.
2. Add map-scoped navigation memory with corridor/failure cause and invalidation on world/building changes; ensure no unverified teleport or precomputed route.
3. Add per-individual identity in persisted snapshots and bounded pending lessons with no duplicate transfers; maintain old-save migration and avoid resurrecting dead identities.
4. Add realistic spoken conversation presentation (verified actual clips/lines), idle/interrupt rules, distinct listener trust, occasional erroneous reports; integrate radio lessons only after native device/channel checks.
5. Profiling and tests with at least 20 NPCs, multiple factions, actual zombies and maps; no frequent scene-wide search or unbounded memory.

## Required future Dev Build tests (not yet executed)

- An NPC shoots several misses at a distant zombie in the **same sector** and pauses longer, conserving ammo; repeated hits adjust it back toward baseline. Confirm no changes when not yet observed or if shot is rejected before ammo debit.
- NPC reaches/collects loot successfully; is later more willing to select local valid loot. A repeatedly blocked table approach gets a bounded penalty, never a permanent ban or immediate evidence of blocked path.
- A and B in the **same group** separated by more than 7m: A learns, B does not. Bring within speech range with clear corridor: B receives a weaker lesson after a finite conversation interval. Put a wall between: audible radius drops. Give A and C only same faction but different group: no transfer.
- Kill A before it speaks: its unshared experience is erased. Kill A after it speaks: B retains its received experience. Replacement C remains naive even if C joins B's group.
- Save and reopen a live NPC after learning: evidence retained for that survivor; v1/v2 save loads with empty learning, no crash; corrupt/hugely oversized experience lists are bounded and rejected.
- Confirm source-only implementation is not treated as runtime-verified; compile and in-game testing deliberately deferred by user's request.

## Known caveats (keep visible)

- The current conversation mechanism is a **server-side simulated, distance-checked report**, not sound waves, real audio clips, dialogue options or player-visible subtitles. It can be inspected in logs.
- Most strategy memory (learned cover, path selection, zombie tactics, cooperative role changes) is still future work; the first connected slices are loot and gun pacing.
- A physical hit on a player/zombie does not guarantee armor-adjusted damage. Native kill attribution and XP integration remain separate items in the permanent backlog.
- The latest unsent lesson can be replaced by a newer event; multiple unspoken lessons queued/persisted remains pending.


## 2026-10-13 — Additional learned cover-access behavior

- Commits `1f09857381d10bb1c65ee6eaf756d97f3a6c2d50` and `caf314160cef9e46a32e2e7bc5b3303ad1964b47` add `CoverAccess` contextual records and integrate them into `MobileNPCBrainDriver.TryFindNearbyCoverPoint`.
- A `SeekCover` destination **actually reported as reached** records success. If the same goal expires before reaching its destination, record failure. The earlier geometry+short corridor validation still runs, and a previously failed sector is deferred behind other valid candidates. If all valid candidates were penalized, the first is retained as fallback rather than freezing the NPC in the open.
- This is learned **accessibility of a validated cover waypoint**, NOT demonstrated bullet protection, cover selection via complete A*, or enemy-specific tactical success. It does not infer that cover prevents damage merely because a destination was selected. Source-only; no Unity compile or gameplay validation.
- Future work: distinguish inability to reach a waypoint from other tactical interruptions, and use actual damage avoidance/survival before claiming effective defensive strategy learning.


## 2026-10-13 — Decision rule used by real cover selection (not external runtime)

`MobileNPCBrainDriver.TryFindNearbyCoverPoint` now implements a small **epsilon-greedy contextual bandit** over physically validated cover candidates; code commit `e0d5d944c2c11b684c805e2814b8070cf0f9b3cf`. At most 10 candidates (ground, shooter occlusion, and obstacle-free immediate corridor) receive a score = learned local `CoverAccess` preference - modest distance cost. Most selections exploit the best current score; with 0.12 probability select a uniform random alternative from validated candidates (reservoir sampling). Context is spatial sector; reward is observed arrival or failure to reach before goal expiry, **not** confirmed protection from gunfire.

There is no imported ML library. This is a reproducible online exploration/exploitation policy in Unity C#, not deep reinforcement learning and not a fully learned tactical brain. Incoming sensory evidence, authority, path checks and resource rules remain hard constraints. Prior evidence may be stale when buildings change; exploration partially permits rechecking older failures but full route invalidation is still a backlog item. Detailed mathematical alternatives such as UCB or Thompson sampling are optional future policy improvements only after instrumenting actual tactical rewards and running performance tests.

# Mobile NPCs - handoff and project continuity

Last updated: 2026-10-13
Repository: pplopes2003-design/Unturned-Better
Working branch: feature/npc-ai-foundation

## Purpose

This is the durable handoff for continuing the mobile-NPC project across ChatGPT conversations. Read this file first before changing NPC code. Treat the current repository as the source of truth; do not rely only on chat summaries. Inspect relevant implementation and history before editing, then update this handoff when architecture, commits, or test status changes.

## User workflow and collaboration rules

- The user uses GitHub Desktop. Give GitHub Desktop steps for Fetch/Pull/Push where practical; do not assume they want PowerShell Git commands.
- Work incrementally, but do not ask the user to paste source files already available in the repository.
- Never claim a fix is complete or tested unless the commit exists and the relevant build/runtime test actually passed. State whether a change is inspected, committed, compiled, or runtime-verified.
- Do not make destructive changes, reset/clean the branch, or alter Unity .meta files without a concrete reason.
- Mobile NPCs should behave like plausible Unturned players, not invulnerable sentries or perfect bots. They should miss shots, react to threats, navigate naturally, use appropriate animations, and obey the game's real damage/death lifecycle.

## Current baseline and recent repository history

- Branch: feature/npc-ai-foundation.
- Previously verified local HEAD and remote-tracking ref were both 06bb0738744e0cfc77f5e753354b1899d9c95ef9 before later fixes.
- Earlier changes include native A* discovery/diagnostics, stale callback handling, injury/medical systems, damage bridge, nameplates, tactical combat, and humanoid movement/animation work.
- Prior chat reported these commits pushed directly to the branch:
  - b50c457: remove unavailable Unity NavMesh dependency from MobileNPCHumanoidMotor.cs; preserve native Unturned A* and bounded local route fallback.
  - 79a8c48: clarify a route waypoint comment.
  - 54c60a0: fix a type mismatch in MobileNPCPopulationBootstrap.cs by using Behaviour for elements returned as Behaviour[].
- Confirm current branch HEAD before relying on commit order or assuming local checkout contains these commits.
- Unity compilation after these commits has not been confirmed in conversation.

## Current user-reported gameplay bugs (not yet resolved)

1. NPCs do not die after receiving enough firearm hits.
2. NPCs do not visibly register physical damage/hit reactions.
3. NPCs still lack a convincing hand-to-hand strike animation.
4. NPCs get stuck against the player and small elevation transitions, e.g. road-to-sidewalk curbs, which players/zombies normally traverse.
5. An NPC picked up an item and then stood idle beside a table.
6. Earlier issues: name appears only after interaction; dialogue options are not displayed reliably and numeric-key interaction can hide the HUD / ESC does not exit; NPCs may investigate gunshots by rotating in place instead of moving; threat response/target priority and long-range combat need to feel human rather than sentry-like.

## Correct death/damage design requirement

Do not solve NPC death as an isolated custom health flag or by simply deleting the NPC GameObject. The user explicitly requires investigating how Unturned records and processes the death of other living entities, then integrating mobile NPCs into that same lifecycle wherever technically appropriate.

Before implementing death:
1. Trace the actual Unturned firearm/melee damage path from raycast/hit resolution through the native damage API, entity classification, damage permission checks, health update, death event, kill attribution, ragdoll/death animation, loot/drop handling, and entity removal/replication.
2. Compare the NPC runtime entity/component registration and network/single-player authority to existing zombie/player/animal paths. Identify a supported extension point rather than assuming a custom MonoBehaviour receives hits automatically.
3. Trace MobileNPCDamageBridge.cs, MobileNPCHumanoidVitals.cs, MobileNPCInjurySystem.cs, population director/bootstrap, and modified UseableGun.cs / UseableMelee.cs code together.
4. Establish where hits on NPC colliders are routed. A component with health is insufficient if the weapon pipeline never calls it, or if it is called with a different target type.
5. Integrate a single authoritative damage/death transition, avoiding duplicate damage application. Preserve attacker/cause attribution and relevant native game side effects when possible.
6. Verify death stops AI, combat, movement, interaction and item use; triggers fitting death presentation; and removes/replaces population safely only after the intended death flow.
7. Test firearm head/body hits, melee hits, overkill, repeated hits after death, and attribution/loot behavior. Do not claim player-equivalent death is implemented until tested.

## Other required investigation / fixes

### Hit reactions and body-part injury
- Check whether actual projectile/melee hit positions and body colliders are mapped to the injury system's body-part enum.
- Check whether hit reactions are invoked on the animation controller, and verify exact animation names/parameters against animations available in this Unturned build; do not guess clip names.
- Separate hit feedback from damage accounting so visual reactions cannot double-apply damage.

### Hand-to-hand attack
- Trace target selection, range/line-of-sight, attack cooldown, animation trigger, hit timing, and damage application.
- Inspect real animation assets/clips available in the game and reuse supported humanoid animations. Avoid invisible damage-only punches or guessed animation strings.

### Movement, collision, small steps, and recovery
- Inspect MobileNPCPopulationBootstrap.cs CharacterController setup (previously noted around lines 73-80: height 1.8, radius 0.35, slopeLimit 38, stepOffset 0.3, skinWidth 0.08) and compare against the actual Unturned player/zombie controller setup in this repository.
- Inspect MobileNPCHumanoidMotor.cs movement, grounded checks, vertical motion, avoidance, route waypoint advancement, and blocked/stuck recovery.
- Reuse verified game movement behavior/constants where possible. Do not fix curb issues by disabling collisions or teleporting NPCs. Test NPC-player contact and road/sidewalk transitions in runtime.

### Item pickup / AI state deadlock
- Trace pickup selection, server-side pickup success/failure, inventory update, and AI goal completion as one transaction.
- On success, update inventory and end the loot goal; on failure or unreachable items, abandon/retry with bounded cooldown. Ensure reaching a loot destination does not leave a goal permanently active.
- Inspect MobileNPCTactics.cs (loot goal has a deadline) and MobileNPCBrainDriver.cs alongside actual pickup implementation. A goal timeout must trigger a new decision/route, not merely expire a timestamp.

### Dialogue/nameplate
- Fix dialogue UI focus, visible options, numeric-key mapping, ESC/cancel and HUD restoration without hiding the HUD permanently.
- Nameplate should show the NPC name at the appropriate distance/visibility before interaction, subject to intended design.

## Known architecture files to inspect first

Under Assets/Runtime/Assembly-CSharp/Unturned/NPCs/:
- MobileNPCPopulationBootstrap.cs
- MobileNPCPopulationDirector.cs
- MobileNPCBrainDriver.cs
- MobileNPCHumanoidMotor.cs
- MobileNPCHumanoidCombat.cs
- MobileNPCHumanoidRangedCombat.cs
- MobileNPCTacticalCombatBehavior.cs
- MobileNPCTactics.cs
- MobileNPCNativeAStarRouteProvider.cs
- MobileNPCDamageBridge.cs
- MobileNPCHumanoidVitals.cs
- MobileNPCInjurySystem.cs
- MobileNPCMedicalBehavior.cs
- MobileNPCVisualAnimation.cs
- MobileNPCInteractionController.cs
- MobileNPCNameplate.cs
- MobileNPCInventory.cs
- MobileNPCPersistence.cs
- MobileNPCSurvivalNeeds.cs
- MobileNPCAllianceService.cs
- MobileNPCTradeService.cs
- MobileNPCAirdropContest.cs

Also inspect Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs, UseableMelee.cs, and the real game damage/entity lifecycle implementations before modifying combat/death.

## Recommended workflow for every new conversation

1. Read this document on feature/npc-ai-foundation.
2. Check current HEAD and recent commits; compare with the user's local GitHub Desktop checkout if needed.
3. Read current versions of relevant source files and inspect diffs/history. Do not assume line numbers or earlier chat code are current.
4. Pick one coherent slice, preferably damage/death first because it underpins combat testing.
5. Make a focused commit and update this handoff with commit SHA, changed files, and explicit test status.
6. Tell the user to use GitHub Desktop: Fetch origin, then Pull origin if available. If local changes are present, inspect before overwriting.
7. Give a small, concrete in-game test plan and ask for exact logs/results.
8. Track each issue as Not investigated / Investigated / Implemented / Compile-verified / Runtime-verified; never conflate these statuses.

## Investigation update — 2026-10-13

### Damage-path findings from current source

- The current firearm path in Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs resolves MobileNPCHumanoidVitals from the authoritative collider transform (falling back to the raycast transform) and directly calls the custom ApplyDamage method on the server.
- The current melee path in Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableMelee.cs has a similar direct custom-vitals branch. Therefore the mobile NPC is not currently being passed through the same native Player/Zombie/Animal damage/death routine; the current hit integration only borrows the weapon's damage calculation.
- MobileNPCHumanoidVitals.DieServer marks the custom component dead, resets/disables AI and movement/combat, and raises DeathPresentationRequested/Died events. It explicitly does not create a native Unturned death/ragdoll or network message. MobileNPCPopulationDirector retains the dead GameObject for deadBodyLifetime and queues a delayed replacement. A stationary upright NPC can therefore look alive even if custom health reached zero.
- The visual hitbox created by MobileNPCPopulationBootstrap is a trigger capsule, while the visual rig's colliders are disabled. Must verify that DamageTool.raycast and server InputInfo collider data consistently see that trigger in the user's build; a direct component lookup alone does not prove a hit was applied.
- Gun and melee custom damage calls previously did not pass the attacking player's Transform, leaving LastAttacker unset for these hits. Two focused commits now pass player.transform into ApplyDamage, so the existing custom vitals preserve the source for subsequent attribution/AI consequences. This is a partial attribution fix, not native kill credit and not a complete death fix.

### Investigation commits

- 08e150df7f96e9023c8279fcc9cef34c3b96e43b — preserve firearm attacker when applying custom NPC damage.
- 16bef97667355084416f1ba09e84a65272cea3af — preserve melee attacker when applying custom NPC damage.

### Next required work

1. Inspect the actual native implementations of DamageTool.damageZombie / damageAnimal and Zombie.askDamage, plus the game's supported death/ragdoll and loot pathways. Do not call Player-only damage APIs with a fake Player.
2. Add temporary, bounded diagnostics at the authoritative gun/melee hit branch: hit transform/collider, resolved NPC name, server authority, calculated damage, health before/after, and whether DieServer fired. Avoid logging every frame.
3. Confirm hit registration and custom health loss first, then choose a correct native-compatible NPC death presentation/entity lifecycle. Do not disguise an unresolved hit pipeline by lowering health or forcibly destroying the NPC.
4. Verify melee hit resolution separately; NPC-vs-NPC melee already calls custom vitals, but player melee's exact target branch and collider data must be runtime-tested.
5. Unity compile and runtime status remains unknown; the two attribution commits have not been build-verified in this conversation.

## Investigation update — loot-goal deadlock fix

- Commit af49be9cd00b7a4dc6e70df207aea807cb9fb849 changes MobileNPCLooting.cs.
- Confirmed failure mode in source: MobileNPCBrainDriver.TickServer clears an expired tactical goal, but MobileNPCLooting previously retained hasTarget and returned each Update while the world item remained present. That can leave the NPC with no active movement destination but a stale loot target, matching the report of standing idle beside an item/table.
- The looting loop now detects when its target exists but the tactical Loot goal has expired/been cleared, drops the stale target, and waits for the bounded scan cooldown before choosing again.
- Pickup transfer success/failure both finish the loot goal and apply a bounded rescan cooldown; a failed transfer emits a diagnostic.
- This is source-level correction only. It has not been compiled or runtime-tested in Unity. The failed-transfer diagnostic currently reads targetInstanceID after ClearTarget resets it, so it may log 0 rather than the attempted ID; fix that small diagnostic detail before treating the logging as useful.
- Damage/death still requires native lifecycle research and runtime confirmation; no native death implementation is claimed.

## Status at time of writing

- Death/damage lifecycle: Not yet verified end-to-end.
- Hit reactions/body-part damage: Not yet verified end-to-end.
- Hand-to-hand animation: Not yet verified in-game.
- Step/collision/stuck recovery: Not yet verified in-game.
- Pickup-goal completion: Not yet verified end-to-end.
- Latest Unity build result after recent commits: Unknown; awaiting user feedback.

## Death-presentation implementation update — 2026-10-13

- Commit `78802eecf46b46d5e5fb767c5cd58eb0d4093704` updates `MobileNPCHumanoidVitals.DieServer` to stop the visual rig's Animation/Animator writers, enable the non-trigger colliders authored under `HumanoidVisual`, release its authored rigidbodies into physics, and apply the lethal hit impulse to the nearest rigidbody. This is intended to stop the zero-health NPC remaining upright while avoiding misuse of Player-only native death APIs.
- The bootstrap currently disables the visual rig's colliders and sets its rigidbodies kinematic, so the new death path reverses those settings at the death transition. It logs an explicit error if the prefab has no usable ragdoll bodies/colliders.
- Native source inspection (Unturned-Datamining, linux-client-preview) confirms vanilla zombie death is not just a boolean: `Zombie.askDamage` updates health, awards kill/XP, optionally drops loot, and calls `ZombieManager.sendZombieDead`; `Zombie.tellDead` sets the dead state and invokes `RagdollTool.ragdollZombie`. Animal death follows a similar native entity-specific path. The mobile NPC is neither a registered `Zombie` nor `Animal`, and PlayerLife/RagdollTool.ragdollPlayer is not a valid substitute for it.
- This commit is **not yet compiled or runtime-tested**. It is not enough to mark the full death issue resolved. Still required: verify the NPC_Client prefab actually contains connected ragdoll rigidbodies/colliders; confirm gun and melee damage reaches health zero; confirm one death transition only; verify attacker/cause attribution, death-state AI/interaction suppression, corpse lifetime and replacement, and that no unwanted loot/XP behavior is silently implied. Current project is local single-player and the bootstrap explicitly states network replication is not implemented, so native multiplayer replication is not claimed.

- Follow-up commit `67ac67b88034883dd3b7adda85050803f46b152e` subscribes `MobileNPCVisualAnimation` to `MobileNPCHumanoidVitals.Died` and disables the locomotion animation writer on death, preventing its next Update from attempting to cross-fade idle over the released ragdoll. This is source-level only; Unity compile/runtime verification is still pending.

- Commit `f1d32050e52b90aeacd1683c881733b7d43e385d` additionally disables server-side looting, medical use, survival ticking, settlement planning and settlement building at the death transition. These components can otherwise keep running even after the brain and motor stop. The interaction controller already rejects dead NPCs in its Update path. Still source-only; no Unity compile/runtime result is available.

## Death diagnostics update — 2026-10-13

- Commit `1bcedb4528ba2bacfb0465e7fab33f2d47aeb9ae` adds a serialized `logDamageDiagnostics` switch (enabled by default) to `MobileNPCHumanoidVitals`.
- When enabled, each accepted server-side damage event logs NPC name, attacker (or environmental source), damage, health before/after, and whether the hit was lethal. The one-time death transition logs the last attacker and timestamp.
- The death-rig audit now counts Unity `Joint` components as well as released rigidbodies and enabled colliders. It logs an error if any of those categories is missing, so the Unity console can distinguish “health reached zero” from “usable ragdoll hierarchy was found.”
- This is diagnostic instrumentation, not proof the hit pipeline or ragdoll works in runtime. Because a log is emitted per accepted hit, disable `Log Damage Diagnostics` on the `MobileNPCHumanoidVitals` component after collecting the needed test evidence.
- Still not Unity-compiled or runtime-verified. The next test should inspect whether gunfire logs `Damage accepted`, then whether lethal damage logs `Death transition` and `Death ragdoll activated` with nonzero bodies/colliders/joints. If no damage log appears, the problem is upstream hit resolution, not the health subtraction method.



## Code review and death-lifecycle fixes — 2026-10-13

- Commit `f2cce7099582f6d02a89fd79b987028997db218e`: `MobileNPCHumanoidVitals` now replaces `LastAttacker` on *every* accepted hit, including a null environmental attacker, avoiding stale killer attribution. All relevant gameplay components stop unconditionally on death (rather than depending on the previous `disableCombatOnDeath` serialized toggle, retained for compatibility).
- Commit `b3216cecdb43da0dd643d92c1f43952fd49d726a`: `MobileNPCPopulationDirector.Update` exits on non-server contexts and tracks newly restored/external NPCs before corpse cleanup, reducing missed/delayed death tracking.
- Review finding: `MobileNPCPopulationBootstrap` creates a **trigger** CapsuleCollider on visual layer 16. Gun/melee damage resolves `info.colliderTransform` server-side, but source verification has **not established** whether native DamageTool raycasts include that trigger/layer in `RayMasks.DAMAGE_CLIENT` / the authoritative server input path. This remains a likely integration blocker and must be tested/confirmed, not assumed fixed.
- Review finding: `ActivateAuthoredRagdoll` requires usable `Rigidbody`, `Collider`, and `Joint` components in `HumanoidVisual`; logs detect missing components but don't create a ragdoll. Confirm authored rig in Unity and verify actual bone stability, impulse and corpse fall.
- Current source changes are uncompiled and not Unity-tested. Native vanilla kill credit, quest tracking, XP and network death replication are not implemented for this custom entity.


## Priority 1 hit-detection fix — 2026-10-13

- `8c8674762448d76b4148306bae4a50dbd93b0f51`: The custom `MobileNPCHitbox` is now on Unturned's **Entity** layer (23), distinct from the visual prefab's **Medium** layer (16). It is now a non-trigger collider, like ordinary damageable character hit volumes; root CharacterController collision against it is explicitly ignored. Runtime logs expose whether `RayMasks.DAMAGE_CLIENT` and `RayMasks.DAMAGE_SERVER` include Entity layer. This addresses a high-probability filtering error but has **not** yet been verified in an actual damage raycast.
- `ee45e6bbbb7353e4e853d1562c16bb6f331e888d`: Death disables the live `MobileNPCHitbox` before releasing the visual ragdoll bodies, so it no longer blocks subsequent hits as an alive NPC.
- Validate on Unity build: inspector layer on spawned `MobileNPCHitbox` is Entity (23); both DAMAGE mask diagnostics are true; ordinary gun and melee hits produce `Damage accepted` logs; lethal hit produces `Death transition` and `Death ragdoll activated`. Confirm overlap physics and ragdoll remain stable. Existing NPC rigs may still lack collider/joint/bone components and therefore need prefab fixes.
- Follow-up review needed: native server hit validation and raycast transform propagation, projectile weapons, ragdoll quality and replacement fairness. Avoid claiming native kill credit or network replication.

- `8a5719b79e3b061eec5bd6e6f2d68cc8d8721676`: `UseableGun` client NPC hitmarker lookup now prefers actual `info.collider.transform`, matching the hit volume rather than relying on `info.transform` alone.
- `0150f973e2345928e5e48f3aa6678c328c288b5e`: `UseableMelee` client NPC hitmarker lookup accepts `info.colliderTransform` fallback before `info.transform`.
- Warning: `Physics.IgnoreCollision` performed while building the inactive template should be verified at runtime (Unity may reset ignore pairs on activation). Ensure Entity-layer hitbox does not impede CharacterController movement.


## Death-body implementation round — 2026-10-13

Code committed directly to `feature/npc-ai-foundation`:

- `0b5c4c5dc9672c8f7a1d53dbfac2a066dd1624b6` — `MobileNPCHumanoidVitals.ActivateAuthoredRagdoll` now checks authored Rigidbody, Joint, and non-trigger Collider completeness before attempting a skeletal fall. If incomplete, `ActivateRigidCorpseFallback` disables incomplete bone physics, creates an Agent-layer capsule/Rigidbody corpse proxy, reparents the existing visible `HumanoidVisual` into it, and applies a lethal impulse. Corpse proxy remains nested under the NPC root for existing cleanup. **Fallback is intentionally a rigid-body corpse, not a bone ragdoll**.
- `0f6a9e2668e40da2905ef6fb4a8f016a0cc64046` — death stops ranged presentation, nameplate, and visual guard, and adds tipping torque to fallback capsule.
- `b39b2925b037313bb004ed918b667edc2ace7b2c` — `MobileNPCSpawnService` reapplies `Physics.IgnoreCollision` between live spawned CharacterController and custom NPC hitbox because ignored collision pairs may reset when activating an inactive template.
- Source inspection confirmed new methods, brace balance and expected code snippets; **NO Unity build, C# compilation, or actual hit/death test has run**. No claim of fully working skeletal ragdoll is possible without confirming actual NPC_Client bone components.

Unity smoke test: spawn mobile NPC; inspect MobileNPCHitbox on Entity layer and root Agent controller; shoot repeatedly and capture `Damage accepted` and `Death transition`; on lethal damage verify either `Death ragdoll activated` or `activated physical capsule corpse fallback`, confirm visual topples, living hitbox disabled, NPC stops fighting, body removes after deadBodyLifetime, replacement waits respawnDelay, no repeated replacement. If error remains, capture Player.log and Unity Console. Known risk: native raycast/InputInfo may still filter custom collider; inspect masks and server-side colliderTransform logs. Native XP/kill-credit/network replication still not implemented.


## Integrated death-cycle round — 2026-10-13

This round reviewed together: `MobileNPCHumanoidVitals`, `MobileNPCPopulationDirector`, `MobileNPCSpawnService`, `MobileNPCPopulationBootstrap`, `MobileNPCVisualAnimation`, `MobileNPCBrainDriver`, `MobileNPCRegistry`, `MobileNPCPersistence`, `MobileNPCGroupMember`, `MobileNPCHumanoidCombat`, `MobileNPCHumanoidRangedCombat`, and `MobileNPCInjurySystem`.

Changes:
- `b352c4331a77cfdb571c29390685902237597010` — introduced idempotent `RegisterSpawnedNPC` and replaced duplicate tracking branches with this one lifecycle registration path.
- `c2303256009bb37ea79ab3afbe059f4af2167824` — register every successful spawn immediately, including restored/manual spawns, before the brain could unregister on a same-frame death.
- `cf647c7919444c4a0a7f810b57f3adfc23cd9e7b` — health InitializeServer is idempotent; repeated initialization cannot heal/resurrect a damaged/dead NPC.
- `016da8a25ebc11962f80cb650820704e7473270a` — failed corpse despawn remains tracked and retries after 5 seconds, instead of disappearing from director bookkeeping.
- `cbb6c266a7756212c9580835d336a723b1cc4ee5` — refresh tracked group ID when delayed replacement joins a surviving group.
- `b4e857074f91a567a0f04f28fd26b48282964467` — logs for death queue times and completed corpse cleanup to correlate with Player.log.

Static review: all 3 changed C# files were fetched back, and brace counts match. This is NOT a C# compiler check. Unity compilation, native raycast registration / client-server hit propagation, real ragdoll movement, and respawn timing still require runtime validation. In particular the fallback corpse is rigid, not articulated. Native kill credit / XP / network replication remain out of scope for custom local entities.

Suggested Unity validation: shoot an NPC until `Damage accepted` then `Death transition`; record whether `Death ragdoll activated` or `activated physical capsule corpse fallback`; observe stop of combat, fallen corpse and disabled live hitbox; find `Death queued` followed by `Corpse cleanup completed`; check replacement only after configured respawnDelay and group inheritance when survivors remain. For unresolved shots, capture diagnostics for RayMasks and input's colliderTransform, and Player.log. Source-only changes cannot prove behavior.


## Movement integration corrections — 2026-10-13 (build not yet tested)

User will build/test movement and death together later. Three source commits on `feature/npc-ai-foundation`:

- `2a2104d851de4f8dd0e672cbd2c9914e7a761121` `MobileNPCHumanoidMotor.cs`: wall avoidance no longer chooses random left/right every frame when both directions are open. Maintains a consistent side for ~3 seconds; on repeated stuck detections alternates side between recovery attempts and emits more coherent steering. Throttles new native A* route retrieval attempts to once per 0.75 seconds, rather than querying every frame with no route.
- `cef029bcd497efdc76583ad9edef62a681e656e3`: advances A* waypoints with horizontal distance so minor height differences don't strand the route; considers a temporary detour reached when within planar 0.8 m, then resumes original goal. Ground-ahead test now probes within 0.65–1.25 m rather than a 1.5–3 m distant point; limits positive rise using CharacterController stepOffset/skinWidth and downward change to ~1.1 m, instead of permitting a 2.25 m cliff/step. Test sidewalks, steps and slopes in Unity.
- `a3068fb2d44df8030a402b7413899b7f9c70b618`: clears stale planned routes/detours when no destination remains; treats a 1.5 m horizontal goal displacement as reason to invalidate a cached route; adds warning with position, steering side, recovery count, and tactical goal for repeated stalls.

Fetched the final file from GitHub and confirmed each new code location; braces balanced. **No compilation or Unity build/run performed**, so movement and pathing correctness is not yet proven.

Combined build tests: spawn NPC and verify damaging/killing (accepted hit, death transition, physical corpse and cleanup); navigate along a house wall then around a corner without side-to-side ping-pong; walk over curb and small stair; chase player with clear visibility; investigate noise behind building and actually walk rather than spin; retreat / cover movement; wait for corpse cleanup and replacement. Collect `Player.log` and record rough map locations and observed behavior. If navigation still fails, capture `[MobileNPCHumanoidMotor] Blocked at` warnings and native A* diagnostics.


## Mobile NPC dialogue HUD overhaul — 2026-10-13 (source-only)

- `a0ffe4cba56db8495cb574aa12f2470ae0bc4279`: `MobileNPCInteractionController` no longer abuses `PlayerUI.message(EPlayerMessage.NPC_CUSTOM, ...)` as a persistent conversation interface. A local `OnGUI` panel (excluded from dedicated server) shows message content, scrollable stock listings, numbered selection buttons and close button. Keyboard keys Alpha1–9 / Keypad1–9 and Escape are retained. All choices dispatch via one `ChooseOption` state handler; no game HUD hide/show API is called. Mouse button usability when gameplay cursor is locked still requires runtime confirmation.
- `a204b75f0af94bf1a00f60780d8c7419edaa110a`: successful alliance returns to visible menu with success notice rather than immediately clearing the dialogue.
- `d7083afd961b924ca6e492c722530a8676d829a1`: NPC nameplate TextMesh now uses visual humanoid render layer rather than unconditional Default (0), improving camera culling compatibility before interaction.
- `aef356eb40b518d46203f42fbf48d5459aaaae31`: hostile NPC refusal is shown until dismissed rather than cleared immediately; hostile roles cannot choose trade/alliance through numeric keys.
- GitHub source reread confirmed `PlayerUI.message` absent and panel/keyboard callbacks present; simple brace balance passes. **Unity compilation and actual HUD/input behavior NOT verified**.
- Combined build test: approach friendly/neutral NPC, confirm nameplate visible prior to E; open interaction, read options and stock, use number row and numpad, click if mouse cursor usable, ESC to close, repeat; verify native HUD remains visible and ESC still works afterward, including when leaving range or NPC dies. Test hostile refusal, alliance confirmation notice, and trade stock scrolling. If camera culls the text or hotbar selection interferes with dialogue keys, include Player.log and reproduction steps.

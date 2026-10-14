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


## Ranged combat / cover / animation round — 2026-10-13

User requested to address dialogue (previous round), then ranged combat, cover and animation interactions, and to report all still-pending items after each development round.

Applied commits:
- `c43bb0e16aadf3bf31d117580ab5480958cbafe3`, `b88c05b20bbff0670be501371d5885ad162e2c08`, `21400802c1ed2c04c37bf24b0e25417df7e36b79`: NPC gunshots now notify other mobile NPCs through `MobileNPCRegistry.NotifyGunfire`; method has optional `sourceNPC` and excludes that shooter to prevent self-suppression. Original player gunfire call remains compatible.
- `05ae01162ea4e6de7cb9d5d245a6d2a82a93a798`: `MobileNPCBrainDriver.TryFindNearbyCoverPoint` ignores character collider hits when assessing genuine world cover, and checks for immediate chest-height blockers along a direct path to candidate. This rejects demonstrably inaccessible straight-line candidates; does not constitute full A* reachability proof.
- `68ddb46b2902d3823f35a203a200fb57b90682c2`: tactical line-of-fire raycast ignores own NPC colliders, selecting closest external hit instead of false occlusion by self.
- `eac7099384140a2db2404450c46fb0b0784fe654`, `979ed8a3642c2fe35fee54cfb31614f97b184b4a`: `MobileNPCVisualAnimation` subscribes to ranged shot/reload and damage-received events. Plays only clips actually present on the loaded legacy Animation rig, matching shoot/fire/recoil, reload, and hit/flinch/damage/hurt names at runtime. If absent it does not invent a clip; unsubscription on destroy is explicit.

Static verification: re-fetched the five modified NPC source files, confirmed expected content and balanced braces. No Unity C# compile or gameplay run performed. Prior GUI panel and nameplate changes remain source-only until build validation.

**Still pending as of this round:** Unity compile/build; in-game ranged-hit reliability at long distance, fair spread and reload behavior, and real accuracy vs player/NPC/zombies; coverage quality/reachability with A* across buildings and under suppression; availability and quality of actual strike, firearm, reload, hit and death animations on NPC_Client; working mouse focus and ESC/game HUD restoration for the IMGUI interaction panel; interaction nameplate visibility; full death-hitbox/corpse/removal/respawn smoke test; full movement/stairs/corners/investigation test; native XP/kill attribution/network replication not implemented for custom server-local mobile NPCs. Collect Player.log from one combined test build when user is ready.


## Consolidation before stealth — 2026-10-13

Consolidation commits:
- `c7392b082eda842074da7ab748e195b231379b7a`: keep an existing cover/retreat destination for a short, 2.5-second defensive planning window under repetitive nearby gunfire; replan for threats displaced by >10m. This preserves suppression and noise stimulus while reducing thrashing between cover goals.
- `9d6067905436a0e6e77dc41cb29479bec2ea9367`: one `IsSessionValid()` guard is shared by the dialogue Update, IMGUI and keyboard/click action handler, preventing purchases/alliance actions after the NPC dies, player dies, or player leaves interaction range.
- `c963a7dde5f030d58448d53f77a21990ad8c996f`: death stops remaining tactical-combat, escort, medical behavior and injury ticking, in addition to earlier AI/movement/combat shutdown.

These three files were re-read from the working branch and have matching opening/closing brace counts. Source check is NOT a Unity compilation or runtime test.

### Next-phase design request: stalking / stealth (DO NOT implement before current fixes are validated)

User requested intelligent optional NPC stealth **after consolidation**, including:
1. NPC may crouch and move quietly to bypass a zombie without alerting it.
2. NPC may stalk and ambush hostile zombies or player when tactically sensible, rather than always firing instantly.
3. On hearing player's footsteps, NPC may stop moving/crouch, suppress its own footsteps, listen and infer player's last-known location before pursuing or ambushing.
4. Build stealth as a decision-tree/utility substate coordinated with existing perception, suspicion, sound, target memory, faction hostility and tactical movement; not omniscient detection or teleporting.
5. Check the native game's crouch/stance animation clips and audio/footstep APIs before coding. Include bounded search/investigation, detection exposure, abort rules and fair player counterplay. Avoid cheating NPC senses and keep CPU bounded.

### Still pending before feature expansion

- No Dev Build or Unity compile on latest code. User will test all systems in one build later.
- Death: reliable firearm/melee raycast, actual ragdoll/fallback and corpse removal, respawn timing.
- Navigation: around building corners, curb/stairs, A* reachability of defensive positions, noise investigation.
- Ranged combat: weapon accuracy, long-distance range, ammunition/reload, line-of-fire safety against all target types.
- UI: IMGUI panel actual focus/mouse click behavior under locked cursor, keyboard input and ESC/HUD behavior; nameplate visibility.
- Animation: available strike/recoil/reload/flinch clips on NPC_Client, visual sync with shots/hits, animation quality.
- Longer-term: native kill XP/credit and network replication of custom mobile entities.


## Advisory cognition foundation / future stealth — 2026-10-13

User approved starting the *architecture* of dual AI, with legacy fixes still awaiting combined Unity validation. First cognitive layer controls the NPC (MobileNPCBrain/Driver + Tactics); the second is a strictly evidence-only sensory memory, NOT an oracle reading the live player Transform after losing LOS.

Committed:
- `b4a694650f990f236539e3f61cfb820850107a43`: new plain C# `MobileNPCPlayerKnowledge` advisor; records server-confirmed sight and identified sounds, limited observed velocity, confidence/time decay, expanding location uncertainty, up to 4 randomized bounded hints and nonpersistent per-NPC memory.
- `4ac862212d43b298d905d30f1145fd1caaf7431b`: `MobileNPCBrain.TrySetInvestigationHint` accepts validated suggestions only in Investigating, never extending investigation expiry.
- `8e0ea49990b1efcc1c69c255545e28a5fae32c52`: `MobileNPCBrainDriver` feeds only confirmed visible Player transforms to visual knowledge; identified player gunfire can yield lower-confidence sound evidence; memory suggestions are checked for server-side safe ground, without movement teleport; current goal still owned by normal brain.
- `6f86731bb7df20eedd986db493c5095cbf65b800`: prevent stale player memory contaminating NPC/zombie/ordinary sound investigations; hints are restricted to a search following lost confirmed Player pursuit and cleared on other goals/reset.

Reread new/modified C# files, checked expected integration points and balanced braces. NOT compiled nor tested in Unity. The assistant did NOT implement crouch, silent footsteps, player footstep detection, ambush, zoom perception, knowledge sharing or learning yet.

### Future stealth/stalking implementation plan
- **Sensory model:** vision with line-of-sight, field of view, illumination/exposure and distance; optics only extend *the observing NPC's* view when they actively use binoculars or a scope, never because the player is zooming. For player zoom, only observable cues (e.g. visible movement) matter. Audio cues from actual player footsteps, sprint, crouch, gunshots, collisions and relevant actions; attenuation by range, obstacles/surfaces and ambient masking. Must hook native Unturned sources, not poll true player position as a shortcut.
- **Stealth tactical states:** listen/freeze, crouch & low-speed silent walking, shadow or bypass zombie, cautious pursuit, ambush when close and unnoticed, abandon stealth when spotted, suppressed or critically injured. Reuse verified native animations/stances, actual collision capsule and audio; no arbitrary clip name guesses.
- **Investigation:** uncertain hypotheses and search priors from sensed events only, plausible last-known velocity and nearby obstacles, limited retries, forget/decay; avoid deterministic spiral as only behavior and avoid psychic navigation.
- **Future learning/social memory (not yet in scope):** constrained per-NPC adaptive statistics / tabular reinforcement or utility-weight tuning before heavy ML training; share knowledge only when two living NPCs actually communicate in-world within hearing/radio range; receiver gets partial aged confidence, not a globally synced omniscient memory. Unshared memory dies with the NPC. Limit memory size, CPU and runaway difficulty; allow player counterplay and faction-filtered communication.
- **Prerequisite runtime validation remains outstanding:** Unity compilation; hit-to-death/ragdoll/despawn/respawn; house corner and curb navigation; 30–50m firefight and aiming realism; cover reachability; clip availability (punch/fire/reload/flinch); dialogue focus/ESC/HUD/nameplates. Native kill XP and networking not implemented. Report these pending items at end of every future round.


## Tactical locomotion decision layer — 2026-10-13

User specifically requested that NPCs dynamically decide whether to crouch, walk, sprint, jump, or pause, instead of repeating one movement action. This round adds tactical movement decisions; no stealth guarantee without zombie hearing integration.

Commits:
- `3df81c98f45192018a6e0b717bac3954d5f6da36`: new pure C# `MobileNPCLocomotionPolicy`, a context-based gait utility layer (Hold, Sneak, Walk, Sprint) considering pursuit, target sight, investigation, close proximity, retreat, suppression, cover, injury, swimming and destination. Minimum gait commitment avoids per-frame oscillation.
- `ccf99755ff009ff62e6b150a7e7687753e048cca`, `3ed0625396186c4388a7c72baa7780d617f7c552`: motor uses gait-selected run/walk/sneak speed and a guarded low-obstacle jump with cooldown. Jump requires ground, non-sneaking gait, injury threshold, low obstacle but clear head space, and terrain ahead. Fixed initial lower-obstacle probe so it does not depend on chest-height blockage.
- `363429ec4c395329f86308f1b937ba278ea612aa`, `edc08cf1038a71691a0f2a4a164c55989f05bd77`: NPC motor now changes actual CharacterController height/center to crouch and checks available head space before standing. Clearance uses only the newly occupied upper region to avoid floor contacts falsely blocking stand-up.
- `ca16d523fb3238d0d38db27fcd8d2a8e5df2c18c`: animation locomotion follows actual gait rather than only pursuing state; searches runtime clips for crouch/sneak and falls back to authored walking if missing. Crouched footsteps reduced in volume and frequency. This is audio presentation only; native zombie hearing attenuation and player-footstep perception still need implementation.

Fetched all three key source files after changes and verified braces balanced and presence of policy/crouch/jump/audio hookups, but no Unity compile/runtime validation occurred.

**Outstanding after this round:** Unity build and runtime check; reliable zombie notice/ignore while stealthing (native sound/hide mechanic), player footstep stimuli, real zoom/optics visibility, stalking ambush state machine, ML/social learning, obstacle jumping/safe stand-up in varied map geometry; and earlier death/respawn, navigation/cover, ranged fire, nameplates/dialogue UI, animation clip availability. Native XP credit/network replication not implemented. In-game tests must specifically check no crouch-lock, repetitive hopping, hotbar input conflicts, footstep audibility, and AI pose transitions.


## Stealth hearing integration round — 2026-10-13

The user said "Próxima etapa", following the request to link NPC sneak/crouch gait with game senses. New source-only changes committed to `feature/npc-ai-foundation`:

- `ad8a615dcfe51631d0af7e36954a0b311da42594`: new server-only `MobileNPCPlayerFootstepSensor` sampling actual planar Player displacement at 0.2-second intervals; uses footfall cooldowns, estimated walking/running speed and bounded radius (9 m walk, 17 m run). Stationary/very slow displacement and implausibly fast movement produce no sound. This is an approximation, **not** an actual native footstep event, stance detection, or source of perfect player location.
- `33a2cb752f0fbd92713b8be2c0f7ac2a0d20b2c7`, `d0e944005e0dce24d452cce5b2a8f43891122993`: `MobileNPCRegistry.NotifyPlayerFootstep` routes only nearby plausible audible sound evidence, attenuates behind world obstacles and adds confidence-based position error. Occlusion ray excludes listener and humanoid shells.
- `be07b9c533317bb4b3e26625c44cb49fe6047f6f`: `MobileNPCBrainDriver.ReportPlayerFootstepClue` uses approximate sound evidence, no player reference, pursuit target or sight claim. A 3–6-second cooldown prevents perpetual resetting, and active pursuit/retreat/cover is not overridden.
- `86b69e03e8e05b4db42a663af22715f0ebe307ba`: installs player-footstep sensor with single-player runtime director.
- `5fefadd7c8cf9ff8a06fa2b46e51c19f708d302a`: mobile NPC motor emits native `ZombieManager.alertZombiesToNoise` only for actual moving NPC: approximately 9 m sprint, 3.5 m walk, none crouched; per-footstep cooldown limits frequency.

Fetched five changed files back from GitHub and checked code presence and brace balance; **NO UNITY BUILD OR TEST**, nor proof native zombie response/noise masks are right.

Critical future follow-ups: more accurate native footstep event/stance/terrain-based volume if game's private runtime API is exposed; hearing calibration/obstacle materials and distance, moving vehicle false positives; prevent improbable shared omniscient inference across groups; verify that NPCs can actually crouch/stand, that zombie response is audible and throttled; stealth intent (listening/ambushing/stalking) and zoom optics still pending. Earlier death, movement, combat, cover, animation, dialogue/HUD issues still await user's combined Dev Build and Player.log.


## Listening and suspicion decision round — 2026-10-13

User requested the next stage of cautious listening/stealth investigation. Commits:
- `07b1fc358bd1a952cc1808aa91b64da3d15eee6c` `MobileNPCBrainDriver`: uncertain footstep sound now triggers a bounded listening window (about 0.55–1.4s). Low clarity (<0.25) yields watchful pause without initiating travel; clearer cues produce a 10s investigation. Footstep cooldown ensures no per-frame infinite resets. Valid visual acquisition and incoming fire interrupt the pause immediately; active pursuit/cover/retreat retain priority.
- `917193b4aea40ad5dcc1d8282168fc166e793d5c` `MobileNPCHumanoidMotor`: active listening holds movement and crouches while preserving the existing destination for later investigation; it cannot reset navigation by itself.
- `edcdadf8a377a3fd12863fc202c510c2ea116923`, `84a375075bc304b6f08f3a1b4ad7073c6d42ae40`: listening actor orients more slowly toward the *approximate* sound clue without exposing live hidden player coordinates or walking to it prematurely.
- Source files re-fetched, key integration confirmed, braces matched. **No Unity compile or gameplay test.**

Outstanding: native player footstep and stance detection refinement; zombie native hearing calibration; full stalking/ambush planning (cover-to-cover movement and concealment), optics/zoom; evaluation of actual crouch/stand, terrain transitions, noise suppression and NPC turn animation; prior death cycle/respawn, navigation, ranged combat, cover, dialogue/HUD, animation clip checks all need combined Unity build and Player.log. Future adaptive learning and per-NPC conversations sharing bounded memories remain planned only.


## Stalking and concealed approach — 2026-10-13 (not compiled)

User requested next stage after listening/suspicion: cautious stalking from known clues, hiding behind geometry, ambush posture with high-priority interruption. Changes committed in `feature/npc-ai-foundation`:

- `393d90220b24742255c8731b0fe2eb29797debee`: PlayerKnowledge exposes a confidence- and age-decayed evidence snapshot (last confirmed sight/sound plus uncertainty); no hidden live player transform in the advisor.
- `e904b1b29d5e1637414232378eabf8fecdc6b1c7`: adds `MobileNPCTactics.Goal.Stalk` and bounded 7s tactical waypoint, disallowing flank/loot override and allowing cover/retreat priorities to preempt.
- `6305e89233089f7135818c043d0d5f2928456fef`: for hostile NPCs only, BrainDriver evaluates limited candidate positions relative to previously observed player evidence, rejecting unsafe ground, immediately blocked approach or no environmental concealment. Chooses short protected waypoint based on cover geometry, with 5s planning cooldown and variation. Waits briefly at destination. Actual target acquisition still requires visible confirmation; no firing permission is gained from evidence alone.
- `4690c65f626a0dca613e16b28bc8042d297f9d50`, `5fb85daab11abbda2b2228d2bd06a158666f178d`: policy/motor give Stalk movement sneak/crouch gait.
- `add2fa258229c74aaab7b9fa434aacf4b297c848`: fixes a hidden-target cheating risk: tactical combat no longer computes flanking from a hidden target's current Transform when target is not confirmed by server perception. The last-known-position cognition owns hidden searches.

Source-only verification: six files re-fetched directly from GitHub and checked brace balance. Still **NO Unity C# compilation, build or gameplay verification**.

Outstanding:
- Real ambush choreography and targeting with visual confirmation, delayed attack/aim readiness, NPC versus zombie stealth, true waypoint reachability around larger buildings and bounded failure memory.
- Optics/zoom perception and reliable native stance/footstep/crouch animations (asset lookup).
- Prior Unity integration smoke tests: damage/death/corpse/respawn, movement/corner/stairs/jumps, NPC/zombie sound reaction, ranged accuracy/cover, UI/HUD/nameplate, animation availability, game log.
- Future: per-NPC experience/ML-like adaptation, in-world transmission and loss of unsent memories at death; native XP attribution and multiplayer replication not implemented.


## Ambush combat readiness + future group combat roadmap — 2026-10-13

User requested next stage of stealth and explicitly added three deferred features. They also requested a **complete architecture diagram only after every AI subsystem is designed and structured**, not an incomplete premature diagram. At the end of EACH round, explicitly enumerate all remaining work.

Implemented source-only ambush readiness (Unity untested):
- `8799ed323c3eb491d32a5cebb49f2dfd59354c11`: `MobileNPCBrainDriver` arms a limited ambush opportunity following a completed `Stalk` waypoint (5s) or visual detection during stalking (2s), but NEVER acquires a target from memory alone. Only visually confirmed targets at 2–18m trigger a one-shot randomized 0.45–0.85s readiness interval. Threat gunfire or damage cancels it. No guaranteed hit bonus/damage multiplier is invented.
- `348b617a6e0450468248337017ae8c8eb0e17869`: ranged attacks hold fire during readiness.
- `84e3caf014542b936962eb25994cf4a196e65cae`: melee attacks likewise wait until readiness ends.
- `dc0ae06069847f9de7538afb9ec8aada828d9ff9`, `a135698ed0536c1f1407fb72f692e61b13d55acd`: motor holds position and crouches while preparing, without destroying the target.
- Re-fetched all four touched C# files and verified expected lines/braces. **No Unity compiler or runtime test conducted**. This is an initial ambush *readiness/decision* layer, not a complete stealth-kill, grenade, suppression or tactics system.

### User-specified deferred combat features (keep all on roadmap)
1. **Frag grenades:** when a player is hidden behind cover, NPCs assess whether a grenade is preferable to shooting. Check actual grenade inventory, trajectory, collision/bounce, explosion radius/line-of-sight, timing and throw skill, and **hard-reject** throws that would threaten friendly NPCs or the thrower. Grenades aren't infinite, should not give perfect target positions, and the NPC should consider stale evidence, not psychic player coordinates.
2. **Smoke grenades:** create genuine obscuration for threat LOS/aim and hide friendly advance, revive/heal, retreat or reposition. Must validate engine smoke effect; no merely decorative effect that leaves AI omniscient.
3. **Group suppression/covering fire:** coordinate faction allies via orders/temporary squad roles and target/sector evidence. One fires suppressively while another advances, with real ammunition costs, reload handling, safe firing sectors and friendly-fire limits. Player must be able to counter.
4. **Heal allies during active combat:** choose a living wounded ally, triage urgency and reachable protected route; assign a medic, check real medical supplies, move to wounded teammate, use healing with animation/timing; suppressing partners or smoke may secure procedure. Do not revive dead NPCs or give infinite healing.
5. **Full AI architecture diagram:** produce after stealth, tactical squad behavior, memory/comms, sensory model and all other core AI subsystems are appropriately structured; clearly distinguish implemented/tested and planned elements.

### Remaining previously reported blockers
- Unity Dev Build compile and runtime verification across all code changes; user currently cannot run it.
- Death/hitbox/ragdoll/despawn/replacement, navigation/corners/curbs/jump/crouch, sound stimuli, A* cover routes, long-range shots/reload, animations (melee/ranged/reload/flinch/death), dialogue/HUD/nameplate.
- Ambush against zombies as a deliberate stalk target, variable wait/attack timing based on observation, safe melee/ranged choice, full stealth disengagement, and optic magnification still not fully implemented.
- Adaptive knowledge/ML and in-world ally information passing on future phase; native kill XP credit and multiplayer replication not implemented.


## Ambush tactical choice round — 2026-10-13

New user request: proceed to next stage and explicitly add **faction and group system validation** to outstanding tasks.

Source changes:
- `d6aaaa0bcd65a3f0091d826d4d6d496dfb2ce7b4` BrainDriver adds a bounded `AmbushMethod` decision: for a freshly visual-confirmed enemy after stalking, usable gun with available rounds and >6m favors ranged; a target <=6m favors close assault; otherwise abandon ambush and let normal pursuit handle it. 0.45–0.85s readiness remains. Method expires in <=4s; gunfire/damage interrupts, and the pre-existing visibility gate still prevents firing at hidden memory targets. This does NOT guarantee stealth kill, synchronized execution or zombie-specific stalking.
- `ce1effe69cf3bf9b456fbf86b3dd68ef80a91255` ranged combat respects a time-limited close-assault choice instead of shooting immediately, while ordinary melee handler can operate when in actual validated hit range.

**Mandatory upcoming faction/group validation checklist** (source and in-game):
- Confirm faction-to-role mapping and hostility matrix (Raiders vs Traders/Survivors/Military; neutrality of Scavengers; Survivor/Military neutral), including role/faction changes, contracts, and AI target filters; avoid friendly fire across faction members.
- Confirm groups spawn/recruit up to max 4 within join radius, do not accidentally combine distinct factions, and promote surviving leader when leader dies.
- Confirm every NPC death removes its membership and group removal only when all members die; delayed population replacement rejoins surviving original group when valid, or forms new group otherwise; validate persistence across save/load and map changes.
- Validate group anchor, settlement/base ownership and rally return behavior; hostile/friendly group combat responses, faction relations after alliance, and NPC/zombie engagement priority.
- Check logs for group IDs, factions, leaders, corpses, replacements, and despawn. Do not assert tests passed until the Dev Build + Player.log confirms.
- Group tactics future: frag/smoke with actual friendly safety/obscuration; coordinated suppression/advance; medical triage/healing under cover, all still NOT implemented.

Other outstanding: build/compilation; damage/death/corpse/respawn; navigation (corners, stairs, jumping, A* reachability), player/zombie hearing, firefight/reload, dialogue/HUD/nameplate and available animation clips; zombie-specific stalking/ambush, optics, machine learning/social memory; native XP/kill credit, replication. User requests complete AI diagram only when ALL core modules structured, not at this interim stage.


## 2026-10-13 — Group contact communication foundation (resume here)

**User request:** Continue improving group/combat integration and ALWAYS keep this handoff current so a new agent/conversation can pick up without losing context. Do not generate the full AI architecture diagram until the major systems have been structured. Always report unresolved items at the end of each development round.

**Repository:** `pplopes2003-design/Unturned-Better`
**Working branch:** `feature/npc-ai-foundation`
**Source root:** `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/`
**Implementation status:** Commits pushed. Only source readback and basic brace balance performed. There has been **NO Unity C# compilation, development player build, automated in-engine test or gameplay validation** for this set of changes.

### Commits this round

- `778e3305eb9a2809a8e2ab50ca0ff6a13a8bf0f3` — `MobileNPCGroupMember.cs`: `BroadcastPlayerContactServer` relays a reported player sighting only among **living, initialized members of the same runtime group**, within 35 m and with a 4 s source cooldown. Each ally receives its own perturbed position (1.5–4.5 m directional error), not a Player Transform. Does not communicate across factions, across the map, or after group dissolution.
- `467b6bdd9d000e7bbe9c1930ddad0cc8e4093910` — `MobileNPCBrainDriver.cs`: `ReportGroupPlayerContactClue` treats verbal contacts as uncertain sensory clues, with 6 s per-listener cooldown and 12 s investigation timer; aborts when pursuing, following, or executing another tactical goal. It does not call `ReportVisibleTarget` or authorize weapon fire. Memory ownership and normal brain decision rules remain intact.
- `bb46f39d214c64e2e28ba111c9f89a430647141f` — `MobileNPCBrainDriver.cs`: report is initiated only following a **server-confirmed visual Player observation** by an NPC whose disposition can engage players. Source never broadcasts inferred hidden-player position.
- `af0a6af85d958bebeeabedc0421fbd186a4af282` — `MobileNPCTacticalCombatBehavior.cs`: reload cover response now checks current perception before using a target position; previously it queried a hidden target's live Transform, violating the memory/no-omniscience design.

### How this connects with existing NPC AI

`MobileNPCHumanoidPerception` validates target sight -> `MobileNPCBrainDriver.ReportVisibleTarget` -> per-NPC `MobileNPCPlayerKnowledge.ObserveSight` and `MobileNPCGroupMember.BroadcastPlayerContactServer` -> same-group eligible listeners `MobileNPCBrainDriver.ReportGroupPlayerContactClue` -> local uncertain sound-like memory and `MobileNPCBrain.ReportNoise` -> normal `MobileNPCHumanoidMotor` and locomotion policy choose navigation/stance. Listeners must independently acquire visual confirmation before attacking. This is **a basic verbal contact protocol**, not suppressive fire, real audible VO, synchronized flanking, shared perfect target knowledge or ML.

**IMPORTANT:** Unity behavior is untested. In particular test 2–4 NPC squad members, independent groups at overlapping distances, same/different factions, member death/replacement, player occlusion, target switch, ally investigating sound and not firing through walls, and unloaded map cleanup. Group persistence in `MobileNPCGroupMember` is runtime-only, while persistence adapters restore selected group metadata via snapshots; don't assume persistent runtime IDs across map loads.

### Current backlog — do not silently mark any item done

1. **Critical Unity validation**: first compile and test in latest dev build; collect `Player.log`; resolve CS errors, missing game APIs, runtime exceptions, physics/animation bugs.
2. **Death pipeline**: firearm/melee hits, health death, corpse/ragdoll, disable components, despawn, delayed replacement and faction/group cleanup.
3. **Navigation and defensive cover**: corners, doors, curbs, stairs, water, crouch standing up, short obstacle jump, A* reachability vs direct-segment checks; cover selection under repeated fire.
4. **Ranged combat**: hit validation, accuracy vs distance, reload, ammo, factions, friendly-fire behavior, avoiding X-ray pursuit and target priority.
5. **UI/presentation**: dialogue IMGUI keyboard/mouse focus, ESC/HUD, nameplates, actual NPC_Client native animation clips for strike/recoil/reload/flinch, ragdoll.
6. **Hearing/stealth**: player footsteps currently based on sampled planar displacement, not native footstep events; native zombie-alert radii from NPC movement are provisional; investigate environment sound, water, vehicles, actual stance and optics.
7. **Stalking and ambush**: concealed position selection from decayed sensory evidence and attack preparation exist as source prototypes; improve zombie-specific stalking, abort conditions, ambush timing, realistic player counterplay and actual reachability.
8. **Faction and group VALIDATION (explicit user request)**: verify faction relation matrix, hostile/friendly target selection and side effects of changing roles/alliances; correct group ID/faction/recruitment (max 4), leader replacement, dissolution only after all die, respawn joining surviving group, settlement anchors, save/load restoration, map transitions and lack of allied damage. New short-range sight reports need group test.
9. **Future squad combat**: FRAG when targets behind cover ONLY after blast radius, throw trajectory/inventory, ally safety and minimum self-risk evaluation; SMOKE as actual visual occlusion for advance/retreat/heal; coordinated suppression and flanking with finite ammo and safe fire sectors; tactical heal of living wounded allies using medical inventory and protected routes.
10. **Future adaptive/social knowledge**: per-NPC bounded memory, lifetime learning and optional propagation through actual contact/radio; unshared memory dies with NPC. No ML implemented.
11. **Longer-term platform features**: scoped/binocular observation, native kill/XP attribution, network replication, robust save/version migration.
12. **Final deliverable**: full AI decision architecture diagram ONLY once all major modules are structured; distinguish operational vs proposed vs untested functionality.

### Suggested next implementation step

After verifying no compilation blockers, implement genuine **squad role coordination** (a lookout reports a sector, a suppressor with a valid firearm/ammo maintains limited fire ONLY with appropriate line-of-fire safety, and a flanker advances to a verified safe waypoint). Avoid relying on hidden player's current transform. Separately evaluate group-aware ally-safe explosive eligibility, without spawning real grenades until inventory/projectile/throw APIs are verified.


## 2026-10-13 — Squad roles and future radio system (LATEST HANDOFF)

**User instructions:** Continue implementing real NPC AI in `feature/npc-ai-foundation`. Update this guide each development round, so other agents and later conversations can resume without guessing. Always report outstanding tasks at the end. A comprehensive AI architecture diagram is requested **only after all major modules are structured**.

**Repo:** `pplopes2003-design/Unturned-Better`; NPC source path `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/`.

### Changes committed this round

- `b470b61dd9357bd14481c708bd9db80c028b2538` — `MobileNPCBrainDriver.TryStartSquadAdvance(Vector3 approximateClue)`: accepts **only an uncertain verbal clue** when the local NPC is in investigation and has no higher-priority tactical destination. Tests four short lateral/forward ground candidates; requires safe ground, a direct corridor free of world obstacles, and environmental concealment from the last reported area. Orders a time-limited `MobileNPCTactics.Goal.Flank` when valid. This is short direct-path candidate validation, **not complete A* reachability**. If none passes, normal investigation continues; no hidden target Transform is read.
- `bcb4a0b883a4063a6ca3b5a78fe18bccc79ab36a` — `MobileNPCGroupMember` introduces temporary `SquadRole.Covering` / `SquadRole.Advancing`. A member with a currently usable gun and ammo can direct one nearby same-group member who received its uncertain local contact report. The advancing member waits ~1.25s for its listening phase, then attempts `TryStartSquadAdvance`. Role ends after 7s, and membership departure/death clears it. **The covering role is not yet suppressive gunfire**; it only assigns responsibilities while the normal weapon system handles actual visible target firing.
- `59eab4cb27c343177f6dc26a57ad55533d853fdb` — forbid a single Covering member from simultaneously assigning multiple advancing partners during repeated sighting reports.
- `312b131e34ae3c9c590e07334102fc3bde26d0cb` — created **`MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`**, the canonical future-radio design: device possession and power requirements, walkie-talkie, placed powered base radio, future vehicle radio, faction/group frequency limits, reinforcements, good loot, airdrops, unknown bases, TTL/confidence, counterplay and exact test checklist. **Radio NOT IMPLEMENTED**.

### System design decision: physical radios, not telepathy

Until implemented and verified, long-range messages MUST NOT work simply due to shared group/faction identity. Sender AND receiver need appropriate physical walkie-talkie, reachable powered fixed radio or a validated vehicle radio, plus channel/range/power/authentication. Fixed radio is an actual future construction objective in the base/settlement planner tied to generator/energy; vehicle radio depends on a separate future NPC vehicle module. Include equipment upkeep, transmission limits, faction filtering, loss-of-power, stale intel and message categories: help/reinforcement, loot, airdrop, previously unknown enemy base and actual target sightings. Do not invent IDs or item APIs. See `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md` for details.

### What is and is not working

- Source code contains observation-to-voice report chain, bounded uncertain memory, `Covering`/`Advancing` role choice, tentative close covered advance, and existing ranged/melee combat. **No new suppressive ammo/volume effect, AI grenade throw, smoke LOS effect, medical squad triage or radio transmission** has been implemented.
- Re-fetch and basic structural checks of modified source files are possible via GitHub, but **no Unity C# compile or Player Dev Build/in-game tests have been performed in these rounds**. Do not mark runtime behaviors as proven. No automatic game-map navigation/radio range test was run.
- A squad member advancing toward a reported location still requires original individual visual confirmation to fire. Memory report cannot reveal live hidden player coordinates.

### Remaining work (append and refine each round)

1. **Critical:** compile and run Unity Dev Build, collect Player.log and fix errors; movement, damage/death/ragdoll/corpse/despawn/respawn/loot after death.
2. **Navigation:** A* reachability beyond straight corridors, building corners/doors, terrain stairs, crouch standing, safe jump/landing, water, dynamic obstacles, real cover.
3. **Combat:** gun ownership/ammo, accuracy, range/reload, safety/friendly-fire checks, humanized reaction timing, zombie and hostile NPC engagement, combat acoustics.
4. **Faction/group validation:** hostility and alliances, role/faction changes, correct groups up to four, stable group IDs, dead leader handoff, survivor group lifespan, delayed replacement joining survivors, base ownership/persistence, map unload and rejoin, squad contact no unintended cross-group leak.
5. **Real squad suppression:** ensure finite ammo, line-of-fire, viable target area and player counterplay, safe fire angles from nearby allies; coordinating an advance is NOT yet actual suppressive fire.
6. **Frag grenades:** stock/loadout check, ballistic trajectory, explosion timing/radius and **hard ally safety**; only when utility beats alternatives.
7. **Smoke grenades:** actual opaque LOS/sight/sound effects integrated in AI; advancement/retreat/heal protection, valid item ownership.
8. **Medic in combat:** patient triage, inventory and route, protection and animation; no resurrection/infinite healing.
9. **Radio (new user requirement):** physical walkie-talkie, powered placed base radio in settlement builder and later vehicle radios; channel/faction, message TTL and consent, help/reinforcement/loot/airdrop/base intel. See canonical `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`.
10. **Vehicles (future):** agent driving/navigation, ownership/fuel/health and radio power, not implemented.
11. **Stealth/ambush:** real zombie-specific stalking, sound and stance correctness, target rediscovery without X-ray, optics/binoculars/scopes, cover-to-cover reachability.
12. **UI/presentation:** mouse/HUD/ESC dialogue focus, nameplate, native animations (melee/shoot/reload/hit), audio/ragdoll; native XP attribution and network replication still require work.
13. **Long-term learning/social intel:** bounded individual experience/skills and knowledge exchanged only via authentic physical conversation/radio; unshared NPC memory lost at death; sophisticated ML optional, not implemented.
14. **Diagram:** full NPC AI graph after all main modules structured, not prematurely.

**Next practical engineering work:** verify latest code compilation compatibility and test tactical group roles. Then implement conservative suppression with ammo and friendly-sector checks, but do not write a purely cosmetic suppression command; ensure it actually affects player pressure and squad movement only after target evidence.


## 2026-10-13 — Local speech realism + radio interception design (LATEST)

User asked to improve realism of **voice-only NPC group communications**, and investigate implementing **native Unturned radio with player eavesdropping** rather than inventing a separate radio simulator.

### Implemented source changes
- `031bd43ad99016bbc7e766d63f736071a5dee8a6` — `MobileNPCGroupMember.BroadcastPlayerContactServer`: replaced unrealistic 35m flat speech with a 24m **alert shout** or a 7m **quiet report when crouched**. Obstructions attenuate effective voice reach to 42%; each recipient's reported location error and confidence scale with how far the report travels. Skips actors' own humanoid collision shells during occlusion query. Communication still restricted to living same-group agents and cooldowns.
- `5b5cebe751fa55c8b018b10dfc39ad382505a550` — new `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCRadioMessage.cs`: immutable-style, data-only report record with message kind, coarse sector, timestamps, confidence, faction, group ID and call sign; supports simulated intercepted subtitles for enemy contact, reinforcement, loot, airdrop and unknown bases. **NO radio transmission or player UI hookup yet**.
- `f5e03d7002858fc4ebd614030e67be84c050701a` — updates `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md` with *verified public Unturned documentation* on walkie and earpiece, design for channel-gated player interception, and explicit correction against inventing portable battery consumption.

### Verified native radio facts (documentation, NOT SDK code integration)
- Vanilla Unturned Walkie Talkie **1445**, `ItemToolAsset` with `Useable: Walkie_Talkie`; user-selectable radio channels, broadcasts to compatible equipped receivers across the map.
- Earpiece **1446** allows *listening* to radio without transmitting.
- Public sources: https://docs.smartlydressedgames.com/en/stable/items/tool-asset.html ; https://restoremonarchy.com/browser/vanilla/item/tool/1445 ; https://unturned.gameinfo.io/items/1446-earpiece ; https://unturned.fandom.com/wiki/Communication .
- Not yet found or fetched the exact native Unity code/class hooks to read the player's tuned channel and equipped radio/earpiece in this repository. Do not claim native radio interception works until that integration is found, coded, compiled and tested.
- Proposed player UX: if tuned correctly and possessing an actual receiver, show discreet **simulated radio dialogue subtitles** (speaker callsign + coarse region + line of speech) and optional native beep/static effects. Wrong channel or no receiver => absolutely no intercepted text. Do not leak precise player positions. No microphone voice acting is required at this stage.
- Fixed base radio is future construction objective with real electrical requirements (power source and fuel only if the native asset supports it). Vehicle radio follows later vehicle NPC support. Do not invent portable radio battery drain or pretend fixed radio is already a working base asset.

### Validation and outstanding
- Read back `MobileNPCGroupMember.cs` and `MobileNPCRadioMessage.cs` from GitHub; confirmed expected symbols and balanced braces. **No Unity C# compile, build or runtime test** performed.
- Test actual audio vs assumed radii, whisper/crouch legibility, walls/doors and indoor occlusion, allied hearing.
- Future: verify native player voice frequency and item/equipment access, build real receiver gating and text HUD only after that, add power-gated base station and reinforcement/loot/airdrop/base report generation from actual game observations.
- Continue previous pending: squad covering-fire with real ammo, group/faction validation, grenades Frag and Smoke with ally safety, healing during combat, AI vehicle usage, death/ragdoll/respawn, navigation/A*, animation and dialogue bugs, NPC zombie stealth/optics and long-term individual memory. Full AI diagram only when the major systems are structured.

**IMPORTANT resume order:** verify Unity build, inspect native walkie/earpiece channel reception, then integrate physical radio messages and player interception. Keep HANDOFF and RADIO_SYSTEM_DESIGN up to date each round.


## 2026-10-13 — Native-radio gating foundation + zombie pursuit outside POIs (LATEST HANDOFF)

**User request:** Continue radio integration in native Unturned terms and ADD zombie pursuit outside regions/POIs to persistent backlog. Update this guide every round so agents/new conversations can resume the exact current status. Full NPC AI diagram only after all major systems are structured.

### Commits / files

- `617f5df428cf5301af7607b3768c4fba3ee9870f`: new `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCRadioOutbox.cs`. Server-only bounded report queue, requiring NPC inventory ownership of vanilla Walkie Talkie item **1445**; one report / 20s, max 4 pending, 24s TTL, needs a valid group and living hostile speaker. **No transmission.**
- `6db6f786984f02169bb00133d0c160de019a2f05`: `MobileNPCPopulationBootstrap.cs` installs new outbox on mobile NPC runtime template.
- `f0361b95518195e331faacd12f6c3d15e0032d27`: `MobileNPCBrainDriver.cs` queues a report solely after confirmed visual acquisition of a player. No invisible player tracking, simulated chat or server-to-client broadcast.
- `2366c5c60b02c59e3e9a7ef4c4ed8b73c89718b1`: `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md` records the implementation boundary and a new zombie pursuit beyond-POIs engineering/test matrix.

### Honest status of radio

Native vanilla Walkie Talkie 1445 uses player-equipment and channel-dependent voice. Earpiece 1446 is receive-only. Sources (research, NOT tested against current Unity DLLs): https://restoremonarchy.com/browser/vanilla/item/tool/1445 and https://unturned.fandom.com/wiki/Walkie_Talkie . Could NOT fetch native `PlayerVoice.cs`, `PlayerEquipment.cs` or `UseableWalkieTalkie.cs` from this branch at their assumed paths. Do **not** fabricate class names/methods or infer player's tuned frequency from item ownership. The current outbox simply prepares text intelligence; **no player espionage, chat subtitle, frequency matching, NPC voice, portable equip detection, powered base radio, vehicle radio or reinforcement command are operational**. Next step: examine actual game's managed C# APIs/Unity project files or code once available, implement checked native receiver and transmitter bridge, test actual player equipment and channel before delivering `MobileNPCRadioMessage.FormatInterceptedSubtitle`.

### New critical zombie behavior backlog: threat outside POIs

User explicitly requests verifying zombie pursuit beyond native regions and limits so zombies remain dangerous outside points of interest. Investigate native region streaming/leash, persistent target while leaving POI, active/inactive zombie navigation and damage, synchronization, zombie noise response, return to spawn and home, offscreen optimization and multiplayer. Need a controlled chase that crosses regions without teleportation, immortal trackers or global swarms; measure CPU cost and memory with multiple zombies. Map test sequence: chase near POI boundary, cross several map regions, lose sight, generate firearm/footstep noise, drive a vehicle, meet NPC factions, check save/load/map transitions. No native `Zombie.cs`/`ZombieManager.cs` sources were found at the attempted standard paths in current branch; must inspect actual project assemblies before editing. This item stays **PENDING, not implemented**.

### Complete remaining backlog (keep on future rounds)

1. First **Unity compile and real Dev Build** plus Player.log; no Unity compilation/gameplay tests have verified recent GitHub work.
2. Death, damage, ragdolls, corpse cleanup, delayed replacement and XP/loot; navigation around corners/stairs, jump/crouch, safe A* cover.
3. **Zombie pursuit outside POIs and across regions**, memory/search and hearing; native zombie AI investigation.
4. **Faction/group validation**: relationships, alliances, 4-member limit, group leader/death/respawn, separation, persistence, base anchors, no friendly fire.
5. NPC individual behavior: stealth, stalking, ambush vs players/zombies, scope/binocular perception, last-known target without cheating.
6. Combat at distance, ammo/reload accuracy, ally safety; *actual suppression* with finite bullets while ally advances; grenades Frag/Smoke safety and effect; medical teammates under combat.
7. Communication: voice realistic ranges/occlusion done in source, group contact & roles prototypes; native **radio** full transport, frequency/equip/receiver checks and radio interception/subtitle UX; powered fixed base radios/energy construction; vehicle radios and NPC driving future.
8. Dialogue HUD/cursor/ESC and nameplate, native animation clips for melee/fire/reload/hurt and death.
9. Future adaptive NPC knowledge sharing and learning with bounded experience/lifetime; unshared memory lost on death. World reports (airdrop, loot, base, reinforcements) only when observed, reported and verified.
10. Final requested full architecture/behavior decision-tree **diagram ONLY after major systems are structured**, reflecting verified vs untested vs planned behavior.

**Next recommended work:** verify actual native Unturned equipment/channel APIs and runtime zombie region logic from Unity assemblies/sources, not fabricated reflection guesses. Then implement working radio frequency-gated player interception and accurate regional zombie pursuit. Follow up with on-screen subtitles only when physical receiver + channel validated.


## 2026-10-13 — New conversation continuation: pickup failure diagnostics

- Recovered this handoff from `feature/npc-ai-foundation` and reviewed the latest radio/zombie-pursuit backlog. Do not confuse the repository default branch `main` with this work branch.
- Commit `7caa02acd51eab9fe5d12c17f05956bd3516d711`: `MobileNPCLooting.cs` now snapshots the attempted `targetInstanceID` before `ClearTarget()` resets it, so a failed pickup logs the actual attempted item instance rather than zero.
- Status: committed via GitHub; NOT compiled, NOT Dev-Build tested, NOT runtime-verified. This change improves diagnosis only; it does not itself establish a fix for navigation, pickup transfer, or stalls.
- Next: validate Unity compile and identify real native radio/equipment/voice channel hooks before claiming radio interception. Retain zombie cross-region/POI pursuit as unimplemented critical backlog. Preserve the full earlier backlog and only advance status after gameplay evidence.


## 2026-10-13 — Authoritative injury-hit result consistency

- Source inspection: `MobileNPCInjurySystem.ApplyHit` applied injury effects and called `vitals.ApplyDamage(damage)` but previously returned `true` even if the authoritative health component rejected the damage. This could falsely acknowledge an accepted hit to a caller, especially if health/death status changes through synchronous callbacks.
- Commit `2f94246bd06a2226eef5fa9e2f5100a2995b59b8`: propagate the actual result returned by `MobileNPCHumanoidVitals.ApplyDamage` rather than returning unconditional success. This is a narrow consistency fix, NOT a repair for missing gun-collider hit resolution or full native death/XP lifecycle.
- Caveat: `ApplyHit` still performs injury effects before the final health call; a broader atomicity review is warranted if callback-driven reentrancy is observed. Avoid duplicating damage by invoking both this method and the vitals adapter for one hit.
- Status: GitHub commit exists. No Unity compile, no player build, no runtime test. Recheck firearm and melee adapters, hit-region mapping, death transition and hit animation with logs.
- Retain prior backlog unchanged, notably zombie pursuit across POI/region boundaries, native frequency-checked radio interception, navigation, group/faction persistence, and the final diagram only after major modules are structured.


## 2026-10-13 — PERMANENT BACKLOG + first actually connected per-NPC online learning (latest handoff)

**Mandatory continuation order:** FIRST read `MOBILE_NPC_PENDING_TASKS.md` (permanent canonical task register, commit `a2e709930a1f5189f3f92489d78fa71f42f1016d`) and THIS HANDOFF. Read `MOBILE_NPC_ADAPTIVE_LEARNING_DESIGN.md` for learning internals and `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md` for communications. Always update backlog AND handoff after each engineering round; update test/design docs when relevant. **Do not delete backlog IDs when source code is written; close only after Unity compilation and real Dev Build validation, keep an archived record of completed tasks.** User will compile and test once the implementation phase ends — do NOT demand a build after each iteration. The complete NPC AI decision-tree/architecture diagram is due at the END after major subsystems are structured, not now.

### Source commits in this round

- `1a41e00319464076e1b8c400052bc5dbbf902313` — new `MobileNPCAdaptiveMemory.cs`: bounded (48 records), individual, spatially contextual online success/failure evidence, softened evidence from an actual conversation; no omniscient shared state or neural ML.
- `564ebfc1a15814ac4dc0f03ff8f3096ef0acd1d1` — attach personal memory to runtime NPC template.
- `a94ca7ad2ac7567d8b2d1039c282e48453117f44` — forget private experience upon death before corpse retirement; no successor inheritance.
- `f6b4583f5f689dcf05ab63486f56455384f15784` — resolved loot success/failure and expired loot-goal outcome teach per-NPC loot preference, capped region bias. No invented pathfinding.
- `a657faad1f1a08169d3a7c53722e9ce7a9edeaff` — committed NPC shots report physical selected-target contact vs missed shot, and learned context controls a small shot cadence multiplier; still realistic weapon dispersion, finite ammo, actual visibility.
- `a576d9209eee7144482f666bebcf0fab45d78834` and `f2eac633a46d074736e62aa08a2b1a61d6c7e9e9` — learning is shared only during simulated distance-limited and occlusion-attenuated voice conversation between living same-group agents, not a global registry broadcast. Source-only; no player subtitles/audio.
- `18297285d414c64d1957a0dcaac75658fabda174` — version 3 `MobileNPCPersistence` individual experience snapshots; backward-compatible read of v1/v2 records (no new experience).
- `c1efbe55d85f2e0600f270dab00d5bfee2a6a0e8` — new `MOBILE_NPC_ADAPTIVE_LEARNING_DESIGN.md` with limitations and tests.
- `2011a6527c84d50dec67b6ba7f5d6b6b85c4f3ea` — update backlog statuses, retain all IDs.

### Important implementation boundaries

This is the **first connected real feedback slice**, not completed long-term AI learning. Training is simple bounded reinforcement from loot and target hit/miss, affecting small adjustments in subsequent decisions. No adaptive selection yet for cover/retreat/flank, zombie subtype weaknesses, learned routes, embodied audio dialogues, ML models, or radio knowledge transmission. Advice shared by speech is weaker and not forwarded; a speaker's pending lesson may be overwritten by newer events. All actual combat authorization continues to use native/adapter hits, ammo, factions and sighting constraints. Body armor/native XP cannot be inferred from the physical-hit result. The per-individual save still lacks a permanent GUID and exact identity; future extension required.

**Verification:** Re-fetched source via GitHub, checked braces and symbol wiring. **NO Unity C# compile, NO Player Dev Build and NO in-game regression verification.** Do not label completed, particularly AI behavior, corpse/death and cross-POI zombie pursuit. User specifically plans to compile/test at the end.

### Next implementation tasks in preferred order

1. Extend learning into outcome-aware actual defensive tactics (cover success/failure, retreat, flank vs target category), and bounded route memory. Validate against real reachable cover/safe path and no hidden target information.
2. Stable individual save identity and robust pending/spoken knowledge data lifecycle; sample budgets, confidence and stale observation loss.
3. Return to urgent native zombie chase across POI/region borders, gun/melee/ragdoll integration, navigation around structures, group/faction validation, and native radio receiver/channel bridge.
4. Add all relevant tests to `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` and continue tracking EVERY outstanding requirement in `MOBILE_NPC_PENDING_TASKS.md`.



### Addendum — 2026-10-13: learning cover access

- `1f09857381d10bb1c65ee6eaf756d97f3a6c2d50`: `MobileNPCAdaptiveMemory` now has `CoverAccess` context (saved in per-individual v3 snapshot).
- `caf314160cef9e46a32e2e7bc5b3303ad1964b47`: `MobileNPCBrainDriver` records cover waypoint arrival vs. expired cover goal as outcome. Cover candidate testing still checks map geometry and short approach corridor; repeated learned failures can postpone a candidate, but first geometry-safe fallback remains when no others work.
- Source/commit only; NOT compiled, NOT run in-game. This measures waypoint access, **not** cover's effectiveness against bullets or full A* route correctness.
- Canonical backlog `MOBILE_NPC_PENDING_TASKS.md` LEARN-05 remains active. User will perform compiled Dev Build and tests at end. Continue adding outcome-aware tactical learning, then physical radios, zombie cross-region pursuit, nav, interactions, etc., and update this handoff and backlog every round.


## 2026-10-13 — PRIORITY P0: zero-damage safety + meaningful navigation progress + exploratory cover policy

Always read `MOBILE_NPC_PENDING_TASKS.md` FIRST; never delete tasks until compiled and runtime-verified. The user defers compiling/build testing to the end. Continue committing meaningful source slices and ALWAYS update the permanent backlog + this handoff each round; update related test/design docs. Full AI diagram is requested only after main modules are structured.

### Actual commits and source changes
- `b4ab966c39a43c20c7c2f51f2c15d49556aca45d` — `Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs`: the custom NPC branch now only applies **finite positive** computed weapon damage. Previously `Mathf.Max(1f, mobileNPCDamage)` dealt 1 HP even for a zero multiplier.
- `6d673383ae00055c84a25cc49cc4f4e12fe530e4` — `UseableMelee.cs`: same fix for player melee vs. mobile NPC.
- `96eab81bc9c0bf6aa870ec99fba80cea2d4e7aea` — `MobileNPCHumanoidMotor.cs`: locomotion `IsMoving` now follows actual planar position delta instead of treating any side contact as no movement; stuck recovery checks net progress toward a still-valid goal or substantial non-diverging sidestep, while resetting when goal changes. The old threshold accepted any >0.12m motion (including useless side-to-side oscillation) as recovery.
- `e0d5d944c2c11b684c805e2814b8070cf0f9b3cf` — `MobileNPCBrainDriver.cs`: bounded contextual **epsilon-greedy** (12% exploration) chooses among up to 10 genuinely ground/LOS/approach-validated cover candidates. Uses existing `MobileNPCAdaptiveMemory.GetCoverAccessAdjustment` (personal previous arrival/time-out feedback). An exploratory candidate is sampled without extra List allocations. This is not a substitute for navigation, target detection, ammo or combat authorization.

### Algorithm decision for user question
No external ML runtime/library is required. Start with auditable, tiny online contextual bandit implemented in C#: exploit learned evidence most of the time, explore a viable alternative occasionally, update from **completed outcomes** and save only living individual experiences. `GetShotCadenceMultiplier` is bounded evidence-smoothed pacing (not Q-learning), loot/cover policy makes small context adjustments. Future higher-level tactics may use UCB or Thompson sampling after real reward signals exist; do not describe neural network training as implemented.

### Current limits / next priorities
- All changes are **source committed**, not Unity-compiled, not Dev Build-tested, not runtime-verified. Client hitmarker may still be inaccurate when weapon damage is zero, and hit region/headshot mapping, proper native death/XP/loot, ragdoll physics and zombie pursuit outside POIs remain unverified.
- Stuck detection is not full pathfinding; moving through doors/around buildings still depends on native A* and local route fallback. A moving player target can shift its goal; future tests must check false-positive stuck recoveries.
- Contextual epsilon-greedy cover selection requires a valid sensory threat and short unobstructed local approach; higher quality A* reachability and measured bullet protection are future tasks. Sample 10 candidates max to bound physics work.
- Continue CORE-02/03/04 and ZMB-01 next, then faction coordination, real radio channel/equipment and full adaptive tactic/route memory. Add tests without asking for Unity compilation each round. Preserve all backlog IDs.


## 2026-10-13 — FOLLOW-UP IMPLEMENTATION: lethal transition reservation and group-compatible replacement

**Read** `MOBILE_NPC_PENDING_TASKS.md` plus this handoff BEFORE changes; update both docs in every round. Keep the user-requested final AI diagram deferred until the main modules are structured. User compiles/tests only after the implementation stage, so do not ask for a Unity build every round.

### Commits and source-level findings

1. `270072bc821d3643897816322b0f409eb774f1b6` (`MobileNPCHumanoidVitals.cs`): a lethal hit now reserves `lethalTransitionReserved` immediately after health decreases to zero, *before* publishing synchronous `DamageReceived` or `HealthChanged`. Other damage/healing requests arriving while death is reserved are refused; native zombie/player death APIs are NOT faked. This prevents a nested callback from mutating the fatal hit and attribution before `DieServer` marks the NPC dead. The authoritative `Died` event and corpse presentation flow still need Dev Build verification. Do not claim that this solves failure to register a gun/melee raycast.
2. `466829ece0b4b129ca3baddf470ef45845b5b38a` (`MobileNPCGroupMember.cs`): add server-only, living-survivor-and-capacity-gated read of a group's actual role and faction. A dissolved or full (four members) group is not a valid target for a replacement.
3. `946d2b62cc2a8fd54d9f5d0f757b01741827075f` and `cd01dac2cdf5157dd83063333387ef9831f32d55` (`MobileNPCPopulationDirector.cs`): due delayed replacement first checks whether predecessor's group survives, spawns a **new individual** with compatible role and sets matching faction before attempting `TryJoinExistingGroupServer`; otherwise ordinary independent population role applies. Pending replacement removed only after successful spawn; fix default initialization for C# definite-assignment safety. No shared dead-person memory, health, inventory, identity or guaranteed same location. Spawn and group join remain separate operations and the join itself revalidates group membership.
4. `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` updated with tests for reentrant lethal callbacks, same-faction rejoin, group dissolution, capacity and retry after spawn failure.

### Explicit state / remaining work

- **GitHub commit and static inspection only. NOT Unity-compiled, NOT Dev-Build runtime-tested.** This is not a certified death/ragdoll/respawn fix.
- Native firearm/melee actual collider hit detection and health loss, visual hit feedback, full ragdoll behavior, attribution/loot/XP, zombie cross-POI/region pursuit, navigation, and many other P0/P1 items remain open.
- Replace/extend current NPC death with a native-compatible, tested lifecycle only after source trace and game tests. Do not attach fake `Player`/`Zombie` APIs.
- When moving into next P0 slice, investigate native zombie streaming/pursuit boundary and faction combat under real map constraints; `Zombie.cs`/`ZombieManager.cs` standard repository paths have not been located in this project and must not be guessed.
- After work, update the permanent backlog and handoff each time; move a task to completed ONLY with Unity compile and real runtime evidence.


## 2026-10-13 — LATEST HANDOFF: learned supply sources (Cobra example), and cross-POI zombie gate

**Mandatory:** read this handoff plus `MOBILE_NPC_PENDING_TASKS.md` first each new conversation; update BOTH after every round. New statuses: a narrow fully wired source feature can be `QUASE PRONTA — AGUARDANDO BUILD`, without representing Unity compilation/gameplay success. After the user's final Dev Build feedback, close only if truly working; otherwise return to `EM IMPLEMENTAÇÃO` with test logs. User will compile/build at END; do not repeatedly request builds. Full requested NPC AI architecture diagram is due only after the major systems are structured.

### New source commits — verified via GitHub

- `3581bc40eac89d3f0bd779a83402fc430fa7fea2` — NEW `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCSupplyKnowledge.cs`: individual bounded memory of up to 32 actual **magazine** pickup locations, successes/failed visits, reliability-weighted site selection for currently owned gun. Compatibility uses native `ItemGunAsset.magazineCalibers` and `ItemMagazineAsset.CalibersContainAnyOfIds`; no invented Cobra ID, caliber or spawn points. One witnessed pickup is a hypothesis, not guaranteed respawning loot.
- `f17d4077cc2c929da7490d2d79466003f890d606` — attach per-individual knowledge to `MobileNPCPopulationBootstrap`.
- `316e4075e0639d386baaf015421fdf728d0ba8e1` — wipe supply location memory on `MobileNPCHumanoidVitals.DieServer`, preserving only already told lessons in living recipients.
- `a19329014e627c65980d51edfffa476452f9f3a6` — `MobileNPCPersistence` new version **v4**; serializes/deserializes up to 32 supply records per living individual, old v1/v2/v3 accepted (missing field = empty sites).
- `292a8cf7feca709c26701c6d488642cffcda9ee9`, `8d19ddddc00d14553b295dc1361b364f8f9f921e`, `2bf1b3fd4906a5d1bf26246ca17f6ec7865c42ac`: bounded `Investigate` tactical destination for remembering supply spot, only when ordinary decision brain idle and no combat priority.
- `b176e9f1dd83af4c88b43ee6efbf368e442dc835`, `d510116264787603b62dc2d4a0ea3134e7156d35`, `73fbe1ce46792378a9e19179641afa22ba50174b`: `MobileNPCLooting` prefers **genuinely compatible magazines** when low ammo; successful world transfer records specific magazine item ID, amount and pickup position; a no-spare low-ammo NPC can follow grounded remembered site within 240m, scan real loot, decrease confidence after unproductive visits, cancel safely on combat/no inventory, with 40s planning cooldown. Magazine with **zero rounds** is NOT treated as evidence of ammunition source.
- `e2b6f099c7c840f581f27c99af33e9471f9b6837`: `MobileNPCGroupMember` can pass an observation to an actually audible same-group ally (distance/wall checks already existing); weaker indirect evidence is retained by the survivor even if source dies. No radio transport, AI speech audio or subtitles yet.
- `07970a63f52f1ff1c855a7d60c630d27af112c66`: fix `MobileNPCLooting` still logging 0 world item ID after `ClearTarget`; now uses captured `attemptedInstanceID`. Previously reported fix had missed the actual log expression.
- `66caf131d595011b582571252376b0a78bc49a4e`: `Zombies/Zombie.cs` pursuer no longer calls `leave(true)` merely because the pursued player has `player.movement.nav == 255` (outside native navigation), preserving unreachable-swimming escape. Native code confirmed under `Assets/Runtime/Assembly-CSharp/Unturned/Zombies/Zombie.cs`, `Managers/ZombieManager.cs`, `Managers/ZombieRegion.cs`. Real cross-bound A* viability, desync, navigation graph extension and performance remain open.

### Critical distinction: what the Cobra example does and does not do

**Now wired as source:** if NPC owns a native gun (e.g. Cobra) and physically picks up a magazine with rounds, it can learn the *actual pickup location*. If gun state has <=8 rounds and no compatible spare is owned, it looks through personally observed magazine sites, filters by NATIVE magazine caliber compatibility with gun, ranks confidence/distance and attempts a physically grounded destination; upon arrival it still needs a real item. Companions only acquire a lower-confidence site through validated nearby conversation; knowledge dies with its owner if unsaid.

**NOT implemented:** semantic categories of police stations / gun shops / hospitals, global unseen loot databases, crafting loose rounds into magazines, ammo from thin air, learning a hidden item's exact spawn, learning magazine/caliber ID by empirical weapon tests rather than reading native weapon compatibility data, or learning navigation across missing A* graphs. Track this as LEARN-07.

### New status discipline and next work

`LEARN-06` scoped v1 is **QUASE PRONTA — AGUARDANDO BUILD**. This means source-wired, pending final build feedback, NOT tested or closed. `LEARN-07` is NOT INVESTIGATED. `ZMB-01` is IN IMPLEMENTATION; removing the nav=255 cutoff is not proof of full cross-POI pursuit. More CORE-02/03/04 and UI/radio also remain open.

Next recommended P0 work: inspect `Zombie.tick`/native A* route failure and how global zombie ticking handles targets outside loaded navigation mesh; investigate safe bounded fallback with no teleport, no bogus target positions; then re-check mobile NPC hitboxes, death and pathing. Update `MOBILE_NPC_PENDING_TASKS.md` and this handoff after each round, preserving IDs and all tests.


### Final static audit addendum — 2026-10-13

Fetched again the modified NPC looting/brain/tactics/supply knowledge/persistence/group and native zombie source. Braces balanced and key integration symbols found; this is **source inspection only**, NOT C# compile or Unity Dev Build testing. Final source cleanup commit `a6e73c82c0300f017fa1226cf4c7b637f8925dc6` normalizes reset of captured loot ID/rounds. Both the permanent backlog and this handoff have been updated. Resume next session with ZMB-01/CORE-04 proper A* chase feasibility beyond navigation regions, and afterwards LEARN-07 semantic POI-type generalization using ACTUAL place/loot metadata. Preserve LEARN-06's source-near-ready status and defer closing until the user's final build feedback.


## 2026-10-13 — SOURCE IMPLEMENTATION: user clarification — learned sources apply to all useful items (LATEST)

**Immediately re-read the canonical backlog `MOBILE_NPC_PENDING_TASKS.md` and this handoff in every continuation.** User stated the Cobra magazine example was JUST ONE USE CASE; NPCs must remember where they physically obtained whatever resources their actual survival, treatment, defense, equipment, crafting and settlement plans need. Status `QUASE PRONTA — AGUARDANDO BUILD` applies only to bounded source-wired slices; build feedback will later close/reopen them. User performs all Unity compiling/Dev Build playtests at the end. Keep updating backlog + handoff in EVERY code round. Final full NPC AI diagram only after major modules.

### Actual commits delivered

1. `e2c59e040bb45a06827a9c314eaa029c783ac20e`: `MobileNPCSupplyKnowledge` generalized from magazine-only to **all native ItemAsset IDs with positive acquired amount**, storing observations per NPC in max 32 sites. `SiteRecord.itemID` is new; `magazineID` retained as legacy v4 read fallback. Learns evidence only after server-authoritative world transfer, never from unloaded POIs. Demand-based recommendations check injuries/health (including correct dressing or splint), hunger, thirst, real gun-magazine caliber compatibility, basic absent equipment, simple construction supplies. Per-item place accuracy, confidence and physical distance matter. Live conversations transmit weaker secondhand reports to audible same-group listeners; unspoken memory erased on death. No new radio implementation.
2. `8c983a377c447985ad31c9382f872501f60f3bd8`: `MobileNPCLooting` now consults `TrySuggestNeededSupplySiteServer`, not a magazine-only planner. **Important functional fix:** once within 6m of a remembered site, completes the `Investigate` goal, so `TryStartLoot` can actually acquire any nearby real world drop rather than being rejected by another outstanding tactical goal. Empty visits count as negative feedback; fighting interrupts the excursion and the world never guarantees a pickup.
3. `ff2cb872be6e1ebc6f8128a6f071dbb73f1c6417`: `MobileNPCInventory` now has `TryCraftNativeWoundDressingServer`, which scans actual vanilla `ItemAsset.blueprints`. Crafts only configured rag/bandage/dressing IDs (393/95/394) through no-special-requirement simple recipes with truly owned materials; tries direct one-step or at most a verified two-step garment→intermediate→wound dressing chain. No infinite bandages, no arbitrary clothes->rag transformation, no fabricated recipe. Two-stage failures may leave the legitimate intermediate instead of a dressing; only runtime asset audit can determine which vanilla recipes qualify. `8ef9fd5159202abb6a36651b47f499b09479ba29` caps expensive blueprint scans to once / 18s per injured NPC, `090cee160b508c49922c0d85b2ec0f9bcb5a62b2` accounts for reserved inventory slots.
4. `ff07f4ace26c6fd1c42a02e6a3aeff0d0fcbf308`: `MobileNPCMedicalBehavior` attempts valid native wound-dressing crafting if actively bleeding and lacking a bandage. `9717fba040380ee531f690e07fc2d674dd09f607`: `MobileNPCMedicalUse` defers generic health item use while bleeding, preserving wound care. `77e60007bf3abff86cbe0340ae1845f82a0935a2` prioritizes the specific right treatment when choosing learned supply sites (rag/bandage/dressing for bleeding, splint for fracture, general med only for HP). `b844018221e20177700eb1935401205f6ac81af4` limits long bleeding trips and allows remembered clothing as *potential* native crafting ingredient without inventing medicine.
5. `e5f3b2f920d40c261aff796e72e9e63710cee517`: `MobileNPCPersistence` now **v5**, accepts prior v1–v4, supports `itemID` with old `magazineID` field as data migration. Existing NPC identity is still incomplete: do NOT claim end-to-end guaranteed same individual restore without Dev Build.

### Accurate completion boundaries and critical next work

- **LEARN-08** newly tracked and `QUASE PRONTA — AGUARDANDO BUILD` only for the real item categories whose needs are currently modeled, NOT “all possible custom items” and NOT category-level generalization. The example “found medical supplies at a hospital, remember to return while hurt” is now wired per actual **observed position**, without magically knowing which locations are hospitals.
- **LEARN-07** still requires verified actual POI/location CATEGORY mapping; learning “hospitals generally have medical supplies” needs multiple visits and contextual world metadata, not hardcoded name-based guessing.
- **CRAFT-01** remains `EM IMPLEMENTAÇÃO` until real recipes are inspected/compiled/tested, with actual resource consumption, no dupe, no injury ignoring. In vanilla, not every garment yields a rag via one-step and some blueprint paths may be disallowed by the strict safe-only filter; do NOT fabricate successful crafting where source recipes are missing.
- Initial sourcing and memory remain constrained by inventory capacity, native terrain/path, priorities, group/faction privacy and performance. Improvements still needed: native POI metadata, goal conflict avoidance, urgent treatment close to battlefield, source type for vehicles/fuel/repairs, tactical inventory disposal, complex recipes and multi-step crafting transaction rollback, death and save races.
- Prior high-priority zombie pursuit `ZMB-01` is STILL OPEN: `66caf131` removed a hard-coded nav=255 bailout but pathfinding in non-POI terrain remains unverified. Continue native `Zombies/Zombie.cs` and `Managers/ZombieManager.cs` tracking without assuming cross-bound routes.
- Final QA: this round consisted of source edits/commits plus static source inspection only. NO C# compilation, Unity test, Player.log or in-game functional validation. Do not mark fully resolved.

**Next session priority:** finish generalized supply physical search, inventory/medical craft semantics and native POI evidence; preserve all backlog IDs. Then work P0 native A* zombie pursuit across navigation bounds and combat/death/navigation regressions; radio native channel/equipment interception remains blocked. Update handoff and pending tasks after every further code round.


## 2026-10-13 — LATEST P0 ZMB-01 ENGINEERING: chasing across empty zombie regions

Always read `MOBILE_NPC_PENDING_TASKS.md` and this handoff before each change and update BOTH every round. User will compile/test in Unity at END. "Quase pronta — aguardando build" may apply only to source-complete scoped slices; never close a task without user's Dev Build feedback. Diagram of total NPC AI deferred until major modules implemented.

### Verified source / commits

- `067bc2f99da31175b80d27ef76e623def031ec8c` in `Assets/Runtime/Assembly-CSharp/Unturned/Managers/ZombieManager.cs`: native `tickingZombies` is the global per-frame (dedicated max 50) active hunting list, independent of region. The separate `Zombie.OnUpdate` historically ran only for zombies whose **spawn/home region** has any players, so active chasers crossing to an unoccupied origin region could cease movement snapshots and stuck detection. Invoke `OnUpdate` immediately after an active `tick` only when its origin `PlayerCountInRegion == 0`; do not double-run on regions updated by the normal occupied-region loop. No scan of every zombie in empty regions. Compatible with the earlier `66caf131d595011b582571252376b0a78bc49a4e` removal of `player.movement.nav == 255` chase bailout.
- `c23e6addf9f5a144e6ec84f65a24d1686781114c` in `Assets/Runtime/Assembly-CSharp/Unturned/Zombies/Zombie.cs`: `TryStepPursuitBeyondNavigationOnOpenGround(delta)` is invoked before native `seeker.Move(delta)`. Only attempts **short, physically possible open-ground motion** when pursued player/mobile NPC target is outside `LevelNavigation.checkNavigation`, within 1.9–24m of zombie, alive, collision-free chest ray, CharacterController grounded, a stable raycasted next footstep (<=38° slope, <=0.65m height change, not underwater) and currently allowed to move. Uses capped per-tick delta and `CharacterController.Move`, native collisions and direct visible orientation. All other paths still defer to `seeker.Move`; never teleports, changes ownership, warps over obstacles or suppresses combat. The fallback is a safety-constrained short-range pursuit, NOT an all-maps navmesh replacement.
- Canonical `MOBILE_NPC_PENDING_TASKS.md` update: commit `e1adf539e624dc3490089a82bde0d92ccf525b63`, `ZMB-01` remains `EM IMPLEMENTAÇÃO`. No tasks removed.

### External dependency finding / boundaries

Official Unturned U3 SDK FAQ `https://docs.smartlydressedgames.com/en/latest/u3-sdk/faq.html` states that licensed **A* Pathfinding Project** integration code has been withheld from the SDK. The built-in interface `IUnturnedPathfindingMovementComponentInterface` and `UnturnedPathfinding.Get().CreateMovementComponentForZombie` exist as game abstraction, but their runtime pathfinder capability must not be assumed to provide long-distance cross-region connectivity in a published map or dev build. A legal and performant solution for routes through doors, bridges, complex buildings, navmesh gaps or long distances remains future work; preserve P0 `ZMB-01/CORE-04`.

**Not tested/compiled:** no Unity/Player.log access in this round. Examine Unity collision ground layers, `RayMasks.BLOCK_COLLISION`, ragdoll/path component movement coexistence and duplicate snapshots. Dedicated networking still uses `GatherRemoteClientConnections(zombie.bound)` rather than the zombie's dynamic position: players in regions other than its origin may not receive full spawn/state; `NET-01` and `ZMB-01` remain OPEN. Audio/noise and sighting behavior remain pending gameplay verification.

### Next continuation

1. Inspect the native pathfinder abstraction and determine what is actually installed in this project's Unity packages; design legal A*/NavMesh fallback for non-POI pathing without overclaiming. No automatic teleport or obstacle bypass.
2. Fix and document **dynamic network interest** for zombies leaving original region as a separate network-safe task, or explicitly keep as blocked if registry IDs and client spawn contract prevent it.
3. Resume P0 `CORE-02/03/04` mobile NPC hitboxes/physical death/navigation; then P1 radio receiver transmission/intercept, AI goals and remembered source-category generalization. Every round update BOTH canonical guides, plus test/design docs.

**Verification:** GitHub source committed, static references inspected, NOT compiled or observed working in Dev Build. The user will do that at final stage.


### Final safety refinement — 2026-10-13

Commit `143a47fe970f132feb4d18ffbded823823f242f1` augments `Zombie.TryStepPursuitBeyondNavigationOnOpenGround`: even when visible open ground passes slope/collision checks, the next step is rejected unless `Level.checkSafeIncludingClipVolumes(ground.point)` confirms playable ground. This intentionally prevents chasing into forbidden map bounds/clip regions. Source re-fetched and syntax structurally checked; **not** compiled/Dev Build-tested. Add explicit map-edge/clip test; ZMB-01 remains open.


## 2026-10-13 — LATEST IMPLEMENTATION: OFF-NAV ZOMBIE CORNER STEERING + REAL OFF-POI NOISE

**READ FIRST each future round:** `MOBILE_NPC_PENDING_TASKS.md` (canonical backlog, never delete until compiled and user-provided Dev Build feedback), this HANDOFF, and now `ZOMBIE_CROSS_REGION_NAVIGATION_DESIGN.md` for zombie paths and native net boundaries. The final full NPC AI architecture diagram is requested after all major modules. User compiles/tests only at the end; partial source slices may be "QUASE PRONTA — AGUARDANDO BUILD", but `ZMB-01` and `ZMB-02` as complete features are still `EM IMPLEMENTAÇÃO`.

### Actual code commits on feature/npc-ai-foundation

1. `6b4465bbb59fc9300708b7a615861ccf0bfb1694` — Zombie.cs: fully server-side `TryStepPursuitBeyondNavigationOnOpenGround` now handles short corners when direct ray is blocked. Four side-heading candidates ±55°/±80°, 2.5m waypoint, at most one candidate scan every 0.65s; each candidate and subsequent movement revalidates line segment, collision, floor, slope<=38°, water, level clip, short vertical delta. Use CharacterController.Move, never bypass collision, teleport or assert full A*. Direct clear always wins. Bounded turn speed, limited movement.
2. `715eb8304ebc39ad662e8506b1d78731c3903b99` — Reset detour state on `reset()`, `stop()`, acquiring new Player or MobileNPCHumanoidVitals. Three stalled waypoints trigger a 6s native-navigation cooldown; reattempt afterward or when target moves significantly, no permanent stale state.
3. `d07e66fde6cab24b61d7887bff499409a56195c6` — Off-nav fallback applies when zombie OR target is outside ordinary nav; otherwise original native seeker manages normal pursuit. This prevents a zombie stranded outside the POI when the player steps back inside. Also fixes unintended every-tick candidate rescans when no detour is active and the remembered previous target position is unset.
4. `0ebc2b77a900063953aedfefb3e0fe332566bda2` — `Managers/ZombieManager.alertZombiesToNoise` no longer requires valid navigation ID for a genuine noise. Validate server + loaded map, finite source/radius, playable `Level.checkSafeIncludingClipVolumes` and preserve existing 0.12s spatial noise cooldown. The origin may be real open terrain outside a POI; zombie recipients are still gated by physical distance, and existing target priority still applies.
5. `aed6443b6184e9027f00c4aab8a2e8a0a5a81d81` — The same bounded short off-nav movement is also allowed for native `EHuntType.POINT` investigation with no living humanoid target. The target remains the genuine heard-position clue, not a fabricated direct target; normal active player/NPC target checks remain authoritative.
6. `35ada15201f8bdcc4c0b542ac2a35e1451ebb8a8` — New detailed `ZOMBIE_CROSS_REGION_NAVIGATION_DESIGN.md` for state machine, performance, obstacles, tests, licensing/navigation gap and client interest blocker.
7. `1fd2fe33283a2b861842f719bb7c2b58729a317d` — `MOBILE_NPC_PENDING_TASKS.md` updated, no ID removed, ZMB-01/ZMB-02 still EM IMPLEMENTAÇÃO.

### Native network blocker (do not implement naive workaround)

Read `Managers/ZombieManager.cs` `ReceiveZombieStates`, `SendZombieStates_Write`, `GatherRemoteClientConnections`. Zombie identities are `bound/id` inside `regions[reference].zombies`, and clients reject states until their region is networked; `GatherRemoteClientConnections(zombie.bound)` only sends to clients in the zombie's **original** region. Physical server-side pursuers can move outside nav yet not appear to clients in the destination region. Broadening the send call to arbitrary nearby remote clients is unsafe unless the zombie has been registered and initialized on that client with its original bound/id. NET-01 and ZMB-01 stay active. User primarily targets singleplayer first.

### Remaining P0 engineering

- Licensed A* Pathfinding Project backend and navigability between distinct regions still unverified; this is **not** full building/bridge/stair navigation nor 24m+ off-mesh routing. Local corner detours may fail on walls longer than 2.5m and defer to native seeker. Don't promise any map can now be traversed.
- `Zombie.tick` calls `seeker.Move(delta)` if local fallback returns false. Native seeker still controls combat turning and path when both actors are on navigation; manual movement only if server-authorized open ground is physically validated. Check interaction with animation, footfalls, fatigue, nav gravity and 3D raycasts in Unity.
- Spawn/corpse, ragdoll, firearms damage, native NPC hitboxes, complex NPC building navigation, native radio interception, dynamic zombie network interest and category-level resource place learning remain pending (see permanent backlog).
- No Unity compilation or gameplay tests were executed in this round. Only GitHub source inspection and source changes. User supplies final Dev Build logs later; don't close items automatically.

**Next step:** audit and address authoritative zombie PLAYER/NPC sight and hearing in POIs, investigate movement stalls under external physical fallback, then return to P0 `CORE-02/CORE-03/CORE-04` NPC hitbox, corpse, building corner routes. For network, design client registration preserving home bound/id; do NOT send invalid state packets. Always update BOTH canonical guides and tests after each implementation round.


## 2026-10-13 — LATEST P0 NPC MOBILE DAMAGE ROOT CAUSE FIX (NEXT SESSION START HERE)

**Always read** `MOBILE_NPC_PENDING_TASKS.md` + this HANDOFF before new work; update both EVERY round, and tests/design docs if applicable. The user wants compilation and play-testing only at the final stage; subfeatures may be source-near-ready without declaring them resolved. Full AI flow/memory diagram is due after all core modules. **CORE-02/03/04 remain OPEN.**

### Verified root cause of "shoot NPC and nothing happens"

The runtime mobile humanoid is neither vanilla `Player` nor `Zombie`. Its `MobileNPCHitbox` is a custom non-trigger Entity-layer capsule on the template, with `MobileNPCHumanoidVitals` on the parent.

Before this round, `Player/PlayerInput.cs` `PlayerInputPacket.write` categorized hits as native types only. A hit on custom `MobileNPCHitbox` would fall through to `ERaycastInfoType.SKIP`; in listen-host `PlayerInput.sendRaycast` it was also dropped as `null`. Therefore the server-side code in `UseableGun/UseableMelee` never received `info.colliderTransform` for the custom NPC, even though its `ApplyDamage` method existed. **This is a source-traced upstream blocker, NOT a hypothetical health formula issue.**

### Implemented source commits (all on feature/npc-ai-foundation)

- `dd69e0ae9728c7ed0c474274665102b75085bc0c`: `Player/PlayerInput.cs` adds append-only `ERaycastInfoType.MOBILE_NPC` (preserving older native enum values). Packet write/read transports world point + ray direction without fake native spawn ID or serialized runtime transform. Listen-server `sendRaycast` classifies custom NPC collider. On consuming input, server performs `DamageTool.raycast(..., RayMasks.DAMAGE_SERVER, ignorePlayer: player)`, requiring first server hit to be a living MobileNPCHumanoidVitals collider within 1.5m of reported point, then supplies server-resolved `colliderTransform` to the original gun/melee damage adapter. This prevents client inventing hidden targets through walls; original gun/melee range/ammo/falloff checks still apply.
- `fa4da5641117cb356d2180bb9120278c25c3d802`: apply default native `ELimb.SPINE` for server-verified custom NPC input and bounded ray candidate range (max 750m for firearm / 15m melee), with per-weapon actual limits still in `UseableGun`/`UseableMelee`. Head region is resolved geometrically by NPC vitals, NOT by client-provided limb.
- `5c338d44abeb414808b1f3fd63bb53a2cd83b6fa`: `MobileNPCHumanoidVitals` `ResolveBodyPartAtWorldPoint` approximates anatomical region based on physically confirmed hit location on upright 1.8m capsule. `ApplyValidatedWeaponImpactServer` invokes `ApplyDamageToPart` once with arm/leg/head/torso, bleeding/fracture probabilities based on final weapon damage and attacker identity. Explicit caveat: one capsule cannot provide bone-precise arms or legs.
- `ada2d7ab92a94227af9a8f3b058db7398248577a` (`UseableGun.cs`) and `8ef25516e2bd786bf9d122c1fbaf8bdd85d912a0` (`UseableMelee.cs`): connect original damage calculation, actual critical head multiplier and client hitmarker to new vitals impact API; no duplicate flat damage application.
- `ebe7533c1549babeb10f448e1c1758ac0ae36c14`: `MobileNPCHumanoidRangedCombat` uses same validated body-injury entry point after physically verified gun ray and consumes finite ammo.
- `083858ff91e0abe9bc2ba838f5a5630a85edbc8e`: `MobileNPCInjurySystem.ApplyHit` delegates to `vitals.ApplyDamageToPart` for unified guards/initialization and prevents setting bleed/splint on dead NPCs.
- `359e6976b16e5591cf7de077ccc6ac963644602a`: `MobileNPCHumanoidMotor` local 9x9 grid route had a wrong extra candidate-origin spherecast pointing away from start, falsely discarding viable grid nodes beside buildings; removed it because actual graph edges already perform proper collision checks. Now forbid route edges between floors with rises >stepOffset/skin tolerance or drops >1.1m.

### Required tests / state labels

**Static source and GitHub commits only. NO Unity compilation, NO Dev Build testing.** `CORE-02` is OPEN despite a now-connected source damage path. PlayerInput packet compatibility must be checked on host and, later, multiplayer. Normal Player/Zombie hits should regress unchanged because enum value was appended and native payloads left intact. Native armor, XP attribution, correct skeletal ragdoll, per-limb physical collider accuracy and NPC corpse retirement are not proven. `CORE-03` and `CORE-04` remain open.

Test shotgun multiple pellets vs individual hit count; head/torso/leg difference; local host vs remote packet; invalid spoofed point, hit through wall, shot after NPC death; no hitmarker if zero damage. Check message logs; `MobileNPCHumanoidVitals` emits `[Damage accepted]` and `[Death transition]` diagnostic output. Compare against `MobileNPC_SINGLEPLAYER_TEST_PLAN` (exact filename `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md`).

### Next safe priorities

1. Audit `MobileNPCPopulationBootstrap` anatomical collider strategy and authoritative project layer masks; improve actual head/torso collider precision if source permits without altering movement collision matrix.
2. Confirm server raycast / packet protocols for punch/unarmed, firearms, melee, NPC-to-NPC, and zombie-to-NPC. Native PlayerInput `MOBILE_NPC` currently intentionally handles **Gun and Melee only**; Punch remains pending, no fake success.
3. Continue P0 `CORE-03`: real authored ragdoll prefab / fallback, corpse cleanup, XP/loot without fake `PlayerLife`. Then `CORE-04` complex buildings, pursuit, cover/nav.
4. Never close tasks before user performs the final Dev Build; update BOTH canonical guides and test plan every engineering round.


### Additional mobile hit packet compatibility — 2026-10-13

Commit `59714ab8d05dcb606834294069c78b03c06554ec` extends `PlayerInputPacket.write` mobile NPC detection to choose `info.collider.transform` when available or `info.transform` otherwise. Some `DamageTool.raycast` paths can provide the transform without a separately retained Collider. The server **still** requires a new physical raycast and a real living NPC collider; this fallback is not an authorization bypass. Re-fetched modified sources via GitHub and checked required symbols and balanced braces, **NOT compiled**. Continue CORE-02/CORE-03/CORE-04 and repeat full Dev Build gun/melee/headshot checks at user-requested final testing stage.


## 2026-10-13 — LATEST CORE-02/CORE-03: REAL ANATOMICAL HIT VOLUMES AND DEATH-LOOT / RAGDOLL PREFAB AUDIT

**READ FIRST for next round:** canonical `MOBILE_NPC_PENDING_TASKS.md`, this `MOBILE_NPC_PROJECT_HANDOFF.md`, and new `MOBILE_NPC_HITBOX_DEATH_DESIGN.md`. Maintain all task IDs and histories; mark subfeature source-ready only, never closed without user-supplied Unity Dev Build feedback. User explicitly compiles and playtests **last**. The full NPC AI flow diagram is requested after the main features have been structured.

### Direct evidence from asset source

Checked `Assets/Resources/Characters/NPC_Client.prefab` YAML in the current feature branch. It contains **ONE Rigidbody and ZERO Joint components**, with six authored BoxColliders, two SkinnedMeshRenderers and legacy Animation. There is NO real articulated bone ragdoll to switch on, regardless of previous comments. Existing `MobileNPCHumanoidVitals.ActivateRigidCorpseFallback` provides a physically colliding, tumbling capsule with gravity and recoil impulse. Do NOT label it an articulated ragdoll. `RAG-01` added as a distinct tracked source/asset blocker. To achieve articulated joints, bone-aligned colliders and constraints would need to be authored/validated in Unity or a correct humanoid rig prefab.

### Source commits this round

- `d0b109d26df87abec349e1be06fcc274504d2286` — new `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCBodyHitbox.cs`, marker for physical hit collider / `MobileNPCInjurySystem.BodyPart`. Marker doesn't own health.
- `8d3973480172ab0a1b4208a3e3b739b21afd60c7` — `MobileNPCPopulationBootstrap` now authors **six** non-trigger, explicitly marked hit volumes under `MobileNPCHitbox` on native Entity(23) layer: Head sphere (0,1.65,0)/r0.20, torso capsule (0,1.02,0)/height0.81/r0.26; paired arms x±0.43,y1.10,h0.72/r0.14; paired legs x±0.17,y0.39,h0.72/r0.16. The controller remains Agent(24), visuals Medium(16) with existing -90° rotation. The collision volumes use ROOT UPRIGHT coordinates, not incorrectly rotated mesh-local positions.
- `cc278d6b9d6bddf3df0433959d6c6376aef376b0` — `MobileNPCBodyHitbox.OnEnable` re-applies `Physics.IgnoreCollision` with own parent CharacterController upon **actual prefab instance activation**, because Unity can reset or ignore ignore-collision relationships on inactive template objects. Required for no self-push or strange movement.
- `e8895e7f91a38bfeef40aed59fbec8407bb2ae63` — `MobileNPCHumanoidVitals.ResolveBodyPartAtWorldPoint(point, hitTransform)` uses hit marker only if it belongs to THIS NPC, retaining point-height fallback if older prefab exists; `DieServer` disables ALL child hitbox colliders before corpse physics. Ensures shots against corpse don't keep applying live injuries.
- `6e799bcc7e3646965f0ffb60b9a0df49deb8480c`, `f18b44c9bb104266c6ebe25c1e4d476dc9a74230` — Unturned `UseableGun/UseableMelee` client and server hit markers/damage now recognize physical Head marker for ELimb.SKULL, using ELimb.SPINE for all other mobile NPC body part multipliers. They pass authoritative hit collider into single-injury `ApplyValidatedWeaponImpactServer`. Native unarmed `Punch` remains not integrated.
- `3efe708f21956a9a61d0723366b38d83ff39de73` — `MobileNPCHumanoidRangedCombat` forwards actual raycast collider to NPC-vs-NPC injury model, instead of guessing region by hit height.
- `68bd3d8b9be71e4b71267cc47d7e057355ab7725`, `ce0da6e8bda2818e2167c331a760f419d04d382c` — `Managers/ItemManager.cs` introduces `TryDropMobileNPCItemServer`. Unlike vanilla `void dropItem` it provides explicit success result after actual `ItemRegion.items` insertion, applies game's `onServerSpawningItemDrop` policy, rejects invalid/pro assets and unsafe map positions before/after event, and broadcasts actual world item with state. Do NOT introduce an artificial gun or infinite health item.
- `21c5c277d2757a715be726ac60314328728804a4`, `b609ed749631a3bde8c75e903547dbbe1376072c`, `1c55dcba22413c296b801c726aab7f57c455ff07` — new `MobileNPCDeathLoot.cs` attached after `MobileNPCInventory` in runtime prefab. Subscribes to `MobileNPCHumanoidVitals.Died`, once per death: drop actual inventory slots with exact state/quantity/ammo after removing item; roll back refused world spawn, try again every >=4s before corpse is removed; no loot table inflation. An actually Hostile mobile NPC directly killed by Player gives 10 XP via real `PlayerSkills.askAward` exactly once, not generic friendly kills and not native Player kill statistics/quests. Environmental bleed fatal damage clears LastAttacker; those give no XP automatically.
- `d702f400d7ac68115b4ae77f34d91a492297d989` — new `MOBILE_NPC_HITBOX_DEATH_DESIGN.md` lists anatomical source dimensions, native physics prefab limitations, loot transactions, XP limits, and final tests.
- `b9b8e7677e173cd984fd21b03c634bbe1e5250ed` — update canonical backlog, add `RAG-01` while retaining CORE-03 unresolved; CORE-02 scoped Gun/Melee/NPC ranged source-near-ready only.

### Remaining constraints (NOT solved)

- All source, **not compiled or playtested**. Validate new `RequireComponent(Collider)` marker alongside existing actual Sphere/CapsuleCollider, custom `MOBILE_NPC` PlayerInput serialization/physical confirmation and all actor hitbox layers in Unity. Native headshot marker must actually be hit by Physics and line of sight. Bullets from NPCs also need realistic non-perfect aim and no friendly fire.
- Anatomical colliders are **root-fixed approximate limbs**, not bone-animated hitboxes. Their alignment with the -90° source mesh animation may vary. For real bone-attached regions, inspect actual Skeleton rig hierarchy and bone matrices and authorize prefab-related changes; do not invent bone references.
- Full ragdoll skeleton cannot exist without Joint/rigidbody authoring; non-articulated physical fall remains implemented, so RAG-01 is OPEN. `CORE-03` still needs XP/drop/loot/kill-feedback validation, and native quest kill attribution is still missing.
- `MobileNPCDeathLoot` must be tested against inventory reservation races, Pro-only assets, event veto, safe zones, water, buildings, exact gun state and cleanup lifetime. ItemManager spawn success is server authoritative but drop-on-death and aftermath unverified.
- Punch and zombie melee paths are still outside this Gun/Melee work. PlayerInput MOBILE_NPC handler currently restricts usages to Gun and Melee, so punching needs explicit native implementation and validation. CORE-02 not fully finished.
- Corpse deletion follows population director (30s default), individual replacement queue (60s default); newly spawned individual has no dead-person private knowledge.
- Update BOTH guides every round; do not do builds until the final user playtest. Next engineering suggested: resolve native punch and zombie→NPC hit adapters, then author rig-safe ragdoll strategy and continue CORE-04 nav.


**Final corpse loot refinement:** `92ef3dd8e6331b90a1ba692e4636221995d32e6b` moves corpse world-drop source slightly sideways (0.9m) to keep native ground-probe from striking its own rigidbody capsule. The same real ItemManager hook/region-validation/rollback semantics remain. Game test required near walls, uneven ground, buildings, stacked corpses. `RAG-01` remains OPEN; neither skeletal joints nor actual Unity testing has been completed.


## 2026-10-13 — LATEST P0 CORE-02: actual Punch and Zombie / NPC melee paths

**Mandatory when continuing:** read canonical `MOBILE_NPC_PENDING_TASKS.md` AND this handoff BEFORE code edits; append updates after each round. User runs Unity build and playtests only AFTER implementation phase; don't prematurely call any task fully resolved. Final full diagram of NPC A-Life, decision, learning, groups and radio remains requested only after main systems are structured. Branch `feature/npc-ai-foundation`, repo `pplopes2003-design/Unturned-Better`.

### Root causes confirmed from native code (not assumptions)

- `Player/PlayerEquipment.cs` `punch(EPlayerPunch)`: native client raycasts 1.75m and sends `ERaycastInfoUsage.Punch`; server consumes this usage and dispatches only native PLAYER/ZOMBIE/ANIMAL/VEHICLE etc. Custom `MOBILE_NPC` packet recently added but `PlayerInput.getInput` explicitly **rejected usage Punch**, so NPC punches could never affect vitals.
- `Zombies/Zombie.cs` native damage branch for `mobileNPCTarget` historically called `ApplyDamage(damage, hitImpulse)` directly, without authoritative current-distance+LOS check at the strike instant and without region injuries; a close but wall-separated target could take damage and no bleeding. Attack animation timing/vanilla species damage multipliers already exist; preserve them.
- `MobileNPCHumanoidCombat.TryAttack(MobileNPCHumanoidDisposition)` also debited `ApplyDamage` without limb injury, though its `Update` had authoritative nearest-transform raycast and faction policy.

### Source commits delivered

- `1a99b7a6ea709a47c47459ac7853f006c28ca38e`: `Player/PlayerInput.cs` allows `ERaycastInfoUsage.Punch` on `MOBILE_NPC` only with angle to actual server aim direction >= dot 0.95, finite range, and a new authoritative `DamageTool.raycast` first-hit collider of a living NPC. Other usage types remain rejected.
- `96a9594bd478c27a7312bcbdd5f8878e472fabd0`: `Player/PlayerEquipment.cs` client `punch` hitmarker uses specific physical `MobileNPCBodyHitbox` head marker for critical; server processes `MOBILE_NPC` prior to native PLAYER branch, uses `DAMAGE_PLAYER_MULTIPLIER.damage` times normal/head factor, player's real melee `OVERKILL` mastery and fake-lag penalty, invokes `ApplyValidatedWeaponImpactServer` with confirmed physical hit transform and the real Player attacker exactly once. Direct hostile NPC kill XP already handled via `MobileNPCDeathLoot`.
- `e77b4bb4d79384230338b47a3e4efba97a1a4ee3`: further cap punch packet hit at 2m and server ray maximum 1.95m (vanilla punches raycast at 1.75m); fix listen-host local `sendRaycast` classification when Collider is null but Transform references a mobile NPC.
- `97c2d29e1f4dd7ddf4e11f5c8a51aebb7944b807`: `Zombies/Zombie.cs` only commits native zombie swipe into living mobile NPC when current horizontal+vertical distances remain within existing game's attack ranges and physical LOS `BLOCK_SENTRY` isn't obstructed by other solids. It may hit its **actual selected NPC collider** but not a wall in front. Calls `ApplyValidatedWeaponImpactServer` (single vitals debit + wound chance + zombie attacker). Vanilla zombie→Player, vehicles, structures, objects unaffected.
- `4fcbe38839f1e3d79da51d57a9ec2e20c72f5d2c`: `MobileNPCHumanoidCombat.cs` forwards `nearestTransform` physical collider from the already validated fight ray to limb-aware `ApplyValidatedWeaponImpactServer`; legacy API callers can omit the optional hit transform, using conservative torso-height fallback. Existing faction enemy check/cooldown/AttackStarted animation preserved.
- `4c20cdbd4b5e21eeb9255e9f8c49a838555a0ba8`: permanent backlog change, CORE-02 source **QUASE PRONTA — AGUARDANDO BUILD**; other task IDs untouched.

### Do NOT overclaim

- No Unity compilation, no in-game tests, no Player.log inspected this round. CORE-02 remains open until user tests.
- Infection from undead bite is **not** yet modeled with validated separate infection status; don't infer it from generic bleed. Zombie target selection, realistic senses, zombie nav beyond POI, animations and rigid capsule corpse remain open under ZMB-01/02, UI-02, RAG-01.
- Colliders are physically authored but still root-local coarse anatomy rather than truly bone-following rig. Verify physical contacts, gun headshots, hitbox collision masks; don't call it skeleton ragdoll.
- Punch per-weapon damage, melee range, attack clocks and anti-lag are reused where possible; remote multiplayer is not ready. Zombie line-of-sight uses `RayMasks.BLOCK_SENTRY`, must test cases where first ray hits own/target collider vs real wall.

### Next implementation

Continue P0 `CORE-04` pathfinding in houses and corner detours, `RAG-01` authored bone-based rig/joints if genuine asset support, or tackle UI-01 dialogue HUD/ESC critical regression. Prioritize the user's original visible bugs. Persist changes and tests across conversations; never assume editor compilation.


## 2026-10-13 — LATEST P0 CORE-04: BUILDING-CORNER / NAVIGATION STUCK FIXES

**Read FIRST in every continued chat:** `MOBILE_NPC_PENDING_TASKS.md` (canonical retained-ID backlog), this handoff, and `MOBILE_NPC_NAVIGATION_RECOVERY_DESIGN.md` for motor/brain navigation. Commit to branch `feature/npc-ai-foundation`, NOT main. User compiles/tests the Dev Build **only at end**; do not repeatedly ask. Source-only subfeatures may be "QUASE PRONTA — AGUARDANDO BUILD" but CORE-04 as a whole stays **EM IMPLEMENTAÇÃO**. Full AI diagram comes after large subsystems.

### Causal findings in live branch source (no runtime testing)

- `MobileNPCHumanoidMotor.Update` acknowledged arrival at `movementDestination` (intermediate A* or detour waypoint) with `brainDriver.ReportDestinationReached()`, wrongly marking tactical COVER/LOOT/INVESTIGATE complete before actual destination! New code acknowledges only if final `destination` is truly within `stoppingDistance` horizontally **and** within a realistic Y stepping limit.
- The native A* early request and local 9×9 grid A* shared `nextRoutePlanTime`. Because native requests run before encountering walls, local fallback was often throttled by the wrong timer and fell back to a jittery sidestep. Distinct `nextLocalRoutePlanTime` now unlocks physically validated local paths independent of pending native requests.
- Local route recovery occasionally replaced a validated grounded end-cell with an *unvalidated* projected `localGoal`, potentially inside the very building the NPC was trying to skirt. Eliminated substitution.
- Stuck scoring demanded forward movement toward distant final target, misclassifying needed sideways movement around building walls as failure. Evaluate actual displacement closer to the current route/detour waypoint, maintain one side for >=4s before rotating again.
- Planar-only waypoint arrival could skip steps on a different building floor; added Y agreement. `IsRouteNodeOccupied` overlap capsule started exactly on ground and could mark cells falsely blocked by touching terrain; offset +0.10m.
- Planned route obstacle recheck formerly each frame; limit to >=0.3s between checks and avoid invalidating working door routes when blockers are ONLY transient people/zombies/NPCs. Still invalidate fixed walls/vehicles.
- Optional tactical point (SeekCover or Flank) proven repeatedly physically unreachable: motor reports after 4 stuck recovery events; brain records individual negative cover-access outcome, suppresses that 3m local position for 14s, and cancels ONLY that optional point. Does NOT erase active pursuit, invent cover or grant pathfinding knowledge.

### Actual commits

1. `320db52b602caed67e4433121394f3fcaf24c9b3` — final brain destination vs intermediate route waypoint and independent native/local cooldowns.
2. `9fb964973f728bf8c44f3ce90fc01c9e630f305b` — grounded route endpoint preservation and bounded route revalidation.
3. `136a581290fea08add61c080f4af961d4a1f88e2` — waypoint-relative stuck progress and side commitment.
4. `dabc9f0777c4b8b448f74536bfedffb87d013239` — vertical waypoint arrival and ground-contact-safe occupancy capsule.
5. `7a718aa67a96f7de6d924739dc2c9e9e6feee624` — temporary actor crowding doesn't tear down static path; walls/doors still do.
6. `43247204664f8a7e02beeb6866795d15c360b2f3` — `MobileNPCBrainDriver.ReportTacticalDestinationUnreachableServer` learns failed personal cover and suppresses immediate same-place selection.
7. `ff857bc2a03fdb08694cad6315b6d450dc140307` — `MobileNPCHumanoidMotor.UpdateStuckRecovery` escalates after four failures of only optional tactics.
8. `58a29b9deefe43b5a57536bb94f67db4b3ce710e` — `MOBILE_NPC_NAVIGATION_RECOVERY_DESIGN.md` created.
9. `4f9d4cb6693abe5855ff7ec89fc5e2a7200421d6` — canonical backlog updated; CORE-04 still EM IMPLEMENTAÇÃO.

### Risks and later work

These source changes were NOT compiled or tested in a real Unturned map. Local search remains 9×9 grid cells at 1.25m pitch, so does not guarantee a path around an entire house; native `MobileNPCNativeAStarRouteProvider` uses reflection and only works if the commercial ASPFP backend is active. The full SDK does not guarantee baked graph connectivity across all levels. Navmesh stitching, actual door use/open/close, ladders/stairs, cover verification under real fire, climbing and swimming still P0/P1 work. `LEARN-05` individual contextual route learning beyond simple cover access remains incomplete.

Next recommend: inspect and correct `UI-01` critical dialogue HUD/ESC broken since previous Dev Build OR continue `CORE-04` safe doorway/stairs handling and actual route success memory. Keep action in GitHub, update BOTH docs and tests each round, final Unity build at end only.


## 2026-10-13 — LATEST ENGINEERING: SHOOTER-INSPIRED TACTICAL URBAN NAV (CORE-04/AI-03)

**Start next session by reading** `MOBILE_NPC_PENDING_TASKS.md` and THIS handoff. For paths also read `MOBILE_NPC_NAVIGATION_RECOVERY_DESIGN.md` and new `MOBILE_NPC_URBAN_COMBAT_NAVIGATION_DESIGN.md`. Keep commits on `feature/npc-ai-foundation`, not main. User requested no Unity compile/playtest until final implementation phase. Update both canonical docs + tests after every engineering round. Never delete a task merely because source code exists. Full NPC AI/learning/communications diagram at END.

### Primary shooter research actually checked

- `F.E.A.R.`: Jeff Orkin (AAAI AIIDE 2005, GDC 2006) goal-oriented action planning dynamically replans action sequences on blocked routes/doors; expensive planning must be budgeted/cached. Real game still uses physical pathfinding separately. `https://ojs.aaai.org/index.php/AIIDE/article/view/18724`; `https://www.gdcvault.com/play/1013459/contactUs`.
- `Killzone`: Guerrilla Games GDC Europe 2005 slide deck shows tactical waypoint-link costs for enemy exposure/friendly fire rather than only shortest path. `https://www.guerrilla-games.com/media/News/Files/gdce05_killzone_ai.pdf`.
- Recast/DetourCrowd official source distinguishes nav/path corridor corners from local steer/crowd avoidance. Avoid installing an unmanaged 2nd transform mover alongside Unity `CharacterController`: `https://github.com/recastnavigation/recastnavigation/blob/main/DetourCrowd/Include/DetourCrowd.h`.
- Game AI Pro tactical navigation chapter `https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter27_Tactical_Pathfinding_on_a_NavMesh.pdf` warns unrestricted visibility rays/waypoint grids increase CPU cost; bounded exposure samples matter.

### Actual GitHub source commits

1. `9d4db3730af9ef2faa10696c5f821d2ddddf3bb2` `MobileNPCHumanoidMotor.cs`: **Killzone-like tactical local path cost**. When current goal is `SeekCover` or `Retreat` and the hostile is actually visible/within 40m, add +1.45 cost for exposed next nodes vs sheltered node cost 0, using real physics/world obstruction. An 81-cell A* route has max 24 supplementary LOS raycasts using per-instance reusable arrays; other goals remain shortest geometric paths, unseen player's live location NOT used (no omniscience). Also **Detour-like safe local path corridor lookahead**: every >=0.35s can skip 1 redundant corner if the following waypoint is within 4m, same floor, collision-free chest corridor and walkable ground verified at each ~1m sample. Movement always through `CharacterController.Move`. No walls/doors/large stair skipping.
2. `ceef34f459de64ab8a2eceda83bcda4e8ea96868` same file: **`CanReachNearbyCoverServer`** runs a small, bounded 9x9 local A* against a cover candidate without mutating `routeWaypoints`, route index or cooldown of the NPC's active goal, using dedicated `coverRouteScratch`. It only returns true when target's actual preferred reachable grid cell is connected with correct floor and a clear last segment. No partial "closest reachable" shortcut counted as success. Targets further than ~6.5m / vertical >1m rejected by this local complex-path query.
3. `42e68122574b1e4523d74352134efe05dcc93e28` `MobileNPCBrainDriver.cs`: cover behind corner was discarded solely because a direct spherecast from actor to shielded position hits wall. Now if ground/shield checks already passed and direct corridor is blocked, allow up to 3 independent physical local A* reachability checks per defensive decision, and only use cover candidate if exactly reachable. No auto wall phase, no globally granted exit knowledge. Existing personal `CoverAccess` success/failure bias remains.
4. `f50d91d1191205cb4f17edc8a83e40b773ecd496` new design doc `MOBILE_NPC_URBAN_COMBAT_NAVIGATION_DESIGN.md` includes comparative research references, actual route logic, safety/performance, final multi-map test matrix and future work.
5. `ad3bddd23713a13fa90527682c0b2f3fad87052c` canonical `MOBILE_NPC_PENDING_TASKS.md` update. **CORE-04 still EM IMPLEMENTAÇÃO, AI-03/LEARN-05/PERF-01 still OPEN.**

### Critical limitations / future P0

Full authored map-wide graph not guaranteed: optional Unturned ASPFP backend is present only in some builds and its commercial source absent from SDK. Local grid 9x9 @1.25m cannot reliably route through multiple rooms/doors/stair floors. `CanReachNearbyCoverServer` permits only short local corner solutions; all other paths and preemptive decisions remain unchanged. Tactical exposure checks represent only **one currently visually confirmed hostile**, not multiple threat cones or allied gunfire paths. 24 visibility probes and <=3 cover A* queries per defense decision are preliminary CPU caps, not profiled. Follow-up on occupancy collision layers and same-floor navigation to avoid any physical mistakes.

Next highest priority should be explicit door/open/closed/breach traversal semantics, native scene door API, and navigation links between floors/POIs; also revisit UI-01 dialog HUD/ESC bug. Do not promise completed 'shooter-level urban combat' until real Unity Dev Build tests.

No Unity compilation, Player.log or real playtesting happened in this round. Source files GitHub re-fetched and structure sanity-checked only. User will compile at END.


## 2026-10-13 — LATEST P0 NAVIGATION: native barricade doors without lock bypass

**Read before all future source edits** `MOBILE_NPC_PENDING_TASKS.md`, this HANDOFF, `MOBILE_NPC_URBAN_COMBAT_NAVIGATION_DESIGN.md`, and NEW `MOBILE_NPC_DOOR_NAVIGATION_DESIGN.md`. Branch `feature/npc-ai-foundation`, NOT main. User does the final Unity/Dev Build only after implementation rounds; no feature is runtime-confirmed. Full AI architecture diagram is still required at end. Keep every task until user feedback and update both canonical docs and tests each round.

### Direct source findings

Unturned's actual `Interactable/InteractableDoor.cs` supports `isOpen`, `isOpenable`, a private asset-derived `isLocked`, native animated `ExpandedBarrier` and shared `OnDoorChanged_Global`. `Managers/BarricadeManager.ServerSetDoorOpen(door,true)` is a real server method that finds registered `BarricadeRegion`, invokes reliable synced `InteractableDoor.SendOpen`, and writes persisted barricade state. Its internal implementation assumes a live BarricadeDrop. **Crucially `checkToggle` can ignore lock on singleplayer host**; AI must not use that as authorization.

### Source commits

- `a982f327fa23c9d797c7653ff880b644a18bd8d2` — read-only `InteractableDoor.IsLockedForMobileNPCNavigation` exposes true native isLocked without modifying permissions.
- `18d793e05058f00b43544369f5322c1bf7d8287a` — new `MobileNPCDoorNavigation` component with bounded physical first-obstruction check, only one *genuinely unlocked* `InteractableDoor`, within 3m/2.4m, excludes vehicle doors, respects animation/toggle cooldown and physical blockers. Invokes only `BarricadeManager.ServerSetDoorOpen`, never fakes a Player/updates animation directly/disables collider/teleports.
- `3e605e9ed0fc1af7b47517271bb9a0698722c898` — attach door component to inactive runtime template.
- `2378921a1498080fd031c1c18ae2443f93f11b02` — motor handles native door before invalidating an existing route or starting new local sidestep; pauses movement while door animates and retains real final destination.
- `79fcc883358fa80cc216592bb2a4b5235d1abd3c` / `ffb23d9ba5d58710a2b16391b9c06476c904c710` — local A* **edge** can plan through one unlocked barricade door (side-effect-free physics classification), if no extra wall/vehicle/entity blocks same edge.
- `54f24edc64ae6b33174bd66e0591b7f5772a32d4` / `423d0b2e4539076130d1039c4901de061b59ebd9` — local A* **node** that overlaps only this valid door can remain walkable, but never one overlapped by both door AND another solid obstacle.
- `f867b1c798a786a0ff784ace65867a4079813fa8` — verify native barricade root has an actual `BarricadeRegion.FindBarricadeByRootFast` registration before invoking `ServerSetDoorOpenInternal` path which assumes nonnull BarricadeDrop.
- `d270eb35387c6c2e3126e53705c0480284e18f08` — keep brief animated door wait across throttled spatial probes; avoids path invalidation while hinge is still moving.
- `145ff0761bc884b4ffa2e9cb82f04df4bcebe6cd` — new full design and test matrix `MOBILE_NPC_DOOR_NAVIGATION_DESIGN.md`.
- `de06ffa7462ed41fc8791e596754e3edab08749d` — backlog updated; NEW `NAV-DOOR-01` near-ready scoped subitem, overarching CORE-04 still EM IMPLEMENTAÇÃO.

### Explicitly NOT solved / test concerns

A native barricade door may have `ExpandedBarrier` collider and animated hinge; the NPC still waits for real physics clearance. Doors native to some specific level objects, custom map interactive doors, vehicle doors, locked barricades, breachable locked doors, elevators, indoor stairs, full global A* connected multi-storey buildings are **not** solved. These require actual APIs/assets and additional planner actions. Route recognition of an unopened unlocked door does NOT permit wall collision bypass or movement through it while closed. A path through a door with extra wall or vehicle collider is rejected. NPC owner/group affiliation policy is not impersonated: only unlocked doors work.

No Unity compile, no game test, no Player.log this turn. GitHub code, native definitions and only structural checks available. `NAV-DOOR-01` is `QUASE PRONTA — AGUARDANDO BUILD` for ordinary registered unlocked InteractableDoors only. `CORE-04`, `AI-03`, `LEARN-05`, `UI-01` remain pending. User's final feedback decides close or reopen.

### NEXT P0 action

Inspect actual *stairs / multilevel navigation link* representations in U3SDK level objects/nav boundaries and graph API without inventing A* source. Avoid authoring universal stairs from floor height alone; require actual safe traversable connectors and geolocated entrance/exits. Alternatively prioritize `UI-01` severe dialogue HUD/ESC bug or `UI-02` native attack animation gap. Source changes and documentation required for each continuation.


**NAV-DOOR-01 final static safety**: `775fb0aa0bcfefd90c44e3dff08d5325c2245e41` rejects unregistered/stale door objects from both the local A* edge and grid-cell traversability classifiers; merely finding an `InteractableDoor` MonoBehaviour is not proof it can actually be opened through BarricadeManager. Physical server opening already checks registration separately. Source only; no Unity compile. Keep NAV-DOOR-01 near-ready scoped, CORE-04 open.


## 2026-10-13 — LATEST P0 CORE-04 / NAV-STAIR-01: physics-grounded two-level local stair navigation

**Read next round:** `MOBILE_NPC_PENDING_TASKS.md` canonical backlog + this HANDOFF; also `MOBILE_NPC_VERTICAL_STAIR_NAVIGATION_DESIGN.md` for stair navigation, `MOBILE_NPC_DOOR_NAVIGATION_DESIGN.md` for doors. All code on `feature/npc-ai-foundation`, user performs Unity compile/Dev Build ONLY AT END. Do not mark tasks fully completed before source and runtime feedback; retain IDs; update both canonical docs and tests every engineering round. Diagram of complete NPC AI/learning/radio at end only.

### Actual source problem confirmed

Existing `MobileNPCHumanoidMotor.TryBuildLocalRoute` casts one local ground point per XZ cell, rejecting heights >0.8–1.25m from current floor. Its 9×9 grid cannot reach an upper storey even via genuine stairs. Simply relaxing `maxFloorDelta` risks connecting 3m-separated floors at same X/Z or crossing a stairwell gap. Need **real physical floor samples + validated stair connector edges** and a separate bounded search, not teleporting Y.

### Source commits

- `75a0613f6b4186ba2c6af25a2a6e7318dfdd8445`: motor allocates per-instance 162-node `9×9×2` workspace (up to 2 independent floor surfaces per XZ), triggers only for a near brain/roaming destination at >1m elevation delta (≤4.5m height, ≤√30≈5.48m planar), without replacing ordinary 81-node horizontal search or native A* provider. Route invalidation now also compares goal height, so a target who changes floors cannot reuse a lower-floor route just because they share XZ.
- `35c83c0adc472551933af964520e57da4677cf49`: `TryBuildNearbyVerticalRoute` samples actual walkable floor surfaces with downward `Physics.RaycastNonAlloc`, maximum 24 hits per cell with saturation fails closed, removes Player/NPC/Zombie body colliders from candidate floors; validates map clips/normal/capsule occupancy. No direct same-cell floor-to-floor graph link. A* explores only adjacent X/Z nodes, with valid slope and physically continuous floor verified every 0.35m and stepOffset/skinWidth checks, plus collision-checked humanoid chest trajectory. Native unlocked-door path can be considered only with real registered barricade. Only **exact connected target elevation** counts as success, no partial lower-floor success.
- `8543f9ccd24e7219eccf1246c436d4bb8a728243`: max 180 expensive vertical connector tests, per-instance 2.5s retry interval, shared one-plan-per-0.16s admission; each intermediate tread also checks full-body standing-capsule space so low ceiling, railing, beam or wall cannot become an imaginary traversable stair route.
- `d50b6fcab163b7dd624f679182c8b49d741593f0`: do not permanently erase a real stair route just because only a **temporary moving actor** (Player/Zombie/mobile NPC) blocks the cell/edge. Other simultaneous world colliders still block. Real `CharacterController.Move` never ignores other actors or collision.
- `c80b8e99e77a99f2d6344e0895ee4e7428441d51`: new `MOBILE_NPC_VERTICAL_STAIR_NAVIGATION_DESIGN.md` details graph, geometry, CPU bounds, still-missing global links and specific final Unity tests.
- `9899aee83ef64c9358f03268b45cc63a87f74f6a`: canonical backlog updated: NAV-STAIR-01 new P1 EM IMPLEMENTAÇÃO, CORE-04 still EM IMPLEMENTAÇÃO.

### Important limitations (do not overclaim)

- This only plans actual short stairs within approximately 5m of the NPC and target, not long-distance entry to a stairwell from another POI/floor. It requires real walkable step surfaces at 1.25m XZ-grid points and intermediate samples, and at most two surfaces per cell. Narrow/winding stairs may fail safely and need a tighter grid or authored offmesh links. Native ASPFP may be absent; public SDK doesn't provide licensed backend.
- No game runtime route demonstrated; all Unity compile/Dev Build testing remains with user at END. Compiler and physics signatures must be validated then.
- Idle NPC standing directly under an enemy but with no detected accessible stairs must not move vertically; it can still lack a global alternate entrance route — this remains CORE-04. Do not claim teleport or guaranteed stairs.
- New query load is bounded in source but not measured; 40 actors and mixed zombies/Player in stairwells can expose bottlenecks in `RaycastNonAlloc`, `OverlapCapsule`, A* scans and memory. PERF-01 still open.
- Next engineering options: external authored stair/door connectors or stairwell spatial portals for global paths, multi-level navigation graph reliability, UI-01 dialogue HUD bug, UI-02 missing NPC melee animation and RAG-01 skeletal joints. Prioritize real user-visible bugs while moving toward final build.

**Final checklist is appended to** `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md`, section 25. Update docs and test plan after any further path changes.


**Source refinement `077bb7a3cc1e4bdeb338ecfbfbbad21d798963b5`:** the ordinary 9×9 planar fallback is no longer permitted to report a closest lower-floor cell as progress toward a nearby upstairs/downstairs final goal (>1m Y, ≤√30m XZ), where the real multi-height stair planner must prove a connection. The motor counts consecutive failed special vertical plans for a stable tactical target; after 3 it may call `ReportTacticalDestinationUnreachableServer` (only optional SeekCover/Flank) to learn unreachable cover. It NEVER auto-cancels active pursuit on a failure to find stairs in the bounded region. Reset count when the goal moves or a real vertical path is found. Source only, no Unity compile/playtest.


## 2026-10-13 — LATEST IMPLEMENTATION: FIRST-HAND LEARNED ROUTES / PRIVATE SAVE v6

**READ FIRST next round:** canonical `MOBILE_NPC_PENDING_TASKS.md`, this HANDOFF and new `MOBILE_NPC_ROUTE_LEARNING_DESIGN.md`. Work on `feature/npc-ai-foundation`. User compiles/tests Dev Build **ONLY AT END**. Never mark task fully resolved before feedback, never delete task IDs. Maintain the full AI diagram request for the end.

### Clarify previous vertical-navigation claims to user

Existing `TryBuildNearbyVerticalRoute` is a short two-floor 9×9×2 A* that recognizes actual floors, walkable stair treads and ramps with physical ground and capsule validation. `MobileNPCHumanoidMotor.ShouldJumpOverLowObstacle` separately supports a REACTIVE real low obstacle hop/landing check, not a **planned A* jump edge**. Vertical ladders, vaulting buildings, jumping gaps to upper platforms, off-mesh jump planning and matching climb animations are NOT implemented. New backlog `NAV-JUMP-01` tracks them; `NAV-ACCESS-01` tracks discovering/connecting distant stair entrances from another room.

### Real source changes and actual limitations

- `85f98dcd493e5472c32560e0d38779c60fd59d5d`: `MobileNPCTravelMemory.cs` stores max 8 personally traversed successful route records with max 36 physical positions each, private start/goal XYZ, success/failure counters; rejects invalid coordinates, >2.6m gaps and trips <3.5m. Route candidates matched only near previously known start (3m) and goal (2m), 3D elevation matched. No shared faction path cache or imported AI model.
- `60a2a9ce5b94e0e3e7de58d91b721e1d9e8147ba`: runtime prefab adds travel memory.
- `c0e284666779793cfada88758ab777cdf4f69351`: `DieServer` discards private route memory like other knowledge, so replacement spawn does not inherit predecessor's routes.
- `fbdc7b67384cea8416e2546f5bd0ceae2c7e8e4d` + `64a3ed6ec3fc71e0693281d5081fb59a4172894b`: `MobileNPCHumanoidMotor` records actual displacement AFTER native `CharacterController.Move`, only commits an observation after final actual 3D destination reached (never speculative A* waypoint). Rejects large unsampled motion or unfinished, overlong route. Before native/local A*, tries matching route with max frequency 1/4s while route-less, verifies EVERY segment against current ground/physical collisions/doors and validates final goal. Stale route retry failure lowers confidence, three failures blacklists until re-learning. Does not reuse old route during visible enemy tactical cover/retreat. Movement always remains physically executed.
- `701a204b91ad0bca8cd3f6f8f1256b6a80ab1641`: `MobileNPCPersistence` current version 6 with per-living-NPC `learnedRoutes`; reading snapshots v1–v5 stays valid and those default to empty new memory. Each NPC only restores its own record, never shares automatically.
- `8f93c27ade1dbca17d5e0e3da7f4d8ee9f23a724`: detailed design `MOBILE_NPC_ROUTE_LEARNING_DESIGN.md` with limitations, required future portal-graph and jump-link actions, tests and CPU bounds.
- `4b383f08c69c32e89a964632a4dc46968168172c`: backlog now contains `LEARN-ROUTE-01` scoped near-ready for learned/replayed physically validated same-start/end journeys; `NAV-JUMP-01` not implemented; `NAV-ACCESS-01` still lacks spatial portal discovery; `LEARN-05` and global CORE-04 still open. SAVE-01 v6 updated.

### Validity/performance

This is **episodic route cache / first-hand outcome-based memory**, distinct from contextual bandit tactical decision learning. No external ML package is required, and no path is considered successful until agent truly traverses it. Route memory is per individual and persisted only while alive; detailed geometry is NOT automatically transmitted over voice/radio (LEARN-03 remains incomplete). Revalidating up to 36 steps still costs physics calls; CPU savings versus a new native/local A* are **hypotheses**, not benchmarked. Reusing only near matching start/goal does not let an NPC discover staircase 30m away; requires `NAV-ACCESS-01` portal access graph with observed entrances/exits, global physical routing and per-map consistency.

No Unity compilation or gameplay test performed; static GitHub source structure confirmed only. Final tests added to `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md`, section 26.

### Next priorities

Continue `NAV-ACCESS-01`: create safe map-scoped stair/door portal/entrance graph from actual confirmed traversals (do NOT infer connectors from mere stacked floors), support goal across rooms with real path to remembered portal. Or `NAV-JUMP-01` with physics-verified landing and native animation. Note `UI-01` nameplate/dialogue/ESC visible regression still pending and `RAG-01` joint ragdoll unresolved. The full diagram remains an end-of-project deliverable.


**Travel memory correctness follow-up `e89e6580f0f5bb4c3257bf973e1d7389e2a67737`:** reset the active remembered route index when the cached waypoints finish; the final tactical destination is still checked separately in 3D. In `RecoverFromUnexpectedFall`, clear the active observed journey and route-use index before resetting the agent's Transform to safe ground. Emergency physics recovery / teleport is NEVER counted as physical walking knowledge. No compile/run done.


## 2026-10-13 — LATEST NAV-ACCESS-01: discovered stair entrance from another room

**Mandatory in next session:** read this HANDOFF and `MOBILE_NPC_PENDING_TASKS.md` first, then `MOBILE_NPC_KNOWN_ACCESS_NAVIGATION_DESIGN.md` plus `MOBILE_NPC_ROUTE_LEARNING_DESIGN.md` if editing navigation. Work only on `feature/npc-ai-foundation`. User compiles/tests Unity Dev Build **at end**, not per step. All statuses source-only until feedback; preserve task IDs and complete AI architecture diagram only at the end.

### Root cause addressed

`MobileNPCTravelMemory.TryFindCandidateServer` only matches current actor near the learned **journey start** (3m). A survivor who had physically discovered an upstairs staircase couldn't reuse that information from another room 10m away. Existing local 9×9×2 floor A* only operates near actor/final target; no composed route via *known stair entry*.

### New source work

- `6c3f3eb2bad7c1b38e38d5e5b510f0a6380ddf0f`: new `MobileNPCTravelMemory.TryFindKnownFloorAccessServer(currentPosition, finalDestination, output, out entrance, out routeIndex)`. Checks only *personal* real traveled routes (max 8), whose observed start floor matches actor, observed end floor matches target, full journey spans >=1.2m Y and exit is <=1.9m from current goal. Entrance must be 2.5–25m from NPC; candidate score uses individual successes/stale failures. Does not infer vertical links or reverse unseen paths.
- `2d5c2936454c751e5f0bf80ab2b619f22d9d2785`: motor probes learned access before asking native A* straight to final upper-storey goal. Additional scratch buffers/interval and pending-state isolated per actor.
- `1ff9371fba2bd04c2c16577ba44a5ea198b7ed5a`: motor `TryBuildKnownFloorAccessRoute` joins **real approach path** to stair entry and **revalidated personally recorded stair route**. When <=5m from observed entry, exact-goal local 9×9 A* must find path. Farther away (<=25m) requires native async A* path to same entrance if A* backend is actually present; no assumed graph availability or fake wall passage. Before accepting, checks each approach/stair/final segment with `IsActuallyWalkableStairConnection` (ground, slope, head/body collision, valid native unlocked door where appropriate), limits native path length and waypoint count. NPC then walks composed route through CharacterController; brain's actual destination never replaced by entrance, so reaching portal is not success.
- `a2e2d028e0554a4161d376ccaf54349a8416104a`: when native entrance request is pending **or already completed but unconsumed**, prevent competing request for upstairs final goal until the entrance callback is consumed. This avoids native provider silently discarding a ready route due to requested-goal mismatch.
- `a4bcc461e2f794532648a98055d0d2fc8f735807`: new `MOBILE_NPC_KNOWN_ACCESS_NAVIGATION_DESIGN.md` with the actual limited approach+one-connector architecture and rigorous final tests.
- `fdf749c41ffe637532665cd8b58b2d1c544191ce`: canonical backlog updated; NAV-ACCESS-01 remains **EM IMPLEMENTAÇÃO**, CORE-04 open.

### IMPORTANT: not a full portal graph!

The implementation handles **one** individually experienced floor-changing journey whose EXIT is near the user's final goal. It still cannot stitch two or more remembered stairways, find the nearest staircase across an entire POI, route 20m beyond a learned landing on a different floor, assume a staircase in reverse direction, or navigate far from entrance without real native A* backend. No building-wide navmesh/connectivity has been authored. If no confirmed viable route to entry exists, fall back rather than teleport. The memory is private (death clears, v6 snapshot of same living survivor), not automatically transmitted to other faction members. No planned jump/vault/ladder links.

No C# compile, no Unity map playtest, no native A* runtime log or Profiler evidence this round. Final test matrix `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 27. **Next scope** likely room-to-stair-to-upper-room continuation as a second explicitly verified route stage, plus multiportal selection with bounded CPU. Must not claim globally reliable urban combat navigation.


**Stair entry extraction fix `24ba6037f00907978a4c468ddec626ee7532a02d`:** `MobileNPCTravelMemory.TryFindKnownFloorAccessServer` derives an entry step from the personally recorded pre-rise / pre-descent footsteps, rather than using the arbitrary start of the original completed journey (which might be a room 10m before the staircase). It returns only that actually observed stair suffix. This does not infer any connector or permit reverse travel without prior experience. The motor composes a physically computed path to that observed step plus the verified suffix. Source-only, not tested in Unity. Previous doc `MOBILE_NPC_KNOWN_ACCESS_NAVIGATION_DESIGN.md` now includes this refinement. Portal graphs/multiple stairways and target far beyond exit remain open.


## 2026-10-13 — LATEST NAV-ACCESS-01 AFTER INTERRUPTED TURN: CHAINED FIRST-HAND ROUTES

**Begin next round** with `MOBILE_NPC_PENDING_TASKS.md`, this HANDOFF and `MOBILE_NPC_KNOWN_ACCESS_NAVIGATION_DESIGN.md`. All code belongs to feature branch `feature/npc-ai-foundation`; user compiles and tests only in final Unity Dev Build. Never remove pending IDs or claim runtime completion before feedback. Final full AI flow diagram is still deferred until all features are structured.

### State recovered instead of recreated

Upon resuming the interrupted request, GitHub already contained prior source commits for `NAV-ACCESS-01`: `MobileNPCTravelMemory.TryFindKnownFloorAccessServer` selected a personally observed stair *entrance* (derived from real elevation-changing footsteps), and `MobileNPCHumanoidMotor.TryBuildKnownFloorAccessRoute` physically planned an approach (local A* <=5m, optional native async A* out to <=25m), concatenated and verified the learned stair suffix. `MOBILE_NPC_KNOWN_ACCESS_NAVIGATION_DESIGN.md` also already existed. Prior implementation only worked when final upstairs goal lay within 1.9m of the remembered stair exit. **Do not duplicate those implementations.**

### New GitHub commits this resumed round

1. `d40634ff04ba3494be68647d1f050ba3a4c979fa`: extends the *personal* `MobileNPCTravelMemory` access selector to find up to one separate, fully walked **same-floor** continuation from within 2m of a learned stair exit to a new end destination within 1.9m. It checks floor matching, original route evidence and failure confidence, and returns both records. Neither new geometry nor reverse links are inferred.
2. `3ce89c4b6f0694e9b6396b0aa6a32508c601e72a`: motor composes actual approach path + remembered stair suffix + learned upstairs corridor + final goal, while maintaining brain's full 3D destination. Every stage boundary and segment revalidates real ground, head/body collision, physical native doors and map bounds. Maximum combined approach/stair/corridor waypoint count 104; continuation must have >=3 firsthand points.
3. `af7681ee710ec2e28ee85a5def864404a3799c52`: clear BOTH learned route references on goal change, no destination, waypoint completion, terrain-fall safety recovery or when a different route is used. Prevent unrelated stale-path penalties.
4. `8ae284d1439b18150f9c0c4a2ada2c01c74474d8`: retain phase markers (approach, learned stair, observed continuation) for in-progress composed paths. A blocked newly computed approach lowers neither learned record's confidence. A physically blocked actual stair lowers its own confidence; an upstairs corridor failure penalizes only the recorded continuation. Still perform actual movement with CharacterController, never warp.
5. `602a733ac7b536a0da05e324c5f0bc62bff8069b`: technical design records new capacity and final test matrix.
6. `20e9115555da27a7d90417faf8c0d4411e851034`: permanent backlog updated; NAV-ACCESS-01 still EM IMPLEMENTAÇÃO.

### Validation limitations

No Unity compilation, no gameplay tests, no profiler measurements. Actual native graph may be missing; long approach still requires genuine A* availability and connected map graph. Only two linked *personally experienced* journey records (stair + same-floor corridor) are accepted, NOT a generalized city/POI portal graph. Other floors, multi-stair concatenation, dynamic reverse direction, unknown rooms and distant endpoints remain pending. Individual route memory is v6, saved for the SAME survivor and erased on death. Other NPCs do not gain it magically; physical speech/radio route sharing remains unimplemented.

**Next engineering focus:** generalized but CPU-bounded graph of observed stair/door transition nodes, navigation to different upstairs target via new valid local/native path, or P0 dialogue UI/ESC bug. Do not mark CORE-04 or NAV-ACCESS-01 resolved until final Dev Build evidence; follow up on any static/C# signature errors.

## 2026-10-13 — New conversation continuation: route memory restoration validation

Branch `feature/npc-ai-foundation`. Commit `80f91042204dd670e5b7b81fc693f6d5f68ae009`: `MobileNPCTravelMemory.RestoreSnapshotServer` now verifies restored routes meet the same structural and physical-observation sample constraints as newly completed trips: finite/level-safe points, start within 0.35m of first step, final goal within 2m of last step, all consecutive distances 0.01–2.6m and total 3.5–80m. This prevents malformed stored tracks from becoming spurious known stair/corridor connectors on reloading. It does **not** establish world topology or guarantee an old route remains unblocked: runtime motor physics checks are still mandatory. Source committed; **no Unity compilation or Dev Build**. Continue `NAV-ACCESS-01` with evidence-based navigation; retain `NAV-JUMP-01`, `CORE-04`, `UI-01`, `ZMB-01` and all other backlog IDs. User requests testing/build **only after development**, not after each patch.

## Permanent status lifecycle confirmed by user (2026-10-13)

For every future conversation: consider an individual task or clearly scoped subtask **QUASE PRONTA — AGUARDANDO BUILD** after its code is implemented, integrated, and statically reviewed, if ONLY the final Unity Dev Build test remains. Do not confuse source review with compiled/runtime verification. Upon the user's final build feedback, move approved and demonstrated work to **CONCLUÍDA (runtime verificado)** with build/map/results evidence; move failed or adjustment-needing work back to **EM IMPLEMENTAÇÃO** or **EM VALIDAÇÃO** while preserving its ID and failure history. Close individual passing subtasks only; do not falsely close an incomplete parent. Defer test requests until the user chooses the consolidated Dev Build. Always update the canonical backlog and this handoff after edits.

GitHub access diagnostic: read and repository write permissions remain functional, proven by source-controlled commit `fdf1141716fabf8905b03edff385516316a110e5` on feature branch. A preceding set of larger write attempts returned a tool-side security-settings block; its exact underlying rule is not exposed. Small subsequent write succeeded. Do not infer repository permission denial from that error.

2026-10-13 UI fix `617344737077aea9ff30ca18135503e0cc9419f1`: corrected footer layout in `MobileNPCInteractionController.OnGUI` so nine stock buttons do not overlap `Fechar`. Scoped visual overlap is source-reviewed and QUASE PRONTA — AGUARDANDO BUILD; UI-01 parent remains EM IMPLEMENTAÇÃO pending actual HUD/ESC/dialogue validation. Backlog protocol expanded in `09eab0c535657626a6276d8ea1515d940f865c44`. Source-only, not compiled.

## 2026-10-13 — Next development round: close-range known stair access

Source commit `8457dc4dc588ba053f9d3b479caf941fa3b430f7` in `MobileNPCTravelMemory.cs` reduces learned stair entry minimum selection distance from 2.5m to 0.8m. Before it, a survivor could lose eligibility for a confirmed remembered access merely because it was already close to the correct staircase. Still only proposes personally physically walked links; exact local approach and all segment collision/ground checks remain in `MobileNPCHumanoidMotor`; no teleport, no inferred reverse edge. Below 0.8m selector continues to yield to local vertical logic; untested. NAV-ACCESS-01 still EM IMPLEMENTAÇÃO. Scoped fix QUASE PRONTA — AGUARDANDO BUILD; no Unity compilation/runtime performed. Continue testing later with near-threshold 0.7/0.9/2.4/2.6m and alternate blocked stairs. Keep user status protocol intact.

## 2026-10-13 — LATEST, MULTI-ROOM MULTI-ACCESS SOURCE MILESTONE

**READ THIS and canonical backlog first next conversation.** Work on `feature/npc-ai-foundation`, do not edit `main`. Follow user-approved task lifecycle: fully connected/reviewed code awaiting ONLY final Unity Dev Build is **QUASE PRONTA — AGUARDANDO BUILD**; user approval closes scoped item with real evidence, failure reopens it without losing history. No Unity compile, playtest or profiler data was produced this round.

### New functional code, no longer limited to one learned stair + corridor

- `787f4216`: `MobileNPCTravelMemory.TryFindKnownAccessChainServer` searches a **directed personal graph** of up to 8 genuinely completed journey records; up to 4 chained edges with last-to-next observation endpoints within 2m and height-compatible; max 144 historical footstep positions. Access via stair prefix derived from actual elevation footprints for different-floor goals. No inferred reversed traversal or world geometry.
- `f59735e0`, `c7d5bb0`: `MobileNPCHumanoidMotor` now consumes complete chains: local/native physically computed path to observed entrance + each learned stage + final target; verified ground, body clearance, doors, clip volumes and segment limits. Route path uses CharacterController, never warp. Initial approach max25m, <=5m local exact A*, >5m actual native async A*, max32 approach points.
- `1948935a`, `d3bfb9fb`: staged blocked-route attribution, no punishment for separately computed approach, final unobserved link or first unobserved inter-record boundary; 5s per-entry retry pause on invalid path so another observed stair may be chosen. Direct firsthand reuse remains separate.
- `9fab1376`, `cefedf3f`: reused per-NPC graph search arrays, bounded DFS 48 nodes per entry, shared expensive plan admission slot 0.12s and per-instance randomized plan interval ~0.65–0.90s; these are budget limits, NOT profiled performance.
- `2a2c61f6`, `5da6f78e`: same-floor distant-room routes can be composed using previous completed corridor/door segments; cheap local fallback retained <=5m.
- `9b580cb6`: scored hops remain positive even with many repeated successes, avoiding accidental preference for needless multi-hop detours.
- `ad8b859c`, `ff664dea`: an NPC already standing at observed entry no longer drops it due to <0.8m threshold; validates a short actual connection instead of relying on empty same-cell A*.

### Status, tests, next focus

`NAV-ACCESS-01` **QUASE PRONTA — AGUARDANDO BUILD** for the explicitly bounded learned multi-access and same-floor rooms network. This is a SOURCE milestone, not a runtime-success claim. `CORE-04` broad universal movement, `NAV-STAIR-01` complex geometry, `NAV-JUMP-01` ladders/offmesh jumps and `PERF-01` measured 40-NPC performance remain open. Backend of native A* may be missing from public SDK; far-room entrance navigation >5m is never guaranteed without a real provider. No unseen portals, reverse links or global architectural map magically learned.

Docs updated: `MOBILE_NPC_KNOWN_ACCESS_NAVIGATION_DESIGN.md`, canonical backlog `MOBILE_NPC_PENDING_TASKS.md`, `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 31. In final Dev Build verify chained same-floor corridors, two staircases, alt entries after blocking first, corner avoidance, no geometry bypass, death/restore, native provider and 20/40 NPC GC/CPU. On user approval close NAV-ACCESS-01 scoped implementation; on errors reopen with reproduction and logs.

**Next recommended development focus:** NAV-STAIR-01 broader stair geometries or P0 CORE-02/03 damage/death and UI-01 user-visible dialogue; do not overclaim entire CORE-04 solved because one bounded memory graph is built. Preserve full AI decision diagram delivery when other modules complete.

**Follow-up source fix `1f6ae7f7766ec2422742b24b2e35b9d72d0919c0`:** native async approach to a personally observed access now pauses planar walking while the route is pending and suppresses competing direct goal detour; formerly the motor could push into a wall while waiting for the path result. Stuck recovery is fed `wantedToMove=false` in this period. Validate in final Dev Build that brain/combat remain responsive and movement resumes on route readiness; do not claim runtime success.

## 2026-10-13 — LATEST NAVIGATION ROUND, before jump planning

**NEXT CONVERSATION START HERE** and read `MOBILE_NPC_PENDING_TASKS.md`, `MOBILE_NPC_NAVIGATION_RECOVERY_DESIGN.md`, `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 32. Work on **`feature/npc-ai-foundation`**; do not edit main. User will compile/Dev Build test at the consolidated final development stage, not between each source round. Preserve stable task IDs and the rule that source-integrated subitems requiring only build tests are **QUASE PRONTA — AGUARDANDO BUILD**, but runtime/compilation must never be claimed until user evidence.

**Functional source changes recorded this round:**

1. `7facf6205ad35b6b9b6fc249c88c6bb75c21d624` in `MobileNPCHumanoidMotor.cs`: per-survivor bounded, ephemeral (6 samples/18s) evidence of actually failed movement near a goal, recorded by stuck recovery. Avoid marking unrelated new goals as blocked.
2. `4968e1458ae1657894c9be765cc8e3ef95780b1a`: unknown-area 9×9 local A* can prefer reachable physical frontier/corner alternatives when target is behind a wall; shared expensive local-plan admission once per ~0.10s, per-NPC randomized cooldown. This is iterative reachable-ground exploration, NOT a complete authored building portal graph.
3. `d5cedb6869905884079679b49be57d7dbd085129`: do not stop search at an actually observed dead-end preferred cell; temporary Player/Zombie/NPC bodies are not permanent obstacles in proposed graph (actual movement still collides).
4. `a6ce9d3b1a94eb663744228b7be97357934cea58`: reject local A* edges without real intermediate ground, cap at 220 midpoint floor probes per search. Never treat grounded endpoint samples as guaranteed continuous bridges.
5. `8856f815d88729cab7916430beb4e9cd3ce28d2d`: nearby vertical A* retries half-size 0.625m X/Z step when full 1.25m grid cannot find actual stair treads within 2.5m goal, retaining physical collision/height and per-pass budget; real narrow/winding stair correctness remains unverified.
6. `fde0be8e909cb45359883f3c828db76d8d14894f` in `MobileNPCNativeAStarRouteProvider.cs`: optional genuine ASPFP response supports both reflection public fields and properties for `Path.error` and `Path.vectorPath`; prior implementation only reflected properties and could silently reject valid routes. Reject nonfinite/out-of-map endpoints and >128 waypoints, partial path endpoints >2m XY/3D from goal / >0.85m Y difference.
7. `36d647ef006d3ba552e6a419687a213a7dbb9d94`: reject stale native route start more than 4m from NPC or wrong elevation, and prevent a different nearby entrance/wrong floor from inheriting a ready/pending result (<=1.5m goal + <=0.65m height tolerance). Native backend may not exist in public SDK; this only repairs the optional adapter.

**Statuses documented in backlog:** `NAV-UNKNOWN-01` and `NAV-NATIVE-01` new scoped subitems **QUASE PRONTA — AGUARDANDO BUILD**, with zero Unity runtime proof; narrow stair grid fallback likewise source-reviewed, but parent `NAV-STAIR-01` remains open for complex stairs; `NAV-ACCESS-01` existing personally learned four-route graph still QUASE PRONTA. **CORE-04 remains EM IMPLEMENTAÇÃO**: truly arbitrary never-visited multiroom floorplans are not guaranteed without an actual available native graph or authored map connectors, planned vault/jump/ladder remains `NAV-JUMP-01`, performance and cross-map data missing. Never call global navigation finished based on this work.

**Documentation:** `MOBILE_NPC_PENDING_TASKS.md`, `MOBILE_NPC_NAVIGATION_RECOVERY_DESIGN.md`, `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 32 updated this round. Test unknown-area corners, temporary body at doorway, locked doors, gaps, alternate entries, 0.625m stairs, ASPFP present/absent, failure/async changes and 20/40 NPC performance at END. Next source-focused engineering task by user request: `NAV-JUMP-01` physically planned low vault/jump/landing and ladders if game animation/physics supports; don't interpret the existing reactive low-obstacle hop as a planned off-mesh connection. No source compilation or in-game tests occurred.

**Final native-route overload refinement this round:** commit `6e59544a56aeca7e0e2649031d00d005efc559a0` chooses exact 3-argument `Seeker.StartPath(start,end,callback)` reflection overload, rather than possibly selecting a 4-arg graph-mask signature and sending default(struct) 0. Official Aron Granberg StartPath docs establish both overloads; only 3-arg avoids silent zero-graph mask. No runtime backend verified. NAV-NATIVE-01 scoped source QUASE PRONTA — AGUARDANDO BUILD. Next requested development round: NAV-JUMP-01 planned jumps / vaults / ladders; do not mark CORE-04 globally closed without map/playtest.

## 2026-10-13 — LATEST NAV-JUMP-01 planned low-obstacle hop source round

**Next conversation:** read this section, `MOBILE_NPC_PENDING_TASKS.md`, `MOBILE_NPC_PLANNED_JUMP_DESIGN.md` and `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 33 before modifying code. Branch `feature/npc-ai-foundation`. No Unity compilation, Dev Build playtest, or physics profiler measurements done. The user's permanent lifecycle remains: fully reviewed source scope waiting ONLY for the final build = **QUASE PRONTA — AGUARDANDO BUILD**; user-approved tested scope = concluded; user-reported failures reopen with evidence and preserve all IDs. Do not test after every source round.

**Actual GitHub changes:**

- `b92feff1f83dcfb538cc27f9f2bee3e1345a6957`: explicit planned-jump state and bounded configuration plus suppression of airborne route memory.
- `a1892748e360888dd5ac019d0569c26a0986d824`: new `TryBeginPlannedLowJump` ballistic preflight: real static low obstruction (exclude dynamic bodies/registered doors), valid landing and height difference, calculate gravity-consistent impulse, swept full-body capsule path against obstacle colliders; no Transform warp, no unverified vault.
- `04ccd8b0dc5270a50beffd5a2d0d7373427e8260`: planned hop drives CharacterController over short airborne phase instead of old `ShouldJumpOverLowObstacle` blind impulse; goal changes, timeout and native grounded landing are checked.
- `03d4724adc206d7f8777cb0a59cf8785c4cabed9`: full trajectory checks spread by per-NPC 0.75s and global 0.10s planning admission; unused old reactive eligibility method removed.
- `b3c9080c1de47e3333417c6452ae86db3c43409c`: visual layer only attempts a clip whose runtime rig contains `Jump` / `Leap`; suppress airborne footsteps. Imported Human/Basic animation metadata currently confirms `Move_Climb`, `Idle_Climb` but NOT a jump/vault clip. Do not assert native clip exists.
- `ba9c4b36286c549d6d9d2d7207dd8d05f8f23e30`: prevent airborne (and post-jump settling) movement from being committed as first-hand walkable corridor memory; block leg-injured jumps because speed assumptions would invalidate preflight; suppress midair server zombie footstep alerts.
- `6571aaa57183de74347432eae30ae9ef1b75cb1a`: water entry or genuine airborne side collision aborts jump without colliding through the wall; keep vertical gravity physics.

**Status:** limited low static obstacle **planned ballistic hop** is source integrated and **QUASE PRONTA — AGUARDANDO BUILD** subitem, not yet real playtest. Parent **NAV-JUMP-01 EM IMPLEMENTAÇÃO**: A* authored off-mesh jump links, vault with true pose, multi-gap crossing, ladders/scale vertical, native NPC energy/stamina resource, and performance still missing. `CORE-04` and `NAV-STAIR-01` remain open. The user's next likely focus is actual ladder/vertical-climb adapter and offmesh edge selection; do not use `Move_Climb` alone to teleport up ladders. Final tests documented section 33; all unexecuted.

Project docs updated: `MOBILE_NPC_PLANNED_JUMP_DESIGN.md`, permanent backlog and test plan. Ensure next round continues at the exact branch tip and updates both docs again.

**NAV-JUMP-01 follow-up** commit `489ed7641dad85a9edda9255f05f13e334952900`: rejects native InteractableVehicle colliders and disallows discretionary airborne crossing while defending under visible hostile SeekCover or Retreat; unused old jumpImpulse serialized field removed. Documentation `MOBILE_NPC_PLANNED_JUMP_DESIGN.md` updated. Only scoped physically planned low static obstacle hop near-ready source; graph-authored offmesh, jump animations and ladders remain open; no Unity compilation.

**New retained backlog sub-IDs `630c9de07f68804a5b6e4695bd3bfc331618cae4`:** `NAV-VAULT-01` (genuine ledge vault including validated pose; NOT INVESTIGATED) and `NAV-LADDER-01` (native ladder entry/climb/exit with physically valid movement; clip names Idle_Climb and Move_Climb observed, but physics not implemented). These are NOT covered by the current bounded ballistic low-obstacle hop. Next round should inspect Unturned ladder native API and game-authored collider/animation setup before writing ladder movement code. Do not infer climbing merely from imported clip names.

## 2026-10-13 — LATEST user-requested continuation: native Ladder traversal

**Always begin future conversation reading `MOBILE_NPC_PENDING_TASKS.md`, this handoff, `MOBILE_NPC_NATIVE_LADDER_DESIGN.md`, `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 34.** Branch remains `feature/npc-ai-foundation` in private `pplopes2003-design/Unturned-Better`. User tests/compiles only in consolidated Dev Build. No Unity compilation, gameplay, logs or profiler were performed this round. User's state workflow: source-complete scoped subitem is QUASE PRONTA — AGUARDANDO BUILD, on approved gameplay CONCLUÍDA, errors reopen; keep stable IDs.

**Verified actual SDK interfaces**: `InteractableLadder.cs` recognizes `CompareTag("Ladder")` on `RayMasks.LADDER_INTERACT`, front/back normal aligned with `collider.transform.up` magnitude >=0.9, near upright world alignment <=0.1, real capsule clearance/LOS. `PlayerStance.ReceiveClimbRequest` ATTACHES THE PLAYER BY CONTROLLED TELEPORT; cannot reuse this for NPCs as if it were physically climbed. `PlayerStance.simulate` checks ladder contact about 0.75m in front while `PlayerMovement` applies climb velocity. Rig importer contains names `Move_Climb`, `Idle_Climb`; runtime NPC_Client binding unconfirmed.

**Source commits**:
- `f79fe8ed2d7375d2e6b6d87d060cd000de40bf31` — new `MobileNPCLadderTraversal.cs`: limited near (<1.7m) native ladder approach/ascend/descend/exit controller. Requires valid real ladder tag/mask/orientation and endpoint entry, real grounded exit close to goal, active same collider, timeout and contact checking; yields velocity only, never sets transform.position.
- `1a8eada0901d25b48f1df89dac6cc3e687196e96` — `MobileNPCHumanoidMotor.cs` attaches action component, selects it for goal on another floor and genuinely nearby ladder, suppresses competing A*/steering/gravity, retains original brain goal, performs sole `CharacterController.Move`, prevents ladder from being learned as a walkable corridor and suppresses fake stuck events.
- `86742f34580e02a8506ed9e17b5dd591b68b6213`, `8cc4479883dd041ee15216048e03f3de89b6aed3`, `bdc3f05fa050b38fa9b50778c813cf7b084e2a1d`, `2702ad0d4fa3a40cf0dd2a835a8eb1bb462b0702`: physical midpoint/ground + capsule LOS verification of approach, live collider checks, progressive climb contact and grounded exit, ray for upper descending starts, actual roaming ground mask, bounded retry. No free vertical shortcut.
- `3cdb54bd45f4b56c00047fd820905708002766a5`: `MobileNPCVisualAnimation` plays real runtime `Move_Climb` or `Idle_Climb` if loaded; suppress footstep clips; logs once if missing.
- `c6e450a9fb8976172bc6c045513c2213bb92bcd1`, `5891245940b852b54f3abd5e267a921dd6a1f41e`: melee and gunfire paused when hands are physically climbing.
- `08d5522fe0fe3175841babf8f74410410a630209`: server-side footsteps/zombie alerts suppressed while ladder movement executes.
- `03745c929bb319d536cbe93fb1e4d88f1daa0ab5`: technical design `MOBILE_NPC_NATIVE_LADDER_DESIGN.md`.
- `39428d06cd9621851c67cd3dbcfc2615a8031f22`: canonical backlog new subtask `NAV-LADDER-NEAR-01` **QUASE PRONTA — AGUARDANDO BUILD**. Parent `NAV-LADDER-01`, `NAV-JUMP-01`, `CORE-04` all **EM IMPLEMENTAÇÃO** for unavailable distant map-wide ladder links, segment ladders/edge cases, missing general vault/stamina/navigation and unproven runtime performance.
- `06c67ba95b74868044d687ea88ff2390df099adb`: final test plan section 34.

**Strict limitations / future focus:** This is a grounded nearby first-ladder traversal action and NOT a full A* offmesh link graph from a distant room; it may reject awkward top/bottom geometry, multi-collider ladders or platforms. A physical ladder cannot be fabricated by unverified observations. Unity runtime prefab, masks, actual collider bounds, physics skin width, climb animation, goal reach after exit and profiling require final user Dev Build. Next source development: connect verified native ladder opportunities to path graph as directed off-mesh links; separately `NAV-VAULT-01` true vault with real animation (not just ballistic jump). Continue to update canonical docs every round and never claim full navigation solved until runtime evidence.

**Ladder entry safety follow-up `0aefbef92c30044f64c9085ddffed3cca08a5ca8`:** the motor now rejects initiating ladder traversal when physically crouched or severely movement-impaired; these states cannot bypass intended full-body ladder entry/collision. Existing native clips and safe exit checks unchanged. Scoped NAV-LADDER-NEAR-01 remains QUASE PRONTA — AGUARDANDO BUILD; no Unity compile or game test.

**Late physics review fix `420b27ace8ae02b040b7bccb877b6fe8344ae8c9`:** the optional ladder controller's Approach phase yields horizontal movement but MUST NOT disable gravity; motor now keeps normal velocity.y until the verified Climb/Exit phases. Without this, an NPC could approach on an air-supported horizontal path. Still uncompiled, no Unity runtime evidence.

## 2026-10-13 — LATEST: authentic Ladder entry A* and restricted parapet clamber

**Next conversation entry point:** read `MOBILE_NPC_PENDING_TASKS.md`, `MOBILE_NPC_PROJECT_HANDOFF.md`, `MOBILE_NPC_NATIVE_LADDER_DESIGN.md`, new `MOBILE_NPC_VAULT_DESIGN.md`, and `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` sections 35–36. Repository `pplopes2003-design/Unturned-Better`, ONLY `feature/npc-ai-foundation` branch. No Unity build, gameplay test or Profiler was run. User consolidates tests at the very end. Preserve statuses: scoped completed source alone = **QUASE PRONTA — AGUARDANDO BUILD**; only user Dev Build evidence can close it, otherwise reopen with logs; parent never auto-closes.

**First critical fix:** commit `5fd7cae137842cf42b2305873609856017cd84f0` deletes three rogue `activeAccessRouteIds.Clear()` and waypoint reset statements accidentally inserted between CLASS FIELD declarations in `MobileNPCHumanoidMotor.cs` during an older replacement. This was a real compile blocker missed by brace-only inspection. Static depth scan repeated (no function calls directly at class scope), but Unity compile still pending.

**New directed nearby Ladder entry:**
- `1a224958754192daed430fb8a961ea34e47e75cf` in `MobileNPCLadderTraversal.cs`: survey up to 12m, fixed collider buffer of enabled real Unturned tag `Ladder` with `RayMasks.LADDER_INTERACT`, face orientation checks, BOTH sides, authentic entry floor, true real exit near intended opposite floor, capsule clearance and contact. `df45d6d70be75285f4e7c416b96038da545f0b8a` expands bounded candidate buffer to 128 for dense indoor POIs with many colliders. No false mapping.
- `0d1eedabcd6a49168e1ebbeb8d3e6635a9a384aa` in `MobileNPCHumanoidMotor.cs`: use exact bounded local 9×9 A* for entry <=5m, **optional real async native ASPFP A*** for roughly 5–11m. Validate end connector <=1.25m and preserve actual brain destination. Waypoints end at verified entry but NPC must freshly recognize/collide with the same type of real ladder to climb. No teleport or magic A* vertical edge; remote ladders/multi-hop links remain open.
- `01beb6a6395ee926afab6c0c7d23558899190bd2`, `fc1e8f5e3fd0d7fa43af952480656a96c62e24ab`, `dbaa79da315bddfb9449c6bb46ebb5c328edaa00`, `1bb651f8806e868ec515efc7ca63717963b7b12d`: pending exact Ladder entry path gets priority over generic native A*, learned-access native A*, fallback detour, blind steering and jumps; protects actual asynchronous request until it finishes or expires.
- **Classification** `NAV-LADDER-LINK-01` **QUASE PRONTA — AGUARDANDO BUILD** (code-level planned physical entry), parent `NAV-LADDER-01` **EM IMPLEMENTAÇÃO** (global native offmesh graph, multiple colliders, unusual top exits, no backend guarantee). No real backend verification.

**New physical parapet clamber, NOT fabricated Vault animation:**
- `256609a567924d9c2c990359a43d9139d4f11fab`: new `MobileNPCVaultTraversal.cs` three phase Rise/Across/Land, only STATIC wall top actually collided at 0.70–1.10m, verified exit ground on same floor, body-volume and swept collision checks; mover produces velocities, motor exclusively calls CharacterController.Move. No Transform warp, no rooftop access, no fake walk memory.
- `19decb269788e977bb699eb708e5552c46217929` and `c606a7eb3b70081fb204230a3ea7542717644795`: wire action into motor with injury/cover/stance guard, preserve original AI goal, prevent conflicting jump/ladder/navigation, restore real gravity during descent and abort on blocked progress/goal changed/water. Backlog records `NAV-VAULT-CLAMBER-01` **QUASE PRONTA — AGUARDANDO BUILD** bounded source only.
- `7d0bd4a0a49bd4fb5bd6b2f18ee54664d93acf9a`, `32926fcbd656ba281ccd0d086e4f3efcba4acde1`, `e3c1904fcca3f7925baa8c23e1202477ee85825f`, `86730adc0a2a845b10e294949d98a7d89b0195ca`, `554747482c2f48a13e739c2a90eb505519c69aaf`, `ec38f6cc186df88993e3df41abb52fd9fdad65a2`: visual tries native runtime `Move_Climb`/`Idle_Climb` (not a dedicated Vault clip), attack/fire/footstep suppression during entire clamber including landing. New `MOBILE_NPC_VAULT_DESIGN.md` records rig limitations. Parent `NAV-VAULT-01` remains **EM IMPLEMENTAÇÃO**, dedicated animation/IK and global vault offmesh links absent.
- Unity MonoImporter `.cs.meta` files added for BOTH new components (`MobileNPCLadderTraversal.cs`, `MobileNPCVaultTraversal.cs`).

**Permanent docs:** backlog `87a17175af2520ef10b193d1397227402affa13f` new sub IDs; test matrix sections 35 & 36 `b56e5fe0d1a82ae74befa234dc59e9357733823f`; design `MOBILE_NPC_NATIVE_LADDER_DESIGN.md` and `MOBILE_NPC_VAULT_DESIGN.md` refreshed. DO NOT claim full CORE-04 solved. All code uncompiled and untested in Unity. Recommended next work: inspect native rig/asset for true Vault pose or author verified alternate movement with hand IK, plus broader generic A* graph link metadata and careful cross-module compile/static audit. User has repeatedly flagged broken NPC combat/death/interaction; P0 checks remain pending and should eventually be prioritized before final Dev Build.

## 2026-10-13 — LATEST CORRECTION: USER-APPROVED STANDARD UNTURNED JUMP FOR VAULT

**IMPORTANT, supersedes the previous custom vaulted-clamber instructions.** The user explicitly states vault does NOT need its own animation; use the normal Unturned jump or a series of separately landed normal jumps over short parapets. There is no need to generate a bespoke Vault rig/clip/IK, and it is no longer a prerequisite for closing `NAV-VAULT-01`. True ladder traversal remains distinct and may use `Move_Climb`/`Idle_Climb` on a real `Ladder` collider.

**Real native code check:** `PlayerMovement.cs` gives basic JUMP impulse 7.0 with a Parkour mastery multiplier up to +25% (8.75 upper bound); the mobile NPC motor already uses a 3x-adjusted gravity acceleration. We now use these as real physical bounds, not an arbitrary forced vertical movement animation.

**Code actually changed in GitHub, `feature/npc-ai-foundation`:**
- `6d3e969febbf20c4713bff722f805083c2083c2f`: `MobileNPCVaultTraversal.cs` is no longer a three-phase scripted movement controller; now a **bounded, collision-validated ballistic feasibility planner**. It checks authentic static 0.65–1.02m horizontal-topped parapet, real ~1.65m same-floor safe landing, actual gravity, native JUMP=7 / up to 8.75, delayed forward travel only to clear top, swept full-body flight with max 24 fixed samples. Rejects doors, players, other bots, vehicles, moving rigidbodies, ceilings, insufficient jump apex, missing floor; no artificial levitation or teleport.
- `92b6b7c811e1cf1b1031963622b0a76c46f2462b`: `MobileNPCHumanoidMotor` now starts **both** low hops and higher wall jumps using the SAME existing gravity-driven `isPlannedJumping` state and `CharacterController.Move`. Clears old artificial `IsVaultTraversing` motion phases, delayed horizontal movement while rising, source-side cooldown + truly grounded state before any successive physical jumps; path memory never stores airborne arc as a walkable corridor.
- `a53bda295f1a2cd2055c41391c572193a5eed99b`: `MobileNPCVisualAnimation` no longer uses `Move_Climb` for vault hops. `IsJumping` tries a genuine Jump/Leap animation only if actually loaded; otherwise preserves a real existing locomotion run/walk pose without airborne footsteps. **No dedicated animation generated**.
- `9c3838490535b8a804aa84c1547473df7921969b`: prevents the ground-failure emergency recovery from teleporting a genuinely airborne jumping NPC; allows another cooldown- and ground-gated hop instead of requiring 2.5sec between successive parapets.
- **CRITICAL REAL LOGIC FIX** `b526f71750c661848a69247a35d102eef20ddbea`: prior short hop computed impulse from time-to-cross width and could produce below JUMP=7, making feasible low jumps fail. It now uses native JUMP=7 and solves duration from gravity/landing height; swept arc samples keep horizontal position from exceeding known landing.
- `da0b7b4f5ffbd08bc707aa50f381571d19e93009` rewrote `MOBILE_NPC_VAULT_DESIGN.md` to supersede the obsolete scripted clamber description. `3a68bd53e3bbb4bf84a7eaad02a2c00a74453029` updated canonical backlog status/history. `5a24d66fc3061e59294152f2ea24210f989a2be5` rewrote final Unity test plan section 36 for actual jump and multiple separate jumps; `3b3c8c16961ac85c221cd17120f244f34d68d810` updated `MOBILE_NPC_PLANNED_JUMP_DESIGN.md`.

**Status:** historical ID `NAV-VAULT-CLAMBER-01` kept only for traceability, with source now **QUASE PRONTA — AGUARDANDO BUILD** for standard physically jumped parapet. No dedicated Vault animation is required or listed as an outstanding blocker. `NAV-VAULT-01`, `NAV-JUMP-01`, `CORE-04` remain **EM IMPLEMENTAÇÃO** only for broad map/goal coverage, optional global A* offmesh links and actual user Unity Dev Build validation. Keep all previously reported bot issues (combat, death, interaction, multi-NPC radio/zombie pursuit) in backlog. **NO Unity C# compile, playtest or profiler data has been produced**; never call it concluded without explicit user feedback.

**Next work:** source-level integration audit, stale fallback route/movement edge cases and P0 real damage/death/UI improvements before consolidated Dev Build, or more global navigation if user prioritizes it. Do not regress to custom vault Rise/Across/Land or ask for a bespoke Vault animation again.

## 2026-10-13 — LATEST: NAV-OBSTACLE-CHOICE-01 physically grounded door/walk/jump decisions

**Next conversation START HERE**, then read canonical `MOBILE_NPC_PENDING_TASKS.md`, `MOBILE_NPC_OBSTACLE_DECISION_DESIGN.md`, `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 37, and latest vault/ladder docs. GitHub repository `pplopes2003-design/Unturned-Better` branch `feature/npc-ai-foundation`. Do not edit main. User's permanent status lifecycle: fully connected source implementation waiting for Dev Build = **QUASE PRONTA — AGUARDANDO BUILD**, after user runtime approval can become CONCLUÍDA, errors revert to in-progress with evidence. No Unity compilation, playtest or Profiler evidence was produced here.

**Concrete source work and commit SHAs:**
- `ce26eeb8031a5694554f7e224e5fe680833c97cc`: IMPORTANT actual player physics mismatch corrected in `MobileNPCHumanoidMotor.cs`. Previous serialized `gravity=20` was still multiplied as `Physics.gravity*(20/9.81)*3` (~60m/s²) while native `PlayerMovement.cs` uses `Physics.gravity.y * 3` under normal air gravity; made JUMP=7 physically inconsistent and broke ordinary falling. Renamed new field to `gravityMultiplier=1` (old Unity serialized prefab `gravity` value must not carry forward), bounded scale 0.5–2; both motor vertical physics and low/high jump planners now use the same multiplier. Physics runtime/other fall effects must be tested, not assumed.
- `4c1a1bf5cc6b182bf7a6346af3df4cc807aa628c`: added `ShouldChooseObstacleJumpServer`, which checks physical nearby static obstruction toward same-floor brain goal, excludes characters/vehicles/doors/rigidbodies, prioritizes native unlocked-door traversal and actual short walk route with thresholds <=direct+3m or <=1.55× direct. A partially reached frontier is not goal evidence; attempt physically walked route first, reconsider after >=2 stuck recoveries. With no route, allow ~1s normal path discovery. Bounded per-NPC 2.5s and shared 0.22s extra **exact** local A* route-comparison checks for goals within 5m. A longer or repeatedly blocked path can authorize subsequent actual jumping FEASIBILITY check; does not launch a jump merely from heuristic cost.
- `06a16b5d4a24497d38c51cf06885c0f8e5b9e6f4`: motor's grounded hop/vault initiation uses that policy and the direct goal direction. On missed genuine landing or midair side collision, record personal failed approach and suppress new jump attempt for 6s to encourage alternate ground route.
- `f1ac8162efb12a0ddb082a1788fa80d378bf7200`: shared `ClearWalkingPlanForVerifiedJump` invalidates only speculative ground waypoints after actually successful preflight, preserves lived memory and final AI destination. No discretionary jumps for SeekCover/Retreat even if target temporarily out of sight.
- `55b5578d93e068b6f32bd6370c6e120d3224a353`: new detailed `MOBILE_NPC_OBSTACLE_DECISION_DESIGN.md` with philosophy, thresholds and limits.
- `2674fda8d66702e61942f0ef9ae34a1e2fe72479`: canonical `MOBILE_NPC_PENDING_TASKS.md` new scoped `NAV-OBSTACLE-CHOICE-01` **QUASE PRONTA — AGUARDANDO BUILD**, parent `CORE-04` **EM IMPLEMENTAÇÃO** (global multi-map and backend unverified).
- `af494f9ea06edd4e22d20d56b3c82f172072fd84`: test plan section 37 with gravity, unlocked/locked doors, short vs long bypass, unknown partial route, off-floor target, failed hop cooldown, defensive cover and 40-NPC performance.

**Important prior user preference:** vault = normal Unturned jump or separate successive actual grounded jumps, NOT a dedicated Vault animation. `PlayerMovement.JUMP=7`, Parkour upper bound 8.75 if motor has suitable physical route; `Move_Climb` remains exclusive to genuine Ladder colliders. Do not regress to old clamber Rise/Across/Land or magic roof access.

**Status / next work:** `NAV-OBSTACLE-CHOICE-01` is source-complete in bounded scope, awaiting final Unity test. General `CORE-04` remains open because the optional ASPFP backend/global arbitrary indoor graph and actual physics are unproven. Other critical P0 bug reports remain: NPC damage/death, visible dialogue, punching animations, realistic cover and zombie pursuit. The next useful round could audit C# compilation across all changed modules and work on P0 damage/death or multiplayer-free UI, rather than endlessly adding speculative navigation heuristics. If user asks next round, make actual code commits and update handoff again.

## 2026-10-13 — LATEST: user requested true status reconciliation, then switch feature

The user correctly flagged that another conversation had already implemented many P0 damage/death/UI fixes. I re-read canonical `MOBILE_NPC_PENDING_TASKS.md` and the handoff rather than assuming source was missing. **DO NOT REDO implemented P0 damage just because it has never been game-tested.** Verified current states (SOURCE only, NO Unity build):
- `CORE-02`: **QUASE PRONTA — AGUARDANDO BUILD**. Integrated authoritative Player Gun/Melee/Punch, NPC→NPC, Zombie→NPC and anatomical hit/debit/last attacker flows from past commits; user gameplay validation still pending.
- `CORE-03`: **EM IMPLEMENTAÇÃO**, but code includes one-time authoritative death, rigid physical collapse, genuine loot/hostile XP, delayed respawn and group cleanup. *Jointed bone ragdoll RAG-01 missing in NPC_Client rig (1 Rigidbody, 0 Joints); do not call it complete or redo death core from scratch.*
- `UI-01`: **EM IMPLEMENTAÇÃO** for full dialogue/name/HUD/Escape. Trading footer overlap only had scoped near-ready fix `61734473`. `UI-02`: **EM IMPLEMENTAÇÃO** for missing punch/hit/other humanoid animations. Both remain to be addressed/tested; not near-ready as whole parents.
- `ZMB-01`: **EM IMPLEMENTAÇÃO** for POI crossing, far obstacle route and client bound interest. `ZMB-02` also open for sound/target verification.

Per user's wish to move to a *different outstanding issue* rather than rework damage, worked on zombie pursuit: commit `2cf9669b42e7f4230963e48b403ac2a5033dcb1d` expands the previously 24m-only physically grounded **OFF-NAV DIRECT chase** to **64m for already acquired live player/mobile NPCs**, with collision probe only of NEXT 3m. Blocked approach >24m defers to native seeker, not invented alternate path. POINT sound investigation remains 24m; no acquisition/awareness boost. Every tick still uses native zombie CharacterController and ground/water/slope/clip safety. A disconnected nav region or multiplayer client interest does not magically fix itself.

**New scoped item `ZMB-OPEN-01`: QUASE PRONTA — AGUARDANDO BUILD**, parent `ZMB-01` EM IMPLEMENTAÇÃO. Technical design `ZOMBIE_CROSS_REGION_NAVIGATION_DESIGN.md` expanded (commit `85c73075`), canonical backlog synchronized (commit `f900cb78`), final Unity plan section 38 (commit `7154e0ae`). All source code-only, no compile/runtime/profiler.

Next reasonable engineering: continue `ZMB-01` with native region-aware client interest and safe long-range path behavior (cannot just broadcast unknown zombie bound IDs) OR other untouched priority like `COM-02` real radios. Preserve all task ID states and do not claim product readiness.

## 2026-10-13 — LATEST NEXT PENDING TASK: COM-02 singleplayer portable radio traffic

**Start any new conversation by reading the latest `MOBILE_NPC_PENDING_TASKS.md` COM-02/COM-RADIO-PORTABLE-01 rows, `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md` last two sections and `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 39.** Branch `feature/npc-ai-foundation` in private `pplopes2003-design/Unturned-Better`. No Unity compilation, host gameplay, radio equipment test or Profiler was performed. User validates in FINAL Dev Build only. Preserve lifecycle: scoped source-finished = QUASE PRONTA — AGUARDANDO BUILD; on user-approved build close only the tested scope, on failure reopen with evidence and preserve history.

**Native source confirmed, not guessed:**
`PlayerVoice.cs`: `canHearRadio = hasUseableWalkieTalkie || hasEarpiece` with earpiece based on `player.clothing.maskAsset.isEarpiece`. `UseableWalkieTalkie.equip/dequip` sets `hasUseableWalkieTalkie`. Actual frequency is `PlayerQuests.radioFrequency:uint`, default `PlayerQuests.DEFAULT_RADIO_FREQUENCY=460327`, native voice culling requires same tuned receiver/speaker channel. NPCs have no native PlayerVoice and MUST NOT masquerade as a player voice NetId.

**Actual code commits:**
- `2f878631f829c21eb23979c50e3c326a69f5a9c0`: new `MobileNPCRadioInterceptHUD.cs`, singleton host-only (singleplayer) passive five-second transcript; checks REAL local PlayerVoice.canHearRadio plus precise native PlayerQuests.radioFrequency, living player, rechecks each display frame. Does not hide ordinary HUD, seize cursor, send native chat or create fake audio.
- `ef7c690024a73621157c3492543f61c8f1709b00`: `MobileNPCRadioOutbox.cs` now turns existing server-observed-player-sighting outbox into a bounded simulated *TEXT radio transmission*. Requires actual owned NPC item 1445, living same group/faction, deterministic NPC group channel `PlayerQuests.DEFAULT_RADIO_FREQUENCY + (uint)(GroupId % 100000)`; group1=460328. Receivers individually require actual owned item 1445, valid living status and matching group/channel/faction. Brain gets approximate jittered low-confidence clue via `ReportGroupPlayerContactClue`, never live Transform or unverified attack. Shared transmit slot 1.25s, sender interval 8s; previous queue preparation cooldown 20s and TTL 24s preserved. No range restriction fabricated against vanilla global walkie behavior; base/vehicle radio off.
- `ce269fea02c13a0682acb4732b7c3f1aea00ff84`: `MobileNPCPopulationBootstrap` installs one HUD on director and resets static radio registry/airtime when map changes.
- `5bd13b81c55f616cc565117d5e3ca0d1bbd6590d`: rate-limited (10s) radio diagnostic in Player.log with channel, group and NPC listener count, no precise player coordinates.
- `0dd1cc42a82748fffd886c8465af4b008110f726`: stable Unity MonoImporter `MobileNPCRadioInterceptHUD.cs.meta`.
- `a3ea3d67fddc010efe4d753eb57e57235dc7c7e4`: `MobileNPCLooting.ScoreItem` treats FIRST genuine nearby walkie world drop item 1445 as useful at moderate score 62 for group NPC; actual ItemManager pickup/inventory transaction required, no free item or spawn, lower than urgent medicine/first gun.

**Permanent documentation:** `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md` latest status/source limits, backlog new `COM-RADIO-PORTABLE-01` scoped **QUASE PRONTA — AGUARDANDO BUILD**, `COM-02` parent still **EM IMPLEMENTAÇÃO** because no NPC visible hand equip, native microphone/audio transport, multiplayer remote client GUI, scanning, base/vehicle radio or other event producers. Section 39 final tests. `COM-03` real powered base/vehicle radios and `COM-04` reinforcement/loot/airdrop/unknown base event production still OPEN. This is real-equipment-gated **simulated text** traffic, not native PlayerVoice audio. The user explicitly asked to eavesdrop with player radio: host-visible subtitle honors actual channel and equipment. User must tune via native frequency selector; channel 460328 for group 1, others group-dependent.

**Other verified project state:** `CORE-02` damage pipeline still QUASE PRONTA source; do not redo. `CORE-03` ragdoll/corpse parent open. `UI-01/02` still incomplete as broad items. `ZMB-OPEN-01` physically bounded open-ground pursuit source near-ready; `ZMB-01` parent remains open for full map and networking.

**Next engineering recommendation:** enhance `COM-04` with event-sourced, resource-aware real reinforcement/loot/airdrop messages using only verified gameplay events; or tackle `COM-03` powered native placeable base radio after checking the real asset and energy infrastructure. Do not promise radios broadcast without item 1445 or that any distant survivor gets reports merely by faction. Never claim the native audio layer is finished without separate implementation and Dev Build evidence.

**Last radio security correction:** commit `f84f390f8cfff3f89e512a766b890651b019a3ef` invalidates an already-presented radio subtitle when the local player tunes off-channel, removes the qualifying device or dies; the text cannot reappear if they retune within the original 5-second window. Player.log diagnostics and recipient group checks unchanged. No Unity playtest.


## 2026-10-13 — Event-sourced airdrop radio and zombie pursuit beyond POIs (LATEST)

**User request:** Continue development, and explicitly add validation of zombie pursuit outside spawn regions/POI limits so zombies remain dangerous in the open world. Keep this handoff current for other agents/conversations. Complete AI architecture diagram only after all major modules are structured.

### Actual changes in this round
- `3c7a1a0b05ddb17e8ac4c161844e314277b95aa7`: `MobileNPCRadioOutbox.TryQueueObservedAirdrop` accepts a physically close (<28m) reported landed airdrop position, but ONLY when server-side NPC owns actual item 1445, belongs to a group/faction, and the 20s preparation cooldown permits. Creates a `MobileNPCRadioMessage.Kind.Airdrop` report with 45s TTL and bounded queue; no radio or carepackage is fabricated.
- `5ab40e687f7bbd7bc25fe50d89af1189a17abccc`: `MobileNPCAirdropContest.TickServer` checks a *real* `InteractableStorage` with `CarepackageDestroy` and remaining items. When a contesting NPC is within 28m, calls its radio outbox to report. The preexisting 220m contest scanning radius does NOT qualify as a radio sighting. Transmission still follows actual owned-walkie channel and player receiver validation via existing `MobileNPCRadioOutbox` and `MobileNPCRadioInterceptHUD`. It remains simulated text, not native audio/VOIP.
- **Source changes only; not compiled or played in Unity.** Validate APIs, 28m proximity, empty/looted crates, repeated announcements, radio frequency/gear, report TTL, player subtitles and group receiving behavior in Dev Build.

### NEW mandatory zombie work item: ZMB-POI-PURSUIT-02 (OPEN)
**Goal:** zombies remain a threat after player/NPC leaves a POI, including rural roads, fields, wilderness, and the spaces between POIs. Existing `ZMB-OPEN-01` source-level open-ground pursuit was near-ready but did not prove the entire behavior. Do NOT claim finished.

1. Audit `Zombie`, `ZombieManager`, `ZombieRegion`, zombie navigation and game spawn/interest rules in the actual Unity game code. Distinguish spawn/region ownership from chase distance, update/tick visibility and lifetime/despawn. Do not casually remove native region safety and performance bounds.
2. Verify a zombie can maintain pursuit across POI boundary and multiple adjacent regions while its target remains detectable, with navigation path recomputation, terrain/corner/door handling, avoidance and no instantaneous teleport/reset to original spawn region.
3. On LOS loss, investigate last known position and sound clues, then time out based on bounded evidence rather than a fixed POI-radius reset. Do not track hidden player Transform omnisciently.
4. Ensure zombies outside POIs can detect new nearby targets/noises and attack players/NPCs. Define safe ambient roaming/spawn density and budget for wilderness without spawning a massive army at once.
5. Verify unloaded regions, map transitions, dedicated server/client networking, zombie kill/death/despawn and perf limits at 20–100 active zombies. Prevent endless across-map pursuit and duplicate region ownership.
6. Dev Build tests: lure zombie beyond town edge, across roads/fields and region borders, temporarily hide behind building, fire gun in open field, approach isolated rural NPC, test water/vehicles/vertical geometry, and inspect `Player.log` for region transitions and stuck states.
7. Status: **PENDENTE DE AUDITORIA/VALIDAÇÃO EM JOGO**. Existing source work `ZMB-OPEN-01` is not proof. Prioritize this alongside NPC navigation and combat tests.

### Still pending
- Real NPC faction/group validation, death/ragdoll/respawn, full A* navigation, combat accuracy and cover, melee/ranged animations, dialogue/HUD/nameplates, actual group suppression, safe frag/smoke, healing under fire, base powered radio and future vehicle radio, more world-derived radio events (loot/reinforcements/unknown base), NPC vehicles, stealth vs zombies, optics and individual learning.
- Radio portable/interception COM-RADIO-PORTABLE-01 still **QUASE PRONTA — AGUARDANDO BUILD**; full COM-02/03/04 unfinished. Zombie pursuit ZMB-POI-PURSUIT-02 remains OPEN until region-crossing tests.
- Keep using the near-ready → finalized only on user's positive Dev Build feedback rule. Rejected or failing items return to PENDING.

**Suggested next work:** audit native zombie region ownership and chase termination code; then implement a guarded cross-region continuation mechanism if native chase is forcibly clamped. Do not promise safe behavior without testing. Also continue event-driven radio intel with no invented world observations.


## 2026-10-13 — MOST RECENT HANDOFF: COM-02 source review, queue and channel correctness

**Read this section first on the next continuation, then inspect current branch HEAD and the COM backlog.** Repo `pplopes2003-design/Unturned-Better`, branch `feature/npc-ai-foundation`. User resumed after commits `2f878631`, `ef7c6900` and `ce269fea`; subsequent checked-in changes already included limited diagnostics `5bd13b81`, real loot prioritization `a3ea3d67`, subtitle revocation `f84f390f` and physically observed airdrop producer. This round re-opened **the actual branch** through connected GitHub and reviewed current (not old commit) sources. **Change commits:** `9a500877` (radio queue / group frequency), `fef88c78` (radio design) and `89fa620a` (backlog); see current HEAD for this handoff and final tests.

**Confirmed native code:** `Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerVoice.cs` has `canHearRadio => hasUseableWalkieTalkie || hasEarpiece`; earpiece means `player.clothing.maskAsset.isEarpiece`; native radio listener/speaker culling uses matching `PlayerQuests.radioFrequency`, default uint `460327`. `MobileNPCRadioInterceptHUD` gates its host-only text on exactly these receiver state checks and a living player; a walkie merely sitting unequipped in player inventory does not qualify. Unity did not run, so equipment UI and visible subtitle remain **unverified gameplay behavior**.

**COM-02 static hardening:** `MobileNPCRadioOutbox.TunedFrequency` now maps positive runtime group ID to `460327 + GroupId` (group 1 remains `460328`), eliminating the former modulo collision. Queue peeking removes messages expired **or owned by a former group/faction**, allowing a fresh report to proceed. Contact enqueue now requires an actual faction component. Source guards still require real item 1445 from `MobileNPCInventory`, nondead sender/receiver, same group, same faction and matching virtual channel; rate limits remain 20-second creation cooldown, 8-second personal airtime / 1.25-second global, bounded queue and TTL. NPCs do **not** equip the radio in the player equipment system: this is explicitly simulated operator use, with no held hand prop, no native voice packets and no remote-client subtitle networking. It does not fabricate walkie power consumption or item spawns. Radio receiver registration resets on map switch, and player subtitle invalidation clears old transmissions if channel/receiver state changes.

**Critical COM-04 audit:** `MobileNPCAirdropContest.TickServer` can enqueue an `Airdrop` report from a real nearby landed nonempty `CarepackageDestroy` crate. Outbox sends its text to the tuned player and iterates eligible NPC radios, but **the NPC receiver only consumes an `EnemyContact` packet into its brain at present**. No remote NPC airdrop objective was implemented. COM-04 moved from blanket `NÃO INVESTIGADA` to **EM IMPLEMENTAÇÃO** with this strict partial scope; it is not ready for final closure. Reinforcements, useful loot and unknown-base event producers/consumers are OPEN. Base radio power / vehicle radios are OPEN.

**Exact lifecycle (user-approved):** `COM-RADIO-PORTABLE-01` **QUASE PRONTA — AGUARDANDO BUILD** for *singleplayer host simulated text sighting delivery and interception with real NPC radio ownership and player receiver gating*; `COM-02` **EM IMPLEMENTAÇÃO** (NPC equip visuals/native audio/networking); `COM-03` **NÃO INVESTIGADA** for native fixed powered base devices (vehicle part blocked by VEH-01); `COM-04` **EM IMPLEMENTAÇÃO** (only genuine airdrop observation/text slice wired). No compilation, Unity Dev Build, actual equipment setup, profiler or runtime acceptance took place during this round. The next engineer must not describe a manual source check as a Unity compilation.

**Next action requested by user:** first complete consolidated static source review and docs (this round), **then** investigate the *real* base/placed radio and native electricity/fuel interactions for COM-03, and separately build verified COM-04 reinforcement/loot intelligence pipelines without infinite NPC spawning or magically precise map truth. Other project priorities (zombie cross-region chase, navigation/jumps, dialogue, combat) remain in the backlog. Do not return to already scoped QUASE PRONTA items absent user build feedback. Test plan section 39 includes channel collision, radio acquisition, queue invalidation and the airdrop non-consumer limitation.

**COM-02 final Unity metadata check (same round):** repository tree revealed that `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCRadioMessage.cs.meta` and `MobileNPCRadioOutbox.cs.meta` were absent while `MobileNPCRadioInterceptHUD.cs.meta` existed. Commits `cb030be0` and `dfe81e57` now version the missing MonoImporter metadata with separate stable GUIDs, matching the adjacent U3 SDK file format. Do not confuse this hygiene step with a Unity import/compile test. When the user pulls via GitHub Desktop, check for pre-existing locally generated untracked .meta files before resolving any conflict. User will compile/test at final Dev Build. Subsequent documentation commits on the same branch contain this note.


## 2026-10-13 — LATEST COM-03 continuation: real powered improvised NPC base-radio station

**Read this section first when resuming the project.** Repo `pplopes2003-design/Unturned-Better`; working branch `feature/npc-ai-foundation`. User explicitly asked to continue after COM-02 and advance COM-03 (fixed powered base radios), without Unity testing until the consolidated final build. This round READ the prior COM-02 handoff, `MOBILE_NPC_PENDING_TASKS.md`, `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`, `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md`, plus actual native U3 SDK classes and NPC base code. GitHub access **worked** through connected GitHub. No local Unity/editor compiler, Player.log, gameplay or performance test has run.

### Native evidence and design decision

In native repository sources:
- `PlayerVoice.canHearRadio` and `PlayerQuests.radioFrequency` remain the player intercept/portable radio authority, as reviewed in COM-02. Walkie item 1445 is a real portable item. `InteractableStereo` exists but is music playback, NOT a fixed two-way communication radio. There is no checked-in native `InteractableRadio` ready to wire into base communications, so do NOT claim it exists.
- `ItemGeneratorAsset` with `EBuild.GENERATOR`, `InteractableGenerator` (isPowered, actual fuel, authored wire range), `InteractableGenerator.IsWorldPositionPowered(worldPosition)`, `InteractablePower` and `Level.info.configData.Has_Global_Electricity` are genuine. `ItemStorageAsset/InteractableStorage.items` are real server-tracked physical item storage. These support an explicitly project-specific **improvised base radio station** assembly: actual native 1445 stored at an NPC settlement plus actual electricity and a nearby NPC operator. No fictional fixed-radio ID, passive faction telepathy, magically added generator or battery use for portable walkies.

### Commits, exact code changes

- `7c4960b1`: `MobileNPCGroupMember.TryGetLivingLeaderServer` verifies a registered living member/leader before a group can use its settlement endpoint.
- `a46dea22`: `MobileNPCSettlementBuilder` exposes its actual existing central native storage to the leader, enables one real carried `ItemGeneratorAsset` as a perimeter position among the unchanged maximum **five base pieces**, leaves generator items out of the generic stash-transfer, reconciles pre-existing generators only once per settlement (not each transmission). Its generator is physical native `BarricadeManager.dropBarricade`, not an in-memory prop.
- `61f7544a`: `MobileNPCBaseRadioStation.CanOperateServer` requires live group leader and settlement, real nil-owner central storage within 3.5m anchor, real 1445 contained in `Items`, native electricity (a fueled ON world generator within actual wiring range, or native global electricity setting), and current NPC operator within **3m** of storage; no remote access.
- `ac008d99`: `MobileNPCRadioOutbox` sends and receives when either a real personally owned 1445 exists OR the above powered base station is physically operated. It still requires same group/faction/virtual frequency, 24s/45s reports, 20s creation cooldown, 8s personal / 1.25s global airtime, no player omniscient position and no native audio/client replication.
- `9525da25` prevents byte-loop wrap during real storage scans; `36e27f5f` versions unique `.cs.meta` for the new station script.
- `fc283b1b`: preserves the original carried generator's native power/fuel item state when spawning its actual barricade, using `new Barricade(asset, health, clone(item.state))`. Does not magically fill a zero-fuel generator.
- `bab2bee1`: radio diagnostics include `source=powered-base` or `source=portable` and physical-station receiver count, retaining prior 10s bounded logging.
- `2c295c43`: technical design and native-API caveats. `802a5af7`: backlog plus scoped task IDs. `7b6278ff`: consolidated Dev Build test plan **section 40**.

### Lifecycle and what to do next

- **COM-RADIO-PORTABLE-01: QUASE PRONTA — AGUARDANDO BUILD** (previous COM-02 narrow text scope).
- **COM-BASE-PHYSICAL-01: QUASE PRONTA — AGUARDANDO BUILD**: source-only scoped *physical, power-gated, near-operator* singleplayer base radio text endpoint and optional real generator placement. This does not mean a fully functioning autonomous base radio infrastructure.
- **COM-03: EM IMPLEMENTAÇÃO**, because generator power/fuel operation by NPCs, robust settlement instance ownership, purposeful travel to station, base UI, external communication between authorized groups/faction and vehicle endpoints are missing. **COM-BASE-POWER-02: INVESTIGADA**, native generator power/toggle/refuel APIs found but no autonomous NPC transactional fueling/switch action yet. `VEH-01` needed before vehicle radio.
- **COM-02: EM IMPLEMENTAÇÃO** for the full native radio/audio/humanoid equipment/cross-client mechanism. **COM-04: EM IMPLEMENTAÇÃO** with observed airdrop text producer but no distant NPC decision consumer, reinforcement orders, valuable-loot and unknown-base sources. All other tasks unchanged.
- A built but OFF or empty-fuel generator is NOT operational. A group with no genuine carried generator does not conjure one. A **working nearby world generator or global map electricity** can power a ready station if 1445 is physically stored. NPC switch-on and fuel supply should be next COM-BASE-POWER-02 engineering, with physical path, actual `ItemFuelAsset` state and `BarricadeManager.ServerSetGeneratorPowered`, and full transactional sync—never directly set private `isPowered`/fuel or invent resources. Handle proper group/base ownership rather than relying permanently on nil owner and location proximity.
- Real generators at loaded bases are matched by a <=6m one-time heuristic and unowned storage by <=3.5m proximity; this can mistakenly match unrelated unowned world objects. No persistent unique placement/barricade ID or cryptographically verified NPC base identity is implemented. Existing prebuilt 5-piece settlements may have no free slot for a generator. These limitations remain tasks, not closed.
- User will validate ALL scoped near-ready source tasks **only after the consolidated Dev Build**. Following positive feedback, close exactly the tested subitem with reproduction/build evidence, and on failure reopen it preserving its history. Do not fabricate compiler or runtime success and do not ask for interim builds. Tests now in section 40.

**Next suggested development round:** `COM-BASE-POWER-02`: review actual ItemFuel item state transfer and native generator `askFill` + `BarricadeManager.ServerSetGeneratorPowered`, implement limited safe interaction by physically present NPC at own generator (with one-shot consumable/fuel delta and anti-duplication verification). After that, extend `COM-04` to the first real actionable distant report/reinforcement request without teleporting or granting magically discovered bases/loot. Continue documenting every round and preserve the rest of the backlog.


## 2026-10-13 — NEWEST COM-BASE-POWER-02 continuation: native generator operating from real fuel

**Next conversation: READ THIS SECTION and current `MOBILE_NPC_PENDING_TASKS.md` first.** Repo `pplopes2003-design/Unturned-Better`; feature branch `feature/npc-ai-foundation`. User requested next pending item following the static COM-03 physical powered base station, without building in Unity until final Dev Build. Connector GitHub read/write functioned, and all work was committed directly on the same branch. **No actual Unity compilation, runtime test, Player.log or profiler was executed**.

**Source changes and commits:** `a67739e2` edits `MobileNPCSettlementBuilder` to cache only native registered generator placed/restored at current leader base (<=6m, world, real `ItemGeneratorAsset`, nil owner/group); `5e34c364` adds `MobileNPCInventory.TrySpendGeneratorFuelServer` for exact updates of native `ItemFuelAsset` canister fuel state, deleting only under native `shouldDeleteAfterFillingTarget` rule; `2a91fae3` creates `MobileNPCBaseGeneratorOperator.cs` for server-only refuel + toggle ON, `c1ff2859` adds the operator to `MobileNPCPopulationBootstrap`, `8aa9e2b1` adds its stable Unity MonoImporter `.meta`, `240a5acc` requires unobstructed raycast for both physically operated generator and base shared storage. `5e03d5f9` updates canonical backlog statuses; `3484acd2` adds native mechanics / limits to `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`; `362cfd3b` adds **section 41** to final test plan. Earlier COM-03 physical base station code remains intact and untested.

**Actual game API evidence:** checked `UseableFuel`: it calls `InteractableGenerator.askFill` and `BarricadeManager.sendFuel`; native fuel canister is an `ItemFuelAsset` whose current units are first two bytes of `Item.state` (ushort). `Items.updateState` and `removeItem` update native storage and invoke persistence hooks, `BarricadeManager.ServerSetGeneratorPowered` is the authoritative toggle that persists/replicates, and `InteractableGenerator` tracks `fuel`, `capacity`, `isPowered`. No new generator ID, fictional fuel or fake batteries. In-world generator ownership is validated using `BarricadeDrop.FindByTransformFastMaybeNull`, the underlying `BarricadeData.owner/group`, not proximity alone (though an unrelated unowned placed generator within 6m remains a residual risk).

**Exact new behavior:** Once every ~12s, a living, not currently engaged NPC with a valid group and active leader-owned native generator within **3.3m**, and an unobstructed interaction ray, may operate it. On ordinary maps (not global-electricity maps), refueling triggers only if fuel <=50% generator capacity. First consume positive units from one actual `ItemFuelAsset` stored in the group's true physically nearby central storage `InteractableStorage.Items` (max 3.5m and unobstructed); if nothing suitable, try the NPC's own `MobileNPCInventory` item. Same item state is reduced or item deleted only per native asset flag, then native `generator.askFill` and `BarricadeManager.sendFuel` synchronize/persist. If fueled but OFF, call native `BarricadeManager.ServerSetGeneratorPowered(generator, true)`. No change without fuel, no remote controls, no operations while combat brain is busy, no scene-wide generator scan per tick. Logs fire only on real refuel/power changes. The check intentionally does not invent a human 'fuel animation' or fake PlayerEquipment.

**Lifecycle and pending scope:** new scoped `COM-BASE-POWER-NEAR-01` **QUASE PRONTA — AGUARDANDO BUILD** for refueling / switching ON ONLY while physically present and supply already exists. Parent `COM-BASE-POWER-02` **EM IMPLEMENTAÇÃO**: ability to actually navigate to generator, generate prioritized missions to gather real fuel when supply depleted, intelligently toggle OFF to conserve fuel, robust unique saved station IDs and authoring actual interactions/animation are not done. `COM-03` remains **EM IMPLEMENTAÇÃO**, including native fixed object / full physical infrastructure / eventual vehicle; `COM-BASE-PHYSICAL-01` **QUASE PRONTA — AGUARDANDO BUILD**. Full `COM-02` (native audio, animated equipment, remote multiplayer) and `COM-04` (actionable airdrop and reinforcement/loot/base reports) remain unfinished. Do not close parents merely because source was committed or if subtitles show up in a dev build.

**Outstanding risk:** the transfer is performed synchronously on Unity's server thread after preflight checks and before generator `askFill`. There is no cross-object ACID database transaction/exception recovery system; if Unity/native callbacks throw after consuming item state, gameplay feedback/logs must be reviewed to detect a possible partial transfer. Multiple valid operators may sequentially refuel, but only actual remaining canister and generator capacity should govern. Tests section 41 must check source and generator quantities after each operation, retries, generator deletion, startup, save/reload and 40-NPC load. Do not claim the absence of all race bugs without testing.

**Next targeted feature:** `COM-04` actionable distant reports and reinforcements, starting with actual nearby landed airdrop intelligence currently only text transmitted. First make receiver treat message as uncertain remembered resource investigation (not instant navigation to exact crate or spawning reinforcements), then validate real NPC paths and cooldowns. Alternatively continue `COM-BASE-POWER-02` long-range generator maintenance in a separate deliberate movement task. User prefers to accumulate source development without asking for Unity testing until final Dev Build. Preserve IDs and update all guides at each round.


## 2026-10-13 — NEWEST COM-04 round: evidence-backed, actionable radio airdrop rumors

**Resume here on next conversation, then check live branch HEAD + `MOBILE_NPC_PENDING_TASKS.md`.** Project `pplopes2003-design/Unturned-Better`, branch `feature/npc-ai-foundation`. User asked to continue to next pending item after COM-BASE-POWER-NEAR-01 (generator refuel/ON scoped near-ready). Previous assistant promised COM-04 first actionable radio airdrop, then future calls for reinforcements. GitHub read/write connector available and functioning. **NO Unity compilation/Dev Build/runtime or 20/40 NPC profiling was carried out**. Keep near-ready scoped subitems awaiting user final Dev Build; parent COM-04 remains OPEN.

### Investigation and important pre-existing flaw

- Native `MobileNPCAirdropContest.cs` previously enumerated global `InteractableStorage` and let NPCs learn/seek a REAL landed `CarepackageDestroy` crate up to **220m away regardless of physical LOS**, making radio reports meaningless. Existing radio `MobileNPCRadioOutbox.cs` created actual near (<28m) observations and transmitted `Kind.Airdrop` text but receiver only acted on `EnemyContact`.
- The NPC uses `MobileNPCTactics.Goal.Investigate` via `MobileNPCBrainDriver.TryStartRememberedSupplySearch`, and actual `Goal.Loot`/3.5m vanilla `Items` transfer after approaching validated carepackage. Goal precedence, existing motor/pathfinder, NPC group/faction, inventory, storage/physics classes confirmed in source. No proprietary A* bypass used.

### Concrete code commits

- `0914bd85`: new `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCAirdropRumor.cs`. One **individual** short-lived rumor per NPC, only after actual same-group/faction on-channel radio; rounds sector to 20m x/z and +/-8m random imprecision; never stores crate Transform, max action range 105m, message TTL max 45s, plausible safe-ground validation; no target/no urgent goal before ordinary `Investigate`. On arrival tries 8s of normal nearby scan then gives up with 95s repeat-sector suppression. Invalidated by stale TTL, death, faction/group reassignment, combat/goal displacement, disabled `MobileNPCSettings.AirdropContestEnabled`, removed inventory capacity.
- `7595e2fd`: `MobileNPCRadioOutbox.Update` now passes `Kind.Airdrop` messages into `MobileNPCAirdropRumor.ReceiveAirdropReportServer` only after already enforced living NPC/group/faction, genuine equipment, native group frequency. No cross-group telepathy.
- `7bc111a1`: added a `MobileNPCAirdropRumor` component on the runtime NPC template after `MobileNPCRadioOutbox`; `007a4946` adds unique stable `.cs.meta`.
- `233b468c`: `MobileNPCAirdropContest` direct crate search now **28m** and checks actual clear `RayMasks.BLOCK_COLLISION` LOS with nonallocating raycast; only genuine active nonempty `CarepackageDestroy` native storage counts. A receiver currently investigating a matching rumor can only transition to real `Goal.Loot` after physically seeing such a crate. No 220m omniscient discovery. At final 3.5m native inventory transfer still enforces actual item presence.
- `39a814d7`: ending an expired/invalid rumor cancels only its own exact currently matching investigate destination rather than leaving obsolete movement running. `63af883a`: dynamic airdrop settings OFF cancels/blocks rumors. `f8c707f5`: an in-progress rumor cannot be overwritten by a different radio location while the old tactical destination remains active.
- `a6346828`: canonical backlog adds scoped `COM-AIRDROP-RADIO-01` **QUASE PRONTA — AGUARDANDO BUILD**, parent `COM-04` **EM IMPLEMENTAÇÃO**.
- `24ebcf59`: append architecture/evidence/limits to `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`, superseding its earlier claim that Airdrop has no receiving decision.
- `05ceffb0`: dedicated FINAL Dev Build **section 42** in `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md`, including former 220m cheating regression, walls, actual radio gear, uncertainty, TTL, group, loot transfers, multi-NPC and 20/40 performance.

### Behavior and important limitations

- A live NPC physically sees a real nonempty landed carepackage **within 28m and unobstructed head-to-crate LOS**. It cannot just scan 220m away; may send an air-drop report only over a genuinely owned portable radio 1445 or valid powered base radio. This current LOS check does not yet restrict NPC horizontal FOV/facing (future visual realism).
- Alive listener in same registered group/faction, on same current frequency, with real valid radio endpoint gets source report as uncertain personal memory. It may independently decide to investigate an approximate sector if not busy. This is **not an automatic crate destination, immediate loot, or promise to travel if path isn't available**; `TryStartRememberedSupplySearch` and motor still apply map physics. The real crate can vanish or be hidden by architecture, in which case investigation fails without inventing loot.
- Once a real nearby LOS-valid nonempty crate is verified, an active matching rumor can call ordinary `TryStartLoot` and later `MobileNPCAirdropContest.TransferAirdropLootServer` operates a physical Items transfer. A depleted crate ends the opportunity. No fake airdrop, group omniscience, native voice, client replication or permanent faction loot map.
- Radio memory TTL <=45s and sector cooldown 95s; max distance 105m to keep ordinary 65s Investigate goal bounded. No radio response if `MobileNPCSettings.AirdropContestEnabled` is disabled. Requires final Dev Build verification particularly through walls, with multiple crates, physical head hit origins, transition from investigate to loot, FPS/GC.
- **COM-AIRDROP-RADIO-01: QUASE PRONTA — AGUARDANDO BUILD** source-only. **COM-04: EM IMPLEMENTAÇÃO**, pending `NeedReinforcements` producer and receiver behavior, `UsefulLoot` first-hand producer/consumer, `UnknownBase` first-hand/uncertain source and path decisions; do not say radio AI is complete. Previous scoped `COM-BASE-POWER-NEAR-01` and `COM-BASE-PHYSICAL-01` remain near-ready, but parent `COM-BASE-POWER-02`, `COM-03`, `COM-02` and vehicles not finished. Preserve navigation, zombie, combat, UI, animations tasks exactly as listed in backlog.

**NEXT RECOMMENDED TARGET: COM-REINFORCEMENTS-RADIO-01**. Design and implement a real-source call from NPC under authentic combat threat (currently visible target, damage, tactical retreat) with scarcity/delay/cooldowns and radio equipment + group gating, and an uncertain response by available safe allies who *choose* to help, pathfind physically, and may refuse based on health/ammo/hostile situation. Never spawn free reinforcements, magically know a shooter, cross factions, or teleport. Update canonical backlog, handoff, radio design and final test plan each time. No intermediate Unity build until user final review.


## 2026-10-13 — LATEST: COM-REINFORCEMENTS-RADIO-01 real combat distress and voluntary help

**Next conversation: read this section and `MOBILE_NPC_PENDING_TASKS.md`, then check current GitHub feature HEAD before editing.** Project `pplopes2003-design/Unturned-Better`; branch `feature/npc-ai-foundation`, not main. User requested next round after `COM-AIRDROP-RADIO-01`. Work was actually committed via connected GitHub; **no Unity compiler or Dev Build / in-game / 40-NPC test occurred**. Keep near-ready subitems unclosed until user final Dev Build feedback.

### Source decisions and commits

- Existing `MobileNPCBrainDriver.ReportVisibleTarget` is a server-side first-hand visual event. Native `MobileNPCHumanoidVitals.LastDamageTime` and `HealthFraction` prove actual recent injury; `MobileNPCDisposition` and target types prove a *living hostile* Player/NPC/Zombie. A false sound or old target memory cannot initiate request.
- `1b0d15b2` added **`MobileNPCTactics.Goal.Reinforce` at the enum end** and `TryStartRadioReinforcement` with finite destination and 52s max duration.
- `77c07f0f` added `MobileNPCBrainDriver.TryStartRadioReinforcementServer` (only idle, target-free, safe-ground destination and no active goal), and calls `MobileNPCRadioOutbox.TryQueueCombatReinforcementsServer(target)` from genuinely observed `ReportVisibleTarget`.
- `0099f12b` outbox: conditional `Kind.NeedReinforcements` when actual injury within 7s leaves HP <=90% plus fresh visual enemy. Message reports **caller NPC's own current position**, never the enemy's Transform/position, confidence 0.78 and TTL 48s; cooldown of 80s per NPC plus existing prep/airtime queue. Outbox checks real physical portable 1445 or powered base station, same GroupId/faction/channel and living receiver, selects **at most one** eligible armed, healthy volunteer by distance/health/ammo. `60422ac0` optimizes source preflight to defer expensive radio endpoint check and avoids fragile target type syntax.
- `f36f67e2` new `MobileNPCReinforcementResponse`: idle same-group responder >=55% HP, native gun selected and >=3 rounds, not reloading, with valid radio, no other goal/target, caller 12–90m away. Converts reported caller position to 15m coarse grid + <=6m noise, validates native safe ground, waits 1.25–3.25s and accepts `Goal.Reinforce` without new NPC/teleport/aim. Cancels if dead, changed group/faction, injured/unarmed, encounter target, route superseded or 48s TTL. Cooldown per recipient 80s. `05ab4370` fixes native motor's automatic `ReportDestinationReached` on reaching a tactical goal; after real arrival within 9m, helper can remain near caller for ~4s normal visual perception, then finishes, without resurrecting a cleared travel goal.
- `593ceb5c` attaches response script on `MobileNPCPopulationBootstrap` runtime template, `a2473b98` creates unique Unity `MonoImporter` `.meta`.
- `a4991950` canonical `MOBILE_NPC_PENDING_TASKS.md` adds `COM-REINFORCEMENTS-RADIO-01` **QUASE PRONTA — AGUARDANDO BUILD** and parent `COM-04` **EM IMPLEMENTAÇÃO**. `d947d1c5` updates radio architecture with exact constraints and remaining work. `0cbfbf85` appends final Dev Build plan **section 43**.

### Behavior and limits

Real visual enemy + accepted recent damage => physically owned walkie/powered station => shared simulated radio outbox => native player intercept text (only if equipped/tuned) and choice of **at most one** suitable *existing same-group member*. The living helper may REFUSE when busy, hurt, unarmed, too far or without equipment; no AI faceless reinforcements are spawned. The location passed is coarse caller area, not enemy: existing motor/navigation still has to get there; afterwards ordinary vision (not radio) is needed before shooting. While helping, urgent damage/target/reassignment cancels only `Goal.Reinforce`. Does not promise that the native nav can reach arbitrary buildings.

**Known omissions / risks:** The source-only producer requires both current sight and recent damage; no late rescue call merely from gunshot or radio hearsay. Reinforcements only inside one group (up to 4); no faction-wide help across distinct squads, no voice audio, vehicle reinforcement, ammo donation, global commander/strategic reserves, or guaranteed successful route. Source code not compiled: pay special attention to `GetComponentInParent<Zombie>`, `Player`, native `MobileNPCBrain.State.Idle`, new tactic enum and responder actions. A selected helper might fail to validate safe ground; in that case the request truthfully gets no volunteer rather than fabricating movement. With multiple simultaneous calls, separate responders might accept different valid messages, but each NPC has individual 80s gate. Need final Dev Build test of radio handoffs and FPS/GC.

**Status:** `COM-REINFORCEMENTS-RADIO-01` **QUASE PRONTA — AGUARDANDO BUILD** for scoped source-only single existing healthy armed teammate responding to radio. `COM-04` remains **EM IMPLEMENTAÇÃO**: `UsefulLoot` and `UnknownBase` first-hand report producers/receiver actions, faction-wide support and detailed combat tactics outstanding. Previous `COM-AIRDROP-RADIO-01`, `COM-BASE-PHYSICAL-01`, `COM-BASE-POWER-NEAR-01` still near-ready awaiting final Unity feedback; parent `COM-03`, `COM-BASE-POWER-02` remain implementing. All zombie, nav, ragdoll, UI and AI tasks still tracked in backlog.

**NEXT SUGGESTED TASK:** `COM-USEFUL-LOOT-RADIO-01`, under COM-04: real first-hand valuable world-drop observation (not global POI memory), item-category priority and truthful TTL confidence, single physically equipped same-group receiver investigating **approximate** item area and only looting after locally verifying a real corresponding instance. Or `COM-UNKNOWN-BASE-RADIO-01` for first-hand real structures and uncertainty. Continue committing and updating four docs each round; user wants build ONLY at final consolidated validation.


## 2026-10-14 — LATEST COM-04 milestone: COM-USEFUL-LOOT-RADIO-01 real drop source to need-based distant investigation

**READ THIS FIRST WHEN RESUMING IN A NEW CONVERSATION**, then the live `MOBILE_NPC_PENDING_TASKS.md` and current branch HEAD. Repo `pplopes2003-design/Unturned-Better`, branch `feature/npc-ai-foundation`; GitHub connector worked and all changes were actually committed. User requested "Proxima prioridade então" immediately after the scoped COM-REINFORCEMENTS-RADIO-01 radio help. This round implemented genuine UsefulLoot radio reporting/investigation in source, **NO Unity compile/Dev Build, logs or runtime testing**, per user's end-only Dev Build policy. Preserve scoped status `QUASE PRONTA — AGUARDANDO BUILD` until user feedback; never close an unverified source subtask.

### Completed source code work

1. `3c2a8db7` — `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCSupplyKnowledge.cs`: new public static `GetCurrentRadioItemNeedServer(ushort itemID, inventory, health, injuries, survival)`. Reuses PREEXISTING `GetCurrentItemNeed` for the actual receiver's wounded/bleeding state, true food/hydration need, missing gun, nonempty caliber-compatible magazines and equipment. A listener with no personal 1445 but an operating base station values an actual observed first vanilla walkie.
2. `81034708` — `MobileNPCRadioMessage.cs`: immutable optional `ReportedItemID` introduced; generic message constructors default 0, new `TryCreateUsefulLoot` validates a native `ItemAsset` type, observed world position, group/faction and TTL. The player text intercept may show a tentative item name/sector, NOT an exact world item instance or guarantee.
3. `e89e35e5` — new `MobileNPCUsefulLootRumor.cs` contains `MobileNPCUsefulLootRumor` per-individual ephemeral rumor plus internal static `MobileNPCUsefulLootVision` for genuine active, nonempty ItemManager-backed `InteractableItem` with `TryGetWorldItemInstanceID`, an actual native item asset, and 17m head-to-item raycast LOS. Categories included: medicine, water, food, gun, loaded magazine, melee, backpack, vest and walkie.
4. `8e03c2b6` — `MobileNPCRadioOutbox.cs`: `TryQueueObservedUsefulLootServer(InteractableItem)` is called ONLY on an actually observed local world drop, rechecks LOS/id/physical radio/faction/group; queues a `Kind.UsefulLoot` typed item report with TTL 62s and confidence 0.76; same item type near same site suppressed 95s, shared 20s sender prep and airtime limits. Existing `ActiveRadios` delivery gates same current group/faction/channel and real 1445 or valid powered base. Receiver `MobileNPCUsefulLootRumor.ReceiveUsefulLootReportServer` stores observation with no world instance/Transform.
5. `dfa333d0` — `MobileNPCLooting.cs` uses existing native `ItemManager.getItemsInRadius` every 2.5s, ranks only genuine first-hand LOS sightings for optional radio, and permits special `Goal.Investigate` radio journey to perform ordinary local scans for the **same item ID**. Only upon re-observing a real world instance within 17m LOS and <=34m from 15m rounded radio region does it promote the rumor to normal `Goal.Loot`; original `targetInstanceID` and `MobileNPCInventory.TryPickupWorldItemServer` still handle real physical item removal / inventory transfer. A rumor NEVER directly adds inventory items.
6. `7601def1` — `MobileNPCPopulationBootstrap.cs` attaches `MobileNPCUsefulLootRumor` after Outbox/Airdrop; `affc08ad` new unique stable `.cs.meta` Unity MonoImporter.
7. `b5dcb5ff` — radio rumor considers native motor having auto-cleared `Goal.Investigate` when genuinely near destination, rather than falsely abandoning its 8s local item check. `e64476dc` — `MobileNPCLooting` resolves the later-created radio components after activation if Awake initial cache is null.
8. `264dde18` — canonical backlog row `COM-USEFUL-LOOT-RADIO-01` **QUASE PRONTA — AGUARDANDO BUILD** and parent `COM-04` **EM IMPLEMENTAÇÃO**. `20c5622c` expands `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`; `c2850e72` adds FINAL Dev Build plan **section 44** (needs/sight/radio/movement/item transactions/20–40 NPC benchmark).

### Runtime behavior expected, NOT yet observed

- Sender with true radio sees a real registered positive-quantity world drop within **17m LOS** during normal local loot scan and may announce native item ID + uncertain sector on group's frequency. No global world loot search and no secret item Transform broadcast.
- Another living same-group equipped listener receives a time-limited tip and checks **its own actual** need score >=70 and free inventory; need includes wound medicine, food/water shortage, no gun, compatible sparse magazines, base-station operator seeking an individual 1445. A healthy/fed/armed NPC may ignore. Receivers between 12–100m may travel to a validated safe-ground approximation (15m rounded x/z plus <=6m individual jitter), with max memory TTL 62s and same item/sector repeat cooldown 110s.
- Ordinary `TryStartRememberedSupplySearch` -> `Goal.Investigate`, motor still must walk physically; a detected fight, important goal, death, health changes or group/faction invalidation cancels the speculative errand. At arrived destination allow about 8s real local scan. Only original native world item of that ID found in scope with actual LOS may result in `Goal.Loot`, then real ItemManager pickup from exact registered world instance. Another player's/agent's earlier pickup makes trip fail, not reproduce loot.
- No voice audio, hand radio props, instant loot transfer, global shared group loot map, cross-group radio, player magically informed if not tuned, 100% guaranteed routes, vehicle operations, or fabricated equipment.

### Status/next

`COM-USEFUL-LOOT-RADIO-01`: **QUASE PRONTA — AGUARDANDO BUILD** (strictly source-only). `COM-AIRDROP-RADIO-01`, `COM-REINFORCEMENTS-RADIO-01`, `COM-RADIO-PORTABLE-01`, `COM-BASE-PHYSICAL-01` and `COM-BASE-POWER-NEAR-01` remain almost-ready awaiting eventual Dev Build. Parent `COM-04`: **EM IMPLEMENTAÇÃO**; unknown rival bases, intergroup/faction radio communications, field operator autonomy/patrol action and radio audio not finished. Parent `COM-03`, `COM-BASE-POWER-02`, `COM-02` remain implementing; zombie, navigation, combat, UI and other tasks unchanged.

**NEXT PRIORITY suggested:** `COM-UNKNOWN-BASE-RADIO-01` — first-hand discovery of **genuine player-built or NPC-built structures**, validate native barricade owner and registration, do not mistake map buildings/anonymous structures for hostile occupied bases; simulate uncertain area intel, short TTL and investigation/avoidance choices with risk/visibility, not direct attack or wallhack. Update four canonical docs every round. User does not want piecemeal Unity tests; final build after source development, then approve/reopen individual scoped IDs based on evidence.

**Known source review concerns to validate during final Dev Build:** newcomer static `MobileNPCUsefulLootVision` raycast mask and world item ID; actual item asset `name` subtitle formatting; C# compile of `GetCurrentRadioItemNeedServer`, message factory, `MobileNPCUsefulLootRumor`, awake lifetime, goal transition, 8s arrive and dedup cooldown; world pickup concurrency and FPS/GC under 20–40 NPCs. No Unity IDE or local C# build was available this round.

**Follow-up commits after the above source milestone (2026-10-14):** `5bd28b14` makes the `MobileNPCLooting` full-inventory path retain NO pickup intent but scan and report an actually visible <=17m real world drop every 2.5s (correct same equip/cooldown checks); `bf38eec9` formats player UsefulLoot interception using the native localized item `itemName`. Tests in section 44 explicitly include a full-pack broadcaster. No Unity compilation/runtime evidence. Next priority still COM-UNKNOWN-BASE-RADIO-01.


## 2026-10-14 — LATEST PRE-DEV-BUILD MILESTONE: unknown owned bases -> grenades -> AI brain diagram

**Read this latest handoff first if conversation resumes.** Repo `pplopes2003-design/Unturned-Better`, branch **`feature/npc-ai-foundation`**, GitHub connector functioning for actual direct source commits. User explicitly requested **"Proxima prioridade e depois o uso de granadas e ao fim quero que gere o diagrama do cerebro da IA de NPCs, depois irei testar a build."** This turn implemented those priorities in order, produced an editable Mermaid brain architecture doc in GitHub and a downloadable PNG/SVG/DOT diagram for visual review. **User's next step is their consolidated Dev Build. No Unity compile, in-game run, Player.log or benchmarking was performed by the assistant.** Do not request a test before delivering this round; once user tests, review errors and adjust source.

### Actual source commits, scoped status

1. **`COM-UNKNOWN-BASE-RADIO-01` source QUASE PRONTA — AGUARDANDO BUILD.** `5e89a319` creates `MobileNPCUnknownBaseIntel.cs`: NPC directly sees >=2 different `StructureDrop` objects physically registered by native `StructureRefComponent`, with real nonzero shared `StructureData.owner`, <=14m separation, LOS and within 25m of source. Checks every 5–6s; no global structures map or magic owner lookups. `0702cc9b` extends `MobileNPCRadioOutbox`: real physical group/faction/frequency endpoint enqueues `Kind.UnknownBase`, TTL 85s and confidence 0.61, and only same live group/faction/radio receivers can store uncertain sector. Listener can walk cautiously to safe lookout ~22m outside reported 20m sector; never target/player/raid/attack/loot because a base was merely heard. Report repeat cooldown ~160s / per-listener revisit ~150s. `97434272` bootstrap registers `MobileNPCUnknownBaseIntel`; .meta `ea5deb01`.
    - **Scope limitation explicit:** existing NPC settlement elements can have `owner=0`. These are NOT labelled enemy bases since no durable owner/hostility is physically established; new `COM-UNKNOWN-NPC-BASE-02` remains **EM IMPLEMENTAÇÃO / dependency** for saved physical NPC base/group identity. A native player's SteamID owner does not mean a currently occupied or necessarily hostile base.
2. **`COMBAT-GRENADE-01` source QUASE PRONTA — AGUARDANDO BUILD.** `234c2c50` adds `MobileNPCInventory.TryConsumeSimpleGrenadeServer`, consuming exactly one genuine owned `ItemThrowableAsset` explosive, excluding sticky, impact, flash and degenerate/negative fuse; preserves item state/stack. `3e110f89` creates `MobileNPCGrenadeThrower.cs`, requiring a fresh physically visible hostile from `MobileNPCHumanoidPerception` + matching `MobileNPCBrainDriver.Target`, healthy NPC (>=34%), range **11–27m**, not in cover/retreat, actual grenade and defensible initial ballistic arc. Check own/group/faction ally distance relative to native blast radius+3.5m; no throw on unsafe path, cooldown **45s** / observation rate **1.8s**. Instantiate the actual vanilla `ItemThrowableAsset.throwable` with Rigidbody force; attach vanilla `Grenade` and fill all native damage/effect/fuse settings. Additional `MobileNPCGrenadeNPCSplash` schedules radius-based LOS-filtered `MobileNPCHumanoidVitals.ApplyDamage` just before native explosion because NPCs are not native Player/Zombie; `12e503f2` fixes collision LOS self-suppression. No infinite grenade, no fake sentry, no reuse of PlayerEquipment. `770888d7` adds cautious loot preference for real limited explosive EItemType.THROWABLE. `97434272` adds GrenadeThrower to bootstrap; .meta `1763dd89`.
    - **Important untested risks:** projectile physical force/arc, classic Grenade Start/Invoke scheduling, damage bridge double hit or missing due to LOS, effect/audio on host, NPC vs enemy damage, actual loaded native inventory resource. No real equipped hand throw animation or separate multiplayer grenade networking. Also potential issue: cached radio components bootstrap order; Unity compilation must confirm APIs.
3. **Diagram:** `515716af` new `MOBILE_NPC_AI_BRAIN_DIAGRAM.md` with editable Mermaid chart, class mapping, priority tiers and explicit architectural limitations. Graphviz-rendered user downloadable files generated locally: **`/mnt/data/cerebro_ia_npcs_unturned.png`**, **`/mnt/data/cerebro_ia_npcs_unturned.svg`**, **`/mnt/data/cerebro_ia_npcs_unturned.dot`**. Actual file paths were created/verified and can be linked in terminal answer if still in active runtime. The brain is a state machine `MobileNPCBrain` + tactical goals `MobileNPCTactics`, coordinated by `MobileNPCBrainDriver`, NOT a formally unified behavior tree or a neural network. Perception+damage -> brain/memory -> safety/needs/group -> goals/specialized processors -> motor/combat/inventory/radio -> world outcome -> feedback.
4. **Docs:** canonical `MOBILE_NPC_PENDING_TASKS.md` commit `7edb4593` (new scoped IDs and full parents open); final test plan `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` commit `b97e5224` adds sections **45 (unknown owned bases), 46 (granada), 47 (diagram and final Dev Build checklist)**; radio architecture design commit `bea7a62c` describes actual owner evidence and conditions.

### Workflow and next user interaction

**When user returns with Dev Build result:** ask/consume Unity Console compile errors FIRST if project cannot compile, and Player.log for runtime gameplay if it did. Maintain original completion rule: `QUASE PRONTA — AGUARDANDO BUILD` becomes `CONCLUÍDA` ONLY after explicit positive user runtime feedback; failures go back to `EM IMPLEMENTAÇÃO` with code patches and historical notes. Full parent `COM-04` stays open because NPC base physical owner and cross-group radio/faction coordination are not finished; `COM-03` and `COM-BASE-POWER-02` still open; older zombie, nav, hitbox/UI/animations issues remain tracked. Do not claim source static review equals build. Preserve project files/docs and update handoff each new round.

**Dev Build minimum test progression:** 1 NPC for boot, damage/death/hud/dialog; 2–4 NPCs for actual radio channels, airdrop/useful loot/reinforcement; 2 native same-owner player structures for base intel LOS; one true grenade inventory item against genuine visible enemy in open map, with safe distance from player/ally; then doorway/walls, NPC splash/native player/zombie blast and 20/40 NPC perf. Attach `Player.log`, Unity Console error list, item state and video/screenshot of real behavior. Do not invent logging or run results.


## 2026-10-14 — NEWEST: Compiler error round, 12 user-supplied Unity CS errors

**Resume here before starting other implementation.** Repo `pplopes2003-design/Unturned-Better`, branch `feature/npc-ai-foundation`. After pre-Dev Build granadas/bases/diagram user attempted Unity compile and explicitly supplied 12 errors: 7 in `MobileNPCHumanoidMotor.cs` (`waitingForLadderApproach` out of scope, non-existing `TryStartServer`, missing `jumpRequestedDirection`, `chosenJumpDirection` unassigned), 3 `Random` ambiguous in `MobileNPCBrainDriver.cs`, 2 `RaycastInfo.colliderTransform` missing in `UseableMelee.cs`.

**Actual fix commits:**
1. `2f1f9026862d5104b70d99b88353fdebb087332b` — Motor: declared `waitingForLadderApproach` once outside navigation-only scope; removed legacy duplicate vault traversal API and missing variable, retaining existing verified real physics hop `ShouldChooseObstacleJumpServer` -> `TryBeginPlannedLowJump`/`TryBeginVaultJump` and actual `MobileNPCVaultTraversal.TryPlanJumpServer`; initialized and passed `chosenJumpDirection` as predeclared out argument. No movement behavior intentionally disabled.
2. `70b769465c31ea6d00854f0e57a78916e025a034` — BrainDriver all three unqualified `Random.Range`/`Random.value` become `UnityEngine.Random.*`, preserving `using System` and tactical logic.
3. `c9bae661857f6579fdf0ba44377362aef72a7455` — In `UseableMelee`, CLIENT `RaycastInfo` hitbox transform now uses real `collider` then `transform`, matching `Tools/RaycastInfo.cs`; preserve separate SERVER `InputInfo.colliderTransform` and unrelated method argument same name. Avoid global renames.

Static final checking confirmed all three source files balanced; 1 shared ladder declaration, no obsolete vault method or direction identifier, `TryBeginVaultJump` still present, no unqualified Random references, correct client melee cast. **Not yet Unity compiled/retested.** User should update same branch, import and recompile; if compiler still errors use new exact log and amend `COMPILER-FIX-01`. `NAV-...parent`, `COM-04` and `UI` statuses unchanged; near-ready is NOT concluded until actual gameplay and user feedback. Do not imply 12 errors' disappearance from Unity proven.

## 2026-10-14 — NEWEST HANDOFF: FIRST REAL DEV BUILD REGRESSION AUDIT, many P0 failures, root cause in PLAYER INPUT

**DO NOT SKIP: user actually tested the Windows Unity Dev Build and supplied `/mnt/data/Player.log` in this conversation plus nine gameplay reports. This is no longer speculative source development.** GitHub repo `pplopes2003-design/Unturned-Better`, branch `feature/npc-ai-foundation`. Canonical comprehensive evidence: **`MOBILE_NPC_DEV_BUILD_AUDIT_2026-10-14.md`**, commit `8b9411f6`. The build corresponds to source BEFORE this round's patches. The user observed zombies vanishing at regional boundary while chasing, NPC invulnerability to player shots, no punching damage reaction, neutrals/friendlies only flee, stuck on posts/walls, unseen loot trips, broken dialogue, reversed see-through nametags. Parent CORE-02/AI-07/UI-01 reopened in `MOBILE_NPC_PENDING_TASKS.md` commit `1ef5379c`; real new test plan section **49**, commit `17a88f02`. ZERO corrections from this round have passed Unity compilation/runtime; all are SOURCE-ONLY awaiting user's recompile/build.

**Player.log facts (source evidence, do not reinterpret):** 141 events `MobileNPCHumanoidVitals Damage accepted`, of which 126 attacker `<environment>`, 15 `Zombie`, no player; actual 2 `Death transition` and `Death queued` -> health/death pipeline NOT fundamentally absent, likely input validation issue. 138 `[MobileNPCHumanoidMotor] Blocked`, 477 `FALL DIAGNOSTIC`, 38 `Pathfinding.NavmeshCut` missing warnings, 1 `MobileNPCLooting Pickup transfer failed item instance 425`; bootstrap `Can't add MobileNPCInventory` and `MobileNPCGroupMember` because `RequireComponent` already created. Zombie disappearance cause inferred from `ZombieManager.onBoundUpdated` local `regions[oldBound].destroy()` despite active hunters, NOT captured as specific log line. User's visual experience is evidence.

**CRITICAL ROOT CAUSE:** custom `ERaycastInfoType.MOBILE_NPC` already flowed client → `PlayerInput.getInput` server validation with physical first-hit. Anatomical NPC hitboxes live on `ENTITY`=23. Native `RayMasks.DAMAGE_CLIENT` contains ENTITY but `DAMAGE_SERVER` **does not**, so the server ALWAYS rejected reported hit (while client rendered hitmarker). Actual patch **`ba3512a4`** `Player/PlayerInput.cs`: use `RayMasks.DAMAGE_SERVER | RayMasks.ENTITY` strictly for server physical validation of MOBILE_NPC type, covering actual gun, melee and punch. Existing wound/HP and death pipeline stays authoritative on `MobileNPCHumanoidVitals`; DO NOT add fake health object, don't use `PlayerLife` or `Zombie.askDamage` as drop-in. Supplemental hardening **`506fe4bf`** `MobileNPCDamageBridge` physics first hit matching point/range; **`463c98c6`** adds PLAYER/ENEMY for first-occluder stop; **`d52424ab`** `UseableGun` and **`15ee9a56`** `UseableMelee` fallback only when InputInfo lacks collider transform.

**NPC retaliation (not gratuitous hostility):** **`82e0085c`** `MobileNPCHumanoidDisposition` stores a validated Player attacker 32s and answers `CanEngagePlayer(player)` for Hostile OR temporary self-defense. **`1ebffbf4`** Vitals records aggressor ONLY after actual server HP debit and notifies defense; **`40f56678`** Perception now searches only permitted attacker with LOS/FOV even for neutral/friendly; **`74895f34`** melee and **`9ea74397`** ranged shots recheck the permission. Previously disposition.CanEngagePlayers false prevented any player perception and combat retaliations. Tactics still initially prefer cover/retreat on hit, so focus validating that neutral can eventually return fire when safe/armed, not force suicide charging.

**UI:** **`3ac0ba63`** `PlayerInteract` now includes `RayMasks.ENTITY` in interaction ray. Previously `PLAYER_INTERACT` didn't include custom NPC anatomy, preventing native focus/open dialogue. **`5112de0a`** `MobileNPCNameplate` range 9m, require camera dot>=0.92 and world LOS raycast; text rotation same as camera for readable (not mirrored) name. Both source changes untested.

**Zombies:** **`d304e89b`** `ZombieManager` checks `isHunting` in source region before local `regions[oldBound].destroy()`, retains when real hunter still alive, sweep 3s removes empty/no-player/no-hunter old region. This protects chase instance while native moving out of bound; does NOT invent a full external navmesh/A* graph and must be tested with 1/5/30 zombies.

**Navigation & Loot:** **`5b4110ef`** BrainDriver cancels unreachable tactical `Loot`, `Investigate`, `Reinforce` goals after ≥4 actual fails; previous code only cancelled `SeekCover`/`Flank`; **`47fcb006`** MobileNPCLooting blacklists same native instance 75s after failed physical approach or 35s after failed transaction; avoids immediate loops, no fake pickup. **`80d96522`** Bootstrap only adds Inventory/GroupMember if missing; source warning confirmed. Fallback native A* license/Pathfinding.NavmeshCut missing, 38 warnings, remains unresolved; 9x9 local search cannot guarantee cross-building multiroom routes. 477 fall diag may be overlogging, not necessarily physical falls. No claim to have finished navigation.

**AI research:** user's suggestion was examined: original S.T.A.L.K.E.R. A-Life requires offline game graph & online AI map/smart terrain, not genuine teleport/pathfinding autonomy; *F.E.A.R.* Jeff Orkin GOAP uses goal-directed planning and cached solutions, but physical engine APIs remain authoritative. Limit potential implementation to bounded *utility/GOAP* choosing verified actions and states, not new 40-NPC heavy planner. See research/source URLs and architecture proposal in `MOBILE_NPC_DEV_BUILD_AUDIT_2026-10-14.md`. GOAP/A-Life rewrite has **NOT** been implemented; do not say otherwise.

**Next user turn likely new Unity compiler errors or Player.log after pulling feature branch:** FIRST address compiler incompatibilities in actual API types, then validate 1-NPC P0 shooting/punch/death, 1 NPC neutral retaliating vs aggressor, dialogue/nametag LOS, 1 zombie cross-bound, 1 easily accessible real loot transfer, then 10/20/40 NPC perf and ASPFP issues. Update backlog/handoff/test plan after each. Never mark CONCLUÍDA without USER runtime evidence. Cross-branch avoid accidental `main`.


## 2026-10-14 — LATEST follow-up: sight vs footsteps, bounded self-defense, live validation tracing

**RESTART HERE next conversation.** Latest source round after the prior 2026-10-14 Player.log audit. User authorized new fixes and specified additional observation: "NPCs me têm em visão mas, quando faço barulho, agacham sem motivo". All commits applied to **`pplopes2003-design/Unturned-Better` branch `feature/npc-ai-foundation`**, source-only. No Unity compile/Dev Build/gameplay retest this round.

Commits (order):
- `efc77eee030105d0bde69accdcd77dff432e8f9e` — BrainDriver: audio clue does not force listening after visually confirmed player. `investigatingFootstepOnly` marks provisional search and `ReportNonThreateningPlayerSightServer` may cancel ONLY own footstep investigation, not active cover/retreat/group goals.
- `a9535acecc29636a8f67be60898b6b28d81b4577` — Perception: independent recognition of nonhostile player and legitimate visible combat target; pass target-visible movement flag only for legally engageable opponents, not pacifist awareness.
- `216b1fcb0518167cd7f57dc053da63e5316c910f` — Tactics: add optional `canReturnFire` to cover selection, avoiding default Retreat when an armed/healthy NPC visibly confirms enemy but no reachable cover exists.
- `c23e8e9fa4856a0cc95ed329a7c10506cad6f595` — BrainDriver: compute retaliation eligibility only with actual local visual evidence + real selected firearm/rounds, may exit previous Retreat on positive visual confirmation and sufficient HP; low-health/unarmed still retreat.
- `aaf077d2ba6bc2e3f85db807bf0c6a4c3177e7a6` — Perception: remove prior erroneous `CanEngagePlayer` filter from `FindVisiblePlayer` itself (otherwise peaceful NPC never recognizes player to cancel false noise behavior); prune remembered aggressor after right to self-defense expires.
- `3e5dd982052f2d91319d4cf717922a75abb117e8` — Motor: `UpdateCrouchStance` suppresses sound-driven/sneak crouch during current true combat target visibility.
- `4b36fad123e1255c222bf488d32ccd0a9656d1c8` — InteractionController: GUI depth -10000 for local dialog and one session-open debug marker; does not prove end-user HUD functionality.
- `b15076a956abea05ba335b1a27b920b68097d9e6` — Aim: legally target player only if hostile or validated recent attacker, independent of benign visual awareness.
- `4eb1eb20395321b780b68a3062d431b820cd0ab9` — PlayerInput: when custom `MOBILE_NPC` client hit ray cannot be confirmed server-side, rate-limited DEVELOPMENT_BUILD diagnostic `[MobileNPCDamageValidation] Rejected`; retains authoritative rejection and geometry mask, no free damage.

Core damage/death flow was already patched in prior source round: `PlayerInput` MOBILE_NPC packet classification and `DAMAGE_SERVER | ENTITY`, UseableGun/UseableMelee server `ApplyValidatedWeaponImpactServer`, Vitals -> DieServer -> ragdoll/corpse/removal. DO NOT falsely attribute those fixes to the current round or claim proven. Latest 2026-10-14 uploaded `Player.log` contained 2 real `Death transition` after zombie/environment damage; no literal `attacker=Player`; 138 stuck logs and one `Pickup transfer failed`. First new Unity run should confirm death from PLAYER bullets AND melee/punch and inspect newly emitted `[MobileNPCDamageValidation]` if hitmarker still only clientside.

What remains: **P0 game tests** of damaged NPC HP/Corpse and reaction; non-hostile NPC passive visual recognition, only self-defense after actual hit; footsteps sound and immediate de-crouch when target visible; dialogue panel open and keys/ESC; past nameplate rotation/LOS fix; zombies across region stream; actual loot world pickup; stuck navigation; finite A* across rooms. No GOAP rewrite or fixes to all these areas were added here. Maintain previously-open CORE-02/CORE-04/UI-01/AI-01. Research: F.E.A.R. GDC 2006 Jeff Orkin uses FSM + GOAP dynamic replanning, S.T.A.L.K.E.R. first game A-Life uses simplified online/offline simulation; neither substitutes Unturned physical path/authorization. If next build fails, inspect console exact error, then fresh Player.log and reproduce one controlled issue at a time. **Do not declare CONCLUÍDA until user confirms runtime.**

## 2026-10-14 — NOVÍSSIMA RETOMADA: última correção de agachamento visual/ruído

O usuário retornou após nova interrupção pedindo continuação. Revalidada branch `feature/npc-ai-foundation` (HEAD anterior `8d1cb933`): código já incluía correções SOURCE-ONLY feitas na auditoria da primeira Dev Build: `PlayerInput` valida `MOBILE_NPC` via `DAMAGE_SERVER | ENTITY`, `MobileNPCHumanoidDisposition` registra agressor validado, `MobileNPCHumanoidPerception` permite VER jogador sem atacá-lo, `MobileNPCBrainDriver` cancela escuta e investigação apenas originada de passos, `MobileNPCNameplate` oculta atrás de paredes/rotaciona para câmera e `MobileNPCInteractionController` mantém menu IMGUI. **Ainda sem nova Unity build.**

**Correção específica nova:** `edd3fe9c5cdf89904ad527a7de94b0420a3f5fe4` e `ff7ace463a034362e91cbc82faddaba0ce4f204c` em `MobileNPCHumanoidMotor.cs`. Defeito adicional: `targetIsVisible` era false para jogador visto, mas não-hostil, pois esse boolean reflete PERMISSÃO de combate, não reconhecimento visual; `Gait.Sneak` de barulho ainda agachava NPC. Nova checagem de `MobileNPCHumanoidPerception.VisiblePlayer` com ausência de alvo real impede crouch sem cancelar stalking de inimigo diferente. Referência de percepção armazenada em cache depois de ativação, dado que template adiciona Motor antes de Perception.

**Estado:** `AUDIT-P0-SIGHT-SOUND-01` quase pronta (somente fonte), **aguardando nova Dev Build**. Ainda priorizar erros de compilação e verificar morte causada pelo jogador/tiro/corpo a corpo com HP, retaliação, fim de agachamento quando vê pacífico, UI, nomeplates, zumbis fora das POIs, objetos bloqueando movimento e saque real. O log anterior continua fonte dos testes; nenhum relato foi esquecido. `MOBILE_NPC_DEV_BUILD_AUDIT_2026-10-14.md` contém análise de A-Life/GOAP e dos sintomas. Não marcar sistemas como concluídos até prova no jogo.


## 2026-10-14 — Seguimento concreto: navegação local e seleção de loot com LOS

**Baseline confirmado antes da rodada:** `c942d4ce9f07e6a4594ebbfc72fc616b8f226cb9`, mesma branch `feature/npc-ai-foundation`. O código já tinha correções do primeiro Player.log; nenhuma foi declarada validada nesta rodada.

**Commits de código (ordem):**
- `126ab53d` — `MobileNPCHumanoidMotor.TryBuildLocalRoute`: após **pelo menos 2 recuperações de travamento físico reais** para um objetivo >6m e quando não é validação de destino exato, troca espaçamento da grade 9x9 de **1,25m para 2m**, ampliando alcance lateral de ~5 para ~8m, com **mesmos 81 nós** e verificações existentes de piso, colisões, portas e travessia de arestas. Objetivos exatos (ex.: cobertura, porta/escada) continuam na grade fina. NÃO cria pathfinding global, não garante saída de todo edifício e não substitui o ASPFP comercial ausente na SDK.
- `a72c7969` — `MobileNPCLooting`: rejeita alvo comum de loot meramente enumerado pelo `ItemManager` que está atrás de parede/outro piso/fora de **17m de visão real**; evita loop de buscar objetos invisíveis com o objetivo tático `Loot`.
- `7e5c0c76` + `9e1b4534` — `MobileNPCUsefulLootVision.CanSeeWorldDropServer` mantém restrição original de classes úteis para confirmação de rádio, mas expõe parâmetro opcional `requireRadioUsefulCategory: false` somente à busca comum, evitando a regressão de impedir saque de roupa, materiais de construção, suprimentos e outros tipos. Itens ainda exigem registro nativo real, quantidade válida, LOS e transferência de inventário no servidor. Fluxo de rádio **JÁ** iniciava `TryStartLoot` em `TryConfirmLocallySeenItemServer`; não o duplicar.

**Revisão estática:** 3 arquivos de código com chaves balanceadas; assinaturas/chamadas e condição de resolução fina confirmadas em re-fetch no GitHub após atualização. **SEM compilação Unity, SEM nova Dev Build, SEM resultado runtime**.

**Testes obrigatórios na próxima build:** 1) loot atrás de parede e no piso superior NÃO cria alvo mágico; ao entrar no cômodo e ver item, NPC pode caminhar, pegar item nativo e trocar inventário exatamente uma vez; incluir roupas/estrutura, alimento/água, arma e rumor via rádio. 2) Navegar até objetivo >6m atrás de parede longa/quina/poste; após 2 stalls testar grade ampla sem atravessar paredes, cair do mapa, subir andar incorreto ou aumentar travamentos; medir logs/fps 1/10/20/40 NPCs. 3) Confirmar vida/morte por tiro player e melee, retaliação, agachamento pacífico, HUD de diálogo, nomeplate e zumbi atravessando POI segundo seção 49/50. Resultado de código: **QUASE PRONTA — AGUARDANDO BUILD**; pais NAV/LOOT/CORE-02/UI/ZMB ainda abertos.

**Atenção:** se a primeira build acusar erros do Unity, corrigir estes primeiro. Depois testar em runtime e só então reclassificar qualquer item como concluído; registro anterior de `Pickup transfer failed` não foi explicado definitivamente por este filtro de visão.


## 2026-10-14 — NOVA RODADA: UI-01 migração do diálogo local ao HUD nativo do Unturned

**Baseline confirmado** no início: `a35d797d2ff97df4361d7ced002955afaca2e372` na branch `feature/npc-ai-foundation`. Não havia qualquer Dev Build nova depois dos patches anteriores. Alterações desta rodada restritas à infraestrutura de UI local/entrada — sem modificar contratos de trocas, alianças, HUD global permanente, dano, zumbis ou árvore de IA.

**Achado estático:** `MobileNPCInteractionController` apresentava opções em `MonoBehaviour.OnGUI` (painel IMGUI independente), enquanto os diálogos nativos deste SDK usam `PlayerUI.container` e `Glazier.Get().CreateBox/CreateLabel/CreateScrollView/CreateButton`. O primeiro mecanismo pode não se compor adequadamente ao pipeline de `Sleek/Glazier` durante a Dev Build. `PlayerUI.escapeMenu()` não conhecia a sessão móvel: podia abrir a pausa e ocultar o HUD enquanto o controlador de NPC mantinha estado ativo; `PlayerEquipment.Update()` processava hotkeys de itens simultaneamente às opções 1–9, modificando involuntariamente o item segurado na negociação.

**Commits da rodada (sequência):**
- `601b9a89` `MobileNPCInteractionController`: adiciona painel `ISleekBox`, rótulo, botões nativos 1–9/Fechar anexados à árvore real `PlayerUI.container`; fornece `IsDialogOpenFor(player)` e `TryCloseOnEscape()`, e evita duplo processamento de ESC. Mantém `OnGUI` somente como fallback se root nativo indisponível.
- `529dacdf` `PlayerUI.escapeMenu`: primeiro fecha o diálogo móvel ativo, sem abrir pausa nem fechar `PlayerLifeUI`.
- `88341f5b` `PlayerEquipment`: suprime hotkeys de equipamentos durante sessão, evitando trocar ou desequipar item usado nas ofertas ao pressionar respostas 1–9.
- `ed209f6c` `PlayerInteract`: impede nova mira/interação com mundo enquanto conversa ativa, evitando reabrir ou operar outro objeto pela tecla de interação.
- `e31a7854` `MobileNPCInteractionController`: adiciona `ISleekScrollView` e altura de conteúdo ajustada por número aproximado de linhas; botões permanecem fixos no rodapé.
- `e9864c2a` `PlayerUI.Update`: o diálogo local integra o cálculo normal de `window.showCursor` em vez de forçar/manter estado de cursor manualmente; assim os botões também podem ser clicados.
- `c1d4f03b` `PlayerUI.tickMenuBlur`: impede o blur de pausa quando o diálogo móvel está ativo, de modo semelhante aos diálogos nativos.
- `75759a03`: usa inteiro para dimensões do conteúdo no Sleek, com compatibilidade de propriedades de layout.
- `ec937b10`: protege remoção do painel durante descarregamento do mapa, quando `PlayerUI.window` já foi destruída.

**Revisão estática:** re-fetch dos quatro arquivos de código; chaves balanceadas, `#if !DEDICATED_SERVER` pareados, referências em `PlayerUI`, `PlayerEquipment` e `PlayerInteract` presentes e distinção entre UI nativa/fallback mantida. **Não houve compilação Unity nem runtime nesta rodada.** Portanto status exato `AUDIT-P0-UI-NATIVE-03 QUASE PRONTA — AGUARDANDO BUILD`; parent `UI-01 EM IMPLEMENTAÇÃO`, não concluído. Apesar de melhoria estrutural fundamentada no SDK, somente a Dev Build pode confirmar Glazier/ISleekElement em execução, renderização legível, mouse, teclas, HUD, foco e ausência de conflitos com UI de multiplayer.

**Dev Build mínima:** iniciar um player SP vivo, spawnar neutro e amigável; F abre panel com 3 opções visíveis, 1/2/3 no topo e keypad mudam a tela sem equipar hotbar; Mouse clica nas opções e Fechar; ESC fecha sem abrir Pause nem fazer desaparecer Life HUD; item segurado preservado até confirmar troca; em loja de 9 slots todas opções são roláveis/selecionáveis; afastar jogador >4m, matar NPC ou trocar de mapa fecha sessão e não deixa painel ou cursor preso; comércio/aliança alteram apenas inventário real. Se falhar, procurar `[MobileNPCInteractionController] Native Sleek dialogue panel attached`, `Dialogue opened for`, ou `Failed to create native dialogue UI` no Player.log. A validação de vida/retaliação/nav/loot/zumbis das seções anteriores continua aberta.


## 2026-10-14 — RODADA AI-07: saque físico em mesa/prateleira, servidor e log

**Baseline revalidado:** `bf0be5af4041fb0d618b5936b55ae4b401a7dc48` em `feature/npc-ai-foundation` antes desta rodada. Não houve compilação nem runtime novo; a evidência antecedente no Player.log tinha `Pickup transfer failed` e itens que NPCs aparentemente não buscavam/atingiam.

**Causa real em fonte:** `MobileNPCLooting` só tentava pegar se `(targetPosition - transform.position).sqrMagnitude <= pickupDistance²` — medição 3D a partir dos PÉS. Em objetos sobre bancadas/prateleiras, a diferença vertical por si só inviabilizava a coleta mesmo com NPC próximo horizontalmente. `ItemManager.TryTransferWorldItemToMobileNPCServer` aplicava a mesma métrica 3D a `ItemData.point`, potencialmente diferente da posição física atual do drop simulado. Assim, simplesmente trocar o gate da IA não resolveria a falha de transferência.

**Commits de código:**
- `1a822cf6`: `ItemManager` mantém assinatura antiga e adiciona overload com `out string failureReason`, valida item **real** registrado em `ItemRegion.items` e correspondente `ItemDrop` ativo, proximidade planar do NPC, limite vertical 2,1m, distância efetiva do ponto de olho/mão (y+1,45m) até objeto, visão física desobstruída, inventário livre. Limita alcance na transferência a 2m planar, sem teleportar jogador/NPC nem entregar itens por rádio. Mantém commit transacional **guardar uma cópia no inventário antes de remover o único item do servidor**, e RPC normal `SendDestroyItem` para remover cópias visuais. Rejeições passam motivo explícito.
- `03035b7f`: `MobileNPCInventory` expõe overload com diagnóstico de falha, mantendo API antiga. Autorização de posse continua em `ItemManager`.
- `7675eb0d`: `MobileNPCLooting` chega à fase de tentativa por **distância planar <=1,4m** + altura <=2,1m e deixa a decisão final de hand reach, LOS, item verdadeiro e inventário ao servidor. Log em falha agora inclui `reason=`, NPC root e posição do item; cooldowns 35s/75s e score de loot permanecem.
- `3ebb65a0`: não autoriza um registro do servidor cujo id do item não corresponda à representação física `InteractableItem.jar.item.id`, evitando copiar um item divergente.

**Checks estáticos executados:** re-fetch dos arquivos, chaves balanceadas. Teste aritmético sintético da geometria: item no chão a 1,1m e sobre mesa (1m planar, 1,45m alto) passam os gates de distância, enquanto item a 3m de altura ou 2,8m no mesmo piso não; cada sucesso ainda exige LOS real, armazenamento e remoção nativos em runtime. **Não é execução Unity nem validação visual ou física real.**

**Status correto:** `AUDIT-P0-LOOT-REACH-02` **QUASE PRONTA — AGUARDANDO BUILD** (subescopo transfer e altura); parent `AI-07` permanece **EM IMPLEMENTAÇÃO**. Ainda testar itens muito altos, mesa larga com colisões, simulação de drop, raycasts obstruídos, física de retirada e identidade de instância, múltiplos NPCs competindo, inventário cheio e itens serializados, FPS 1/10/20/40. Se houver `Pickup transfer failed`, o `reason=` novo deve permitir diagnóstico concreto sem ajustes especulativos. UI-01, CORE-02, CORE-04 e perseguição ZMB-01 seguem abertas. Próxima prioridade pode retomar zumbis desaparecendo por fronteira/streaming POI.


## 2026-10-14 — Próxima rodada ZMB-01: retenção física + reentrada sem duplicação + boss crossing

**Baseline GitHub:** `b0d17be341652fead7f271e1879dff587c4a74db`, branch `feature/npc-ai-foundation`. O código já tinha `d304e89b` para preservar regiões de zumbis `isHunting`, a movimentação física limitada de alvo vivo 24–64m e `OnUpdate` de hunters de bounds vazios. Não reescrever isso.

**Novos commits:**
- `280acfcf` — `ZombieManager.cs`: retenção do bound de origem depende agora de **distância física do zumbi ao jogador local** (48m geral; 112m se perseguindo), sem exigir que o jogador continue no bound. Reentrada em bound ainda `isNetworked` não executa `generateZombies` outra vez; a rotina antiga resetava contador `alive` e podia adicionar zumbis à lista retida/IDs existentes.
- `79e05b1e` — `ZombieRegion.cs`: lógica de boss de missão originalmente infligia 50.000 de dano quando todo jogador abandonava o bound, podendo matar o boss enquanto ainda caçava fora da POI. Servidor local preserva o boss vivo perto ou caçando (48/112m); limpador de quests continua ativo após afastamento real/morte.
- `6e212061` — `ZombieManager.cs`: zumbis ociosos ainda fisicamente próximos e de home region retida continuam recebendo `OnUpdate`. Nenhuma duplicação de atualização para hunters/regions com players. Logs limitados a transições de bound `[ZMB-01]` em Development Build (retain, unload, reentry no spawn).

**Validação:** inspeção no GitHub, chaves C# balanceadas e `#if/#endif` coerentes; não houve compilação, Dev Build ou prova do usuário. `ZMB-POI-STREAM-03` é **QUASE PRONTA — AGUARDANDO BUILD**; `ZMB-01` (pai), `ZMB-02` e `NET-01` **EM IMPLEMENTAÇÃO**. Faltam testes de zombies e boss em borda de POI, distância 30–140m, ida/volta, dano/morte real, agressão a NPC móvel, visibilidade de origem e estabilidade de rede, profiler 1/20/50/200 zumbis. Nenhum caminho global/A* novo é prometido. Documentação especializada: `ZOMBIE_CROSS_REGION_NAVIGATION_DESIGN.md`, plano de testes seção 54.

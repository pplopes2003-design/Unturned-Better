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

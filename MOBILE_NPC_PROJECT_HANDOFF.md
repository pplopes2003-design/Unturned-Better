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

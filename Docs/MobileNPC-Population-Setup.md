# Mobile NPC population — runtime bootstrap and current behavior

Branch: `feature/npc-ai-foundation`

## Runtime integration

- `MobileNPCPopulationBootstrap` creates a runtime-only humanoid template using the game's `Resources/Characters/NPC_Client.prefab` as a visual child.
- The template receives the server-local brain, motor, perception, disposition/faction, vitals, combat, visual adapter, inventory, survival needs, looting, medical use, settlement planner/builder, group membership, and companion components.
- The population director currently spawns around vanilla player-spawn anchors and uses a configurable active-population cap. This is not a fully map-wide population sampler.
- The prototype remains single-player/local-server oriented. It must not be considered multiplayer-ready without replication and client-side movement presentation.

## Roaming and terrain

- Roaming/home territory defaults to a radius of **220 metres** around the current home anchor.
- On open terrain, NPCs choose successive ground-validated waypoints roughly 24–72 metres ahead, generally maintaining their previous direction. Near the territory edge they turn inward.
- They also prefer approaching nearby large map objects, which can lead them toward buildings. If no suitable structure exists, they continue over open terrain.
- Local steering checks obstacles and the next short ground segment. If terrain validation rejects all directions, the current roaming waypoint is abandoned and a new one is sampled.
- A stuck-movement watchdog detects very low movement over repeated checks. Roaming goals are discarded; tactical destinations attempt alternate local steering.
- These are bounded local-route and ground checks, **not full navmesh pathfinding**. Complex interiors, doors, steep terrain, cliffs, and map-specific collision layers still need in-game testing.

## Groups: current decision rules

- `MobileNPCGroupMember` assigns a new NPC to the nearest eligible group within **140 metres**, or creates a new group if none qualifies.
- Groups currently allow at most **4 living members** and only combine NPCs whose role and faction match.
- The first member becomes leader. If a group leader dies, the living group can elect/use a successor through the existing group bookkeeping; delayed replacements can rejoin a still-living former group when eligible.
- Group membership is runtime state. It is not yet a persistent, fully simulated social system: it does not independently model friendship, morale, supply lines, patrol schedules, or a sophisticated command hierarchy.

## Settlement/base selection and construction

- Only the living group leader may plan a settlement.
- Planning begins after a delay and requires at least one construction-category item in the NPC inventory (barricade, storage, or structure).
- The planner tests up to 10 candidate points about **7–18 metres** from the group's current home anchor, checking safe ground, slope, and local clearance.
- Once a site is accepted, the group shares that anchor and its members' home anchors are updated.
- The builder uses a constrained pattern, not free-form base design: it attempts a central storage barricade when supplies permit, then up to four defensive pieces around cardinal perimeter positions. Defaults are five total pieces, a 12-second initial delay, and 18 seconds between build attempts.
- The builder prefers compatible vanilla structures/barricades from carried inventory and can transfer non-construction supplies into its central vanilla storage once that storage is available.
- Site choice is not yet a strategic assessment of nearby loot, water, defensibility, faction borders, terrain chokepoints, or distance to the player. Those are future improvements.

## Trading

- The interaction controller supports item barter, XP, and vanilla Canadian notes/coins as payment modes.
- Cash items use the vanilla IDs: $5 note 1051, $10 note 1052, $20 note 1053, $50 note 1054, $100 note 1055, $1 coin 1056, and $2 coin 1057.
- Cash purchases are item-based, remove notes/coins from inventory, and return change using the same denominations. Prices are prototype values and need balancing.
- XP remains available as a fallback while the cash flow is tested. A later iteration can make the currency asset/configuration selectable instead of hardcoding Canadian denominations.

## Suggested test pass

1. Let an NPC roam on a broad beach/field and check that it continues through multiple long waypoints instead of repeatedly returning to its spawn point.
2. Watch it approach a nearby building and attempt to get around a slope, step, wall, or rock. Record the exact spot if it gets stuck.
3. Spawn same-faction NPCs near each other and confirm the group stays at four members or fewer; then test a different role/faction nearby.
4. Give a group leader construction items and allow enough time for a site decision and several build intervals. Confirm that only one group member builds the shared pattern.
5. Buy an item using a mix of notes/coins where exact payment is impossible, and verify that the change and inventory are correct. Repeat with insufficient money and a full inventory.

## Validation boundary

The source has received static review, but Unity compilation and in-game behavior have not been verified in this environment. The runtime tests above are necessary before treating these systems as working features.


## Settlement crafting (experimental)

The settlement builder first requests a storage-producing native recipe when the group has no central storage, then attempts other compatible placeable recipes when it lacks a buildable to place. It searches loaded vanilla/workshop item assets and only accepts a deliberately narrow subset of recipes: one placeable output, discrete-slot inputs, at least one consumed ingredient, and no target-item operation, skill requirement, map restriction, nearby workstation tag, static tag, or NPC quest condition. Inputs are taken only from the NPC's own runtime inventory; no ingredients are created for free.

This is an initial safe subset, not yet full parity with the player's crafting menu. Recipes using stacked-amount counting, tools-only inputs, multiple outputs, repair/refill, special operations, workstation requirements, or NPC conditions are skipped. The builder can stage missing ingredients from the central vanilla storage container into the leader's simulated inventory, but crafting/building progress is not persisted as a separate NPC recipe queue. These limits should be verified in the development build before expanding the supported recipe set.

Suggested test: give a settlement leader the exact ingredients for a vanilla recipe that produces a barricade or structure, ensure it has no buildable in inventory, then observe the log for a successful craft and confirm that the resulting item is consumed by a later placement attempt. Repeat with a missing ingredient and confirm it does not craft.


## NPC appearance variation

The mobile NPC template now attaches an appearance component that configures the native `HumanClothes` visual system on each spawned NPC. It chooses available vanilla face, hair, and beard assets, then applies a small palette of skin and hair colors. The intent is to give each NPC a recognizable generic face and variation rather than merely tinting the same blank mesh. This reuses game assets; it does not generate new face textures or use workshop/player cosmetics.

The settlement crafting attempt can now also stage missing recipe ingredients from its central vanilla storage into the leader's simulated inventory. This is limited to the same safe recipe subset documented above. Ingredients that cannot fit or are unavailable are not fabricated; staged ingredients may remain in the NPC inventory if another recipe requirement is missing.

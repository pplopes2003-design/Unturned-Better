# Linha 2 — ADR: NPC brain and vehicle controller are separate

Status: **Accepted design decision** (2026-11-10). Implementation of NPC vehicle driving remains pending.

## Ownership

**NPC brain / director** (existing `PrototypeActor`, `Line2MobileNPCBrain`, `Line2MobileNPCTactics`):
- Chooses an intent such as walk, investigate, pursue, retreat, embark, drive-to, disembark.
- Owns threat perception, confidence, memory, squad orders, medical priorities and faction policy.
- Does not edit vehicle Rigidbody, steering, engine, seat arrays or network state.
- Requests a vehicle action by ID and destination, with a deadline and tactical reason.

**NPC vehicle controller** (future independent component/service):
- Owns one server-authoritative vehicle reservation and its lifecycle.
- Validates asset and physics profile, seat topology, actual seat occupancy, locks, fuel, damage, ground clearance and route.
- Finds a navigable exterior boarding point; controls boarding, acceleration, braking, steering, exiting and recovery via supported native game mechanisms.
- Produces status/progress/failure to the NPC brain; never creates a fake SteamPlayer.
- Does not select enemies, invent player coordinates or decide squad strategy.

## Contract (design; NOT wired to game yet)

Request:
`{ npcId, vehicleInstanceId, operation: approach|board|drive|stop|exit|cancel, destination?, expiresAt, squadId? }`

Response:
`{ state: rejected|approaching|boarding|seated|driving|blocked|arrived|exiting|complete, reason, lastProgressAt }`

The controller must reject stale requests and only commit if server authority, valid vehicle, validated route and real seat occupancy checks pass. Vehicle reservation is exclusive, expires, and is released on death, vehicle destruction, emergency disembark, NPC despawn or timeout. Never overwrite `Passenger.player` or invoke player-only `VehicleManager.ReceiveEnterVehicleRequest` using dummy users.

## Compatibility layering

1. Native ground cars first using validated Unturned physics; prove acceleration/braking/obstacle avoidance before passengers.
2. Other ground engines and multi-seat assets, with driver/passenger and turret controllers distinct.
3. Workshop vehicles by capability inspection (engine type, seat transforms, wheel/physics profile, turrets, script hooks). Unknown configurations are unsupported by default, not forced through car steering.
4. Air/sea/train modes each require dedicated navigation/physics adapters.
5. Multiplayer: dedicated authoritative NPC+vehicle simulation and replicated seat/vehicle state; never trust remote client requests for NPC driving.

## Acceptance tests

- NPC stays on A* path to the exterior door/seat and does not teleport.
- No duplicate reservations, ghost drivers or stolen player seats.
- Stop and release controls on vehicle damage/death, NPC death, out-of-fuel, invalid path, blocked entrance and save/load.
- Workshop car without a supported physics adapter is skipped safely.
- NPC and vehicle decisions remain separable for localhost AI and eventual server execution.
- Driving adds no per-frame full-map vehicle scan.

Related source: `ClientModule/Line2NpcVehicleDiscovery.cs` is currently read-only; `Passenger.player` is SteamPlayer in game source. This ADR does not claim functional NPC driving.

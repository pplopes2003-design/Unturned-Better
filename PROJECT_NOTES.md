# Unturned-Better — Project Notes

## Project baseline

- Upstream source: https://github.com/SmartlyDressedGames/U3-SDK
- Private working repository: https://github.com/pplopes2003-design/Unturned-Better
- Unity editor version recorded in `ProjectSettings/ProjectVersion.txt`: **2022.3.62f3**
- Startup scene named by the SDK README: `Assets/GameStartup.unity`
- Package manifest: `Packages/manifest.json`
- Current development branch for baseline work: `chore/project-baseline`

## Working rules

1. Keep the official source as the `upstream` remote and the private repository as `origin`.
2. Make each feature on its own short-lived branch; keep commits focused.
3. Do not edit core gameplay until the project opens and a baseline build/test is confirmed.
4. Measure performance before and after optimization; avoid speculative changes.
5. Keep a record of Unity console errors and build results.
6. Before importing community assets, verify the creator's license and redistribution permissions.
7. Do not publish or redistribute modified builds until the SDK license and third-party notices have been reviewed.

## Roadmap

### Phase 0 — Baseline / audit
- [ ] Confirm Unity Hub has editor **2022.3.62f3** installed.
- [ ] Open the project and let package resolution finish.
- [ ] Open `Assets/GameStartup.unity`.
- [ ] Record console errors and whether Play Mode starts.
- [ ] Confirm Steam and Unturned are installed, as required by the SDK README.
- [ ] Review `LICENSE`, `THIRDPARTYNOTICES`, and SDK FAQ locally before distribution decisions.

### Phase 1 — Performance
- [ ] Establish a reproducible test scene/session and record FPS, CPU/GPU use, memory, and stutters.
- [ ] Profile before changing code.
- [ ] Implement only measured improvements and compare results.

### Phase 2 — Zombie AI
- [ ] Inspect existing zombie targeting, perception, navigation, and home/territory logic.
- [ ] Design states: idle/patrol, investigate, chase, search/lurk, return.
- [ ] Preserve multiplayer/network behavior where applicable; test single-player first.

### Phase 3 — NPCs and factions
- [ ] Define faction relationships and NPC data model.
- [ ] Implement ally/friendly behaviors, trade and loot exchange.
- [ ] Implement hostile raids and loot theft with configurable rules.
- [ ] Add save/load and performance safeguards.

### Phase 4 — Community assets
- [ ] Track each asset's author, source URL, license, modifications, and required attribution.
- [ ] Only include assets with compatible permissions.

## Current constraints / unknowns

- The Unity editor has not yet been confirmed installed or the project opened successfully.
- No local Unity compile or Play Mode test has been reported yet.
- The source release's current license terms and third-party notices must be checked before distributing any modified build.

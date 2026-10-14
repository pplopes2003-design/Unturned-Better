# Zombie pursuit outside POIs — implementation notes and final Dev Build checklist

Date: 2026-10-13. Work branch: `feature/npc-ai-foundation`. Canonical permanent backlog: `MOBILE_NPC_PENDING_TASKS.md`, issue `ZMB-01` (P0) and `ZMB-02` (P1). **SOURCE ONLY — not compiled or tested in game.**

## Scope and authority

The design preserves the original Unturned zombie actor, native `seeker` interface, collision `CharacterController`, player/NPC target priority, game damage, per-zombie roles, server authority and relevant map/level safety. It does **not** create a fake `Player`, substitute unlicensed A* source, teleport through obstacles, broadcast zombie awareness or change native client region IDs. The original goal is an animalistic pursuit threat beyond POI and navigation region boundaries, not universal pathfinding.

## Known root causes and source-level fixes

| Problem | Source change | Commit |
| --- | --- | --- |
| Zombie abandoned an already acquired player as soon as `player.movement.nav == 255` | Removed just that automatic bailout; inaccessible swimming retains escape | `66caf131d595011b582571252376b0a78bc49a4e` |
| A zombie hunting from a now-unoccupied origin region was still ticking but might not receive `OnUpdate` / stuck or snapshot bookkeeping | The existing globally-budgeted `tickingZombies` loop also calls `OnUpdate` for living hunting zombies whose home region has zero players, and not for those covered by occupied-region update pass | `067bc2f99da31175b80d27ef76e623def031ec8c` |
| A player or NPC left a nav mesh, native seeker cannot always resolve a nearby open-ground path | Added physically collision-checked, grounded, short-range (1.9–24m) movement, with slope/height/water/map-clip validation; otherwise defer to native `seeker.Move` | `c23e6addf9f5a144e6ec84f65a24d1686781114c`, `143a47fe970f132feb4d18ffbded823823f242f1` |
| Open-ground path is blocked by a nearby corner/short obstacle | Search at most four physically checked 2.5m side-angle waypoints (±55°, ±80°), choose best toward target, follow for up to 2s with every step rechecked; native navigation fallback when no safe waypoint. Recompute candidates at most once / 0.65s | `6b4465bbb59fc9300708b7a615861ccf0bfb1694` |
| Corner detours could persist after death/reuse/deaggro or get stuck forever / prevent scan cooldown | Reset state on reset/stop/retarget; three stalled attempts trigger a 6s native-navigation fallback; moving target invalidates active waypoint; no candidate doesn't cancel scan cooldown; supports zombie returning to a POI while itself outside nav | `715eb8304ebc39ad662e8506b1d78731c3903b99`, `d07e66fde6cab24b61d7887bff499409a56195c6` |
| Shots and explosions on playable ground outside any navigation region were silently discarded | Real 3D noise hearing no longer requires `tryGetNavigation`; retains server, finite coordinates, playable map/clip and spatial cooldown validation | `0ebc2b77a900063953aedfefb3e0fe332566bda2` |
| Zombie heard a sound off-nav but could not move to investigate | Reuse the same physically bounded obstacle-aware short-range `POINT` walking for an authentic sound's position. This is sound investigation, **not** knowledge of the shooter's location or automatic player acquisition | `aed6443b6184e9027f00c4aab8a2e8a0a5a81d81` |

## Corner avoidance behavior (source state machine)

1. Existing zombie hunt or genuine point alert is active, server authoritative. A player/mobile NPC target is alive; or a valid sound point is retained as `EHuntType.POINT`. `isStunned` / `isLeaving` / returning-to-spawn / dead actors cannot use fallback.
2. **Both** pursuer and target within normal navigation => use the native pathfinder. Otherwise consider local fallback only at horizontal distance 1.9–24m and vertical delta <=2.5m.
3. If the direct forward ray is clear and future floor valid, move a small capped time step via `CharacterController.Move`. No goal teleport.
4. If direct ray is obstructed, prefer the currently valid 2s detour if the target hasn't moved >6m. Otherwise (at most once per 0.65s) test four sideways 2.5m candidates: no wall on ray, slope <=38°, height difference <=0.65m, solid floor, no water, within playable map/clip volume, and not standing on player/NPC/zombie colliders. Prefer candidate minimizing squared distance to target. Waypoint revalidated each physical step.
5. If no candidate, native seeker receives the tick normally; do not march through a wall. Detect negligible travel over 0.8s; after three stalls defer to native pathfinding for 6s, then permit another bounded attempt. Deaggro, reset and target change clear stale state.
6. A target returning to navigable ground doesn't abandon the pursuer if **the zombie itself** remains outside the nav region. Original native seeker regains control when both have normal nav.
7. Do not reinterpret a sound as visual contact. Player priority acquisition and NPC faction relations are unchanged.

## Resource budget

- Only existing server-side ticking chasers are updated outside empty home regions, not the entire global idle population.
- Local detour checks are bounded by 4 candidates per rescan (>=0.65s apart) plus 1 physical step/active hunt tick and a progress check every 0.8s. No heap route lists, graph baking, global world-map cache or dynamic imports.
- Zombies beyond 24m from point/target remain with native movement; original dead-end path behavior may continue. No claim of universal route connectivity.

## Remaining work (do not close ZMB-01/ZMB-02)

**Native engine A* coverage:** U3 SDK has an interface but not the licensed A* Pathfinding Project implementation source, which means this repository cannot safely promise long-distance navigation between disconnected nav meshes. Map-to-map junctions, doors, staircases, bridges, obstacles wider than 2.5m, cliffs, deep water, static obstacles spawned by players and chase beyond 24m still need geometry/path verification and possibly licensed/runtime integration.

**Network interest:** `ZombieManager` stores zombies in a home `bound` region with fixed `id` and `ReceiveZombieStates` resolves `regions[reference].zombies[zombieIndex]`. The send path `GatherRemoteClientConnections(zombie.bound)` targets clients subscribed to the original region; simply sending these updates to nearby clients in another region would cause an unnetworked-region rejection / unknown zombie ID. Must design a compatible per-client interest registration/spawn protocol instead of broadcasting out-of-region IDs. This is not fixed by the new local movement code.

**Movement / animation:** Direct `CharacterController.Move` is server-only; check native attack animation, effect steering, root-motion assumptions, footstep/stuck detection, replication, passenger vehicles, and side-wall collisions in the Dev Build. Multiple off-nav movers can collide without gaining special global obstacle avoidance.

**Audio:** Noise uses 3D radius and spatial cooldown. Need test walls/noise occlusion and perceived loudness before claiming all events realistic. Off-nav `POINT` pursuit only responds within 24m via local fallback; farther noises still depend on native seeker.

## Suggested final game validation

- Pursue player and moving friendly/hostile NPC across POI boundary; temporarily move player back inside while zombie outside; observe whether zombie can catch up.
- Fire weapon / detonate explosion 10–20m outside nav regions and behind at least one near corner; zombies inside radius should investigate a real sound, not instantly know player location. Do not react outside radius.
- L-shaped outdoor corner, a single obstacle, solid building, door, road, puddle, 40° incline, steep cliff, narrow gap, shoreline and map clip volume. Observe native fallback and no wall penetration.
- 1, 20, 50 and 200 zombie scenarios with logs and profiler. Pay attention to physics-raycast calls and unusual movement/stuck loops.
- Multiplayer client outside zombie's home region: treat visibility and damage replication as pending `NET-01/ZMB-01`, not a green test.
- Gather `Player.log`, map name, Dev Build version, nav and current position if possible. Do not mark any feature resolved before user's final compile and actual feedback.

**Future conversation rule:** read this file along with `MOBILE_NPC_PENDING_TASKS.md` and `MOBILE_NPC_PROJECT_HANDOFF.md` before changing zombie navigation again. Never erase pending issues without runtime verification.

## 2026-10-13 — Bounded longer open-ground chase, after user requested next priority

Commit `2cf9669b42e7f4230963e48b403ac2a5033dcb1d` in `Assets/Runtime/Assembly-CSharp/Unturned/Zombies/Zombie.cs` fixes a concrete remaining range cutoff in the existing *already-acquired target* chase fallback. Previously the local off-nav physical `CharacterController.Move` helper returned immediately beyond **24m**, even for a living player/mobile NPC running along completely open outdoor ground. It now accepts an already-tracked living actor out to **64m** **only when** the next physically checked **3m lookahead** has no BLOCK_COLLISION obstruction, ground ahead passes the established slope/elevation/map/underwater checks, and server-side pursuit state still authorizes movement. Every step uses the physical controller; this does not teleport, share hidden target intelligence or bypass walls. A blocked long-range (>24m) approach returns to native `seeker.Move` rather than attempting a fake long-range detour. The original ±55/±80 degree physical detour logic remains limited to 24m; noise investigation POINT remains limited to 24m and never learns the source's exact location. Beyond 64m, original native behavior remains.

**Status:** `ZMB-OPEN-01` (scoped physical off-nav open-ground pursuit 24–64m) source **QUASE PRONTA — AGUARDANDO BUILD**, **not compiled or runtime-tested**. Parent `ZMB-01` **EM IMPLEMENTAÇÃO** because unknown wide obstacles/nav disconnected POIs, per-client region-interest registration and actual Unity performance remain unresolved. `ZMB-02` also open for audio/visual behavior. On user Dev Build feedback, close only demonstrated scope, otherwise reopen with map/coords/Player.log. Test cases: player/NPC running along clear ground at 18, 30, 50, 65m from pursuer outside POI; fence appearing after 10m vs directly ahead; water/steep terrain, turned corner, nearby sound POINT at 30m (must not trigger privileged 64m pursuit); 1/20/50/200 zombies and native network interest separately.


## 2026-10-14 — Streaming robusto no single-player: posição real, retorno à região e chefes

Esta rodada começa do HEAD `b0d17be341652fead7f271e1879dff587c4a74db` e preserva os ajustes anteriores de movimento físico fora de nav (até 64 m em terreno aberto) e alertas de ruído. **Não altera IDs de rede do Zombie nem cria pacotes cross-region**, portanto `NET-01` continua aberto.

**Defeitos concretos revelados pelo código:**
1. O commit anterior `d304e89b` só retinha regiões com `Zombie.isHunting` vivo. Um Zombie fisicamente ao lado do jogador podia sumir na troca de bound se tivesse perdido aggro ou estivesse entre estados de perseguição. Além disso, um caçador distante poderia manter região para sempre.
2. `ZombieManager.onBoundUpdated()` chama `generateZombies(newBound)` na reentrada mesmo que `regions[newBound].isNetworked` já seja verdadeiro após retenção. A rotina zera `region.alive` e pode adicionar zumbis; não deve ser chamada sobre lista/IDs de zumbis já existentes.
3. `ZombieRegion.UpdateRegion()` aplica `bossZombie.askDamage(50000,...)` quando nenhum player permanece no bound de origem. Um boss em perseguição física além da POI pode morrer automaticamente apesar da retenção do seu ZombieRegion.
4. Zumbis não caçadores de regiões mantidas, se próximos, não recebem `Zombie.OnUpdate`, pois o loop padrão acompanha somente regiões ocupadas e hunters na lista ativa.

**Alterações em fonte:**
- `280acfcf` `ZombieManager.cs`: `ShouldRetainZombieRegionForLocalPlayer(bound)` só para servidor local **não dedicado**, com localização válida de `Player.LocalPlayer`; mantém região quando algum zombie vivo está a até **48m** do jogador, mesmo ocioso, ou `isHunting` a até **112m**. Ambos limites são **de retenção/streaming, não percepção nem velocidade**. Descarrega por sweep padrão a cada 3s após distanciar e ficar sem perseguidor elegível. Reentrada no bound mantido reutiliza lista e ids existentes; `generateZombies` executa somente quando `isNetworked == false`. Multiplayer/dedicado mantém política legada, interesse por bound ainda não resolvido.
- `79e05b1e` `ZombieRegion.cs`: impede que limpeza nativa de missão mate chefe vivo próximo ou caçando no raio 112m enquanto player está fora do bound em host local. Ao se distanciar, morrer, ou abandonar a luta, volta ao cleanup original que mata boss/permite tentar missão outra vez. Sem invulnerabilidade artificial no combate.
- `6e212061` `ZombieManager.cs`: `OnUpdate` de zumbis **não caçando**, vivos e fisicamente próximos (<=48m) nos bounds originais sem player que ainda estão retidos. Caçadores e regiões ocupadas continuam nos loops existentes, evitando duplicação de `OnUpdate`. Logs `[ZMB-01]` sob `UNITY_EDITOR || DEVELOPMENT_BUILD`: região retida, região descarregada e reentrada sem regeneração.

**Limitações/verdade:** revisão estática de chaves e pré-processador passou, **sem Unity compile nem teste real de Dev Build**. Não se pode afirmar que a perseguição atravessa obstáculos grandes, que rede multiplayer vê perseguidores sem home bound, nem que deaggro, perseguição de NPCs distante e sons estão aprovados. O alvo pode ficar além da capacidade de 64m de deslocamento off-nav; não fingir rotas globais. Perseguir NPCs longe do jogador **não** ativa streaming ilimitado: a retenção desta rodada é deliberadamente ancorada no jogador local para conter uso de memória/CPU. Chefes por missões e streaming em dedicado/multiplayer precisam de testes específicos.

**Teste mínimo:** zumbi comum em caça na borda POI, player sai do bound mas permanece 20–40m de distância, observa persistência e movimento; repete com zumbi ocioso próximo, depois vai além de 112m e espera sweep; retorna à mesma região antes do descarregamento e confirma que mesmo `id` não foi recriado nem `region.alive` resetado; chefe ativo cruzando borda não morre sozinho. Confirmar via `[ZMB-01]` em Player.log e monitorar CPU/memória com várias transições. `ZMB-01` permanece **EM IMPLEMENTAÇÃO**; o recorte `ZMB-POI-STREAM-03` **QUASE PRONTO — AGUARDANDO BUILD**.

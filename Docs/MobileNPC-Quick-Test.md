# Mobile humanoid NPC quick test (development branch)

Branch: `feature/npc-ai-foundation`

## Current components

The opt-in humanoid prototype is under `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/`:

- `MobileNPCBrain` / `MobileNPCBrainDriver`: server-side state, target memory, tactical goals and decision events.
- `MobileNPCRegistry`: server-only registry of enabled mobile brain drivers for future faction/population systems.
- `MobileNPCSpawnService`: validates an explicitly supplied mobile humanoid prefab and creates it on the server with a selected role.
- `MobileNPCPopulationBootstrap` / `MobileNPCPopulationDirector`: runtime bootstrap subscribes to map-loaded events in local single-player, builds a hidden template from the game's humanoid visual prefab, and starts periodic population around the player. The spawned entities remain server-local and are **not network replicated**.
- `MobileNPCHumanoidPerception`: nearest living player within sight range, field of view and unobstructed line of sight.
- `MobileNPCHumanoidDisposition`: opt-in `Hostile`, `Friendly` or `Neutral` role. Hostile NPCs can engage players and opposing Friendly NPCs; Friendly NPCs engage Hostile NPCs but not players; Neutral NPCs engage neither faction.
- `MobileNPCHumanoidMotor`: server-side CharacterController movement with bounded autonomous roaming waypoints and short-range sphere-cast steering to try to pass nearby obstacles; combat/investigation destinations take priority. This is local avoidance, not navmesh/pathfinding, so walls and dense structures can still block an NPC.
- `MobileNPCVisualAnimation`: best-effort adapter for legacy animation clip names in the visual model.
- `MobileNPCHumanoidAim`: variable reaction time, movement/shot spread and suppression penalty.
- `MobileNPCHumanoidCombat`: server-authoritative melee damage.
- `MobileNPCHumanoidRangedCombat`: baseline hitscan damage, ammunition/reload simulation, variable bursts and pauses.
- `MobileNPCHumanoidVitals` / `MobileNPCTactics`: health-related tactical decisions, retreat/cover/loot goal helpers.

## Spawn service contract

`MobileNPCSpawnService.TrySpawnServer(...)` accepts only a prefab with a root `CharacterController`, `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, and `MobileNPCHumanoidDisposition`. It rejects client-side calls, null prefabs, and positions outside safe level bounds. The caller must supply a dedicated mobile prefab; do not pass `NPC_Server`, `NPC_Client`, or a legacy static-NPC prefab. The service initializes the server-side brain/motor and, when present, vitals synchronously before returning. `TryDespawnServer(...)` validates server authority and the mobile-NPC component signature before destroying the server instance; this is local lifecycle cleanup only, not a client-visible network despawn. It intentionally does not claim client replication, persistent saving, or full pathfinding. `TrySpawnBatchAroundServer(...)` samples candidates inside a caller-supplied circular region and rejects candidates too close to another NPC created in the same batch (horizontal spacing based on the prefab `CharacterController` radius). This is only a batch-local spacing heuristic: it does not check nearby pre-existing NPCs, full capsule/obstacle overlap, or map regions automatically.

## Quick smoke test

1. Open the project in the configured Unity Editor and wait for script import; check Console errors before launching.
2. Start a disposable single-player map. The bootstrap should log that the population was initialized; it must not run on a dedicated server.
3. Wait for the first spawn interval (default about 18 seconds). Confirm a visible humanoid appears outside immediate player range and that population remains capped.
4. Approach within sight range and line of sight. Confirm the hostile NPC detects and pursues the player; observe whether the available legacy animation clips switch sensibly between idle and movement.
5. Break line of sight: ranged combat should stop firing, aim tracking should clear, and the brain should investigate the last known position before returning.
6. Check ground candidates near steep slopes, structures and map boundaries. Failed candidates should be skipped without repeated per-frame spawn attempts.
7. Kill an NPC and verify the dead instance is cleaned up after the configured delay. Load a second map and confirm there is no duplicate population director.
8. When no target or tactical destination is active, confirm an NPC periodically chooses a nearby roaming waypoint, walks toward it, pauses briefly after arrival, then chooses another. Verify it stays within its spawn-area roaming radius.
9. Place or lure an NPC near a small obstacle. Check whether short-range steering chooses a clear side; then test walls and buildings to document cases where local avoidance cannot find a route.
10. Record any compiler errors, Console exceptions, invisible model, incorrect scale/orientation, bad animation selection, or NPCs stuck against obstacles.

This smoke test has not yet been run. The bootstrap is experimental and local-only; it does not add client replication, persistence, roaming, or base construction.

## Manual component test

If the runtime bootstrap cannot initialize, create a test GameObject with a valid visual/model, `CharacterController`, `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, `MobileNPCHumanoidDisposition`, `MobileNPCHumanoidAim`, `MobileNPCHumanoidVitals`, and one or both combat components. Set the role to `Hostile` for combat tests; `Friendly` and `Neutral` should not attack players.able reaction delays, misses, burst pauses, ammunition depletion and reload events. The damage model is a prototype hitscan implementation, not a fully integrated vanilla gun/inventory/projectile simulation.
6. Test NPC death by applying server-validated damage through `MobileNPCHumanoidVitals.ApplyDamage(...)`: health must reach zero once, the brain must reset, movement/collision and combat/perception must stop, the driver must leave the active registry, and the `Died` event must fire once. Healing and repeated damage after death must be rejected.
7. Exercise `TryDespawnServer(...)` with a live mobile NPC: on the server it should destroy the instance and remove its driver from the active registry; a client-side call and a non-mobile GameObject must be rejected. This checks server-local cleanup only, not network despawn.
8. Verify dedicated-server and client behavior separately.

## Faction smoke test

1. Spawn one Hostile and one Friendly mobile prefab with vitals and melee or ranged combat enabled. Confirm each acquires the opposing NPC only with line of sight, and damage is applied through the target's server-side vitals.
2. Spawn two NPCs from the same faction and confirm they do not acquire or damage one another. Neutral NPCs should not join faction combat.
3. Fire a mobile NPC's ranged weapon and confirm nearby vanilla zombies investigate the gunfire position. This is a noise reaction only; zombies do not yet treat mobile NPCs as full living attack targets.
4. Repeat with a Hostile shooter and a Friendly shooter. Both should produce the same zombie-noise stimulus.

## World population, settlement and war status

- A single-player runtime bootstrap now attempts automatic local spawning around the living player on each loaded map. This is source-level integration only and has not been compiled or tested; map-wide sampling and multiplayer replication are not implemented.
- Persistent regional populations, autonomous roaming, claiming territory, building/maintaining bases, resource gathering, storage, and long-term settlement are not implemented.
- Hostile-versus-Friendly acquisition and prototype melee/hitscan damage are now connected. Full native weapon integration and replication remain incomplete.
- Zombies currently react to mobile NPC gunfire by investigating its position. They do **not** yet select, chase, and attack mobile NPCs as full targets; that requires integrating NPC perception/health into the native zombie target and damage pipeline.

## Death and ragdoll status

- The mobile NPC currently **does not produce a ragdoll** when it dies. `MobileNPCHumanoidVitals` raises `Died` and disables its movement/combat components, but the visible body can remain in its last pose until a death presentation is added.
- Unturned already has `RagdollTool.ragdollPlayer(...)` and `RagdollTool.ragdollZombie(...)`, plus the `Characters/Ragdoll_Player` and `Characters/Ragdoll_Zombie` assets. These are not drop-in replacements for the mobile NPC: they expect a compatible humanoid skeleton and, for full appearance, player/zombie clothing data.
- `DeathPresentationRequested` is only a server-side hook; it does **not** send an RPC or replicate an entity. Correct multiplayer behavior requires the server to replicate a stable NPC identity plus death pose/impulse, and clients to create the ragdoll locally. Calling the visual ragdoll function only on the server would not make remote clients see it, and directly reusing zombie ragdoll would require zombie-specific appearance data.
- Next implementation step: establish stable network identity and spawn replication for mobile NPCs, then add an opt-in ragdoll presentation adapter using a compatible humanoid skeleton, the lethal-hit impulse, client-side visual spawning, and configurable cleanup. Until that is integrated with the entity replication protocol, ragdolls remain pending rather than being faked with a server-only visual.

## Important limits

- The role component only defines basic hostility toward players. Friendly following, dialogue, squad behavior, and inter-NPC relationships are not implemented yet; these belong to later behavior/faction work.
- **Multiplayer replication is not finished.** The motor currently moves the NPC on the server; a dedicated spawn/lifecycle and client snapshot/interpolation path is still required before remote clients can reliably see movement.
- Local movement is collision-aware but is not full pathfinding around obstacles.
- The ranged component uses server-side hit detection and damage, but is not yet bound to a real item gun, attachments, physical ammo inventory, native muzzle effects, or all vanilla gun sounds.
- Events such as `ShotFired`, `ReloadStarted`, and `ReloadFinished` are available for audio/animation adapters. Reuse existing game audio assets through the game's supported effect/audio pipeline; do not synthesize voice chat or change existing static NPCs.
- Do not merge to the release branch until Unity compilation and singleplayer/dedicated-server tests pass.

## Acceptance checklist

- [ ] Unity compilation succeeds with the configured editor/toolchain.
- [ ] Target detection respects sight range, field of view and obstacles.
- [ ] The NPC does not fire at a player it cannot currently see.
- [ ] Humanized reaction, aim spread, suppression, burst timing and reload work as configured.
- [ ] Dead NPCs cannot move, perceive or attack; death is emitted once and subsequent damage/healing is rejected.
- [ ] Server cleanup rejects clients and non-mobile objects; network despawn remains a separate acceptance item.
- [ ] Existing static NPCs, vanilla zombies and sentries retain their current behavior.
- [ ] Server-to-client spawn and movement replication are implemented and tested before multiplayer acceptance.
- [ ] Population spawning works on arbitrary supported maps without hand-placed NPCs.
- [ ] NPCs can build, defend, repair and persist bases/settlements in claimed regions.
- [ ] Hostile and Friendly factions fight one another using server-authoritative damage.
- [ ] Vanilla zombies can perceive, pursue and damage both mobile NPC factions, not merely investigate their gunfire.

## Testes adicionais — recuperação de bloqueio e passos
- [ ] Caminhe perto de uma parede/canto: o NPC tenta direções laterais mais amplas em vez de insistir apenas no primeiro ângulo.
- [ ] Observe um NPC que ficou preso durante exploração: após detectar pouco deslocamento, ele abandona somente o waypoint de exploração e tenta outro depois de uma pausa.
- [ ] Durante perseguição/investigação, verifique que a recuperação não apaga o destino tático do cérebro.
- [ ] Caminhe sobre materiais diferentes (terra/grama, concreto, madeira, metal quando disponíveis): os passos devem usar as definições vanilla do material (FootstepWalk/FootstepRun) quando existirem.
- [ ] Confirme que os passos não se repetem continuamente quando o NPC está parado e que a compilação dedicada não depende do código de áudio.

**Limite:** os testes acima ainda não foram executados no Unity/Unturned. A recuperação é heurística local, não pathfinding; vocalizações, sons de dano/morte e áudio de armas ainda não estão ligados.

## Teste adicional — robustez do spawn
- [ ] Em terreno plano, confirmar que NPCs continuam surgindo no anel ao redor do jogador.
- [ ] Em região íngreme ou obstruída, confirmar que o diretor tenta outros setores antes de desistir.
- [ ] Verificar que cada rodada respeita o limite configurado de tentativas e não cria NPCs em terreno inválido ou sobrepostos.
- [ ] Observar se NPCs aparecem dentro do campo de visão do jogador; o sistema ainda não garante spawn fora da linha de visão.

**Estado:** código atualizado e revisado estaticamente; a validação no Unity/Unturned permanece pendente.


## Teste adicional — spawn fora da linha de visão
- [ ] Em terreno aberto, confirmar que candidatos com linha de visão direta são rejeitados antes da instanciação.
- [ ] Repetir atrás de uma parede/edifício: o NPC pode surgir se o raio até o tronco for bloqueado por collider sólido.
- [ ] Em mata/vegetação e áreas com estruturas transparentes, registrar falsos positivos/negativos causados pelos colliders do mapa.
- [ ] Se todos os candidatos estiverem visíveis, confirmar que não há aparição seguida de destruição e que o diretor aguarda o próximo intervalo.

**Limite:** a ocultação é uma heurística de raio/collider, não uma avaliação da câmera nem do sistema visual completo. Ainda sem compilação Unity ou execução no jogo.


## Teste adicional — âncora de território doméstico
- [ ] Depois da inicialização, confirmar que `HomePosition` corresponde à posição de spawn e `IsWithinHomeTerritory` retorna verdadeiro.
- [ ] Chamar `TrySetHomePositionServer` com uma posição em terreno seguro: a âncora muda e o waypoint de roaming antigo é descartado.
- [ ] Fornecer uma posição sobre terreno íngreme/sem chão válido: a mudança deve falhar e preservar a âncora anterior.
- [ ] Confirmar que novos waypoints de exploração permanecem dentro do raio doméstico configurado.
- [ ] Confirmar que o recurso não é tratado como persistência: ao reiniciar/recriar a entidade, a âncora volta a ser capturada do spawn.

**Limite:** ainda não há construção, reivindicação de território, salvamento ou coleta real de itens.


## Teste adicional — retorno ao território doméstico
- [ ] Deslocar o NPC para fora do raio doméstico sem alvo/objetivo ativo: ele deve caminhar de volta à âncora sem teletransporte.
- [ ] Enquanto o NPC persegue/investiga um alvo, confirmar que o destino do cérebro continua tendo prioridade mesmo fora do território.
- [ ] Após terminar o objetivo, confirmar que a recuperação doméstica retoma o controle e que o roaming só volta após reentrar no território.
- [ ] Colocar uma parede ou estrutura entre NPC e âncora: documentar que a evasão local pode não encontrar rota em torno de obstáculos grandes.

**Limite:** teste ainda não executado no Unity/Unturned; a recuperação depende de steering local, não de pathfinding global.


## Teste adicional — armazenamento virtual de NPC
- [ ] Anexar `MobileNPCInventory` a um NPC móvel e confirmar capacidade inicial/slots livres.
- [ ] No servidor, armazenar um item já transferido e verificar que o snapshot preserva ID, quantidade, qualidade e estado.
- [ ] Alterar o array `state` do snapshot retornado e confirmar que o item armazenado não é alterado (cópia defensiva).
- [ ] Preencher todos os slots e confirmar que novas inserções falham sem alterar os slots existentes.
- [ ] Remover um slot e confirmar que o item sai uma única vez.
- [ ] Confirmar que este componente sozinho não faz pickup de drops, não salva em disco e não sincroniza inventário para clientes.

**Limite:** ainda não conectar a inserção à coleta de mundo antes de existir transferência atômica server-authoritative de drops.


## Teste adicional — coleta autoritativa de item
- [ ] Em single-player, deixar um item registrado no mundo a poucos metros de um NPC com espaço livre.
- [ ] Invocar `TryPickupWorldItemServer` com o `instanceID` real e confirmar que o item aparece em um slot do inventário NPC e desaparece do chão.
- [ ] Confirmar que clientes/região não recriam o drop após a sincronização vanilla.
- [ ] Repetir com inventário cheio, ID inexistente, NPC longe e servidor desativado: a operação deve retornar `false` sem remover o item.
- [ ] Testar dois pedidos para o mesmo `instanceID`: somente o primeiro pode transferir.
- [ ] Confirmar que item simulado sem `ItemData` autoritativo não é coletado.

**Limite:** operação de transferência existe, mas a IA ainda não procura itens nem chama automaticamente o método ao chegar ao destino.


## Coleta automática (protótipo)
- [ ] Criar NPC com espaço livre no inventário e deixar um item físico registrado no mundo dentro do raio de busca.
- [ ] Confirmar que o NPC escolhe o drop mais próximo e caminha até ele.
- [ ] Confirmar que, ao chegar, o drop desaparece e `MobileNPCInventory.UsedSlots` aumenta.
- [ ] Repetir com inventário cheio, item já recolhido, alvo de combate ativo e item próximo à fronteira de região.
- [ ] Confirmar que o NPC não abandona um combate para recolher item e que não há exceções no console.

**Ainda sem validação em execução Unity.**


## Prioridade contextual do loot
- [ ] Soltar uma arma e um item comum a distâncias parecidas: sem arma armazenada, o NPC deve preferir a arma.
- [ ] Com arma armazenada, verificar que munição tem prioridade maior do que quando não há arma.
- [ ] Com vida abaixo de 75%, verificar que itens médicos sobem na lista de candidatos.
- [ ] Verificar que itens com o mesmo ID já guardado perdem prioridade e que itens comuns perdem prioridade quando restam dois espaços.
- [ ] Verificar o ajuste `buildingLootPreference` para itens de barricada, armazenamento, estrutura e suprimentos.
- [ ] Confirmar que o fator distância desempata candidatos, mas não supera necessidades críticas.

**Observação:** comida e água recebem prioridade genérica porque ainda não existem medidores de fome/sede para NPCs; a compatibilidade de munição e a comparação real de equipamentos ainda não são calculadas.


## Teste — inventário ligado ao combate à distância
- [ ] Com inventário vazio, provocar um alvo hostil: o NPC não deve disparar nem gerar tiros gratuitos.
- [ ] Arma armazenada sem carregador: o NPC reconhece o alvo, mas não dispara enquanto estiver sem munição.
- [ ] Arma + item `MAGAZINE` no inventário: confirmar que o recarregamento consome um carregador e só depois permite disparar.
- [ ] Esgotar o carregador simulado sem outro carregador: o NPC para de disparar e não recria munição.
- [ ] Recolher outro carregador: confirmar que volta a recarregar quando estiver em combate.
- [ ] Confirmar que combate corpo a corpo continua disponível independentemente de arma/munição.
- [ ] Testar categorias de carregador incompatíveis com a arma; esse caso ainda não é filtrado e deve ser registrado como limitação conhecida.
- [ ] Confirmar que armas e carregadores permanecem no inventário quando não foram usados/consumidos.


## Teste — seleção de arma e carregador compatível
- [ ] Guardar duas armas diferentes e confirmar que a escolha segue a pontuação de alcance, cadência e condição; não assumir que é DPS exato.
- [ ] Guardar uma arma com munição já indicada no estado vanilla e confirmar que o protótipo reconhece as balas iniciais se o carregador inserido for compatível.
- [ ] Guardar apenas carregadores de calibres incompatíveis: o NPC não deve consumi-los para recarregar.
- [ ] Guardar um carregador compatível: deve ser removido do inventário apenas quando começa a recarga.
- [ ] Verificar que a quantidade de tiros após recarregar respeita o valor lido e os limites configurados.
- [ ] Trocar por uma arma de maior pontuação durante o combate; confirmar que o estado da arma selecionada é atualizado sem consumir carregadores incorretos.
- [ ] Conferir no jogo se `Item.amount` representa a quantidade de munição esperada para os carregadores usados nos drops.
- [ ] Registrar exceções de estado nulo/curto e IDs de carregador inválidos.


## Arma móvel: áudio e estatísticas (teste manual pendente)

1. Inicie uma sessão single-player com NPC móvel e um item de arma válido no inventário do NPC.
2. Provoque combate com um alvo hostil: confirme que o modelo vanilla da arma aparece no esqueleto do NPC e que o som de disparo é ouvido com atenuação espacial.
3. Espere a munição acabar: confirme que o som de recarga é reproduzido ao iniciar a recarga.
4. Compare duas armas com alcances diferentes: a distância máxima de tiro não deve exceder o alcance positivo definido no asset nem o limite configurado no componente.
5. Confirme que arma sem clip de disparo/recarga continua funcional silenciosamente e que o bootstrap não altera NPCs estáticos.
6. Quando o inventário mudar para outra arma, confirme que o modelo visual acompanha a arma selecionada e que o modelo anterior é removido.
7. Este teste não foi executado no Unity; registre exceções, arma desalinhada, áudio duplicado ou diferenças de API antes de marcar a etapa como validada.


## Cadência, recarga e dispersão por asset

1. Teste duas armas com `Firerate` diferentes e compare a cadência média; o comportamento ainda deve incluir pausas de rajada e variação humana.
2. Teste armas com `Reload_Time` diferentes e confirme que o tempo de recarga segue o asset quando o valor é positivo.
3. Compare uma arma com espalhamento baixo e outra com espalhamento alto à mesma habilidade do NPC; a arma mais dispersa deve produzir maior variabilidade, sem remover o erro humano.
4. Confirme que zumbis investigam disparos dentro do alcance efetivo calculado para a arma.
5. Estes testes ainda não foram executados no Unity; registrar resultados antes de marcar como validados.

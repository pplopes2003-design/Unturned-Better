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
3. For a quick nearby test, enter the map, face an open flat area and press **F8**. The development helper attempts to spawn one NPC about 7 metres away, respecting the configured population cap; check the Unity/player log for success or the reason it was skipped. If F8 does nothing, ensure the game window has keyboard focus and the current build includes the latest branch commit.
4. Alternatively, wait for the normal spawn interval (default about 18 seconds). Automatic spawns are sampled around vanilla player-spawn anchors, so they can be much farther away and are deliberately kept outside immediate visibility.
5. Approach within sight range and line of sight. Confirm the hostile NPC detects and pursues the player; observe whether the available legacy animation clips switch sensibly between idle and movement.
6. Break line of sight: ranged combat should stop firing, aim tracking should clear, and the brain should investigate the last known position before returning.
7. Check ground candidates near steep slopes, structures and map boundaries. Failed candidates should be skipped without repeated per-frame spawn attempts.
8. Kill an NPC and verify the dead instance is cleaned up after the configured delay. Load a second map and confirm there is no duplicate population director.
9. When no target or tactical destination is active, confirm an NPC periodically chooses a nearby roaming waypoint, walks toward it, pauses briefly after arrival, then chooses another. Verify it stays within its spawn-area roaming radius.
10. Place or lure an NPC near a small obstacle. Check whether short-range steering chooses a clear side; then test walls and buildings to document cases where local avoidance cannot find a route.
11. Record any compiler errors, Console exceptions, invisible model, incorrect scale/orientation, bad animation selection, or NPCs stuck against obstacles.

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


## Persistência de munição entre trocas de arma

1. Dê ao NPC duas armas com estados de item válidos e munições compatíveis.
2. Faça a primeira arma disparar algumas vezes; force a troca para a segunda arma e depois volte à primeira.
3. Confirme que a primeira arma mantém a quantidade simulada restante, sem restaurar munição já disparada.
4. Faça uma recarga e confira que o ID do carregador e a quantidade simulada permanecem no estado do item após trocar de arma.
5. O teste depende de estado de arma com pelo menos 11 bytes e permanece pendente de execução no Unity.


## Uso de item médico por NPC móvel (teste manual pendente)

1. Em uma sessão single-player, reduza a saúde de um NPC móvel para abaixo de 55% e coloque um item da categoria `MEDICAL` no inventário de teste do NPC.
2. Sem alvo de combate nem objetivo tático ativo, confirme que o NPC consome um item médico e recupera aproximadamente 30% da saúde máxima (limitado à saúde máxima).
3. Confirme que não consome item médico quando a saúde está acima do limiar, quando está morto ou enquanto combate/objetivo tático está ativo.
4. Confirme o intervalo entre usos e que a cura não ultrapassa a saúde máxima.
5. Teste o caso de cura rejeitada e verifique que o item é devolvido quando há espaço.
6. Este é um efeito de cura simulado comum a todos os itens `MEDICAL`, não a implementação dos efeitos vanilla individuais; não foi executado no Unity.


## Planejamento de local de assentamento (teste manual pendente)

1. Dê a um NPC móvel pelo menos um item da categoria `BARRICADE`, `STORAGE` ou `STRUCTURE` no inventário de teste. Também deixe alguns desses itens como drops próximos para testar a prioridade de coleta.
2. Deixe-o sem alvo e sem objetivo tático; após o atraso configurado, confira se ele escolhe uma posição de terreno seguro próxima e atualiza sua âncora de retorno.
3. Teste terreno plano, declive forte, locais próximos a paredes/objetos e locais sem espaço suficiente. A seleção deve rejeitar pontos inválidos e tentar novamente mais tarde.
4. Sem itens de construção, confirme que não escolhe um novo local.
5. Este teste valida apenas o planejamento do ponto. Nenhuma estrutura é construída nesta etapa; não marcar como construção funcional de base.
6. O teste não foi executado no Unity.


## Colocação de barricadas do assentamento (teste manual pendente)

1. Use ambiente de teste/single-player e dê ao NPC um item de barricada permitido (por exemplo, uma fortificação ou armazenamento) no inventário.
2. Confirme que o NPC primeiro escolhe um local e que não constrói enquanto está em combate ou executando outra tarefa tática.
3. Observe se uma barricada aparece no mundo, é replicada para o cliente e o item correspondente é removido do inventário uma única vez.
4. Confirme que o limite configurado de peças e o intervalo de construção são respeitados.
5. Teste perto de paredes, árvores, objetos, terreno inclinado, regiões não válidas e zonas protegidas. A checagem é aproximada; se surgirem colocações inválidas, interrompa o teste e corrija antes de usar em servidores públicos.
6. Confirme que sentries, charges, safezones e outros tipos excluídos não são colocados.
7. A contagem não persiste após recriação do NPC e não há ainda persistência do assentamento.
8. O teste não foi executado no Unity.


## Ordem e distribuição das peças (reteste manual pendente)

1. Coloque no inventário do NPC ao menos uma barricada `STORAGE` e algumas peças de fortificação.
2. Confirme que a unidade de armazenamento tem prioridade sobre outras barricadas compatíveis.
3. Observe as quatro tentativas de construção: os pontos devem ficar em direções aproximadamente cardeais e a distância do centro deve ser semelhante.
4. Bloqueie uma das posições com uma árvore/parede e verifique se o sistema tenta ângulos alternativos antes de desistir daquela rodada.
5. Verifique se o NPC não consome item quando `dropBarricade` falha e se consome apenas uma unidade quando a colocação é confirmada.
6. Repetir em terreno irregular e próximo de regiões protegidas; interromper se o jogo aceitar colocações inválidas.
7. Ainda não há snapping, validação integral de claim/suporte, interação do NPC com o inventário do armazenamento, ou persistência das peças.


## Padrão fixo de posto (teste manual pendente)

1. Em ambiente de teste, forneça ao NPC uma unidade de armazenamento e pelo menos quatro peças de barricada/fortificação permitidas.
2. Confirme que ele tenta colocar o armazenamento no centro do local escolhido antes do perímetro.
3. Confirme que as peças defensivas são tentadas em posições cardeais com raio semelhante, sem inventar posições aleatórias quando um ponto está bloqueado.
4. Sem armazenamento, confirme que o NPC só tenta peças de perímetro permitidas. Itens não permitidos (porta, cama, escada, vidro, sentry, explosivo ou estrutura) não devem ser consumidos pelo construtor.
5. Verifique que um item só é removido do inventário quando a colocação retorna sucesso. Inspecione especialmente pontos próximos de paredes, terreno irregular e zonas protegidas.
6. Este padrão ainda não tem persistência: recriar a instância reinicia a contagem e não restaura a base.

## Morte, reposição e identidade de grupo (comportamento atual)

1. Mate um NPC e aguarde a limpeza do corpo e a reposição da população.
2. Confirme que a nova instância é individual e não herda automaticamente o grupo, a base ou o inventário do NPC morto.
3. Registre se o planner escolhe um novo ponto; isso é a limitação atual, não um comportamento de grupo esperado.
4. Não considerar continuidade validada até existir registro persistente de assentamento/grupo e atribuição explícita do novo NPC a esse registro.

**Não executado no Unity/Unturned; roteiro para validação posterior.**


## Grupo temporário e reposição com atraso (teste manual pendente)

1. Em sessão de teste, observe o log de IDs de grupo ou inspecione `MobileNPCGroupMember.GroupId` nos NPCs vivos próximos.
2. Mate um membro de um grupo com pelo menos outro membro vivo. Confirme que o grupo permanece e que a reposição só aparece depois do atraso configurado (60 s por padrão, podendo haver espera adicional até o próximo ciclo de spawn).
3. Confirme que o novo NPC, se surgir dentro do raio de associação de 140 m do grupo sobrevivente, recebe o mesmo `GroupId` e a âncora compartilhada.
4. Mate todos os membros do grupo antes da reposição. Confirme que o grupo é dissolvido; a próxima instância deve criar/entrar em outro grupo, não recuperar a identidade dissolvida.
5. Confirme que o tempo de remoção do corpo (30 s por padrão) é independente do atraso de reposição (60 s).
6. Mate dois NPCs em instantes diferentes e confirme que cada morte mantém seu próprio prazo de reposição, sem a segunda morte reiniciar o relógio da primeira.
7. Repita sem jogador vivo, em terreno inválido e perto de limites do mapa; a reposição deve aguardar uma condição de spawn válida.
8. Este sistema é volátil: trocar/recarregar o mapa ou reiniciar o processo não preserva grupos ou bases.

**Não executado no Unity/Unturned; estes itens são testes manuais pendentes.**


## Grupo limitado por facção e reposição atrasada — teste para dev build

1. Entre em single-player com a build de desenvolvimento e espere a população móvel surgir. Abra o log do jogo e procure mensagens [MobileNPCGroupMember] indicando criação/adesão a grupo, ID e contagem de membros.
2. Observe vários NPCs surgirem: cada grupo deve ter no máximo quatro membros vivos e NPCs de funções diferentes não devem entrar no mesmo grupo. A associação é por proximidade de até 140 m, então o posicionamento da base e do jogador influencia o resultado.
3. Quando um grupo tiver pelo menos dois membros, mate um deles. O membro morto deve sair do grupo imediatamente; os sobreviventes devem continuar usando a mesma âncora. O log do diretor deve registrar a morte e programar a reposição.
4. Aguarde pelo menos 60 segundos, mais até um ciclo de spawn (normalmente até 18 s) e mais o tempo que uma tentativa de posição segura exigir. A nova instância deve aparecer fora da linha de visão e, se nascer dentro de 140 m do grupo sobrevivente e houver vaga, deve entrar no grupo existente.
5. Em um teste separado, elimine todos os membros de um grupo antes da reposição. O log deve indicar que o grupo foi dissolvido. A próxima instância não deve reutilizar o ID dissolvido; ela cria ou entra em outro grupo elegível.
6. Confirme que o corpo pode desaparecer após aproximadamente 30 s, independentemente do prazo de reposição de 60 s. O spawn pode demorar mais em terreno inválido ou se não houver jogador vivo.
7. Faça um teste com mais de quatro NPCs próximos e confirme que o sistema cria outro grupo em vez de exceder o limite. Não exija que o grupo seja persistido após reiniciar/trocar de mapa: essa função ainda não existe.

**O código foi revisado estaticamente; os passos acima ainda precisam ser executados na dev build.**


## Reposição que tenta preservar o grupo original (atualização)

1. Espere até um grupo ter pelo menos dois NPCs vivos e anote o ID do grupo nos logs.
2. Mate um membro e confirme que a reposição fica agendada para 60 segundos depois da morte, independentemente da remoção do corpo aos 30 segundos.
3. Não mate os sobreviventes. Depois do prazo, aguarde também o próximo ciclo de spawn (até aproximadamente 18 segundos, além de tentativas de terreno seguro).
4. Confirme nos logs que o novo NPC tenta reentrar no ID original e que o total de membros não passa de quatro. Se conseguir entrar, confira que recebe a âncora compartilhada do grupo.
5. Em outro teste, mate todos os membros do grupo antes de vencer o prazo. O log deve registrar dissolução; a reposição não pode reativar o ID dissolvido.
6. Se o grupo ficar cheio, mudar de função/facção ou for dissolvido antes do respawn, o novo NPC pode permanecer no grupo atribuído pelo spawn normal. Não considerar persistência entre sessões: os registros continuam em memória.
7. Repetir com duas mortes em momentos diferentes: cada reposição deve respeitar seu próprio relógio e a tentativa malsucedida de spawn não deve consumir a reposição pendente.

**Ainda não executado no Unity/Unturned; validar em dev build antes de considerar concluído.**


## Player(2).log regression — 15 October 2026

**Status: QUASE PRONTO (static source verified, dev build not yet retested).** The log showed 52 spawn rejections and 12 deferred persistence retries, all caused by the same anatomical collider failure: `Hit_LeftLeg` was no longer on Entity (23) by the time `MobileNPCSpawnService` checked it. `MobileNPCVisualGuard.EnforceRenderableState()` was wrongly repainting every visual descendant to Medium (16), including all six new hitbox children. The guard now activates/re-layers only actual mesh renderers and the ancestors required to draw them; the six hitboxes keep `LayerMasks.ENTITY`. The spawn validator reports the actual layer separately from ray mask errors. No hitbox was added, removed, or resized: the six Player_Server box dimensions are unchanged.

**Dev-build acceptance:** the startup log should no longer contain `Spawn refused` caused by incorrect hitbox layers, and should show a successful `[MobileNPCDamageInvariant] PASS at spawn` followed by NPC activity. Only after a successful spawn should firearm, melee, zombie/animal damage, death, ragdoll and persistence be considered testable; none of those gameplay outcomes is validated by Player(2).log, because the NPCs did not spawn.

**Independent environment warning, still pending:** the game asset bundles reference `Pathfinding.NavmeshCut`, but the public U3-SDK intentionally ships `UnturnedPathfinding_Empty` without the licensed ASPFP backend. Repeated missing-script notices from `LevelObjects.load` are not resolved by the hitbox fix; do not add a dummy NavmeshCut or claim native A* navigation is functional. Keep physics-based fallback navigation as the supported mode until a compatible, licensed backend/asset setup can be verified.


## Player(3).log follow-up — ragdoll, curbs, tactical destinations (15 Oct 2026)

**Build feedback:** NPCs genuinely lose HP, die, dissolve groups, queue delayed respawn and schedule corpse removal. \`Player(3).log\` contains successful six-anatomical-hitbox spawn checks and \`[MobileNPCDeathInvariant] PASS\` entries. Health/death can be treated as **FINALIZADO for this smoke test**, but distinct damage sources, XP, loot and reputation remain separate validations.

**Death presentation — QUASE PRONTO (changed source; NOT build verified).** The log has four native articulated death activations (11 bodies/11 colliders/10 joints) and two explicit \`Left_Leg\` bind-pose failures that fell back to a rolling single-capsule corpse. Native Ragdoll_Player stores rest-pose bones; the live NPC_Client rig is animated. The authoring code no longer rejects a valid articulated rig for moving an arm/leg, validates stable parent identities, aligns joint connected anchors to the actual death pose, and caps bullet-to-ragdoll impulse. No rolling-capsule fallback is invoked anymore: if the native articulated rig really is unavailable, the corpse stays posed, emits an error, and is cleaned by the existing director. Death-loot, group and damage flow is unchanged. **Retest at least three firearm, three melee, and three zombie deaths; require ragdoll limbs to physically fall, not just a 11/10 console count.**

**Curbs/sidewalks — QUASE PRONTO.** Runtime CharacterController now uses the *checked-in Player_Server.prefab* step offset of 0.4m and slope limit of 60 degrees, replacing 0.3m / 38 degrees. Ground route connector considers verified step-height vertical rises independent of ramp slope. Higher obstacles still require real stairs/ladder/vault, and furniture-top access remains a different navigation mode. **Retest Washington asphalt→sidewalk both directions, low steps, doorway thresholds and curb corners without jumping**, then verify walls and fences are not passed through.

**AI tactical goal validation — QUASE PRONTO.** Under fire, the previous cover/retreat raycast started 12m above the desired X/Z point and could choose a roof/second floor as a supposedly safe nearby destination. The candidate ground search is now bounded to local elevations, and cover/retreat points with >0.55m height difference require physically verified short local route. This preserves the existing local A*, individual access memory and tactical decision tree. **Retest SeekCover/Retreat around multilevel structures; require the chosen cover to be actually accessible and for failed points to stop repeating.**

**Independent pending work:** A* \`Pathfinding.NavmeshCut\` is missing from the SDK/runtime, so native ASPFP routing is unavailable. Keep the bounded physical fallback without pretending native graph routes exist. Remaining AI test areas: room/door transitions, complex stair and furniture vault, noncombat scavenging, dialog HUD, zombies outside regions, faction/group reinforcement, ranged engagement balance, injury/medical behavior and longer-term A-Life/uncertainty; do not mark these complete solely because this patch compiles.

**Close-zombie self-defense — QUASE PRONTO.** The current melee implementation skipped every attack while `Retreat` was active, even if a zombie was already within its 2m melee range. Retreat still has movement priority, but a *visually selected, valid Zombie target at actual melee range* may now receive one ordinary cooldown- and line-of-sight-validated defensive hit. This does not authorize attacking friends, punching an unseen zombie, shooting while fleeing, or unlimited melee damage. Retest a wounded unarmed NPC cornered by a zombie; it should attempt to defend itself without stopping the retreat route.


## Zombie movement parity and A-Life decision review — 16 Oct 2026

**Status: QUASE PRONTO — source changes reviewed, player-build observations NOT yet obtained.**

### Native zombie vs NPC navigation

Verified the checked-in native \`Assets/Resources/Characters/Zombie_Server.prefab\`: \`CharacterController\` is height 2.0, radius 0.4, center (0,1,0), slope 75°, step offset 0.5, skin width 0.1; \`Player_Server.prefab\` has step 0.4 and slope 60. The NPC runtime root had height 1.8, radius 0.35, step 0.4 and slope 60. This discrepancy was real and could impair small curbs.

The NPC's spawned root now matches **Zombie_Server** on those six physical values and retains \`LayerMasks.AGENT\` and \`enableOverlapRecovery=false\`. The native \`NonPathfindingZombieMovementComponent.Move\` uses \`Physics.gravity.y * 2\` when moving, unlike the NPC's near-zero grounded vertical correction. The NPC now uses this zombie-equivalent downward contact **only while ordinarily grounded**; ladders, swimming, jumping and verified vault arcs retain their own motion, CharacterController collisions remain authoritative.

The next-stride ground probe no longer blindly raycasts 5 metres above the NPC (risk of selecting another floor/roof), and recognizes only a nearby genuine world-support surface; it excludes player/zombie/NPC bodies and furniture. The physical-ground confirmation also recognizes \`GROUND2\` when the map uses that secondary terrain layer. Local detour, floor-connector and grid-edge rise thresholds are based on the real 0.5m step (plus 0.05m tolerance), not on skin width incorrectly granting extra ledge-climbing capacity. Tall walls, railings, rooftops and upper floors still require a physical route, ladder or jump.

**Required dev-build tests:** Washington asphalt→sidewalk→asphalt without any jump; curb corners; shallow steps; steep ramp; doorway thresholds; vault over a genuinely high obstacle; no teleport through walls or furniture; compare with a nearby native zombie on the same curb. Examine \`[MobileNPCHumanoidMotor] Blocked\` logs after a stuck episode. Confirm that changes to the larger capsule did not regress narrow doors or ordinary spawn clearance.

### A-Life online planning and decision brain

The original online planner's utility lacked resource **ownership**: an NPC with water could leave guard because its thirst bar was low, and a bleeding NPC with no wound dressings could enter a repeated \`Recover\` loop. Job scoring now considers native inventory holdings, \`MobileNPCMedicalBehavior.NeedsMedicalSupplies\`, active bleeding, food/water shortage, health and combat interruption. It does not synthesize items, automatically heal or read hidden spawns.

\`GuardSettlement\` now uses bounded, individually timed guard shifts while idle, releasing the *existing* \`MobileNPCGroupMember\` post reservation and giving other members an opportunity to take over. It does not abandon a confirmed combat encounter mid-defense. Visible loot selection is now gated by \`MobileNPCAutonomyPlanner.CurrentJob\`: scavengers actively collect; patrols require worthwhile items; guards/recovering NPCs mostly preserve their duties except urgent resources. Remembered supply trips can preempt non-scavenger duties only for real medical, food or water emergencies. Every actual item still requires local sighting or personally remembered/communicated clues and a genuine \`ItemManager\` transfer.

When an investigating brain reaches a genuine sound clue, its next three uncertainty-search spiral candidates are now smaller, same-floor, ground-checked and validated by bounded local route planning before becoming a movement destination. Inaccessible guesses are **skipped, not treated as visited**; the ordinary search deadline/return and player knowledge advisory remain the existing systems.

**Required dev-build tests:** wounded/bleeding no-bandage NPC chooses Scavenge instead of Recover, but still only picks up real loot; full inventory does not generate impossible item jobs; equipped guard does not abandon post for junk; after a guard shift another NPC can reserve the same post; confirmed zombie/player combat preempts routine; sound investigation checks actual adjoining accessible rooms instead of repeatedly colliding with walls, and never uses the unseen player's current Transform.

**Remaining AI tasks:** diagnosis of native ASPFP/\`Pathfinding.NavmeshCut\` absence and multi-room/long-range navigation without the native graph, full combat behavior validation, radio rumors with actual devices, persistent job state across save/load, injury and supply side effects, NPC relationships/trading/HUD, long-range zombie pursuit. Do not classify these as fixed solely from these changes.

**Follow-up during this source pass:** both personally remembered supply sites and uncertain player-knowledge hints used tall ground probes that could silently choose an unrelated higher floor. Their lookup now stays near the original first-hand item/clue elevation and rejects actor/furniture support or cross-floor jumps. This remains speculative *navigation knowledge*, not a guarantee that loot or the unseen enemy is currently there. These fixes also remain QUASE PRONTOS until the Unity build confirms them.


## A-Life v7: persistent physiology, jobs and resumed routines — 16 Oct 2026

**Status: QUASE PRONTO — source-reviewed changes only. Not Unity compiled or playtested.**

### Concrete flaws found

- Previous MobileNPCPersistence snapshot **version 6** saved player-equivalent NPC health, inventory, group identity, individual experience/supply locations and learned routes, but not current routine, personal personality biases, hunger, thirst, bleeding, limb condition or broken legs. An old severely thirsty/bleeding NPC was reborn full of water and without any wound on game reload.
- Medical wound-care logic existed as MobileNPCMedicalBehavior, but the actual \`MobileNPCPopulationBootstrap\` runtime template did **not attach it**. Existing vitals did already require the InjurySystem; the planner's missing-supply utility therefore could never evaluate actual missing bandages/splints, and rag/bandage/splint use wasn't running through the dedicated wound-care loop.
- An interrupting tactical/sensory destination erased a low-priority patrol/scavenge waypoint. Once combat/noise finished, this survivor would choose a fresh random point, even if the previous verified location was still useful.

### Implemented

- SaveFile \`CurrentVersion=7\` adds optional \`MobileNPCSurvivalNeeds.NeedsSnapshot\`, \`MobileNPCInjurySystem.InjurySnapshot\` and \`MobileNPCAutonomyPlanner.RoutineSnapshot\` to each *living* NPC record. v1-v6 records remain loadable; missing new fields retain the existing default survival/AI initialization. No extra hitbox was created; injury records map exactly to the pre-existing six body parts.
- Stores actual food/water percentages, limb condition, leg fractures and bleeding severity/location, without applying duplicate damage/treatment at restore. Bleeding restarts on the ordinary medical tick timer; consuming existing items still goes through the existing inventory and medical services.
- Stores the *individual's* current strategic role (Patrol, Scavenge, GuardSettlement, Recover), personal tendencies, bounded remaining commitments, shift/cooldown timers and a recent unsuccessful location. Uses remaining durations rather than serializing invalid \`Time.time\` from another game session. Guard post/sector ownership and tactical routes are **not** carried over as facts: group and physical navigation services must revalidate. An NPC re-generated after death receives NONE of this individual's saved state.
- Attaches the already implemented wound-care component (bandage, dressing, splint logic) to the real NPC runtime template; planner late-resolves this component as needed.
- When native brain/tactical travel interrupts ordinary Patrol/Scavenge movement, the motor temporarily retains the *intent* (the grounded site, job identity, 38-second expiry), releasing any previous group sector lease. If AI returns to leisure without changing job or exceeding the home radius, it checks real near-ground geometry and an available group sector **once** before walking there again. There is no teleportation, wall phase, stale A* reuse, magical item respawn or unseen-target location recovery.

### Player dev-build checks before calling finalized

1. Spawn a neutral NPC; observe its Patrol/Scavenge choice in development log. Approach/fight, stop combat, verify that it can resume an unfinished legitimate exploration waypoint instead of choosing new random coordinates. Test route blocked during the interruption: it must reject and replan normally.
2. Use authentic shots to inflict torso/leg wounds, fracture and bleeding; check injury and lack of bandages triggers an urgent supply-seeking decision; medical supplies must physically exist and be consumed. The six native Player-equivalent damage regions must stay unchanged.
3. Save/reload the singleplayer map with a living hungry/thirsty/wounded NPC and an active routine; verify that the same individual retains health, deficits, injuries and job commitment. Healing or eating requires real inventory and should happen during ordinary ticks.
4. Load an **older v6 saved file**: it should load without rejecting the snapshot, initializing newly added fields with defaults. Verify unchanged inventories, groups, learned locations and routes. Do not overwrite the only copy of a valuable save during testing.
5. A dead NPC must not regain its individual memory on replacement. All living guards must re-reserve physical group posts on load; abandoned claims must not persist from the previous Unity session.
6. While observing wounded NPCs near curbs, verify zombie-style \`CharacterController\` ground stepping remains operational and that injury movement multipliers still apply.

**Known remaining work:** Actual Unity C# compile/Play mode verification, native ASPFP backend absence, richer cooperative long-range group logistics/roles, persistent whole-world base actor structures (native game system), full FEAR-like combat tuning, player dialogue HUD, long-range zombie pursuit and ragdoll runtime confirmation.


## A-Life director v2, local group contact and native clothing — 16 Oct 2026

**Status: QUASE PRONTO.** Source reviewed and committed, not compiled or tested in Unity. No player-facing faction/intent HUD has been added.

- Local combat groups previously shared approximate player sightings and some squad roles; now a member that individually and legitimately sees a **zombie or hostile mobile NPC** may call a short-range alert. Recipient gets a noisy 12-second investigative clue, **not a target Transform or immediate permission to fire**. Speech respects 7m whisper / 24m shout and wall attenuation. Combat/cover tasks take priority, and the group must still independently validate contact. Development logs record delivered alert counts and coordinated covering/advancing assignments.
- The existing `MobileNPCEncounterDirector` was a combat pacing and optional-search governor, **not an actual starting-life exploration director**. New startup feature consults only nearby authored `LevelItems.spawns` (possible item-spawn locations, not live items), samples with 144-point CPU cap, and suggests a **noisy direction**. The individual motor tries genuine grounded destinations 22-58m away and explores via its actual existing pathing/perception/looting. No item ID, actual item state, enemy, player coordinates or specific exact loot coordinate is given. Maximum two successful/attempted hints per newly spawned NPC, and guidance is suppressed once personally learned supply locations/routes are available. The per-individual count of previously used novice hints is also included in the backward-compatible v7 NPC save record, so reload does not grant another two free hints to the same surviving individual.
- NPC clothing before this change was merely native `HumanClothes` face/hair variation plus clothing stored in `MobileNPCInventory`; there was **no wearable gear mechanic or damage mitigation**. `MobileNPCEquipment` now selects real owned items and applies them to the native humanoid visual (hat, shirt, pants, vest, backpack, mask, glasses) at most every 1.5s when changed.
- Uses the existing `DamageTool.getPlayerArmor` anatomy mapping: helmet/hat head; shirt arms; pants legs; shirt multiplied by vest for torso. Actual `ItemClothingAsset.armor` with owned item quality, decrementing the inventory clothing quality on a validated part hit only if Unturned's native `Items.ShouldClothingTakeDamage` option enables clothing wear. The single existing `MobileNPCHumanoidVitals.ApplyDamageToPart` entry point remains authoritative, does not add hitboxes, preserves death/loot flow, and never synthesizes equipped gear.
- Native loot scorer prefers visible clothing with genuinely better protection than the NPC's existing items, not duplicate worse ones. A backpack can appear on the model but **does not yet increase the simulated inventory's capacity**. Injury/protection tests are scoped to verified body-part hits; non-anatomical environmental damage still goes through the generic vitals method.

### Dev build experiments

1. Spawn multiple same-role/faction NPCs nearby and confirm log statements `created group/joined group`. Trigger a genuine player contact or let a zombie enter a teammate's field of vision: expect approximate local shout, nearby listener investigating independently, sometimes Covering/Advancing assignment. Put a wall between allies / move outside 24m: local alerts must not work beyond auditory range. Radio communication is a separate feature.
2. Spawn a fresh NPC near native authored item spawn areas: only a limited number of initial vague exploration bearings may be suggested, and only after actual movement is grounded. Inspect a region without any currently spawned items: NPC must NOT fabricate loot or know a nonexistent drop.
3. Have an NPC pick up a stronger real vest/helmet and verify the visible outfit changes. Hit torso/head vs naked NPC with equivalent authentic weapon, compare health debits. Headwear never shields legs. Test changing item quality and compare protection to native Player armor formulas; check clothing quality persists in save and can be looted after death.
4. Save/reload a clothed survivor: ownership still comes from stored real inventory, with equipment recalculated. Remove/give away garments: clothing must disappear from live appearance/protection. No clothing or item should appear without a real acquisition.
5. Watch CPU and logging with several NPCs. Discovery is bounded per request, visuals polled every 1.5 seconds, and group shout cooldown is 5 seconds. In release builds, developer diagnostics are compiled out.

**Still outstanding:** actual Unity compile and build validation, persistent group-roles beyond quick tactical assignments, physically audible voice/sound playback of local NPC callouts, proper backpack-driven inventory capacity, equipment on corpses/ragdolls, and deeper coordinated cover/reload/flank evaluation. The director currently offers two short start-of-life orientation hints rather than a full offline A-Life world simulator.


## Coordinated combat v2 — 16 Oct 2026

**Status: QUASE PRONTO** — static source checks passed. Actual Unity compile and PlayMode still await dev build. No privileged faction/tactic HUD was added.

### Coordinated actions

- A covering role is assigned only when a shooter is healthy, actually loaded, personally sees the target and has a clear firing corridor. A teammate's cautious 3–7 m advance still requires the existing verified local flank solver; if gunner loses confirmed sight/ammo or the flank is blocked, the proposed pair cancels. A COVERING label never grants suppression fire or aimbot aiming.
- MobileNPCHumanoidRangedCombat checks each REAL spread-adjusted shot direction against current living squadmates before ammunition is debited. Friendly-fire corridors force a short delay without a shot; personal perception, legitimate shooting, ammo and damage remain unchanged.
- Healthy nearby groupmates can pass genuine rag, bandage, dressing or splint items to a physically adjacent, noncombat injured group member if that patient actually lacks the item. The handoff respects ≤2.4 m separation, ≤1m height, line-of-sight, server inventory slot reservation and rollback. Original MobileNPCMedicalBehavior must consume the transferred supply and administer care. Never fabricates health or items.
- On death, a group member's nearby loss informs only same-group survivors within physical hearing range, with wall attenuation. Individual caution remains for 12–20 s; no hidden shooter identity or global group-wide notification. A wounded or low-ammo survivor who individually sees its original target may voluntarily withdraw via the existing Retreat tactical goal, after newly validating nearby real cover or reachable ground. The caller throttles replanning.

### User build validation

1. Spawn two to four compatible hostiles together. Observe natural mutual alerts and log-confirmed Covering/Advancing only with real hearing and a gunner's own line of fire. Block the flanker corridor and verify the attempted maneuver cancels without a teleport.
2. Cross a teammate through a gunner's actual shot path. The gunner should pause instead of shooting its own team or consuming ammo. Clear the lane and confirm original humanized firing resumes; walls still block bullets.
3. Hurt a friendly NPC while another healthy friendly nearby owns an actual native rag/dressing/splint. Confirm exactly one item transfers across a physically clear ≤2.4m space and ordinary injury care consumes it later. Behind a wall or farther away, no magical medical handoff.
4. Kill one squad member within earshot. With another low-HP/low-ammo survivor who can personally see its opponent, expect a cautiously chosen verified fallback if terrain permits. Check no global alarm for distant allies and no knowledge transfer of the killer.
5. Watch Unity profiler with 4+ NPCs; casualty/medical checks are limited and shooting safety tests loop over the existing ≤4-member group. No new per-frame spatial world scan was added.

Still pending: build compilation, sound/animation of spoken warnings, actual tactical suppression, more sophisticated squad leader orders, radio-mediated long-distance support, ragdoll and complete nav testing. Only user-approved in-build behavior becomes FINALIZADO.


## Melee parity and leader-directed squad tactics — 16 Oct 2026

**Status: QUASE PRONTO** — source reviewed; Unity C# compilation and play-build behavior NOT YET TESTED. Preserve prior final/quase pronto classifications on unrelated navigation, hitboxes, ragdoll and dialogue.

### Melee: both directions against humans and zombies

- Baseline MobileNPCHumanoidCombat already physically checked a close forward ray and forwarded a short-range fixed-damage strike to DamageTool.damagePlayer (human with native armor), Zombie.askDamage (native zombie life/death/loot), or the mobile NPC's ApplyValidatedWeaponImpactServer (authoritative six-part injury/death). It did not use actual owned ItemMeleeAsset weapon statistics, even if the NPC could pick up melee loot.
- MobileNPCInventory now chooses an actual owned melee weapon using vanilla ItemWeaponAsset playerDamageMultiplier/zombieOrPlayerDamageMultiplier and instance quality. The weapon's authentic range is used (bounded for non-penetrating collision safety), rather than magically applying fixed punch damage to every carried weapon. For unarmed NPCs, use the same 1.75m reach and 12 torso damage as the native PlayerEquipment fist baseline (15 times 0.8 spine); attacks to a Player are marked PUNCH or MELEE correctly, and respect actual player armor. Item melee strikes reuse the native body hit, armor, wound, death and zombie damage APIs; no new hitboxes or extra kill tracking invented.
- Native weapon condition below 50% reduces strike damage by the same quality formula as UseableMelee; actual owned item quality wears on valid strikes only if mode configuration enables durability, preserving its real identity for saves/trade/corpse loot. Invalid or no-damage weapons cannot create damaging punches at longer weapon reach.
- A ready loaded gun generally takes priority over a simultaneous melee strike, except when a hostile genuinely crowds into the NPC's immediate personal space. Retreating wounded NPCs only strike physically close visible zombies, not blindly pursue an unverified enemy.
- Visual system now probes native bound Punch/Weak/Strong/kick clips with truthful fallback and plays the vanilla punch or real weapon Use audio. MobileNPCRangedPresentation reuses its EXISTING Unturned hand hook + ItemTool attachment to display truly owned melee weapon meshes when selected; temporarily clears the gun during fist swings. Clip/hand model existence depends on the actual NPC_Client prefab and must be verified on the build. Animation/audio failure cannot prevent already-validated damage, thanks to isolated AttackStarted listeners.
- Inbound PLAYER -> mobile NPC unarmed punches were already patched in PlayerEquipment.punch using validated ERaycastInfoType.MOBILE_NPC, physically authoritative PlayerInput.getInput and the same mobile NPC vitals. Inbound melee weapon strikes use UseableMelee with MobileNPCDamageBridge physical first-hit resolution. Those pre-existing source pathways were reviewed, **not rewritten** in this phase.

### Small-group leader

- MobileNPCGroupMember now has a deliberately limited local commander. An alive, elected leader samples at low frequency (~3.5–5s), evaluating its **own visibly confirmed opponent** plus teammate readiness only within physically audible 14m voice (shorter whisper, wall attenuation). A contact genuinely shouted and heard from an ally may elicit a short HOLD, but NEVER creates the leader's own target Transform or grants a flank from hearsay.
- Four short-lived instructions: HOLD preserves locally positioned firing and inhibits opportunistic tactical flanking; PROBE allows the existing individual physically grounded flank algorithm; WITHDRAW requests the already validated individual near-cover retreat (every NPC must itself see the opponent); REGROUP occurs only after previously observed combat, once vision/contact is lost, and each idle nearby NPC must find real same-floor ground and path to a rally point near the actual leader. No teleports or silently transferred knowledge. Sound, footsteps, shots and renewed visible contact cancel regroup goals.
- Leadership survives the original group's election lifecycle: if the leader dies, surviving members elect a successor; no dead actor issues persistent orders. Group directives expire quickly and require renewed local evidence.

### Must-test in Unity dev build (before FINALIZADO)

1. Player punches neutral/hostile NPC: must debit health, trigger real six-part injury/death and not merely display a hitmarker. Repeat with real machete/knife and inspect corpse loot/XP. Verify old fixes not regressed.
2. NPC with no weapon punches visible hostile player and zombie from approx. 1.75m with realistic native 12 torso HP damage. Walls must block. No reach from outside physical melee envelope.
3. Give an NPC a genuine world-looted melee weapon while it has no usable gun; weapon should appear in hand when available and alter attack damage/range based on the actual Unturned asset. Check its quality wears, save/reload maintains it, and death drops the exact item rather than fabricating one.
4. With a usable loaded gun, NPC must prefer shooting rather than double-damaging at 2m; during close crowding/reload, allow physical short melee. Test unarmed and armed strike animations and sound with the actual NPC_Client rig; no clip means valid damage still works but visual must be reported.
5. Two-to-four compatible faction NPCs: when leader personally sees the player, compare healthy loaded squad (PROBE) with many wounded/empty squadmates (HOLD/WITHDRAW); when contact is lost, nearby idle survivors can REGROUP if same-floor path exists. Separate members by >14m or a solid wall: no squad command telepathy. Logged orders are for dev builds only, never visible in player HUD.
6. Check no false squad rally within first 26s on a newly created group that has never been in combat. Trigger real footsteps during a rally and verify existing sensory investigation resumes.

**Known limitations:** NPC melee hand attachment and attack clip must be visually validated (assets vary); selected melee behavior currently uses torso multipliers for players/zombies while exact NPC hit-collider maps to its own actual body part; no advanced timed wind-up/parry/disarm, locomotion-integrated swing trace or human voice tactical barks yet. Zombie askDamage remains the original game's server path rather than a new damage bridge. This batch is not Unity compiled.


## Zombie acoustic regression: restore original gunshots and explosions — 16 Oct 2026

**Status: QUASE PRONTO.** Source-comparison and 18 static regression checks passed. Unity compilation and behavioral approval are still missing. The original U3 SDK `main` code is the behavioral reference; recent branch modifications must not replace native gunshot/grenade semantics.

### Root causes confirmed in source

- Original `UseableGun.fire` calls `AlertTool.alert(transform.position, equippedGunAsset.alertRadius)` only when a barrel is not silenced or its suppressor is depleted. Our branch had **added a second global `ZombieManager.alertZombiesToNoise` pass** with `clamp(gun range * 0.75, 45, 120)`, ignoring authored gun noise and suppressors. Removed. Mobile NPC hearing remains a separate `MobileNPCRegistry.NotifyGunfire` callback, now called with exactly the original gun alert radius within the same native unsuppressed-shot conditional.
- Original `DamageTool.explode(ExplosionParameters)` already calls `AlertTool.alert(parameters.point, parameters.alertRadius)` once for the actual explosion. The branch erroneously called `ZombieManager.alertZombiesToNoise` inside EACH `Zombie.ApplyExplosionDamage` callback with a synthetic radius up to 160m. Removed; native damage guard restored to original ordering.
- The extra `ZombieManager.alertZombiesToNoise` implementation scanned the global `AllZombies` list and forced `zombie.alert(position, false)` regardless of native sound, navigation and source-specific rules; it also suppressed distinct close stimuli via shared 120ms cooldown. Converted the method to a guarded compatibility delegate to vanilla `AlertTool.alert` instead. There should be no active extra zombie sound relay from Player, NPC gunfire or explosion callbacks.
- Original `Zombie.alert(point)` only accepts a replacement point when it is *closer* than the current point. Our previous branch replaced the current point with every fresh shot (even on the opposite side of the POI), and kept updating its search center. Restored closer-point preference; real native startling alerts alone start the optional limited search. A sound sweep is allowed only when the zombie ACTUALLY reached the area (~11m), samples a few close navigable locations 2.5–5m around the real source, and then stops instead of sprinting toward a randomly chosen unrelated zombie spawn in its region.
- The custom off-navigation CharacterController stepping is now reserved for an already legitimately acquired **living** Player/mobile NPC pursuit (EHuntType.PLAYER). A gunshot/explosion POINT hunt always uses the original zombie `seeker.Move` navigation rather than a non-native physical side detour. The native minor post-alert ambient wander radius (0.5–1.0m) is restored rather than our unrelated 4–8m deviation.
- The dormant `ZombieManager.UpdateAmbientHordes` random-destination generator was NOT called by `Update`; it is not claimed as the observed cause. Do not enable without a separate opt-in behavior spec.

### Required Washington dev-build tests

1. One UNSUPPRESSED vanilla firearm from an open POI: nearby unoccupied zombies should startle and head toward the SHOOTER position (native AlertTool), rather than the projectile impact, a random opposite navigation region or a fake 45–120m sound radius. Confirm normal gun/weapon-defined alert distance.
2. Repeat using a working SILENCER: do not trigger the extra unsilenced global alert. A genuinely visible player can still be acquired by ordinary sight; that is NOT proof a suppressed noise event leaked.
3. Throw one real grenade (or detonated explosive) inside a POI. The explosion should alert via the single native DamageTool.explode AlertTool call, regardless of whether there were zero, one, or many zombie damage callbacks. Observe one sensible investigation toward the explosion and original blast damage/death.
4. Spawn an NPC with a real gun and let it fire. Its committed ammo-accounted shots must call native AlertTool with ItemGunAsset.alertRadius, not its bullet flight range. Zombie defense / NPC-vs-zombie combat must not require a new improvised sound system.
5. While a zombie is investigating a point, fire from much farther away on the opposite side. If the new point is less relevant than the current one, it should continue its native nearer-point investigation. Once the original source is reached, a short 2.5–5m local sweep is acceptable; it must NEVER run away to a random spawn or opposite POI.
6. Repeat just outside a navigation region and across the region/POI boundary. The original AlertTool intentionally requires a navigable sound source; **out-of-nav sound acquisition is still a separate unimplemented enhancement**, not something to fake by global teleport-like alerts. An already visually acquired live target may continue off-nav pursuit through the separate physically checked fallback. Verify no zombie vanishes at the boundary.
7. Compare FPS/profiler with the previous dev build. Removing per-shot AllZombies sweeps and repeated per-victim explosion alerts should reduce extra CPU work, but performance improvement is not yet measured.

**Acceptance:** only mark this audio-routing regression FINALIZADO after observed correct shot/explosion direction, silencer behavior, zombie source investigation and pursuit stability in build; other zombie/navigation systems remain independently QUASE PRONTOS or PENDENTES.

## A-Life independent exploration and genuine outcome memory — 16 Oct 2026

**Status: SOURCE-ONLY.** No Unity compilation/runtime claim. Features shipped in source: `9180ccf6` / `b6a6c12d` / `7a50f84b`.

1. **First life / uncertain guidance:** spawn an untrained solo NPC, no known supply sites or route records. Observe ordinary patrol/scavenge; only two successfully adopted Director *bearings* may be spent in its life. If suggestions have no safe ground/path, they must not exhaust both hints. No nearby player position, loot ID or loot item materializes from a hint. Try a beach/remote island without known item spawnpoints.
2. **Visited districts:** use a walkable Washington neighborhood with several large buildings. NPC reaching a genuine patrol/scavenge destination marks a private short cooldown. In the next 95s, it should prefer a different sector farther than ~18m over returning to the same entrance every few seconds. When multiple structures are candidates, try alternatives before unstructured terrain.
3. **Blocked district:** present an unreachable wall/closed door and allow a real physical route failure. NPC should reject nearby repeats for ~55s and choose another reachable sector, not teleport/phase into the building. The blocked place must NOT be reported as reached/successful.
4. **Immediate priorities:** trigger actual visual enemy, audible shot, nearby footsteps, loot in reach, medical need or group order while scouting. These must supersede routine destination; the interrupted task must NOT count as a visited sector. A suspended, still-safe and unblocked routine point can be resumed once after the event, with native route revalidation.
5. **Individual vs group knowledge:** spawn 2–4 NPCs of the same group. Separate sector leases prevent crowding; a solitary survivor still avoids their own recently visited points. A stranger or fresh replacement must not inherit deceased scout memory. Combat targets/loot coordinates must never be shared by the Director.
6. **Save/reload + old saves:** after a verified arrival, save the SAME living NPC and reload before the TTL expires. It must keep its recent-sector avoidance and used-hint count; positions/remaining seconds must be validated against current map. Old v1–v7 records without the new optional arrays still load. Kill this scout and let a fresh NPC spawn: personal sector outcomes must NOT be inherited.
7. **Different floors:** after visiting one floor, a physically valid nearby goal over 2.5m higher/lower should remain eligible; still requires genuine stairs/ladder/path, no false transition. Check pathfinding warnings separately.
8. **Performance:** try 1, 20 and 40 NPCs on Washington plus another map. Observe profiler AI/sensors/physics, logs, frame times and save JSON growth. Each NPC retains max 4 recent sites; Director scans at most once per 18–28s on unsuccessful guidance, only while genuinely idle.

**Acceptance:** only after Unity compile, in-game independent exploration behavior, correct save/reload with no shared inherited knowledge, and performance comparison. The movement/global A* and P0 death/hitbox regressions remain separate requirements.

## Sensory evidence priority, uncertainty and search leases — 16 Oct 2026

**SOURCE-ONLY; NOT UNITY COMPILED.** Code commits `ea071c42`, `6fe00eba`, `4643b0fb`; static dependency/source checks 16/16, rough C# brace scan balanced. This is NOT behavioral validation.

1. **Direct sight vs indirect sound:** let a hostile NPC see the player then run behind cover and create a weak footstep or ally report at a different point. The recent personally seen location must remain stronger for 8s, but the NPC must not keep seeing/tracking the player's live position through the obstacle. Recover direct target only after real LOS; friendly/neutrals must not attack just because of a sound or report.
2. **Continuous footsteps:** player circles near an investigating NPC while occluded. One initial footstep can start a bounded ~10s investigation, but each subsequent similar step must not reset the same search timer forever. Once the search expires it resumes normal A-Life, unless a genuine new stimulus arrives. Visible peaceful player must not keep the NPC crouched.
3. **Firsthand sound vs verbal secondhand report:** have one NPC directly hear and another receive only a physically audible group report. The receiver gets an approximate, short-lived bearing with more uncertainty, never a Player target Transform or authorization to fire. Group member behind a thick wall and outside speaking range receives no data; radio remains governed by existing channel/device rules.
4. **No task hijack:** while collecting a real item, following a companion, retreating under fire, stalking a verified enemy or investigating a recent stronger sound, repeat faint footsteps and group reports. The weaker clue must not replace the active goal. Genuine gunfire/damage/visual threat still interrupts through its existing higher-priority pathways.
5. **Physically invalid search:** last player sighting next to a multi-floor building, wall or inaccessible upper level. The Director may offer search hypotheses, but only reachable same-floor ground points are adopted; failing 2 attempts should leave the original base investigation instead of consuming four accepted-hint credits or walking through walls. `CanReachNearbyCoverServer` performs an expensive local A* probe only for bounded clue attempts; profile load with 20–40 NPCs.
6. **Squad lease fairness:** with 3–4 hostile squadmates and one sealed corridor, let one fail optional sheltered search. Its group lease must be released promptly so another eligible teammate with valid own evidence can attempt its own safe route; no shared perfect target positions or instant squad teleport. Retreat, cover, genuine sound and vision must not be suppressed by the Director cooldown.
7. **Regression:** repeat save/reload for the same survivor, death/new individual, zombie perception/firearm alerts, dialogue and looting priorities, 20–40 NPCs on Washington and a second map. Inspect Player.log and compare Profiler timings with last Dev Build; do not mark completed on C# source alone.

## Real scopes, time-of-flight and learned projectile drop — 16 Oct 2026

**SOURCE ONLY: NOT UNITY COMPILED, NOT VERIFIED IN BUILD.** Use Washington and another map. Enable development logs; measure in Profiler. User runs one consolidated build after AI development.

1. **Gun + scope ownership**: equip NPC with weapon *without* mounted sight. He must not see the player outside regular 20m visual radius (NPC/zombie 32m), although actual gunshots can be audible. Equip REAL `ItemSightAsset` with zoom >=2.5 from gun state (not an NPC flag), valid magazine and rounds. Stop moving; after aiming preparation, narrow-forward real LOS may acquire player farther than 20m, but never beyond the authored gun range or absolute 115m cap. Behind wall/facing away/no bullet ammo/reload: no remote free vision.
2. **Human stationary shooting**: NPC who personally sees an opponent and has a loaded firearm should stop rushing into player collision when within suitable firing range. Test 12/20/35m without scope, 40/70/95/110m with scope. After occlusion, exhaustion of ammo, movement action, attack, surrender/withdrawal, active cover, or lost LOS, pre-existing brain, motor and tactical route regain precedence. A sniper can turn slowly toward a genuinely seen target, not track behind a wall.
3. **Native physical projectile**: enable `Gameplay.Ballistics`. Compare real player fired native gun with NPC using same rifle on stationary dummy/zombie. A hit should occur AFTER time of flight (native 0.02s segments), with gravity downward, actual muzzle velocity, `ballisticSteps` and authored max range; wall/vehicle intercept stops the bullet. No immediate target damage or real-time homing. If Ballistics=false, normal optional instant ray semantics continue, but still with imperfect human aim.
4. **Practice learning without omniscience**: use same alive NPC, same owned weapon, moving target at ~35/75/100m. Record at least tens of *actually committed/resolved* rounds and their target hits, wall impacts, misses with LOS and ammo. Test nine possible profiles (lead/drop variations) without exact guaranteed hit or instant correct trajectory. Change gun/range band to confirm experience does not blindly transfer. Disable target sight mid-flight: cannot grade invisible miss as confirmed feedback. Shoot a wall in front of visible target: do not grade lead/drop incorrect. Verify all misses possible even after high practice.
5. **Real inventory and reload**: give an empty high-range rifle plus a loaded shorter gun; choose a functional shot option and consume native real magazine/ammo once. No fake bullets; reload remains timed. Scope mounted on a new rifle is recognized after choosing it. Working native silencer must suppress both zombie `AlertTool` and mobile-NPC `NotifyGunfire` gunshot alerts; worn-out/unmounted barrel restores noise.
6. **Persist individual ballistics**: save after practice and reload SAME surviving NPC. Restore bounded weapon ID/range/profile successes and trials. Older v1–v7 JSON lacking optional ballistic field must still restore. Kill NPC, wait for replenishment: new survivor must not inherit the dead marksman’s learning.
7. **Other entities / injury / death**: player, opposing MobileNPC with six Player_Server hitboxes, zombie (including sprinter), solid window, fences, thin barriers, changing elevation, head/limb hit and armor. Confirm actual HP damage only after bullet physical contact and correct XP attribution where supported. Re-run corpse and persistence tests; developer must not mark P0 fixed from compilation alone.
8. **Stress/perf and edge cases**: 1/20/40 mobile NPCs and many zombies exchanging fire while moving through two POIs. Per-actor max six bullets in flight, max five bullet raycasts per NPC per visual frame and 100 global. Collect `Physics.RaycastNonAlloc`, frame time, stutters, 50Hz backlog after FPS drops, loot/hitmarkers/FX and kill attribution. Disabling/despawning an NPC currently discards its unresolved component-owned projectiles.
9. **Open items intentionally NOT completed**: multiple shotgun pellets per cartridge, launcher prefab rockets/grenades, actual physical gun/bullet tracer sound/visual fidelity and detached bullet persistence after shooter death, truly independent hit on an unintended crossing actor, per-limb hit multipliers and multiplayer replication. Retest after dedicated implementation rather than claiming native parity now.

**Acceptance:** realistic long-range behavior only when tested with real optics and authored gun physics in Unity Dev Build. Static C# source inspection alone does not prove that bullets are visually present, get damage right, compile, or hit moving targets appropriately.

## NPC aggressiveness game mode, cautious fire and safe erase — 16 Oct 2026

**SOURCE-ONLY. Not Unity compiled or confirmed in play.** Scope: menu `Agressividade: Normal/Combate`, safe erase, ammo conservation, optional verified reposition. Group invasion remains a DIFFERENT unimplemented feature.

1. **UI confirmation and preservation:** open Mobile NPC settings in main menu. Confirm the two new buttons exist and fit in display at 720p/1080p. Change to Combat, back, reopen; label and PlayerPrefs must retain selection. Difficulty Easy/Normal/Hard remains independent. Selecting Normal restores original population mix. In Combat the world-mix menu label must disclose the 66/21/13 ratio; returning to Normal restores saved preference.
2. **History erasure:** with no map loaded and known test-only NPC snapshots under `persistentDataPath/MobileNPCs`, click `Apagar historico` once: no files removed. Close/reopen: previous confirmation cancelled. Click again within 8s to confirm: only `*.json`, `*.json.bak`, `*.json.tmp` in that NPC folder are removed across maps/characters. Native Unturned player/world saves remain intact. Check UI deletion count, Player.log and inaccessible folder failure. In-world/server attempt must fail. Note this is an irreversible reset of saved NPC identity, items and memories.
3. **Normal survival regression:** observe hunger, thirst, eating, starvation damage in normal gameplay, saving/restoring same survivor. Switch to Combat: no drain/damage and no food/water consumption or looting; injuries, medicine, bullets, zombies and mortality are unchanged. Switch back to Normal: prior hunger/thirst values remain, do not grant artificial supplies.
4. **Combat strategic priorities:** on Washington and another map with 1/10/20/40 NPCs verify newly spawned hostile/friendly/neutral rough ratio 66/21/13. Armed group patrols world regions, takes existing verified physical guard posts, changes shifts and actually walks there; armament/compatible magazine drops are preferred over food and water. Do not claim an enemy POI or conquer territory: MODE-RAID-01 remains pending.
5. **Decision to fire:** give same survivor real rifle, limited rounds and moving target at 30/75/100m. With repeated _visible_ misses, Normal should gradually pause more and sometimes attempt short lateral LOS-verified path through motor route tests; Combat should retain a higher chance of firing even with poor past results. Both must consume real ammo, keep imperfect spread/time-to-aim/travel/drop, and never shoot through walls/friendlies.
6. **Risk tolerance:** combat NPCs begin flanking sooner when actual line of fire is blocked, stay engaged somewhat longer when healthy, but still retreat when critically injured or without real ammunition. Group ally-death withdrawal and leader commands are not overridden by cheat aim or teleport.
7. **Performance and regression:** 20–40 NPC combat with real bullets/projectile physics, zombie sounds and navigation, verify FPS/GC, excess A* route probes and ray budget. Re-run six hitboxes, headshot/limb/armor, XP/reputation, NPC mortality/ragdoll, persistent identity and dialog HUD. Use Player.log. No prior source-only change should be called finished before a real Dev Build.

**Acceptance:** UI works and is reversible except for explicitly confirmed history erase; Combat alters survival, loot, aggression, patrol/guard and decisions visibly; Normal unaffected; no cheats. Territory invasion, battlefront conquest and persistent ownership intentionally await follow-up.

## NPC native settlement build and Combat-only hostile raid staging — 16 Oct 2026

**SOURCE ONLY, NOT UNITY-COMPILED OR RUNTIME VERIFIED.** Tests below must be executed in a consolidated Washington Dev Build plus another map, with `Player.log`, actual native barricades/structures and inventory snapshots before and after actions.

1. **Construct with genuine items:** Give elected NPC leader one compatible native STORAGE buildable, two native FORTIFICATION/BARRICADE items, optional generator (with native fuel state). Wait until `SettlementPlanner.HasChosenSite`, watch leader physically WALK to the chosen site. No distant item teleport or instant house; the native world object must appear only when the actual stored item has been consumed. Verify one world structure per item, correct stacked-amount decrement, no duplicated inventory. No item / invalid slope / occupied footprint => no spawned structure and item intact. Housing rules may reject walls without support and should fallback to appropriate barricade, not fabricate one.
2. **Real depot semantics:** with a verified native NPC-built central STORAGE within 4m of leader, place nonconstruction supplies; transfer must remove one item from simulated inventory and add it to `InteractableStorage.items` exactly once, not every frame. Move leader far away (or block area): transfer must not occur remotely. Full storage must reject without losing or duplicating. Player-owned or free-floating prefabs must never be mistaken for group's native depot.
3. **Persistence and succession:** Save after 1–5 pieces; reload same world using both vanilla world data and MobileNPCs JSON. Confirm native objects persist ONCE (not respawned by NPC snapshot), `group.SettlementPiecesPlaced`/perimeter/central storage progress survives, new leader reconnects to *correct* actual native storage, radio 1445+power still requires items and physically correct state, group guard jobs reserve actual distinct post. Test no materials, partially destroyed base, old v1–v7 JSON default progress and group extinguished. Do not accept orphan/duplicate nil-owned containers as group-owned without physical evidence.
4. **Combat raid discovery:** toggle AI mode Combate; have hostile group leader directly SEE a living defender from a DIFFERENT hostile faction standing near its actual constructed native STORAGE, also visible within 38m. A concrete faction relationship (Raider vs Trader/Survivor/Military) must be HOSTILE, not simply different or neutral. Provisional group home with no storage, player private base, static house and unknown/empty base MUST NOT trigger planning. Obstruction between scout and storage blocks discovery.
5. **Staging and real cooperation:** minimum 2 living armed healthy same-group survivors within physical 14m speaking range, with obstruction tested. Leader should plan a nearby outside-the-base point ~17m from observed depot only when idle, and squad members who can really hear validate their own ground/path. Those behind thick walls/out of speaking range must not receive plan. Record no teleport, no magic target transform, no shooting through wall, and normal group cover/flanking/retreat after genuine enemy sight.
6. **Combat interruptions and mode swap:** while approaching, reveal an enemy in direct sight: the RaidStaging goal must clear immediately and native combat/cover takes precedence. Set AI Normal before clash: staging must cancel and no new raid plan can be issued. Last personal sighting expires in ~100s; raid attempts have ~100s cooldown. If enemy/base disappears or map changes, no persistent phantom target.
7. **Not implemented:** true ownership of native nil-owner NPC pieces, capture/destruction/occupation, flags/control points, persistent region capture after defenders die, scripted base demolition, player-owned base assault, faction reinforcements, raid-plan persistence and multi-POI offensive traversal. Keep MODE-RAID-02 open, do not mislabel as complete. Test stable FPS/GC with 20–40 NPCs, zombies, alarms and construction.

**Acceptance:** limited fixed-pattern native construction observed at the correct physical place without duplication; live group staging triggered only from firsthand rival+base evidence, route-checked and audible; normal tactical combat in contact. No claim of actual territorial conquest until MODE-RAID-02 is coded and tested.

## Native RPG raids, explosive looting and author-defined bomb crafting — 16 Oct 2026

**SOURCE ONLY / MUST COMPILE IN UNITY BEFORE CLAIMING FUNCTIONALITY.** Cover Washington and a second map, 1/4/20/40 NPCs, with Player.log/Profiler and real damageable vanilla walls/barricades.

1. **Spawn and acquisition:** in mode Normal RPG remains optional/irrelevant, no raid. In Combat spawn Raider leader with a genuinely hostile Trader NPC settlement whose actual native STORAGE exists. Observe a REAL RPG and compatible rocket magazine spawned in world. NPC must travel to and pick it via ItemManager, not materialize it or take it through walls; favor rocket ammo even if carrying ordinary rifle. Repeat with RPG-only (no magazine), magazine-only (no RPG) and full inventory.
2. **Crafting:** grant only real ingredient item IDs for a simple vanilla `Blueprint` whose output is `ItemThrowableAsset` explosive AND does native barricade/structure damage. NPC with recent firsthand base knowledge should craft ONE output using real ingredients, no fake recipes or early success. With inaccessible ingredient, lack of space, quest/skill/workstation-only blueprint, nothing is fabricated. Place ingredients in genuine NPC native STORAGE and stand within 4m to test staging/consumption; no remote crafting via stockpile. EBuild.CHARGE can be collected but NOT USED yet.
3. **Raid gate:** no raid staging unless a leader and at least one same-group armed teammate with a LIVE rocket/grenade breacher, with physical speaking/LOS to the enemy's registered storage+living defender; faction relationship must be Hostile. A planned empty "settlement" and static houses never count. No explosion when using only rifles; still scavenge.
4. **Rocket projectile:** same genuinely owned rifle launcher/mag asset as a player using vanilla `UseableGun`. Watch actual `Rocket` prefab fly, collide, use authored explosion/damage to a native wall/depot rather than delete through arbitrary code. Check missing prefab, wall blocking muzzle, nearby ally/safe distance, cooldown 9–13s, bullet/ammo debit exactly once. Change target while rocket is in flight: projectile does not home. Empty magazine -> physical spare-mag swap, otherwise no shot. Count ammo before/after and examine native barricade HP.
5. **Squad members and knowledge:** no member gets leader's magic target. Leader plans spoken staging only; other member carrying RPG can fire against enemy base only if IT directly sees enemy defender and storage. Behind wall, too far (>38m), faction neutral, or NPC friendly near blast: no rocket. Observe native zombie/other-NPC gunshot alerts and effect registration without duplicated listeners.
6. **Thrown explosive:** normal grenade continues only against personally pursued/seen target. Combat leader may use owned structurally damaging throwable at personally seen hostile base with vanilla `Grenade` and explosion fields. Check actual arc prediction, fuse, damage, friendly safety, depleted inventory, no repeat after 45s cooldown. Base no longer visible -> cannot launch at remembered coordinate.
7. **Regression & known limits:** vanilla `Rocket` may not deal damage to custom MobileNPC humanoids without a separate explosion bridge; verify real NPC death/HP. No C4 charge planting/detonation until NPC-native `InteractableCharge` integration; no persistent capture/ownership claim. Visual equip, sound, smoke, ragdoll, kill attribution and multiplayer still require independent verification. Inspect 40 NPC memory/GC cost of blueprint scan every 35–55s with known hostile sites, physics and rockets.

**Acceptance:** real physical breach of a test vanilla barricade with actually owned/fired rocket or consumed grenade; no infinite ammo, no magic target ID, safe path and understandable drop/crafting decisions. The source review is not build verification.

## Native C4 demolition, logical territorial capture and portable-only radio — 16 Oct 2026

**SOURCE ONLY, NOT COMPILATION/UNITY BUILD.** Test with Washington, Development Build, Player.log, and sample original/edited Unity save folders isolated before using irreversible test actions.

1. **C4 inventory + crafting:** grant real `ItemChargeAsset` with `EBuild.CHARGE` AND an `ItemDetonatorAsset`. Missing either disables C4 breacher. Find only genuine world drops; use native simple blueprint ingredients if there is such an actual recipe (do not invent any). Check 1 consumed charge per world object, detonator remains reusable, stack remainder preserved. Full inventory/failure to native drop cannot duplicate items.
2. **Native placement and retreat:** scout must first SEE living hostile NPC by its registered native storage. Breacher must personally revalidate both. Walk within asset.max placement radius (<=3.1m) and have clear SphereCast to actual STORAGE surface (not a player base through a wall), healthy enough with reachable same-floor withdrawal point. Place native charge object on actual wall, inspect BarricadeDrop and InteractableCharge in Unity. NPC must run to the checked safe spot; if blocked or pursued and still close, C4 must **not explode**.
3. **Native blast / failure safety:** check charge `Detonate(null)` executes vanilla explosion and updates world barricade health, with correct native smoke/audio, no fake Player killer/stats. Another ally or host player inside blast radius must prevent detonation. Test death before triggering (charge remains a real world object, new NPC does not inherit detonator), destroyed/deactivated charge, loading old JSON and NPC without working prefab.
4. **Physical occupation:** same leader personally sees rival living defender near same native depot, fights real battle and walks within 8m after enemies leave/die. At least 2 allied surviving NPCs within 12m; no hostile faction NPC within 26m. Assert `TryRecordCapturedSectorServer` accepts **exact originally observed `BarricadeDrop.instanceID`**, not arbitrary empty/no-owner nearby storage, planned settlement, player-built object, through-wall knowledge, or distant AI squad. Guard receives grounded, path-checked local assignment, not sentry vision.
5. **Capture persistence:** save with captured depot intact; reload same world and survivors. Captured marker persists only when native STORAGE with identical instance ID and owner0/group0 exists; destroyed depot clears capture; old JSON without fields remains loadable. No Steam ownership/Native permission transfer occurs. Death of whole group clears shared record per established group lifecycle. Attempt faction recapture/contested site with guards. A map with independent group IDs cannot import claim from another map.
6. **Radio base REMOVED:** while stationary at any base with a walkie ID 1445 placed in native STORAGE and powered generator, NPC **must not transmit/receive unless carrying its own walkie or actually wearing an ItemMaskAsset.isEarpiece**. No map electricity bonus, no imaginary placed radio. Worn earpiece alone receives simulated NPC text; cannot transmit or request reinforcements. Own walkie enables TX/RX. Change its mask to non-earpiece: RX stops if no walkie.
7. **Player interception & vehicle:** player's vanilla `PlayerVoice.canHearRadio` and matching `PlayerQuests.radioFrequency` necessary to see the NPC radio intercept HUD. Earpiece allows player receive; unequipped walkie only in player backpack cannot intercept. Sitting in a car without walkie/earpiece must not add radio functionality; carrying a real walkie does not gain new range from vehicle. No claim of built-in vehicle radio without verified engine support.
8. **Performance and regressions:** 1/4/20/40 NPCs, multiple raids, 2 planted charges and a grenade/RPG, native blast vs P0 custom hitboxes/HP, zombie sound investigations, loot/crafting loops, route checks, no CPU/GC runaway. Prior current editor FPS was poor; measure Profiler before claiming 60 FPS. No multiplayer guarantees. UI settings and safe history deletion remain separate.

## Same-faction walkie relay and future multiplayer hosting — 16 Oct 2026

**SOURCE-ONLY; needs Unity compile + Windows Dev Build.** `PlayerQuests.DEFAULT_RADIO_FREQUENCY=460327`; Survivor=461327, Raider=462327, Military=463327, Scavenger=464327, Trader=465327; Unaffiliated=470327+GroupId. Check actual User UI permits these frequencies.

1. Spawn two DIFFERENT live groups assigned the SAME faction, at >local voice range with genuine walkies. Sender firsthand sees airdrop, hostile target or loot. Report should reach receivers on same frequency who have actual walkie or equipped `ItemMaskAsset.isEarpiece`. Recipients get uncertain positions/hints only, not object pointers or magical visibility. Do not relay if source drops walkie or faction changes before TX.
2. Give receiver an earpiece only in inventory but equip another higher-priority mask: it must NOT receive. Equip earpiece: RX works but cannot report/reinforce via TX. Remove worn mask while report queued: RX stops. Walking to an NPC base with power/walkie in storage or entering a vehicle must NOT give radio.
3. Different-faction groups on different frequencies do not share reports even if both carry walkies. Unaffiliated GroupId channels cannot leak across unrelated groups. Switch sender faction during queued report; old sender group/faction reports are dropped.
4. One healthy idle armed NPC from ANOTHER same-faction group within 90m of reported uncertain sector may volunteer for reinforcement, ordinary physical navigation and time/risk constraints; no instant spawn; more than one cannot be assigned on one transmission. On grouping/faction change cancel. If leader dies, existing responder group lifecycle must remain correct. NPC behind walls still needs route validation to rendezvous, no target telepathy.
5. Player uses vanilla equipped Walkie or earpiece mask + proper frequency to intercept simulated radio text; no native NPC mic audio. Check HUD on singleplayer host after changing frequency and equipment and not after death.
6. Future MP hosting BUTTON is NOT implemented. When ready, separate custom client and custom U3DS build required. First verify dedicated assembly compilation, transport replication of MobileNPC custom visuals/health/inventory and RPC compatibility before UI; Steam server codes/fake IP, GSLT and Save/Shutdown via official server helper. Never assume Steam auth/discovery in an unsigned custom build.

## Priority regression: urban scavenging, native punching and articulated deaths — 16 Oct 2026

**NOT YET COMPILED / NOT RUNTIME-VALIDATED.** This replaces old initial prototype assumptions earlier in this accumulating test file. Latest MobileNPCs use isolated `Resources/Characters/MobileNPC_Client.prefab` so vanilla `NPC_Client` remains untouched. New visual must include `Punch_Left/Punch_Right` animation states. Existing physiological/damage body hitboxes count remains **six** (Skull/Spine/Left_Arm/Right_Arm/Left_Leg/Right_Leg), not 11; 11 rigidbodies are ONLY released in bone-ragdoll after death.

1. **Urban spawn:** Washington city/town, NPC spawned within a block among buildings, Normal, free inventory, no combat. Observe `MobileNPCAutonomyPlanner` chooses Scavenge with higher priority than random Patrol. NPC should inspect building-interior ground points from physically present LevelObject candidates; motor computes legitimate route, can open unlocked native doors. No unrelated novice Director bearing drags him away while valid structures nearby. No teleport through door or wall.
2. **Loot physically from world:** place/preserve true vanilla `ItemManager` item inside reachable house; NPC must get close by physical route, sight/LOS and pickup `TryPickupWorldItemServer`, transfer only real instance, no fake inventory. Repeat on inaccessible shelf and locked door: route fails eventually, memory marks blocked and chooses other candidate; no endless wall oscillation.
3. **Room scans / group dispersion:** two NPCs same squad enter same city. They should claim sectors within ~7m to inspect different rooms; retry history per room radius ~5m, ttl 42s, group visited 28s. Patrol outside still respects wider 22m leases. Test 5–10 bots for A* spikes and repeated CheckCapsule over 6 candidates per nearby building.
4. **Punch animations:** default naked fists NPC attacks authorized visible player, hostile NPC or zombie; observe actual `Punch_Left` or `Punch_Right` visible on cloned prefab and `[MobileNPCVisualAnimation] Confirmed native melee clip` log. NPC equipped with true melee should play a linked kick/punch action as fallback; actual melee damage remains independent and server validated. If missing, inspect `[MobileNPCPopulationBootstrap] Visual legacy Animation ... clips=[...]` and prefab m_Animations references.
5. **Hit reactions:** shoot/strike living MobileNPC; if no authored Hit clip is available, small 0.23s additive animated Spine recoil should appear, before death transition; hit markers alone do NOT prove authoritative HP loss. Head/limb armor remains vanilla.
6. **Death/ragdoll:** shoot NPC until zero. Console must show `[MobileNPCDeathInvariant] PASS` then either `[MobileNPCHumanoidVitals] Death ragdoll activated` showing 11 physics bodies, 10 joints, >=11 actual colliders, and visible articulated joints; or an explicit validation error with physics-only falling capsule fallback (NOT bone ragdoll). No corpse staying rigidly standing, 6 gameplay hitboxes disabled once, no rerender forced by animation. Capture `Player.log` exactly if 11-body ragdoll fails. Repeat with melee kill, zombies, near walls, on slope, and after save/load/corpse despawn.
7. **Profiler and regression:** 1 NPC, 4 NPCs group, 20–40 NPCs urban, record FPS/GC/Physics/ItemManager, LOQ inventory consistency and original zombie behavior. Do not mark feature DONE from string checks or C# syntax alone.

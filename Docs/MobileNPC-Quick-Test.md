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

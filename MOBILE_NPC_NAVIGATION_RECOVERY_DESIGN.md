# Mobile NPC navigation: house corners, real waypoint progress, local A* and tactical failures

**Project:** Unturned Better | **Branch:** `feature/npc-ai-foundation` | **Date:** 2026-10-13

Status **SOURCE IMPLEMENTED / NOT COMPILED OR PLAYTESTED**. Canonical task `CORE-04` remains EM IMPLEMENTAÇÃO. Related `AI-03` (finding real cover) and `LEARN-05` (individual memories of blocked routes) remain open. Read alongside `MOBILE_NPC_PENDING_TASKS.md` and `MOBILE_NPC_PROJECT_HANDOFF.md`.

## Why the NPC oscillated at a house wall

Direct inspection of `MobileNPCHumanoidMotor.Update()` and `TryBuildLocalRoute()` identified several causal issues:

1. **A* waypoint falsely acknowledged as the brain goal.** When the motor was within `stoppingDistance` of the current intermediate waypoint, it unconditionally invoked `brainDriver.ReportDestinationReached()`, even if the true loot/cover/investigation destination was still several meters away. This could prematurely finish seeking cover and produce turn-back behavior. Now a brain destination can only be acknowledged if the **final destination** is within the stopping radius **AND within realistic vertical step tolerance**.
2. **Local and global planning shared `nextRoutePlanTime`.** The motor requested global/native A* in advance and set the shared 0.75-second cooldown, then on encountering a wall it would often immediately refuse local grid A* due to this unrelated cooldown. Separate `nextRoutePlanTime` (native) and `nextLocalRoutePlanTime` (local), initialized and bounded independently.
3. **Invalid local endpoint silently substituted into the route.** `TryBuildLocalRoute` validated ground and occupancy for a selected grid node, but when within 1 meter of a projected local goal it replaced the safe node with the **raw unvalidated localGoal**, which could be inside a wall or void. That substitution has been removed. Every accepted local route point remains the physically checked ground point.
4. **Stuck logic ignored genuine progress around corners.** It measured mostly distance to the far-away brain goal. A good corridor route may temporarily go sideways or *away* from that goal, triggering forced 90-degree recovery and left-right alternation. Now progress toward the active valid A* waypoint or detour waypoint also counts; a stable wall-follow side is kept for approximately 4 seconds rather than flipping every recovery cycle.
5. **Node/waypoint validation crossed floors or collided with the floor.** Planar-only acceptance of waypoints could skip a point on another level. Both regular and detour waypoints now require elevation agreement. Local occupancy test moves its capsule clear of the actual walked-on floor by 0.10m, so ground contact alone does not disconnect all neighboring nodes. Edge heights remain bounded by the real CharacterController stepping limits.
6. **Stale route revalidation on every frame, even during brief crowding.** Route obstruction tests now run at most once per 0.3 seconds (rather than every frame) and distinguish true map obstacles from temporary player/NPC/zombie body collision. Other people waiting in a doorway do not automatically invalidate an otherwise valid global A* route. Walls and closed solid doors still do.
7. **Unreachable cover repeatedly chosen.** After four genuinely stalled recovery events on the same tactical `SeekCover` or `Flank` destination, the motor reports the failed goal to `MobileNPCBrainDriver.ReportTacticalDestinationUnreachableServer`. The brain records personal negative `CoverAccess` evidence for cover, cancels only this optional tactical goal, avoids the same destination within a 3m radius for 14s and may choose another physical candidate later. **Active pursuit, retreat, and other life-or-death actions are not automatically erased.**

## Source commits

| Change | Commit |
| --- | --- |
| Final-goal-only acknowledgement; independent native/local planning cooldowns | `320db52b602caed67e4433121394f3fcaf24c9b3` |
| Don't replace validated A* grid points with unvalidated localGoal; bounded stale route validation | `9fb964973f728bf8c44f3ce90fc01c9e630f305b` |
| Waypoint progress drives stuck recovery; stable wall-follow side | `136a581290fea08add61c080f4af961d4a1f88e2` |
| Waypoint elevation checks + keep occupancy capsule slightly clear of ground | `dabc9f0777c4b8b448f74536bfedffb87d013239` |
| Brief crowding by another actor doesn't discard multi-corner route | `7a718aa67a96f7de6d924739dc2c9e9e6feee624` |
| NPC brain remembers repeatedly unreachable cover and suppresses immediate reselection | `43247204664f8a7e02beeb6866795d15c360b2f3` |
| Motor notifies the brain after four stuck recoveries, without cancelling live pursuit | `ff857bc2a03fdb08694cad6315b6d450dc140307` |

## Routing / state handling

1. A destination is owned by the brain (combat, loot, cover, investigate, follow) or by ordinary roaming. A new destination invalidates stale route state.
2. Native `MobileNPCNativeAStarRouteProvider` may request a path asynchronously from the game's licensed runtime backend, **if actually available**; a failure or pending native request is not permission to teleport or assume a route exists.
3. At short-distance collision, bounded 9×9 local grid A* around the NPC resolves candidate ground, capsule occupancy, slope, step-height, diagonal corner squeezes, and sphere-cast edges. Each accepted waypoint is an existing safe, grounded position.
4. If local A* fails, constrained single-hop detour / terrain-aware steering retains normal collision and checks. Dynamic character congestion allows local avoidance without prematurely deleting a valid static map route.
5. While walking, the motor periodically checks route segment viability; validates reach in both horizontal and vertical axes, and recognizes actual waypoint approach as progress. A waypoint is NOT success of the brain-level objective.
6. If an optional tactical position repeatedly cannot be reached, report explicit failure; record limited individual learning and suppress immediate identical cover choice. Combat pursuit goals remain active for alternative routing or future gameplay decisions.

## Tests to run only in final Dev Build

- House exterior with L-shaped corner, stair landing, doors, windows, narrow corridor, fence and rooftop; player firing from inside and outside.
- Waypoint 2–5m from the final cover objective: reaching the intermediary must not mark cover reached or stop pursuit.
- Two-storey structure with waypoint directly above NPC: do not skip waypoint on vertical overlap or attempt to walk through ceiling.
- Compare native A* backend available vs not available, including pending async requests; local grid must not be blocked by native request cooldown.
- Test 4 allied mobile NPCs and zombies congregating in a doorway: no thrash of routes due purely to temporary bodies; static door closing must still invalidate.
- Sprinting NPC detours around a wall, temporarily increasing its distance to the player; stuck detection must recognize progress to the **current waypoint**.
- Four repeated genuine stuck-recovery cycles at unreachable hard cover: log one failure and choose another cover, avoiding the exact place for 14s; do not discard live pursuit or create cover in thin air.
- 20/40 NPCs simultaneously planning local grids, measure GC/CPU and log volume (0.75s grid cooldown, 0.3s route checks).
- Compare survival action priorities (retreat, cover, loot, medicine), particularly goal cancellation/reach handling. No movement through closed doors, locked buildings or clip volumes.

## Outstanding nontrivial tasks

Not yet implemented or tested: all-map A* connectivity, doors and destructible barricades interaction, full multilevel staircase/ladder links, path smoothing with multi-floor validation, vehicle navigation, swimming under buildings, native dynamic obstacle updates for graph navigation, per-map path cache and detailed `LEARN-05` route memory. A true path failure callback for **general pursuit** needs careful priority semantics and should not silently erase an enemy. All changes remain uncompiled, and none imply a confirmed working Dev Build.

## 2026-10-13 — Unknown interiors: grounded frontier search (source-only)

New source work on `feature/npc-ai-foundation`:

- `7facf6205ad35b6b9b6fc249c88c6bb75c21d624`: `MobileNPCHumanoidMotor` stores up to six *actual stuck-site* observations per individual, tied to the recent goal, each expiring after 18s. This is short-lived tactical navigation evidence, not a mapped portal or persistent worldwide memory.
- `4968e1458ae1657894c9be765cc8e3ef95780b1a`: unknown-area local A* (9×9 / 1.25m) now chooses reachable side/frontier positions using geometric distance + observed dead-end penalty + small boundary bonus, rather than invariably the closest cell adjacent to a wall. Shared admission at most one local search about every 0.10s across NPCs, with per-individual randomized replanning. Cannot guarantee a route around a building much wider than the local grid; always physically move and replan.
- `d5cedb6869905884079679b49be57d7dbd085129`: a preferred A* cell that was actually witnessed as a nearby dead end no longer stops the whole search early; transient living actors may occupy candidate nodes/edges without permanently removing all known walkable terrain. The motor still collides with them in actual movement.
- `a6ce9d3b1a94eb663744228b7be97357934cea58`: a locally planned edge between two grounded nodes also requires a real, height-consistent midway floor sample; fails closed when there is a gap. Cap of 220 such probes per local route. This is not a perfect proof of every centimeter of terrain; CharacterController ground/collision and replanning remain active.
- `8856f815d88729cab7916430beb4e9cd3ce28d2d`: nearby staircase planner may try a second 0.625m XZ resolution when the standard 1.25m 9×9×2 physical floor graph finds no link, only for goal within 2.5m in XZ. Both passes retain 180 expensive connector edge probes, standing capsule, no floor-to-floor teleport, and existing throttling. CPU expense doubles in that narrow case; profiler test required.
- `fde0be8e909cb45359883f3c828db76d8d14894f` + `36d647ef006d3ba552e6a419687a213a7dbb9d94`: optional native ASPFP reflection callback reads `Path.error` and `Path.vectorPath` as fields OR properties, supporting actual documented signatures rather than property-only assumption; rejects malformed nonfinite/clip-invalid/long paths, path endpoints >2m / >0.85m Y from goal, returned origin >4m / >0.85m Y from current NPC, and loose same-XZ different-floor request matches. Native graph availability remains **unverified**. Source: Aron Granberg official Seeker.StartPath documentation.

**Status:** `NAV-UNKNOWN-01` and `NAV-NATIVE-01` scoped source paths **QUASE PRONTA — AGUARDANDO BUILD**, no Unity compile/playtest. `CORE-04` remains **EM IMPLEMENTAÇÃO** because this bounded frontier search cannot guarantee arbitrary unseen long indoor paths without native graph or authored geometry, and jump/ladder links are separate `NAV-JUMP-01`. `NAV-STAIR-01` remains open for complex stairs. Do not claim universal graph coverage or measured FPS.

**Test design:** walls with multiple turns, connected/unconnected apartments, doors open/locked, a building with alternate entrance, a real narrow stair, gap/bridge missing between grounded samples, temporary NPC/Player standing in corridor, player changing target floors, backend present/absent, 1/10/20/40 NPC pressure; collect stuck counters, successful final arrival, A* diagnostic and Unity Profiler data.


## 2026-10-15 — CORE-04: contorno físico e saída de becos sem saída (sem nova IA paralela)

**Evidência que originou o escopo:** a primeira Dev Build produziu **138** eventos `[MobileNPCHumanoidMotor] Blocked`, frequentemente junto a casa/poste/quina ou tarefas opcionais, e 38 avisos `Pathfinding.NavmeshCut` faltando. Não existe evidência de que o A* licenciado do Unturned esteja presente ou que o grafo global conecte salas do mapa; conservar todo fallback local/portas/escadas/ladder/vault/rotas aprendidas até validar na Unity.

**Alterações reais em `MobileNPCHumanoidMotor`, mantendo o CharacterController físico:**

1. `NAV-WALL-CONTOUR-01` (commit `a0a9e652`, hardening `6774289f`): quando o objetivo sem uma rota A* já designada está atrás de obstáculo estático REAL, `TryFollowObservedSolidObstacle` lê o primeiro collider via `SphereCastNonAlloc`, descarta própria anatomia e personagens dinâmicos, usa tangente à normal horizontal de contato com pequena componente de afastamento e verifica se essa direção tem corredor livre/solo próximo. Se o buffer físico satura, não assume caminho livre. Mantém até 4s de escolha de lado; a direção original só é retomada quando houver clearance maior que o probe curto, impedindo o antigo zigue-zague na quina. Se o contorno fracassar ou não houver normal segura, retorna ao steering/A* original; jamais warp. Quando o contorno está realmente ativo, a correção antiga de stuck de 90° não o sobrescreve.
2. `NAV-FRONTIER-REVISIT-01` (`85bb6d6d`, `7898ca95`): um frontier parcial de `TryBuildLocalRoute` carrega marcador provisório de endpoint e objetivo final. Quando o NPC realmente alcança esse waypoint mas continua separado do destino por COLISÃO ESTÁTICA próxima, registra preferência contrária por 8s na memória individual *já existente* `recentNavDeadEndPositions/Goals`; o A* local passa a preferir outra fronteira fisicamente conectada. Nunca confundir waypoint com cumprimento do objetivo do BrainDriver; só memoriza após chegada real, altura compatível e exclusivamente quando não é apenas Player/NPC/Zombie bloqueando a porta. Entrada de porta nativa ou rota conhecida não vira barreira eterna.
3. `NAV-ROAM-RECOVERY-02` (`f0c46b6a`): A-Life patrulhando tem DUAS tentativas extras de replanejar rota física/contorno antes de abandonar a estrutura, com dead-end temporário, sem loop infinito ou perda do alvo tático. Depois da terceira falha real, sinaliza ao mesmo `MobileNPCAutonomyPlanner.ReportRoamBlockedServer` já existente e procura outro destino real; não simula progresso sem andar.
4. `NAV-NONCOMBAT-EXIT-01` (`55396170`, `79c10779`): o `MobileNPCBrainDriver.ReportNonCombatNavigationExhaustedServer` só pode desistir de `Investigating` ou `Returning` sem combate, sem target e sem Goal tático após >=6 travamentos físicos para o ponto corrente. Limpa a pista e retorna a Idle, sem publicar falsa chegada, teleportar para base ou abortar perseguição genuína. Errands Loot/Investigate/Reinforce ainda seguem o próprio `ReportTacticalDestinationUnreachableServer` original após >=4 stalls, protegendo combate.
5. `NAV-OBS-LOG-01` (`79c10779`): logs limitados por NPC (~3s) distinguem objetivo, modo `planned-waypoint/physical-detour/wall-contour/direct-no-path`, disponibilidade real do adaptador `nativeAStarRouteProvider.IsAvailable`, lado e número de recoveries, para comparar os próximos Player.logs. Este log **não prova** suporte native ASPFP nem caminho encontrado.

**Integração obrigatória preservada:** prioridade `BrainDriver` acima do roaming `AutonomyPlanner`; `MobileNPCDoorNavigation` abre porta física destrancada antes de desvio; `MobileNPCLadderTraversal`, saltos padrão, A* nativo opcional, A* local de 9x9 (ampliado por resolução após stalls), memória pessoal `MobileNPCTravelMemory` e janela de cobertura permanecem. Tráfego Player/NPC/Zombie não se torna bloqueio estático memorizado. Vida/loot/grupo/HUD e ZMB-01 não foram alterados. A morte desativa motor e BrainDriver via Vitals.

**Limites honestos:** esta rodada reduz fonte de ping-pong e repetição de frontiers, mas não é capaz de GARANTIR alcançar um cômodo arbitrário ou contornar um prédio maior que os horizontes local/nativo disponíveis, nem garante native A* instalado. Objetivos sem conectividade física correta devem falhar com segurança e escolher outra atividade; inventar rota/atravessar parede/teleportar seria regressão. **Sem compilação Unity nem Dev Build**. `CORE-04` permanece **EM IMPLEMENTAÇÃO/P0**; subitens implementados em fonte são **QUASE PRONTOS — AGUARDANDO BUILD**. Próxima rodada após validação/nav: retornar à ordem anterior de aprimoramento AI-DIRECTOR-02/AI-01, respeitando preferência do usuário.


## 2026-10-15 — Continuação após interrupção: corredor ocupado não é parede

O usuário pediu retomar o trabalho de navegação após uma interrupção, mantendo o objetivo P0 (paredes, postes, quinas e interiores). A rodada já continha os commits físicos de contorno de parede `a0a9e652`, A* frontier `85bb6d6d`/`7898ca95`, retries de patrulha `f0c46b6a`, encerramento não-tático `55396170`/`79c10779` e integração da steering antiga `6774289f`.

**Falha encontrada na retomada:** a rotina de `UpdateStuckRecovery` podia penalizar uma rota e invalidar a memória pessoal do trajeto mesmo quando o único obstáculo próximo era um outro ator vivo (Player, Zombie ou Mobile NPC) parado na porta. O planejador já distinguia `IsOnlyTemporaryActorBlockingAlong` ao verificar trechos, mas o detector final de stuck não compartilhava essa distinção e alimentava falso `RememberLocalNavigationDeadEnd` + `ReportLearnedRouteInvalidatedServer`.

**Correções integradas:**
- `f86a31fa` — `MobileNPCHumanoidMotor.IsOnlyTemporaryActorBlockingCurrentApproach` consulta o MESMO `IsOnlyTemporaryActorBlockingAlong` usado pelo A* do motor, apenas na primeira porção do trajeto real (próximo waypoint, desvio ou destino). Após falta de avanço real por 1,5s, se somente outro ator dinâmico obstrui o caminho, espera por até ~5s, sem marcar parede permanente nem cancelar goal/tática. Persistindo o congestionamento, admite nova tentativa de pathfinding/destino físico, sem falsear memória ou ignorar `CharacterController.Move`.
- `e5060637` — se patrulha A-Life for redirecionada por congestionamento, libera sua lease pelo `MobileNPCAutonomyPlanner.ReleaseExplorationSectorServer(false, false)` **sem** marcar o local como visitado ou bloqueado por terreno. Para goals táticos, caminhos descartáveis podem ser replanejados após a espera, mas jornadas pessoais reais e cadeias de portas/escadas (`activeRememberedRouteIndex`, `activeAccessRouteIds`) ficam preservadas para retomar quando a porta for liberada. `hasPartialLocalFrontier` de rota descartada é invalidado.
- Sem novos componentes, layers, morte, inventário, metas de combate ou teleporte; mantém a ordem de prioridade do BrainDriver, acesso por porta destrancada, saltos físicos e A* nativo opcional. Observação de obstrução é amostrada apenas durante a recuperação (não por frame).

**Limitação objetiva:** corredor estreito permanentemente congestionado pode não ter caminho alternativo físico; a IA deve aguardar, mudar atividade opcional ou continuar tentando conforme seu goal, sem atravessar pessoas nem inventar porta. O fallback 9×9 não assegura navegação global de prédios e ASPFP nativo ainda carece de disponibilidade comprovada. Testar com 1/4/20/40 NPCs, zumbi e player obstruindo passageiros e diferentes trocas de goal. **22 contratos estáticos em 9 arquivos revisados, braces/#if balanceados, nenhuma compilação Unity/Dev Build.** Subescopo `NAV-ACTOR-CROWD-01` QUASE PRONTA — AGUARDANDO BUILD; pai `CORE-04` EM IMPLEMENTAÇÃO.


## 2026-10-15 — NAV-WALL-CONTOUR-02: contorno estável por normal física

Commit `b72e5ae7`: `MobileNPCHumanoidMotor` ganhou último recurso local de **wall following** a partir do `SphereCastNonAlloc` que realmente acerta parede/poste sólido, usando normal horizontal e tangente à face encontrada; NPC/Player/Zombie temporários não são interpretados como paredes. Não inventa portal, não teleporta, não ignora `CharacterController`, e não substitui A* nativo, grade A* local, rotas aprendidas, escadas ou portas. Cada avanço consulta bloqueio real com máscara existente e solo do passo seguinte. Mantém lado de contorno por janela ~4s, em vez de voltar imediatamente a mirar através da quina após 1,6m de corredor aparentemente limpo; exige trecho frontal mais longo liberado para abandonar o contorno. Saturação do buffer de física falha com segurança.

Commit `9cdff1a2`: descarte explícito da escolha de lado quando objetivo desaparece/muda; falha de rota fisicamente medida permite abandonar/alternar lado, sem manter compromisso de obstáculo passado no próximo objetivo. Preferência tática, morte e ações de loot/rádio continuam controladas pelos componentes anteriores.

**Verificação pendente:** Dev Build cruzando poste estreito, parede reta longa, esquina L, porta destrancada/trancada, corredor interno estreito e jogador atrás de parede; não houve Unity compilation/playtest nesta rodada. Não afirmar correção universal: contorno é fallback local, edifícios arbitrários dependem do A* nativo/rotas de conexões físicas. Parent CORE-04 EM IMPLEMENTAÇÃO; subitem `NAV-WALL-CONTOUR-02 QUASE PRONTA — AGUARDANDO BUILD`.


## 2026-10-15 — CORE-04-NAV-CONTOUR-01: anti-oscilação em quinas/postes e fronteira local

**Retomada após interrupção da conversa.** HEAD verificado `2ef135b9559c93b4a7db01b0f45b70c5ee355afc` na branch `feature/npc-ai-foundation`. O commit inclui mudanças efetivamente gravadas em `MobileNPCHumanoidMotor.cs`: contorno de parede/poste pela normal da colisão estática com compromisso lateral curto (~4s), validando a próxima passada por terreno/collision, sem prevalecer sobre waypoints de A* nativo/local, escadas, portas ou rotas memorizadas. Há também `hasPartialLocalFrontier` e memória curta (8s) quando uma fronteira realmente ALCANÇADA termina diante de obstáculo físico e não é o destino final do `BrainDriver`; isso desencoraja reescolher o mesmo nó na grade 9×9. Não foi criado novo cérebro, nem permitido teleporte/atravessar parede, nem atribuído sucesso de chegada ao goal por alcançar waypoint intermediário. As mudanças mantêm tarefas A-Life, rádio, morte e limites de CPU do motor. Verificação estrutural do arquivo passou (chaves e #if balanceados).

**Limitações honestas:** A* global ASPFP ainda não está comprovado na SDK/Dev Build (primeiro log: 38 avisos de `Pathfinding.NavmeshCut` ausente). Contornar parede local não garante atravessar edifícios arbitrários, corredores longos ou interiores de vários pisos; são necessárias entradas, corredores, escadas e conectores reais. 138 avisos `Blocked` ocorreram na build anterior; nenhuma medição nova comprovou melhora. `CORE-04` permanece **EM IMPLEMENTAÇÃO**, escopo `CORE-04-NAV-CONTOUR-01` **QUASE PRONTA — AGUARDANDO BUILD**.

**Próximos testes em Unity:** NPC hostil perseguindo ao redor de poste único, canto em L e casa com porta real; NPC neutro buscando loot dentro de casa por duas quinas; caminhos em diferentes pisos sem shortcut; evitar flip por 4 segundos e repetir depois que obstáculo muda; 1/10/20/40 NPCs; avaliar novos `[MobileNPCHumanoidMotor] Blocked` vs 138 da build anterior. Se não houver caminho verdadeiro, reportar falha e trocar objetivo de baixa prioridade, jamais teletransportar. Próxima rodada depois da navegação: IA-DIRECTOR-02 conforme ordem aprovada pelo usuário, mas somente após priorizar CORE-04.

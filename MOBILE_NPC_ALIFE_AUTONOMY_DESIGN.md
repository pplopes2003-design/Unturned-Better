# MOBILE NPC A-Life — autonomy and job planning

Date: 2026-10-14. Branch: `feature/npc-ai-foundation`.

## Principle and referenced architecture

Model *autonomous behaviors*, not a gigantic STALKER code copy. In X-Ray/Anomaly, smart terrains are job catalogues and squad destinations, a gulag assigns jobs to individuals, schemes execute behaviors, and GOAP evaluates actions. Active ("online") and distant/background ("offline") simulation have different CPU and realism contracts. Useful architectural references:
- https://github.com/damiansirbu-stalker/AlifePlus/blob/main/doc/architecture.md
- https://github.com/robE127/stalker-anomaly-modding-guide/blob/main/docs/systems/smart-terrains.md
- https://github.com/erepb/Anomaly-knowledgebase/blob/main/flows/smart-terrain-core.md

This project is not integrating X-Ray, copying proprietary engine code, or claiming that Unturned's SDK includes STALKER's GOAP. It is a staged, **Unturned-native**, server-authoritative implementation.

## Source implemented in the first slice (AI-ALIFE-01)

- `MobileNPCAutonomyPlanner.cs`: independent low-cost job scores **Patrol / Scavenge / GuardSettlement / Recover**. Evaluates server facts only: individual health and recent damage, consumed hunger and thirst values, actually owned gun, available inventory capacity, assigned living group and built settlement. Two small randomized preferences differentiate otherwise equal NPCs; they are not long-term learned personalities. Scores checked at ~5s intervals with slight per-NPC staggering, minimum commitment ~27s and ~16 utility-point hysteresis. Severe wounds or severe resource needs can override commitment.
- Recover causes a short 6–11s **stationary routine**, not an animation or healing mechanic. Automatically cools down for 36s instead of immobilizing a permanently wounded NPC indefinitely; native medical/needs components still own actual treatment/consumption.
- Patrol retains original long-range exploration. Scavenge increases preference for authentic **world structure destinations**, shortens steps and limits its neighborhood to ~110m while ordinary `MobileNPCLooting` continues to detect/collect **physically visible real drops**. GuardSettlement orbits the actual group settlement anchor (7–20m steps, at most ~26m) instead of dispersing every guard over hundreds of meters.
- `MobileNPCHumanoidMotor`: reads routine only with no higher-priority brain/tactical destination. Normal locomotion path, physical ground checks, collision, native/local A*, door/ladder routes all remain in force. On routine change, cancel only an obsolete roaming destination, never combat, actual loot goal, reinforcement or follow orders. Physically blocked roaming waypoints are remembered for 35s in this individual; don't nominate another within 12m, and 3 repeated failed scavenging walks defer another scavenge attempt for 42s. Only confirmed physical arrival resets failed-walk streak. Does not invent map visibility or successful navigation.
- Bootstrap adds component to the existing inactive server-local prefab. Unity .meta GUID is versioned; no changes to legacy prefab.

**Limits:** This is **online strategic routine arbitration**, not full ALife/GOAP. Existing loot, building, combat and radio component actions are still opportunistic/independent; job scoring influences only ordinary wandering destinations and short recovery pauses. No offline/distant AI step, no real day/night schedule, no assignable world job objects, no full faction economy, no end-to-end crafting task plans, no virtual A* path or persistence of routine across saves. "Scavenge" means searching in plausible districts while the *existing* loot subsystem performs validated pickup; it does not guarantee finding food or a gun. "Guard" is local patrol, not new defense/cover combat system. No visual sit/sleep animation has been authored. No Unity compile or Dev Build validation.

## Follow-on layers (separate stable backlog scopes)

1. **AI-ALIFE-02 — Smart jobs and task ownership:** register physically verified locations with individual or group assignments: guard post, rest point, actual storage, patrol corridor, scavenging POI; reserve jobs so all four squad NPCs do not crowd one object. Each action has real prerequisites (owned items, safe path, observed landmark, destination and deadline) and a distinct success/failure contract.
2. **AI-ALIFE-03 — Short GOAP/utility sequences:** world-state facts, preconditions and action effects for e.g. hungry -> seek *known* food -> travel -> see/drop -> pickup -> eat; out of bullets -> seek compatible ammo; wounded -> cover/medication. Replan on real danger or failed action, not per frame; no magical resource creation.
3. **AI-ALIFE-04 — Offline bounded simulation:** when NPC is far from player, no confirmed interaction/combat and no in-view entity, switch to low-frequency symbolic intent evaluation and persistence, **not virtual in-world pickup, death or teleport across obstacle**. Rehydrate online NPC by validating actual map position, inventory and physics; strictly no combat or loot effects out of view without server-authoritative world transactions.
4. **AI-ALIFE-05 — Group roles, economy, social contact:** leader distributes guard/scout/scavenger tasks; faction-appropriate aggression and radio reports use verified knowledge only. Records attach to individual life and group memory; no global telepathy.
5. **AI-ALIFE-06 — Feedback-driven goal selection:** weigh actual achieved pickup, successful cover, path blockage, health and scarce resources. Record reasons so tests can diagnose "why didn't it scavenge" rather than invent successful activity.

## Developer checks for next single-player build

- On first spawn, NPC chooses one of the four jobs based on **real** local needs/group state. Log `[MobileNPCAutonomyPlanner] ... routine=...` only when job changes in development mode.
- Two NPCs with different needs and carrying states diverge where the facts justify; healthy, fed exploratory NPCs still travel beyond 110m (up to original configured home territory) when terrain permits; group members with a real settlement stay closer while guarding.
- Wounded NPC with no enemy pauses briefly, then resumes activity rather than freezing. Visible confirmed enemy, damage response, radio reinforcement, investigation and player follow order preempt all routine movement; no invalid hotkeys or HUD interaction.
- Three blocked scavenge waypoints trigger cooldown; a reached waypoint does not claim real loot success. Items must transfer through `ItemManager` transaction once and stay bound to individual inventory.
- Test 1/10/20/40 NPC CPU and logs plus map save/load, group leader death/reassignment, settlement creation, active loot and melee/ranged death; no Unity compilation claimed so far.

**Status:** AI-ALIFE-01 source QUASE PRONTA — AGUARDANDO BUILD. Parent long-term A-Life autonomy EM IMPLEMENTAÇÃO.


## 2026-10-14 — AI-ALIFE-02: primeiros smart jobs físicos, sem duplicar o cérebro

Foi implementado o recorte **AI-ALIFE-02-GUARD-01** usando **MobileNPCGroupMember.GroupRecord já existente**, não criando um mundo ou organização paralela. Cada grupo vivo com **armazenamento central nativo realmente registrado pelo líder** pode reservar **dois** postos de vigia individuais em setores opostos (~9–12m da base). Grupo com base apenas planejada, sem contêiner físico ativo, não ganha postos imaginários. Cada candidato precisa de solo físico seguro, continuidade de altura (diferença <=2,1m), ponto seguro de mapa e espaço de ocupação; o motor continua responsável pela rota real e pode falhar caso a passagem não exista. Reservar o posto NÃO significa já estar nele.

Os membros consultam disponibilidade junto à avaliação de utilidade `MobileNPCAutonomyPlanner`; o ato de assumir ocorre somente durante o deslocamento da rotina de guarda. Se a vaga deixar de existir, o membro retorna a Patrol por 20s antes de tentar novamente. A patrulha se concentra ao redor de **seu próprio** posto reservado (passos 3–8m), em vez de quatro agentes se sobrepondo ao mesmo destino. Sair da rotina, mudar de facção/grupo, morrer ou ser destruído libera a vaga. Mudanças do grupo real invalidam cache dos postos; `PruneDeadMembers` remove reservas órfãs. Tarefas de loot/medicina/radio/defesa continuam pertencendo aos sistemas originais, sem transferência de itens, posições mágicas ou auto cura por postos.

O primeiro slice não contém smart terrain para POIs, postos em construções específicas, abertura de portas por job, ocupação com animações dedicadas, economia de trabalho, disputa entre facções e simulação offline; pai AI-ALIFE-02 permanece EM IMPLEMENTAÇÃO. Ainda sem Unity compile/Dev Build.

### Correção P0 paralela: mortalidade do NPC e suas vagas
`UseableMelee` continha uma verificação `info.type != NONE && != SKIP` antes de chegar ao código que aplica dano aos hitboxes personalizados; o fallback de `NONE` agora só entra se o primeiro raycast físico revalidado no servidor atingir anatomia verdadeira de NPC. O protocolo específico `ERaycastInfoType.MOBILE_NPC` já existia em `PlayerInput` tanto na serialização quanto na validação de raio do servidor e permanece preservado. `MobileNPCHumanoidVitals.DieServer` agora também desativa o planejador A-Life, e o `OnDisable` libera a vaga sem depender de respawn. Isso não prova que o dano de tiro ou melee chegou ao HP durante a última build; exige log `Damage accepted` com `health=...->0.0` e `Death transition`, visual de corpo, nenhuma retomada de trabalho e vaga revendicada por vivo. P0 CORE-02 aberto.


**Revisão de sucessão:** `MobileNPCSettlementBuilder` executa reconciliação física uma vez depois que o sucessor assume (mesmo se o padrão de construção já estiver no máximo), permitindo reusar armazenamento real em vez de registrar vagas em uma base imaginária; dependência preservada com COM-BASE/rádio. Sem execução Unity.

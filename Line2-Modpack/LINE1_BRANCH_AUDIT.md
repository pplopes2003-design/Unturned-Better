# Auditoria real de migração — Linha 1 vs Linha 2

**Data:** 7 de novembro de 2026. **Branch ativa:** `line2/modpack-framework`.  
**Ramo antigo mais abrangente inspecionado:** `feature/steam-modpack-local-ai-prototype-2026-11-04`, também comparado aos ramos `feature/integrated-friends-host`, `feature/release-candidate-cycle-2026-11-04`, `feature/hybrid-npc-navigation`, `feature/npc-hybrid-integration` e `backup/alife-survival-2026-10-16`.

## Resposta objetiva

**O cérebro completo da Linha 1 NÃO está na Linha 2.** O antigo `MobileNPCBrainDriver.cs` possui aproximadamente **75 mil caracteres** e combina múltiplos subsistemas; não pode ser substituído por `NpcDecisionBrain.cs` sem perda funcional. A Linha 2 ainda precisa portar e integrar vários executores de decisão. Não basta preservar nomes de classes ou copiar arquivos do assembly principal para um módulo Steam.

| Subsistema existente nas branches antigas | Fonte original de referência | Situação na Linha 2 em 07/11 |
|---|---|---|
| Máquina de decisão, investigação, memória, busca física | `MobileNPCBrain.cs`, `MobileNPCBrainDriver.cs`, `MobileNPCAdaptiveMemory.cs`, `MobileNPCPlayerKnowledge.cs` | **Parcial**: `NpcDecisionBrain` conectado ao protótipo; busca espiral, conhecimento multiagente, emboscada e prioridade completa ainda não |
| Arbitragem tática, objetivos e comprometimento | `MobileNPCTactics.cs`, `MobileNPCTacticalCombatBehavior.cs`, `MobileNPCCombatOnlyPolicy.cs` | **Parcial**: `Line2MobileNPCTactics.cs` migrado da Linha 1 com metas de cobertura/recuo/flanco/reagrupamento; atualmente conectado a dano por explosão + cobertura básica; combat behavior armado ainda falta |
| A* original do Unturned Steam | `MobileNPCNativeAStarRouteProvider.cs` | **Portado/sem teste Steam**: `Line2NativeAStarRouteProvider.cs` por reflexão, `PrototypeAStarFollower.cs`; usa `Pathfinding.Seeker` existente se backend `UnturnedPathfinding_ASPFP` estiver disponível |
| Navegação de portas, janelas e interiores | `MobileNPCDoorNavigation.cs`, `MobileNPCInteriorSurvey.cs`, `HybridNPCObservedPortalRegistry.cs`, `HybridNPCNavigationSession.cs` | **Não migrado**: A* retorna caminho no grafo; não abre portas nem cria conexões dinâmicas onde o grafo não possui passagem |
| Motor de humanoide, escalada, nado, vault e ladeiras | `MobileNPCHumanoidMotor.cs`, `MobileNPCLadderTraversal.cs`, `MobileNPCVaultTraversal.cs` | **Parcial**: `CharacterController`, desvio local, A* follower, animação visual; faltam escadas, água, salto e transições |
| Percepção humanoide e sons | `MobileNPCHumanoidPerception.cs`, `MobileNPCPlayerFootstepSensor.cs` | **Parcial**: visão com raycast e tiros nativos + pista aproximada; outros NPCs e zumbis ainda não são detectados pelo cérebro completo |
| Tiro, melee, mira, recarga, granadas, ferimentos | `MobileNPCHumanoidRangedCombat.cs`, `MobileNPCHumanoidCombat.cs`, `MobileNPCHumanoidAim.cs`, `MobileNPCGrenadeThrower.cs`, `MobileNPCInjurySystem.cs` | **Não migrado**: NPC protótipo não dispara nem aplica danos de arma |
| Explosões e vida/morte | `MobileNPCHumanoidVitals.cs`, `MobileNPCDamageBridge.cs`, `MobileNPCBodyHitbox.cs` | **Correção nova parcial**: `PrototypeNpcVitals.cs` implementa `IExplosionDamageable` para explosões nativas, tem HP/morte, e conserva origem real do blast; outras fontes de dano, hitbox anatômica, ragdoll/loot faltam |
| Rádio físico, inteligência, interceptação e reforços | `MobileNPCRadioOutbox.cs`, `MobileNPCRadioMessage.cs`, `MobileNPCRadioInterceptHUD.cs`, `MobileNPCReinforcementResponse.cs` | **Parcial/offline**: `AIService/npc_radio_network.py` testa regras, mas não está conectado ao inventário nem ao servidor Steam |
| Facções, grupos e relações | `MobileNPCFaction.cs`, `MobileNPCGroupMember.cs`, `MobileNPCAllianceService.cs` | **Parcial/offline**: modelo de população/grupos no Python; faltam relações e NPCs Unity |
| Necessidades, profissões, A-Life e estratégias | `MobileNPCSurvivalNeeds.cs`, `MobileNPCAutonomyPlanner.cs`, `MobileNPCOccupation.cs`, `MobileNPCArchetypePolicy.cs` | **Não migrado** |
| Loot, comércio, diálogos e alianças | `MobileNPCLooting.cs`, `MobileNPCTradeService.cs`, `MobileNPCInteractionService.cs` | **Não migrado** |
| Bases, territórios, raids, airdrops e reforços | `MobileNPCSettlementPlanner.cs`, `MobileNPCTerritoryRaidPlanner.cs`, `MobileNPCAirdropContest.cs` | **Não migrado** |
| Spawn natural, persistência e morte de grupos | `MobileNPCPopulationDirector.cs`, `MobileNPCPersistence.cs`, `MobileNPCPersistentIdentity.cs` | **Parcial**: spawn de protótipos C#, política/grupos Python, sem integração real de save |
| Networking/host amigos | `MobileNPCNetworkReplica.cs`, `MobileNPCNetworkCombatEvents.cs`, `MobileNPCFriendsHostControlServer.cs` | **Não migrado**; protótipos são singleplayer |
| Zumbis, hordas e perseguição entre regiões | `Zombie.cs`, `ZombieManager.cs`, `ZombieRegion.cs` modificados na Linha 1 | **Parcial**: prioridade de visão 20 m opcional e diagnóstico de regiões real, sem hordas jogáveis nem cross-region pursuit |

## A* Pro do jogo original — escolha técnica

O Unturned Steam inclui o **A* Pathfinding Project**. O SDK distribuído não inclui as bibliotecas terceirizadas por causa das licenças. O adaptador antigo da Linha 1 já usa `UnturnedPathfinding.Get()`, detecta `UnturnedPathfinding_ASPFP`, adiciona o componente `Pathfinding.Seeker` em runtime por reflexão, solicita `StartPath` e lê `vectorPath` com timeout/rejeição de rotas obsoletas.

Foi migrado como `Line2NativeAStarRouteProvider` (sem copiar DLLs da biblioteca); `PrototypeAStarFollower` usa os pontos da rota, deixa o `CharacterController` como proprietário da movimentação e só recorre ao steering local se o backend não existir ou se houver falha explícita. No caso de rota A* pendente, espera em vez de atravessar uma parede. **Ainda sem prova de funcionamento em Washington no Steam**. Portas fechadas, escadas, cômodos sem nós A* e atualizações de obstacle graph ainda exigem seus próprios adaptadores.

## Explosões — diagnóstico da falha relatada

No código original da Steam, `DamageTool.explode` usa **`Physics.OverlapSphere` com máscaras específicas** (principalmente `RayMasks.AGENT` para zumbis/animais) e procura `GetComponentInParent<IExplosionDamageable>()`. O `MobileNPCHumanoidVitals` antigo não implementa diretamente essa interface, e ter só `CharacterController` e hitboxes `ENTITY` não garante dano de explosão.

Para a Linha 2, `PrototypeNpcVitals` agora implementa a interface com `IsEligibleForExplosionDamage`, `GetClosestPointToExplosion` e `ApplyExplosionDamage`; o GameObject físico exige `Agent`/ `RayMasks.AGENT`, preserva o comportamento de filtro/obstrução escolhido pelo `DamageTool`, aplica dano radial, registra HP e transição de morte e mantém a origem da explosão para cobertura/recuo. O `NpcExplosionMath` possui testes C# de distância e valores inválidos.

**Limites de cobertura:** essa correção funciona para explosões que realmente consultam a camada AGENT e possuem dano para zumbis/atores; explosivos personalizados que afetem *apenas* Players podem não selecionar o NPC. Também faltam arma de fogo, corpo a corpo, veículos, blast VFX/kill attribution/loot e testes Steam. A Linha 1 congelada não foi modificada.

## Critérios de integração antes de classificar como completo

- Validar GitHub Actions Windows e compilação real do C# net472 contra o Unturned Steam.
- Confirmar que o prefab humanoide `Resources/Characters/NPC_Client` está disponível no runtime Steam; se não, empacotar recurso próprio sem assets licenciados.
- Testar em partida local o backend `UnturnedPathfinding_ASPFP`, uso de grafos existentes e waypoints; instrumentar falhas de portas e regiões.
- Demonstrar que granadas/foguetes reconhecem NPC, respeitam obstáculos, causam dano único, matam e não duplicam dano com várias hitboxes.
- Migrar e conectar todo o resto da lista, especialmente `MobileNPCBrainDriver`, combat executor, rádio e persistência.
- Só então oferecer ao usuário **uma bateria final de gameplay integrada**.

**Referências oficiais:**
- https://docs.smartlydressedgames.com/en/stable/u3-sdk/faq.html
- https://www.arongranberg.com/astar/docs/callingpathfinding.html

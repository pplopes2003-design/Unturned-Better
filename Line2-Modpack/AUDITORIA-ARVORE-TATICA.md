# Auditoria de paridade tática: Linha 1 vs Linha 2

**Escopo:** comparação de código entre `feature/release-candidate-cycle-2026-11-04` e `line2/modpack-framework`, após os patches de ragdoll, tiros, postura armada e reação defensiva. **Não é validação de execução em Steam.**

## Veredito

**Paridade integral: NÃO.** A Linha 2 transferiu muitos contratos e políticas, mas o motor físico e o controlador tático não são equivalentes ao conjunto `MobileNPCBrainDriver + MobileNPCTacticalCombatBehavior + MobileNPCHumanoidMotor` da Linha 1.

| Requisito | Linha 1 | Linha 2 | Lacuna |
| --- | --- | --- | --- |
| Estados de percepção, perseguição, investigação | `MobileNPCBrain`, `MobileNPCBrainDriver`, `MobileNPCHumanoidPerception` | `Line2MobileNPCBrain`, `NpcDecisionBrain`, `Line2NPCCombatPerception` | Adaptado, diferente integração entre percepção e objetivo |
| Prioridade/arbítrio de objetivos | `MobileNPCBrainDriver` | `PrototypeActor.Update`, `Line2MobileNPCTactics` | Parcial: não há controlador idêntico de autoridade de movimento |
| Escolha de cobertura | `MobileNPCTacticalCombatBehavior` e validações de acessibilidade do motor | `PrototypeCoverPlanner` (6 candidatos) | Suporte do piso/obstrução validado, mas rota até cobertura NÃO provada antes da escolha |
| Fuga sob fogo | Motor, tática, validação física | Tática, ponto de recuo e watchdog | Alguns objetivos pendentes ainda podem parar em `transform.position` |
| Flanqueamento | Busca geométrica, rota acessível, flanco por bloqueio de tiro | `TryFindVerifiedFlank` + A* depois | Ponto final pode estar em ilha desconectada; faltam mesmas condições de flanco por LOS obstruída |
| Reposicionamento de tiro durante confronto prolongado | `MobileNPCTacticalCombatBehavior.TryShortCombatRepositionServer` | Postura de tiro estática e flancos opcionais | NÃO equivalente; pode manter posição indefinidamente |
| A* nativo Steam | `MobileNPCNativeAStarRouteProvider` | `Line2NativeAStarRouteProvider` + `PrototypeAStarFollower` | Ponte presente, requer logs do backend e caminhos reais |
| Rotas de acessos indoor/múltiplos pisos | `MobileNPCHumanoidMotor` (aprox. 6842 linhas) | `Line2CoarseRouteBridge` (257 linhas) + serviços menores | Paridade ausente (corredores, ligação de andares, acesso conhecido) |
| Destravamento/recuperação | Motor com rotas, progress tracking, detours | `PrototypeObstacleNavigator`, watchdog e falhas de rota | Parcial, menos robusto em cantos e edifícios |
| Portas/janelas | Motor, descoberta de aberturas e navegação | `Line2MobileNPCDoorNavigation` | Portas parcial; passagens complexas em janelas ainda não equivalentes |
| Equipamento, postura e ragdoll | `MobileNPCVisualAnimation`, `MobileNPCHumanoidVitals` | `Line2NpcWeaponPresentation`, `Line2NpcCorpseRagdoll` | Portes recentes ainda sem teste Steam |

## Evidências concretas e por que o NPC pode ficar parado

1. `PrototypeCoverPlanner.TryGetDefense` testa terreno, `CheckCapsule` e barreira visual, mas não testa uma rota navegável completa antes de retornar `_lastCover` ou `_lastRetreat`. O A* só atua mais tarde.
2. Em `SteamNpcPrototypeRuntime.PrototypeActor.Update`, objetivos defensivos sem `HasTacticalDestination` podem impor `_goal = transform.position`. Isso preserva segurança contra atravessar paredes, mas provoca espera prolongada quando nenhuma rota é confirmada.
3. `Line2NpcFiringPositionPolicy.ShouldHoldPosition` pode zerar o objetivo de deslocamento quando há visão e arma em alcance; o reposicionamento periódico de combate exposto na Linha 1 não é um componente equivalente na Linha 2.
4. `TryFindVerifiedFlank` verifica o chão, a cápsula e a linha de tiro **no destino**; não verifica previamente uma rota contínua do NPC até esse destino.
5. O `PrototypeAStarFollower` pede um trajeto ao ASPFP, mas enquanto aguarda pode retornar velocidade zero. Em fallback, `Line2CoarseRouteBridge` pode retornar zero se não encontrou caminho conectado. Isso não é automaticamente erro: precisa da telemetria de backend e motivo de falha.
6. A Linha 2 usa `LiveNpcDecisionBridge` para sugestões Python, que NÃO conferem conhecimento do alvo nem substituem o motor local. Iniciar o Python não resolve lacunas físicas.

## Ordem para fechar a paridade, sem quebrar regras de segurança

1. Tornar cobertura/recuo/flanco **dependentes de rota fisicamente conectada**, não apenas de posição final válida. Não inventar caminho, teleporte nem atravessar colisores.
2. Portar o **reposicionamento curto e persistente de combate** da Linha 1, coordenado com o mesmo dono da locomoção e respeitando objetivos de defesa.
3. Trazer partes úteis do `MobileNPCHumanoidMotor` por responsabilidade: verificação de corredor, rota entre planos, passagem estreita, bloqueio e recuperação, e não copiar as 6842 linhas integralmente para a DLL Steam.
4. Melhorar diagnósticos em `probe.log`: objetivo, proprietário do movimento, estado do A*, caminho pedido, tempo de espera, posição atual e distância percorrida.
5. Validar no Steam: com e sem visão, tiro e ferimento, cobertura acessível/inacessível, janela e porta, edifício de dois andares, flanco e recuperação. Comparar com observação real no mapa Washington.

**Critério de conclusão:** não declarar todas as árvores e os requisitos presentes até comprovar os casos de gameplay e a compilação Steam.

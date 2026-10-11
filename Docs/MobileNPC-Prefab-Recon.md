# Reconhecimento do prefab humanoide móvel

Branch: `feature/npc-ai-foundation`  
Data: 2026-10-11

## Resultado da inspeção

A revisão estática confirmou as assinaturas usadas pelo controlador de população:

- `MobileNPCSpawnService.TrySpawnBatchAroundServer(..., out int spawnedCount, out string error)` existe e aceita a lista de instâncias.
- `MobileNPCSpawnService.TryDespawnServer(GameObject, out string error)` existe.
- `MobileNPCRegistry.CopyActiveDrivers(List<MobileNPCBrainDriver>)` existe.
- `MobileNPCHumanoidVitals.IsDead` é uma propriedade booleana.
- `MobileNPCHumanoidMotor.InitializeServer()` inicializa também o `MobileNPCBrainDriver` com a posição de surgimento.

Assim, as chamadas verificadas no `MobileNPCPopulationDirector` correspondem às APIs atuais nesses arquivos. Isso é uma revisão estática, não uma confirmação de compilação.

## Por que não ativar o bootstrap automaticamente ainda

A pasta `Assets/Resources/Characters` contém os assets legados `NPC_Server.prefab`, `NPC_Client.prefab` e `Player_Server.prefab`, mas não existe um prefab `MobileHumanNPC` dedicado pronto para a assinatura exigida pelo serviço de spawn.

- `NPC_Server.prefab` é apenas um objeto raiz vazio com Transform no arquivo inspecionado; não atende à assinatura do prefab móvel.
- `NPC_Client.prefab` contém uma hierarquia visual/esquelética, mas não é, por si só, um prefab de agente móvel com `CharacterController`, cérebro, percepção, disposição e ciclo de vida.
- `Player_Server.prefab` pertence ao fluxo de entidade do jogador e não deve ser clonado como se fosse um NPC genérico.

Não é seguro apontar o diretor para qualquer um desses prefabs nem anexar componentes em runtime a NPCs legados. Isso poderia produzir um objeto invisível/incompleto ou interferir em caminhos existentes.

## Próximo passo correto

1. Criar um prefab móvel dedicado que reutilize apenas a hierarquia visual compatível, sem alterar os prefabs estáticos do jogo.
2. Configurar nele `CharacterController`, `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, `MobileNPCHumanoidDisposition`, `MobileNPCHumanoidVitals` e os componentes de combate escolhidos.
3. Validar escala, ponto dos pés, colisão, Animator/rig, máscaras de raycast e sons humanoides.
4. Só então conectar o bootstrap ao evento de carregamento de mapa e carregar o prefab dedicado.
5. Executar um teste de fumaça em single-player no Editor/Unturned.

## Limites atuais

- Nenhum prefab foi criado ou alterado nesta inspeção.
- Nenhum bootstrap automático foi habilitado, pois ainda não há um asset válido para carregar.
- Não houve compilação nem execução do Unity/Unturned neste ambiente.
- NPCs estáticos, zumbis e seus prefabs não foram alterados por esta etapa.


## Follow-up implementation (2026-10-11)

The earlier recommendation not to enable an automatic bootstrap was superseded by a **runtime-only prototype**:

- `MobileNPCPopulationBootstrap` creates an inactive agent template and a population director after a local single-player map loads.
- It loads `Resources/Characters/NPC_Client` only as a child visual hierarchy. It does not use that prefab as the agent root and does not edit the original asset.
- The runtime root supplies its own `CharacterController` and mobile-NPC components; the spawn service activates each clone before initialization.
- `MobileNPCVisualAnimation` tries to reuse legacy animation states by name.

This is not yet validated in Unity. The visual rig/clip names, runtime loading order, collider masks and combat behavior must be tested. Server-local movement/spawn still has no multiplayer replication, full pathfinding, persistent world state, autonomous roaming or base-building.

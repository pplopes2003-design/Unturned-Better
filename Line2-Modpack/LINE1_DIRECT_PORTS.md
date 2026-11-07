# Portabilidade da Linha 1 — componentes preservados

**Branch de destino:** `line2/modpack-framework`  
**Origem:** `feature/steam-modpack-local-ai-prototype-2026-11-04`  
**Verificação:** comparados conteúdos dos arquivos diretamente via GitHub. Os quatro componentes abaixo correspondem integralmente ao arquivo de origem após **somente** a troca de namespace/nome de classe (e, em AdaptiveMemory, tipo de saúde e import SDG.Unturned).

| Classe original | Classe na Linha 2 | Integração atual |
|---|---|---|
| `MobileNPCBrain.cs` | `ClientModule/Line2MobileNPCBrain.cs` | `PrototypeActor` reporta visão real, evidências auditivas e chegada física; o cérebro antigo mantém 8 s de memória de perseguição, 25 s de busca, até três destinos de busca em espiral, retorno à base |
| `MobileNPCPlayerKnowledge.cs` | `ClientModule/Line2MobileNPCPlayerKnowledge.cs` | Memória individual recebe visão e ruídos confirmados; calcula confiança e incerteza, sem seguir o Transform do player através de paredes. Hipóteses de busca ainda requerem validação de caminho antes de serem aceitas |
| `MobileNPCLocomotionPolicy.cs` | `ClientModule/Line2MobileNPCLocomotionPolicy.cs` | Define `Hold`, `Sneak`, `Walk`, `Sprint` a partir de situação observada; `CharacterController` executa velocidade, A* nativo define direção |
| `MobileNPCAdaptiveMemory.cs` | `ClientModule/Line2MobileNPCAdaptiveMemory.cs` | Armazenamento de experiência **individual**, com limite de registros, atualiza preferência de cobertura após realmente alcançar destino; ao morrer, limpa memória. Ainda não persistida no save nem integrada às demais fontes de experiência |

O seletor `MobileNPCTactics.cs` também foi portado previamente como `Line2MobileNPCTactics.cs` (pequena adaptação de configuração). O `MobileNPCNativeAStarRouteProvider.cs` também já foi portado como `Line2NativeAStarRouteProvider.cs` e usa o **Seeker do A* nativo instalado no Steam por reflexão**; não redistribui a biblioteca proprietária.

## Responsabilidade explícita: evitar dois cérebros competindo

- `NpcDecisionBrain`: **prioridade de comportamento** local conforme ameaça confirmada, saúde e modo de jogo; não decide cada ponto da investigação
- `Line2MobileNPCBrain`: **destino de perseguição/seguimento/investigação** e avanço da busca somente após aproximação física
- `Line2MobileNPCTactics`: **metas defensivas urgentes** quando há saúde/dano/cobertura reais; podem tomar temporariamente o movimento
- `Line2MobileNPCPlayerKnowledge`: **aconselhamento por memória sensorial**, nunca mira telepática
- `Line2MobileNPCLocomotionPolicy`: **gait/velocidade**; não escolhe destino
- `Line2NativeAStarRouteProvider` + `PrototypeAStarFollower`: **rota via A* nativo**, enquanto `CharacterController` continua único proprietário do deslocamento
- `Line2MobileNPCAdaptiveMemory`: registra êxitos físicos limitados; não altera o grafo, vida nem permissão de combate

Esta separação preserva a fonte original sem criar novos controladores que movam o Transform diretamente ou contornem máscaras de colisão.

## Verificações disponíveis

`BrainTests/BrainTests.csproj` agora inclui os arquivos C# originais adaptados e testes para: perseguição com visão direta, não seguir `Transform` quando o jogador desaparece, entrada em investigação, três visitas sucessivas, retorno ao spawn, validade de memória sensorial, modos Sprint/Hold, seleção de cobertura/recuo e queda radial de dano de explosões. `check_sdk_api_surface.py` verifica que a integração e símbolos permanecem.

**Atenção:** inclusão nos testes **não confirma execução bem-sucedida** do GitHub Actions; o módulo Steam completo também não foi compilado nem testado nesta rodada.

## Pendências principais que ainda exigem adaptação do código existente

`MobileNPCBrainDriver.cs` (coordenação total, ~75 mil caracteres), `MobileNPCTacticalCombatBehavior.cs`, `MobileNPCHumanoidRangedCombat.cs`, `MobileNPCBodyHitbox.cs`, `MobileNPCHumanoidPerception.cs`, `MobileNPCDoorNavigation.cs`, `HybridNPCObservedPortalRegistry.cs`, `MobileNPCRadioOutbox.cs`, `MobileNPCGroupMember.cs`, `MobileNPCPersistence.cs` e demais serviços identificados em [LINE1_BRANCH_AUDIT.md](LINE1_BRANCH_AUDIT.md).

Não classificar a Linha 2 como concluída antes dos executores de combate e danos, portas/edificações, facções e rádio, persistência, multiplayer e dos testes no Unturned Steam.

## Portes adicionais na mesma rodada

| Origem antiga | Classe nova | Integração e limites |
|---|---|---|
| `MobileNPCPlayerFootstepSensor.cs` | `Line2MobileNPCPlayerFootstepSensor.cs` | Adaptado para `Player.player` na partida local; amostra deslocamento real para diferenciar passos/corrida e informa apenas pistas auditivas quantizadas aos NPCs próximos. Não cria falsos sons por teleporte nem acompanha players de outros servidores. |
| `MobileNPCTravelMemory.cs` | `Line2MobileNPCTravelMemory.cs` | Portado integralmente após troca de namespace/tipo de vitals. `PrototypeAStarFollower` registra exclusivamente passos **fisicamente percorridos pelo CharacterController**, aprende apenas trajetos realmente concluídos e apaga conhecimento na morte. A função de reutilizar uma rota aprendida **não está habilitada** sem validação de colisão e A* atual, pois portas e construções podem mudar. |

A Linha 2 também reforçou a autoridade singleplayer (`Provider.clients.Count <= 1` quando disponível). Se uma sessão com amigos começar, o runtime remove os protótipos locais para evitar NPCs não replicados. Isso **não** implementa multiplayer.

### Precisão da migração

As classes `Line2MobileNPCBrain`, `Line2MobileNPCPlayerKnowledge`, `Line2MobileNPCLocomotionPolicy`, `Line2MobileNPCAdaptiveMemory` e `Line2MobileNPCTravelMemory` preservam o código-fonte funcional da Linha 1, com apenas nomes/namespace e associação à saúde do protótipo alterados. O sensor de passos exigiu uma mudança adicional deliberada: os eventos deixam de ir para a antiga registry e entram no runtime Steam atual. `BrainTests` valida parte desses comportamentos de forma isolada, **não o funcionamento in-game**.

**Ainda pendentes:** comportamento completo de combate com armas e dano, integração de portas/janelas indoor com o grafo A* existente, facções, rádio, NPCs contra zumbis, persistência no mundo e multiplayer. A fonte original do `MobileNPCBrainDriver` continua significativamente maior que o pequeno árbitro da Linha 2.

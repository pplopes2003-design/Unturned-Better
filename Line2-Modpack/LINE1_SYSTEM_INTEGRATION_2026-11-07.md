# Linha 2 — consolidação de inventário, combate, facções e rádio

**Data de desenvolvimento:** 2026-11-07.  
**Branch de trabalho:** `line2/modpack-framework`. **Main e Linha 1 não foram alteradas.**  
**Fonte primária:** `feature/steam-modpack-local-ai-prototype-2026-11-04` e [auditoria das branches](LINE1_BRANCH_AUDIT.md).

## O que foi adaptado e conectado ao runtime Steam nesta rodada

| Sistema da Linha 1 | Adaptação executada na Linha 2 | Restrições reais |
|---|---|---|
| `MobileNPCInventory.cs` | `Line2MobileNPCInventory.cs`, preservando cópias individuais, instâncias de armas, seleção, carregadores, consumo/reposição de munição, equipamento e inventário finito | A engine modificada tinha `ItemManager.TryTransferWorldItemToMobileNPCServer`, **inexistente no Steam**; coleta do chão **falha explicitamente** até que um adaptador transacional nativo retire o drop original. Nunca duplicar loot |
| `MobileNPCRaidSupplyPolicy.cs` | `Line2MobileNPCRaidSupplyPolicy.cs`, classificação de assets nativos | Não fornece explosivos nem executa raids sozinho |
| `MobileNPCCombatOnlyPolicy.cs` | `Line2NPCStartingEquipment.cs`: 5 pares genuínos de armas/carregadores da Linha 1 (Maplestrike, Zubeknakov, Bluntforce, Snayperskya e Grizzly), autovalidação por calibre no catálogo Steam, opcional Auto 24 custom 32610/32611 apenas se instalado. Inicializa exatamente UMA vez no spawn; dois carregadores sobressalentes, itens médicos e faca físicos | Kit é *loot inicial legítimo gerado no nascimento do NPC*, não abastecimento infinito. No modo normal nem todos NPCs nascem armados |
| `MobileNPCHumanoidRangedCombat.cs`, `MobileNPCHumanoidAim.cs` | `Line2NPCRangedExecutor.cs` reproduz o ramo de arma de projétil não explosivo: delay inicial de reação, dispersão espacial, sequência de 2–5 disparos, pausa, cadência limitada por firerate nativo, alvo confirmado e raycast físico pelo primeiro colisor. Dano pelo `ItemGunAsset`, `DamageTool.damagePlayer`, `Zombie.askDamage` ou `PrototypeNpcVitals` do outro NPC | **Adaptação funcional de arquitetura, NÃO porte integral** dos algoritmos de mira/aprendizado balístico, arma visual, áudio, física de projéteis ou animações completas da Linha 1. Falta compilação/gameplay. Não alegar fidelidade balística Steam |
| `MobileNPCInventory` + ramo de reload do ranged | `Line2NPCAmmoRules.cs` impede tiro sem munição, mais de 255 balas ou estado maior que a capacidade do magazine. O tiro debita o estado da instância exata antes de aplicar dano. Recarga atrasada consome uma peça genuína do inventário quando completa; preserva munição parcial anterior e tenta rollback seguro | Tratar falhas de rollback como incidentes, não ignorá-las; sem persistência, inventário ainda não sobrevive ao fechamento do jogo |
| `MobileNPCFaction.cs` | `Line2NPCFactionPolicy.cs` mantém **a matriz original** Raiders/Traders/Military/Survivors/Scavengers/Unaffiliated | Facção local derivada do papel; sem mudanças de reputação e diplomacia persistentes |
| `MobileNPCGroupMember.cs` | `Line2NPCGroupMember.cs`: até 4 membros vivos próximos em 140 m, facção igual; grupos sem membros são removidos; grupo limpo ao encerrar sessão | Não reproduz território, bases, guarda, raid, respawn de líderes, identidade permanente ou networking |
| `MobileNPCRadioOutbox.cs`, `MobileNPCRadioMessage.cs` | `Line2NPCRadioOutbox.cs` + `Line2NPCRadioRules.cs`: hostil com walkie nativo id 1445 pode enviar informação de jogador **realmente visto** a membros vivos do mesmo grupo e facção, que também possuem walkie e estão a até 90m. Posição é reduzida a setor de 20m, com intervalos/cooldowns e memória curta. Receptor investiga sem ganhar visão, mira nem poder de disparo | NÃO há voz real, interceptação pelo player, escuta de frequência do PlayerQuests, earpiece realmente vestido, reforços de outros grupos nem rádio entre clientes. Não confundir com o sistema completo da Linha 1 |

## Regras de autoridade e desempenho

- **Singleplayer local autorizado**, cheats e host: todos os danos e mensagens têm validação de autoridade local; ao entrar outro cliente, os NPCs locais são removidos até existir replicação.
- CPU: percepção de zumbis por região e janela global pequena; radio por amostra cadenciada e no máximo 32 atores, raycast apenas para tiro com cooldown real, controles de população existentes preservados.
- `PrototypeActor` continua sendo o único ponto de arbitragem de objetivos; física apenas em `CharacterController` com orientação de `PrototypeAStarFollower` e Seeker do A* presente no Steam.
- Fogo não nasce de mensagens de rádio, de sugestões do AI Service, de munição imaginada ou de objetos atravessando paredes.
- Esses recursos são C# no módulo `Unturned2.Line2.Probe` e **não** restauram automaticamente as alterações nativas em `ZombieManager` e `Zombie.cs` da Linha 1.

## Regressões automatizadas

O executável independente `BrainTests/BrainTests.csproj` compila/testa regras de prioridade, facções, segurança, munição finita (inclusive 24/12 cartuchos e recarga), critérios de transmissão entre grupos de rádio, alcance, item real, quantização de posição, comportamentos básicos de investigação, trauma e táticas. O script `check_sdk_api_surface.py` checa presença do novo código e símbolos públicos do SDK original.

**IMPORTANTE:** a pipeline atual não possui os DLLs efetivos do Steam instalado no Windows. Portanto, mesmo se todas essas verificações passarem, **NÃO** é prova de que o módulo inteiro compila nem de que o jogo e combate foram testados.

## Bloqueios que ainda impedem declarar “TUDO pronto”

1. **Compilação completa contra DLLs do Unturned Steam atual** e correção de eventuais símbolos internos, tipos e assemblies. Depois, testar em Washington com os NPCs em situação real.
2. Porte integral de `MobileNPCBrainDriver` e sua integração às entidades Steam, em especial executores de decisões complexas (combat tactics, rotas de portas/janelas e indoor A*, som e rádio avançado).
3. `MobileNPCHumanoidRangedCombat` completo: balística com quedas/tiro em movimento, armas/granadas nativas visíveis e animadas, recarga visual, áudio e modelos equipados; no momento há um executor experimental de tiro direto.
4. Interação completa **zumbi ↔ NPC**: zumbis vanilla não selecionam esse humanoide como alvo, mesmo que NPCs possam observar/atacar zumbis. Hordas, investigação e perseguição em regiões descarregadas permanecem pendentes.
5. Transferência de loot do chão para inventário **atômica**; drops de morte reais, equipamentos e armadura com efeitos; craft e economia/lojas/diálogos.
6. Grupos/facções com identidade, bases, reivindicações, território, raids, reforços, rádio físico com frequência/interceptação e A-Life contínuo.
7. Save/load real (IDs, posições, ferimentos, grupos, inventário e progressão), respawn e limites/população de longo prazo.
8. Networking autoritativo para hospedar entre amigos, suporte Workshop e integração com modo Combat Only versus sobrevivência normal.
9. **Bateria final de gameplay integrada**, que o usuário pediu que não fosse fragmentada.

Não confundir código versionado, teste de política isolada, teste de compilação da DLL e aprovação real de gameplay; são gates distintos. A `main` fica base original. Este documento será atualizado conforme validação completa, sem alegar entrega final antecipada.

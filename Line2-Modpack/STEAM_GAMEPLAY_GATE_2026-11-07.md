# Linha 2 — Gate de validação real no Steam (07/11/2026)

**Branch:** `line2/modpack-framework`. **Linha 1 e main não são alvos deste gate.**

## Evidência existente

- Os testes Python / BrainTests / auditoria estática de SDK foram executados na pipeline anterior, mas não são uma build real do módulo Steam.
- O serviço localhost `AIService/server.py` é apenas um decisor determinístico: não aplica dano nem move Unity GameObjects.
- O `SteamNpcPrototypeRuntime` só autoriza protótipos no singleplayer local com cheats, sem BattlEye. Não há replicação multiplayer.
- O `LiveNpcDecisionBridge` agora envia a fração real de saúde, dano recente e cobertura validada pelo runtime. Sugestões defensivas são recusadas sem dano/cobertura/rota local apropriados.
- A coleta do chão usa um novo adaptador **singleplayer experimental**, limitado à instância real do `ItemManager.regions` com transferência por `instanceID`, reserva do inventário NPC, verificação física e `ItemManager.ReceiveDestroyItem`. Sem compile/gameplay Steam, **não** afirmar ausência de perda/duplicação ou ampliar a hosts com convidados.
- Foram integrados adaptadores experimentais para: persistência por mapa (incluindo ferimentos e NPCs de regiões ainda não carregadas), necessidades, cura com itens nativos, diálogos/comércio por XP, reposição atrasada, ataque de zumbis a NPCs, rally local de hordas, múltiplos pellets e trajetória de projétil, lançamento físico de granadas nativas e loot do inventário do NPC morto. Todos exigem compilação contra os assemblies Steam e validação in-game.
- Da Linha 1 foram migrados o planejador A* incremental de horizonte (61m), validação física de arestas/pisos/portas, salto por peitoril baixo e rotina A-Life de exploração/guarda/recuperação. Foi acrescentada construção real limitada de 4 barricadas nativas por grupo, com consumo de materiais/carregamento de receitas nativas e registro por mapa. Isso **não** equivale ao `BrainDriver` completo nem a construção avançada de casas/fortalezas.
- `Line2NpcObstacleAudit` registra apenas colisões realmente observadas com objeto nativo, tipo de colisor, camada e destino; serve para localizar os assets que continuam bloqueando a IA, sem desativar fisicamente paredes ou vidros.
- A aprovação da pipeline não comprova navegação indoor, dano de zumbis em NPCs, A-Life, bases, comércio ou persistência.

## Preparação

1. No Windows, atualizar a branch e a instalação oficial do jogo.
2. Sem BattlEye, compilar o módulo com a pasta de jogo real:
   `dotnet build .\ClientModule\Unturned2.Line2.Probe.csproj -p:UnturnedDir="C:\Program Files (x86)\Steam\steamapps\common\Unturned"`
3. Copiar `ClientModule/Unturned2.Line2.module` e o DLL gerado para `Unturned/Modules/Unturned2.Line2/`.
4. Executar `python AIService/server.py`, abrir Washington em singleplayer com cheats, ativar NPCs no menu F7.
5. Salvar `Unturned2-Line2/probe.log`, a saída do build e os logs do jogo após a sessão. Não compartilhar identificadores pessoais presentes nos logs.

## Matriz de verificação (não marcar como aprovada sem evidência no jogo)

| Cenário | Evidência necessária | Estado |
|---|---|---|
| Carregamento do módulo | `Module initialized` e menu F7 | Pendente de teste Steam |
| Spawn e anatomia | NPC visível, colisor funcional, nome e hitboxes | Pendente de teste Steam |
| Percepção e localhost | `NPC_AI_LIVE_BATCH_OK` com NPC válido | Pendente de teste Steam |
| Saúde real e recuo | Dano validado reduz HP; sob pressão NPC procura cobertura/recuo | Pendente de teste Steam |
| Combate completo | Armas gastam munição real e respeitam recarga, cadência, precisão e LOS | Pendente de teste Steam |
| Explosões | Granada nativa causa dano verificado aos NPCs na camada Agent | Pendente de teste Steam |
| Portas e janelas | Entrada/saída por portal real sem atravessar paredes | Portas nativas + fallback incremental + salto físico baixo implementados; escadas, vidros e janelas exigem validação no Steam |
| Zumbis contra NPCs | Zumbi percebe, persegue e causa dano no NPC | Adaptador local experimental integrado; teste Steam pendente |
| Hordas/regiões | População vanilla respeitada e transição regional | Rally local no mesmo nav bound; perseguição entre regiões NÃO implementada |
| Loot do chão | Transferência atômica sem duplicação/perda após reinício | **Código local conectado; Steam não validado:** só itens físicos no alcance, por ID exato, com rollback e checkpoint pós-coleta; suporte multiplayer permanece bloqueado |
| Comércio/bases/A-Life | Ciclo de interação, tarefas, materiais e mundo persistido | Comércio básico, necessidades, coleta local, exploração, bases simples com barricadas nativas e observação visual de rival/estágio de raid codificados; depósitos/raids completos/fortalezas, não |
| Multiplayer | Host autoritativo, replicação, entrada/saída, reconexão | Não integrado; runtime local suspende NPCs se entrar outro jogador |

## Componentes e prioridades que ainda impedem o teste único final

1. **Compatibilidade real:** compilar toda `ClientModule` com a versão instalada do Unturned Steam e resolver eventuais APIs Unity/Assembly-CSharp divergentes. O Windows CI testa políticas C# isoladas, Python e superfícies textuais de SDK, não o módulo real.
2. **Cérebro completo:** portar os estados restantes de `MobileNPCBrainDriver` e operadores táticos, incluindo objetivos de facção, formação de grupos, invasões, recuo em combate prolongado e reação avançada a zumbis. Hoje há subconjuntos funcionais integrados e prioridade local de combate; **não** uma porta 1:1 completa.
3. **A-Life e bases:** validar o novo algoritmo de coleta do chão contra o `ItemManager` real (sem perdas/duplicação entre checkpoints), construção persistente das barricadas nativas e suas colisões; acrescentar caixas/depósitos/ocupação de estruturas, planejamento logístico, invasão e destruição coordenada de bases usando armas e explosivos realmente carregados. A aproximação de raid atual é reconhecimento físico + movimentação, não destruição autônoma completa.
4. **Navegação avançada:** comprovar conexão do ASPFP original e do grafo alternativo com portas, janelas quebradas, baixa travessia física, escadas, andares e obstáculos móveis em mapas reais sem clip ou teletransporte.
5. **Zumbis:** validar dano mútuo, perseguição em regiões/níveis e o comportamento de investigação/hordas ao perder o alvo; o rally atual reutiliza zumbis já existentes, não altera o limite vanilla nem garante travessia de regiões.
6. **Multiplayer integrado:** host Steam, sincronização autoritativa de NPCs/danos/itens/radio e entrada/reconexão de convidados. A Linha 2 permanece intencionalmente singleplayer local enquanto isso não existir.
7. **Auto 32 e distribuição:** preservar IDs, carregadores 24/12, rajada automática, material de modpack/Workshop e assinatura/instalação, testando possíveis conflitos de mods no cliente Steam.

### Evidência de testes desta rodada
- `BrainTests` do código original portado: sucesso na rota incremental por qualquer lado da parede e recusa de caminhos desconectados.
- Persistência isolada: ida e volta de inventário, grupos, ferimentos, identidades, tickets de reposição e manifesto de barricadas de cada base, com rejeição de registros corrompidos.
- Ledger de grupos isolado: sem duplicar posição de barricadas, máximo de 4 por grupo, separação entre bases rivais, cópias independentes e não reaproveitamento de IDs de bases extintas (com ligação de runtime).
- A auditoria `check_sdk_api_surface.py` inclui os símbolos originais de granadas, objetos de mapa, portas, transação de itens (`ItemManager.regions`, `ItemData`, `ReceiveDestroyItem`), construção (`BarricadeManager.dropBarricade`) e reconhecimento de barricadas (`FindBarricadeByRootTransform`).
- **Nenhuma dessas evidências substitui uma build real ou demonstra o gameplay completo.**

**Regra de liberação:** só anunciar uma build integrada de gameplay após passar os testes automatizados, compilar contra assemblies Steam reais e observar os cenários acima sem regressões. O adaptador singleplayer de coleta deve ser exercitado com drops reais, telemetria da remoção, reinício do mapa e múltiplos NPCs disputando o mesmo `instanceID` antes de ser considerado seguro. Nunca habilitar o código local de NPC/loot enquanto houver clientes multiplayer não replicados.

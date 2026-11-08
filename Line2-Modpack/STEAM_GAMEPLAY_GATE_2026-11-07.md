# Linha 2 — Gate de validação real no Steam (07/11/2026)

**Branch:** `line2/modpack-framework`. **Linha 1 e main não são alvos deste gate.**

## Evidência existente

- Os testes Python / BrainTests / auditoria estática de SDK foram executados na pipeline anterior, mas não são uma build real do módulo Steam.
- O serviço localhost `AIService/server.py` é apenas um decisor determinístico: não aplica dano nem move Unity GameObjects.
- O `SteamNpcPrototypeRuntime` só autoriza protótipos no singleplayer local com cheats, sem BattlEye. Não há replicação multiplayer.
- O `LiveNpcDecisionBridge` agora envia a fração real de saúde, dano recente e cobertura validada pelo runtime. Sugestões defensivas são recusadas sem dano/cobertura/rota local apropriados.
- A coleta do chão usa um novo adaptador **singleplayer experimental**, limitado à instância real do `ItemManager.regions` com transferência por `instanceID`, reserva do inventário NPC, verificação física e `ItemManager.ReceiveDestroyItem`. Sem compile/gameplay Steam, **não** afirmar ausência de perda/duplicação ou ampliar a hosts com convidados.
- Foram integrados adaptadores experimentais para: persistência por mapa (incluindo ferimentos e NPCs de regiões ainda não carregadas), necessidades, cura com itens nativos, diálogos/comércio por XP, reposição atrasada, ataque de zumbis a NPCs, rally local de hordas, múltiplos pellets e trajetória de projétil, lançamento físico de granadas nativas e loot do inventário do NPC morto. Todos exigem compilação contra os assemblies Steam e validação in-game.
- Da Linha 1 foram migrados o planejador A* incremental de horizonte (61m), validação física de arestas/pisos/portas, salto por peitoril baixo e rotina A-Life de exploração/guarda/recuperação. Foi acrescentada construção real limitada de até 5 peças nativas por grupo (uma caixa de armazenamento central e quatro defensivas), com consumo de materiais/carregamento de receitas nativas e registro por mapa. Isso **não** equivale ao `BrainDriver` completo nem a construção avançada de casas/fortalezas.
- `Line2NpcObstacleAudit` registra apenas colisões realmente observadas com objeto nativo, tipo de colisor, camada e destino; serve para localizar os assets que continuam bloqueando a IA, sem desativar fisicamente paredes ou vidros.
- O `MobileNPCBrainDriver` da Linha 1 ganhou nesta rodada um port parcial de **monitoramento de progresso físico tático**, temporização de fuga/flanqueamento, bloqueio de rotas sem conexão e **reagrupamento pessoalmente observado** de aliados. Chegar à posição de recuo não encerra o recuo imediatamente. Contato visual com inimigo interrompe tarefas de regroup/observação de raid.
- A morte real do NPC agora faz um checkpoint mesmo com natural spawn desativado, evitando reaparecimento de personagem morto após a criação de loot de cadáver.
- O reconhecimento de base rival ganhou **ataque corpo a corpo com item melee nativo**, contra barricada física observada e não pertencente a jogador (aplicação via `BarricadeManager.damage`, desgaste do item). Raids explosivas/coordenadas, não.
- A busca de integrantes da horda examina uma janela limitada de zumbis já carregados inclusive além da região de navegação do líder; `Zombie.alert` e o caminho vanilla continuam responsáveis pelo movimento, sem garantia de perseguição inter-regional.
- Da Linha 1 foi acrescentado o `Line2NpcInvestigationProbe`: os pontos da espiral e hipóteses sensoriais passam por teste de piso verdadeiro, cápsula desobstruída, altura compatível e orçamento limitado; rota definitivamente desconectada avança para o próximo ponto. `Line2NpcConcealedApproachPlanner` só escolhe abrigo a partir de uma pista própria ainda válida. O NPC só prepara arma/combate após a percepção atual confirmar pessoalmente o alvo.
- O rádio de facções agora pode orientar até um waypoint **local (máximo 12m)** na direção do setor aproximado transmitido. Isso não concede mira, teletransporte, nem localização precisa do jogador.
- A lógica de `MobileNPCEncounterDirector` foi parcialmente migrada: no máximo dois integrantes de um mesmo grupo executam buscas adicionais por hipótese simultaneamente. As autorizações expiram após oito segundos e são descartadas na morte, contato visual e troca de mapa; o sensor e combate normais não sofrem limitação.
- Uma morte violenta fisicamente observada ou ouvida a curta distância pode acionar retirada limitada de aliados sobreviventes do mesmo grupo para cobertura local; sem alvo herdado do morto.
- Novidade A-Life: NPCs observam itens nativos úteis até 19m e iniciam uma jornada pelo A* para chegar ao drop verdadeiro. Uma reserva temporária de `instanceID` limita a disputa por item e expira/release no desaparecimento, morte/remoção do actor e troca de mapa. Sobrevivência urgente prioriza alimento/água/medicina. Não cria itens por tabela de spawns nem teletransporta NPCs.
- Monitor de rotas **não táticas**: patrulha, exploração de ponto authored, guarda e scavenging agora exigem progresso real do CharacterController, com desistência após caminho desconectado/estagnação e tolerância a desvios e espera limitada do ASPFP nativo. Objetivos que falharam são temporariamente lembrados para evitar insistir na mesma parede.
- Apoio presencial de grupo: um NPC fisicamente próximo de um aliado necessitado pode transferir uma unidade autêntica de água, comida ou medicina, com reserva do inventário receptor e tentativa de rollback da origem. É proibido via rádio, através de paredes ou durante combate e exige que o doador possua material excedente. Não é ainda uma logística persistente de depósitos.
- O caminho local de `ItemManager.ReceiveDestroyItem` foi endurecido contra callback de remoção que lance exceção depois de destruir o drop: o estado real do drop é consultado antes de restaurar o registro original, evitando duplicação fantasma de `ItemData`.
- A base experimental agora reconcilia manifestos com o registro físico do BarricadeManager por identificação nativa, asset, posição e proprietário. Um membro próximo pode depositar suprimentos excedentes em uma caixa nativa realmente registrada, ou retirar alimento, água e curativos existentes mediante reserva exata e verificação de ItemJar, LOS e espaço. Há jornadas A-Life para entregar suprimentos e buscar ração/medicina, sem criar itens. A caixa e seu conteúdo permanecem geridos/salvos pelo jogo Steam; ainda exigem teste de salvamento e colisão in-game.
- O grupo abstrato é aposentado quando o último sobrevivente comprovado morre; após restaurar todos os atores, campings históricos órfãos também são aposentados. As barricadas do mundo não são destruídas pelo mod e permanecem como ruínas nativas.
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
| Percepção e localhost | `NPC_AI_LIVE_BATCH_OK` com NPC válido | Evidência sensorial, busca com piso/altura e emboscada de alvo observado integradas; servidor Python e atualizações Steam pendentes de teste em jogo |
| Saúde real e recuo | Dano validado reduz HP; sob pressão NPC procura cobertura/recuo | Monitor físico de destino, limite de travamento, blacklist de fuga e manutenção temporária de cobertura/flanco implementados; Steam pendente |
| Combate completo | Armas gastam munição real e respeitam recarga, cadência, precisão e LOS | Pendente de teste Steam |
| Explosões | Granada nativa causa dano verificado aos NPCs na camada Agent | Pendente de teste Steam |
| Portas e janelas | Entrada/saída por portal real sem atravessar paredes | Portas nativas + fallback incremental + salto físico baixo implementados; escadas, vidros e janelas exigem validação no Steam |
| Zumbis contra NPCs | Zumbi percebe, persegue e causa dano no NPC | Adaptador local experimental integrado; teste Steam pendente |
| Hordas/regiões | População vanilla respeitada e transição regional | Descoberta delimitada de integrantes em regiões carregadas vizinhas; A* original recebe posição. Perseguição efetiva inter-regional NÃO comprovada/garantida |
| Loot do chão | Transferência atômica sem duplicação/perda após reinício | Busca por loot observado até 19m, reserva por instância, navegação A* e coleta local com checagem de propriedade; rollback pós-callback melhorado. **Sem validação real no Steam** e bloqueado em multiplayer |
| Comércio/bases/A-Life | Ciclo de interação, tarefas, materiais e mundo persistido | Depósito central **nativo**, quatro defesas, entrega e retirada de itens reais, recuperação de slots destruídos, sobrevivência/loot, grupos extintos viram ruínas e melee contra barricada rival codificados. Logística coordenada, raids completas e fortalezas, não |
| Multiplayer | Host autoritativo, replicação, entrada/saída, reconexão | Não integrado; runtime local suspende NPCs se entrar outro jogador |

## Componentes e prioridades que ainda impedem o teste único final

1. **Compatibilidade real:** compilar toda `ClientModule` com a versão instalada do Unturned Steam e resolver eventuais APIs Unity/Assembly-CSharp divergentes. O Windows CI testa políticas C# isoladas, Python e superfícies textuais de SDK, não o módulo real.
2. **Cérebro completo:** integrar ainda estados restantes de `MobileNPCBrainDriver` e operadores avançados (conversa sobre pistas/classificação fina de sons, comportamento urbano/indoor, ordem de patrulha, papéis de esquadrão, comando de incursões em várias fases, A-Life econômico). Busca com piso/altura validados, aproximação protegida/emboscada breve, reforços por rádio, orçamento de buscas, retirada de aliados próximos e flanco já possuem adaptadores; **não** é uma porta 1:1 completa.
3. **A-Life e bases:** validar agora coleta por loot real, concorrência, reinício, rollback, avanço físico, navegação bloqueada, cinco peças de base e o depósito **nativo experimental** com entrega/retirada/consumo e morte de grupo, especialmente sem duplicação/perda em callbacks do original. Complementar crafting logístico, guardas, papéis de esquadrão, usurpação/ocupação, explosivos de cerco e assaltos coordenados. Raid ainda é aproximação e possível melee da barricada, não incursão completa.
4. **Navegação avançada:** comprovar conexão do ASPFP original e do grafo alternativo com portas, janelas quebradas, baixa travessia física, escadas, andares e obstáculos móveis em mapas reais sem clip ou teletransporte.
5. **Zumbis:** validar dano mútuo, perseguição em regiões/níveis e o comportamento de investigação/hordas ao perder o alvo; o rally atual reutiliza zumbis já existentes, não altera o limite vanilla nem garante travessia de regiões.
6. **Multiplayer integrado:** host Steam, sincronização autoritativa de NPCs/danos/itens/radio e entrada/reconexão de convidados. A Linha 2 permanece intencionalmente singleplayer local enquanto isso não existir.
7. **Auto 32 e distribuição:** preservar IDs, carregadores 24/12, rajada automática, material de modpack/Workshop e assinatura/instalação, testando possíveis conflitos de mods no cliente Steam.

### Evidência de testes desta rodada
- `BrainTests` do código original portado: sucesso na rota incremental por qualquer lado da parede, recusa de caminhos desconectados e detecção `no-connected-route`.
- Monitor tático determinístico: progresso real, falha após ausência de movimento, tempo máximo do A* pendente, impossibilidade de chegar em outro andar e retenção temporária do ponto de fuga/flanco.
- Testes adicionais para A-Life: reserva exclusiva por `instanceID`, expiração e isolamento por sessão, condições de partilha de recursos sem parede/combate, e monitor de exploração com A* pendente, rota desconectada, estagnação, objetivo novo e andares incompatíveis.
- Manifests de base em teste isolado: quinta peça de depósito, ID nativo e retorno JSON, separação entre caixa central e barricadas, remoção da peça confirmada destruída, reposição do slot, rejeição de duplicatas e descarte de grupo extinto. Autorizações de depósito/saque testam LOS, grupo, item real, espaço e necessidade. Falhas de callback do Unturned ainda precisam ser provocadas no gameplay. 
- Reagrupamento: política isolada rejeita líder sem visão, perigo ativo, aliado recrutado, diferença vertical, distâncias inválidas e NaN.
- Busca/emboscada: testes isolados rejeitam memória insuficiente, local não observado, pista vencida, janela em outro andar, coordenadas inválidas e tiro de surpresa em alvo não confirmado.
- Rádio: testes exigem setor genuíno, waypoint de aproximação de no máximo 12m e rejeitam comunicação com risco em combate ou incompatibilidade de andar.
- EncounterDirector (parcial): testes limitam as pistas extras a dois NPCs por grupo, liberam licenças vencidas e garantem isolamento entre grupos/mapas.
- Reação de grupo: testes rejeitam retirada quando a morte não foi vista/ouvida, não foi violenta, está em outro andar, veio de outro grupo ou ocorreu longe demais.
- Persistência isolada: ida e volta de inventário, grupos, ferimentos, identidades, tickets de reposição e manifesto de barricadas de cada base, com rejeição de registros corrompidos.
- Ledger de grupos isolado: sem duplicar posição de barricadas, máximo de 4 por grupo, separação entre bases rivais, cópias independentes e não reaproveitamento de IDs de bases extintas (com ligação de runtime).
- A auditoria `check_sdk_api_surface.py` inclui os símbolos originais de granadas, objetos de mapa, portas, transação de itens (`ItemManager.regions`, `ItemData`, `ReceiveDestroyItem`), construção (`BarricadeManager.dropBarricade`) e reconhecimento de barricadas (`FindBarricadeByRootTransform`).
- **Nenhuma dessas evidências substitui uma build real ou demonstra o gameplay completo.**

**Regra de liberação:** só anunciar uma build integrada de gameplay após passar os testes automatizados, compilar contra assemblies Steam reais e observar os cenários acima sem regressões. O adaptador singleplayer de coleta deve ser exercitado com drops reais, telemetria da remoção, reinício do mapa e múltiplos NPCs disputando o mesmo `instanceID` antes de ser considerado seguro. Nunca habilitar o código local de NPC/loot enquanto houver clientes multiplayer não replicados.

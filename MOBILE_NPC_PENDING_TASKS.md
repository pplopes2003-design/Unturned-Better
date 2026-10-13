# UNTURNED BETTER — REGISTRO PERMANENTE DE PENDÊNCIAS

**Fonte canônica do backlog:** este arquivo, na branch `feature/npc-ai-foundation`. O guia `MOBILE_NPC_PROJECT_HANDOFF.md` mantém histórico de decisões, commits e continuidade. O plano `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` reúne testes. O design do rádio permanece em `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`.

**Regra obrigatória para toda conversa e rodada de implementação:** ler este arquivo e o HANDOFF **antes** de alterar código; depois de cada rodada, atualizar ambos (e documentos técnicos/testes afetados) no mesmo branch. Preservar IDs, resultados e histórico. **NUNCA excluir uma pendência apenas por ter código implementado ou commit criado.** Quando o escopo de fonte de um SUBITEM estiver totalmente conectado, marcar `QUASE PRONTA — AGUARDANDO BUILD`, mesmo sem compilação; após feedback do usuário, fechar ou reabrir. Somente mover para `CONCLUÍDA (runtime verificado)` após revisão, compilação e teste funcional no Unturned; manter o item arquivado na seção de concluídos com links de commits e evidência (build/mapa/caso), sem apagar. Se um teste regredir, reabrir preservando histórico.

**Estados válidos:** `NÃO INVESTIGADA`, `INVESTIGADA`, `EM IMPLEMENTAÇÃO`, `IMPLEMENTADA / NÃO COMPILADA`, `COMPILADA / NÃO TESTADA`, `EM VALIDAÇÃO`, `QUASE PRONTA — AGUARDANDO BUILD`, `CONCLUÍDA (runtime verificado)`, `BLOQUEADA`. Uma etapa pode avançar sem compilar; isso **não** significa que funciona. Registre qualquer regressão, branch, caminho e commit. Não atribuir capacidades ainda não existentes aos NPCs.

**Processo do usuário:** o usuário fará compilação e testes de Dev Build **por último**, depois da etapa de desenvolvimento. Não interromper cada rodada para solicitar build; acumular verificações objetivas. Usar GitHub Desktop (Fetch origin / Pull origin). Trabalhar no branch de feature, não confundir com `main`.

## Pendências ativas — não apagar

| ID | P | Módulo / requisito | Estado em 2026-10-13 | Evidência para encerrar |
| --- | --- | --- | --- | --- |
| CORE-01 | P0 | Unity compile, Dev Build, logs, smoke tests e regressões cross-map | NÃO INVESTIGADA | Compilação limpa, Player.log sem erros relevantes, testes manuais reproduzíveis |
| CORE-02 | P0 | Dano do player e NPC a NPC (tiro, melee, hitbox, cabeça/corpo), sem dupla aplicação e com atribuição | IMPLEMENTADA / NÃO COMPILADA (parcial) | HP cai por impacto; dano/cabeça/bleed corretos; atribuição e ataques a mortos corretos |
| CORE-03 | P0 | Morte/ragdoll físico, animações de impacto e morte, XP/loot, cadáver, limpeza e reposição tardia | IMPLEMENTADA / NÃO COMPILADA (parcial) | Animação/física, loot/XP apropriado, sem IA após morte, sem duplicação, respawn com atraso |
| CORE-04 | P0 | Navegação nativa/A*: edifícios, portas, quinas, escadas, meio-fio, obstáculos móveis, água, saltos seguros, cover path e stuck | EM IMPLEMENTAÇÃO | Testes multi-mapa sem andar contra parede/atravessar objetos/ficar preso ao player |
| ZMB-01 | P0 | Zumbis perseguem fora da POI e atravessam regiões; memória de alvo/som, leash, otimização e rede | EM IMPLEMENTAÇÃO (tick ativo além da região + perseguição física em chão aberto; A* completo e rede pendentes) | Perseguição sustentada cruzando regiões sem teleporte, exploração indevida ou custo descontrolado |
| ZMB-02 | P1 | Zumbis: investigação de tiros/ruídos, prioridade player visível e alvo já adquirido, não girar parado | EM IMPLEMENTAÇÃO | Busca real por posições sem atrasos/reação a ruído inexistente |
| AI-01 | P1 | Árvore de decisão/objetivos autônomos plausíveis, investigação, patrulha, comportamento fora das POIs | EM IMPLEMENTAÇÃO | NPC age com contexto e prioridades e não congela |
| AI-02 | P1 | Combate humanizado: armas reais, precisão imperfeita, munição, recarga, alcance, reaction time, fogo amigo | EM IMPLEMENTAÇÃO | Teste a curta/longa distância, falhas realistas e projéteis rastreáveis |
| AI-03 | P1 | Resposta tática: buscar cobertura real, recuar de forma inteligente, suprimir e flanquear em esquadrão | EM IMPLEMENTAÇÃO | Movimentos viáveis e supressão com munição real, linha de tiro e contra-jogo |
| AI-04 | P2 | Furtividade, stalking, emboscada, visão/som, ópticas, busca de último indício sem visão de raio X | EM IMPLEMENTAÇÃO | Testes cegos com obstáculos e perda de alvo |
| AI-05 | P2 | Frag e smoke reais, trajetória, risco a aliados, oclusão e reação tática | NÃO INVESTIGADA | Consumo de item, efeito físico e tático, nenhuma granada mágica |
| AI-06 | P2 | Socorro/triagem de aliados, tratamento com consumíveis, segurança e animações | EM IMPLEMENTAÇÃO | Cura sem recursos infinitos, escolha e deslocamento seguros |
| AI-07 | P1 | Loot prioritário e transacional, objetivo expira/falha sem deadlock, itens reais/inventário | IMPLEMENTADA / NÃO COMPILADA (parcial) | Após pegar item prossegue; falha e mesa/curb sem travamento; sem dupe |
| GRP-01 | P1 | Grupos de até 4: líder, mudança de papel/facção, alianças, sem friendly fire nem inteligência global | EM IMPLEMENTAÇÃO | Matriz de relações, reconciliação, morte do líder, recrutar e combater facções rivais |
| GRP-02 | P1 | Grupos sobrevivem à morte de um membro; dissolvem quando todos morrem; respawn não herda memória individual | EM IMPLEMENTAÇÃO | ID/âncora corretos, não recria grupo morto e reposições respeitam intervalo |
| GRP-03 | P1 | Construção de bases padronizadas, recursos, armazenamento, território e persistência | EM IMPLEMENTAÇÃO | Estruturas válidas/energia no mapa e estado restaurado |
| SAVE-01 | P1 | Persistência identidade individual, HP, itens, ferimentos, facção/grupo, conhecimento e migração | EM IMPLEMENTAÇÃO (snapshots memória/fontes v5, leitura de v1–v4; identidade/ferimentos pendentes) | Save/reload com continuidade da mesma pessoa, nenhum dado herdado por recém-criado |
| COM-01 | P1 | Voz local realista, audível com distância/parede/agachamento e observação, grupo/facção | IMPLEMENTADA / NÃO COMPILADA (parcial) | Aliados recebem somente falas ao alcance; outros não ganham dados mágicos |
| COM-02 | P1 | Rádio nativo portátil 1445, escuta 1446, canal/equipamento, mensagens, player intercepta com legenda | EM IMPLEMENTAÇÃO (outbox apenas) | Frequência compatível + aparelho real, sem interceptação sem receptor |
| COM-03 | P2 | Rádio fixo de base com infraestrutura elétrica comprovada e eventual rádio veicular | NÃO INVESTIGADA | Base construída e energizada, transmissão interrompe sem energia |
| COM-04 | P2 | Chamadas por reforço/loot/airdrop/base rival com TTL, incerteza e autorização | NÃO INVESTIGADA | Destino e recursos verificados, canal e alcance corretos, sem telepatia |
| VEH-01 | P2 | NPCs usam veículos, combustível, direção, colisão, posse e rádio veicular | NÃO INVESTIGADA | Navegar corretamente e respeitar recursos/regras nativas |
| UI-01 | P1 | Nome visível antes da interação, diálogo/opções numéricas, foco, mouse, HUD/ESC restauração | EM IMPLEMENTAÇÃO | Abrir/fechar sem HUD sumir e opções renderizadas |
| UI-02 | P1 | Animações/som humanoides reais de punch, tiro, recarga, hit, correr, ajoelhar, ragdoll | EM IMPLEMENTAÇÃO | Conferir clips/rig reais da Unity; reproduções sincronizadas |
| PERF-01 | P2 | Limite 1–40, respawn, escalonamento, streaming regiões, perf/GC e logs limitados | EM IMPLEMENTAÇÃO | FPS/GC e logs com 20 NPCs + zumbis em dois mapas |
| NET-01 | P3 | Replicação multiplayer de NPC/loot/animações/combate/rádio e atribuição nativa | NÃO INVESTIGADA | Cliente/servidor com estados consistentes |
| LEARN-01 | P1 | Memória longa **por indivíduo**: resultados de combate contra jogador/zumbi, táticas, caminhos e perigos | EM IMPLEMENTAÇÃO (loot e disparos; táticas/rotas pendentes) | Aprendizado observável e limitado, sem acesso a dados invisíveis |
| LEARN-02 | P1 | Aprendizado online (contextual bandit/recompensa incremental, ou ML viável), exploração, esquecimento/validade | EM IMPLEMENTAÇÃO (feedback incremental e decisões limitadas; aprendizado tático pendente) | Evidência sucesso/fracasso altera escolha de tática em contexto comparável; sem exploit |
| LEARN-03 | P1 | Transmissão de ensinamentos **somente por voz local ou rádio físico**; confiabilidade, rumor, consentimento | EM IMPLEMENTAÇÃO (fala local simulada; rádio/áudio/UI pendentes) | Membro ausente não sabe; com contato pode aprender, com erro/incerteza; sem facção-telepatia |
| LEARN-04 | P1 | Morte apaga conhecimento privado não compartilhado; sobreviventes guardam apenas o que ouviram; persistência individual | IMPLEMENTADA / NÃO COMPILADA (parcial; identidade e fila de conversas pendentes) | Morte antes de contar apaga; depois de contar sobrevivente lembra; substituto nasce novo |
| LEARN-05 | P2 | Aprender rotas/obstáculos, cobertura válida, comportamento contra diferentes tipos de zumbi/jogador | EM IMPLEMENTAÇÃO (acesso à cobertura; aprendizado de rotas completo pendente) | Cache por mapa/local e validação de mudanças; bloqueios não viram caminho perfeito |
| LEARN-06 | P1 | **Memória de fontes de munição/carregadores compatíveis**: aprender local de coleta real, revisitar quando necessitar, diminuir confiança após visita vazia e passar a aliados por comunicação física | QUASE PRONTA — AGUARDANDO BUILD (subcaso de memória genérica v5; sem categorias POI) | Com arma própria e munição baixa: ir a local previamente aprendido, sem spawn artificial; compatibilidade de calibres verdadeira, conversa local, save/load, perda após morte |
| LEARN-07 | P2 | Generalizar quais **categorias de lugares** costumam ter quais recursos (delegacias, lojas de armas, hospital, fazenda, oficinas), aprendizagem arma/calibre/carregador e rotas de aquisição | NÃO INVESTIGADA (dependente da classificação nativa de POI/loots) | Aprender a partir de visitas confirmadas, não enxergar itens invisíveis nem assumir conteúdo garantido, item compatível e probabilidade baseada em resultados |
| LEARN-08 | P1 | Memória de abastecimento **generalizada para cada item realmente coletado**, baseada em necessidade atual (cura, alimentos, água, equipamentos, armas, componentes de base e munição), persistência e conversa local | QUASE PRONTA — AGUARDANDO BUILD (subcaso v1 dos tipos contemplados, ainda sem todos os itens/objetivos) | Cada item vem do ItemManager real; o NPC revisita somente se precisa, valida terreno/caminho, reduz confiança se vazio, não materializa recursos |
| CRAFT-01 | P1 | Tratamento improvisado: criar rag/bandagem de roupas/insumos **somente pela receita nativa**, consumo de materiais, prioridade de sangramento e sem duplicação | EM IMPLEMENTAÇÃO (receita direta ou cadeia simples de 2 etapas, aguardando comprovar assets) | Rasgar apenas roupas previstas no blueprint, gastar insumos corretamente, produzir bandagem utilizável, não gastar curativo com cura genérica durante sangramento |
| DOC-01 | P0 | **Atualizar sempre** este registro, HANDOFF, designs/testes e links de commits em cada rodada | EM VALIDAÇÃO (processo contínuo) | Histórico nunca perde itens; cada etapa registrou seu status |
| DOC-02 | P2 | Diagrama completo da IA/árvore de decisão e fluxo de memória/comunicação ao final de todos os módulos | NÃO INVESTIGADA | Diagrama fiel ao que existe, distinguindo implementado/testado/proposto |

## Regras de memória/experiência (REQUISITOS)

1. **Identidade individual única.** Memória e experiências não são estado global da facção ou do grupo. O NPC recém-spawnado não conhece automaticamente o anterior; carregar o MESMO sobrevivente de um save restaura apenas suas memórias.
2. **Observação e feedback reais.** Aprender com seus próprios tiros acertados/errados, dano sofrido, fuga/escudo útil, rotas bem-sucedidas/bloqueadas, encontros com zumbis comuns/especiais e jogadores. Não inferir sucesso porque uma ação foi apenas iniciada.
3. **Aprender estratégias, não onisciência.** Usar táticas de combate com escolha probabilística/limites de risco; repetir ações comprovadas úteis, diminuir preferência por falhas, testar alternativas ocasionalmente. Não gerar tiros perfeitos nem prever posições invisíveis.
4. **Compartilhamento físico.** Passar ensinamentos só por interação de voz realmente audível ou rádio válido/tunado/energizado; mesma facção ou grupo não é canal mágico de dados. O interlocutor guarda cópia com fonte, confiança e limites; fonte pode morrer sem apagar cópias já aprendidas.
5. **Morte real.** Apagar conhecimento particular não compartilhado, impedir sucessores de herdar cérebro/política/inventário do morto. O grupo manterá apenas lembranças dos sobreviventes; base futura pode conter arquivo físico se explicitamente implementado.
6. **Economia/performance.** Limitar episódios por NPC/contexto/mapa, recência, número de parâmetros aprendidos, frequência de decisões e uso de CPU/GC; resultados negativos e observações desatualizadas decaem.
7. **Sem ML pesado fictício.** Primeiro implementar aprendizagem incremental contextual verdadeira, com estado, atualização por recompensa/resultado, generalização limitada, persistência e decisões efetivamente influenciadas. Redes neurais só se houver benefício, dados, budget e teste; não declarar `machine learning` operacional se só existirem flags/contadores.
8. **Cuidados de segurança tática.** Qualquer sugestão aprendida ainda precisa de validação física de path/visão/alcance/munição/facção, nunca sobrescrevendo regras do motor, permissão de tiro ou sobrevivência.

## Protocolo de cada rodada

1. Ler `MOBILE_NPC_PROJECT_HANDOFF.md` e este backlog; verificar branch/arquivos/commits atuais, distinguir `main`.
2. Escolher 1–3 subconjuntos com mudança funcional/coerente; inspecionar APIs existentes e integrar, nunca apenas declarar intenção.
3. Fazer commits no branch `feature/npc-ai-foundation`, com caminho e descrição; atualizar o backlog com status **sem remover linha**.
4. Atualizar o HANDOFF com commits, limites, achados, dependências e próximo ponto de retomada, e atualizar designs/testes relacionados.
5. Comentar claramente **estática / commit / compilada / runtime verificada**. Usuário fará Unity Dev Build ao final do desenvolvimento, não exigir em cada rodada.
6. No encerramento definitivo, validar plano de testes e entregar o diagrama completo dos módulos reais. Mesmo depois de concluídos, manter IDs e evidências neste arquivo.

## Itens concluídos e verificados no jogo

*Nenhum foi formalmente encerrado por este critério até 2026-10-13.* Componentes parciais ou commits existentes permanecem acima.

## Histórico de atualização — 2026-10-13, rodada de memória/aprendizado

**LEARN-01 a LEARN-04 continuam ATIVAS, não encerradas.** Implementada em fonte uma camada inicial de aprendizagem online individual a partir de resultados reais de saque e de disparos. A ação posterior de seleção de loot ou o intervalo entre disparos consulta experiência do próprio indivíduo. Até 48 registros com contribuição de conhecimento ouvido mais fraca do que aprendizado direto; transmissão simulada só em conversa local entre membros vivos do mesmo grupo próximos o bastante para ouvir, sujeita a paredes; sem rádio remoto. Morte apaga memória privada. Snapshot individual `MobileNPCPersistence` migrou de versão 2 para 3, aceitando versões anteriores.

Arquivos: `MobileNPCAdaptiveMemory.cs`, `MobileNPCPopulationBootstrap.cs`, `MobileNPCHumanoidVitals.cs`, `MobileNPCLooting.cs`, `MobileNPCHumanoidRangedCombat.cs`, `MobileNPCGroupMember.cs`, `MobileNPCPersistence.cs`.

Commits de implementação: `1a41e003`, `564ebfc1`, `a94ca7ad`, `f6b4583f`, `a657faad`, `a576d920`, `f2eac633`, `18297285`. Documento técnico: `MOBILE_NPC_ADAPTIVE_LEARNING_DESIGN.md` commit `c1efbe55`.

**Critério de verdade:** código revisado estruturalmente via GitHub, mas NÃO compilado, NÃO testado em Dev Build e NÃO runtime-verificado. Aprender estratégias de cobertura/emboscada, planos contra tipos de zumbis, caminhos bem-sucedidos, ML sofisticado, voz audível, fila/persistência de lições não contadas, identidade individual persistente e compartilhamento via rádio nativo **seguem pendentes**. O usuário fará compilação/testes ao final da implementação. Atualizar ambos este guia e HANDOFF em toda rodada.


### LEARN-05 update — 2026-10-13

`LEARN-05` remains **active** and is now in `EM IMPLEMENTAÇÃO (cover access; general navigation incomplete)`. Commits `1f098573` and `caf31416`: `MobileNPCBrainDriver` rewards only actual reported arrival at a cover goal, penalizes only expiry of that destination, and modestly deprioritizes subsequent already-validated cover waypoints in sectors with prior failures. This is **not** a complete route-learning/navigation system. Unity compile/runtime verification deferred as requested. Do not remove the item.


## Histórico — 2026-10-13, rodada P0 de dano, recuperação de navegação e ε-greedy

**Nenhum ID encerrado.** O usuário fará a compilação e os testes integrados ao fim do desenvolvimento.

- **CORE-02:** `b4ab966c39a43c20c7c2f51f2c15d49556aca45d` (`UseableGun.cs`) e `6d673383ae00055c84a25cc49cc4f4e12fe530e4` (`UseableMelee.cs`) rejeitam dano zero/negativo/não finito na ramificação de NPC móvel em vez de convertê-lo em um mínimo artificial de 1 HP. Armas com dano positivo preservam seu cálculo e origem. Isso não valida hitbox, tipo de raio, hits corporais ou morte.
- **CORE-04 / AI-03:** `96eab81bc9c0bf6aa870ec99fba80cea2d4e7aea` (`MobileNPCHumanoidMotor.cs`) define `IsMoving` por deslocamento físico planar em vez de apenas `CollisionFlags.Sides`, e melhora a detecção de stuck por progresso em direção ao objetivo ou desvio substancial coerente. Pequenas oscilações junto a paredes não devem redefinir a tentativa como sucesso. Faltam testes de quina, meio-fio, rota dinâmica e terreno.
- **LEARN-02 / AI-03:** `e0d5d944c2c11b684c805e2814b8070cf0f9b3cf` (`MobileNPCBrainDriver.cs`) aplica **ε-greedy (ε = 0,12)** à escolha entre até 10 candidatas de cobertura com solo, obstrução do atirador e corredor local já verificados. O NPC geralmente escolhe a melhor nota ajustada por experiência pessoal (`CoverAccess`) e distância, mas ocasionalmente explora outra candidata admissível. Amostragem reservatório evita listas adicionais. **Não** equivale a rede neural, A* completo, aprendizado de sobrevivência sob fogo ou garantia de caminho até a cobertura.

**Estado de verificação:** commits remotos e inspeção de código; **não compilado**, **não executado**, **não testado no jogo**. Permanecem todas as demais pendências, principalmente `ZMB-01` perseguição dos zumbis entre regiões/POIs, rádio físico e interceptação `COM-02`, aprendizado de táticas contra tipos de inimigo `LEARN-01/05`, diálogo e animações.

**Decisão de algoritmo:** sem biblioteca externa obrigatória. Usar inicialmente política ε-greedy/bandido contextual pequeno em C# com recompensas de consequências observadas; técnicas alternativas (UCB/Thompson, Q-learning ou rede neural) só mediante avaliação de desempenho/benefício, dados e integração com escolhas reais. Regras físicas e de facção continuam determinísticas.


## Histórico — 2026-10-13, rodada seguinte: morte idempotente e reposição compatível por facção/grupo

**Sem baixa de pendências.** A compilação e os testes no Player Dev Build continuam reservados para a fase final do desenvolvimento, por escolha do usuário.

- **CORE-02 / CORE-03:** commit `270072bc821d3643897816322b0f409eb774f1b6` — `MobileNPCHumanoidVitals.cs` passa a reservar a morte imediatamente quando o HP chega a zero, antes de publicar `DamageReceived` e `HealthChanged`. Tentativas reentrantes de dano ou cura durante esses callbacks não devem alterar o golpe fatal nem substituir o autor; `DieServer` continua a única rotina autoritativa de transição. Sem alteração dos APIs nativos `PlayerLife`/`Zombie`; hitbox, ragdoll, kill credit e drop ainda pendentes de validação.
- **GRP-02 / CORE-03:** commit `466829ece0b4b129ca3baddf470ef45845b5b38a` — `MobileNPCGroupMember.TryGetSurvivingGroupIdentityServer` retorna função e facção **somente** de grupo com 1–3 membros vivos, mantendo 4 como máximo. Grupo extinto, inexistente ou lotado não pode ser readmitido por este mecanismo.
- **GRP-02 / GRP-01:** commits `946d2b62cc2a8fd54d9f5d0f757b01741827075f` e `cd01dac2cdf5157dd83063333387ef9831f32d55` — `MobileNPCPopulationDirector` verifica um evento de reposição cujo prazo venceu **antes** do spawn. Caso o grupo anterior ainda tenha vaga, cria um indivíduo novo com papel e facção compatíveis e pede `TryJoinExistingGroupServer`; caso tenha desaparecido, cria indivíduo independente com papel normal sorteado. Consome a reserva somente após geração válida; mantém delay/cadáver separados e não transfere inventário, memória ou identidade do morto. Corrigida atribuição inicial de variáveis do grupo.
- **Risco ainda aberto:** a criação da instância e o cadastro no grupo são ações sequenciais; morte de último sobrevivente ou mudança de facção durante o spawn deve resultar em falha segura de reentrada. Testar limite quatro, facções customizadas, múltiplas mortes, grupo dissolvido, reposição que falha ao procurar terreno, mudança de mapa/salvamento.

**Status:** fontes e commits confirmados; não compilado, não testado em jogo. CORE-02/03, GRP-01/02 e todos os outros IDs acima permanecem abertos.


## Histórico — 2026-10-13, abastecimento aprendido e perseguição real de zumbis

**Revisão de status solicitada pelo usuário:** o rótulo `QUASE PRONTA — AGUARDANDO BUILD` identifica submódulos com fluxo de código conectado cujos testes de compilação e comportamento estão intencionalmente reservados para o FINAL. Depois de ouvir o feedback da Dev Build, converter para `CONCLUÍDA (runtime verificado)` ou `EM IMPLEMENTAÇÃO` com bugs reproduzidos. **Não converter automaticamente por commit**, e não remover IDs nem histórico. A compilação final em si continua CORE-01.

- **LEARN-06** está `QUASE PRONTA — AGUARDANDO BUILD` **somente no escopo v1 por localização específica**. `3581bc40` cria `MobileNPCSupplyKnowledge`: até 32 sítios individuais com itemID de magazine real, posição física, evidência de coletas e visitas fracassadas; compatibilidade verificada por `ItemGunAsset.magazineCalibers` e `ItemMagazineAsset.CalibersContainAnyOfIds`, não por palpite ou nome do item. `f17d4077` instala no prefab; `316e4075` apaga na morte. `a1932901` grava/carrega no snapshot individual **v4**, mantendo leitura v1/v2/v3. `292a8cf7` / `8d19dddd` permitem objetivo limitado de busca; `b176e9f1` conecta ao loot (incluindo seleção prioritária de carregadores compatíveis), visita física e diminuição de confiança se não encontrou; `e2b6f099` compartilha apenas via fala local validada entre membros do grupo. `a542afc5` / `d5101162` só registram como fonte de munição revistas recolhidas **com projéteis**, não revistas vazias. `2bf1b3fd` evita interromper a investigação pré-existente; `73fbe1ce` cancela destino de busca sob combate ou inventário cheio. `07970a63` repara o ID real logado após coleta fracassada. O conhecimento de quais carregadores são fisicamente compatíveis com uma arma vem do **asset nativo**, e não foi treinado como classificação; o que aprende por experiências é ONDE encontrou o item e a confiabilidade do lugar.
- **LEARN-07** criado e segue `NÃO INVESTIGADA`: distinguir aprender o prédio concreto ("achei carregador nesta delegacia") de generalizar a categoria ("delegacias têm maior probabilidade de munição"), além de rotas e outros tipos de recursos; usar somente metadados reais de POI e sucessos/falhas observados.
- **ZMB-01** `66caf131d595011b582571252376b0a78bc49a4e`: em `Assets/Runtime/Assembly-CSharp/Unturned/Zombies/Zombie.cs`, removido `player.movement.nav == 255` como desistência automática do alvo **já adquirido**; regra da natação inacessível foi mantida. Foram encontrados e inspecionados também `Managers/ZombieManager.cs` e `Managers/ZombieRegion.cs`. `Zombie.tick` / `setTicking` já usam lista de zumbis ativos independente da região de origem. **Não quer dizer que o A* continue tendo malha navegável fora da POI**; não atribuir garantia de perseguição cross-map, desempenho ou rede. A pendência principal permanece aberta.

**QA:** commits verificados por leitura GitHub; Unity, Player.log, testes multi-map, navegação, voz, física e UI NÃO executados. Os demais IDs da tabela permanecem ativos.


### Fechamento da rodada — 2026-10-13

Revisão estática adicional: conferidos `MobileNPCLooting`, `MobileNPCSupplyKnowledge`, `MobileNPCBrainDriver`, `MobileNPCTactics`, `MobileNPCPersistence`, `MobileNPCGroupMember` e `Zombies/Zombie.cs` via commits GitHub. Chaves sintáticas balanceadas e referências aos novos componentes presentes; **isso não substitui compilação C#**. Última correção editorial de fonte: `a6e73c82c0300f017fa1226cf4c7b637f8925dc6` (limpeza de metadados do item-alvo após a transferência). LEARN-06 fica em `QUASE PRONTA — AGUARDANDO BUILD` apenas para o recorte de memória de local de carregadores, enquanto LEARN-07 e ZMB-01 **continuam ativos**. Não fechar ou descartar nenhum item antes do feedback da Dev Build.


## Histórico — 2026-10-13 — GENERALIZAÇÃO DO APRENDIZADO DE ABASTECIMENTO E CURA IMPROVISADA

**Mudança de requisito do usuário:** o caso da Cobra era somente ilustrativo. `LEARN-08` registra o requisito geral: todo item que um NPC utiliza e julga necessário deve poder ter suas fontes reais lembradas; necessidades do contexto determinam quando vale voltar, não uma lista global de loot revelado. `LEARN-06` permanece como caso específico de carregadores nativos; `LEARN-07` continua aberto para generalizar *tipos de lugares*, não simplesmente as coordenadas já visitadas.

- `e2c59e040bb45a06827a9c314eaa029c783ac20e`: `MobileNPCSupplyKnowledge.cs` agora observa **qualquer ItemAsset real** com quantidade útil após coleta autenticada, até 32 locais/indivíduo, guarda ID/posição/indícios de sucesso e falha, utiliza categoria nativa e necessidades do ator para recomendar medicamentos, curativos, comida, água, carregadores compatíveis, armas e equipamento básico; apenas recomenda fontes em que algo foi encontrado. `SiteRecord.magazineID` é mantido como campo legado de v4, com `itemID` como campo principal v5; lição falada passa ID de item genérico.
- `8c983a377c447985ad31c9382f872501f60f3bd8`: `MobileNPCLooting.cs` usa a demanda real para planejar excursão a um local conhecido, permitindo executar saque **após alcançar** a posição (antes o objetivo Investigate podia impedir `TryStartLoot`); altera falha por visita vazia, reaproveita comunicação local, respeita metas de combate.
- `ff2cb872be6e1ebc6f8128a6f071dbb73f1c6417`: inventário tenta produzir curativos usando `Blueprint` nativo real, ferramentas/requisitos ausentes excluídos, insumos realmente consumidos. Procura receita direta e, conservadoramente, cadeia de duas receitas vestuário→insumo→curativo quando ambas estão registradas. **Não afirmar que qualquer camisa pode ser transformada em bandagem** nem que os blueprints disponíveis na Dev Build serão compatíveis.
- `ff07f4ace26c6fd1c42a02e6a3aeff0d0fcbf308` integra tentativa à decisão médica com sangramento; `9717fba040380ee531f690e07fc2d674dd09f607` reserva os curativos específicos para parar sangramento em vez de consumi-los como cura genérica.
- `8ef9fd5159202abb6a36651b47f499b09479ba29` limita busca de receitas por NPC a cada 18s; `090cee160b508c49922c0d85b2ec0f9bcb5a62b2` verifica slots de inventário reservados na transação.
- `77e60007bf3abff86cbe0340ae1845f82a0935a2` diferencia necessidades de rag/bandage/dressing para sangramento, splint para fratura e remédios comuns para HP, sem tratar medicações quaisquer como bandagem. `b844018221e20177700eb1935401205f6ac81af4` limita deslocamentos emergenciais sob sangramento ativo a 75m, oferece roupa lembrada como **potencial insumo**, nunca como cura gratuita.
- `e5f3b2f920d40c261aff796e72e9e63710cee517` migra snapshots individuais para **v5**, mantendo leitura 1–4. Memórias que não foram comunicadas desaparecem com o NPC na morte, sem transmissão remota mágica (continua a rotina `MobileNPCGroupMember`).

**Estado de conclusão:** `LEARN-08` quase pronto no **recorte funcional dos tipos de item com necessidades implementadas**; crafting improvisado `CRAFT-01` ainda em implementação porque receita real do mapa e cadeia de inventário requerem teste e otimização. `LEARN-07` e `ZMB-01` seguem PENDENTES. Nada foi compilado no Unity. O aprendizado por categoria hospital/delegacia como classe, uso de veículos/combustível/peças/reparo, descarte inteligente de inventário cheio, fontes de produção e banco de receitas sob mudança de mapa precisam continuar em aberto. Não remover itens da tabela.


## Histórico — 2026-10-13 — P0 ZMB-01: continuidade de perseguição além das regiões + fallback físico limitado

**Commits desta rodada:**
- `067bc2f99da31175b80d27ef76e623def031ec8c` — `Assets/Runtime/Assembly-CSharp/Unturned/Managers/ZombieManager.cs`: `tickingZombies` já contém zumbis que perseguem além dos limites de região, mas `Zombie.OnUpdate()` historicamente era chamado só para zumbis cuja região de origem contava com jogador. Um perseguidor originário de região vazia passava a perder atualização de posição, rede e detecção de stuck apesar de executar `tick()`. Agora, durante os `tick()` já orçados, chama também `OnUpdate` apenas para perseguidores vivos de regiões originais vazias; os das regiões ocupadas continuam no loop regional existente para evitar dupla atualização. Limite do tick dedicado já era até 50 por frame; não varrer todas as regiões vazias.
- `c23e6addf9f5a144e6ec84f65a24d1686781114c` — `Assets/Runtime/Assembly-CSharp/Unturned/Zombies/Zombie.cs`: `TryStepPursuitBeyondNavigationOnOpenGround` oferece perseguição server-side do player ou NPC móvel **somente quando o alvo está fora da navegação** (LevelNavigation), perto (até 24 m), vivo, zona aberta sem linha bloqueada, chão sólido, declive menor ou igual 38°, diferença de altura curta e sem água. Usa `CharacterController.Move` preservando colisões e gravidade/contato; limita delta e respeita `seeker.CanMove`, estado de stun/leave e ataque nativo. Se falhar qualquer restrição, chama `seeker.Move` usual (não inventar trajetória). Não inclui teleporte nem atravessa paredes.

**Descoberta importante:** documentação oficial do U3 SDK afirma que o componente licenciado **A* Pathfinding Project não é redistribuído** no SDK (`https://docs.smartlydressedgames.com/en/latest/u3-sdk/faq.html`). Não interpretar código do SDK como garantia de implementação de A* completo nos mapas publicados. O fallback físico não substitui bake/navmesh, rotas em edifícios, escadas, obstáculos, transições longas, pontes, terrenos íngremes ou replicação em clientes fora da região de origem. Resolver esses cenários exige integração legal com pathfinder e/ou geometria de navegação validada para cada mapa.

**Critério de encerramento ainda não satisfeito:** regressões reais no Player Dev Build cruzando POIs em mapas distintos com nav=255, 1 e 20+ perseguidores, zombies atacando player e NPC, ambientes com parede, beira d'água, subida, queda e escadas; validar teleporte inexistente, rede, animações, desempenho e atualização de posição. O módulo **ZMB-01 permanece EM IMPLEMENTAÇÃO**, apenas os dois subfluxos de fonte ficaram quase prontos para a build. Não excluir nem fechar.


**Ajuste final de segurança ZMB-01:** commit `143a47fe970f132feb4d18ffbded823823f242f1` — a sondagem do próximo passo fora da malha agora também usa `Level.checkSafeIncludingClipVolumes(ground.point)`, para respeitar limites jogáveis e volumes de bloqueio mesmo em solo plano. Não pressupõe que todas as regiões do mapa sejam conectadas pelo A*; exigir testes com bordas de mapa/clip volumes.

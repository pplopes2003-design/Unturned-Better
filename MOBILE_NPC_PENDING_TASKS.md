# UNTURNED BETTER — REGISTRO PERMANENTE DE PENDÊNCIAS

**Fonte canônica do backlog:** este arquivo, na branch `feature/npc-ai-foundation`. O guia `MOBILE_NPC_PROJECT_HANDOFF.md` mantém histórico de decisões, commits e continuidade. O plano `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` reúne testes. O design do rádio permanece em `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`.

**Regra obrigatória para toda conversa e rodada de implementação:** ler este arquivo e o HANDOFF **antes** de alterar código; depois de cada rodada, atualizar ambos (e documentos técnicos/testes afetados) no mesmo branch. Preservar IDs, resultados e histórico. **NUNCA excluir uma pendência apenas por ter código implementado ou commit criado.** Somente mover para `CONCLUÍDA (runtime verificado)` após revisão, compilação e teste funcional no Unturned; manter o item arquivado na seção de concluídos com links de commits e evidência (build/mapa/caso), sem apagar. Se um teste regredir, reabrir preservando histórico.

**Estados válidos:** `NÃO INVESTIGADA`, `INVESTIGADA`, `EM IMPLEMENTAÇÃO`, `IMPLEMENTADA / NÃO COMPILADA`, `COMPILADA / NÃO TESTADA`, `EM VALIDAÇÃO`, `CONCLUÍDA (runtime verificado)`, `BLOQUEADA`. Uma etapa pode avançar sem compilar; isso **não** significa que funciona. Registre qualquer regressão, branch, caminho e commit. Não atribuir capacidades ainda não existentes aos NPCs.

**Processo do usuário:** o usuário fará compilação e testes de Dev Build **por último**, depois da etapa de desenvolvimento. Não interromper cada rodada para solicitar build; acumular verificações objetivas. Usar GitHub Desktop (Fetch origin / Pull origin). Trabalhar no branch de feature, não confundir com `main`.

## Pendências ativas — não apagar

| ID | P | Módulo / requisito | Estado em 2026-10-13 | Evidência para encerrar |
| --- | --- | --- | --- | --- |
| CORE-01 | P0 | Unity compile, Dev Build, logs, smoke tests e regressões cross-map | NÃO INVESTIGADA | Compilação limpa, Player.log sem erros relevantes, testes manuais reproduzíveis |
| CORE-02 | P0 | Dano do player e NPC a NPC (tiro, melee, hitbox, cabeça/corpo), sem dupla aplicação e com atribuição | IMPLEMENTADA / NÃO COMPILADA (parcial) | HP cai por impacto; dano/cabeça/bleed corretos; atribuição e ataques a mortos corretos |
| CORE-03 | P0 | Morte/ragdoll físico, animações de impacto e morte, XP/loot, cadáver, limpeza e reposição tardia | IMPLEMENTADA / NÃO COMPILADA (parcial) | Animação/física, loot/XP apropriado, sem IA após morte, sem duplicação, respawn com atraso |
| CORE-04 | P0 | Navegação nativa/A*: edifícios, portas, quinas, escadas, meio-fio, obstáculos móveis, água, saltos seguros, cover path e stuck | EM IMPLEMENTAÇÃO | Testes multi-mapa sem andar contra parede/atravessar objetos/ficar preso ao player |
| ZMB-01 | P0 | Zumbis perseguem fora da POI e atravessam regiões; memória de alvo/som, leash, otimização e rede | INVESTIGADA (design apenas) | Perseguição sustentada cruzando regiões sem teleporte, exploração indevida ou custo descontrolado |
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
| SAVE-01 | P1 | Persistência identidade individual, HP, itens, ferimentos, facção/grupo, conhecimento e migração | EM IMPLEMENTAÇÃO | Save/reload com continuidade da mesma pessoa, nenhum dado herdado por recém-criado |
| COM-01 | P1 | Voz local realista, audível com distância/parede/agachamento e observação, grupo/facção | IMPLEMENTADA / NÃO COMPILADA (parcial) | Aliados recebem somente falas ao alcance; outros não ganham dados mágicos |
| COM-02 | P1 | Rádio nativo portátil 1445, escuta 1446, canal/equipamento, mensagens, player intercepta com legenda | EM IMPLEMENTAÇÃO (outbox apenas) | Frequência compatível + aparelho real, sem interceptação sem receptor |
| COM-03 | P2 | Rádio fixo de base com infraestrutura elétrica comprovada e eventual rádio veicular | NÃO INVESTIGADA | Base construída e energizada, transmissão interrompe sem energia |
| COM-04 | P2 | Chamadas por reforço/loot/airdrop/base rival com TTL, incerteza e autorização | NÃO INVESTIGADA | Destino e recursos verificados, canal e alcance corretos, sem telepatia |
| VEH-01 | P2 | NPCs usam veículos, combustível, direção, colisão, posse e rádio veicular | NÃO INVESTIGADA | Navegar corretamente e respeitar recursos/regras nativas |
| UI-01 | P1 | Nome visível antes da interação, diálogo/opções numéricas, foco, mouse, HUD/ESC restauração | EM IMPLEMENTAÇÃO | Abrir/fechar sem HUD sumir e opções renderizadas |
| UI-02 | P1 | Animações/som humanoides reais de punch, tiro, recarga, hit, correr, ajoelhar, ragdoll | EM IMPLEMENTAÇÃO | Conferir clips/rig reais da Unity; reproduções sincronizadas |
| PERF-01 | P2 | Limite 1–40, respawn, escalonamento, streaming regiões, perf/GC e logs limitados | EM IMPLEMENTAÇÃO | FPS/GC e logs com 20 NPCs + zumbis em dois mapas |
| NET-01 | P3 | Replicação multiplayer de NPC/loot/animações/combate/rádio e atribuição nativa | NÃO INVESTIGADA | Cliente/servidor com estados consistentes |
| LEARN-01 | P1 | Memória longa **por indivíduo**: resultados de combate contra jogador/zumbi, táticas, caminhos e perigos | NÃO INVESTIGADA | Aprendizado observável e limitado, sem acesso a dados invisíveis |
| LEARN-02 | P1 | Aprendizado online (contextual bandit/recompensa incremental, ou ML viável), exploração, esquecimento/validade | NÃO INVESTIGADA | Evidência sucesso/fracasso altera escolha de tática em contexto comparável; sem exploit |
| LEARN-03 | P1 | Transmissão de ensinamentos **somente por voz local ou rádio físico**; confiabilidade, rumor, consentimento | NÃO INVESTIGADA | Membro ausente não sabe; com contato pode aprender, com erro/incerteza; sem facção-telepatia |
| LEARN-04 | P1 | Morte apaga conhecimento privado não compartilhado; sobreviventes guardam apenas o que ouviram; persistência individual | NÃO INVESTIGADA | Morte antes de contar apaga; depois de contar sobrevivente lembra; substituto nasce novo |
| LEARN-05 | P2 | Aprender rotas/obstáculos, cobertura válida, comportamento contra diferentes tipos de zumbi/jogador | NÃO INVESTIGADA | Cache por mapa/local e validação de mudanças; bloqueios não viram caminho perfeito |
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

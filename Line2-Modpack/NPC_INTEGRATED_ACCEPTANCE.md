# Linha 2 — NPCs: entrega integrada e bateria única de testes

**Política de desenvolvimento:** não solicitar ao usuário um teste para cada método, classe ou comportamento isolado. Só solicitar uma **bateria completa** quando o pacote tiver passado por verificações de compilação, testes automáticos e revisão de integração. Um gate técnico inicial (compilar e carregar o módulo C# no Steam) ainda é obrigatório: sem ele, não há como validar a API efetiva do jogo.

## Plataforma / limites

- Windows 11, Unturned Steam original, sem BattlEye, Washington e mapas Steam Workshop.
- Linha 1 congelada; não editar `main` nem recompilar a Unity standalone.
- Mods e IA modular com controle sob autoridade local/sessão host.
- População de zumbis **igual ao jogo padrão**; hordas reagrupam orçamento existente.
- Sem controle real por IA até provar existência, propriedade e estado de cada entidade.
- Segurança de desempenho: limites de população e trabalhos por frame, consultas de física limitadas, sem refletir assemblies por frame, sem HTTP síncrono na thread Unity.

## Escopo mínimo da primeira versão jogável de NPCs

1. **Spawn e identidade**: comandos do chat/console, spawn neutro/amigável/hostil, skins e nomes, ID estável, despawn e política de população; spawns coordenados e sem sobreposição.
2. **Movimentação**: chão, colisão, animações, detecção de portas, navegação indoor/outdoor, escape de obstáculos, perseguição e busca do último local conhecido; janela de teste para evitar oscilações e travamentos nas quinas.
3. **Percepção**: audição (tiros, zumbis), visão e checagem de oclusão, reação a dano, prioridade ao jogador visível dentro de 20m quando aplicável; sem onisciência.
4. **Combate**: corpo a corpo animado, armas de fogo com mira e dispersão humanizadas, buscar cobertura e recuar de maneira coerente, munição, reload, dano e morte. Ataque entre NPCs e contra zumbis sujeito a facções.
5. **Interações**: diálogos com opções visíveis e saída pelo Esc, comércio básico, recrutamento/aliança quando apropriado, sem esconder HUD.
6. **Facções e vida persistente**: relações, grupos, patrulha, base com modelos fixos, loot coerente, fome/sede em regime limitado, save/load, morte definitiva do indivíduo, reposição após intervalo sem recriar memória indevida.
7. **Hordas e zumbis**: grupos dentro do limite vanilla; perseguição entre regiões com rota válida e regras de retorno/desistência, busca ativa e ausência de loops de rotação.
8. **Observabilidade/estabilidade**: erros reportados em log, contadores por etapa, orçamento CPU, FPS e benchmark pequeno/médio com comando para desativar NPCs; nunca causar efeitos em servidores públicos.

O escopo acima representa **critérios de aceitação**, não funcionalidades já prontas. Não habilitar todas de uma vez sem a infraestrutura de entidade/navegação.

## Gating técnico interno antes do teste único

- [ ] Módulo C# compila na versão Steam atual e carrega sem quebrar Unity.
- [ ] F10 e F12 identificam entidade e componentes reais (se necessário em um mapa com NPC).
- [ ] Adapter do jogo implementado e validado com autorização de sessão.
- [ ] IA externa comunica ações por tick/TTL, com fallback local e sem executar replay.
- [ ] Spawn e despawn funcionam sem vazamentos, travamentos ou sobreposição.
- [ ] Navegação passa por portas, retornos, mudanças de região e obstáculos em múltiplas edificações.
- [ ] Combate, morte, diálogo, comércio e persistência passam em testes internos.
- [ ] Hordas respeitam cap vanilla, e perseguição cross-region passa casos de retorno/desistência.
- [ ] Scripts Python e compilação C# passam; logs e instruções de rollback documentados.

## Roteiro único de aceitação do usuário (quando os gates passarem)

- Inicializar Washington: spawn misto, identificar nome/skin e limite populacional.
- Fazer NPC seguir ao redor de casa, entrar/sair, porta fechada/aberta, perseguir na esquina e recuperar rota ao travar.
- Iniciar combate de curta/longa distância; verificar dano/morte, mira, recuo, cobertura e comportamento ao receber tiro de longe.
- Atrair zumbi para NPC e NPC hostil para NPC amigável; conferir percepção e facções.
- Dialogar, comprar e recrutar; verificar opções e fechar com Esc.
- Testar patrulha, coleta/loot e retorno à base; salvar, sair e carregar novamente; confirmar identidade de sobreviventes.
- Testar morte de membro, preservação do grupo, repovoamento retardado e fim de grupo após perda total.
- Atrair zumbi além do limite de região e verificar perseguição com rota válida e desistência; observar hordas sem aumento do número total.
- Medir FPS/stutters com NPCs desabilitados e ativos, analisar logs e comparar performance.

## Etapa atual (2026-11-06)

A Auto 32 foi confirmada funcional no Steam pelo usuário. O Modpack Manager GUI também passou na instalação local e leitura da Workshop. C# ainda não compilado por falta de SDK na máquina do usuário, então NPCs móveis/combate e hordas **não estão prontos para a bateria final**. O script `scripts/build_diagnostics_windows.cmd` permite executar a etapa de build/instalação sem política de scripts PowerShell. Não apresentar F10/F12 como NPCs jogáveis.

## Integração adicional — 08/11/2026 (linha 2)

A branch de modpack recebeu implementações que avançam os critérios acima, **sem liberar os gates Steam**:

- **Combate corpo a corpo com equipamento real:** o executor seleciona um `ItemMeleeAsset` efetivamente possuído, aplica alcance, dano por tipo de alvo, qualidade, desgaste e cadência limitada. Soco continua sendo o fallback sem arma. São testados multiplicadores finitos, qualidade e limite de alcance.
- **Combate físico e percepção:** os três adaptadores (visão do NPC, tiro do NPC e hit suplementar do jogador contra NPC) incluem `RayMasks.BLOCK_COLLISION` para respeitar também veículos e volumes `CLIP`, omitidos pela máscara de dano nativa.
- **Locomoção real:** animação de caminhada e penalidade por movimento no tiro decorrem da posição física após `CharacterController.Move`; uma intenção bloqueada por parede não conta como deslocamento.
- **Investigação:** som, pegadas e relatórios de rádio só renovam o último ponto conhecido e o relógio de ameaça se forem aceitos pela memória sensorial, evitando buscas reiniciadas por pistas descartadas.
- **Coordenação viva de esquadrões:** a ordem do elenco de até quatro integrantes atribui papéis de líder, guarda, intendente e explorador. A morte de líder promove um sobrevivente, sem reciclar sua identidade ou conhecimento privado. Os papéis influenciam construção, patrulha defensiva, locais authored e entrega presencial de suprimentos.
- **Zumbis contra NPCs:** a escolha de um NPC em outro andar não consome mais a oportunidade de o mesmo zumbi avaliar um NPC efetivamente alcançável no andar atual.

**Testes disponíveis:** regras puras executadas em `BrainTests`, verificações Python e auditoria textual do SDK via GitHub Actions. A compilação completa contra a versão instalada do Steam, a renderização de arma na mão, a animação nativa de combate, rotas indoor reais, impacto explosivo, transferência segura de itens e raid multietapas **continuam sem comprovação de gameplay**. A aprovação automática não deve ser apresentada como homologação final dos NPCs.

## Integração adicional — combate armado e apresentação Steam (08/11/2026)

A comparação com a branch `feature/release-candidate-cycle-2026-11-04` identificou uma lacuna concreta no módulo Steam: o antigo `MobileNPCRangedPresentation` não estava adaptado para `ClientModule`, e o executor de tiro apagava o alvo durante o próprio cooldown.

**Alterações integradas nesta branch:**

- **Correção crítica do tiro:** conservar alvo confirmado durante o intervalo de recarga/cadência, sem reiniciar indefinidamente a reação inicial. O alvo ainda é abandonado quando desaparece da percepção ou o NPC inicia retirada.
- **Teste determinístico de rajadas:** simula sucessivos disparos contra o mesmo alvo confirmado, intervalos de reação, perda de contato, retirada e timestamps inválidos.
- **Apresentação das armas:** o adaptador `Line2NpcWeaponPresentation` usa modelos dos `ItemGunAsset`/`ItemMeleeAsset` efetivamente possuídos, sem criar itens; aplica os pontos nativos de encaixe (mão direita/esquerda, coluna/gancho), desliga colisores do modelo e reproduz o áudio de disparo/recarga apenas quando o executor confirma a ação.
- **Posicionamento armado:** não perseguir até encostar em alvo atualmente visível quando o NPC já dispõe de uma posição válida de tiro dentro do alcance da arma. Fora do intervalo, perseguir normalmente. Retirada, cobertura, dano recente e falta de arma carregada continuam prioritários.
- **Cobertura durante recarga:** usar unicamente ameaça confirmada localmente e ponto de cobertura fisicamente verificado; ainda requer A* nativo para alcançar o destino, não cria teletransporte nem obstáculo fictício.
- **API do módulo:** referência a `UnityEngine.AudioModule` e auditoria de presença de encaixes, eventos, políticas de distância e ligação ao spawn real do NPC.
- **Robustez localhost:** erro HTTP 413 após corpo ligeiramente excedente é devolvido de forma confiável no Windows, sem ler corpos de tamanho arbitrário (antes, o teste podia sofrer `ConnectionAbortedError`).

**Validação disponível:** testes de políticas C# / Python e auditoria textual do SDK aprovados no GitHub Actions. **Não é uma compilação do módulo C# com as DLLs instaladas do Unturned**, nem demonstra que todo modelo e rig Workshop encaixa corretamente em uma cena Steam.

**Ainda bloqueiam a homologação dos NPCs:** compilação real `ClientModule`; demonstração de tiro, animações e efeitos em gameplay; colisores/portas/janelas/escadas reais; teste de explosões e dano mútuo com zumbis; loot e comércio sem perda/duplicação; persistência após reiniciar mapa; incursões multietapas e A-Life coordenado. Esses itens permanecem sem aprovação até evidência real.

## Integração adicional — pisos nativos, linhas de visão e portas (08/11/2026)

**Correções efetivamente integradas nesta branch:**

- **Suporte físico de construções:** todos os planejadores de navegação, posições de cobertura, pesquisa A-Life, reconhecimento de barricadas e restauração de NPCs passam a procurar apoio em `GROUND`, `GROUND2`, `LARGE`, `MEDIUM`, `ENVIRONMENT` e `STRUCTURE` do SDK nativo, não apenas `GROUND2`.
- **Andares sobrepostos:** `Line2NpcFloorGeometry.TryFindStaticSupport` verifica no máximo 16 impactos de um raio vertical e seleciona a superfície fisicamente estática de inclinação válida mais próxima do nível esperado. Saturação rejeita a amostra. Piso e teto não podem ser considerados conectados sem a posterior verificação de cápsula, borda e rota A*.
- **Exclusões:** veículos, atores e corpos rigidbody dinâmicos não são superfícies de sustentação para o planejador.
- **Visão do jogador:** a percepção do protagonista pela IA ignora os próprios seis colisores anatômicos do NPC, mas considera o primeiro obstáculo físico real, incluindo veículos, `CLIP`, outros atores e estruturas. Também exige alvo vivo, alcance, campo frontal e buffer não saturado.
- **Portas inacessíveis:** uma porta fechada e sem ação nativa disponível não pode mais fazer o motor esperar indefinidamente nem constituir uma falsa conexão de navegação indoor. Uma espera de animação só vale para abertura realmente iniciada e é limpa no despawn.
- **Auditoria automatizada:** bloqueia regressões para busca exclusiva em `GROUND2`, exige o amostrador nativo de múltiplas superfícies, a checagem visual física e a autorização nativa de abrir portas.

**Limites ainda não homologados:** cenas Steam reais, clips e assets da Workshop, escadas e janelas com acesso efetivo, navegação por andares com A* ASPFP, build completa do módulo, performance com NPCs + zumbis, gameplay integrado e multiplayer. Nenhum desses itens deve ser descrito como testado apenas pela passagem no CI textual/BrainTests.

## Integração adicional — progresso físico, A* e anti-oscilação (08/11/2026)

**Implementado na branch `line2/modpack-framework`:**

- Rotas A-Life e táticas reconhecem deslocamento **real do CharacterController**, inclusive quando um corredor exige afastamento temporário do objetivo. Os limites absolutos permanecem 140 s (rotina) e 45 s (manobra).
- Um histórico circular de 16 posições físicas impede que retornar repetidamente aos mesmos dois pontos de uma esquina renove os prazos de rota. O sistema não confia na velocidade pedida pelo cérebro, mas nas posições observadas.
- Um caminho ASPFP cujo **último nó** é alcançado, mas cujo destino solicitado continua distante ou em outro andar, não é mais rotulado como sucesso. Apenas chegada física 3D validada conclui a viagem; caso contrário, uma nova rota pode ser solicitada sem gerar teletransporte.
- O provedor nativo A* invalida a associação ao backend Seeker ao ser desativado, para redescobrir o backend ativo após reativação e troca de mapa.
- Candidatos de piso rejeitam camas, armazenamento, sentrys, manequins e portas: superfícies de mobília não são promovidas a pisos arquitetônicos.
- `BrainTests` recebeu regressões de flanqueamento com desvios, patrulhas que contornam construções, tempo máximo, chegada ao andar correto e oscilação em paredes. O auditor textual exige as integrações ativas no runtime.

**Não comprovado por CI:** a execução das rotas nativas no mapa Washington, acesso real por portas/janelas/escadas com assets instalados, conclusão de missões A-Life, coordenação total da facção, compilação contra DLLs da versão Steam e teste de gameplay unificado. CI verde não remove estas pendências.

## Integração adicional — combate dinâmico e modos de tiro (08/11/2026)

**Alterações efetivas na `line2/modpack-framework`:**

- **Tiros contra facções:** os explosivos agora consultam `Line2NPCCombatPerception.CanEngageNpcTarget` para avaliar todos os NPCs no raio de efeito. Aliados de facção e neutrals são protegidos independentemente da string `hostile`/`friendly`.
- **Dano balístico controlado:** o executor de rifle aplica validação finita do multiplicador de queda de dano e dos impactos contra jogador, zumbi e outro NPC. Os raios continuam sujeitos à colisão nativa, ao consumo real de munição e às autorizações de combate.
- **Supressão de tiro REAL que errou:** uma rajada de munição debitada contra rival NPC pessoalmente visível pode criar pressão temporária (2,5s, raio máximo 35m) **sem infligir dano**. A vítima valida novamente o atirador e a linha de visão; intervalos de 1s evitam que dezenas de pellets renovem continuamente o pânico. O cérebro pode optar entre cobertura fisicamente comprovada, resposta armada ou retirada após dano. Não cria percepção de jogadores ocultos.
- **Estresse e dispersão humana:** disparos realizados em movimento, sob ferimentos recentes, sob supressão ou no fim de uma rajada têm maior desvio angular, com teto explícito. O espalhamento ocorre no **eixo local da mira**, não ao redor dos eixos globais do mapa.
- **Flanqueamento armado:** com um inimigo já selecionado, o NPC pode movimentar-se lateralmente mantendo o corpo voltado para o alvo. A navegação, colisão e confirmação de visão do executor permanecem distintas. Ao recuar de forma emergencial, a orientação volta a privilegiar a saída.
- **Modo nativo da arma:** a lógica agora lê `GunStateIndices.FIREMODE` de cada item possuído e respeita `EFiremode.SEMI`, `BURST`, `AUTO` e `SAFETY`, além das capacidades declaradas por `ItemGunAsset`. Armas semiautomáticas deixam de emitir rajadas automáticas fictícias; armas no modo segurança não disparam.
- **Armas adquiridas sem carregador:** se o item nativo possui `MAGAZINE_ID=0` e munição zerada, o executor pode procurar um carregador **realmente possuído e compatível**, iniciar a recarga e aplicar a mesma transação autenticada já existente.
- **Testes:** a suíte C# verifica política de segurança do dano, supressão sem ferimento, cadência, penalidades de precisão e duração de rajadas por modo. A auditoria de SDK verifica o encadeamento do modo nativo aos executores e à movimentação.

**Não é homologação Steam.** O teste real deverá comprovar posicionamento da arma, animação de tiro, danos às seis hitboxes, granadas, ritmo de rajadas, precisão, cobertura, reações a quase acertos, zumbis e CPU com vários NPCs. O `BrainTests` não compila o módulo inteiro com as DLLs instaladas; a experiência completa em jogo ainda depende dessa validação.

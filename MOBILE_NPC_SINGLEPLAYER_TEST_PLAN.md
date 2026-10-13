# Mobile NPC — plano integrado de validação single-player

## Escopo e critérios

Este plano cobre a fundação local dos NPCs móveis no Unturned/U3-SDK. Executar depois de a Unity regenerar a pasta `Library`, terminar a importação e compilar os scripts sem erros.

**Importante:** um item deste plano só fica aprovado após observação no jogo. O código atual é experimental, server-local e single-player-first; não considerar replicação multiplayer, interface de comércio ou sincronização visual de clientes como concluídas.

## 1. Preparação de build limpa

- [ ] Fechar Unity e Unity Hub e confirmar que os processos do Editor terminaram.
- [ ] Apagar somente a pasta `Library` do projeto correto; preservar `Assets`, `Packages` e `ProjectSettings`.
- [ ] Abrir com Unity 2022.3.62f3 e esperar a importação terminar.
- [ ] Confirmar no Console que não existem erros C#.
- [ ] Gerar uma build Windows em uma pasta fora da pasta do projeto.
- [ ] Se falhar em `GenerateNativePluginsForAssemblies` ou limpeza de `AsyncPluginsFromLinker`, guardar o Editor.log completo: essa falha deve ser separada de erros de compilação C#.

## 2. Inicialização e spawn

- [ ] Iniciar uma partida single-player em um mapa oficial.
- [ ] Confirmar no log que o bootstrap encontrou `Resources/Characters/NPC_Client` e inicializou a população.
- [ ] Confirmar que NPCs aparecem fora da linha de visão do jogador, em terreno válido, sem surgir dentro de objetos ou no ar.
- [ ] Andar por regiões com encostas, interiores e áreas obstruídas; spawn inválido deve ser recusado sem travar a população.
- [ ] Confirmar que NPCs não aparecem em cliente remoto ou servidor dedicado por este bootstrap local.
- [ ] Repetir em um segundo mapa para verificar reset de estado e chave de persistência por mundo/mapa.

## 3. Limite populacional e ciclo de vida

- [ ] Confirmar que a população ativa não excede o limite configurado (padrão recomendado: 20; intervalo suportado: 1–40).
- [ ] Confirmar que NPCs restaurados e NPCs criados por outras rotas contam para o mesmo limite.
- [ ] Manter corpos mortos durante o tempo de limpeza e confirmar que eles não consomem vagas do limite de NPCs vivos; a restauração pendente também deve contar apenas vivos.
- [ ] Matar um NPC e confirmar que ele para de combater/mover-se e deixa de ser contado como vivo.
- [ ] Confirmar que o corpo é removido após o tempo configurado.
- [ ] Confirmar que a reposição respeita o atraso configurado e não acontece imediatamente.
- [ ] Matar vários NPCs em sequência e verificar que cada reposição mantém seu próprio tempo de espera.
- [ ] Verificar que um substituto só volta ao grupo anterior se ainda houver membros vivos, espaço e compatibilidade de função/facção.
- [ ] Confirmar que o grupo se dissolve quando seu último membro vivo sai/morre.

## 4. Persistência — prioridade máxima

- [ ] Deixar NPCs vivos em posições distintas, incluindo pelo menos um ferido, um com itens e um em grupo/assentamento.
- [ ] Esperar um ciclo de gravação (aproximadamente 30 s) e encerrar a sessão normalmente.
- [ ] Reabrir o mesmo personagem e mapa; conferir posição, rotação válida, função, facção, vida, grupo/âncora e inventário.
- [ ] Conferir que o limite populacional continua valendo após a restauração e que não aparecem duplicatas.
- [ ] Testar saída imediatamente após spawn e usar o salvamento no encerramento como verificação adicional.
- [ ] Fazer uma cópia de segurança do arquivo JSON e testar recuperação: arquivo primário válido; primário inválido com `.bak` válido; ambos inválidos.
- [ ] Quando primário e `.bak` forem inválidos, confirmar que o sistema avisa e preserva ambos os arquivos; o autosave não deve substituí-los por um snapshot vazio.
- [ ] Em um mapa sem arquivos de persistência prévios, confirmar que o primeiro autosave cria o JSON normalmente; o bloqueio deve ocorrer somente se já existirem arquivos inválidos.
- [ ] Confirmar que arquivo inválido gera aviso controlado e não impede iniciar uma partida.
- [ ] Testar um registro com coordenadas inválidas e confirmar que ele não cria um NPC nem gera loop de erro por frame.
- [ ] Testar uma posição bloqueada: o registro deve permanecer pendente e ser tentado novamente com intervalo, sem apagar silenciosamente os demais registros; registros bloqueados devem alternar na fila para não impedir a restauração dos seguintes.
- [ ] Simular uma falha ao aplicar metadados de um snapshot: confirmar que a instância parcial é removida e o registro continua pendente, sem criar duplicatas.
- [ ] Confirmar que salvar enquanto ainda há registros pendentes não os elimina.
- [ ] Confirmar que outro personagem/mapa não carrega o snapshot deste mundo.
- [ ] Não editar o arquivo de save real sem antes fazer cópia.

## 5. IA, percepção e combate

- [ ] Confirmar que NPCs investigam ruídos, perseguem alvos detectados e retornam quando perdem o alvo.
- [ ] Testar perda de linha de visão e memória de alvo; NPC não deve perseguir indefinidamente uma posição antiga.
- [ ] Ao perder o alvo, confirmar que o NPC revisita a última posição conhecida e percorre três pontos de busca próximos antes de retomar a rotina normal; ruídos também devem gerar busca local.
- [ ] Confirmar que aliados não se atacam por erro de relação de facção.
- [ ] Testar combate a curta, média e longa distância, usando cobertura e obstáculos.
- [ ] Verificar que tiros são limitados por rajadas, pausas, recarga e dispersão; não devem se comportar como sentries com precisão perfeita.
- [ ] Confirmar que dano é aplicado apenas pelo lado servidor/local autoritativo e que um NPC morto não continua disparando.
- [ ] Testar munição esgotada, recarga, arma incompatível e ausência de arma.
- [ ] Conferir no log se algum erro se repete por frame ou cresce continuamente.

## 6. Inventário, saque, comércio e airdrops

- [ ] Confirmar que o inventário respeita a capacidade configurada e copia o estado dos itens.
- [ ] Testar coleta de item do mundo: item deve ser removido da origem apenas quando a transferência for aceita.
- [ ] Testar capacidade cheia e itens inválidos; não deve haver duplicação ou desaparecimento.
- [ ] Testar uso/consumo de item médico e recursos de combate.
- [ ] Verificar o serviço de troca diretamente apenas se existir um chamador válido no build: ele é uma primitiva de servidor, não uma interface de comércio pronta para o jogador.
- [ ] Após um airdrop pousar, verificar se NPCs elegíveis se aproximam e transferem somente itens do contêiner marcado como carepackage.
- [ ] Confirmar que NPCs não saqueiam baús comuns como se fossem airdrops.
- [ ] Testar inventário cheio durante saque e airdrop.

## 7. Grupos, facções e assentamentos

- [ ] Confirmar que grupos respeitam tamanho máximo, distância, função e facção.
- [ ] Mudar função/facção por uma rota autorizada e conferir que a associação ao grupo é atualizada.
- [ ] Confirmar que apenas o líder vivo planeja o local do assentamento.
- [ ] Confirmar que apenas o líder vivo constrói as peças do assentamento; outros membros não devem duplicar estruturas nem consumir materiais em paralelo, e um sucessor deve assumir após a morte do líder.
- [ ] Matar o líder durante uma construção parcial e confirmar que o sucessor herda a contagem de peças do grupo, sem reiniciar o padrão e duplicar as estruturas já registradas.
- [ ] Dar a um NPC itens de construção válidos e verificar escolha de local em terreno seguro.
- [ ] Verificar tentativa de construir armazenamento central e peças de perímetro apenas quando há itens válidos e local livre.
- [ ] Testar local bloqueado, item ausente e construção interrompida; não deve consumir itens se a colocação falhar.
- [ ] Confirmar que itens de construção restantes permanecem no inventário e que armazenamento colocado recebe os suprimentos destinados a ele.
- [ ] Sair e voltar: conferir a âncora/grupo persistidos; observar separadamente estruturas colocadas, pois a persistência vanilla das estruturas não é implementada por este snapshot de NPC.

## 8. Estabilidade e desempenho

- [ ] Testar limite baixo (1–5) e limite recomendado (20).
- [ ] Observar CPU/FPS por pelo menos 10 minutos; verificações periódicas não devem ocorrer sem intervalo.
- [ ] Com 20 NPCs ativos, confirmar que a percepção de jogadores percorre a lista de clientes conectados sem gerar um array de cena por NPC a cada varredura; observar GC Alloc no Profiler.
- [ ] Confirmar que logs não crescem indefinidamente em funcionamento normal.
- [ ] Trocar de mapa antes dos 30 s de autosave e depois retornar ao mapa original; conferir que o snapshot de saída foi gravado e os NPCs voltam sem duplicatas.
- [ ] Trocar de mapa e retornar ao menu sem deixar bootstrap/diretor duplicado.
- [ ] Encerrar normalmente e confirmar gravação final via `OnApplicationQuit`, inclusive após alterações feitas depois do último autosave.
- [ ] Repetir o teste com um save novo e com um save existente.

## 9. Fora do escopo desta rodada

- Replicação multiplayer de spawn, movimento, animação, combate, morte, inventário e construção.
- Interface de negociação e interação contextual com o jogador.
- Garantia de comportamento idêntico em todos os mapas/mods e assets personalizados.
- Aprovação final de gameplay sem executar os testes acima dentro do Unity/jogo.

## Relatório a guardar

Para cada falha, guardar: mapa, passos para reproduzir, resultado esperado, resultado observado, trecho do Console e Editor.log completo se houver falha de build. Distinguir erros de compilação C#, erros de inicialização em runtime, falhas de gameplay e falhas de infraestrutura/cache do Unity.


## 10. Regressão de hit/ferimento (acrescentado em 2026-10-13)

- [ ] Em ambiente servidor local, disparar na cabeça e tronco de um NPC: confirmar um único débito de vida por impacto, registro do atacante e classificação corporal correta.
- [ ] Aplicar dano suficiente para matar: confirmar apenas uma transição de morte, desativação de IA/ataques/interações e apresentação física de cadáver (distinguir ragdoll esquelético de fallback rígido).
- [ ] Repetir dano após morte: a API de vida deve rejeitar a tentativa e a rota de ferimentos não deve declarar sucesso nem modificar estado de partes corporais.
- [ ] Testar ataque corpo a corpo do jogador e de outro NPC, validando o mesmo comportamento sem dupla contagem de dano.
- [ ] Aplicar ferimento com sangramento: conferir dano periódico independente; parar após morte; observar atribuição do último golpe vs. morte ambiental.
- [ ] Se houver falha, salvar mensagens `[MobileNPCHumanoidVitals] Damage accepted`, `Death transition`, `Death ragdoll activated` e erros do Console com mapa/arma/alvo. O commit 2f94246 apenas corrige o valor de retorno de `MobileNPCInjurySystem.ApplyHit`, sem comprovar funcionamento do dano no jogo.


## 11. Experiência individual e aprendizado contextual (NOVO — ainda não validado)

- [ ] Dois NPCs recém-nascidos começam com 0 registros de aprendizado, sem herança de facção/grupo.
- [ ] Com arma real/ammo, um NPC dispara várias vezes em zumbi, player hostil ou NPC rival: só tiro efetivamente comprometido gera um resultado; bloqueio/erro conta como erro e contato físico com o alvo selecionado como acerto.
- [ ] Após sequência de erros no mesmo setor, o intervalo entre tiros muda modestamente sem eliminar o erro humano e sem disparar através de paredes; após acertos, pode se aproximar do baseline. Não confundir acertar o corpo com causar dano/XP.
- [ ] NPC recolhe loot, registra resultado no setor e prioriza subsequentemente com pequena bonificação; após destino de loot expirar bloqueado, o setor recebe penalização limitada, sem bloquear item crítico.
- [ ] A e B estão no mesmo grupo, mas a 20 m de distância: A aprende algo, B continua com memória anterior; grupos da mesma facção, mas IDs diferentes, também não compartilham.
- [ ] Aproximar A de B até ~7 m, em período fora de combate: uma lição da experiência de A é transmitida com peso menor, sem cópia total de estatísticas; parede reduz distância de fala. Agachado usa faixa menor (~4 m).
- [ ] A morre sem contar: memória particular é apagada; substituto não recebe conhecimento. Se B tiver ouvido antes, B mantém o que aprendeu, mesmo com A morto.
- [ ] Salvar NPC vivo com aprendizagem; voltar ao mesmo mapa/personagem e verificar o snapshot JSON v3 `experience` por indivíduo; saves v1/v2 devem carregar com memória vazia, sem crash.
- [ ] Repetir experiências em diferentes setores e com >=20 NPCs, observar memória limitada a 48 registros/indivíduo e ausência de exploração indevida de dados invisíveis.
- [ ] Testar que a troca de lições ainda é apenas um evento simulado do servidor: **não** existe áudio, diálogo de jogador ou rádio até a integração nativa ser concluída.
- [ ] **Futuro:** validar estratégias realmente aprendidas contra diferentes tipos de zumbi, caminhos bloqueados, cobertura e flanqueamento; estas ainda não estão implementadas nesta etapa.


## 12. Experiência de acesso a cobertura (ainda não validado)

- [ ] Após objetivo `SeekCover` com ponto real validado, confirmar que chegar ao destino gera uma experiência positiva `CoverAccess`, e tempo esgotado antes de chegar gera negativa.
- [ ] Simular cobertura atrás de parede com acesso inválido: NPC deve continuar usando teste físico do corredor e não criar caminho mágico.
- [ ] Repetir falhas em um setor e disponibilizar duas coberturas candidatas: NPC deve preferir uma opção não penalizada que ainda passe verificação geométrica. Se nenhuma alternativa existir, manter tentativa defensiva de fallback, sem ficar congelado.
- [ ] Não confundir sucesso ao chegar na cobertura com proteção contra dano. Medir separadamente dano após chegar, risco de recuo e flanqueamento (aprendizado tático mais amplo pendente).


## 13. Regressão P0: dano zero, avanço real e cobertura ε-greedy (2026-10-13)

- [ ] Com arma do player cujo dano de NPC calculado é zero, acertar hitbox: HP **não cai** (arma não vira 1 HP por força de `Mathf.Max`). Com multiplicador positivo, debita apenas uma vez e preserva o atacante. Comparar com zumbi nativo quando aplicável.
- [ ] Repetir com melee sem dano / com dano e verificar que o hitmarker não cria evidência falsa de dano (a UI ainda pode precisar de correção separada).
- [ ] Em corredor com parede e meio-fio, o NPC pode tocar uma lateral do CharacterController e mesmo assim andar: animação de caminhada e footsteps seguem **deslocamento real**; não bloquear `IsMoving` apenas por `CollisionFlags.Sides`.
- [ ] Aproximar-se de parede frontal e provocar oscilação esquerda/direita: NPC não deve registrar progresso de objetivo fictício; o detector stuck deve tentar solução nova em ciclos limitados, sem teleporte.
- [ ] Acompanhar NPC dando volta real em quina: deslocamento lateral significativo deve ser aceito quando continua viável sem se afastar demais do objetivo. Trocar o objetivo no meio da rota e confirmar reset do contador stuck.
- [ ] Em ambiente com duas ou mais coberturas verificadas, acumular no mesmo indivíduo acertos de `CoverAccess` e timeouts. Repetir eventos de ameaça em posições comparáveis e verificar que a opção mais confiável tende a ser escolhida, **mas uma alternativa segura pode ser explorada**. Nunca aceitar um ponto sem solo, obstrução e corredor liberado.
- [ ] Confirmar que o conjunto de candidatas de cobertura é limitado a 10 verificadas por planejamento; avaliar FPS/GC com 20 NPCs e ameaças simultâneas.
- [ ] Guardar `Player.log`, Console, mapa, arma, ângulo, objeto, coordenadas aproximadas e passos de reprodução; não marcar funcionalidade concluída antes de compile + Dev Build.


## 14. Regressão: transição de morte e reposição em grupos (2026-10-13, não executada)

- [ ] Ativar diagnósticos de dano e verificar tiro letal: apenas **uma** linha `Death transition`, uma apresentação física da morte e uma reserva de reposição, mesmo com tiros repetidos no mesmo frame.
- [ ] Submeter callback artificial de `DamageReceived`/`HealthChanged` que tenta curar o NPC ou aplicar dano adicional durante golpe fatal. Não deve cancelar morte, substituir `LastAttacker`, atualizar HP, disparar duas mortes nem duplicar loot.
- [ ] Sobreviventes A/B da mesma facção num grupo (máximo 4); matar apenas A. Após o delay, o indivíduo substituto nasce com identidade/memória nova, e pode se unir ao grupo de B com função/facção compatível se ainda houver vaga.
- [ ] Mudar a facção de um grupo sobrevivente para um valor diferente do padrão do papel; garantir que substituto compatível seja elegível sem trazer a identidade do morto.
- [ ] Matar todos os membros antes do prazo de reposição. O grupo antigo deve dissolver; nenhum substituto pode reconstruí-lo automaticamente ou herdar memória individual.
- [ ] Preencher grupo sobrevivente até 4 antes do novo spawn. Substituto não pode ultrapassar limite; deve permanecer independente, sem compartilhar conhecimento mágico.
- [ ] Provocar falha de spawn (sem chão seguro) após cooldown: reserva deve continuar pendente para próxima tentativa, sem NPC parcial; mapa válido depois deve permitir reposição.
- [ ] Múltiplas mortes com diferentes `RespawnAt`: nenhuma vaga deve ser usada antes do seu prazo, com limpeza de corpo independente.
- [ ] Salvar/trocar mapa/reabrir: verificar grupo, facção, inventário e conhecimento do novo indivíduo; separar claramente o que o snapshot salva do que ainda precisa de migração.
- [ ] Registrar `Player.log` e Console + cenário completo; nenhum destes testes foi realizado na rodada de código.


## 15. Lembrança de munição/carregadores e perseguição fora das POIs (2026-10-13)

- [ ] NPC adquire arma Cobra real: consultar asset nativo do jogo, sem adivinhar item ID. Encontrar e RECOLHER carregador nativo **compatível e com pelo menos 1 bala** num local concreto (delegacia ou loja de armas do mapa). Antes da coleta: `KnownSiteCount` não cresce. Depois: registra magazine ID, posição e evidência positiva.
- [ ] Recolher carregador vazio: não pode ensinar falso local de munição. Carregador incompatível com a arma atual pode ser memorizado como item encontrado, mas **não** recomendado para essa arma (interseção de calibres nativos obrigatória).
- [ ] Com carregador usado/arma com até 8 tiros restantes e nenhum sobressalente compatível, afastar NPC entre 12 e 240m da posição lembrada. Quando estiver ocioso, inventário com espaço e nenhum loot próximo, deve considerar uma viagem limitada ao local real; verificar trajetória por A*, terreno, obstáculos, prioridade de combate e sem teleporte.
- [ ] No destino, item real ainda presente: NPC recolhe via `ItemManager.TryTransferWorldItemToMobileNPCServer`, não duplica; novo local de coleta reforça memória. Sem item: após janela de procura, a visita infrutífera reduz confiança; nunca materializa munição nem considera sucesso por chegar ao prédio.
- [ ] Testar interrupção no caminho por tiro, zumbi, perseguição e inventário cheio: o NPC abandona a tarefa secundária, retoma prioridades de sobrevivência e **não** registra falsamente uma visita fracassada quando foi interrompido.
- [ ] Grupo: A conhece local; B está longe ou atrás de parede que bloqueia audição: B continua sem localização. Dentro do alcance correto em repouso: B recebe informação mais fraca por conversa simulada. Se A morre sem contar, não há herança; se A contou antes, B mantém o que ouviu.
- [ ] Save v4: salvar, fechar e reabrir mapa; confirmar memórias particulares de fonte de munição em NPCs sobreviventes. Saves v1–v3 continuam carregando sem campos novos; personagem novo ou substituto não recebe lembranças do morto.
- [ ] Distinguir `LEARN-06` de `LEARN-07`: o NPC sabe o LOCAL concreto, **não** que todos os prédios do mesmo tipo têm munição. Testar outro tipo de arma / outro magazine compatível por API nativa; evitar mapeamentos hardcoded de Cobra.
- [ ] Zumbi já persegue jogador dentro da POI; jogador cruza a fronteira de navegação e `player.movement.nav` chega a 255: o zumbi **não** deve desistir apenas por esse valor. Observar se o A* consegue seguir no terreno externo; caso contrário, documentar barreira de navegação sem marcar ZMB-01 como concluída.
- [ ] Repetir zumbi cruzando para região vizinha e voltando à POI, sem teleporte, flutuação, travamento, explosão de logs ou perda errada de prioridade do jogador. Testar perseguição de NPC, ruído e swim separadamente. Registrar FPS/GC e logs com 20+ NPCs.
- [ ] Estas verificações são do plano FINAL de Dev Build, não foram executadas na rodada.


## 16. Regressão: fontes aprendidas de TODOS os itens úteis, cura e crafting sem criação mágica (2026-10-13; ainda não testado)

- [ ] NPC recolhe via ItemManager **comida** em fazenda, **água** em casa, **medicina** no hospital, **carregador compatível** na delegacia e um material de construção numa base. Cada recolhimento válido registra **itemID próprio** + posição + indícios positivos. Não permitir aprender itens só porque o NPC os viu de longe, nem items vazios.
- [ ] Quando passa fome e não tem comida, NPC pode preferir local real onde achou comida antes; sede/água idem, machucado/medicina idem. A arma precisa cumprir calibres nativos para recomendar carregador específico. NPC já alimentado/hidratado, medicado ou com reserva de carregador NÃO deve fazer viagens de reposição irrelevantes.
- [ ] Com sangramento e nenhuma bandagem, NPC distingue curativo real IDs `393/95/394` de medicina genérica; fratura exige splint `96`; vida baixa pode demandar medicação. Não usar curativo como cura de HP genérica enquanto sangra.
- [ ] Sem remédios, mas com camisa/calça na mochila: inspecionar as receitas nativas carregadas. **Se existir** receita simples real de roupa→rag/bandagem (direta ou cadeia segura de 2 passos), consumir os exatos materiais e produzir item nativo, para `MobileNPCMedicalBehavior` então estancar sangramento. Se a receita exigir ferramentas, habilidades, estação, item específico ou não existir, NPC **NÃO** deve inventar um curativo.
- [ ] Validar que crafting com slots reservados/cheios ou material insuficiente NÃO destrua suprimentos; no máximo um intermediário real persistente após cadeia incompleta; sem dupe. Repetir com 20 NPCs feridos e observar intervalos de 18s/uso de CPU.
- [ ] NPC lembra posição de atendimento, chega perto e sai do objetivo `Investigate`, podendo iniciar `Loot` e transferir um item real da região. Se nada sobrou, confiança cai; se zumbi/jogador ameaça, abandona destino com prioridade de sobrevivência. Não teleporta para hospital/loja.
- [ ] Relembrar durante sangramento: NPC não deve iniciar viagem de mais de 75m, mesmo se hospital conhecido for distante; verificar risco de morte por bleed e comportamento de tratamento local.
- [ ] A ensina localização encontrada a B somente quando próximos e audíveis e do mesmo grupo; outro NPC distante/hostil não sabe. Morte antes da fala apaga informação particular; morte depois não apaga cópia na memória de B.
- [ ] Salvar e recarregar `MobileNPCPersistence` v5: registros antigos v4 com `magazineID` devem ser reconhecidos, registros v5 com `itemID` devem ser restaurados; saves v1–v3 continuam válidos. Não restaurar memória de NPC falecido num substituto.
- [ ] Várias delegacias/hospitais diferentes: NPC **ainda não generaliza automaticamente a categoria**; esta distinção está pendente `LEARN-07`. Medir procura por hospital específico real observado, não todos.
- [ ] Inventário cheio, roupa realmente vestida (não na mochila), receitas indisponíveis, mundo customizado, POI fora da nav mesh e itens trazidos de armazenamento devem ser registrados como limitações se não funcionarem; não marcar genericamente resolvido.
- [ ] Compilar e executar todos os testes somente na fase final como combinado; guardar mapa/build, coordenadas aproximadas, itens nativos, screenshots e Player.log para feedback.


## 17. P0 — Perseguição dos zumbis após sair das POIs (rodada 2026-10-13, NÃO EXECUTADO)

- [ ] Em mapa com duas POIs separadas, atrair um zumbi pela visão ou tiro e sair da POI de origem. Depois que a região original contar zero jogadores, o zumbi perseguindo deve continuar a atualizar `Zombie.OnUpdate()` e produzir posição/stuck válidos, sem duplicar eventos da mesma região no loop normal.
- [ ] Andar para área aberta sem `LevelNavigation.checkNavigation` (incluindo caso `player.movement.nav == 255`): perseguidor já adquirido a menos de 24m deve andar fisicamente em terreno válido, usando colisões do CharacterController. Testar alvo jogador e NPC móvel.
- [ ] Abrir distância superior a 24m: o fallback físico não deve fingir alcance ilimitado; verificar o que o pathfinder nativo realmente consegue fazer e registrar gaps para CORE-04/ZMB-01.
- [ ] Atrair perseguidores para parede/casa/porta: o fallback **não** atravessa objetos, não sobe parede, não teleporta; caminho complexo fica para A*/navmesh, detectar quando fica travado. Repetir inclinações acima de 38°, borda de penhasco, água, pedras, escadas e pisos multiníveis.
- [ ] Comparar perseguidores já em região ocupada com aqueles de região sem jogadores: não devem ter atualização duplicada, movimentação acelerada ou spam de eventos. Simular 1, 20, 50 e 200 zumbis perseguindo, coletando Player.log, FPS, travamentos e custos de physics raycasts.
- [ ] Em multiplayer (pendência distinta), client fora do bound de origem do zumbi pode não receber o spawn/snapshot mesmo que o zumbi se mova no servidor; testar `GatherRemoteClientConnections(zombie.bound)` e registrar risco de NET-01, **não** marcar ZMB-01 fechado sem consistência.
- [ ] Inspecionar o projeto Unity: dependência comercial A* Pathfinding Project no U3 SDK pode estar ausente. Registrar componentes de navegação realmente presentes, bake da navmesh de cada mapa, link entre POIs e resultado de path requests antes de declarar perseguição completa.
- [ ] Confirmar que nadar em condição inalcançável ainda permite a reação nativa, que alvos mortos/ausentes liberam perseguição e que zumbis atacam sem passar por paredes mesmo após moverem fora da POI.
- [ ] Compilar e executar no final do desenvolvimento, conforme instrução do usuário. Marcar `ZMB-01` como CONCLUÍDA apenas com feedback do jogo e todas as situações essenciais verificadas.


- [ ] Atravessar borda de mapa, saída da área jogável e volumes de clip com alvo vivo visível: a perseguição física de zumbi não deve atravessar a fronteira nem se mover em posições rejeitadas por `Level.checkSafeIncludingClipVolumes` (commit `143a47fe`).


## 18. Zumbis fora de POI: contorno local de paredes e tiros exteriores (2026-10-13; não executado)

- [ ] Comparar partida limpa com 1 zumbi e múltiplos zumbis: atrair do interior de uma POI para fora da nav mesh e, em seguida, voltar à POI enquanto o zumbi está fora. O perseguidor não deve desistir automaticamente quando nav=255 nem quando o alvo retorna ao interior; conferir `CharacterController` e colisão.
- [ ] Criar esquina em "L" no terreno aberto (fora nav) com obstáculos reais do mapa, sem buraco no solo. Com alvo a 4–20m, o zumbi deve avaliar alternativas laterais de até 2,5m em quatro direções limitadas, sem atravessar parede, tentar salto impossível ou teleporte. Se não houver caminho seguro, deve devolver controle ao navegador nativo e **pode falhar**; registrar a geometria exata.
- [ ] Testar parede longa sem passagem, corredor estreito, porta trancada, colisões dinâmicas de veículo, inclinações >38°, ribanceira >0,65m, água e limites de clip: desvio físico não ultrapassa. Detector de travamento respeita cooldown após três tentativas fracassadas, depois reconsidera. Medir ausência de oscilação infinita.
- [ ] Com alvo movendo-se >6m durante um desvio, a alternativa antiga é invalidada; com alvo visível e caminho direto livre, abandonar imediatamente o desvio e aproximar diretamente. Nova aquisição e reset/reaparecimento do zumbi não podem reutilizar waypoint anterior.
- [ ] Dar um tiro real **fora de todas as regiões nav**, mas dentro dos limites jogáveis. Zumbis a 10–20m devem receber o alerta auditivo e investigar a posição do disparo; um tiro em área inválida ou ruído com coordenadas inválidas não deve gerar eventos. Não exigir que zumbi identifique o atirador se não o viu.
- [ ] Emissão de som atrás de uma esquina fora nav: validar que `EHuntType.POINT` usa o mesmo desvio físico curto e a busca pelo ponto, sem alternar imediatamente para perseguição do player através de parede. Alvo vivo visible claramente a 20m mantém prioridade conforme regras originais.
- [ ] Sons fora nav a mais de 24m: fallback manual não deve fingir caminho; observar pathfinder nativo e registrar os locais em que faltam malhas A* (ZMB-01 ainda aberto). Testar chuva, explosão, arma automática/tiro rápido e cooldown espacial.
- [ ] Testar 1, 20, 50 e 200 zumbis ativos perto de obstáculos: avaliar chamadas de raycast/probe no Profiler; cada escolha de desvio tem no máximo 4 candidatos e intervalo mínimo de 0,65s, com verificações locais por tick.
- [ ] Multiplayer: cliente em região fora do bound do zumbi **pode não ter recebido o spawn ID**; o pacote `ReceiveZombieStates` exige região networked e índices conhecidos. Não marcar replicação correta até existir registro/streaming seguro; no teste singleplayer, isso não deve impedir a simulação do servidor.
- [ ] Anexar Player.log/Console, mapa, posições aproximadas, número de zumbis, nav de origem/destino, tipo de obstáculo, caminho esperado/real e captura do Profiler. **Dev Build é a etapa final do usuário; ainda não foi compilado.**


## 19. P0 — Fim a fim do dano de tiros e melee em NPCs móveis (NOVO — não executado)

**Causa a verificar:** `PlayerInputPacket.write` antes emitia `SKIP` para collider móvel sem tipo nativo, então nenhum dano chegava ao servidor. A fonte agora define `MOBILE_NPC` e refaz o raycast autoritativo no servidor.

- [ ] Testar num jogo local (host) NPC hostil/amigável: tiro no torso com arma real reduz HP **uma vez**, cria ferimento numa região válida e informa `[MobileNPCHumanoidVitals] Damage accepted`. Não repetir apenas hitmarker: garantir log de vida real.
- [ ] Testar impacto na cabeça de NPC de 1,8 m: multiplicador `ELimb.SKULL` e marcador crítico (quando arma permitir); tiro no torso usa base `ELimb.SPINE`; tiros em pernas podem causar fraturas. Não exigir precisão anatômica da capsule única, que precisa de colisores por osso.
- [ ] Matar NPC com arma de fogo e melee; o mesmo golpe causa `Death transition` **uma vez**, desabilita agente, interrompe movimento/tiro/coleta, habilita ragdoll/fallback e agenda cadáver/reposição atrasada. O NPC morto não intercepta novos disparos nem ganha XP/itens duplicados.
- [ ] Testar armamento com dano zero/negativo ou munição insuficiente: nenhum dano falso, nenhum sangramento e nenhum hitmarker indevido. Espingarda multi-pellets: cada pellet validado gera seu próprio hit uma única vez; morte não aplica dano adicional.
- [ ] Testar tiro longo até ~500 m quando arma nativa permite: o novo `MOBILE_NPC` do pacote não descarta automaticamente a distância; o raycast físico e a arma continuam impondo alcance real, desvio/queda e linhas de visão.
- [ ] Testar parede sólida ou NPC atrás de zumbi/Player/objeto: confirmação do servidor só admite primeiro collider de NPC móvel real, bloqueando tiros forjados ou cobertura atravessada. Ponto client/server com divergência >1,5 m não deve causar dano.
- [ ] Com jogador usando melee, verificar `PlayerInput` envia tipo `MOBILE_NPC`, distingue hitbox sem classe nativa e valida alvo dentro do alcance da arma. Conferir que lag/collider temporariamente movido não gera falso positivo.
- [ ] Testar combate de NPC armado contra outro NPC rival: pontaria imperfeita, munição consumida, raycast real, ferimentos por região e atribuição de agressor, sem fogo amigo autorizado por engano.
- [ ] Testar golpes nativos de zumbis e socos sem arma contra NPC separadamente; o novo tipo `MOBILE_NPC` foi conectado só a `Gun`/`Melee`, e socos continuam pendentes se o pipeline nativo não os suporta.
- [ ] Regredir acertos nos jogadores/zumbis/animais/objetos, nas armas melee e gun; novo enum foi inserido ao final para preservar números originais. Sem mensagens desconhecidas do NetEnum nem descarte indevido.
- [ ] Cliente multiplayer só quando `NET-01` for implementado: NPC runtime ainda não é replicado como entidade em cada cliente. Não interpretar a nova mensagem como replicação completa de NPCs.
- [ ] Rodar câmera em primeira pessoa e terceira pessoa, testar várias munições e mapa/POI diversos; compartilhar `Player.log` e log de dano após a build final, como combinado.

### Regressão de grade A* local corrigida

- [ ] NPC contorna quina de casa: grid de 9×9 células mantém nós válidos próximos às paredes, mas rejeita arestas realmente obstruídas. Não atravessar parede nem caminhar lateralmente sem progresso.
- [ ] Dois pisos separados por >stepOffset: grafo local não conecta um piso superior ao inferior por aresta impossível. Escada verdadeira depende da malha nativa/nav e de pequenos degraus válidos.
- [ ] NPC muda de objetivo durante detour: descartar caminho obsoleto, não entrar em oscilação permanente junto ao muro.


- [ ] No diagnóstico de `PlayerInputPacket.write`, testar tanto `RaycastInfo.collider` presente quanto impacto que só preserve `RaycastInfo.transform`; o pacote `MOBILE_NPC` deve ser preservado somente quando o transform pertence efetivamente a um NPC móvel, sem alterar impactos do chão ou de outros objetos (commit `59714ab8`).


## 20. P0: anatomia física, morte/cadáver, loot nativo e XP — 2026-10-13 (testes para a build final)

- [ ] NPC vivo: seis colliders `Hit_Head`, `Hit_Torso`, `Hit_LeftArm`, `Hit_RightArm`, `Hit_LeftLeg`, `Hit_RightLeg` sob `MobileNPCHitbox`, Entity 23, são realmente detectados por `RayMasks.DAMAGE_CLIENT` e `DAMAGE_SERVER`. Os colliders NÃO são triggers, e a cápsula de movimento Agent 24 é independente.
- [ ] Testar arma e melee de frente, lado e costas: tiro na cabeça ativa ELimb.SKULL/hitmarker crítico e dano maior quando multiplicador correto; tronco e membros usam base SPINE, com lesões por regiões coerentes. Não pressupor articulações visuais exatas: os colliders novos são root-local, não bone-bound.
- [ ] O NPC não deve se empurrar, tremer, deslizar ou travar por colisão entre hitbox e seu próprio CharacterController, inclusive após carregamento/spawn/ativação, agachamento, escada e outras instâncias NPC próximas. `MobileNPCBodyHitbox.OnEnable` reaplica IgnoreCollision depois que template inativo é ativado.
- [ ] Tiro de NPC hostil em NPC inimigo: raycast real encontra marker do membro, nunca atravessa muro, gasta bala, aplica UMA lesão e 1 débito HP; aliados não se acertam intencionalmente.
- [ ] Morte com tiro na cabeça e com sangramento tardio: `DieServer` disparado uma vez, todos os seis colliders deixam de existir para raycasts de combate, AI/motor/deslocamento/tratamento/looting param; corpo físico começa a cair e não muda de cor ou nome por componentes de vida.
- [ ] Confirmar o prefab original `NPC_Client.prefab` sem ragdoll: um Rigidbody e zero Joints. `ActivateRigidCorpseFallback` deve usar uma cápsula física que cai, **não** ser declarada ragdoll esquelético. Futuro `RAG-01` exige ossos articulados/juntas reais.
- [ ] NPC vivo com Rifle/Cobra e carregador carregado, comida, água e bandagens no inventário: matar e conferir que cada **item real** aparece no mapa uma única vez, com mesmos ID, quantidade, qualidade e `state` original; remover os itens dos slots apenas após drop bem-sucedido. Zero drops inventados ou duplicados.
- [ ] Simular veto no `ItemManager.onServerSpawningItemDrop`, região inválida ou item Pro-only: nenhum item fantasma; rejeitado retorna ao inventário do cadáver ou permanece não-droppable, tenta de novo sem spam crítico até limpeza. Registrar o caso em que a entidade é destruída antes de resolver o item recusado.
- [ ] Confirme que um jogador que mata diretamente NPC **hostil** recebe 10 pontos de XP reais (uma vez), no sistema de experiências do Unturned. NPC amigável/neutro morto dá 0 XP; morto por zumbi, NPC ou sangramento posterior não credita falsamente o jogador. Stats/quest kills ainda não estão implementados.
- [ ] Corpse remains physical and lootable within lifetime (30s default); replacement spawns with new identity and no former memories after delay (60s default), group survivors retain only already shared knowledge. Stress: 40 NPCs and simultaneous kills.
- [ ] Regredir zumbi/animal/player vanilla gun and melee: `MOBILE_NPC` enum extension/marker detection doesn't change native hit processing. Não solicitar compilação antes do encerramento da implementação; aguardar a Dev Build final + `Player.log` do usuário.


- [ ] Verificar drop lateral (~0,9m da origem do cadáver) em terreno plano e ao lado de casa, parede, plataforma, encosta e outros cadáveres: itens pousam em solo apropriado, sem afundar, flutuar, ficar dentro da cápsula ou duplicar (commit `92ef3dd8`).


## 21. P0 — Socos desarmados e zumbis golpeando NPCs (a executar somente na última Dev Build)

- [ ] Jogador sem arma dá soco com EPlayerPunch.LEFT/RIGHT em NPC hostil. O raycast cliente acerta o collider Entity 23, a rede envia `MOBILE_NPC` com usage `Punch`, servidor reconfirma primeiro collider dentro de 1,95m/dot >=0,95 e registra apenas UM `ApplyValidatedWeaponImpactServer`. HP deve cair, hitmarker normal ou crítico se `Hit_Head`.
- [ ] Socar NPC neutral/friendly registra o dano real sem XP de hostil; matar hostil por socos concede XP UMA vez, sem duplicação em hits pós-morte; ragdoll/corpse limpeza dentro do contrato.
- [ ] Soco ao ar, soco mirando pelas costas, através de parede, a mais de 1,95m, NPC que acabou de morrer, lag desmedido e dois NPCs empilhados: nenhum acerto fabricado. Atingir outro NPC mais próximo pode acontecer somente se realmente for o primeiro collider reconfirmado. Comando com usage falsa deve ser descartado.
- [ ] Testar host/listen server e pipeline remoto `PlayerInputPacket`: tipo MOBILE_NPC preservado quando existe apenas `RaycastInfo.transform`, e com collider presente. Não regredir soco vanilla contra Player/Zombie/Animal, estruturas ou árvores.
- [ ] Zumbi comum, sprinter e crawler golpeiam NPC vivo em alcance físico. O dano deve vir do perfil nativo de zombie, só uma vez no frame de impacto, gerar possível sangramento/lesão e memorizar `LastAttacker=Zombie`.
- [ ] Atrair zumbi para um lado de parede e NPC para outro a curta distância horizontal: LOS `BLOCK_SENTRY` deve bloquear dano, inclusive quina e porta fechada. Colocar player entre NPC e zumbi; conferir prioridade e que o primeiro obstáculo impede dano indevido.
- [ ] Zumbi perde NPC alvo por morte ou fuga: golpe enfileirado não acerta cadáver nem alvo distante; animações de mordida/ataque existentes ainda são executadas; zumbi não ganha percepção onisciente através de parede.
- [ ] NPC sem arma ataca NPC inimigo: a verificação de alvo físico `nearestTransform` fornece região correta para `ApplyValidatedWeaponImpactServer`, gasto de cooldown, animação `AttackStarted`, sem ataques a aliados ou HP duplicado.
- [ ] Testar NPC armados e desarmados contra player/zumbis; as rotas nativas player/zombie de `DamageTool.damagePlayer`/`Zombie.askDamage` devem continuar sem regressão.
- [ ] Medir logs do servidor de dano e morte, tempo de recarga de golpe, animação, FPS com 20 zumbis + 20 NPCs; verificar que não existe economia infinita de XP por atacar o mesmo cadáver.
- [ ] Infecção específica por ataque zombie é **pendência**, não foi simulada como consequência fictícia de todo soco. Guardar observação para sistema médico futuro.
- [ ] Todos estes são testes PLANEJADOS, não executados. Final Unity Dev Build e Player.log serão fornecidos pelo usuário ao fim, e a pendência só será encerrada após feedback positivo.

- [ ] Conferir a enumeração exata `ERaycastInfoUsage.Punch`: servidor não deve consumir um pacote da categoria `MOBILE_NPC` quando o cliente disser `Gun`, `Melee` ou outro uso durante um soco; manter a sequência da fila e a cadência de ataque nativas.


## 22. P0 CORE-04 — validação de waypoint, rota por quinas e stuck (para a Dev Build FINAL; NÃO EXECUTADO)

- [ ] NPC hostil perseguindo jogador contorna esquina em L de uma casa no mapa real. Primeiro waypoint fica cerca de 2–4m antes do destino: chegar à quina **não** chama `brainDriver.ReportDestinationReached` nem encerra perseguição/cobertura/loot; segue até destino real. Confirmar no console/decisão.
- [ ] Forçar A* nativo disponível e indisponível; pedido global pendente não deve bloquear a rota grade A* local por compartilhar cooldown (agora timers independentes).
- [ ] Escolher destino atrás de parede a aproximadamente 4–7m: último nó da grade deve permanecer em solo válido; `localGoal` projetado dentro de parede não entra diretamente em `routeWaypoints`; não atravessar parede para alcançar o alvo.
- [ ] Quina em que o caminho começa indo de lado ou até afastando-se do player: a distância ao waypoint diminui e conta como progresso; recuperar stuck NÃO deve trocar de lado e reiniciar o caminho a cada 1,5s.
- [ ] Casa com dois andares: nó de waypoint projetado no mesmo X/Z do NPC mas >stepOffset na vertical não é marcado atingido; não atravesse teto nem pule para o andar superior sem escada validada.
- [ ] NPC encostado em chão/parede: nodes perto do chão ainda são válidos se não houver paredes, pois cápsula de ocupação não deve tratar terreno como obstáculo bloqueante. Testar raio real do CharacterController contra meios-fios.
- [ ] Quatro aliados e dois NPCs inimigos diante de uma porta: quando somente um personagem vivo bloqueia brevemente o corredor, o caminho A* planejado não é destruído e refeito todo frame; quando porta sólida fecha, a rota deve ser invalidada e replanejada. Inspecionar custo máximo dos SphereCast/Overlap.
- [ ] Cobertura real atrás de parede, mas sem corredor navegável: depois de quatro recuperações de stuck, o motor deve notificar a IA de falha, desmarcar SeekCover/Flank, registrar `CoverAccess=false` apenas se cover, evitar repetir aquela posição num raio de 3m por 14s e procurar outra possibilidade. Não encerrar perseguição por isso.
- [ ] Outro tipo de tarefa (looting, medicina, retirada crítica, seguir colega, investigar som): mudanças na conclusão de waypoint **não** podem marcar destino prematuramente; falha em cobertura não deve cancelá-las.
- [ ] Estresse com 20–40 NPCs móveis + zumbis em duas POIs, incluindo cadeiras, mesas, escadas, pontes, portas, veículo, água e quina externa: medir FPS/GC/logs de stuck e observar buffer de raycasts.
- [ ] Anotar mapa, build, obstáculo e posições iniciais, status A* nativo, quantidade de NPCs, log do console e `Player.log` quando usuário compilar no final; até lá manter `CORE-04` aberta.


## 23. Shooter-inspired urban combat routing — CORE-04 / AI-03 (FINAL BUILD ONLY; NOT EXECUTED)

- [ ] Enemy firing from exposed courtyard; NPC seeks cover with two physically valid paths: short fully exposed vs slightly longer route sheltered by concrete wall. Under **real visual contact**, local A* may pick safer slightly longer route (+1.45 exposed link cost), obeying collision, exact target point and stop distance. Confirm it still reaches the chosen cover rather than standing in the open.
- [ ] Repeat when player leaves sight: route cost should **not** use current live `Player.transform` to infer a hidden moving player's location. No sixth sense; only cached/learned clues when explicitly shared.
- [ ] No combat/normal roaming/looting: tactical route exposure penalty **does not apply**. Path shortest viable with physical collision. Verify threats >40m away don't cause expensive tactical ray queries.
- [ ] Exposed node versus world-shielded node: characters (Player, NPC, Zombie) are not considered solid durable cover. Static building walls protect according to actual RayMasks.BLOCK_COLLISION; no fake protection just because friendly walks past ray.
- [ ] 81-node grid local route triggers at most 24 supplemental threat-visibility raycasts, plus existing occupancy and physical edge probes. Test 1, 4, 20, 40 NPCs under simultaneous fire with Unity Profiler, ray counts, frame time, GC and log volume.
- [ ] Native/local planned path with a redundant near corner: NPC can look past the next waypoint only if 4m or less direct corridor is clear AND each intermediate ground sample valid; no cutting through house wall, locked door, multiple floors, cliffs, broken bridges or roof.
- [ ] Candidate hard cover behind a wall reached through a genuine side doorway/corridor within ~6m: direct line from NPC to cover is blocked, but `CanReachNearbyCoverServer` 9x9 A* finds exact physically accessible cell; after choice NPC walks through passage to cover (never teleports).
- [ ] Similar hard cover completely walled off: local A* may have nearby partial cells but **must reject** candidate when actual destination is not reachable. After 4 repeated failed movements, existing tactical failure feedback / short cover avoidance applies, no infinite side-to-side ping-pong.
- [ ] Brain checks at most 3 expensive indirect-cover route queries per defensive evaluation. Rejected cover does not mutate current route or cancel old movement state. Check `coverRouteScratch` and `routeWaypoints` are independent.
- [ ] Moving zombies or 4 NPC allies momentarily block a doorway: do not trash a valid global route or walk through living people, no precision perfect navigation; use bounded local collision.
- [ ] Compare outdoors/indoors on at least two maps (stock + custom) with ASPFP optional backend present/absent. Stairs, opening a door, breaching obstacles, multiple rooms, congestion and full global integration remain open under CORE-04.
- [ ] Retain CORE-04 EM IMPLEMENTAÇÃO, AI-03/LEARN-05/PERF-01 open until user's final Dev Build/Player.log plus measured physical behavior; record any collision masks, compile errors or frame-rate regressions.


## 24. P0 CORE-04 / NAV-DOOR-01 — genuine physical doors (only in final Dev Build)

- [ ] NPC physically outside a house wants destination behind **native InteractableDoor barricade** registered in `BarricadeManager`; door is unlocked and closed. Motor must compute a route across the doorway (A* cell and edge), approach door, call `ServerSetDoorOpen`, wait until hinge/barrier collider really clears, and cross via CharacterController; never teleport/disable collision manually.
- [ ] Same doorway **locked**: `IsLockedForMobileNPCNavigation` must block opening EVEN on singleplayer host, despite native player `checkToggle` bypass. No owner/steam group spoofing; no silent unlock.
- [ ] Wall or locked barricade physically closer than unlocked door: first collision is a wall, never opens door from behind wall; local grid does not accept cell/edge if it overlaps another solid object in addition to the door.
- [ ] Animated door recently opened/closed by player: respect `isOpenable` 0.75s debounce and no repeat network spam; keep path during <=1.25s hinge motion, but fall back to route replan if collision never actually clears.
- [ ] Two/four NPCs at same doorway: only actual first NPC or whichever arrives first needs to open; others observe the synchronized state and wait rather than oscillate or cause repeated toggle actions. Zombies/players temporarily crowding door do not make it an imaginary open wall.
- [ ] Doors placed on vehicle, unregistered prefab outside BarricadeRegion, a door blocked by extra structure, door at clip/map edge, custom-map doors: no false opening, crash, passage or magical collision removal. Future types remain CORE-04.
- [ ] With no path through the door because it's locked, NPC can still seek an alternate valid route/cover if one exists. Do not get stuck indefinitely vibrating; report genuinely unreachable cover for tactical goals.
- [ ] Player returns to close door mid-pursuit: NPC must re-check physical collision and decide to reopen if unlocked/nearby, rather than follow cached path through closed collider.
- [ ] Upstairs target behind closed unlocked doorway: opening door is not equivalent to linking floors; require actual staircase graph. If target remains inaccessible, document under CORE-04.
- [ ] 1, 4, 20 and 40 NPCs, in stock/custom maps: Unity Profiler physics casts/CPU, no excessive open requests; check original vanilla Player door interactions and reliable state replication unchanged.
- [ ] `Player.log`, map, native asset door type, owner/group/locked flag and obstacle geometry supplied by user ONLY at final build; then mark `NAV-DOOR-01` resolved or return to implementation. CORE-04 stays open until all navigation scenarios pass.


- [ ] Em teste de navegação A* local, inserir collider de prefab InteractableDoor ainda **não registrado** em BarricadeManager/BarricadeRegion: o NPC não pode presumir que a célula/aresta é atravessável apenas por haver componente de porta (`775fb0aa`).


## 25. NAV-STAIR-01 — real vertical stairs and floors (reserved for user's FINAL Dev Build)

- [ ] NPC on house ground floor, player directly overhead on upper floor (same X/Z). It must **not** move through ceiling or claim reached destination solely by X/Z; if a walkable stair lies within bounded local grid, it should follow floor-supported horizontal waypoints up to the upper floor, physically using the CharacterController.
- [ ] Ascend then descend a simple straight staircase or gently sloped 35° ramp: floor sampled every ~0.35m, climbing within real stepOffset/skinWidth and slope limit, not one giant vertical jump; no interruption of melee/firearm attack detection beyond ordinary collision.
- [ ] L-shaped staircase and landing, narrow flights, real multi-level office stairs: record which connect within the 9×9×2 local grid; a search failure is a pending NAV-STAIR-01 scenario, not evidence of success. Test route across two distinct floors with roof geometry.
- [ ] Two horizontal floors separated by 3m and NO stairs: two walkable overlapping surfaces alone must NEVER form a route. Vertical edges within same XZ cell are forbidden, even if target stands directly above NPC.
- [ ] Open shaft, cliff, stairwell with missing tread, water below, locked door on stairs, wall/railing or head-level beam: reject physically disconnected adjacent nodes. Validate samples, full capsule clearance, door permissions, collision and map clip safety. No teleport, no forced Y correction and no fake jump.
- [ ] NPC approaching stairs occupied by allied mobile NPC, Zombie or Player: temporary actor doesn't irreversibly delete the path but real movement must wait/steer — characters cannot pass through each other; if an additional solid wall blocks the stairwell, path must fail.
- [ ] Combat scenario: enemy moves upstairs without changing X/Z; previously built downstairs route must be invalidated because target Y changed >0.8m, and the NPC must only shoot if real LOS and weapon/fire rules permit.
- [ ] Test a corridor with an unlocked native BarricadeDoor and actual staircase: A* may classify native unlocked door as a possible edge, but the agent must physically open it, wait for hinge collider and then traverse stair. Locked and unregistered doors remain impassable.
- [ ] With 1, 4, 20 and 40 NPCs attempting nearby floor transitions, inspect Profiler: one vertical plan globally every >=0.16s, per-agent >=2.5s, max 180 connector tests per plan, 24 floor hits buffer/cell. Measure CPU, allocations, spikes and frame-rate.
- [ ] Several floors (>2) stacked at one X/Z and maze-like stairwells whose entry is >5m away: are KNOWN LIMITATIONS; avoid pretending the current planner works there. Future authored level connectors / global A* path graph necessary.
- [ ] Test at least two stock maps plus custom house; record 3D coordinates, floor geometry, map name, A* backend status, any invalid ray collider, `Player.log` and short clip for user's final Dev Build review.
- [ ] Until compilation and genuine in-game behavior are verified, keep `NAV-STAIR-01` and `CORE-04` EM IMPLEMENTAÇÃO. Do not delete or close either task.


- [ ] Escada vertical inacessível com cobertura superior: A* horizontal não pode escolher célula do térreo próxima em X/Z como se fosse caminho válido; após três falhas para a mesma cobertura tática opcional, memória registra `CoverAccess=false` e reavalia objetivo. Em perseguição de jogador no andar superior, o inimigo mantém consciência apenas de pistas válidas e NÃO cancela a perseguição por falta de caminho local. `077bb7a3`.


## 26. LEARN-ROUTE-01 / NAV-JUMP-01 / NAV-ACCESS-01 — physically experienced route memory (FINAL DEV BUILD ONLY)

- [ ] NPC 1 with real brain destination walks from room A up a genuine staircase to room B, using native/local A* and CharacterController. While moving, positions are sampled AFTER movement; only after reaching goal XYZ (not waypoints) `MobileNPCTravelMemory.RouteCount` becomes >=1. An uncompleted/changing objective must never create a fake learned route.
- [ ] Repeat same trip (origin within 3m and goal within 2m at correct elevations). The same NPC may reuse its personal stored waypoints but must still collide with walls and physically ascend; compare new A* calls and physics checks with first visit, and measure CPU before claiming optimization.
- [ ] NPC 2 from same faction, same map but no prior trip: must NOT inherit NPC 1's exact waypoints. Two unrelated NPCs and one dead NPC replacement each start without knowledge. Specific route transmission over real speech/radio is still future, not simulated automatically.
- [ ] Block previously learned route with placed structure, locked native barricade door, new collider, destroyed stair step, water or terrain change: physical revalidation must reject changed route and decrement trust; after three stale reuse failures the record is ineligible until traversed and learned again. No teleport.
- [ ] NPC 1 save with snapshot v6 then reload same surviving record: privately learned `learnedRoutes` restored. Load old v5 snapshot: should safely read empty route memory without throwing; ensure no NPC data loss from migration, and test distinct player/map isolation.
- [ ] Early game without learned route: ordinary native A*/local A* must still operate. Repeated movement target changes (pursuit while player runs around) cannot create hundreds of half-learned entries; max eight route records, 36 footsteps each, valid gap <=2.6m.
- [ ] During visible hostile SeekCover/Retreat, old route must NOT override current exposed/sheltered tactical A* decision. In ordinary travel, saved route must be geometry-checked and 3D goal-matched; no reuse simply because target appears same X/Z upstairs.
- [ ] Jump a LOW curb/obstacle with existing reactive `ShouldJumpOverLowObstacle`, checking real landing and run/sneak/injury constraints. Separately test stairs of 45–90°/vertical ladder, vaulting, gap-jumping: EXPECT NOT FULLY SUPPORTED by navigation A*, keep `NAV-JUMP-01` open.
- [ ] From a newly visited room far from known stair entrance, with goal upstairs: existing cache may NOT have a matching start/goal and must not pretend to know a global connector. `NAV-ACCESS-01` requires future learned entrance/exit portal graph and native map routing; lack of link is pending, not user error.
- [ ] With 1/4/20/40 NPCs making repeated trips, profile waypoint revalidation casts, route lookup/CPU, garbage allocations, save-file size, FPS and stutters. Performance improvement is a TESTABLE expectation, not confirmed result.
- [ ] Final user Dev Build only: attach `Player.log`, map/build, start/end XYZ, path blockage and snapshot version; only then close or reopen scoped `LEARN-ROUTE-01`. Keep global CORE-04, LEARN-05, NAV-STAIR-01 and jumping/access graph tasks pending until proven.


- [ ] Forçar `RecoverFromUnexpectedFall` durante viagem longa entre andares: a posição recuperada por segurança NÃO pode ser contada como passo fisicamente aprendido. Depois de cumprir todos os waypoints lembrados, apagar o ID da rota atual e não penalizá-la por um futuro obstáculo em uma rota já diferente (`e89e6580`).


## 27. NAV-ACCESS-01 — Accessing a personally learned staircase from another room (FINAL BUILD ONLY)

- [ ] NPC physically discovers/uses a stair route between two floors and finishes goal, so the same individual records real first-hand footsteps and saved v6 route. Place that NPC again ~8–15m away on the same original floor in another room and give it a goal near the learned stair EXIT. With native A* runtime backend active, it should find a real native path to the observed entrance, then replay the validated stair steps through CharacterController.
- [ ] Confirm **entrance is only an intermediate waypoint**, not `brainDriver.ReportDestinationReached`; reach goal upstairs at correct 3D Y. No teleport, no fake XZ shortcut through stacked floor/ceiling. Going back DOWN an upstairs journey that was only observed UP must not be magically known as another traveled route.
- [ ] When the entrance is 2.5–5m away, the bounded **local exact-goal** A* may approach without native backend if corridor physically exists; when entrance farther away, missing native ASPFP must not be treated as success; no marching straight into a building wall.
- [ ] Simulate native entrance path pending async for multiple ticks: pending/ready unconsumed entrance response is NOT discarded by another request straight to final upstairs goal. Goal switching midway cancels old plan safely.
- [ ] Place new locked door, actual wall, blocked stair, destroyed floor or barricade on recorded connector: composed route fails physical ground/collision revalidation and may reduce personal reuse confidence. An entirely different obstacle on **approach to entrance** must not falsely erase learned stair connector itself.
- [ ] Enemy starts heading upstairs then moves target to another room 15m beyond stair exit: this is **known unsupported** as a direct portal+upper-floor continuation. Avoid declaring success at landing or teleporting to new room. Remains NAV-ACCESS-01 / CORE-04.
- [ ] Reload save v6 on same survivor; route can be proposed again but must revalidate. Kill NPC and spawn replacement or place allied NPC of same faction: neither inherits exact stair entrance from the dead person's private memory without actually learning it.
- [ ] With 1, 4, 20, 40 NPCs attempting indoor pursuits, profile number/CPU of personal route lookups, async requests, revalidation stair probes, movement stalls, FPS and allocations. Capture runtime Player.log, map, XYZ and video.
- [ ] An unlocked native registered barricade door on approach/stair may be opened physically; a locked or phantom door never permits path/plan. Other group NPCs temporarily in doorway don't become solid lifelong block.
- [ ] Only after actual Unity compile and gameplay evidence may a narrow NAV-ACCESS-01 subfeature be closed. The broad room portal graph and multi-stage upper-storey planning remain **EM IMPLEMENTAÇÃO**; no claim of finished global urban navigation.


- [ ] Verificar `24ba6037`: NPC aprende trajeto que começa dentro de uma sala, passa 8–10m pelo corredor e então sobe escada. Ao procurar o acesso conhecido de outra sala, deve buscar **último ponto observado antes da subida**, não a posição inicial arbitrária daquela viagem. Nunca inventar um conector quando não existe variação de altura comprovada nos passos.


## 28. NAV-ACCESS-01 resumed — chaining observed stair with observed upper-room corridor (FINAL DEV BUILD ONLY)

- [ ] Same living NPC completes journey A from floor 1 through real staircase to landing on floor 2; separately completes journey B from near landing along hallway to upstairs room. Position this survivor in a different downstairs room with matching upper-room goal. Motor must physically approach remembered stair entrance, walk stair A and corridor B, then reach the actual goal. Intermediate entrance/landing never satisfies brain destination.
- [ ] Survivor has experienced ONLY stair A, but final upstairs goal is 5–15m from exit with no known second segment. It must **not** claim that a corridor exists or skip wall/door; try other valid navigation or keep goal pending.
- [ ] Add a wall or locked native door along the approach from the new downstairs room: no fake passage, and previous stair A and corridor B confidence must remain unchanged if the motor never reached their learned segments.
- [ ] Block the actual staircase only: reduce confidence of A, not B; block only upper-floor learned corridor: reduce confidence of B, not A. Re-check record selection after repeated failures.
- [ ] Goal changed or survivor falls and is safety-repositioned while following composed trip: clear both active route references, no penalties to future unrelated route, no recording teleport as genuine walked knowledge.
- [ ] Test same destinations in **reverse direction** without separate firsthand reverse trip: no inferred reversed stairs/corridors. NPC may use real native A* separately if available.
- [ ] Two learned corridors available from stair landing: only physically compatible and verified one may be selected. Changing map geometry after learning forces fresh segment validation; no invisible floor paths.
- [ ] Validate memory v6 save/reload of the SAME living NPC and full memory loss on death; newly generated substitute or ally does not inherit route segments. No magical sharing through group/radio.
- [ ] Native A* missing/pending: near entrance local route may still work, far entrance remains unresolved; no ping-pong between pending entrance request and final-goal request.
- [ ] Profile 1, 4, 20 and 40 active NPCs, recording extra raycasts / 104 max composed waypoints, time to find entrances, incorrect early ReportDestinationReached events, CPU, GC and logs. Source only until final user Dev Build/Player.log.

## 28. Snapshot integrity for learned stair/corridor paths (not yet tested)

- Save and reload a **living** NPC with valid firsthand stair + corridor memories; check paths persist and are still physics-validated before use.
- Load prior snapshot versions without route data; expect empty route memory, no invented accesses.
- With an intentionally corrupt test save (separate backup), verify first/last footprint mismatch, zero-length repeated steps, >2.6m skipped samples, invalid/NaN coordinate and observed route lengths outside 3.5–80m are ignored without crashes or bogus upstairs routing.
- Change door lock/geometry after an authentic save: learned paths are only candidates and must be rejected by real-world collision checks.
- Kill an NPC and spawn replacement: no inheritance of personal stair/room knowledge. Code commit `80f9104` was **not** compiled or runtime-tested; verify in final Unity Dev Build only.

## 29. Trade panel button visibility (source-reviewed; await Dev Build)

Open trading with 9 stocked items. Verify option buttons 1-9 are separately clickable, none overlaps `Fechar`, and selecting 7-9 selects the intended slot. Test both numpad/number row, Escape, changing dialogue state, lower screen resolutions and no HUD loss. Compare after closing with ordinary Unturned HUD. Commit `6173447` modifies footer geometry only: do not assume the broader dialogue/HUD bug is fixed until user feedback.

## 30. Near-entry known staircase handoff (source-only)

Teach the same living NPC an authentic stair route, then position it roughly 0.7m, 0.9m, 2.4m and 2.6m from the genuinely observed stair entry with an upstairs target. Verify it never loses access merely crossing 2.5m, never warps or clips, and successfully validates approach/steps/final XYZ where feasible. With a new locked door or blockage, remembered entry must not bypass collisions. Verify with optional native A* unavailable and multiple bots; collect log/fps evidence. Source fix `8457dc4`, runtime untested.

## 31. Multi-access / multi-room learned graph — FINAL DEV BUILD ONLY

**Classification:** NAV-ACCESS-01 bounded route-network source scope is QUASE PRONTA — AGUARDANDO BUILD. No Unity compile/playtest or profiler measurements have been performed for these commits. Use one living NPC and ensure routes were actually traversed and completed, not merely marked as destinations.

- [ ] **Same-floor multi-room:** NPC personally completes route A down corridor 1 and B from its actual exit through a native unlocked door to another room. Reposition in a third same-floor room within 25m of A start; objective is near B end and >5m away. Verify approach + both historical segments, physical door interaction, final 3D arrival, no door bypass.
- [ ] **Two different staircases:** in a connected multilevel environment, teach routes A (up first stair), B (connecting upper corridor), C (down second stair), D (lower corridor), all directions recorded first-hand. With objective near D end, verify four observed stages are chained, correct floor, no teleport/wall/timing skip.
- [ ] **Alternative access selection:** teach two independent ascents reaching comparable upstairs objectives. Block the initially selected staircase or its approach after learning; verify its entry is temporarily deprioritized and a physically accessible alternative is tried without erasing still-valid old routes. Remove blockage and verify route can be reconsidered.
- [ ] **No invented reverse direction:** teach up-only route, request down-only target without a corresponding observed down route. Must not treat upward footprints as a free reversed portal.
- [ ] **Unobserved gap between records:** two memorized corridors end/start ~1.5m apart but have a wall in between; graph may propose the edge, but physical validation MUST reject without clipping or reducing confidence in either old genuine journey.
- [ ] **Door and terrain changes:** save routes, then close/lock a native door or destroy a bridge/stair section. Route validation must fail closed; no forced unlocked-by-memory traversal.
- [ ] **Already at entry:** test NPC 0m, 0.3m, 0.7m, 0.9m, 2.4m from previously learned real access. None should be dropped merely for closeness; grounded/collision-checked first step required.
- [ ] **Async native provider:** with usable native A* validate approach >5m and <=25m, including pending callback not canceled by a request to final destination. With backend absent, do not cross walls or invent a far-entry path; nearby same-floor/vertical fallback still works.
- [ ] **Changing targets:** target moves to different room/floor during pending approach and during stage C. Old route indices/timers must clear without falsely marking destination reached or penalizing unrelated memory.
- [ ] **Memory/persistence:** same surviving NPC saves/reloads routes; replacement after death has empty first-hand route graph; nearby allies without actual physically delivered communication have no instant copy.
- [ ] **Population/performance:** 1/10/20/40 NPCs and active zombies near stairwell; inspect Player.log/Unity Profiler for raycast/ground validation spikes, GC allocations, native A* retry pressure, 0.12s shared admission, frame times, stuck counts and movement completion. Verify no starvation of low-priority NPCs or endless alternate-stair thrashing.
- [ ] **Compilation:** validate all new C# signatures/Unity API usage and preprocessor variants with actual project Dev Build. On approval mark NAV-ACCESS-01 CONCLUÍDA (runtime verificado) for stated scope, or reopen and attach exact log/map/repro scenario.

Related: `MOBILE_NPC_KNOWN_ACCESS_NAVIGATION_DESIGN.md` and permanent `MOBILE_NPC_PENDING_TASKS.md`. All tests unexecuted until the user consolidates final Dev Build.

NAV-ACCESS-01 additional async regression (commit `1f6ae7f`): while native A* has a pending entrance request, verify the NPC stops walking toward a blocking building wall, retains combat activity, does not falsely mark arrival, does not accumulate unwanted stuck recoveries and resumes once a physically valid route is accepted.

## 32. Final pre-jump navigation regressions — Dev Build pending

**Source verification only**. Scoped `NAV-UNKNOWN-01` and `NAV-NATIVE-01` are QUASE PRONTA — AGUARDANDO BUILD. `CORE-04` global nav and `NAV-STAIR-01` arbitrary complex stair geometry stay open.

- [ ] **Previously unknown corners:** player on opposite side of 1- and 2-corner house wall, no recorded route. NPC should stop choosing the same wall-adjacent blocked cell; after an actual stuck event (~1.5s) it prefers a reachable alternative frontier. It must not report destination reached until real XYZ matches.
- [ ] **Alternative entrance:** from a room with two doorways, lock the first native door. NPC may try genuinely walkable other approaches, but never opens the locked door, clips through walls, or invents a portal. If no real path/back-end, fail safely rather than claiming navigation solved.
- [ ] **Stale dead-end expiration:** remove a temporary barricade after 18 seconds, ensure stuck evidence expires and previously blocked route can become usable again. Changing tactical goal (>3m) must not bias navigation to unrelated destination.
- [ ] **Player/other NPC in doorway:** temporary body occupation should not permanently erase graph nodes, but physical CharacterController collision must continue to prevent overlap.
- [ ] **Floor continuity:** place small missing-floor segments between grounded grid points; no shortcut across the void. A different existing ramp/bridge must remain usable if its floor is actually continuous.
- [ ] **Narrow stairs:** real 0.625m or narrower sampled staircase landing vs original 1.25m graph, both directions. Verify no full-body/capsule intersections, no missing floor jumps, no multiple-floor snap. Profile double-pass cost.
- [ ] **Native A* field/property compatibility:** when genuine UnturnedPathfinding_ASPFP runtime exists, verify Seeker.StartPath callback with public `Path.error`, `Path.vectorPath` API, await/consume route and check final goal. When SDK has UnturnedPathfinding_Empty, expect unavailable diagnostics and safe fallback, not fictional global routing.
- [ ] **Native partial/stale safety:** no acceptance of wrong-floor endpoint (same XZ, different Y), changed goal, >128 waypoints, nonfinite vector, missing clip or first waypoint >4m from character. Confirm no crash or path spam after timeout.
- [ ] **20/40 active NPCs:** log global shared local plan admission (~0.10s), per-NPC route planning delays, A* pending, repeated unsuccessful routes and total frame CPU for new midpoint probes. Measure rather than assuming performance.
- [ ] **Cross-map:** test at least two maps, including enclosed POI, doors, steep curbs, stairs, water boundary and outdoor obstacle pursuit. If inaccessible indoor map without native graph still fails, keep CORE-04 OPEN with exact screenshot/log instead of marking full navigation complete.
- [ ] **Before jumping work:** record any new collision/stuck failures in backlog. NAV-JUMP-01 remains separate planned jump/vault/ladder work, not part of this source milestone.

Related commits: `7facf620`, `4968e145`, `d5cedb68`, `a6ce9d3b`, `8856f815`, `fde0be8e`, `36d647ef`.

NAV-NATIVE-01 regression `6e59544a`: on licensed ASPFP runtime inspect selected `Seeker.StartPath` reflection signature (exact three parameters), confirm graph mask is not accidentally set to zero by the adapter; verify callback receives a valid route. Absent backend must still fail safely with diagnostics.

## 33. Planned low-obstacle jump — ONLY final Dev Build (no runtime evidence yet)

- [ ] Stand near low static obstacle (~0.35–0.65m), target more than 1.45m ahead, ground within 0.25m elevation difference at landing. Verify full ballistic motion with `CharacterController.Move`, grounded completion and no source-level transform teleport.
- [ ] Thin railing/fence/wall too tall, solid low roof/ceiling, locked/unlocked native door, moving Player/NPC/Zombie, vehicle, and missing or sloped landing: verify static-only trigger and predicted swept capsule rejection, no fake jump/door bypass.
- [ ] Inured leg or crouched/listening NPC, water entry, change destination during leap, unexpected side collision and missed landing: verify abort, physics gravity, alternate route and no forced destination completion.
- [ ] Two/20/40 NPCs: inspect 0.10s shared probe rate, 0.75s per actor, low GC, cool-down and Unity Profiler physics cast counts; repeat on multiple maps.
- [ ] Save/reload after jumping: a completed ballistic arc must NOT be stored as ordinary walkable route in personal memory, nor reused as a floor-to-floor hallway.
- [ ] Verify whether runtime NPC_Client includes a true Jump/Leap animation (checked-in importer does not prove one), and that no airborne footstep sounds are triggered. Do not claim real vault/ladder animation in this subcase.
- [ ] Compile `MobileNPCHumanoidMotor.cs` + `MobileNPCVisualAnimation.cs` with game/Unity actual refs, inspect physics/contact logs and record user feedback. Mark only physical low-hop subitem concluded if approved; NAV-JUMP-01 parent remains open for off-mesh graph jumping, proper vault pose, ladders.

Technical design: `MOBILE_NPC_PLANNED_JUMP_DESIGN.md`. Commits `b92feff1`, `a1892748`, `04ccd8b0`, `03d4724a`, `b3c9080c`, `ba9c4b36`, `6571aaa5`.

NAV-JUMP-01 follow-up (commit `489ed764`): park a vehicle low in front of NPC and confirm it cannot be treated as static jump obstacle. With visible hostile targeting NPC and brain goal SeekCover or Retreat, verify jump is suppressed in favor of safer real movement. Other valid ordinary movement goals may still use preflight hop. Untested until final Dev Build.

## 34. Native ladder traversal — scoped nearby genuine Ladder (FINAL Dev Build)

**Test only in user's consolidated Unity Dev Build.** `NAV-LADDER-NEAR-01` source status QUASE PRONTA — AGUARDANDO BUILD, NO runtime evidence.

- [ ] Identify a real InteractableLadder map object with `Ladder` collider tag and `RayMasks.LADDER_INTERACT`. Place NPC on lower landing near authentic front. Give a target on confirmed upper platform. Record NPC positions every 0.2s, collider contact, physically grounded exit, real final XYZ arrival; no instant transform relocation or climbing a wall.
- [ ] Place NPC on upper landing and goal near bottom of same real ladder. Descend and confirm ground exit and no fall through map.
- [ ] Facing ladder from opposite legal side. Reject highly sloped/rotated/tagless colliders, normal solid walls and remote/midair starts. Check no ghost ladders created by memory.
- [ ] Locked native door, wall or low ceiling between character and the ladder approach or at upper exit. Must refuse or stop at real collision, never phase through.
- [ ] Map ladder with several tagged collider pieces, steep roof, offset exit and narrow landing. Record cases requiring next iteration. Source uses one continuous collider and a grounded destination close to its bounds; this is a scoped limitation, not a claim of generalized ladder navigation.
- [ ] Destroy/disable collider while climbing, reassign target to a different floor mid-climb, injure NPC, interrupt by entering water and block rung with another survivor. Ensure no teleport or animation stuck, cancel or fail closed and use native gravity/collision.
- [ ] Ensure NPC does not fire guns/punch, produce land-footstep zombie alerts, or record the vertical traversal as a normal personally walked same-floor corridor. On truly grounded exit, movement/pathfinding should resume to the remaining brain goal.
- [ ] The visual NPC_Client prefab must actually have `Move_Climb` or `Idle_Climb` loaded to display that state; if absent check one warning and keep physics safe. No invented jump/vault clip.
- [ ] 1, 10, 20, 40 NPCs competing for nearby ladders with Player/Zombie in the way. Inspect 0.18s shared entry budget, repeated retries, `Player.log`, Unity Profiler, collision, frame cost, no permanent crowd deadlock.
- [ ] Compile motor, new `MobileNPCLadderTraversal.cs`, visual, combat and ranged scripts; verify all Unity/Unturned names/signatures in live Dev Build. User feedback moves **only** the scoped `NAV-LADDER-NEAR-01` to CONCLUÍDA or reopens it with evidence; `NAV-LADDER-01` parent remains open for A* links and other layouts.

See `MOBILE_NPC_NATIVE_LADDER_DESIGN.md`, native `InteractableLadder.cs`, `PlayerStance.cs` and `PlayerMovement.cs`. None of these tests has been executed.

NAV-LADDER-NEAR-01 additional safety (`0aefbef9`): try to start climbing while physically crouched beneath a low ceiling or with severe leg injuries (MovementMultiplier <=0.5). It must reject initiation rather than crossing the ladder attachment in an invalid stance; test again after medically recovering/standing if headroom permits.

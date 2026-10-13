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

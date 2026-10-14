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

NAV-LADDER-NEAR-01 (`420b27ac`): approach a tagged ladder from a slight downhill slope/step; verify normal player-like gravity and floor collision during Approach, then controlled vertical climb only after reaching the verified ladder surface. No horizontal air-walking.

## 35. Directed native Ladder entrance — planned off-mesh approach (final Unity Dev Build)

Status: `NAV-LADDER-LINK-01` **QUASE PRONTA — AGUARDANDO BUILD**; no compilation/playtest here.

- [ ] Confirm the actual collider has a native `Ladder` tag and one valid face orientation. Place NPC 3–5m away on the true lower floor with upper-platform goal. NPC should inspect candidate, compute EXACT local A* waypoints to physical ladder entrance, walk around doorway/corner and only then attempt real climb with same live collider. Arrival at entry is NOT arrival at brain target.
- [ ] Place NPC >5m and <=11m on same ground floor. With ASPFP genuinely available, confirm native asynchronous A* entry path, pending wait without walking into building wall, eventual close-range Ladder revalidation and actual climb. With no ASPFP backend: no fabricated path; bounded fallback/open parent.
- [ ] Try a correct Ladder facing the wrong room, an obstructed entry door, another player blocking approach, fake Ladder-tagged angled collider, wrong floor, no top ground, deleted collider, and 128+ unrelated nearby objects. No phantom offmesh link, clipping or teleport, and no heavy unbounded overlap scans.
- [ ] Move final enemy/loot goal during async A* or midway to entrance. No previous native path consumed for a different destination; no plan to another floor from same XZ coordinate alone.
- [ ] Two authentic ladders: first blocked, second free. Check bounded re-attempt behavior; note that global optimal multi-ladder choice and chaining are STILL open parent requirements. Never overclaim.
- [ ] Run 1/10/20/40 NPCs in indoor POI; check bounded per-actor 1.5s and shared 0.18s survey, local/native A* occupancy, actual runtime CPU/GC and no path request starvation.
- [ ] Confirm compilation and `Player.log`. Regression: `5fd7cae1` class-scope C# statements were deleted. Check Unity import recognizes stable new .cs.meta GUID and dynamically added components.

## 36. User-approved standard Unturned-style vault jump — FINAL Dev Build

**Source-only status:** scoped historical `NAV-VAULT-CLAMBER-01` now means **jump over parapet**, QUASE PRONTA — AGUARDANDO BUILD. Parent `NAV-VAULT-01` OPEN for final multi-map/multi-hop coverage. No dedicated vault animation will be created or required. The previous artificial Rise/Across/Land clamber tests are **SUPERSEDED**.

- [ ] Compare an NPC and Unturned player at real colliding static obstacles of heights **0.65 / 0.75 / 0.90 / 1.02 / 1.10m**. The last height must be rejected by the bounded planning policy; below it, only jumps with a feasible parabolic apex/trajectory, open headroom and a real same-floor landing may start. Use native **JUMP=7** or bounded 25% Parkour-style boost **8.75**, with real gravity applied by CharacterController.
- [ ] Observe **one normal jump**, optionally forward movement delayed until clearance, and landing under native gravity; verify no scripted vertical climbing, floating hover, transform reposition, ladders’ `Move_Climb` pose or mandatory Vault clip.
- [ ] With TWO separated obstacles and real solid ground between them: verify **jump 1 -> confirmed ground landing -> cooldown/reprobe -> jump 2**. Deny a second impulse midair, an absent landing, unsafe jump chaining or crossing a building floor.
- [ ] Blocked wall top, insufficient apex, ceilings, solid wall taller than policy, native locked door, other NPC, Player, Zombie, rigidbody vehicle, missing/steep landing and water: no artificial vault, no geometry bypass, fail closed.
- [ ] Damage legs, make NPC crouch/sneak/ambush, force visible enemy cover/retreat, change destination while airborne or block the landing after preflight. Preserve combat safety, gravity and correct failed approach recovery; no false learned walk route across the wall.
- [ ] Inspect visual components: use an authored Jump/Leap clip ONLY if it is actually loaded; otherwise ordinary run/walk pose in air is acceptable per user's direction. `Move_Climb`/`Idle_Climb` remain only for a genuinely tagged Ladder. No airborne footsteps/zombie noise.
- [ ] Repeat with 1,10,20,40 NPCs, Unity physics collision stress and Profiler measured CPU/GC. Confirm the old three-phase custom clamber source/method calls are gone and actual build compiles; collect `Player.log`, map name, coordinates, reproduction for defects.
- [ ] Only after the user's runtime approval may scoped `NAV-VAULT-CLAMBER-01` be concluded; parent `CORE-04` remains open if broader navigation or routing still fails. Do not request/demand a dedicated Vault animation.

Related: updated canonical `MOBILE_NPC_VAULT_DESIGN.md`, `MOBILE_NPC_PENDING_TASKS.md`. All checks **UNEXECUTED** until consolidated Unity Dev Build.

**Section 33 correction after user-approved normal jumps (`b526f717`):** low 0.35–0.65m obstacle uses the actual native PlayerMovement JUMP impulse 7 rather than solving an often-too-low value from horizontal travel time. Check takeoff velocity, gravity-based airtime, real landing and bounded horizontal distance. Section 36 supersedes any older three-phase custom clamber scenarios: ordinary Unturned jumps only, optional Parkour-style 25% boost for higher parapets, next hop only after genuine ground contact and cooldown. No dedicated vault clip required.

## 37. Obstacle decision priority and native gravity — FINAL Dev Build, not executed

**Source status:** `NAV-OBSTACLE-CHOICE-01` QUASE PRONTA — AGUARDANDO BUILD. **Global CORE-04 remains EM IMPLEMENTAÇÃO.** No compilation/gameplay/Profiler evidence in this round.

- [ ] **Gravity regression P0**: compare a player and mobile NPC normal jump at level ground with standard Unity gravity. PlayerMovement uses `JUMP=7` and `Physics.gravity.y * 3`; NPC must no longer apply its old approximately 60m/s² fall acceleration from `gravity=20`. Confirm apex, flight time, knockback/falling and floor snapping on multiple maps. Check old serialized prefab YAML does NOT override the new named `gravityMultiplier=1f` field.
- [ ] **Short safe side corridor**: target on same floor beyond static low railing, short real path around of <= direct distance+3m or <=1.55×. NPC should use actual A*/detour/door, not jump over railing reflexively.
- [ ] **Long physically walkable detour vs truly safe jump**: target beyond low fixed obstruction with genuinely distant alternate route; NPC may propose low-hop or bounded wall jump ONLY if a full-body ballistic arc and real landing are valid. Verify brain destination remains exact and abandoned speculative ground path is cleared after physically approved jump, WITHOUT decrementing memory confidence.
- [ ] **Unknown partial A* frontier**: an adjacent cell near a building wall is not destination arrival. NPC must attempt real frontier walking and not leap on first sight; only after meaningful failed progression may an actual safe jump become an alternative.
- [ ] **Native door vs wall**: genuine unlocked InteractableDoor should open and permit walking; locked door, owner permission and blocked building entrance must NOT trigger a leap as a door bypass. Try walls adjacent to an unlocked door; walking to door is still possible.
- [ ] **Goal on different floor**: same XZ on another storey must never be treated as hop across plain solid wall. Real stairs/ladder and A* provider are separate mechanisms, no teleport.
- [ ] **Failed landing / moving barricade**: block physically valid landing after preflight, or force an actual side collision. Motor must stop special jump, keep normal gravity, record temporary blocked approach and refrain from repeated jumps for **6 seconds**; use ground recovery/alternative door/path.
- [ ] **Tactical movement**: SeekCover and Retreat should never voluntarily jump even while hostile temporarily leaves view. Listener, ambush, crouch, injured legs, swimming, ladder and target changing during async A* should likewise maintain action precedence.
- [ ] **Two obstacles**: first jump must terminate at real ground with cooldown, later next obstacle evaluated afresh. No airborne fake path added to MobileNPCTravelMemory and no inherited knowledge after death.
- [ ] **Parallel pressure**: compare 1/10/20/40 living NPCs + zombies near same blocked building. Check shared exact local A* comparison admission at ~0.22s, per-NPC comparison timeout 2.5s and existing local route throttles; inspect Unity Profiler CPU/GC, FPS, Player.log and collision logs. Confirm no path/fairness starvation.
- [ ] On success, mark **NAV-OBSTACLE-CHOICE-01** CONCLUÍDA (runtime verificado) only for demonstrated scope; otherwise revert to PENDENTE/EM IMPLEMENTAÇÃO with map, coordinates, screenshot, log and reproduction. Broad CORE-04 still requires actual map-wide route evidence.

Technical guide: `MOBILE_NPC_OBSTACLE_DECISION_DESIGN.md`. Source commits: `ce26eeb8`, `4c1a1bf5`, `06a16b5d`, `f1ac8162`.

## 38. ZMB-OPEN-01 — Existing off-nav target pursuit extended through 64m (FINAL Unity Dev Build)

**Source status QUASE PRONTA — AGUARDANDO BUILD only; parent ZMB-01 OPEN.** Code `2cf9669b42e7f4230963e48b403ac2a5033dcb1d`.

- [ ] With an authentic ALREADY ACQUIRED player target, follow on open, dry, gently sloping terrain beyond POI/nav mesh from 18→30→50m, no teleport, valid rotations/attack distance and physical 3m lookahead.
- [ ] Repeat with genuinely alive mobile NPC target (zombie must not detect a hidden NPC merely because it's within 64m).
- [ ] Put a fence/wall/house corner 2m away while target is 40m away: return to native seeker safely, no clip or fake long-range detour. At <=24m, the existing bounded ±55/±80 angle physical detour can still attempt a corner.
- [ ] A target beyond 64m or with >2.5m elevation difference does not gain an imaginary route. Long distance from same map across disconnected nav meshes remains an OPEN global navigation problem.
- [ ] An authentic POINT noise 30–50m outside a POI must not receive the new live-actor 64m traversal privilege; only previously acquired player/NPC target qualifies. Existing sound radius and audio occlusion require separate validation.
- [ ] Try river/deep water, 40° incline, cliff, narrow ledge and unloaded/safe map bounds; existing CanStepOnOffNavGround must reject movement without genuine floor.
- [ ] 1/20/50/200 active zombies crossing multiple boundaries; measure profiler raycasts, animation, stuck detector, pursuit persistence and server CPU. Multiplayer per-client region-interest tracking remains pending separately and should not be assumed solved.
- [ ] On final build pass, close only ZMB-OPEN-01 with runtime evidence; any failure reopens scoped item with Player.log/map/positions. Do not mark overall ZMB-01 completed until network region and distant obstacle routing are truly addressed.

## 39. COM-RADIO-PORTABLE-01 — simulated physical walkie traffic and player interception (FINAL Dev Build)

Source only — **QUASE PRONTA — AGUARDANDO BUILD**, no Unity compile, gameplay, audio, remote-network test. Parent COM-02 remains EM IMPLEMENTAÇÃO.

- [ ] Two living group members **each own native real walkie item 1445** in their respective MobileNPCInventory; trigger one server-verified player sighting by the hostile sender. Confirm sender broadcasts after preparation, recipient has a *weak approximate investigation clue* (not player Transform or wall-penetrating gunshot) and Player.log reports receiver count. Enemy reports expire after 24 seconds and max 4 queue items; 20-second sender preparation gap and shared 1.25-second airtime limit.
- [ ] Remove radio from sender inventory before cue, and then receiver inventory independently. No long-distance communication when either endpoint lacks real 1445. Same-group but unarmed communication still uses only preexisting short-range voice, never a radio fallback without hardware.
- [ ] Different group or faction, different assigned `GroupId`: no radio report delivery to rival group. Group 1 radio is native frequency **460328** (`DEFAULT_RADIO_FREQUENCY=460327 + 1`), dynamic if group identities differ. No inferred channel match from proximity alone.
- [ ] Local singleplayer player with **earpiece ID 1446 actually worn as mask** and tuned to 460328 sees a ~5-second `[RÁDIO 460328]` Portuguese subtitle. Repeat with **walkie ID 1445 actually equipped**. This is synthetic text, NOT recorded voice/audio or fake native chat message. Wrong frequency, unequipped walkie sitting in inventory, mask other than earpiece, dead player: no subtitle.
- [ ] Remove mask or change frequency **during** active subtitle: it disappears immediately. Returning to frequency without a new transmission should not reveal queued secret history.
- [ ] Use other UI menus (NPC interaction/trade, ESC, standard HUD). Radio subtitle must not grab focus, hide HUD, change cursor or interfere with native voice.
- [ ] Kill NPC sender during queued pending transmission, destroy/deactivate recipient, change group while message pending, reload map. No undead radio emission, no stale active registry or cross-map leakage; group and channel must update from living objects.
- [ ] 1/10/20/40 mobile NPCs with 1445 distributed through real loot, do not autogive items to whole population outside testing; profile Update/radio transmissions/GC. Diagnostic at most one radio info log every 10 seconds while transmissions exist.
- [ ] Dedicated server/multiplayer: do NOT expect subtitles on remote clients or native microphone voice; mark this part of COM-02 still EM IMPLEMENTAÇÃO and collect details for future network protocol.
- [ ] User approval after build can close **only COM-RADIO-PORTABLE-01**, not COM-02, COM-03 or COM-04; defects reopen with Player.log, group IDs, tuned frequency, physical items, screen recording and reproduction.

Source: `MobileNPCRadioOutbox.cs`, `MobileNPCRadioInterceptHUD.cs`, `MobileNPCPopulationBootstrap.cs`, `PlayerVoice.cs`, `PlayerQuests.cs`, `UseableWalkieTalkie.cs`; design `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md`.

**Section 39 real loot acquisition (`a3ea3d67`):** place one genuine world walkie ID 1445 within the loot scan radius of a living, radio-less grouped NPC. Confirm the usual ItemManager pickup/inventory transaction is completed before HasOwnedPortableRadio becomes true; a duplicate radio is deprioritized, and first gun/urgent healing still outranks the walkie. No free 1445 at spawn and no radio transmission before actual acquisition.

COM-RADIO-PORTABLE-01 follow-up (`f84f390f`): rapid tune 460328 -> other channel -> 460328 within a few seconds, or temporarily remove/re-equip earpiece. Previously heard message must NOT reappear; only a new fresh transmission may show new text.


### Section 39 addendum — 2026-10-13 COM-02 static review, queued-message integrity and COM-04 scope

**No tests below have been executed.** Commit `9a500877` is **source-only**, not compiled or verified in Unity. Execute after user's final consolidated Dev Build. Keep `COM-RADIO-PORTABLE-01` **QUASE PRONTA — AGUARDANDO BUILD** until the user supplies evidence; preserve `COM-02` as **EM IMPLEMENTAÇÃO**.

- [ ] **Radio source and equipment:** 1445 must first exist as a real world drop. NPC has to acquire it via the normal ItemManager inventory transaction. Two living **same-group** NPCs each need one in inventory. Player holding unequipped 1445 in backpack must hear nothing; player equipping it or wearing 1446 as mask and tuning correctly can see a **fresh** subtitle. Do not infer that a radio is in an NPC's visible hand: this has not been implemented.
- [ ] **Frequency correctness:** inspect two distinct living group IDs, each with its own assigned `460327 + runtimeGroupId` frequency. Tune player to group A and trigger only group B: no intercept. Switch to B and repeat: receives fresh B message; group A NPCs never receive B's contact. Check IDs after save/reload: runtime group IDs may be remapped, so discover the current channel rather than assuming it persists.
- [ ] **Queue stale-group regression:** acquire 1445, generate a genuine hostile sighting, and before its queued report transmits reassign the same living NPC to another valid group/faction (or change the faction under a controlled test), then generate a fresh observation after cooldown. The old report must be purged, must not go out under the new frequency, and must not obstruct the new report until its old TTL expires.
- [ ] **Radio removed/destroyed/death:** removing item 1445 from sender or receiver makes that endpoint ineligible; source dying before send produces no transmission; reviving replacement NPC has no automatically inherited radio; destroy radio NPC objects and change maps without leaks, stuck static registry entries or stale player subtitles.
- [ ] **Source truth vs receiver behavior:** verify `EnemyContact` from physical server sighting causes ONLY an approximate, low-confidence investigate clue to eligible receiving brains, not immediate LOS/gunfire/omniscient tracking. A real landed nonempty airdrop within 28m may enqueue a player-intercepted `Airdrop` subtitle; a crate 28–220m away must not. **Do not mark distant NPCs as successfully navigating to the airdrop:** no airdrop decision consumer exists yet. This is a COM-04 test failure/remaining work, not something to waive when the player sees text.
- [ ] **No hallucinated features:** text is not native voice chat audio, walkie has no invented battery use, NPCs do not visibly equip it, fixed base radio and vehicle radio have not been implemented, and remote multiplayer clients do not receive radio subtitle packets.
- [ ] **Performance/diagnostics:** at 1, 10, 20, 40 NPCs verify the 10-second bounded log, 20-second preparation throttle, 8-second per-NPC interval, 1.25-second global airtime, <=4 pending reports, 24s contact TTL and 45s airdrop TTL; test radio receive after rapid radio/frequency toggle. Collect Player.log + map + reproduction + screenshots + FPS/GC observations.

**Acceptance rule:** approve and close only the tested source-scoped `COM-RADIO-PORTABLE-01` after passing the Dev Build; otherwise reopen it with exact evidence. Parent `COM-02` and all unwired COM-03/COM-04 functions remain open regardless. If no convenient in-game test controls exist for faction/group reassignment, record that regression as **not yet testable** rather than claiming a pass.


## 40. COM-BASE-PHYSICAL-01 — fixed improvised powered base radio (FINAL Dev Build)

**Source only — QUASE PRONTA — AGUARDANDO BUILD. NO Unity compilation, in-game/equipment/power testing or native voice/audio validation has occurred.** Parent COM-03 remains EM IMPLEMENTAÇÃO. Requires a native central storage, real ID 1445, nearby live operator and electrical power. Test radio-only requirements independently of future NPC refuel/toggle; automatic generator operation is COM-BASE-POWER-02, **still pending**.

- [ ] **Real setup:** In a map with no global electricity, build or restore an NPC base with genuine native `InteractableStorage` owned by nil owner/group at current group anchor; put one **actual** ID 1445 into storage. Place a native real `ItemGeneratorAsset` using NPC inventory/build path or existing legitimate world construction inside its wiring radius. Do not use a decorative prop, arbitrary fake ID or plugin-imagined radio asset. Examine whether generator item state (power flag/fuel byte data) survives NPC placement and map reload.
- [ ] **Power OFF vs ON:** With an NPC physically within 3m of station, verify zero-fuel generator -> zero base transmissions, turned-off fueled generator -> zero base transmissions; ON fueled generator within native wire radius -> eligible transmission. Walkie taken out of storage -> immediately no station link. Deleting generator, exhausting fuel or powering off -> next radio send/receive is rejected. No invented battery drain is applied to separately owned vanilla portable 1445.
- [ ] **Operator proximity:** Remove NPC's personal radio; while stationary within 3m of storage, trigger an actual visible player contact and verify new text goes to remote same-group NPC with its own walkie, never to a different group. Repeat >3m away: no base-originated transmission. Remote NPC without 1445 and not at its own powered station receives nothing. EnemyContact is only a weak approximate clue, not authorization for firing without LOS.
- [ ] **Receiving at base:** Remote same-group NPC with real 1445 transmits a genuine contact; a living NPC within 3m of the powered station can receive with no personal radio; NPC outside radius cannot. Confirm on-air `source=powered-base` and `base-station receivers` in the bounded `[MobileNPCRadioOutbox]` diagnostic (max once/10s when messages sent).
- [ ] **Player interception:** When equipped 1445 or wearing 1446, player tuned to current group `460327+runtimeGroupId` hears **text subtitle only** from base transmissions. With mismatched frequency, unequipped 1445, no mask earpiece, player death or transmitter without power: no subtitle. Radio subtitles must never take UI focus, hide ESC or steal NPC dialogue options.
- [ ] **Real construction supply:** If group carries one genuine `ItemGeneratorAsset`, the fixed builder pattern reserves up to ONE of its four perimeter slots for that item and consumes it on successful native placement. Verify it does not magically spawn fuel, change existing max 5-piece pattern, or place a second generator if a nearby real one is already recorded/restored. If no physical generator is carried, nothing is spawned.
- [ ] **Save/map/group:** Switch between maps or reload a saved base, confirm native structure, stored walkie, fuel/power state and nearby settlement anchor reconcile. Kill group leader with surviving allies: elected successor should recover the actual central storage, not another unrelated player's container. When last member dies, radio sender/receiver endpoints must stop; replacement group must not inherit a lost group ID or phantom station.
- [ ] **Power map special case:** In a map configured with native `Has_Global_Electricity`, station may operate without generator, but STILL must have real 1445 stored, valid group, genuine central storage and close NPC operator. This is the verified native map rule, not a free battery/station.
- [ ] **Guardrail and perf:** One/10/20/40 NPCs, varying several real stations, real 1445s and native generator on/off states. No `FindObjectsOfType` scan per frame/radio transmission; the builder's reconciliation scan should run at most once per leader/site. Collect Player.log and FPS/GC measurements. Test after removing walkie/fuel during a queued report, and before/after queued-message group change.

**Known unimplemented behavior (do not mark PASS):** NPC autonomous refueling (real canister/fuel transfers), switching generators by reaching them/using native interaction, radio hand animation/voice sound, real base-radio asset and client replication, cross-group communications, choosing a station as a travel objective, reliable long-term ownership IDs for stored nil-owner radios. Treat these as COM-BASE-POWER-02/COM-03/COM-02 follow-up. Successful scoped tests can close **COM-BASE-PHYSICAL-01 only**; failures return it to pending with build/map/steps. See `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md` latest section.


## 41. COM-BASE-POWER-NEAR-01 — real native fuel and generator activation (FINAL Dev Build)

**Source-only / QUASE PRONTA — AGUARDANDO BUILD**, not compiled or executed. This section supersedes section 40's older claim that ALL generator activation/refueling is unimplemented. The physical near-operator subcase now exists in `MobileNPCBaseGeneratorOperator.cs`; the autonomous travel/strategic fuel supply and deliberate shutdown still do NOT exist.

- [ ] **First build/import:** verify `MobileNPCBaseGeneratorOperator.cs` and its unique `.meta` import correctly; `MobileNPCPopulationBootstrap` creates only one per NPC. Check for C# errors on `BarricadeDrop.FindByTransformFastMaybeNull`, `BarricadeData.owner/group`, `Items.updateState/removeItem`, native `ItemFuelAsset` and generator power APIs.
- [ ] **Real generator ownership:** server-built generator is registered under `BarricadeManager` with real native `ItemGeneratorAsset` and nil owner/group, within <=6m of group anchor. Another player's owned generator close to the base must NOT be operated; off-map/vehicle generator and unrelated group must never be touched. Check selection after leader death (survivor promoted), save/reload and map change.
- [ ] **Real canister state:** use a genuine `ItemFuelAsset` (fuel state is native ushort in 2 bytes) with 0, partial, full volume and `shouldDeleteAfterFillingTarget` true/false variants. With no actual fuel, generator stays empty; using filled fuel from real group `InteractableStorage.Items` decrements stored state and updates UI/save; using a personal NPC fuel tool decrements only that inventory instance. If partially used, the canister persists with correct remaining units; if empty, delete it only when asset native rule says so. No duplication after NPC death or save/load.
- [ ] **Fuel/energy accounting:** place generator OFF and EMPTY. Put a real partially filled fuel canister in group storage; NPC alive and free within 3.3m, visually accessible to both container and generator, waits until its 12s action cadence: `generator.fuel` increases by EXACT units deducted from source. `BarricadeManager.sendFuel` and `ServerSetGeneratorPowered` must update live native state and world serialization. After turning ON, the powered base radio from section 40 activates only when real ID 1445 remains stored and a radio operator within 3m exists. Do not expect instant operation the frame a fuel item arrives.
- [ ] **Bounds/safety:** generator >=50% filled is not topped up; <=50% may fill up to capacity, never overfill. Empty canister, missing registered native `BarricadeDrop`, dead/hostile-to-group NPC, NPC in combat/retreat, inaccessible machinery, 3.4m away, locked wall between operator and generator or container: no change. Toggling power from OFF to ON without fuel must fail.
- [ ] **Multiple operators:** 2–4 same-group NPCs around one generator with a limited canister supply: combined fuel gained equals exact total units removed from one or more real canisters, no duplicated refill/activation, no over-capacity, no spam. Different group, dissolved group, new respawn NPC must not inherit a private canister or unauthorized station power.
- [ ] **World/save/race:** refuel, check fuel/canister in game, manually save, reload, confirm generator powered and remaining native fuel and shared inventory states. Remove generator while nearby, disable base storage, change map, kill leader, switch groups mid-action; never operate stale references or lose fuel to orphan objects. Profile 1, 20 and 40 NPCs + zombies and monitor `Player.log`, GC, FPS and `[MobileNPCBaseGeneratorOperator]` diagnostics (action logs only).
- [ ] **Global electricity:** on map with native `Has_Global_Electricity`, base station can work without generator activation. Maintenance operator should not unnecessarily spend fuel. Outside a powered map, no real fuel means no power and therefore no base-radio station.
- [ ] **Remaining unimplemented cases:** actively choose to switch OFF to conserve resources, walking/pathfinding to generator from another room, deliberate acquiring fuel from a POI, claiming a unique persistent base/generator ID, and visual hand/equipment/animations. These keep `COM-BASE-POWER-02` and full `COM-03` OPEN and cannot be marked PASS by the scoped test.

**Lifecycle:** User's final consolidated Dev Build + positive feedback allows closure of `COM-BASE-POWER-NEAR-01` ONLY. Compile issues or failed tests return to `EM IMPLEMENTAÇÃO` with exact map, Player.log, fuel quantity before/after, screenshot and repro. Other COM, zombie, navigation and combat tasks remain in canonical backlog.


## 42. COM-AIRDROP-RADIO-01 — actionable uncertain radio airdrop reports (FINAL Dev Build)

**Source-only: QUASE PRONTA — AGUARDANDO BUILD; no Unity compilation/gameplay testing performed.** Parent `COM-04` remains `EM IMPLEMENTAÇÃO`. Adds a memory/decision consumer to the previously implemented real local `CarepackageDestroy` report and changes the previous direct 220m airdrop detection to **28m with LOS**. COM-02/COM-03 equipment and radio constraints remain prerequisite and independently testable.

- [ ] **Compiler/import:** `MobileNPCAirdropRumor.cs` plus unique `.meta` import, one component per cloned NPC via `MobileNPCPopulationBootstrap`; the existing `MobileNPCRadioOutbox` calls `ReceiveAirdropReportServer`, `MobileNPCAirdropContest` calls `TryInvestigateConfirmedCrateServer`. Compile original native U3 SDK and record C# errors if any, do not mark TESTED because methods exist.
- [ ] **First-hand evidence and 220m regression:** Place genuine vanilla landed `CarepackageDestroy` storage containing an item 120m away, then at 30m and 27m. An idle NPC without radio must NOT know distant exact crate / start loot beyond 28m. At 27m and unobstructed LOS, a real radio-equipped NPC can enqueue report; another NPC with its own real radio may receive it. A room wall, fence or solid structure between NPC eyes and crate at 10m must block discovery and broadcast. Non-carepackage vanilla storage and already emptied/destroyed crate cannot trigger report.
- [ ] **Real equipment and delivery:** Source and listener need a real owned 1445 or valid powered base station with true stored 1445, same live group, faction and exact virtual group channel; player must separately meet real 1445-equipped/1446-worn, matching tune requirements to intercept TEXT only. Different group/faction/frequency, disabled radio, sender killed before queued transmit or receiver death: no useable report or investigation. No fake native voice.
- [ ] **Uncertain navigation:** Radio listener 20–100m away, alive, with inventory space, no player target/other action, receives a REAL airdrop report and begins a normal `Goal.Investigate`, NOT `Goal.Loot` or instant `CarepackageDestroy` reference. Inspect the intended point: coords rounded to 20m plus an independent 8m per-axis offset, safe-ground check and existing motor/route validation. No teleport, infinite-range path, automatic item IDs, knowledge or gunfire. Verify it can find an alternative doorway if pathfinder can, does not walk through solid geometry; if path fails, it gives up.
- [ ] **Sight-based promotion to loot:** While the listener follows its uncertain investigate point, allow it to see the genuine landed nonempty crate within 28m. It should transition from `Investigate` to `Loot` ONLY after LOS; must approach within native loot distance 3.5m and physically transfer real `Items` to its own `MobileNPCInventory`. If another NPC/player removes items first or the box despawns, the listener gives up without creating any loot. Multiple eligible NPCs compete for one actual finite crate without duplicates.
- [ ] **No omniscience / walls:** Verify crate 10–20m away behind a wall remains invisible even if in the reported radio sector. An exposed crate 10–20m away can be found when physically visible. Source pickup does not magically mark receivers' inventories as containing a crate's items. Hearing the same rumor with full inventory gives no mission. Test small houses, floors, stairs, rubble and crates near obstructions.
- [ ] **Expiry and cooldown:** 45s radio report TTL remains bound on receiver memory; a report already stale cannot cause travel, nor can a rumor outlive its originating group/faction or reset repeatedly with every similar message. At the approximate destination, if no crate is verified after 8s, give up and wait 95s before revisiting the same sector. When the receiver is shot, pursues player, follows companion, changes group, dies, or radio mode is disabled, optional investigation cancels but no unrelated combat or follow goal is cleared.
- [ ] **Performance/maps:** 1/10/20/40 NPCs with numerous native normal storage containers, multiple airdrops, walls, separate factions and generator-powered groups. The server still scans the scene every 2s, but now only physics-checks LOS on real within-28m crate candidates. Measure FPS, GC allocations, scan cost and `[MobileNPCAirdropRumor]` logs; test spawning/despawning across map transitions, group death, re-election of leader and reload without stale static references.
- [ ] **Gameplay config:** turn `MobileNPCSettings.AirdropContestEnabled` false during a trip and confirm listener cancels its rumor and the real-crate scanner stops; enable again and verify only NEW fresh reports are eligible, not expired retained intel.
- [ ] **Remaining nonimplemented COM-04 types:** no automatic reinforcement squads, real useful-loot tips, unknown-base observations or navigation to those messages. Do not mark COM-04 complete from a successful airdrop demo, nor imply NPC facing/FOV visual animation, live radio voice or multiplayer state replication.

**Approval:** only after final Dev Build with above explicit evidence can `COM-AIRDROP-RADIO-01` be marked `CONCLUÍDA (runtime verificado)`. Otherwise regress it to pending/implementing and preserve code, test map, exact distance, radio equipment, screenshots, Player.log and before/after container Items count. Full parent `COM-04` remains open.


## 43. COM-REINFORCEMENTS-RADIO-01 — real wounded-combat request and volunteer rally (FINAL Dev Build)

**Source-only, QUASE PRONTA — AGUARDANDO BUILD; Unity C# NOT compiled or tested yet.** Requires the existing physical native radio group/channel, actual damage and real source LOS. No free NPC recruitment or instant enemy targeting; parent `COM-04` remains IN DEVELOPMENT.

- [ ] **Compilation and Unity template:** `MobileNPCReinforcementResponse.cs` and unique `.meta` import; `MobileNPCPopulationBootstrap` attaches it only once per mobile NPC. Confirm new trailing `MobileNPCTactics.Goal.Reinforce`, outbox message sender/receiver and `MobileNPCBrainDriver.TryStartRadioReinforcementServer` compile and preserve previous enums/animations. Collect compiler errors instead of assuming.
- [ ] **Actual radio + visible damaging enemy:** Put 2–4 living NPCs in one group with genuine 1445s or properly powered physical base station and matching group frequency. Let hostile player or hostile NPC (or zombie) VISIBLY injure one NPC to <=90% HP, and keep enemy currently visible. After radio prep, sender may queue a single `Kind.NeedReinforcements` call carrying its own position, never shooter position. Verify same under attack from long range, body damage and low health; NO call for unhurt player, old damage >7s, wall-hidden enemy, false sound, idle ambient gunshots or hostile role's neutral friend.
- [ ] **Real destination, no extra spawn:** Place healthy, armed, loaded ally 25–70m away, idle, on radio. At most ONE existing ally receives optional help task per transmission. It should wait 1.25–3.25s, plan valid safe ground near 15m sector approximating the injured caller (+/-6m), use `Goal.Reinforce` in existing path/motor, cross actual doors/rooms if supported, never teleport or receive the hostile transform. Observe ordinary vision on arrival: can fight ONLY if it independently detects the enemy; otherwise hold a short window and abandon.
- [ ] **Eligibility and refusal:** Ally without 1445/valid powered base, wrong group/faction/channel, dead, <=54% health, no selected usable gun, fewer than 3 loaded rounds, reloading, active combat target, currently following someone, running another tactical objective, or >90m from caller: does not volunteer. An ally 10m away may already hear natural calls; this specific long-distance response should not recruit it. Compare preference with a healthy 30m ally and a wounded 60m ally.
- [ ] **Busy/damage cancellation:** While traveling, wound helper below 55%, exhaust ammo, kill it, change group/faction, give it an actually perceived combat target, overwrite its tactical goal, block its route or let message TTL 48s lapse: radio rally cancels without overwriting defensive cover, retreat or real player pursuit. No stuck `Goal.Reinforce` after deadline or a route failure. Check in walls, high stairs, different floors and 1–40 NPC workloads.
- [ ] **Spam resistance and duplicates:** Many repeated visual perception callbacks plus authentic damage across 10 seconds must not produce one call per frame. Confirm 80s sender request cooldown + 20s shared report prep and 8s personal/1.25s global airtime. Helpers have 80s individual reconsider cooldown; only one helper per transmission, regardless of having 3 listeners or several simultaneous calls from wounded same-group NPCs. Calls that cannot find safe physical rendezvous do not invent a path or teleport.
- [ ] **Equipment/interception:** Player with real equipped walkie 1445 or worn earpiece 1446 tuned to group's current `460327 + runtimeGroupId` receives ONLY simulated text intercept. Wrong tune/no equipped native device receives nothing; radio should never remove keyboard/HUD/ESC interaction. Turn OFF group base generator/remove stored walkie: base receiver or sender becomes ineligible immediately. Personal portable radios continue independently.
- [ ] **Persistence/session:** Kill sender before queued call; ensure no broadcast from dead NPC and no stale receiver target. After map switch or save/reload, no lingering global requests or phantom responding NPCs. Dissolved group must not spawn/recruit ghost squad; new replacement NPC cannot inherit deceased individual's help assignment. Verify old combat, airdrop, local shout and group coordination still function independently.
- [ ] **Profiling and concrete logs:** 1, 10, 20, 40 NPCs under actual firefight, different groups, varying real gun ammo and powered radios. Monitor `[MobileNPCRadioOutbox]` for bounded text/broadcast and `[MobileNPCReinforcementResponse]` for one accepted physical volunteer, along with FPS/GC/Player.log. Evaluate whether 48s TTL / 52s goal deadline is sufficient for route complexity rather than extending blindly.
- [ ] **Not this feature:** faction-level recruitment across multiple `GroupId`s, radio conversations between unrelated squads, emergency call without visible live source, requesting helicopters/vehicles, group-wide tactical flanking, military command AI, firing at remotely known targets, or new NPC spawn. These remain explicit COM-04 / GRP / VEH pending items, not implicit pass criteria.

**Approval rule:** Only after user consolidated Dev Build and positive feedback can the narrowly defined `COM-REINFORCEMENTS-RADIO-01` be marked `CONCLUÍDA (runtime verificado)`. Otherwise reopen it with exact logs, group/radio frequency, wounded role, range, actual ammo, map and reproduction steps. The `COM-04` parent stays OPEN.


## 44. COM-USEFUL-LOOT-RADIO-01 — honest observed world loot, radio tip and conditional physical pickup (FINAL Dev Build)

**SOURCE ONLY — QUASE PRONTA — AGUARDANDO BUILD.** No C# compile, Unity simulation, native ItemManager transaction test or FPS/GC benchmark occurred. Parent COM-04 is still open. Confirm with console/Player.log screenshots and actual item instance counts, not merely hearing text.

- [ ] **Import/build:** `MobileNPCUsefulLootRumor.cs` and unique .meta import once via runtime `MobileNPCPopulationBootstrap`. `MobileNPCSupplyKnowledge.GetCurrentRadioItemNeedServer`, `MobileNPCRadioMessage.TryCreateUsefulLoot`, `MobileNPCRadioOutbox.TryQueueObservedUsefulLootServer`, `MobileNPCLooting` runtime component discovery and matching world-item path all compile cleanly in Unity; inspect collision mask, `ItemAsset.id`/name, `ItemManager.TryGetWorldItemInstanceID`, and inventory snapshots.
- [ ] **Genuine sighting vs wall:** Drop ACTUAL ordinary Unturned medicine, gun, compatible nonempty magazine, water, food and walkie 1445 at 4m, 12m, 16.5m, 18m and behind a solid 5m wall. Sender needs 1445 personally or physically active powered base, alive/current GroupId. Only physically unobstructed <=17m registered world drops produce `Kind.UsefulLoot`; not an unopened crate, object decoration, item spawn-table prediction, ordinary storage inventory or distant 100m player-known item. Pick up/destroy item before its radio transmission, then ensure hearsay does not conjure anything at receiver.
- [ ] **Radio authentication/intercept:** Two genuine same-group/faction NPCs, each with portable 1445 or operational powered station, using `460327 + runtimeGroupId`; correct receiver gets optional tip about item type and rough location. A third NPC without equipment, wrong group/faction/channel or dead gets nothing. Player properly equipped 1445 or wearing 1446 and matching frequency sees ONLY text; without correct native gear/tune sees nothing. Do not claim voice or animation.
- [ ] **Personal need:** Wounded NPC with no medicine should value medicine; thirsty NPC with no water values water; hungry NPC values food; NPC with no gun values gun; a low-round loaded gun with no reserve values a *compatible* magazine but not incompatible caliber. Healthy/fed/hydrated/armed NPC or one with sufficient supplies REFUSES irrelevant tip. Full inventory refuses all. A base-station operator without personal 1445 may value actual 1445 and search for it. NPC never knows exact instance ID from radio.
- [ ] **Uncertain trip:** Receiver 20–90m away with valid need/room, free brain and radio begins normal `Goal.Investigate` to region rounded 15m + <=6m random noise, not direct perfect item coordinates. Validate optional native A* and local A* through houses/doors/other floors with no teleports, wall penetration or perfect tracking; reject unsafe ground and >100m; bleeding NPC does not travel >55m. Regular fight, following companions, serious wounds and other tactical goals outrank speculative loot.
- [ ] **Arrival and local re-observation:** Put requested item inside actual reported region, a similarly named but different item next door, and another ID elsewhere. Only a physically present, matching **same item ID**, ItemManager-registered, positive amount, <=17m LOS drop and <=34m from radio sector can promote the active radio `Investigate` to normal `Loot`. Existing `MobileNPCLooting` must approach within pickup range and use real `TryPickupWorldItemServer`; world item quantity goes down exactly once, recipient inventory receives that one item, `MobileNPCSupplyKnowledge.RecordSuccessfulPickupServer` only runs AFTER actual transfer. No free-loot injection and no skipping player/other-NPC contention.
- [ ] **Motor arrival race:** Test motor clearing `Goal.Investigate` at the physical destination before the rumor's 2s `Update`. The recipient should still be allowed <=8s of local scan to verify the item, and should not accidentally pick up irrelevant items under the rumor. Remove item while approaching, block path with door/wall or let TTL expire: abort rumor and leave any other unrelated goal intact. Timed-out/blocked travel must not issue repeated requests at every frame.
- [ ] **Spam/group/clock:** Sender shares 20s global preparation and 4-report queue, 8s personal and 1.25s global radio airtime; same type+nearby position is suppressed for 95s. Receiver stores at most one active trip; expires <=62s, same item+same sector suppressed for 110s after resolution, no permanent remote item map. Group reassignment, faction changes, death, reload, map switch and radio reset clear or invalidate stale knowledge. A replacement NPC after death inherits no old rumor or item.
- [ ] **Combat interactions:** Start radio loot trip, then give NPC an actually detected enemy, incoming fire and need to retreat, or an urgent medical demand change. Optional loot travel must stop without turning into infinite investigation and without overwriting cover/retreat. A hostile player watching can steal the source item and NPC must not magically find a replacement.
- [ ] **Performance:** 1/10/20/40 NPCs, dense map 50+ dropped world items, multiple radios/groups, stairs, walls, cargo-full NPCs, simultaneous airdrop/NeedReinforcements messages. Measure real `Player.log`, FPS, GC, log cadence, and world item count before/after. Ensure per-NPC 2.5s scans + local LOS raycasts do not cause large frame spikes.
- [ ] **Not complete:** unknown base ownership reports, inter-group/faction broadcasts, player-operable native base radio UI, full audio/animations, vehicle radio, tool durability/crafting chains or resource trading. These keep COM-04/COM-03/VEH open.

**Closure rule:** After USER final Dev Build only, positive evidence closes **COM-USEFUL-LOOT-RADIO-01**; runtime errors or source regressions reopen it with exact map, item ID and ammo/health need, radio equipment, group, physical position, Player.log and transfer count. Never close parent COM-04 based on this single subtask.

**Section 44 addendum (2026-10-14):** Test a reporter with ALL personal inventory slots occupied, a genuine equipped 1445 (or valid physically operated base station), no ongoing tactical objective, and a real valuable nearby world drop; it should still broadcast a truthful <=17m LOS UsefulLoot tip without ever attempting `TryPickupWorldItemServer` locally. Remove the radio, hide item behind a wall or put it at 18m: no tip. The tuned player's subtitle should use native `ItemAsset.itemName` and not a Unity internal asset identifier. Commit `5bd28b14`, `bf38eec9`; no build was run.


## 45. COM-UNKNOWN-BASE-RADIO-01 — Native owned player structures + cautious radio scouting (FINAL Dev Build)

**QUASE PRONTA — AGUARDANDO BUILD (C# source only).** This narrow subcase considers ONLY structures with actual `StructureDrop` registration, `StructureData.owner != 0`, two distinct instanceIDs and matching owner within 14m, detected by NPC in <=25m with unobstructed LOS. It does NOT identify ownerless NPC bases or authorize combat.

- [ ] Compile/import `MobileNPCUnknownBaseIntel.cs` and matching .meta, check `StructureRefComponent`, `StructureDrop.GetServersideData`, `StructureData.owner`, `Physics.OverlapSphereNonAlloc`, `RayMasks.BLOCK_COLLISION`, and registration once per mobile NPC in bootstrap.
- [ ] Place one player structure, then two related player structures with the SAME real Steam owner <=14m apart, within 25m and clear LOS. Only the two-piece site should produce a report. Test two distinct owners, destroyed structures, decorative map houses and editor world buildings: they should NOT trigger. Test behind closed walls, indoor/outdoor, corners and vertical structures. `owner=0` must not be fabricated as hostile player base.
- [ ] Send `Kind.UnknownBase` from a living NPC with owned 1445 or powered-group-base endpoint, to correctly tuned living groupmate of SAME `GroupId`/faction (and player with correctly tuned native 1445/1446). Wrong equipment/frequency/group/faction should not receive actionable intel. Repeat site cooldown ~160s, radio TTL <=85s.
- [ ] Recipient 35–115m away, idle, not fighting, gets rough location rounded to 20m and attempts ordinary `Goal.Investigate` to a safe-ground point roughly 22m outside the reported center; never enters the structure, never acquires a shooter target or tries to open locked doors from only hearing the report. Actual motor path may reject it; no teleport. If current task/companion combat overrides, expire/cancel own investigation only; revisit same sector suppressed ~150s.
- [ ] Different factions, owner=0 settlements, empty/despawned/destroyed base, group dissolution, disabled radio station, 1/20/40 NPCs and 100+ nearby player structures. Profile 5–6s scanner cadence/96-collider overflow, LOS masks, structures with several child colliders, fps/GC and native C# errors. Excess colliders should fail closed, not classify anything from incomplete evidence.
- [ ] **Explicit outstanding COM-UNKNOWN-NPC-BASE-02**: NPC-built ownerless world barricades/structures are not yet tied to a saved unique rival group owner. Do not close this subtask because the player-owned case works, and never claim to know base occupancy from structure owner.

## 46. COMBAT-GRENADE-01 — Consumed native grenades and conservative thrown combat (FINAL Dev Build)

**QUASE PRONTA — AGUARDANDO BUILD (C# source only).** Features gated by actual `ItemThrowableAsset`, native `Grenade` component and source inventory transaction. **Test very carefully in safe test map before normal gameplay**: Grenade splash can affect real players/NPCs and structures.

- [ ] Compile/import `MobileNPCGrenadeThrower.cs`, .meta and bootstrap component. Confirm native asset members `isExplosive`, `throwable`, `isSticky`, `explodeOnImpact`, `fuseLength`, `range`, `playerDamageMultiplier.damage`, `Grenade.explosionEffectGuid`, `Rigidbody.AddForce`, `EffectManager.RegisterDebris`, `MobileNPCRegistry.CopyActiveDrivers`, `MobileNPCFaction.GetRelationship`.
- [ ] Give NPC one real vanilla fuse-explosive throwable through normal loot or inventory; with a genuine VISIBLE hostile player / hostile NPC / zombie 11–27m away and clear ballistic corridor, NPC may throw 1 grenade. Before/after inventory must show EXACT decrement of one item; no throw when empty, fake asset, flash/sticky/impact weapon, no actual visible target, or dead NPC. Confirm loot scoring collects limited relevant grenades when available, below urgent medicine/water/ammo.
- [ ] Check projectile at initial spawn: actual `ItemThrowableAsset.throwable` physical model, Rigidbody, native fuse explosion with correct effect and original per-target damage multipliers; inspect arc across hallway, ceiling, doorway, fences and terrain. Must not teleport, spawn grenade inside blocking wall or substitute a sentry hit. Ensure initial arc blocking test actually rejects unsafe throws rather than mysteriously consuming one grenade.
- [ ] **Friendly/self safety:** Put groupmates/allied-faction members within native `asset.range + 3.5m` around target, then outside radius. No throw toward nearby allies or self. Verify floor-to-floor and cover walls, group/faction changes, current cover/retreat or dangerously low HP. Thrower cooldown **45s**, check rate **1.8s**; no endless spam with repeat visual callbacks. Humanlike trajectory and accuracy need game validation.
- [ ] **Real damage:** Native `Grenade.Explode` must damage appropriate player, zombie and placeable world barricades. Separately `MobileNPCGrenadeNPCSplash` should damage genuine mobile NPC humanoid `MobileNPCHumanoidVitals` only once with falloff and actual wall LOS; test nonliving/respawn, Grenade destruction/fuse race, self/allied accidental injury, and double-damage possibility from any existing DamageTool bridge. No native Player owner/stats should be fabricated for NPC instigator. Capture before/after HP and exact explosion position.
- [ ] Test sound/effects and throw animation: source uses native prefab/effects but has **NO dedicated NPC hand/equip throw animation and NO full multiplayer replication**, which remain pending. A grenade may be physics-simulated differently on different frame rates. Ensure ordinary ranged fire continues to work and throwable selection does not monopolize the brain.
- [ ] Check Save/Load and death loot for consumed grenade, multiple throwing NPCs, interactions with zombies beyond POI, locked houses and 1/10/20/40 NPC load. No infinite item creation, projectile stuck forever, heavy raycast allocations, crashes or log spam.
- [ ] Required feedback: Unity console errors, `Player.log`, screenshot/video showing thrown grenade, player/NPC/zombie HP before/after, item ID/amount before/after, map and obstacle layout. Failures return scoped item to `EM IMPLEMENTAÇÃO`; do not claim source-only status as verified.

## 47. AI BRAIN diagram / consolidated handoff for USER'S FIRST Dev Build

- [ ] Open [`MOBILE_NPC_AI_BRAIN_DIAGRAM.md`](MOBILE_NPC_AI_BRAIN_DIAGRAM.md) for editable Mermaid diagram of **real** perception -> brain state machine -> priority arbitration -> tactical goals -> navigation/combat/transactions -> feedback/memory, with physical radio/loot/reinforcement/base intel/grenades.
- [ ] Unity import all scripts + .meta on `feature/npc-ai-foundation` and compile WITHOUT stopping after first error; record the COMPLETE C# console errors list and associated source line numbers. Build the final Dev Player only after successful compile.
- [ ] Before behavioral tests, spawn 1 hostile and 1 neutral/friendly NPC in a simple open area; verify hitbox/death/animations/dialog/UI/ESC, followed by navigation into houses, doors, nado/vault/jump and zombie pursuit (older unresolved issues). Do not treat this section as fixing them; collect regressions.
- [ ] Run radio equipment and group/faction/TTL, base station fuel/power, airdrop/loot/reinforcements, player-built base scouting, grenade one-item consumption/impact, combat visibility and ally safety. Start with isolated cases, then 10/20/40 NPC load with zombies/POIs and long sessions.
- [ ] Compare actual behavior and screenshots/Player.log to tests 40–46. A scoped `QUASE PRONTA` becomes `CONCLUÍDA` ONLY when the user approves; if an error appears, reopen relevant ID `EM IMPLEMENTAÇÃO` and preserve all evidence in project docs.

**No Unity compile or runtime gameplay tests were executed by the assistant in this source-authoring round.**


## 48. COMPILER-FIX-01 — first Unity compilation 12 diagnostics (RECOMPILE)

**Reported by user in Unity, now fixed ONLY in GitHub C# source:** `MobileNPCHumanoidMotor.cs` CS0103/CS1061/CS0165 x7; `MobileNPCBrainDriver.cs` CS0104 x3; `UseableMelee.cs` CS1061 x2. Fix commits `2f1f9026`, `70b76946`, `c9bae661`. **No Unity compiler/Dev Build test executed by assistant.**

- [ ] Pull latest `feature/npc-ai-foundation`, let Unity recompile all scripts, clear the console, and confirm the **original 12 errors have disappeared**. If any new error appears, capture every exact file/line/compiler code rather than assuming build success.
- [ ] Spawn NPC near a true Ladder and verify an approaching NPC waits for its actual path before climbing, remains affected by gravity and does not resume movement prematurely. Native `MobileNPCLadderTraversal.TryStartServer` should still function.
- [ ] Real low ledge and parapet: `ShouldChooseObstacleJumpServer` selects a direction; `TryBeginPlannedLowJump` and existing `TryBeginVaultJump`/`TryPlanJumpServer` still run on actual collision-checked arc, land before repeating, never warp through wall or locked door.
- [ ] Incoming fire + coverage scoring: `UnityEngine.Random.Range` and `UnityEngine.Random.value` preserve cover sampling and cautious stalking decisions without a compiler ambiguity or runtime exception.
- [ ] Melee at native player, zombie and NPC custom hitbox: client `RaycastInfo` uses actual hit collider or fallback transform; server `InputInfo.colliderTransform` remains intact; damage/hitmarkers/body-part selection still work. No bogus melee hits through walls or double hit.
- [ ] Continue final Dev Build test sections 42–47 ONLY after successful compilation, and use Player.log for runtime errors. This compile fix is **QUASE PRONTA — AGUARDANDO RECOMPILAÇÃO**, not runtime completed.

## 49. PRIMEIRA DEV BUILD: correções das falhas REAIS (2026-10-14) — nova compilação e teste controlado

**Fonte dos repros:** `Player.log` enviado com 9.066 linhas, descrições observadas do usuário e `MOBILE_NPC_DEV_BUILD_AUDIT_2026-10-14.md`. Todos os commits abaixo são SOURCE-ONLY; nenhum foi compilado ou verificado em runtime nesta nova rodada. Execute primeiro 1–2 NPCs e preserve HP/logs, antes de testar 20–40.

- [ ] **Compilar fonte da branch atual:** inspecionar todas as alterações em `PlayerInput.cs`, `MobileNPCDamageBridge.cs`, `UseableGun.cs`, `UseableMelee.cs`, `MobileNPCHumanoidDisposition/Vitals/Perception/Combat/RangedCombat`, `MobileNPCNameplate.cs`, `PlayerInteract.cs`, `ZombieManager.cs`, `MobileNPCBrainDriver.cs`, `MobileNPCLooting.cs`, `MobileNPCPopulationBootstrap.cs`. Checar erros C# exatos antes de prosseguir.
- [ ] **P0 tiro e vida:** NPC de 100 HP visível parado em local aberto; disparar arma vanilla 5–10 vezes e observar cada linha `[MobileNPCHumanoidVitals] Damage accepted: attacker=<Player>`, HP com queda numérica, anatomia (head/body), efeito ferimento, morte em HP 0 com **1** `Death transition`, cadáver e item real. Hitmarker CLIENT sem `Damage accepted` SERVIDOR não basta. Repetir por trás de parede: NENHUM dano. Usar arma com dano alto e arma normal sem acertar várias vezes no mesmo frame.
- [ ] **P0 punho e arma corpo a corpo:** jogador golpeia NPC vivo (sem arma, arma branca nativa), HUD pode mostrar hitmarker mas vida **deve** cair após raycast validado usando `RayMasks.DAMAGE_SERVER | ENTITY`, e NPC deve responder a atacante identificado. Repetir com parede/fora de alcance — rejeitar. Testar sem dupla aplicação `UseableMelee`/`PlayerEquipment`.
- [ ] **Retaliação justa:** amigável ou neutro sem provocação nunca escolhe jogador como inimigo. Após receber dano comprovado de UM Player, tenta busca/segurança, escolhe SOMENTE o mesmo agressor se visto com FOV + LOS até TTL **32s**; pode atirar ou golpear se possuir arma/munição e não estiver em `Retreat`; após expirar não continua atacando. Não atacar terceiro inocente nem através de paredes. Pode escolher cover/recuo quando ferido — não exigir troca suicida de tiros.
- [ ] **HUD de diálogo:** aproximar-se <=4m de NPC neutro/amigável real, usar tecla nativa de interação do Unturned, validar que `PlayerInteract` foca Collider ENTITY, `MobileNPCInteractionService.InteractLocal` abre controlador e aparece painel com opções 1–3. Testar opção numérica, ESC, fechar e restauração HUD; NPC hostil não deve iniciar diálogo. Não invadir outra GUI nativa.
- [ ] **Nome legível:** até 9m, NPC dentro do centro da visão, geometria de mundo livre: exibe identidade sem espelhamento. Atrás de parede, câmera para outra direção ou >9m: **oculto**. Verificar nome acompanha rotação sem ver através de lajes/portas. Não confundir raycast de nome com interagir através de paredes.
- [ ] **Zumbis entre bounds:** 1 e 5 zumbis já caçando o jogador; atravessar borda `LevelNavigation` (não simplesmente andar dentro da mesma POI). Zumbi não pode desaparecer/desligar caça imediatamente pela antiga região. Quando deixa de perseguir e não há mais PlayerCount, a antiga região pode descarregar em ~3s. Verificar não atravessa cerca/parede, não teleporta e não fica imortal fora de navmesh. 30+ zumbis e múltiplas regiões depois.
- [ ] **Navegação de obstáculo/loot:** coloque NPC com objetivo real de pickup em casa/mesa/atrás de poste e objetivo alternativo no chão livre. Repro anterior: `[MobileNPCHumanoidMotor] Blocked` >=138 na sessão, stuck recoveries >10. Agora após >=4 falhas de `Goal.Loot/Investigate/Reinforce`, encerrar objetivo impossivel; manter hostilidade e ordens urgentes. O mesmo item mundo não é novamente tentado durante ~75s após rota fracassar, ~35s depois de transferência falha. **Ainda não prova que qualquer rota complexa é alcançável.**
- [ ] **Transferência real:** item no chão a <=pickupDistance, dentro de espaço de inventário e rota aberta; `ItemManager.TryTransferWorldItemToMobileNPCServer` deve devolver true, retirar uma instância do `ItemRegion` e registrar no inventário. Log anterior `Pickup transfer failed for item instance 425`; anotar posição, instance ID e razão se falhar. Não sintetizar loot.
- [ ] **Bootstrap/ASPFP:** desapareceram erros `Can't add MobileNPCInventory/GroupMember` depois de `GetComponent` condicional? Registrar aviso `Pathfinding.NavmeshCut` ausente: 38 vezes no log antigo. Confirmar se pacote/assembly A* requerido está presente e funcional; distinguir assets de mapa com script missing de falha de fallback. Não silenciar warnings sem entender o impacto.
- [ ] **Morte e corpse:** comparar com zumbi nativo apenas o contrato (dano validado → HP → uma morte → desativar AI → efeito/corpo/loot/limpeza), sem converter o NPC para tipo Zombie/Player. Verificar ragdoll físico é fallback rígido se não há joints, e loot ocorre só depois de morte real. Separar de UI/animação.
- [ ] **Desempenho e estabilidade:** só depois de P0 teste de 1/2 NPC, validar 10/20/40 NPC, FPS, memória, `Player.log`, interação com zumbis fora POI, pontes/escadas, loot, nomes com LOS e rádio; evitar spikes de raycasts/GC e loops de goal que expiram.

**Critério de fechamento:** usuário precisa informar que a NOVA Dev Build compilou, HP caiu por tiro/melee, inimigos morreram, zumbis não sumiram, diálogos aparecem, nomes não revelam atrás de paredes e loot/rotas são verificáveis. Se falha reaparecer, reabrir ID no backlog com o novo `Player.log`. **Nenhuma das correções desta seção foi confirmada em execução ao escrever isto.**


## 50. 2026-10-14 — Hearing vs real visual awareness; retaliation and client/server damage differentiation (NEXT Dev Build)

**Source change only, not yet Unity compiled or tested**. All IDs AUDIT-P0-SIGHT-SOUND-01 / DEFENSE-02 / UI-DIALOG-02 / DAMAGE-TRACE-02 are **QUASE PRONTA — AGUARDANDO NOVA BUILD**.

- [ ] Spawn 1 FRIENDLY, 1 NEUTRAL and 1 HOSTILE NPC on safe open ground, one Player. Walk/sprint in direct line-of-sight at 3m/9m/18m and vary camera/NPC facing. Peaceful NPC must SEE the player (only benign perception), must NOT attack or permanently crouch from those footsteps. Hostile NPC with true LOS may aim/fight; shouldn't freeze/agachar solely because previous sound clue arrived. If player is behind wall/around corner, NPC can investigate audible uncertainty without knowing the exact live Transform.
- [ ] Introduce unrelated sound source or tactical cover goal: real target sight should invalidate only temporary *footstep-driven* listening, NOT valid goal of cover/retreat or companion follow. Test expired attacker self-defense memory after 32s; a peaceful NPC must revert to nonhostile if no further verified damage.
- [ ] Shoot peaceful NPC with real gun from clear LOS and record HP/shot: damage must be accepted in server Vitals, record actual attacker's Transform, NPC now permitted to retaliate ONLY against that attacker, if equipped with actual rounds and HP >55%; if cover is genuinely available it may use it and fire later. Healthy + armed + no cover should **not** choose automatic retreat. If unarmed or low HP, fleeing/hiding remains valid. Test enemy player behind wall: no blind targeting.
- [ ] Shoot/Punch with hitmarker on custom `MobileNPCHitbox` and head/body/limbs. Confirm `[MobileNPCHumanoidVitals] Damage accepted`, exact previous/new HP and one death at zero. If hitmarker occurs without HP loss, filter Player.log for new `[MobileNPCDamageValidation] Rejected` diagnostic. This appears only `UNITY_EDITOR || DEVELOPMENT_BUILD` and at most once per 2s per player. Test vanilla player, zombie, structure hits for nonregression.
- [ ] Open dialogue with local interaction, check console `[MobileNPCInteractionController] Dialogue opened`, visible IMGUI panel above HUD, 1–9 (top row/keypad), ESC/Fechar, 4m invalidation, no permanent HUD disappearance. Panel-depth-only fix may be insufficient, so preserve UI-01 open until visually validated. Verify name label does not appear through wall and faces camera with readable glyphs, based on earlier untested patch.
- [ ] Keep nav/zombie/loot tests from section 49 open; they are NOT covered by this specific new round. Maintain game screenshot/video, exact build SHA, 1/2 NPC, equipment, Player.log, route map and Unity Console. Only user runtime validation closes scoped items.

### Section 50 — addendum pós-interrupção (2026-10-14, `edd3fe9c`/`ff7ace46`)

- [ ] Spawn NPC Neutral/Friendly com jogador visível, sem dano prévio: jogador andando e correndo perto não induz agachamento por escuta; `VisiblePlayer` visual não vira `Target` hostil por si só. Verifique se a animação/altura real do `CharacterController` acompanha o resultado, e não apenas o `Gait` nominal.
- [ ] Após ouvir passos ocultos, NPC pode escutar cautelosamente; ao passar a enxergar o jogador pacífico, cancela postura sonora e hipótese de investigação de passos, mas não cancela combate real contra zumbi/hostil nem ordem ativa de cobertura.
- [ ] Depois de um tiro real e aceito no HP: pacífico pode defender-se contra **aquele** jogador por janela curta; ataque sem dano e apenas hitmarker não autoriza agressão. Diferenciar pelo `Player.log` de validação e `Damage accepted`.
- [ ] Regressão de bootstrap: percepção adicionada depois do motor no template inativo deve ser resolvida e armazenada em cache após ativação; 20–40 NPCs não fazem `GetComponent` em cada ciclo para a mesma leitura.

**Source-only, ainda não compilado/testado pelo usuário em Unity.**


### Section 51 — follow-up de navegação e loot visível (2026-10-14; ainda NÃO executado)

- [ ] Preparar mapa com item nativo atrás de parede: NPC no lado externo não recebe meta Loot **só por região ItemManager**. Entrar no cômodo/linha de visão a até 17m: deve poder escolher, alcançar e recolher item sem teleporte/parede atravessada. Rodar com água, alimento, arma, item de roupa, estrutura/material de base (não só classes de rádio).
- [ ] Colocar dois itens reais: de longe (>17m), nenhum deve ser escolhido só pelo raio de scan de 28m. Ao aproximar-se até 17m com visão desobstruída, prioridade situacional do ScoreItem mantém vantagem por necessidade e distância. Parede opaca deve bloquear visão; vidro/porta têm de respeitar máscara nativa real.
- [ ] Com item em mesa ou prateleira: observar posição da origem do loot, colisão do tampo, fim do Goal.Loot, `Pickup transfer failed` e estado do inventário, sem pickup mágico. Se falhar por geometria/altura, reabrir bug específico; novo filtro de visão por si só NÃO corrige transferência.
- [ ] Por rádio, confirmar que `MobileNPCUsefulLootRumor.TryConfirmLocallySeenItemServer` segue escolhendo SOMENTE classes úteis e primeiro item verdadeiro localmente visto; não duplicar `TryStartLoot`, não perder estado da investigação, nenhum item materializado.
- [ ] Obstrução de poste e canto pequeno: evitar esquerda/direita sem avanço; com objetivo >6m atrás de muro comprido, capturar no Player.log ≥2 eventos reais `[MobileNPCHumanoidMotor] Blocked` e checar se a busca ampla de 81 células/2m permite contorno sem atravessar colisão. Se não, classificar como pendente; rota global continua dependente de backend de navegação válido.
- [ ] Escadas, portas e pisos diferentes: validação exata deve manter 1,25m, não saltar andar; porta trancada continua fechada, porta destrancada só abre pela API de BarricadeManager. Descida brusca/penhasco e água sem chão seguro não podem virar rota aceita.
- [ ] Medir 1, 10, 20 e 40 NPCs com vários obstáculos e loot, FPS/GC e frequência de raycasts/logs, especialmente depois de travamentos. Não considerar nível de autonomia A-Life/GOAP concluído.
- [ ] Refazer P0 de tiros, soco/melee, morte, retaliação, diálogo/ESC, nome por trás de parede, postura por ruído e perseguição de zumbi além de POI. Anotar SHA da build efetivamente compilada; diferenças entre Unity local e HEAD do GitHub podem invalidar o teste.

**Status desta seção:** correções commitadas e revisadas estaticamente; compilação/Dev Build ainda NÃO testadas.


### Seção 52 — HUD de diálogo Glazier/Sleek, teclas e mouse (2026-10-14; NOVOS COMMITS, SEM TESTE)

- [ ] No SP Unity Development Build atualizada, chegar a até 4m de um NPC neutro/amigável e pressionar F: `[MobileNPCInteractionController] Dialogue opened` no log e `Native Sleek dialogue panel attached` uma vez; na tela, ver rótulo/saudação e opções numeradas 1–3. Conferir que não aparece "null", menu vazio ou texto atrás do HUD.
- [ ] Usar `Alpha1`, `Alpha2`, `Alpha3`, `Keypad1`, `Keypad2`, `Keypad3`: seleção exibe estados comércio, proposta de aliança e conversa. Confirmar que hotbar 1–9 NÃO equipa/desequipa item simultaneamente, especialmente durante oferta de item nativo real.
- [ ] Com cursor visível apenas durante sessão, clicar nos botões nativos e em `Fechar`: os cliques não atuam no mundo atrás do diálogo nem abrem outro NPC. Fora da sessão, hotbar/interação/mira voltam ao comportamento normal.
- [ ] Abrir e pressionar ESC UMA vez: fecha somente a conversa; Pause/Dashboard não abre, `PlayerLifeUI` não desaparece, cursor volta ao estado definido pelo jogo e armas/hotbar voltam. Com sessão fechada, ESC deve abrir a pausa normal, validando não regressão.
- [ ] Comércio com 9 slots reais no NPC: painel rolável exibe entrada e opções 1–9, confirmação de pagamento/compra também funciona; não sobrepor botões de rodapé. Cancelar retorna tela esperada. Testar roupas/itens com nomes longos, resolução 720p e 1080p, UI scale diferente e idioma português.
- [ ] Enquanto sessão aberta, NPC/jogador se afastam >4m, NPC morre, jogador morre ou nível descarrega: menu desaparece sem travar cursor, HUD, inventário ou próxima conversa; garantir não ficar com `PlayerUI.window.showCursor` true.
- [ ] Verificar que mouse não obriga ocultação permanente de `PlayerLifeUI`; investigar exceções `Glazier/ISleekElement`, `CreateScrollView`, `Native Sleek dialogue panel` no `Player.log`. Se backend nativo não inicializar, registrar fallback IMGUI e manter UI-01 aberta.
- [ ] Compilar sem CS errors, registrar SHA exato da build, NPC, input, resolução e 2 screenshots (menu e pós-ESC). **Nenhum critério acima foi executado; status: QUASE PRONTA — AGUARDANDO BUILD**.

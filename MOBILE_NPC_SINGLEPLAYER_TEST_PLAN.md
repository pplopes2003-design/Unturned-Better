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

# Mobile NPC — plano integrado de validação single-player

## Escopo e critérios

Este plano cobre a fundação local dos NPCs móveis no Unturned/U3-SDK. Executar depois de a Unity regenerar a pasta `Library`, terminar a importação e compilar os scripts sem erros.

**Importante:** um item deste plano só fica aprovado após observação no jogo. O código atual é experimental, server-local e single-player-first; não considerar replicação multiplayer, interface de comércio ou sincronização visual de clientes como concluídas.

## 1. Preparação de build limpa

- [ ] Fechar Unity e Unity Hub e confirmar que os processos do Editor terminaram.
- [ ] Apagar somente a pasta `Library` do projeto correto; preservar `Assets`, `Packages` e `ProjectSettings`.
- [ ] Abrir com Unity 2022.3.62f1 e esperar a importação terminar.
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
- [ ] Confirmar que arquivo inválido gera aviso controlado e não impede iniciar uma partida.
- [ ] Testar um registro com coordenadas inválidas e confirmar que ele não cria um NPC nem gera loop de erro por frame.
- [ ] Testar uma posição bloqueada: o registro deve permanecer pendente e ser tentado novamente com intervalo, sem apagar silenciosamente os demais registros.
- [ ] Confirmar que salvar enquanto ainda há registros pendentes não os elimina.
- [ ] Confirmar que outro personagem/mapa não carrega o snapshot deste mundo.
- [ ] Não editar o arquivo de save real sem antes fazer cópia.

## 5. IA, percepção e combate

- [ ] Confirmar que NPCs investigam ruídos, perseguem alvos detectados e retornam quando perdem o alvo.
- [ ] Testar perda de linha de visão e memória de alvo; NPC não deve perseguir indefinidamente uma posição antiga.
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
- [ ] Dar a um NPC itens de construção válidos e verificar escolha de local em terreno seguro.
- [ ] Verificar tentativa de construir armazenamento central e peças de perímetro apenas quando há itens válidos e local livre.
- [ ] Testar local bloqueado, item ausente e construção interrompida; não deve consumir itens se a colocação falhar.
- [ ] Confirmar que itens de construção restantes permanecem no inventário e que armazenamento colocado recebe os suprimentos destinados a ele.
- [ ] Sair e voltar: conferir a âncora/grupo persistidos; observar separadamente estruturas colocadas, pois a persistência vanilla das estruturas não é implementada por este snapshot de NPC.

## 8. Estabilidade e desempenho

- [ ] Testar limite baixo (1–5) e limite recomendado (20).
- [ ] Observar CPU/FPS por pelo menos 10 minutos; verificações periódicas não devem ocorrer sem intervalo.
- [ ] Confirmar que logs não crescem indefinidamente em funcionamento normal.
- [ ] Trocar de mapa e retornar ao menu sem deixar bootstrap/diretor duplicado.
- [ ] Encerrar normalmente e confirmar gravação final.
- [ ] Repetir o teste com um save novo e com um save existente.

## 9. Fora do escopo desta rodada

- Replicação multiplayer de spawn, movimento, animação, combate, morte, inventário e construção.
- Interface de negociação e interação contextual com o jogador.
- Garantia de comportamento idêntico em todos os mapas/mods e assets personalizados.
- Aprovação final de gameplay sem executar os testes acima dentro do Unity/jogo.

## Relatório a guardar

Para cada falha, guardar: mapa, passos para reproduzir, resultado esperado, resultado observado, trecho do Console e Editor.log completo se houver falha de build. Distinguir erros de compilação C#, erros de inicialização em runtime, falhas de gameplay e falhas de infraestrutura/cache do Unity.

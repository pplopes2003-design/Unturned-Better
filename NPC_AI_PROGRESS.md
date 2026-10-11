# NPC AI — acompanhamento do desenvolvimento

## Estado atual

**Fase:** arquitetura de movimento e sincronização  
**Branch:** `feature/npc-ai-foundation`  
**Base:** `perfomance-baseline`  
**Implementação de IA móvel:** ainda não concluída  
**Build/teste no Unity:** pendente — este ambiente não executa o Editor nem compila o projeto Unity.

## Descobertas confirmadas

- `InteractableObjectNPC` representa NPCs colocados como objetos estáticos do mapa; o componente cuida de diálogo, roupas, armas visuais, animações e olhar para o jogador.
- A posição do NPC é usada como posição-alvo do diálogo, e sua identidade usa `NetIdRegistry`.
- `NetIdRegistry.AssignTransform` registra uma referência a um Transform; não é, por si só, um mecanismo de replicação de posição.
- O movimento de zumbis usa uma interface específica de pathfinding/movimento. Na configuração pública do SDK, o fallback `UnturnedPathfinding_Empty` cria `NonPathfindingZombieMovementComponent`, que movimenta um `CharacterController` diretamente; isso não fornece automaticamente uma solução geral de navegação para NPCs.
- Portanto, mover um NPC estático apenas no servidor não basta: é necessário um fluxo explícito de sincronização para clientes, além de manter válido o alvo de diálogo e a checagem de distância do servidor.

## Decisão técnica tomada

Não alterar a posição dos NPCs estáticos existentes nem adicionar movimento automático a todos eles. A primeira implementação móvel deve ser opt-in e usar autoridade do servidor e sincronização explícita. Isso reduz o risco de quebrar mapas, diálogos e multiplayer.

## Próximas etapas

- [ ] Mapear o protocolo de mensagens e o ciclo de vida das entidades móveis existentes.
- [ ] Escolher a integração mínima de replicação compatível com o SDK disponível.
- [ ] Verificar os pontos de extensão disponíveis para mensagens de rede sem depender de código privado ausente no SDK público.
- [ ] Implementar uma primeira capacidade isolada de patrulha/percepção, desativada por padrão para NPCs atuais.
- [ ] Revisar impacto no diálogo, salvamento, regiões e multiplayer.
- [ ] Solicitar teste no jogo quando houver alteração concreta.
- [ ] Registrar feedback, bugs encontrados e correções.

## Regra de validação

Nenhuma mudança será marcada como testada no jogo até haver teste real no Unity/Unturned. Commits nesta branch são experimentais até a validação.

## Registro de progresso

### 2026-10-11
- Criada branch de trabalho `feature/npc-ai-foundation`, separada da branch de referência.
- Mapeado o componente de NPC estático e sua dependência de diálogo/NetIdRegistry.
- Confirmado que o registro de Transform não equivale à replicação de movimento.
- Confirmado que o SDK público usa uma implementação de movimento de zumbi de fallback sem pathfinding completo.
- O movimento de veículos é replicado por um protocolo dedicado em NetPak, com sequência de atualizações, posição, rotação e estado físico; não é um componente genérico que possamos simplesmente anexar a um NPC.
- O movimento dos jogadores usa snapshots/interpolação próprios. Reutilizar apenas o registro de NetId ou o componente de movimento de zumbi não replica um NPC por esse protocolo.
- Evitada uma alteração insegura que moveria NPCs apenas no servidor e causaria divergência visual entre clientes.

### 2026-10-11 — implementação de código
- Confirmado que o SDK possui chamadas de rede cliente-servidor tipadas e código NetInvokable gerado; a sincronização de NPCs móveis precisa integrar-se a esse mecanismo.
- O SDK público não garante registro dinâmico arbitrário de novas mensagens. Por isso, a implementação não deve depender de RPC improvisado nem movimentar apenas o objeto do servidor.
- Próximo passo técnico: adicionar o estado de movimento e a lógica de IA em uma alteração isolada, junto com a mensagem de rede correspondente e sua serialização gerada, mantendo os NPCs estáticos desativados por padrão.

### 2026-10-11 — confirmação do limite arquitetural
- A documentação oficial informa que as RPCs de gameplay são geradas pelo Net Gen do Editor (Window > Unturned > Net Gen > Generate). Isso significa que adicionar apenas um método de rede não basta para uma implementação completa; os arquivos gerados também precisam acompanhar a mudança. Fonte: https://docs.smartlydressedgames.com/en/latest/u3-sdk/unity-project.html
- Foi localizada uma solicitação pública de agosto de 2026 pedindo exatamente uma entidade humanoide server-authoritative e replicada, independente de Player/SteamPlayer. Ela descreve a lacuna entre NPC estático, zumbi/animal e jogador: https://github.com/SmartlyDressedGames/U3-SDK/issues/5541
- Conclusão: o caminho de produção é criar uma entidade humanoide móvel com ciclo de vida, NetId, snapshots e integração de dano; converter diretamente o NPC estático de diálogo sem essa infraestrutura arriscaria quebrar mapas e multiplayer.
- As mudanças existentes de IA de zumbis continuam separadas dessa nova entidade. Nenhuma mudança de NPC móvel foi marcada como concluída ou testada.

### 2026-10-11 — correção de alcance de sons entre regiões
- Corrigido `ZombieManager.alertZombiesToNoise`: antes, tiros e explosões só alertavam zumbis registrados na mesma região de navegação do evento, mesmo quando outros estavam dentro do raio do som.
- Agora o servidor percorre a lista de zumbis e alerta os que estão dentro do raio espacial configurado, inclusive atravessando fronteiras de regiões. O cooldown existente continua reduzindo eventos próximos repetidos.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/70b2a1e5e998bc70125b3e4722c85ef04c3b4558
- Validação pendente no Unity: conferir custo em mapas com muitos zumbis e confirmar a reação em regiões adjacentes.

### 2026-10-11 — investigação com busca local
- Alterado `Zombie.cs` para que, ao concluir uma investigação por ponto, o zumbi faça pausas curtas de 2–5 segundos e caminhe para até três pontos de busca válidos em torno da posição investigada (raio máximo de 18 m).
- A busca tem limite de tempo de 55 segundos e número máximo de deslocamentos; se não encontrar alvo, o zumbi retoma o caminho para um ponto de surgimento. Alertas de jogador interrompem a busca local e limpam o orçamento de investigação.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/b7be0a6e48cee4f954b501ad5af494e7529df0c4
- Não compilado/testado no Unity neste ambiente. Validar principalmente: pausas e transições de estado, navegação em terrenos com obstáculos e resposta a novos sons durante a busca.

### 2026-10-11 — correção da transição entre etapas da busca
- Revisando o fluxo da investigação, identifiquei que a regra original do jogo só aceitava um novo destino de investigação se ele fosse mais próximo que o destino atual. Isso podia descartar os pontos de busca laterais e fazer o zumbi parecer parado.
- Ajustei a transição interna da busca para aceitar o próximo ponto escolhido, reiniciar os temporizadores de travamento e reativar a navegação. Alertas externos continuam respeitando a regra normal de priorizar alvos mais próximos.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/bff02aae75bcdc2013403deb7a6317afec19f7f8
- Pendente: compilação Unity, testes em singleplayer e servidor dedicado/multiplayer, e observação de custo com muitos zumbis ativos.

### 2026-10-11 — atualização de investigação por novos sons
- Corrigido um caso em que um zumbi que já investigava um ponto podia ignorar a posição de um novo tiro/explosão como centro da busca, pois o estado só era atualizado quando ele ainda não estava caçando.
- Novos alertas por ponto agora renovam o centro, o prazo e o orçamento da investigação mesmo durante outra investigação por ponto. Perseguição de jogador, etapas internas da busca e retorno ao ponto de surgimento não são sobrescritos por essa atualização.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/a4cbbf20ec3c385f04716eab35c9612b0fb93051
- Pendente: compilar e testar no Unity/Unturned; verificar se o novo som redireciona a busca sem interromper perseguições a jogadores.

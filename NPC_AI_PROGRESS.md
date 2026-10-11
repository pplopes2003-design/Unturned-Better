# NPC AI — acompanhamento do desenvolvimento

## Estado atual

**Fase:** protótipo de percepção/movimento local; integração de rede e combate real pendentes  
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

### 2026-10-11 — redirecionamento imediato para novo som
- A atualização do centro de investigação, sozinha, não bastava: a regra antiga ainda podia manter o destino anterior se o novo som estivesse mais longe do zumbi.
- Agora, durante uma investigação por ponto, um novo alerta externo substitui o destino atual e reinicia os temporizadores de travamento e busca. Perseguições a jogadores não usam esse caminho; etapas internas de busca e retorno ao surgimento preservam as próprias regras.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/c01d5393461b3c98102ce74784bc8517f08d3cb1
- Pendente: validar no Unity se sons sucessivos redirecionam corretamente a IA e se o reinício de navegação funciona com o pathfinder usado no projeto.

### 2026-10-11 — ignorar alertas antes de alterar zumbis mortos
- A revisão encontrou que `alert(Vector3, bool)` atualizava o estado de investigação antes de verificar `isDead`. Alertas sonoros próximos podiam, portanto, alterar campos de uma entidade morta, embora ela retornasse sem iniciar movimento.
- A verificação de morte foi movida para o início do método, evitando mutações desnecessárias e deixando explícito que entidades mortas não recebem novas ordens de investigação.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/8041277bbc959b53fbdc53087e9e46f23bf32a9e
- Pendente: compilação e teste no Unity/Unturned; confirmar que não há outro fluxo que dependa de chamar `alert` em zumbis mortos.

### 2026-10-11 — validação da origem antes do cooldown sonoro
- A revisão identificou que o cooldown era consumido antes de validar se a região de navegação retornada era válida e se a lista global de zumbis estava disponível. Um evento inválido poderia suprimir um disparo válido logo em seguida.
- A validação de servidor, raio, lista, navegação e índice agora ocorre antes de registrar o cooldown.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/f89aab161a659d1cfe3d619c883b94f15b886064
- Limite conhecido: o alerta ainda percorre `AllZombies` para preservar a reação entre regiões de navegação. Uma otimização espacial segura exige confirmar a estrutura e os limites de `ZombieRegion`; não foi substituída por uma busca local que poderia deixar de alertar zumbis próximos em outra região.


### 2026-10-11 — integração de combate para NPCs móveis
- Revisei `InteractableSentry` e `DamageTool` para separar o que pode ser reutilizado do que ainda falta para um humanoide móvel.
- `InteractableSentry` é uma referência útil de arma controlada pelo servidor: mantém estado de alvo, mira e disparo e usa assets/attachments de armas. Porém, é um `InteractableStorage` do mapa, não uma entidade humanoide genérica.
- `DamageTool` expõe eventos específicos para dano em jogadores, zumbis e animais (`damagePlayerRequested`, `damageZombieRequested`, `damageAnimalRequested`), mas não um evento público genérico para qualquer agente. Criar só um componente de IA ou atribuir um `NetId` não faz o NPC participar do fluxo normal de acerto/dano.
- A solicitação pública #5543 descreve a lacuna de uma entidade humanoide/agent replicada de primeira classe. A integração ainda precisa respeitar o fluxo de raycast e dano do cliente/servidor. Fonte: https://github.com/SmartlyDressedGames/U3-SDK/issues/5543
- Decisão: não adicionar uma classe de “NPC combatente” isolada que pareça funcional, mas não receba acertos legítimos dos clientes. A próxima mudança deve conectar identidade, spawn/despawn e snapshots com o caminho de raycast/dano, ou começar por uma melhoria de IA que opere sobre entidades já suportadas.
- Estado de teste: inspeção estática do código via GitHub; nenhuma compilação ou teste em Unity/Unturned foi executado.


### 2026-10-11 — proteção contra regiões de zumbis indisponíveis
- A revisão do agendador de hordas identificou uma condição de inicialização em que `AllZombies` poderia estar disponível antes de `regions`; o código acessava `regions.Length` sem verificar o array.
- `UpdateAmbientHordes()` agora retorna com segurança quando o array de regiões é nulo ou vazio, antes de procurar líder ou membros da horda.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/5dd858b8c45087c019dc1f1bb14c689ac92c390b
- É uma correção defensiva de NRE identificada por inspeção estática; não confirma que seja a causa do erro de inicialização da dev build do usuário. Logs ainda necessários para associar a exceção a uma linha concreta.


### 2026-10-11 — primeira classe separada para decisões de NPC móvel
- Criado `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCBrain.cs`, separado de `InteractableObjectNPC`.
- A classe mantém estados `Idle`, `Investigating`, `Pursuing` e `Returning`, com memória temporária de alvo, investigação de ruído com prazo e retorno ao ponto inicial.
- O alvo precisa ser validado pela camada de percepção do servidor. Quando fica oculto, a IA mantém a última posição observada, em vez de seguir a posição atual do Transform através de paredes.
- Deliberadamente não move Transform, não aplica dano e não afirma replicação de rede: esses são contratos da futura entidade móvel e da camada de locomoção/rede.
- Commits: https://github.com/pplopes2003-design/Unturned-Better/commit/ec1f5e40a33ea04fc9e7d90a0cc4b5ef592ddec3, https://github.com/pplopes2003-design/Unturned-Better/commit/1e8e544d842582fcbdfd3118d7ab8e5896843dbd, https://github.com/pplopes2003-design/Unturned-Better/commit/5cc059ca4fbc3b18fd52e516f1dd877c01049edb
- Estado de validação: revisão estática apenas; ainda não compilado nem testado no Unity. Próximo passo: ligar essa máquina de estados a uma entidade móvel com navegação e snapshots, sem alterar os NPCs estáticos existentes.


### 2026-10-11 — adaptador entre a IA de NPC e a entidade hospedeira
- Criado `MobileNPCBrainDriver.cs`, um componente opt-in que instancia e controla `MobileNPCBrain` somente quando inicializado no servidor.
- O adaptador expõe operações explícitas para registrar alvo validado, ruído, chegada ao destino, atualização de visibilidade e reset. A entidade hospedeira recebe `DecisionUpdated` quando estado/destino mudam, sem reiniciar a navegação a cada tick.
- O componente não é anexado automaticamente a `InteractableObjectNPC`; NPCs estáticos e diálogos existentes permanecem inalterados.
- Limite deliberado: o host ainda precisa fornecer percepção, navegação, ciclo de vida, replicação e dano. O adaptador é o ponto de conexão da decisão, não uma entidade móvel completa.
- Commits: https://github.com/pplopes2003-design/Unturned-Better/commit/5454e5c04829a7a82fb8d064f5e3e1cf608a85f1 e https://github.com/pplopes2003-design/Unturned-Better/commit/7634bff4678ec2bb44aabeb6798a15bf6656f0a2
- Validação: inspeção estática apenas; sem compilação Unity ou teste em jogo.


### 2026-10-11 — táticas defensivas e objetivo de loot
- Criado `MobileNPCTactics.cs` com seleção de objetivos de defesa e coleta: procurar cobertura após tiros, recuar quando a saúde fica crítica/baixa com dano recente, e aceitar um destino de loot quando não há uma ordem defensiva urgente.
- `MobileNPCBrainDriver.cs` agora mantém a máquina de estados e as táticas juntas e publica o objetivo efetivo ao host. A ordem defensiva tem prioridade sobre o deslocamento para loot.
- Contratos importantes para a futura entidade: o host precisa detectar tiros/dano no servidor; validar cobertura por linha de visão e navegação; calcular um ponto de recuo seguro; verificar capacidade do inventário; e executar a coleta por uma operação autoritativa do servidor. O código atual não concede itens automaticamente nem presume que qualquer ponto seja cobertura segura.
- Ainda não conectado a uma entidade humanoide concreta: spawn/despawn, navegação real, snapshots de rede, acertos/dano e pickup continuam sendo a próxima integração. Os NPCs de diálogo estáticos permanecem sem alteração.
- Commits: https://github.com/pplopes2003-design/Unturned-Better/commit/cb976354f12e0a24541e574310b8a6b9bea77da0 e https://github.com/pplopes2003-design/Unturned-Better/commit/1b18b94340c0bbd70b4f3e05ef8c54cdd1d9ddcc
- Validação: revisão estática via GitHub; não compilado nem testado no Unity/Unturned.


### 2026-10-11 — primeiro motor de movimento humanoide
- Criado `MobileNPCHumanoidMotor.cs`, componente opt-in para um prefab humanoide com `CharacterController` e `MobileNPCBrainDriver`.
- O motor executa deslocamento no servidor, rotação gradual, gravidade e colisão local; usa velocidade maior para perseguição e recuo. A visibilidade do alvo precisa ser fornecida explicitamente por percepção autoritativa do servidor.
- Limite importante: ainda não é navegação por caminhos. O motor segue o destino em linha direta e pode ficar bloqueado por paredes/obstáculos; não deve ser tratado como pronto para mapas reais. Não há prefab, spawn/despawn nem replicação cliente-servidor ligados nesta etapa.
- Commits: https://github.com/pplopes2003-design/Unturned-Better/commit/65b1da5048863f628a833936e42f7dc346a06064 e https://github.com/pplopes2003-design/Unturned-Better/commit/e32d64a51c8af46a09433f07062c92959b7388d5
- Próximas integrações: navegação real compatível com o SDK, alvo de combate/dano autoritativo, inventário e retirada autoritativa de itens do mundo; depois snapshots e ciclo de vida multiplayer.
- Validação: revisão estática via GitHub; não compilado nem testado no Unity/Unturned.


### 2026-10-11 — primeiro ataque corpo a corpo autoritativo
- Criado `MobileNPCHumanoidCombat.cs`: resolve o alvo a partir do alvo já validado pelo cérebro, exige estado de perseguição, verifica alcance e linha de visão e aplica dano pelo `DamageTool.damagePlayer` no servidor, respeitando a armadura.
- A camada usa o sistema de dano do jogo em vez de reduzir vida por conta própria. O alvo precisa ser um `Player`; outros tipos de alvo não recebem ataque neste componente.
- O ataque ainda não está sincronizado com animação de golpe e não tem teste no Unity. A validação de permissões PvP/zonas seguras depende dos contratos internos de `DamageTool`; não foi criado um sistema de facções para NPCs.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/17008a2fe45d76541d260415ae77fb1dc423671c
- Próximo bloqueio técnico: loot exige uma operação de servidor própria para adicionar ao inventário de um agente não-Player e remover o drop/replicar sua remoção. O fluxo existente `ReceiveTakeItemRequest` está ligado a `Player.inventory`, portanto não será reutilizado artificialmente para um NPC.
- Validação: inspeção estática do contrato de dano e do fluxo de pickup; não compilado nem testado no Unity/Unturned.


### 2026-10-11 — vida e morte autoritativas do humanoide
- Criado `MobileNPCHumanoidVitals.cs`: vida máxima configurável, dano/cura validados no servidor, eventos de mudança de vida e morte única, e notificação de dano ao cérebro/táticas para permitir recuo por ferimentos.
- Ao morrer, o componente limpa a decisão de IA e desativa movimento e combate. A entidade ainda precisa de um host para animação de morte, remoção/despawn e replicação desse estado aos clientes.
- Importante: o componente recebe dano após validação do acerto; não foi ligado diretamente a `DamageTool`, cujas rotinas existentes são específicas de Player/zumbi/animal. Também não há ainda sistema genérico de projéteis que detecte e encaminhe impactos a NPCs.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/5c965b2740e7c399b267bec273cd9480a7d849e4
- Validação: revisão estática via GitHub; não compilado nem testado no Unity/Unturned.


### 2026-10-11 — avanço do protótipo humanoide móvel
- Mantida a implementação modular existente em `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/`: brain/driver, percepção, motor, mira, combate corpo a corpo e à distância, vitals e táticas. NPCs estáticos continuam separados.
- Corrigida a percepção para ignorar os próprios colisores do NPC ao escolher o obstáculo mais próximo na linha de visão.
- O combate à distância agora exige um jogador atualmente visível pela camada de percepção; não deve continuar atirando durante a memória de um alvo escondido. A mira também deixa de acompanhar o Transform de um alvo que saiu do campo visual.
- Humanizada a cadência de tiros: rajadas de tamanho variável, intervalos com jitter e pausas variáveis entre rajadas. A mira sorteia atraso de reação por alvo e aumenta o espalhamento por movimento, tiros anteriores e supressão.
- Ligado o relato de fogo recebido à penalidade de supressão da mira.
- Corrigida a verificação de linha de visão do combate corpo a corpo para ignorar os próprios colisores.
- Atualizado o guia de smoke test. A compilação Unity continua pendente; o build executado ainda encerra por `Steam API initialization failed`, uma falha de inicialização separada.
- **Limitações restantes:** spawn/ciclo de vida da entidade, replicação e interpolação nos clientes, pathfinding real, integração completa com arma/inventário/efeitos/sons do jogo e teste em singleplayer/servidor dedicado. Os eventos de disparo/recarga permitem conectar áudio e animação existentes, mas essa integração ainda não foi validada.
- Commits recentes incluem `5577a414` (rajadas/cadência), `96b31b40` (percepção), `b95f8388` (reação/supressão), `254c0c16` (ligação da supressão), `2dc0311c` (visibilidade no combate à distância) e `0ec9c956` (linha de visão corpo a corpo).


### 2026-10-11 — preparação de papéis hostil/amigável
- Corrigido erro de código detectado por revisão estática: o componente de combate à distância usava parâmetros de rajada/pausa que não estavam declarados. Os cinco campos serializados foram adicionados.
- Adicionado `MobileNPCHumanoidDisposition` com papéis `Hostile`, `Friendly` e `Neutral`. A percepção agora só adquire jogadores como alvos quando o papel é hostil; amistosos/neutros limpam o alvo e não atacam jogadores nesse protótipo.
- Atualizado o guia de teste para configurar o papel no prefab.
- **Ainda não é uma build jogável pronta:** a implementação atual não tem spawner próprio, ciclo de vida de entidade nem replicação de movimento para clientes; amizade por enquanto significa não hostil, não escolta ou diálogo. Não é possível afirmar que o próximo build conterá NPCs funcionais até implementar essas ligações e compilar/testar no Unity.
- Adicionados arquivos `.meta` estáveis para todos os scripts do protótipo móvel, evitando GUIDs regenerados pelo Unity durante importação.
- Revisão estática final desta etapa: os cinco parâmetros de cadência de rajadas estão declarados; equilíbrio de chaves/parênteses nos componentes principais conferido. Isso não substitui a compilação do Unity.


### 2026-10-11 — registro de ciclo de vida (preparação para facções)
- Adicionado `MobileNPCRegistry`, registro em memória somente do servidor para enumerar os `MobileNPCBrainDriver` móveis ativos.
- O driver registra-se após `InitializeServer` e é removido ao ser desativado; entradas Unity destruídas/inativas são filtradas ao copiar a lista.
- O registro é apenas infraestrutura de gestão: **não cria NPCs, não replica movimento e não altera NPCs estáticos**. Serve como base para futuras populações e facções sem acoplar esses sistemas ao diálogo legado.
- O arquivo `.meta` foi criado. Compilação Unity e teste no jogo continuam pendentes.
- Próxima etapa correta continua sendo a integração com o ciclo de vida de entidades e o Net Gen oficial; não adicionar RPC manual sem a geração correspondente.


### 2026-10-11 — serviço de spawn controlado
- Adicionado `MobileNPCSpawnService.TrySpawnServer`: valida autoridade do servidor, prefab explícito, componentes obrigatórios e posição dentro dos limites seguros do mapa antes de criar a instância.
- O serviço aceita o perfil Hostile/Friendly/Neutral e inicializa o motor/brain após a criação.
- Criados metadados Unity e documentação de contrato.
- Limite importante: este é spawn **somente no servidor**, não é spawn replicado. Não usar os prefabs legados `NPC_Server`/`NPC_Client` nem considerar pronto para multiplayer até implementar e validar o protocolo de replicação gerado.
- Compilação e testes Unity permanecem pendentes.

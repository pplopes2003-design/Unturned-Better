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


### 2026-10-11 — ciclo de vida de morte do NPC móvel
- Corrigido `MobileNPCHumanoidVitals`: quando a vida chega a zero, o protótipo agora desativa também o combate à distância, percepção, mira e `CharacterController`, além do movimento e combate corpo a corpo. Isso evita NPCs mortos continuarem atirando ou mantendo colisão de movimento.
- Mantida a aplicação de dano e cura sob autoridade do servidor; dano inválido e ações após a morte continuam rejeitados. O evento `Died` permanece o ponto de extensão para futura animação de morte/remoção.
- Atualizado `Docs/MobileNPC-Quick-Test.md` com um teste de fumaça específico para morte e pós-morte.
- Commit de código: https://github.com/pplopes2003-design/Unturned-Better/commit/61a3eee9bcdb27df144acac617e8a2a08a6e2b70
- Commit da documentação de teste: https://github.com/pplopes2003-design/Unturned-Better/commit/bfe559a89d6efd41f5b923757c20c0a4227e41a6
- Ainda não compilado nem validado no Unity; o teste real continua pendente.


### 2026-10-11 — análise de ragdoll para humanoides móveis
- Inspecionado `RagdollTool`: o jogo já possui `ragdollPlayer` e `ragdollZombie`, além dos prefabs `Characters/Ragdoll_Player` e `Characters/Ragdoll_Zombie`.
- Esses métodos copiam a pose de um esqueleto compatível e aplicam impulso físico; a variante de jogador também recebe `PlayerClothing`, enquanto a variante zumbi depende de dados específicos de roupa/tipo de zumbi. Portanto, não é seguro chamar uma dessas funções como se fosse um componente genérico pronto.
- Documentado que o NPC móvel atualmente não gera ragdoll ao morrer: ele dispara `Died` e desativa componentes, mas a apresentação visual de morte está pendente.
- A implementação correta precisa de um adaptador de ragdoll que use o esqueleto do prefab móvel e replique o evento/pose para clientes, que criam a representação física localmente. Isso deve acompanhar o protocolo de entidade móvel; uma chamada apenas no servidor não é suficiente para multiplayer.
- Commit da documentação: https://github.com/pplopes2003-design/Unturned-Better/commit/58a760c18d303cf266a3df75b09d63a798833866
- Nenhum teste Unity executado.


## Death presentation integration hook (2026-10-11)

- Added `MobileNPCHumanoidVitals.ApplyDamage(float damage, Vector3 hitImpulse)`. Existing `ApplyDamage(float damage)` remains compatible and forwards a zero impulse.
- Rejects non-finite impulse components along with invalid damage values.
- Added server-side `DeathPresentationRequested(position, rotation, impulse)` event, emitted once during the death transition before the legacy `Died` event. This provides the death pose and lethal-hit impulse to a future presentation/network layer.
- Important: this is an integration hook only. It does not replicate the NPC, send an RPC, or spawn a ragdoll. Unturned's generated network methods are project-level generated code; correct multiplayer ragdolls require a stable replicated NPC identity and spawn/state replication first. See the SDK networking overview: https://docs.smartlydressedgames.com/en/latest/u3-sdk/unity-project.html
- Validation status: source reviewed; Unity compilation and multiplayer playtest not run.


## Spawn/death lifecycle hardening (2026-10-11)

- `MobileNPCSpawnService.TrySpawnServer` now initializes optional `MobileNPCHumanoidVitals` synchronously, so callers do not receive a successful spawn before server health initialization.
- On death, `MobileNPCHumanoidVitals` disables the brain driver after publishing the final reset. Its `OnDisable` unregisters the driver from `MobileNPCRegistry`, preventing dead NPCs from remaining in active-agent enumeration.
- Existing static NPC prefabs and code paths remain untouched.
- Validation status: source-level review only; Unity compilation, dedicated-server test, and multiplayer client test remain pending.

### 2026-10-11 — limpeza explícita de NPC móvel no servidor
- Adicionado `MobileNPCSpawnService.TryDespawnServer`: exige autoridade do servidor, rejeita referências nulas e verifica a assinatura de componentes móveis antes de destruir a instância.
- O método não aceita objetos arbitrários nem os NPCs estáticos legados por engano. A destruição desativa os componentes e permite ao driver sair do registro de NPCs ativos.
- Limite explícito: isso é limpeza do lado do servidor, não um pacote de despawn para clientes; não deve ser usado como substituto da futura camada de replicação.
- Atualizado `Docs/MobileNPC-Quick-Test.md` com o contrato e a limitação.
- Commit de código: https://github.com/pplopes2003-design/Unturned-Better/commit/4ccba0504ac6beb4f78c0c037af3852d8f2e3dc6
- Validação: inspeção estática; sem compilação Unity nem teste no jogo.

### 2026-10-11 — relações entre facções e estímulo de tiros para zumbis
- `MobileNPCHumanoidDisposition` agora define Hostile e Friendly como facções opostas; Neutral não entra em guerra. NPCs da mesma facção não são alvos hostis entre si.
- A percepção autoritativa pode selecionar humanoides móveis da facção oposta, respeitando distância, campo de visão, linha de visão e estado de vida.
- O combate corpo a corpo e o hitscan foram ligados às vidas autoritativas dos NPCs adversários. O disparo móvel também chama o alerta de ruído existente do `ZombieManager`, para zumbis investigarem tiros de qualquer facção.
- Limite importante: os zumbis ainda não perseguem/atacam NPCs móveis como alvos vivos; o alerta atual só faz investigar a posição do ruído. Também não existe população automática independente de mapa, construção de bases, recursos, reivindicação territorial ou persistência de assentamentos.
- Requisitos do usuário registrados como critérios de aceitação no guia: spawn em qualquer mapa compatível, assentamentos regionais, guerra entre facções e zumbis capazes de perceber/perseguir/danificar ambas as facções.
- Commits de código: https://github.com/pplopes2003-design/Unturned-Better/commit/8d6d1fd3152a95f32cb5311282ea3945de23e34b, https://github.com/pplopes2003-design/Unturned-Better/commit/cb8bfb9ebcd9f840c201dd5bfe571122578235d8, https://github.com/pplopes2003-design/Unturned-Better/commit/8f23b3fab993f2e2d0e577af8fe44caec5c27590, https://github.com/pplopes2003-design/Unturned-Better/commit/a88064ec6a1119968998d62612685a82512f9245, https://github.com/pplopes2003-design/Unturned-Better/commit/f3e2c7d902f72110fde6fe5794a12d66d8fd2352, https://github.com/pplopes2003-design/Unturned-Better/commit/99ab6bee32233f47a3f2fd12ffd24ff8b69f07dc
- Validação: revisão estática e balanceamento de delimitadores; não houve compilação Unity nem teste em jogo. Esses commits são implementação inicial, não confirmação de NPC pronto.


### 2026-10-11 — redução de alocações na percepção entre facções
- A varredura de inimigos móveis não usa mais `FindObjectsOfType<MobileNPCHumanoidDisposition>()` a cada ciclo de percepção de cada agente. Agora consulta os drivers ativos já registrados em `MobileNPCRegistry`, reutilizando uma lista temporária compartilhada.
- Isso reduz buscas globais e alocações repetidas no caminho de combate entre facções; não muda a seleção de alvo nem resolve ainda o custo da busca de jogadores, que continua sendo uma etapa separada.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/7dd6e02775fa8f1f7f861e4eb69b1f9f35128d49
- Validação pendente: compilar no Unity e medir custo com múltiplos NPCs ativos. O uso da lista compartilhada pressupõe o fluxo normal de atualização de gameplay na thread principal do Unity.


### 2026-10-11 — otimização do acerto dos disparos
- O combate à distância agora reutiliza um buffer de `RaycastNonAlloc` e escolhe o colisor externo mais próximo sem ordenar todos os impactos a cada tiro.
- Para preservar a correção quando o buffer de 64 colisões fica cheio, usa-se a consulta completa alocadora apenas nesse caso de saturação.
- Commits: https://github.com/pplopes2003-design/Unturned-Better/commit/db216815c1ddbf1c845e343658b66720144717f6 e https://github.com/pplopes2003-design/Unturned-Better/commit/ee4cce56b608f07bf509fbf80260a653cc30f16e
- Validação pendente: compilação Unity e teste de tiro contra jogador/NPC com obstruções e colliders sobrepostos. A otimização não altera a precisão humana configurada pela dispersão e pelo tempo de reação.


### 2026-10-11 — primitiva de posicionamento em terreno
- `MobileNPCSpawnService.TryFindSafeGroundPosition` agora resolve uma coordenada candidata para o chão usando raycast descendente, máscara de colisão configurável, limite de inclinação e validação de limites/clip volumes do mapa.
- É independente do nome ou tamanho do mapa carregado, desde que o chamador forneça coordenadas candidatas e uma `groundMask` adequada. Não escolhe sozinho posições pelo mapa, não valida ocupação completa do capsule e não faz spawn automático; isso continua sendo responsabilidade do futuro population director.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/c88ed83da6425f3d80c486856a2a78282d8a6f2b
- Validação pendente: testar máscaras de colisão em mapas diferentes, declives, bordas/clip volumes, terreno e estruturas. Ainda não houve compilação Unity nem teste em jogo.


### 2026-10-11 — validação defensiva antes de instanciar NPC móvel
- `MobileNPCSpawnService.TrySpawnServer` rejeita coordenadas não finitas, quaternions inválidos/zerados e valores de facção fora do enum antes de chamar `Instantiate`.
- A validação ocorre antes da criação do objeto, reduzindo o risco de instâncias malformadas ou configurações de facção inválidas deixarem entidades parciais no mundo. O fluxo de NPCs estáticos não é tocado.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/54643483996d2254ec72b588e962a14d8876855f
- Validação: revisão estática do fluxo; ainda faltam compilação Unity, teste de servidor dedicado e teste multiplayer. Isso não confirma que o NPC esteja pronto.

- Complemento: quaternions válidos são normalizados antes da instanciação para evitar que pequenos desvios numéricos contaminem a transformação inicial. Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/5acad989cb9bd9e459c8bc56b9050e9edc5e65bb


### 2026-10-11 — lote limitado de spawn em uma região
- Adicionado `MobileNPCSpawnService.TrySpawnBatchAroundServer`: recebe um centro regional explícito, raio, quantidade desejada, limite de tentativas, máscara de terreno e papel de facção; sorteia candidatos em distribuição circular e reutiliza a resolução de solo seguro antes de cada spawn.
- A API exige servidor, lista de saída fornecida pelo chamador, raio positivo e limite de tentativas de no máximo 1000. Informa quantidade realmente criada e aceita sucesso parcial para que o chamador possa decidir se tenta completar o lote depois.
- Limites deliberados: ainda é spawn local no servidor, sem replicação de rede; o centro regional e a máscara de terreno ainda precisam ser fornecidos por uma integração de mapa. A primitiva não verifica ocupação completa do capsule nem espaçamento entre os NPCs, não escolhe sozinha regiões do mapa e não mantém população ao longo do tempo.
- Commits: https://github.com/pplopes2003-design/Unturned-Better/commit/9a91e86f331c6f034d78c9948bc2a3bcd33185d8 e https://github.com/pplopes2003-design/Unturned-Better/commit/b774d680b559f3071e88a6bf7e34da5c8e005d39
- Validação: revisão estática apenas; sem compilação Unity ou teste dentro do jogo.

### 2026-10-11 — espaçamento mínimo dentro do lote regional
- O lote agora rejeita candidatos próximos demais de NPCs que já foram criados no mesmo lote. O limiar horizontal deriva do raio do `CharacterController` do prefab, com um mínimo de 1,25 m.
- Isso reduz sobreposição imediata entre membros do lote sem alterar NPCs já existentes. É uma heurística de distância no plano XZ, não uma verificação completa de cápsulas, obstáculos ou NPCs que já estavam no mundo.
- Commit: https://github.com/pplopes2003-design/Unturned-Better/commit/d00c5c26615c4f2a0aaeec831c7b8782d2bef6f2
- Validação: inspeção estática do código; compilação Unity e teste em mapas diferentes continuam pendentes.


### 2026-10-11 — controlador de população local (single-player first)
- Adicionado `MobileNPCPopulationDirector`, componente opcional que, quando colocado num objeto de bootstrap ativo e configurado com um prefab humanoide dedicado, tenta criar um NPC por intervalo em um anel aleatório ao redor do jogador vivo.
- O controlador limita a população ativa, limita tentativas por ciclo e delega a validação de solo, inclinação, limites do mapa, ocupação e espaçamento ao `MobileNPCSpawnService`.
- Adicionada limpeza dos corpos criados pelo próprio controlador após um tempo configurável, para que mortes não acumulem objetos indefinidamente.
- Arquivos: `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCPopulationDirector.cs`, metadados Unity e `Docs/MobileNPC-Population-Setup.md`.
- Limites: o componente ainda precisa ser instanciado/anexado por um bootstrap de runtime e receber um prefab configurado; não descobre sozinho limites/regiões do mapa e não persiste estado entre sessões. Não adiciona replicação multiplayer.
- Validação: revisão estática apenas. Unity/Unturned não foram compilados nem executados neste ambiente. Próxima tarefa: integrar bootstrap/prefab no ciclo real de carregamento de mapa e fazer teste de single-player antes de ampliar comportamento.


### 2026-10-11 — reconhecimento do prefab móvel e validação das assinaturas

- Conferidas as assinaturas chamadas por `MobileNPCPopulationDirector`: lote de spawn, despawn, cópia do registro ativo e propriedade `MobileNPCHumanoidVitals.IsDead` correspondem aos arquivos atuais. A inicialização do motor também inicializa o driver cerebral.
- Inspecionados os prefabs legados em `Assets/Resources/Characters`: `NPC_Server.prefab` é apenas um objeto raiz com Transform no arquivo disponível; `NPC_Client.prefab` é uma hierarquia visual/esquelética, não um agente móvel pronto; `Player_Server.prefab` pertence ao fluxo de jogador e não será reutilizado como NPC genérico.
- Documentada a decisão em `Docs/MobileNPC-Prefab-Recon.md`: não ativar bootstrap automático nem apontar o diretor para um prefab legado antes de criar e validar um prefab móvel dedicado.
- Próxima tarefa: criar o prefab dedicado reutilizando apenas a hierarquia visual compatível; configurar colisão, rig/Animator, componentes de IA e sons; depois integrar o bootstrap ao evento de carregamento de mapa.
- Validação: revisão estática via GitHub. Não houve compilação ou execução no Unity/Unturned; NPCs estáticos e zumbis não foram alterados nesta etapa.


### 2026-10-11 — bootstrap de população e adaptação visual em runtime

- Criado `MobileNPCPopulationBootstrap`, que se registra em `Level.onPostLevelLoaded` e tenta inicializar a população apenas em sessão server local, não em servidor dedicado.
- O bootstrap carrega `Resources/Characters/NPC_Client` apenas como hierarquia visual de um template runtime; não altera o prefab original. Os colliders e o Rigidbody do modelo são desativados e os MonoBehaviours legados do modelo ficam desabilitados.
- O template recebe os componentes de vida, percepção, movimento, disposição, mira e combate. O diretor é configurado em runtime e o serviço de spawn ativa cada cópia do template antes de inicializar seus componentes.
- Adicionado `MobileNPCVisualAnimation`, adaptador best-effort para reutilizar clipes do componente `Animation` legado, escolhendo estados com nomes relacionados a idle/walk/run quando disponíveis.
- Commits recentes: bootstrap `a930529b`, metadados `d7a23cc1`, animação `009d8025` / `8f42a42a`, conexão com diretor/spawn `ab5a991d` / `96897f6f` / `e5cf75d0` / `8efefb34`.
- Limites conhecidos: o spawn é local e não replicado; a navegação não faz pathfinding; não há persistência, exploração autônoma ou construção de bases. A população inicial é centrada no jogador. A seleção de animação é heurística e sons ainda precisam de integração/validação.
- Validação: revisão estática via GitHub; Unity não foi compilado nem executado. O teste de fumaça no Editor/Unturned é obrigatório antes de tratar o bootstrap como funcional.

### 2026-10-11 — primeiros waypoints de exploração autônoma
- `MobileNPCHumanoidMotor` agora escolhe pontos de caminhada aleatórios dentro de um raio configurável da posição inicial quando o cérebro não tem destino de combate, investigação ou objetivo tático.
- Os candidatos são limitados a oito tentativas por escolha e usam `MobileNPCSpawnService.TryFindSafeGroundPosition` para validar superfície, inclinação e limites/clip volumes do mapa. Após chegar, o NPC faz uma pausa aleatória antes de escolher outro ponto; falhas também aplicam espera, evitando busca contínua a cada frame.
- Destinos do cérebro/táticas sempre têm prioridade e cancelam o waypoint de exploração. O comportamento é servidor-local e não modifica NPCs estáticos nem zumbis.
- Limite importante: isto é navegação por direção direta entre waypoints, **não pathfinding**. Muros, edifícios e obstáculos podem bloquear o trajeto; o terreno/máscara de colisão padrão ainda precisa ser verificado em mapas reais. Construção de bases, coleta de recursos e persistência continuam pendentes.
- Guia de smoke test atualizado para verificar a exploração e as pausas.
- Validação feita nesta etapa: revisão estática dos símbolos usados e atualização de código/documentação no GitHub. **Não houve compilação Unity nem execução no jogo**, então a funcionalidade ainda precisa de teste real.

### 2026-10-11 — desvio local de obstáculos
- `MobileNPCHumanoidMotor` agora faz um `SphereCast` curto na direção desejada. Se houver obstáculo próximo, testa direções laterais e escolhe uma que esteja livre; se ambas estiverem bloqueadas, interrompe o movimento naquele frame em vez de avançar diretamente contra a colisão.
- A lógica é local e limitada: não calcula rotas, não usa NavMesh e não garante contornar edifícios ou paredes longas. Máscaras de colisão e altura do probe precisam ser validadas no Unity, pois colisores de cenário e triggers variam entre mapas.
- O guia de teste agora inclui um teste específico perto de obstáculos.
- Verificação estática: chaves/parênteses balanceados e uso de overload padrão de `Physics.SphereCast`. Sem compilação Unity nem teste em jogo nesta etapa.

### 2026-10-11 — recuperação de bloqueio e passos humanoides vanilla
- O desvio local testa rotas laterais mais amplas (90°, 135° e retorno) quando os dois probes iniciais estão bloqueados, evitando uma parada imediata em alguns cantos. Os ângulos de fallback são reutilizados sem alocação por frame.
- Foi adicionado monitoramento de deslocamento em janelas de 0,75 s. Se um waypoint de exploração continuar bloqueado, ele é descartado e uma nova escolha ocorre após breve pausa; se o destino vier do cérebro (combate/investigação/tática), o destino é preservado e o motor tenta temporariamente uma direção alternativa de recuperação.
- MobileNPCVisualAnimation agora consulta o material do chão e reutiliza PhysicMaterialCustomData com as chaves vanilla FootstepWalk/FootstepRun, OneShotAudioParameters e intervalos com pequena variação. A reprodução fica excluída da compilação dedicada por #if !DEDICATED_SERVER.
- Isto reutiliza os passos humanos existentes do jogo; não cria clipes novos. Sons de dor/morte/voz e áudio de armas continuam pendentes, pois ainda não foi validada uma referência de áudio humano adequada para esses eventos.
- Validação: revisão estática dos caminhos e assinaturas existentes no código de PlayerMovement/Zombie; commits no GitHub. **Sem compilação Unity ou teste real no Unturned**. O comportamento de áudio e a navegação ainda precisam ser confirmados em jogo.

### 2026-10-11 — tentativas de população em setores alternativos
- O diretor de população agora tenta até quatro setores ao redor do jogador por rodada, com ângulo aleatório e distância variável, antes de concluir que não encontrou um local seguro.
- O orçamento máximo de tentativas é dividido entre os setores; cada candidato continua passando pelo serviço de spawn existente, que valida terreno, inclinação, limites do mapa, ocupação e espaçamento dos NPCs.
- O raio de distância variável mantém o spawn fora da proximidade imediata do jogador, mas **não garante ocultação visual**; ainda é necessário testar visibilidade e distribuição em mapas reais.
- Se nenhum setor funcionar, a próxima rodada continua limitada pelo intervalo normal de spawn, sem repetição por frame.
- Validação estática: delimitadores balanceados e revisão das condições de servidor/limites de tentativas. **Sem compilação Unity ou teste em jogo.**


### 2026-10-11 — rejeição de spawn visível ao jogador
- Adicionada uma sobrecarga compatível de `TrySpawnBatchAroundServer` que aceita um observador opcional; chamadas antigas mantêm o comportamento anterior.
- O diretor de população passa o jogador como observador. Antes de instanciar o NPC, cada candidato é rejeitado quando um raio aproximado entre a posição dos olhos do jogador e a altura do tronco do NPC não encontra cobertura. Não há spawn seguido de destruição, evitando o efeito de aparição rápida.
- A verificação é conservadora e baseada nos colliders da cena: vegetação sem collider, transparências, geometria especial e ângulos de câmera podem produzir resultados diferentes. O orçamento de tentativas continua limitado; em áreas abertas o diretor pode não encontrar ponto oculto naquela rodada.
- Validação estática: revisão de overloads e chamadas, sem alteração do caminho legado. **Ainda não houve compilação Unity nem teste em jogo.**


### 2026-10-11 — âncora de território doméstico para NPCs
- O motor agora expõe a posição-base do NPC, o raio do território doméstico e uma consulta para saber se o NPC está dentro dessa área.
- Foi adicionada a operação de servidor `TrySetHomePositionServer`, que só atualiza a âncora depois de validar terreno, inclinação e limites usando o serviço de posição segura já existente. Ao alterar a base, o waypoint de exploração atual é descartado.
- A exploração limita seus destinos ao menor valor entre o raio de roaming e o raio territorial. A âncora é um ponto de referência em runtime; isto **não** implementa propriedade de terreno, construção, persistência em disco ou retorno forçado durante combate.
- Pesquisa de APIs do mundo: `ItemManager.findSimulatedItemsInRadius` e `ItemManager.getItemsInRadius` existem, mas a coleta vanilla termina em `Player.inventory` e no fluxo de requisição do jogador. Não conectei o NPC a esse caminho para evitar invocar indevidamente a interface/inventário do jogador; a coleta real requer um inventário próprio e uma remoção de item autorizada pelo servidor.
- Validação estática apenas; sem compilação Unity ou execução no jogo.


### 2026-10-11 — retorno ao território doméstico
- O motor agora define a âncora doméstica como destino de recuperação quando o NPC está fora do raio territorial e não existe destino ativo do cérebro.
- Perseguição, investigação e objetivos táticos mantêm prioridade; ao terminar o destino do cérebro, a verificação territorial volta a atuar. O retorno usa o mesmo CharacterController, gravidade e desvio local — não teletransporta o NPC.
- Limite conhecido: obstáculos grandes podem impedir o retorno porque o motor ainda não possui pathfinding completo. A âncora permanece apenas em runtime e não é persistida.
- Pesquisa do fluxo de coleta: `ItemManager.ReceiveTakeItemRequest` exige um `Player` válido e transfere o item para `player.inventory`. Não reutilizar esse RPC para NPCs. A coleta permanece bloqueada até uma operação de transferência própria que remova o item do mundo e registre o conteúdo do inventário NPC de forma atômica.
- Validação estática do fluxo e das prioridades; **sem compilação Unity ou teste no jogo**.


### 2026-10-11 — primeira camada de inventário NPC
- Adicionado `MobileNPCInventory`, um armazenamento server-local com capacidade configurável, slots, cópia defensiva do estado dos itens e operações server-only para inserir/remover snapshots.
- Contrato importante: `TryStoreTransferredItem` só aceita um item cuja posse já foi transferida exclusivamente para o NPC. O componente **não** recolhe nem remove drops do mundo; chamar a inserção enquanto o drop ainda existe duplicaria o item.
- Inspecionando `ItemManager.ReceiveTakeItemRequest`, confirmei que o caminho vanilla exige `Player`, usa `player.inventory` e só remove o drop após `tryAddItem` bem-sucedido. Não é apropriado para um NPC sem inventário de jogador. A integração de pickup requer uma operação de servidor que remova um `ItemData` específico por `instanceID` e o transfira ao inventário NPC de forma atômica, além de sincronizar a remoção visual nas regiões.
- A capacidade atual conta cada item recebido como um slot, sem empilhamento, equipamento, persistência, saque automático ou replicação.
- Validação estática apenas; sem compilação Unity ou teste em jogo.

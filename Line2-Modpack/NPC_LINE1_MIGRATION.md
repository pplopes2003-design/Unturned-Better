# Atualização — protótipo Steam de NPCs (código-fonte, ainda NÃO VALIDADO)

O novo `ClientModule/SteamNpcPrototypeRuntime.cs` implementa `ILine2NpcRuntime` e foi registrado em `Nexus.cs`. Ele tenta instanciar o recurso humanoide nativo `Resources/Characters/NPC_Client` (cuja existência foi confirmada no SDK base, não ainda na build Steam local). Usa `CharacterController`, material/pele via `HumanClothes`, detector de animação nativa `PrototypeAnimation`, nomes visíveis a até 17 m, escolha limitada de terreno com Physics e movimento simples com checagens de colisão em intervalos.

A implementação é **exclusiva de singleplayer e opt-in**: exige `Provider.isServer`, sessão não dedicada, cheats ativos, mapa carregado e `!u2 npc on`; então `!u2 spawn friendly|neutral|hostile [1..5]` tentará criar atores. `!u2 natural on` solicita um novo ator por ciclo de ~18 s até o limite configurado; `!u2 max N` até 32 altera imediatamente o limite; `!u2 npc off` limpa protótipos. Se o prefab nativo não existir, o spawn **falha sem criar cápsulas brancas de substituição**.

**Não é o NPC final**: os atores têm roaming e seguimento/perseguição rudimentares, mas não possuem armas, dano, morte, diálogo, comércio, navegação indoor, portas, facções vivas, rádio nativo, multiplayer ou persistência no save. Um spawn `PROTOTYPE_SPAWN_OK` indica apenas que um GameObject local foi criado. Falta confirmar compilação e execução contra DLLs da Steam; as assinaturas de `HumanClothes`, `NPC_Client` e demais APIs podem variar na instalação real. Ainda não solicitar bateria final de gameplay ao usuário.

A compilação passou a referenciar `UnityEngine.AnimationModule.dll` e `UnityEngine.IMGUIModule.dll`, exigidos pelo adaptador visual e pelo painel F7. Esses componentes não devem ser confundidos com NPCs permanentemente integrados à engine.

---

# Linha 2 — Migração de NPCs da Linha 1 para o Unturned Steam

## O que foi verificado no código antigo

Branch **somente de leitura** `feature/npc-hybrid-integration` (não modificar).

- `Assets/Runtime/Assembly-CSharp/Unturned/Command/CommandMobileNPC.cs`: comando original `/mobilenpc spawn/hostile`/friendly/neutral; dependia de `MobileNPCPopulationDirector`.
- `.../NPCs/MobileNPCSettings.cs`: configuração persistente de população, modo normal/combate/combate-only, distribuição por facções e flags.
- `.../NPCs/MobileNPCPopulationDirector.cs`: spawn, diretor populacional, limite, morte/limpeza e respawn atrasado; depende de prefab humanoide registrado.
- `.../NPCs/MobileNPCHumanoidMotor.cs`: controlador com CharacterController, navegação/obstáculos e código de geometria; dependia de componentes privados da build Unity.
- `Assets/Resources/Characters/MobileNPC_Client.prefab`: prefab próprio **da build Unity**, não necessariamente incluído no Unturned Steam.
- Subsistemas também identificados: `MobileNPCBrainDriver`, `MobileNPCDoorNavigation`, `MobileNPCEquipment`, `MobileNPCDamageBridge`, `MobileNPCFaction`, `MobileNPCGroupMember`, `MobileNPCAllianceService`, `MobileNPCDeathLoot`, `MobileNPCHumanoidCombat`, `MobileNPCPersistence` e navegadores híbridos.

**Atenção:** simplesmente copiar essas classes para `ClientModule` não funciona: são códigos integrados ao Assembly-CSharp e recursos da build original. O Steam exige empacotamento/registro de prefab, autorização de servidor, sincronização e adaptação de APIs.

## Interface integrada disponível agora

`ClientModule/Line2AdminControl.cs`, inicializado por `Nexus.cs`:

- **F7**: menu administrativo local em jogo.
- **Chat:** `!u2 help`, `!u2 status`, `!u2 npc on`, `!u2 npc off`, `!u2 natural on`, `!u2 natural off`, `!u2 max 8`, `!u2 combat passive`, `!u2 combat normal`, `!u2 combat total`, `!u2 hordes on`, `!u2 hordes off`, `!u2 population`, `!u2 server`.
- `!u2 spawn friendly`, `!u2 spawn neutral`, `!u2 spawn hostile`, `!u2 spawn hostile 3` — **reconhecem e validam o pedido, mas retornam SPAWN_UNAVAILABLE** até existir um adapter validado; não criam entidades falsas.
- Configuração salva em `Unturned2-Line2/admin-settings.json` no diretório de trabalho do jogo.
- Limites: 1 a 32 NPCs (configuração) e até 5 solicitados por comando, para evitar lotação.
- Segurança: somente sessão com autoridade local e sem servidor dedicado; não permite configuração remota, nem altera spawn/limite de zumbis.

O prefixo `!u2` foi escolhido para não disputar nomes de comandos oficiais do jogo. O chat precisa passar pelo evento `ChatManager.onChatted`, ainda **não validado na versão Steam do usuário**. O F7 é uma segunda entrada, também não validada. `/mobilenpc` da Linha 1 não é registrado na Linha 2.

## Bloqueadores reais do jogo (sem atalhos falsos)

1. **Compilar e carregar módulo Steam**: usuário tinha `No .NET SDKs were found`; o binário atual não foi confirmado. Config/menu só existem no código-fonte até essa verificação.
2. **Prefab humanoide compatível**: identificar forma permitida de empacotar visual humanoide com animação, hitboxes, equipagem e identificação nas APIs Steam; não assumir `Resources/Characters/MobileNPC_Client.prefab` na instalação oficial.
3. **Spawn autoritativo**: registrar entidades, conservar identidade e vida/morte, evitar sobreposição e sincronizar nos modos host/cliente.
4. **Mobilidade e pathfinding**: portar em etapas internas os algoritmos da Linha 1, substituindo dependências privadas e validando nós/passagens em builds Steam.
5. **Percepção/comportamento/combate**: ligar `AIService` com snapshots reais, limitar taxas, validar reação e autoridade de dano.
6. **Grupos, bases, diálogos, comércio, persistência**: reutilizar lógica sem copiar serviços do Assembly-CSharp sem adaptação.
7. **Hordas e zumbis entre regiões**: portar apenas mecanismos com limite vanilla preservado; evitar respawn adicional e atualizações caras por frame.

## Aceitação única solicitada

Faremos internamente build/testes de API e automações. Só oferecer **uma bateria de gameplay completa ao usuário** quando todos os pontos de spawn, visão, combate, diálogo, persistência, zumbis e performance estiverem conectados. Critérios detalhados: `NPC_INTEGRATED_ACCEPTANCE.md`.

A entrega atual **não contém NPCs móveis** nem spawn natural realmente ativo; os comandos preparam configuração, não substituem o sistema de entidades. Não afirmar funcionalidade até haver log de teste real.

## Migração incremental interna (sem testes fragmentados com o usuário)

A lógica de população/grupos agora possui implementação Python isolada `AIService/npc_population_model.py`. Regras cobertas: ID único, limite máximo, papéis friendly/neutral/hostile, grupos com integrantes vivos, morte definitiva de identidade, reposição retardada, reentrada em grupo ainda vivo e extinção quando todos os integrantes morrem. Os testes estão em `AIService/test_npc_population_model.py`. Esse modelo **não possui adapter Steam**: não spawna GameObjects, não participa do ciclo de vida Unity nem altera saves do Unturned. O JSON exportado é um contrato inicial de serialização e não grava automaticamente arquivos.

A investigação de arma também foi ampliada com **F6**: `ClientModule/GunApiDiscovery.cs` escreve `gun-api-discovery.txt` com APIs/assinaturas de `UseableGun`, `PlayerEquipment`, `PlayerInput` e `ItemGunAsset`, sem executar métodos. O agendador `RepeatingBurstScheduler` permanece somente como política de cadência; ainda não dispara a Eaglefire no jogo. O objetivo é integrar o disparo **original**, com munição, dano e validação de autoridade corretos.

Fases internas restantes antes da bateria única solicitada: compilar módulo e capturar APIs Steam; criar/empacotar prefab compatível, implementar `ILine2NpcRuntime` e `IGameNpcAdapter`; conectar navegação, combate, persistência e facções; teste de integração com Unturned Steam e desempenho.

## Rádio NPC (migração de modelo de dados, 2026-11-07)

As regras encontradas em `MOBILE_NPC_RADIO_SYSTEM_DESIGN.md` da Linha 1 e em `MobileNPCRadioOutbox.cs` foram transpostas para `AIService/npc_radio_network.py` com testes `AIService/test_npc_radio_network.py`.

- Walkie Talkie vanilla 1445 exigido para transmitir; escuta por walkie 1445 ou earpiece 1446 **efetivamente equipado**.
- Frequência base 460327; facções organizadas compartilham canal determinado pela facção, enquanto grupos sem facção usam um canal distinto por grupo.
- Transmissões somente para NPCs vivos com rádio/receptor e frequência/facção compatíveis. O plano usa só observações locais **verificadas**, sem visão telepática nem disparos por posição recebida.
- Setores aproximados de 100 m, TTL limitado, 4 mensagens pendentes por emissor, intervalo de 20 s para observações, 8 s por emissor e 1,25 s entre transmissões globais.
- Mensagens cobrem `enemy_contact`, `reinforcements`, `useful_loot`, `airdrop` e `unknown_base`. Aviso de reforços **não cria NPCs** nem altera orçamento populacional.
- Morte, remoção de walkie, troca de grupo/facção ou validade expirada bloqueiam transmissões indevidas.
- Testes também rejeitam `verified="yes"` (string verdadeira enganosa) e mensagens com data futura.

**Estado real:** isso é lógica Python com contrato de entrada e testes, ainda **não está conectado a `PlayerVoice`, aos itens físicos de NPCs Steam, ao rádio do veículo, à rede de áudio ou ao HUD do jogador**. O interceptador na Linha 1 era texto local condicionado a equipamento/frequência, não transmissão nativa de voz de NPC. Não classificar essa migração como concluída enquanto não existir o adaptador de entidades, rádio equipado e sincronização em jogo.

Outros sistemas ainda necessários: comportamento de zumbis cross-region, spawn/população real, controle de armas e navegação 3D, facções/settlements, bases/loot, interface/dialogo, save/load de mundo e testes de performance.

## 2026-11-07 — Primeira ponte de observações/decisões de NPCs do Steam + prioridade zumbis

**Alterações aplicadas ao código da Linha 2 nesta rodada:**

- `LiveNpcDecisionBridge.cs` foi registrado em `Nexus.cs` e consulta `http://127.0.0.1:8765/v1/decide_batch` a ~2 Hz, somente com até 32 `PrototypeActor` reais. Serializa observações do Unity na thread principal e executa HTTP no worker, com timeout, payload máximo, validade, tick e identidade verificados antes de aplicar decisões.
- `SteamNpcPrototypeRuntime.cs` expõe `CopyLiveActors`, e `PrototypeActor` mede visibilidade real a 20 m e registra por até 8 segundos o último local do jogador. As ações `idle`, `engage` e `investigate` influenciam exclusivamente a movimentação dos **protótipos hostis**, com modo `passive` respeitado. Em caso de perda do servidor Python, mantém-se fallback local. Sem dano/combate real, `seek_cover` e `retreat` são recusadas.
- `PrototypeObstacleNavigator.cs` substitui o bloqueio frontal total por desvio lateral de curto horizonte, consultas físicas limitadas e lado de manobra estável. Isso **não** é uma malha navegável de edificações nem interação com portas.
- `ZombiePriorityVisionBridge.cs` usa `Zombie.checkAlert` e `Zombie.alert` para dar prioridade ao jogador visível a 20 m entre zumbis **já presentes**, máximo de 24 avaliações distintas por segundo, em singleplayer/cheats; ativação opcional `!u2 zombies on`, desligada por padrão, sem alterar limite vanilla.
- `ZombieTrackingProbe.cs` F11 agora usa `Regions.tryGetCoordinate` e dados tipados de `Zombie` para diferenciar corretamente **região espacial x/y** de **spawnBound**. Registra perseguição, movimento, desaparecimento e passagem entre regiões.
- `Line2AdminControl.cs` mostra o status da IA ao vivo, população de protótipos e comandos de zumbis. `WorldScanner.cs` passou a permitir relatório F6 de armas. `AIService/server.py` emite TTL de 600 ms nos lotes NPC. O check de SDK fonte e os testes de protocolo foram ampliados.

**Não implementado nesta rodada**: NPCs com vida/dano e morte, animações de combate e armas, rádio físico, diálogos, comércio, fome/sede, grupos/bases vivos no Unity, IA de fuga/cobertura, controle real dos zumbis por região descarregada, hordas vanilla reagrupadas, persistência no save, multiplayer LAN/Internet. Esses componentes da Linha 1 ainda requerem adaptação, mesmo que modelos Python auxiliares existam.

**Status de execução:** commits enviados; C# **não compilado nem executado contra a instalação Steam local**, e resultado da pipeline Actions ainda não verificado como verde. Não declarar entrega final ou recomendar bateria de gameplay antes desses gates.

## 2026-11-07 — Cérebro C# de decisões e audição de tiros nativos

Para evitar que o AI Service seja a única fonte de comportamento, adicionei `ClientModule/NpcDecisionBrain.cs`: máquina de estados **local, determinística e independente do Unity**, com intenções `Patrol`, `Follow`, `Investigate`, `Pursue`, `SeekCover` e `Retreat`. A máquina possui histerese curta (mínimo de 0,45 s entre mudanças não urgentes), memória limitada a evidências observadas, prioridade de ameaças visíveis e regras de modo passivo. Recebe apenas fatos de sensores autorizados, não alvos arbitrários de respostas HTTP. Foi integrada ao `PrototypeActor` para selecionar deslocamento por perseguição, última posição, patrulha e seguimento.

O novo `NpcAcousticSensorBridge.cs` escuta `UseableGun.onBulletSpawned` do Unturned e encaminha até 32 NPCs próximos, a no máximo 80 metros, um setor aproximado de 12 metros como pista auditiva com validade de até 7 segundos. Conta apenas `pellet == 0` e limita a frequência dos eventos para não multiplicar trabalho por rajada/escopeta. O NPC não recebe a localização exata do jogador, não vê através de paredes e não inventa disparos quando o serviço Python está desligado.

O núcleo tem autoteste `NpcDecisionBrainSelfTest`, iniciado em `Nexus.initialize()`, e projeto de compilação/teste separado em `BrainTests/BrainTests.csproj` para GitHub Actions Windows. Esse teste valida as regras do cérebro C# **sem depender do jogo**, mas **não valida a DLL Steam**, o spawn, a física, as animações ou o combate.

**Limites intencionais:**
- `SeekCover` e `Retreat` são decisões disponíveis na máquina, **não comportamentos jogáveis**: o protótipo não possui saúde real, reação a balas recebidas nem seleção de cobertura verificável.
- `Pursue` significa que o personagem se move em direção ao inimigo visível, não que saiba disparar, causar dano ou abrir uma porta.
- A navegação ainda usa steering local, não rotas indoor com análise de prefab/portal e collider.
- Rádio/facções, inventário e comércio, persistência, zumbis entre regiões, hordas e servidor integrado ainda não estão conectados ao runtime dos personagens.
- Nenhuma compilação da DLL contra Unturned Steam ou validação de gameplay foi confirmada nesta rodada. Não considerar a migração total da Linha 1 concluída.

**Próxima integração prioritária dentro do mesmo pacote de NPCs:** adaptador de vida/dano e morte, armas e animações reais, detecção/uso de portas e cobertura, navegação indoor/outdoor com rotas, facções e rádio físico, persistência e orçamento de desempenho. A bateria de gameplay do usuário será **uma só**, quando esses recursos estiverem integrados e validados.

## 2026-11-07 — Revisão sistemática de todas as branches (A* e explosões)

A auditoria detalhada está em **[LINE1_BRANCH_AUDIT.md](LINE1_BRANCH_AUDIT.md)**; compara o cérebro e executores da Linha 1 das branches `backup/alife-survival-2026-10-16`, `feature/hybrid-npc-navigation`, `feature/integrated-friends-host`, `feature/npc-hybrid-integration`, `feature/release-candidate-cycle-2026-11-04` e `feature/steam-modpack-local-ai-prototype-2026-11-04`. **A Linha 2 não contém todas as funções antigas**.

**Mudanças efetivas nesta rodada:** adaptação do provedor nativo `MobileNPCNativeAStarRouteProvider` da Linha 1 para `Line2NativeAStarRouteProvider` (reflexão de `Pathfinding.Seeker` já instalado no Steam); `PrototypeAStarFollower` passa a usar seus waypoints quando a rota chega, com freio durante espera e fallback para o navegador leve se o backend A* não estiver disponível/falhar. O operador de movimento ainda é `CharacterController`; não há cópia ou distribuição do A* Pro.

Foi portado o seletor de decisões `Line2MobileNPCTactics` (a partir de `MobileNPCTactics.cs`), preservando metas de cobertura, recuo, flanco, espreita, reforços, reagrupamento, loot e staging de raid. Está ligado aos danos reais por explosão e ao planejador físico de cobertura/recuo (`PrototypeCoverPlanner`). As metas mais avançadas não têm ainda executores do jogo, então **não** são funcionalidades concluídas. O projeto `BrainTests` passou a compilar esse seletor usando stubs de tipos de valor do Unity, fora do módulo Steam, e testa os seus bloqueios básicos.

**Bug histórico de explosões:** o `DamageTool.explode` vanilla usa `RayMasks.AGENT` ao selecionar zumbis/animais e procura `IExplosionDamageable` na hierarquia do colisor. Sem ambos, uma granada pode ignorar um NPC. `PrototypeNpcVitals` agora implementa a interface, o spawn exige a camada física Agent, o dano respeita raio, queda e parede, registra HP e mata a entidade (despawn do corpo após intervalo); `NpcExplosionMath` tem testes determinísticos. Essa correção ainda exige **teste in-game** com explosivos do Steam e não implementa dano de bala nem saque de corpo.

**Riscos e gates:** backend A* pode não ter grafo em ambientes indoor e ainda precisa de lógica de portas; custom explosives que só geram dano a Player mas não consultam Agent não necessariamente encontram NPC; falta a integração das hitboxes e da autoridade de dano completo. Nenhuma compilação Steam validada; o protocolo GitHub Actions está em atualização, não presumir sucesso.

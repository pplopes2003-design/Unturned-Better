# Multiplayer integrado — Unturned Better (Steam Server Code + LAN)

## Hospedagem pela internet — implementação Steam Server Code

- Menu `Jogar > Hospedar amigos`: alterna entre **Internet via Steam (com senha)** e **LAN para diagnóstico**. O padrão é Internet. A validação exige senha ao escolher internet.
- Internet inicia **o build dedicado modificado** com `+InternetServer/UnturnedBetterFriends`, mantendo as flags `-batchmode -nographics`. LAN usa `+LanServer/UnturnedBetterFriends`. A Steam e o próprio Unturned são responsáveis por autenticação e transporte; não existe relay próprio do projeto.
- **O código não é calculado pelo cliente:** após `Provider.isServer`, `Level.isLoaded` e `SteamGameServer.GetSteamID().BGameServerAccount()`, o processo dedicado salva o Server Code real em `Servers/UnturnedBetterFriends/Server/ServerCode.dat` (arquivo local, conteúdo somente número, escrita via arquivo temporário). Somente o modo Internet publica o código. A inicialização seguinte apaga o código anterior antes de executar o servidor.
- O menu consulta o arquivo a cada segundo e valida que ele representa uma conta de game server da Steam antes de exibir. **Copiar Server Code** usa a área de transferência do sistema.
- **Convidar amigos Steam** abre o diálogo nativo `SteamFriends.ActivateGameOverlayInviteDialogConnectString("+connect " + code)` para selecionar amigos, através de rich presence de servidor (não é um lobby). O código é incluído no convite; **a senha não é incorporada**, para evitar exposição por argumentos/logs. O convidado precisará da senha por outro canal e poderá usar `Jogar > Conectar` manualmente se a tentativa automática falhar. O overlay pode não abrir em builds sem a integração Steam efetiva.
- **Conectar ao meu servidor** usa `Provider.connect(new ServerConnectParameters(new CSteamID(code), password), null, null)` em modo internet; o botão LAN mantém a conexão local pela porta configurada.
- **Privacidade:** uma senha impede entradas sem credencial, mas um servidor do tipo Internet pode continuar visível para consultas Steam. Este projeto **não promete ocultação do navegador** nem que a build customizada será aceita pelos serviços Steam.
- A existência de um Server Code válido indica uma identidade de servidor, **não** prova conectividade relay, jogo carregado para ingresso, autenticidade da build ou teste ponta-a-ponta. Não compartilhar senha em arquivos de log ou relatórios.

### Como testar pela internet

1. Gerar **cliente Windows64** e **servidor Windows64_Headless** da **mesma revisão** desta branch. O servidor recebe a marca `UnturnedBetterDedicated.marker` criada por `BuildMethods`.
2. Abrir o cliente, escolher **Hospedar amigos**, selecionar **Internet via Steam**, preencher **senha** e demais configurações. Iniciar servidor.
3. Aguardar o painel exibir o **Steam Server Code** de 17 dígitos, depois **Copiar Server Code**; caso não apareça, verificar a inicialização Steam e o log do dedicado. Não tentar adivinhar o código.
4. O anfitrião usa **Conectar ao meu servidor**. O amigo abre a **mesma build modificada**, entra em `Jogar > Conectar`, digita o Server Code no campo de host e a senha fornecida.
5. Validar duas sessões reais: handshake de Steam, visibilidade e combate dos NPCs, sincronização de morte e loot, scripts dos dois lados, entrada tardia, saída/reconexão.
6. Salvar e encerrar pelo menu. Reiniciar o servidor e verificar se nenhum Server Code antigo aparece antes de uma nova autenticação.

**IMPORTANTE:** revisão de código e commits não substituem a execução da build; a compatibilidade real do Steam Relay e a autenticação de servidor neste fork permanecem pendentes de teste.

## Objetivo e estado

O menu **Jogar > Hospedar amigos** controla um **segundo executável** compilado do mesmo projeto Unturned Better com o alvo `DEDICATED_SERVER`. Não é um processo embutido no mesmo Unity Player e não usa mais um U3DS vanilla. O servidor autoritativo utiliza o protocolo nativo de conexão do Unturned.

**Estado:** código inicial implementado e enviado à branch `feature/integrated-friends-host`. NÃO compilado nem validado em Unity, Steam ou duas máquinas. A sincronização remota é um protótipo, não um multiplayer pronto.

## Gerar as duas builds a partir da mesma branch

1. Selecionar no Unity a branch `feature/integrated-friends-host` (ela foi criada a partir de `feature/npc-hybrid-integration`; incorporar futuras correções de NPC por merge antes da versão final).
2. Compilar o cliente de desenvolvimento (Windows 64), normalmente em `Builds/Windows64/Unturned.exe`.
3. Usar **Unturned Better > Build Matching Dedicated Server (Windows x64)** no menu do Unity. O BuildMethods existente usa `StandaloneBuildSubtarget.Server`, a define `DEDICATED_SERVER` e a saída `Builds/Windows64_Headless/Unturned.exe`.
4. A compilação dedicada bem-sucedida grava `Builds/Windows64_Headless/UnturnedBetterDedicated.marker`. O menu recusa servidores sem essa marca; evita lançar um Unturned Steam padrão que não contém nossas classes MobileNPC.
5. Certificar-se de que os dois builds têm **a mesma revisão do código**. A marca comprova somente origem pelo processo modificado, não igualdade de commit ou compatibilidade binária; o handshake real ainda precisa ser testado.
6. Abrir o cliente, selecionar **Hospedar amigos (LAN)**, escolher mapa (padrão Washington), senha, nome, porta, capacidade e **Modo NPC: Normal / Combate / Combate Total**. O cliente grava `MobileNPCGameMode.dat` na configuração do servidor. O bootstrap dedicado aplica esse modo antes de gerar/restaurar NPCs; o valor de PlayerPrefs do cliente não é usado como autoridade do servidor. A pasta da build dedicada é detectada automaticamente quando as saídas são irmãs; caso contrário, selecionar manualmente.
7. Clicar **Iniciar servidor**. O cliente cria `Servers/UnturnedBetterFriends/Server/Commands.dat` no diretório da build dedicada e inicia o processo com `-batchmode -nographics` e `+InternetServer/UnturnedBetterFriends` ou `+LanServer/UnturnedBetterFriends` conforme o seletor. Esse ato não certifica que o mapa já está carregado.
8. Depois da inicialização do mapa no console, clicar **Conectar ao meu servidor**, que consulta `127.0.0.1:<porta>` usando `MenuPlayConnectUI.connect`. Amigos na mesma LAN precisam usar o IP local da máquina e a porta, com firewall permitindo o tráfego.
9. **Salvar e encerrar servidor** solicita, por um arquivo local de controle, os comandos nativos `save` e `shutdown 5` no processo dedicado. Não usa `Process.Kill`; conferir o log de salvamento dos NPCs no fechamento.

## O que foi ligado ao servidor

- `MobileNPCPopulationBootstrap` não descarta mais `Dedicator.IsDedicatedServer`: a IA e os sistemas de dano, motor, percepção e persistência podem rodar no host como autoridades únicas. No multiplayer, o modo do servidor (Normal/Combate/Combate Total) é configurado explicitamente e não depende dos PlayerPrefs locais do usuário.
- A verificação de visual da população aceita que uma build Unity headless não possua malhas renderizáveis. O esqueleto e as seis regiões físicas continuam obrigatórios; se as transformações ósseas faltarem, a inicialização falha com diagnóstico.
- `MobileNPCNetworkReplica` envia snapshots periódicos (ID efêmero de instância, posição, ângulo horizontal, ID de arma e flags de movimento/corrida/recarga) via `ClientStaticMethod<...>` com validação `ONLY_FROM_SERVER`, pela rede nativa. Filtra receptores por proximidade (165 m) e limite de NPCs por atualização.
- Clientes remotos não instanciam um segundo cérebro de NPC. Criam *proxies visuais* baseados em `Resources/Characters/MobileNPC_Client`, com interpolação e seis caixas de raycast; objetos expirados e de mapas anteriores são destruídos.
- O cliente agora monta o modelo da arma real `ItemGunAsset` no hook correto e tenta executar clipes originais de marcha, corrida, disparo e recarga (quando `AnimationClip.legacy` estiver disponível). Não executa armas nem dano localmente.
- `MobileNPCNetworkCombatEvents` escuta `ShotFired`, `ReloadStarted` e `Died` reais do servidor. Disparo e recarga produzem RPCs de apresentação com áudio e animação. Morte retira o proxy da lista de vivos, instala tombstone por 20 s e tenta liberar 11 ossos físicos usando o layout nativo de `Ragdoll_Player` com descarte automático em 12 s. A física do corpo é apenas cosmética, sem loot ou autoridade no cliente.
- `MobileNPCNetworkReplica` também envia metadados confiáveis de nome e seed visual a cada 4 s, separadamente dos snapshots. A aparência é determinística por NPC; a placa de nome exige proximidade, câmera e ausência de obstrução. O pitch da cabeça é emitido junto aos snapshots a partir do alvo realmente observado/selecionado pelo servidor.
- `Assets/Runtime/Assembly-CSharp/NetGen/NetInvokable/MobileNPCNetworkReplica_NetMethods.cs` contém adaptadores READ/WRITE de TODOS os cinco RPCs. Sem esses adaptadores o sistema nativo `NetReflection` não registra métodos como `ClientStaticMethod` serializáveis. Mantenha assinaturas do método e do adaptador em paridade.
- `UseableGun` do cliente reconhece a caixa de acerto do proxy para apresentação do hitmarker; a lógica servidor `MobileNPCDamageBridge.TryResolvePhysicalServerHit` valida geometria/obstrução antes de aplicar qualquer dano. **O hitmarker local não é prova de dano confirmado.**

## Bloqueadores da validação completa

- [ ] Compilar ambos os executáveis e corrigir eventuais erros de Unity/assinaturas de RPC.
- [ ] Iniciar sessão com anfitrião e segundo cliente real na mesma LAN; confirmar handshake, atualização remota e entrada tardia.
- [ ] Confirmar que o prefab `NPC_Client` existe nas duas builds (headless precisa manter esqueleto) e que os bone hitboxes têm posição correta.
- [ ] Atirar de dois clientes no mesmo NPC, verificar dano exclusivamente no servidor, acerto através de obstáculo bloqueado e sincronização de mortes.
- [ ] Validar em dois clientes a **arma renderizada**, clipes de marcha/corrida, eventos de tiro/recarga e desaparecimento pós-morte. Esses mecanismos agora têm uma implementação preliminar, mas não foram executados.
- [ ] Verificar in-game aparência, nome, pitch da cabeça e ragdoll remoto nativo; o código existe, mas compatibilidade do rig e das classes do Unity não foram testadas.
- [ ] Replicar ainda **roupas/equipamento visual completos, animação avançada de braços, granadas, ferimentos, loot de cadáver e estado de grupos**. A posição e a arma não equivalem a estado completo de NPC.
- [ ] Impedir fantasma de NPC em disconnect/reconnect, mudança de mapa e servidor desligado, sob perda de pacotes.
- [ ] Definir persistência por save do dedicado e verificar Save/Shutdown com NPCs vivos, mortos e reabertura do mundo.
- [ ] Controlar orçamento de rede (128 snapshots por rodada no máximo, atualizações a cada 0,35 s) por relevância/região e com compressão/agrupamento se necessário.
- [ ] Diagnosticar sincronização de disparos/hitmarkers no modo Combate Total e no modo Normal, sem regressão do singleplayer.
- [ ] Adicionar convite Steam/relay ou acesso externo com NAT/portas/firewall e autorização. **LAN não equivale a internet.**
- [ ] Verificar no log do dedicado `[MobileNPCFriendsHost] Applied authoritative NPC mode` para cada modo e confirmar equipamentos, agressividade, sobrevivência e spawns.
- [ ] Testar performance, pois hospedar cliente Unity e servidor dedicado na mesma máquina requer CPU/RAM adicionais.

## Ciclo 1 — persistência dedicada e identidade (2026-11-04)

Alterações efetuadas nesta branch:
- `MobileNPCPersistence.NPCRecord.displayName` adiciona o nome individual ao formato de snapshot existente **sem incrementar a versão**: é um campo opcional para versões antigas. No restore, `MobileNPCHumanoidDisposition.RestoreDisplayNameServer` valida tamanho/caracteres antes de substituir o nome criado no spawn. Saves anteriores continuam sem nome explícito e adotam o nome gerado normalmente.
- Identidade cosmética remota passa a usar FNV-1a determinístico sobre o nome persistente, em vez de `GetInstanceID()` efêmero. Há possibilidade de aparência idêntica entre dois NPCs com o mesmo nome; não é um identificador de rede ou chave de save.
- `MobileNPCFriendsHostControlServer` chama `MobileNPCPersistence.FlushBeforeDedicatedShutdownServer()` **antes** do comando nativo `save`, seguido de `shutdown 5`. O flush mantém os bloqueios de segurança do sistema atual: não sobrescreve snapshots cuja leitura falhou, nem salva mapa ainda não restaurado. `SaveServer` passa a retornar o resultado da gravação para diagnóstico.
- O salvamento existente já contém atributos vitais, inventário físico, profissão, facção, grupo, âncora da base, peças construídas, memória individual, ferimentos e rotina. Construções físicas continuam sob a persistência de mundo nativa, sem cópias no JSON de NPCs.

### Trabalho ainda necessário neste ciclo

- [ ] Distinguir de forma persistente identidade única individual (não apenas nome) e separar aparência de colisões de nomes.
- [ ] Replicar metadados visuais de grupo/base e ferimentos necessários à interface remota sem expor memória tática/posições secretas de NPC.
- [ ] Garantir que todos os itens recusados no drop de morte sobrevivam a um despawn/encerramento: hoje `MobileNPCDeathLoot` mantém itens recusados no inventário do cadáver apenas enquanto o objeto existe. Considerar uma fila persistente de drops com limite e rollback transacional.
- [ ] Validar que `Provider.serverID`, `Application.persistentDataPath`, modo de jogo e mapa formam a chave correta em dois saves do servidor dedicado. Não migrar diretórios automaticamente sem backups.
- [ ] Auditar a ordem de restore dos grupos e construções nativas; manter a regra de não duplicar estruturas ao carregar o mundo.
- [ ] Testar escrita, reinício, erro de acesso ao disco, arquivo corrompido com backup e parada graciosa em Unity. Código não foi executado.

## Workshop — pendência formal

Integração planejada de mapas e itens da Steam Workshop por IDs de publicações, usando arquivos de dependência e download do próprio Unturned. O uso privado entre amigos não substitui as permissões dos autores para redistribuição.

- [ ] UI de seleção de mapas instalados / Workshop IDs.
- [ ] Criar e manter `Servers/<ServerId>/WorkshopDownloadConfig.json` com lista oficial `File_IDs`.
- [ ] Validar download e dependências antes de aceitar o mapa e antes de conectar clientes.
- [ ] Usar a distribuição legítima da Workshop, não embutir pacotes de terceiros no GitHub.
- [ ] Identificar colisões de IDs com os itens customizados 59010/59011.
- [ ] Reavaliar navegação, interiores e colisões nos mapas de terceiros antes de liberar NPCs.

## Critério para build final

Uma **build única para o jogador** pode agregar IA, armas e menu, mas hospedagem requer um **segundo executável headless compatível** distribuído junto à instalação. Todas as branches de funcionalidades deverão ser incorporadas e testadas na branch de integração final antes do build de release.

## Aceitação de eventos de combate — próxima Dev Build

- [ ] Servidor e cliente de MESMO commit: RPC `ReceiveNpcSnapshot` de seis argumentos e `ReceiveNpcIdentity`, `ReceiveNpcShot`, `ReceiveNpcReload`, `ReceiveNpcDeath` registrados sem falhas de reflexão/assinatura. Rodar a geração/validação `NetGen` caso o editor indique divergências de adaptadores.
- [ ] Dois clientes simultâneos, 10 NPCs hostis e 10 neutros: cada um enxerga os mesmos NPCs e armas, sem um segundo cérebro em cliente.
- [ ] NPC andando/correndo: os modelos seguem posições por interpolação sem gerar travas, com clipe nativo disponível.
- [ ] NPC disparando/recarregando: eventos do servidor produzem um áudio e uma ação visual no cliente, nunca consumo/dano client-side duplicado.
- [ ] NPC morrendo: o cliente remove o corpo vivo com evento confiável; snapshots UDP atrasados não recriam personagens; o ragdoll remoto usa o prefab físico `Ragdoll_Player`, mas ainda precisa de validação visual/colisões e limpeza aos 12 segundos.
- [ ] Jogador de cliente remoto atirando no NPC: validar o primeiro obstáculo e ponto informado com `MobileNPCDamageBridge.TryResolvePhysicalServerHit`, não confiar no hitmarker.
- [ ] Entrada tardia, afastamento além de 165 m, retorno, mudança de mapa e desconexão: proxies surgem/somem coerentemente.
- [ ] Conferir `[MobileNPCReplication]` em Player.log e log do servidor; nenhum teste real foi executado nesta alteração.

- [ ] Mover NPC de lugar e confirmar que face/cabelo e nome não mudam; reconectar cliente e verificar estado reconstituído.
- [ ] Confirmar que os NPCs não miram por meio de telemetria de cliente; o pitch só é derivado do estado selecionado pela autoridade.

## Nova rodada: granadas e equipamento visual — implementação de código

- `MobileNPCGrenadeThrower` notifica o servidor de replicação **somente após** a criação física da granada, a validação do `Rigidbody` e a retirada de um item real do inventário.
- `ReceiveNpcGrenade` envia o ID de asset, origem, velocidade e fuse da granada confirmada. O cliente instancia um **prefab físico visual**, **sem componente Grenade de dano/explosão**. Sua cópia é removida ao fim do fuse; as cópias restantes são limpas quando o jogador desconecta ou muda de mapa. O servidor continua sozinho responsável pelos efeitos reais e pelo dano.
- `ReceiveNpcOutfit` replica os IDs físicos de capacete/chapéu, camisa, calça, colete e mochila encontrados no inventário autoritativo. O cliente os aplica ao `HumanClothes` já existente, limpando os colliders extras de roupa, sem criar inventários paralelos.
- `MobileNPCNetworkReplica_NetMethods.cs` inclui agora **sete** pares de serialização READ/WRITE: snapshot, identidade, disparo, recarga, morte, granada e roupa. A revisão estática confirmou a mesma contagem de parâmetros nas assinaturas e o uso de codec `ReadClampedVector3/WriteClampedVector3` no projétil.

### Validação pendente (obrigatória)

- [ ] Build Windows x64 cliente e servidor headless da mesma revisão. Confirmar os sete métodos RPC carregados sem erro em `NetReflection`.
- [ ] Com dois clientes LAN, lançar granadas como NPCs: aparecer apenas **um projétil visível por evento**, e apenas a explosão autoritativa do servidor; validar trajetória, colisão, sincronização do efeito e dano a jogadores/zumbis/NPCs.
- [ ] Conferir lançamentos junto a paredes e jogadores aliados; a predição de queda e a política anti-dano amigo ainda não cobrem todas as situações reais de quique.
- [ ] Trocar de mapa/desconectar durante o fuse: nenhum projétil cosmético persiste em mundo descarregado.
- [ ] NPCs em Combate Total com capacete, colete e mochila: ambos os clientes devem visualizar os mesmos modelos; verificar `HumanClothes.apply` e bonecos com ragdoll sem collider fantasma.
- [ ] Inventário real versus equipamento vestido: o protótipo seleciona o **primeiro item de cada tipo no inventário**, pois ainda não existe um conjunto separado de slots de roupa dos NPCs. Não apresentar como seleção rigorosa de roupa equipada até adicionar esses slots.
- [ ] Testar 20–40 NPCs com dois jogadores para medir custo real das mensagens de identidade/roupa a cada quatro segundos e granadas/eventos.

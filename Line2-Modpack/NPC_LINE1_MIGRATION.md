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

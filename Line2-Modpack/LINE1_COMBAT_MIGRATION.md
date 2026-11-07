# Migração de combate NPC — Linha 1 → Linha 2

**Branch ativa:** `line2/modpack-framework`  
**Fonte antiga:** `feature/steam-modpack-local-ai-prototype-2026-11-04`, classes `MobileNPCHumanoidPerception.cs`, `MobileNPCHumanoidCombat.cs`, `MobileNPCHumanoidDisposition.cs`, `MobileNPCHumanoidRangedCombat.cs`.

## Entrega de código desta rodada

A Linha 1 possuía um `MobileNPCHumanoidCombat` de aproximadamente 18 KB e um `MobileNPCHumanoidPerception` de 22 KB, diretamente vinculados a `MobileNPCBrainDriver`, `MobileNPCInventory`, `MobileNPCHumanoidDisposition`, `MobileNPCHumanoidMotor` e à engine modificada. Copiar esses arquivos inteiros para a DLL Steam geraria referências ausentes. Em vez disso, **as rotas de ataque desarmado e as regras de percepção foram adaptadas do código original**, preservando o dano real e os bloqueios críticos em novas classes nativas:

- `Line2NPCCombatRules.cs`: prioridades de jogador hostil realmente visível em até 20 m, hostilidade entre grupos explicitamente `hostile`/`friendly`, neutralidade e modo passivo. NÃO substitui as facções persistentes da Linha 1.
- `Line2NPCCombatPerception.cs`: vê primeiro jogador hostil dentro do raio permitido, depois NPC contrário, depois zumbi. O reconhecimento de zumbis usa `ZombieManager.getZombiesInRadius` e varredura cíclica de no máximo **48** objetos existentes de `ZombieManager.AllZombies` por amostragem, para detectar zumbis visíveis no outro lado de limites de navegação. Respeita distância, direção e primeiro contato físico; não cria zumbis, não dá visão através de paredes.
- `Line2NPCMeleeExecutor.cs`: adaptado de `MobileNPCHumanoidCombat`; golpe só pode ser cometido em sessão local autorizada, com alvo vivo selecionado por sensor e visão física **reconfirmada**, até **1,75 m**, direção frontal (produto escalar ≥ **0,72**) e aproximadamente **1,4 s** entre golpes. Para jogadores usa `DamageTool.damagePlayer` com `EDeathCause.PUNCH` e **12** de dano base; para zumbis usa `Zombie.askDamage` com os **9** do perfil original de punho (15 × 0,6 Spine); para NPCs rivais usa `PrototypeNpcVitals.ApplyDamageToPart` sem descontar HP duas vezes. Um recuo urgente não é cancelado para iniciar combate; apenas a defesa corporal contra zumbi próximo pode continuar.
- `PrototypeAnimation.PlayMeleeSwing()`: varre somente clipes de ataque/punch/melee que realmente existem no `NPC_Client`; se nenhum existir não inventa animação nem concede dano pela animação. Continua independente da autoridade física.
- `SteamNpcPrototypeRuntime` adiciona os componentes ao spawn, fornece referências `PrototypeActor` e passa um destino de **alvo vivo visível** ao seguidor do A* nativo quando não há alvo jogador prioritário nem ordem defensiva. Mesmo ao parar de andar, gira na direção do alvo real para validar golpes frontais. No modo passivo não há danos nem perseguição de combate.

## Validações disponíveis e limites

`BrainTests` compila de fato `Line2NPCCombatRules` e testa (a) ausência de visão, (b) alcance além de 20 m, (c) modo passivo, (d) neutralidade, (e) oposição entre roles e (f) ausência de servidor/hit real. `check_sdk_api_surface.py` confere os símbolos do SDK `DamageTool.damagePlayer`, `Zombie.askDamage`, `ZombieManager.AllZombies`, `ZombieManager.getZombiesInRadius` e os pontos de integração C#.

**Muito importante: o workflow Windows não compila a DLL completa do Unturned Steam**; ainda precisam ser testados dentro do jogo: detecção física do prefab, ataques animados, tempo/raio das lesões, nenhum golpe através de parede, integração com as portas nativas, A* indoor e estabilidade de performance.

**Pendências que continuam sem implementação jogável:** inventário e arma equipada, munição, recarga, disparo com dispersão natural, confirmação de acerto balístico, NPCs atacarem com armas de fogo, combate tático multiagente, zumbis **atacarem NPCs** (o cérebro vanilla de zumbi ainda trata jogadores como adversário), relações e facções persistentes, rádio e reforços, decisões de sobrevivência/loot/comércio/grupos/saves/multiplayer. NÃO interpretar esta etapa como migração integral do enorme `MobileNPCHumanoidRangedCombat` ou `MobileNPCBrainDriver`.

**Próxima migração de código existente:** portar `MobileNPCInventory` junto às dependências mínimas do perfil de armas e o executor original `MobileNPCHumanoidRangedCombat`; ligar percepção e descarte de munição real, evitando spawn de balas infinitas. Manter o A* instalado no jogo Steam, sem copiar seus binários licenciados.

## Evidência

- [Ver os componentes portados](https://github.com/pplopes2003-design/Unturned-Better/tree/line2/modpack-framework/Line2-Modpack/ClientModule)
- [Matriz geral das branches](LINE1_BRANCH_AUDIT.md)
- [Migrações diretas de classes](LINE1_DIRECT_PORTS.md)

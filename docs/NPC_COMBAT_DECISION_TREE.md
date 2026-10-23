# Unturned 2 — Contrato da árvore de decisões de combate

**Status:** arquitetura obrigatória, implementação ainda parcial; testar na Unity Development Build antes de afirmar eficácia.

## Regra de integração

Antes de substituir comportamento, comparar com classes, colliders, armas, animações e física do Unturned base disponíveis neste SDK. Não pressupor A* Pathfinding Project, portas em vãos arquitetônicos ou recursos inexistentes no inventário.

## Autoridade e responsabilidades

- `MobileNPCHumanoidPerception`: percepção individual de ameaças, linha de visão e pistas sonoras.
- `MobileNPCBrainDriver`: alvo validado, intenção e orquestração.
- `MobileNPCTactics`: árbitro único de decisões defensivas e seu compromisso temporal.
- Sistemas de cobertura/flanqueamento: propõem somente destinos com chão, abrigo e caminho físicos confirmados.
- `MobileNPCHumanoidMotor`: executa uma rota por vez e confirma passagem/chegada real; não escolhe inimigos.
- `MobileNPCHumanoidAim` + `MobileNPCHumanoidRangedCombat`: executam tiros somente com alvo atual, visão, segurança, munição e modo nativo validados.
- Cura: só consumir medicamentos do inventário real, usando `MobileNPCHumanoidVitals.Heal` no servidor. A existência de `Heal` não implica que a escolha de item esteja implementada.

## Árvore de combate (prioridade descendente)

```text
Vivo e servidor autoriza ações?
├─ NÃO → interromper decisões/ataques.
└─ SIM
   ├─ Perigo físico crítico / HP crítico?
   │  └─ RETIRADA para ponto seguro fisicamente acessível.
   ├─ Retirada/cobertura já comprometida, válida e não expirada?
   │  └─ MANTER plano mesmo após silêncio ou alvo reaparecer.
   ├─ Recebeu fogo / dano recente, posição exposta?
   │  ├─ Cobertura validada e acessível → CORRER PARA COBERTURA.
   │  └─ Sem cobertura → RETIRADA segura ou fogo defensivo, conforme risco.
   ├─ Ferido + item médico real + posição segura?
   │  └─ CURAR (dano novo pode interromper).
   ├─ Inimigo pessoalmente identificado e visível?
   │  ├─ Nossa linha de tiro é válida?
   │  │  ├─ Sim e posição favorável → DISPARAR usando arma real.
   │  │  └─ Não → REPOSICIONAR sem disparar na parede.
   │  ├─ Atacante está em cobertura e NPC em campo aberto?
   │  │  ├─ Flanco ou abrigo validado → REPOSICIONAR.
   │  │  └─ Sem rota segura → não avançar cegamente.
   │  └─ Oportunidade de aproximação segura → AVANÇAR com A*.
   ├─ Inimigo antes identificado, agora oculto?
   │  └─ INVESTIGAR última posição observada sem informação onisciente.
   └─ Sem combate → exploração, loot, grupo e sobrevivência.
```

## Regras de preempção e integridade

1. Morte e risco fatal prevalecem sobre qualquer comportamento.
2. Retirada e cobertura com destino validado não são canceladas por novos tiros, silêncio nem reacquisição visual; expiram, são concluídas ou falham explicitamente.
3. Flanco exige rota física e posição de disparo verificada; não trocar de lado a cada frame.
4. Alvo atrás de cobertura não autoriza corrida suicida em terreno aberto.
5. Percepção de ruído não comprova agressor nem autoriza tiro ou agachamento.
6. Nenhum waypoint atravessa parede, mobília, vão estreito, pavimento desconectado ou rampa com inclinação acima do limite real do CharacterController.
7. Rampa interna caminhável é superfície contínua: validar subida gradual ao longo de toda a aresta, não comparar exclusivamente a altura entre extremidades com `stepOffset`.
8. Fogo automático só com arma que realmente ofereça `isAuto`, munição e linha de tiro válidas.
9. Habilidade máxima não significa mira perfeita nem imunidade a ferimentos.

## Cenários obrigatórios antes de aprovar a build

| Teste | Resultado esperado |
| --- | --- |
| NPC nasce sobre cama | Posição rejeitada; procurar piso arquitetônico real |
| NPC sob fogo; atacante para de atirar | Prossegue até cobertura ou falha de rota verificável |
| Atacante reaparece durante fuga | Não cancela automaticamente o compromisso defensivo |
| Jogador protegido, NPC exposto | NPC busca cobertura ou flanco; não corre diretamente contra tiros |
| Ferido com remédio e em segurança | Usa o item real e recupera HP; sem medicamento não se cura |
| Jogador entra em construção | Busca acesso físico e não atira através da parede |
| Rampa interna | Sobe/desce se piso contínuo, declividade/cápsula válidas |
| Vários NPCs em combate | Orçamentos de A* e sondas preservados sem erros recorrentes |

**Pendências reais:** ainda é preciso integrar completamente a seleção de cobertura ofensiva e de cura; o contrato não comprova esses comportamentos.

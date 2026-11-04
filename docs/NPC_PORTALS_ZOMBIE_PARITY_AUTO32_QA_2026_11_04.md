# RC QA — Portais, zumbis e Bluntforce Auto-32 (2026-11-04)

Branch: `feature/release-candidate-cycle-2026-11-04`. `main` é o jogo-base, não foi alterada.

## 1. Portas e janelas (PRIORIDADE MÁXIMA — PENDENTE DE VALIDAÇÃO FÍSICA)

Os NPCs móveis usam `CharacterController` e rota baseada em geometria física; portas/janelas de muitas construções de Washington não são GameObjects independentes: são vazios/aberturas nas malhas/colisores de objetos do cenário. O detector `TryPlanObservedBuildingThresholdServer` já procura um corredor com **piso contínuo, cápsula livre e abordagem externa** — sem criar falso portal ou desligar colisões.

Esta rodada adicionou log de diagnóstico **no colisor real atingido**, inclusive nome, objeto-raiz, `gameObject.layer`, `obstacleMask`, `bounds`, normal de contato e coordenada. Procure no Player.log Development Build:
- `[MobileNPCGeometryThreshold] facade`: colisor realmente atingido, layer, máscara e bounds.
- `[MobileNPCGeometryThreshold] No traversable opening`: número de pares com chão e número de pares realmente livres para cápsula.
- `[MobileNPCGeometryThreshold] Opening found but near-side route rejected`: passagem encontrada, mas acesso físico ao lado externo rejeitado.

**Interpretação do resultado**:
- `groundedPairs=0`: o teste de piso/altura/ocupação falhou antes do teste de travessia — verificar geometria vertical, máscara de solo, colisor de mobília e clip volumes.
- `groundedPairs>0`, `capsuleClearPairs=0`: piso encontrado, mas colisor obstrui cápsula ou travessia não tem continuidade de chão — inspecionar `Collider.bounds`, `MeshCollider` e largura/altura reais.
- `capsuleClearPairs>0` com rota rejeitada: erro na abordagem/planejamento antes da passagem, não na detecção do vão.
- Não desabilitar colisores de paredes para forçar a passagem: isso cria passagem de tiro e de NPC através de paredes sólidas.

**Atenção**: a geometria não foi inspecionada dentro do Unity nesta rodada. A telemetria não é uma correção garantida de atravessamento. Continuar o trabalho de navegação depois de um log de NPC junto a fachada específica.

## 2. Zumbis — prioridade de jogadores e NPCs

A aquisição de humanoides próximos agora escolhe o mais próximo entre **jogador e NPC visíveis** dentro dos 20 m existentes. Removida precedência absoluta do jogador na aquisição, nos alertas por som e no tick de deslocamento. A aquisição compartilhada periódica do ZombieManager evita varrer todos os NPCs em cada frame de cada zumbi. Ainda dependemos dos alertas nativos quando um player entra no alcance; validar trocas por distância, LOS, morte e desconexão.

Casos para testar: jogador a 12 m e NPC a 5 m; inverso; ambos no mesmo lado de parede; alvo morre ou some; alvo já ativo mais próximo não deve ser trocado por outro distante.

## 3. NPCs — alvo de um zumbi

`Zombie.IsHuntingMobileNPC(vitals)` fornece evidência autoritativa de perseguição direta. O avaliador pessoal de risco registra isso apenas para um zumbi realmente visível e considera retirada imediata se o perseguidor está a até 8 m e o NPC não tem arma eficaz, está ferido ou o zumbi entrou a até 2,8 m. NPC armado/saudável pode manter ataque por sua lógica atual. Retirada ainda exige ponto realmente alcançável; caso contrário, combate existente permanece.

Casos: NPC armado e saudável contra um zumbi; NPC desarmado perseguido; NPC ferido; perseguidor oculto atrás de parede; zumbi desistiu; perseguidor está em grupo. Não marcar concluído sem Player.log.

## 4. Bluntforce Auto-32 — ids isolados

IDs anteriores `59010` (arma) / `59011` (tambor) foram migrados para:
- **65042** — Bluntforce Auto-32
- **65043** — tambor 32 cartuchos
- **113** — pacote padrão de munição usado pelo blueprint, 4 unidades por tambor.

Referências atualizadas na arma (`Magazine 65043`) e no carregador (`Blueprint_0_Product 65043`). GUIDs originais **não** foram alterados para preservar a identidade GUID dos assets.

Comandos de validação em servidor/jogo com cheats:
```
/give 65042
/give 65043
/give 113/4
```

**IDs não são globalmente reserváveis**: pode haver outro mod da Workshop com o mesmo ID, mesmo numa faixa alta. Caso ocorra, coletar o erro de duplicate legacy ID do Player.log, identificar qual mod está colidindo e então trocar os IDs com nova verificação. Itens salvos que dependem do antigo ID podem precisar ser gerados novamente. Confirmar que `Mods/UnturnedBetter` está instalado/carregado pela build.

## Critério de release

- [ ] Build Windows64 recompila
- [ ] Portal realmente identificável e atravessável em Washington (porta aberta e janela geometricamente transitável)
- [ ] Jogador/NPC recebem mesma regra de prioridade visual do zumbi, sem atropelar LOS
- [ ] NPC persegue/mata ou retira-se do zumbi de forma funcional
- [ ] Auto-32 aparece, usa tambor, recarrega e não conflita com mods Workshop ativos

*Revisão e alterações no fonte; nenhum teste Unity/Player foi executado nesta rodada.*

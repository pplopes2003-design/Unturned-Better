# Ciclo de navegação global e rádio — 2026-11-04

Branch: `feature/release-candidate-cycle-2026-11-04`. Preservar `main` como jogo-base.

## Alterações efetivas

1. `HybridNPCObservedPortalRegistry.Remember`: quando o cache de 64 entradas está cheio, uma passagem apenas observada só pode substituir outra **não confirmada**. Antes, eliminava indiscriminadamente a primeira entrada e podia apagar um portal já atravessado.
2. `HybridNPCObservedPortalRegistry.ConfirmCrossing`: quando entra uma nova passagem confirmada e o cache está cheio, tenta remover uma observação não confirmada; se não existe, remove a entrada confirmada mais antiga. Não amplia o limite de 64.
3. `MobileNPCRadioOutbox.Update`: componentes ativos de rádio reingressam no registro estático de receptores/transmissores após transição de mapa sem esperar `OnEnable`; não são produzidos novos relatórios, itens, conhecimento ou NPCs. Só reingressa componente pertencente ao servidor, em mapa carregado, com NPC vivo.
4. A segurança do canal foi preservada: transmissão exige walkie-talkie real, receptor exige walkie-talkie ou earpiece realmente equipado, afinidade por canal e facção, mensagens com TTL e checagem de grupo para não afiliados. Contato recebido continua sendo rumor, não alvo confirmado.

## Arquitetura auditada, sem reescrita neste ciclo

- `MobileNPCGlobalRoutePlanner`: fallback A* incremental de horizonte móvel 61 m, grid de 3 m, orçamento compartilhado de seis expansões/frame e validação de chão/arestas. **Não é** navmesh global de todo Washington.
- `MobileNPCNativeAStarRouteProvider`: tenta usar um backend nativo opcional quando presente. Não afirmar que a versão SDK inclui o backend completo.
- `HybridNPCObservedPortalRegistry`: passagens confirmadas são topologia consultiva. `HybridNPCGridPlanner` valida fisicamente cada etapa; jamais teletransportar através de porta imaginária.
- `MobileNPCRadioMessage`: tipos EnemyContact, NeedReinforcements, UsefulLoot, Airdrop, UnknownBase. Relatórios recebidos por outros grupos compatíveis não substituem a observação direta nem revelam a identidade atual do jogador.

## Testes obrigatórios — ainda não executados

- [ ] Povoar 64 passagens, confirmar parte delas, adicionar centenas de observações falsas; garantir que as confirmadas não sejam expulsas por entradas não confirmadas.
- [ ] Confirmar novo portal quando as 64 entradas já forem confirmadas e verificar descarte da mais antiga, sem ultrapassar 64.
- [ ] Caminhar com NPCs nos dois sentidos por portal estreito, rampa e múltiplos interiores Washington; validar colisão física, andar correto, ausência de teleporte e recuperação de bloqueios.
- [ ] Reiniciar mapa sem recriar o componente de rádio; comprovar registro recuperado e comunicação apenas quando o NPC possui os itens adequados.
- [ ] Mudar facção/grupo entre enfileirar e transmitir; mensagem antiga deve ser descartada; não vazar um relato para grupo incompatível.
- [ ] Separar NPCs em regiões distantes; verificar informações por rádio como rumor sujeito a TTL e redução de confiança, não aquisição de alvo mágico.
- [ ] Testar encerramento do servidor, recarga do mapa e dois clientes remotos; não atribuir execução autoritativa ao cliente.
- [ ] Medir custo de A* (expansões por frame, tempo por NPC, contenção da busca) e de rádio (transmissões por segundo, membros ativos).

## Pendências de qualidade

- Testar e ajustar o comportamento do seguidor de caminhos em portais e escadas, especialmente colisões com bordas de portas/camas.
- Reduzir buscas A* repetidas quando o destino oscila e prover telemetria útil para casos de stuck.
- Medir e otimizar transmissão de RPCs de NPCs ao longo do tempo; a comunicação por rádio aqui é servidor-local e não deve gerar RPC de rede para cada rumor.
- Compilar cliente/dedicado no Unity e executar cenários de teste antes de declarar RC.

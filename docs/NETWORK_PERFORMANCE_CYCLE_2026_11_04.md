# Multiplayer: orçamento de snapshots de NPC — 2026-11-04

Branch: `feature/release-candidate-cycle-2026-11-04`. Código em desenvolvimento, ainda sem compilação ou medição real.

## Alteração implementada

`MobileNPCNetworkReplica` mantém a frequência de amostragem do servidor de 0,35 s, mas não emite um RPC de posição para um NPC **parado e praticamente inalterado** em todas as rodadas. Um heartbeat de 1,4 s mantém presença para entrada tardia e a validade de proxies remotos (expiram após 6 s sem atualização).

- Primeiro snapshot sempre transmitido.
- Movimento/flags (movendo, sprint e recarga) sempre são transmitidos no ciclo normal.
- Mudança de arma ou pitch de cabeça transmitida no ciclo normal.
- Alteração de posição >= 0,2 m ou yaw quantizado >= 2 unidades envia atualização no ciclo normal.
- NPC parado, sem mudança relevante, não é transmitido antes do heartbeat.
- O histórico de snapshots é removido no reset de mapa e seus IDs que deixaram de existir são limpos a cada varredura.
- Eventos confiáveis de disparo, morte, recarga e granada NÃO foram alterados.
- A replicação continua limitada aos clientes em até 165 m, usando a API nativa já implementada.
- Em DEVELOPMENT_BUILD, log de auditoria registra amostrados, emitidos e suprimidos por estado parado. Esses valores contam chamadas de RPC/ NPC, não bytes reais e não multiplicam pelo número de clientes.

## Precauções

O cache é **por NPC no servidor**, não por cliente. Um novo cliente obtém o primeiro snapshot no próximo heartbeat (até 1,4 s, caso parado), mas metadados confiáveis continuam atualizados no intervalo independente de 4 s. Não afirmar melhora quantitativa sem profiler / logs.
O código tem a mesma frequência de leitura das fontes NPC; reduz emissões mas não elimina leitura de componentes Unity ou coleta de players na entrada do RPC enviado.
NPCs em movimento permanecem sem compressão de posição. A implementação de comparação por posição não altera colisão, pathfinding, IA ou autoridade de dano.

## Matriz de validação

- [ ] Executar testes EditMode `MobileNPCReplicationBudgetTests`; compilar cliente e dedicado na mesma revisão.
- [ ] NPC imóvel por 2 minutos, jogador conectado: comparar emitted vs idleSkipped e medição real do transporte (bytes/s).
- [ ] Jogador entra tardiamente perto de NPC imóvel: modelo deve aparecer rapidamente, sem fantasma nem perda de nome/roupas.
- [ ] NPC começa a correr/atirar/recarregar: animações e tiro devem atualizar sem regressão.
- [ ] NPC para de correr: flags devem zerar no primeiro ciclo seguinte.
- [ ] Testar limite de 165 m e reentrada na região de relevância.
- [ ] Cliente desconecta/reconecta, mapa troca, servidor reinicia e NPC morre: não permitir ressurreição por pacote atrasado.
- [ ] Testar 1/2/4 clientes com 10/20/40 NPCs, registrar FPS, tempo do servidor, RPC/s, bytes/s e custo de GC ao longo de pelo menos 10 minutos.
- [ ] Verificar metadados confiáveis de roupa/nome (hoje periódicos a cada 4 s); otimização per-client exige rastreamento de estado por conexão ou mecanismo de delta com ACK.

## Critérios de promoção

Nenhum ganho de largura de banda foi medido nesta sessão. Sem Unity build e regressão multiplayer, manter esta branch como candidata em desenvolvimento, não como release aprovado.

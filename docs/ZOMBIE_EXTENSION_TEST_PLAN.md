# Extensão de zumbis — contrato de implementação e validação

## Preservação do jogo base
**Ainda não integrado ao Zombie/ ZombieManager nativo.** Localizar primeiro o controlador de perseguição, as regras de região, spawn, velocidade, animation state e alertas do Unturned original. Evitar mudar a enumeração nativa de tipos de zumbi ou deslocar inimigos entre ZombieRegions sem conferir as rotinas de registro, replicação, save e despawn.

## Estados da investigação opcional
Ruído válido (sem inimigo nativo ativo) → `Search` durante 30–45 s → `Alert` por 30 s → `ReturnHome` até alcançar o spawn → `Inactive`.

- Durante `Search`, sortear pontos dentro de raio horizontal de 15 m ao redor do som.
- **Antes de executar** cada ponto, o integrador precisa projetá-lo no terreno navegável real e comprovar rota física. Amostra não é permissão para atravessar parede ou escapar da região.
- Enquanto `Alert`, permanece atento sem inventar alvo.
- Quando visão, ataque, perseguição ou alerta nativo assumir o controle, cancelar imediatamente esta extensão; a lógica nativa permanece autoridade.
- Barulhos novos podem reiniciar o ciclo apenas se o controlador nativo autorizar.
- Ao voltar, usar o caminho nativo se disponível; não teletransportar.

## Zumbi corredor
**Não integrado.** Após localizar a implementação nativa: definir variante opt-in de perfil de velocidade comparável à corrida do jogador, mantendo restrições de aceleração, animação, física, custos de rede e agressão. Não alterar os tipos nativos existentes nem aumentar globalmente a velocidade de todos os zumbis.

## Perseguição além de regiões
**Não integrada.** Exige investigar como as `ZombieRegion` gerenciam atualização, spawn, despawn, replicação, pathfinding e ownership. Não liberar limites artificialmente sem confirmar que o zumbi continua registrado/atualizado corretamente e sem custos excessivos.

## Cenários para testes automatizados e runtime
1. `HearSound(t=0)`: estado Search, duração 30–45 s.
2. Amostras de busca sempre dentro de 15 m em XZ; múltiplos pontos distintos.
3. Em `PhaseEndsAt`: transição Search → Alert; 30 s após, Alert → ReturnHome.
4. Inimigo identificado pelo sistema nativo: cancelar complemento, sem interferir no ataque.
5. Retorno concluído: Inactive.
6. Som com posições NaN/Infinity: ignorar.
7. Janela, parede, escada, rampa e regiões adjacentes: adaptar somente quando houver navegação nativa validada.
8. Com muitos zumbis: controlar frequência de procura e custos por frame no servidor.

## Diagnóstico dos NPCs
`MobileNPCDecisionDiagnostics.Record` é apenas utilitário de rastreamento, desativado por padrão, compilado para emitir log somente em Editor/Development Build. Ainda não foi conectado aos eventos de IA, para não modificar os testes pendentes.

## Estado de qualidade
Código novo enviado ao GitHub, sem compilação Unity ou testes NUnit nesta sessão. O plano não substitui teste na dev build.

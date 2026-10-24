## Auditoria posterior do controlador nativo (2026-10-24)

Localizado em `Assets/Runtime/Assembly-CSharp/Unturned/Zombies/Zombie.cs` (não no diretório raiz `Unturned/`). Já existiam no próprio `Zombie.cs` um ciclo de investigação de ruídos, um tratamento de perseguição além do leash para jogadores e a especialidade nativa `EZombieSpeciality.SPRINTER`. Portanto, **não devemos instalar a política isolada como segunda IA concorrente**.

Mudanças diretamente no sistema já existente, adicionadas em dois commits nesta data:
- Duração da investigação de som aceita: 30–45 s (antes 18 s).
- Busca limitada a 15 m do centro sonoro e até 12 destinos potenciais (antes raio máximo de 18 m e 3 destinos, com amostras de 2,5–5 m).
- Após esgotar investigação, 30 s de alerta com `setTicking(true)`; depois tenta voltar à posição memorizada em `tellAlive` usando o `seeker` e `alert(position,false)`. Se o ponto de origem não pertencer à navegação, encerra sem teleporte.
- A visão de um jogador continua a preceder o ciclo de busca na atualização nativa.
- O código de `ZombieNoiseSearchPolicy.cs` é uma política experimental independente, **não ligada** ao comportamento em produção e não deve ser ligada em paralelo com `Zombie.cs`.

**Atenção:** `Zombie.cs` já ignora o corte por distância de 64 m para alvos do tipo jogador/NPC vivo em determinada condição; isso não comprova que `ZombieManager` continue atualizando e replicando o zumbi em todas as regiões. Testes de perseguição inter-regional ainda pendentes.

**Zumbi corredor:** `EZombieSpeciality.SPRINTER` já existe. O trecho de deslocamento fora da malha em `Zombie.cs` utiliza velocidade 5,2 para SPRINTER. Antes de acrescentar outro tipo, investigar como a velocidade é calculada na navegação principal e a velocidade efetiva do PlayerMovement; não duplicar especialidades e não prometer velocidade idêntica ao jogador sem teste.

**Validação pendente:** compilação, busca de 30–45 s, alerta de 30 s, retorno sem teleporte, interrupção por visão, colisões, mudanças de região, respawn, desempenho e compatibilidade com o seletor de especialidades.

---

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

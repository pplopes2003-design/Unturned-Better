# Multiplayer integrado — plano de implementação segura

## Objetivo
Uma opção **Hospedar partida** no menu do Unturned 2 que configure mapa, nome, senha opcional, limite de jogadores, privacidade e porta sem editar arquivos à mão. **A opção não está funcional ainda**: inicialmente há um modelo de configuração validada, sem iniciar processos ou alterar rede.

## Observações de compatibilidade
- A base original contém interfaces nativas de `Provider`, servidor e Steam; os caminhos e APIs dessa camada ainda precisam ser identificados nesta branch antes de integrar.
- Não iniciar um segundo servidor dentro do mesmo processo Unity presumindo que `Provider` suporte player+servidor ao mesmo tempo.
- Estratégia inicial mais segura a avaliar: UI dentro do cliente que inicia e gerencia uma instância local do servidor nativo, separada do processo do jogo; o cliente então ingressa via loopback. Confirmar existência, distribuição, licenciamento e argumentos corretos do executável nativo antes de escrever o launcher.
- Não exigir aluguel de host remoto. Para amigos fora da rede local, escolher solução já compatível com o Unturned (Steam networking, convite, ou redirecionamento de portas conforme o servidor suportar). Não alegar NAT traversal automático antes de integrar/testar.
- Não armazenar ou emitir senha em Player.log, parâmetros de processo visíveis ou mensagens de UI; avaliar canal seguro de provisionamento nativo.
- A ação de parar servidor deve desligá-lo graciosamente e liberar recursos; tratar falhas/portas ocupadas e impedir duas instâncias simultâneas.

## Etapas
1. **Feito:** modelo `MobileNPCIntegratedHostConfig` isolado, com validação de mapa, nome, senha, jogadores e porta; não há efeitos no runtime.
2. Auditar `Provider`, servidor nativo, opções da linha de comando, configurações e fluxo de mapas dentro deste SDK.
3. Criar adaptador de ciclo de vida (iniciar, ler estado, parar) separando processo de UI; inativo até auditoria e compilação.
4. Adicionar botão `Hospedar partida` ao menu principal com mapa e opções essenciais, sem interromper Singleplayer nem Connect existentes.
5. Iniciar servidor nativo, aguardar confirmação de disponibilidade, conectar cliente por caminho válido.
6. Fazer testes em mesma máquina, LAN, outra rede, queda de conexão, firewall e desligamento com salvamento.

## Critérios de aceitação
- Funciona sem gerenciador externo manual e sem servidor alugado.
- Configuração simples e recuperável entre sessões.
- Conexão e autorização pelo stack nativo; nenhuma rede paralela construída.
- Mundo, NPCs, zumbis, hordas, inventário e combate com autoridade do servidor.
- Erros visíveis ao usuário, sem travar menu/cliente.
- Não mudar navegação ou IA enquanto testes da baseline não forem concluídos.

## Hordas: auditoria de 2026-10-24
O controlador `ZombieManager.UpdateHordeRespawnEventServer()` já existe e é invocado na atualização de servidor. Reutiliza slots mortos, não altera IDs e limita a 2 revives por tick. Introduzida pré-validação de pontos de spawn numa zona de 40 m antes de iniciar evento com mínimo 10 (normal) ou 20 (Blood Moon). Se zumbis reaparecerem, jogadores se moverem ou o mundo mudar durante o evento, a pré-verificação não garante que todos possam nascer. Ainda faltam compilação, teste de desempenho e revisão de sincronização.

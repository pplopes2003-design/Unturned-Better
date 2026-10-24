# Plano de desenvolvimento sem alterar testes pendentes — Unturned 2

## Congelamento de baseline
Até a próxima dev build testada, evitar modificar em produção `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, `MobileNPCBrainDriver`, `MobileNPCTactics`, `MobileNPCHumanoidRangedCombat`, `MobileNPCVisualAnimation` e a configuração de spawn. Não associar novas decisões a `Update`/`TickServer` durante este período. Preservar histórico de commits e branch de testes.

## Trabalho seguro e independente
1. Diagnósticos **opt-in**: evento estruturado por NPC contendo hora, ID, estado do cérebro, alvo observado/lembrado, plano tático, destino, estado de caminho, motivo de falha e métricas. Limitar frequência, anonimizar identificação de jogadores, evitar alocação por frame, manter desativado por padrão.
2. Testes puros: criar testes de unidades para classificação de prioridades, políticas de expiração, seleção de itens médicos e validação de serialização. Não exigir instanciar cena ou NPC e não alterar regras existentes para satisfazer testes.
3. Persistência versionada: schema/snapshot de estados serializáveis (NPC, grupo, base) e validação de leitura com dados fictícios. **Não ligar** carregamento/gravação automática enquanto spawn e ciclos de vida estiverem pendentes.
4. Inventário médico: modelar elegibilidade, quantidades e consumo previsto usando apenas tipos reais do SDK; não executar `Heal` ou consumir itens até validação no jogo.
5. Comunicação por rádio: modelar mensagens, prazo de validade e destinatários como dados; sem publicar sons, posições de inimigos ou ordens reais até auditoria.
6. Verificador estático/CI: checar alterações perigosas, referências ausentes e compilar quando ambiente compatível disponível. Relatórios devem distinguir checagem textual de compilação real.

## Critérios de aceitação
- Ausência de novos componentes ativados automaticamente em runtime.
- Nenhuma mudança nos fluxos de combate, navegação, spawn, cura ou visibilidade antes da build-base.
- Código novo atrás de configurações internas desligadas, ou componentes puros sem `MonoBehaviour`, quando possível.
- Cada novo módulo precisa de testes que usem entradas inventadas e saídas verificáveis.
- Documentar dependências e equivalentes do jogo base antes de integrar.

## Testes que permanecem pendentes
Travessia de entradas, rampas, percepção por vãos, recuperação de spawn sobre móveis, cobertura, flanqueamento, retirada sustentada, cura, modo automático, animação de armas e exceções de menu. Manter o roteiro de testes e logs dessas condições como baseline.

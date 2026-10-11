# NPC AI — acompanhamento do desenvolvimento

## Estado atual

**Fase:** investigação técnica e desenho da implementação  
**Branch:** `feature/npc-ai-foundation`  
**Base:** `perfomance-baseline`  
**Implementação de IA de NPC:** ainda não concluída  
**Build/teste no Unity:** pendente — este ambiente não executa o Editor nem compila o projeto Unity.

## Descobertas confirmadas

- `InteractableObjectNPC` representa NPCs colocados como objetos estáticos do mapa.
- O componente atual já trata diálogo, roupas, armas visuais, animações de espera e acompanhamento da cabeça do jogador.
- A identidade do alvo de diálogo usa `NetIdRegistry` e a posição do objeto do mapa.
- Mover diretamente esses objetos pode afetar a validação de distância do diálogo e a sincronização entre servidor e clientes.
- A implementação deve manter os NPCs de mapa existentes funcionando sem alterações inesperadas.

## Próximas etapas

- [ ] Mapear o ciclo de vida e a replicação de entidades móveis existentes.
- [ ] Definir uma base de NPC móvel que use movimento validado e autoridade do servidor.
- [ ] Implementar uma primeira capacidade pequena e isolada (patrulha/percepção), sem mudar os NPCs estáticos por padrão.
- [ ] Revisar impacto no diálogo, salvamento, regiões e multiplayer.
- [ ] Solicitar teste no jogo quando houver alteração concreta.
- [ ] Registrar feedback, bugs encontrados e correções.

## Regra de validação

Nenhuma mudança será marcada como testada no jogo até haver teste real no Unity/Unturned. Commits nesta branch são experimentais até a validação.

## Registro de progresso

### 2026-10-11
- Criada branch de trabalho `feature/npc-ai-foundation`, separada da branch de referência.
- Confirmada a arquitetura atual de NPCs estáticos e os pontos de integração com diálogo.
- Nenhuma IA móvel foi implementada ainda.

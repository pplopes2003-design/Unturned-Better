# Replicação de identidade e aparência — auditoria 2026-11-04

Branch: `feature/release-candidate-cycle-2026-11-04`. Ainda requer compilação Unity e testes com clientes reais.

## Problemas detectados

1. O servidor enviava `ReceiveNpcOutfit` e `ReceiveNpcIdentity` confiáveis para TODOS os clientes próximos a cada 4 s, mesmo sem alteração.
2. Como o snapshot de posição é não confiável, mensagens de identidade e vestimenta podem chegar primeiro; os receptores descartavam ambas porque o proxy visual ainda não existia.
3. A identidade precisa voltar a ser enviada quando um cliente entra pela primeira vez, troca de área ou reconecta; apenas um cache por NPC, comum a todos os clientes, seria incorreto.

## Correções feitas

- `MobileNPCNetworkReplica` agora mantém cache autoritativo **por ITransportConnection e ID de NPC**.
- A cada intervalo de 4 s, compara nome, semente de aparência e os cinco IDs de vestimenta; envia os dois RPCs confiáveis somente a receptores novos, alterados ou cujo refresh de segurança venceu (12 s).
- Quando um jogador deixa o raio de 165 m, sua entrada daquele NPC é descartada para que nova entrada no alcance provoque reenviar os metadados.
- Conexões desconectadas e IDs de NPC que já não existem no registro ativo são removidos do cache. Trocar de mapa ou desabilitar o componente limpa o histórico.
- No cliente, identidade e roupa recebidas antes do primeiro snapshot são guardadas em buffers temporários limitados a 128 IDs por tipo, válidos por 12 s. São aplicados após o proxy visual ser criado. O buffer é apagado em troca de mapa.
- A semântica dos RPCs e os arquivos NetGen não foram alterados; somente a seleção de destinatários e o tratamento de ordem de entrega.
- Nenhum dado de memória individual, alvo, invenção de loot ou lógica autoritativa é transmitido.

## Observações de confiabilidade

O cache lembra a **tentativa de envio** e não um ACK de aplicação do metadado no cliente. O refresh de segurança a cada 12 s permite recuperação, mas não substitui teste de perda de pacotes. O par de mensagens confiáveis é emitido junto; a ordem relativa frente aos snapshots não é garantida, por isso o buffer no cliente.

## Testes obrigatórios antes de release

- [ ] Compilar Assembly-CSharp cliente e dedicado da mesma revisão, incluindo `SDG.NetTransport.ITransportConnection` e `Provider.GatherRemoteClientConnectionsMatchingPredicate`.
- [ ] Dois jogadores entram em momentos diferentes no raio do mesmo NPC: ambos devem receber nome, skin e equipamento correto.
- [ ] Cliente sai do raio >165 m por pelo menos 4 s e retorna: garantir novo envio e reaparição sem identidade vazia.
- [ ] Cliente aproxima do limite, passa brevemente fora, retorna: validar recuperação mesmo com proxy visual recriado.
- [ ] Simular perda/atraso de snapshots para fazer identidade/roupa chegarem antes do modelo; devem ser aplicadas quando o snapshot cria o proxy.
- [ ] Alterar uma peça de roupa no inventário autoritativo: servidor deve enviar mudança sem esperar o refresh de 12 s.
- [ ] NPC morre, outro surge, servidor reinicia: cache antigo não pode dar nome/roupa à entidade errada.
- [ ] Com 1, 2 e 4 clientes e 10, 20 e 40 NPCs, registrar RPC confiáveis por segundo, bytes/s, GC, FPS e custo de CPU.
- [ ] Confirmar que jogos singleplayer e cliente host local não transmitem metadados de NPC para o host local erroneamente.

## Critério

A redução deve ser medida em rede real. Código e checklist foram gravados no GitHub, **mas nenhum build Unity, runtime Steam ou teste NUnit foi executado**.

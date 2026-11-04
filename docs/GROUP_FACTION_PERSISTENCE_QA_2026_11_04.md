# Persistência de grupos e facções — revisão 2026-11-04

Branch: `feature/release-candidate-cycle-2026-11-04`. Não mesclar na `main` antes da compilação e dos testes.

## Constatado no código existente

- `MobileNPCPersistence` salva os sobreviventes individualmente com `persistentId`, `displayName`, `role`, `faction`, `occupation`, `groupId`, a âncora da base e seu progresso, reclamação de depósito nativo, inventário, necessidades, ferimentos e experiência.
- A identidade do grupo permanece **enquanto algum integrante estiver vivo**. A morte do último membro dissolve o registro; o substituto recebe nova identidade individual, não herda memórias nem reanima o grupo extinto.
- O inventário de cadáver não integra o snapshot dos sobreviventes; o jornal de loot é separado.
- A existência física de estruturas deve ser validada pela persistência nativa do mundo, não recriada com base em contadores do snapshot.

## Alterações deste ciclo

1. O mapa de IDs salvos para grupos runtime é agora composto por **ID original + papel + facção**. IDs antigos coincidentes com facções diferentes não são reunidos inadvertidamente.
2. Durante o restore, a âncora da base é herdada do primeiro registro consistente. Um membro restaurado em seguida com âncora antiga não muda novamente a localização de todo o grupo. Se o primeiro registro não tiver assentamento e o próximo tiver, esse próximo fornece a primeira âncora de assentamento.
3. Falha de `SetGroupAnchorServer(savedGroupId, ...)` agora aborta o restore daquele NPC para que o snapshot continue na fila de tentativas; não é mais considerado restauração bem-sucedida.
4. A transição de mapa realiza o flush do **modo anterior** antes de aplicar a configuração autoritativa dedicada do próximo mapa.
5. Testes EditMode adicionados para chaves de grupo estáveis, IDs diferentes, facções e papéis incompatíveis. **Não foram executados no Unity.**

## Testes de aceitação obrigatórios

- [ ] Crie dois grupos hostis e um de comerciantes, feche o servidor graciosamente e confirme os mesmos nomes, GUIDs individuais, facções, âncoras e progresso após reiniciar.
- [ ] Elimine o líder mantendo ao menos um integrante vivo, reinicie: o grupo continua com liderança eleita; o morto não retorna e nenhum item/experiência pessoal é transmitido ao substituto.
- [ ] Elimine o último integrante: o grupo desaparece. A reposição futura cria grupo novo, sem recuperar um posto/base como pertencente à facção anterior.
- [ ] Troque um NPC de facção por oferta de aliança e reinicie: o NPC deve continuar na facção correta sem reassociar a raiders apenas por ID antigo.
- [ ] Monte um save legado com dois membros com mesmo `groupId` e papéis/facções diferentes; validar grupos separados.
- [ ] Monte dois membros do mesmo grupo com âncoras divergentes e verifique que o segundo não move a base do primeiro.
- [ ] Com capacidade máxima de quatro membros, teste restauração gradual: nenhum membro pode ser descartado silenciosamente por grupo cheio.
- [ ] Teste mudança entre Normal e Combate Total, mudança de mapa e retorno, conferindo arquivos independentes e sem modificação dos registros anteriores.
- [ ] Valide estruturas físicas nativas e armazenamento capturado, inclusive estruturas destruídas durante o servidor desligado.
- [ ] Multijogador: hospede com dois clientes, force desconexão/reconexão e verifique que somente o dedicado mantém identidade, facção e registros, sem dois líderes divergentes.

## Limitações / próximos ciclos

- A restauração de facção/grupo é baseada nos NPCs vivos, **não há um registro persistente independente de grupos extintos** (por projeto).
- Não se persistem ordens táticas, alvos, contratos de trabalho temporários ou memórias de rádio como se fossem conhecimento global.
- Relações `MobileNPCFaction.GetRelationship` são regras estáticas de diplomacia, não uma tabela de alianças dinâmicas persistida separadamente.
- Não houve compilação de cliente/dedicado, teste com Steam nem auditoria física completa de bases nesta rodada. O marco para release permanece condicionado a esses testes.

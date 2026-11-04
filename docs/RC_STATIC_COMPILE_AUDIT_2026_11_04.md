# RC — Auditoria estática de compilação (2026-11-04)

Branch: `feature/release-candidate-cycle-2026-11-04`. A `main` é o jogo-base e não deve ser modificada.

## Escopo efetivamente inspecionado

- `MobileNPCIntegratedHostLauncher.cs` e `WorkshopDownloadConfig.cs`: confirmação de que a classe nativa de configuração não tem `[Serializable]`; o launcher passou a produzir JSON compatível com `File_IDs` numéricos e atualização com backup. **Não foi executado o parser nativo**.
- `MobileNPCNetworkReplica.cs`, `Provider.cs`: conferidos os símbolos `SDG.NetTransport.ITransportConnection`, `SteamPlayer.transportConnection`, `Provider.clients` e `Provider.GatherRemoteClientConnectionsMatchingPredicate` existentes no código-fonte. O sistema usa esses símbolos no cache por cliente. **Nenhum byte de rede foi medido**.
- `MobileNPCCorpseLootJournal.cs`, `MobileNPCDeathLoot.cs` e `MobileNPCPersistence.cs`: ligações entre retentativa, registro persistente e o loop de salvamento; sem teste de commit atômico com mundo nativo.
- Testes NUnit para parsing de Workshop, chaves de grupos e controle de snapshots foram inspecionados. Sem execução no Test Runner do Unity.
- Consulta integral à árvore da branch apontou quatro novos scripts sem `.meta` que **foram criados nesta rodada**:
  - `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCCorpseLootJournal.cs.meta`
  - `Assets/Editor/Assembly-CSharp-Editor/Tests/MobileNPCFriendsHostWorkshopTests.cs.meta`
  - `Assets/Editor/Assembly-CSharp-Editor/Tests/MobileNPCGroupRestoreKeyTests.cs.meta`
  - `Assets/Editor/Assembly-CSharp-Editor/Tests/MobileNPCReplicationBudgetTests.cs.meta`

## Critérios de aprovação da compilação

1. Abrir o projeto na versão Unity 2022.3.62f3 e aguardar importação sem perda de GUIDs; confirmar que as DLLs e símbolos `SDG.NetTransport` são resolvidos nos assemblies.
2. Executar testes EditMode: `MobileNPCFriendsHostWorkshopTests`, `MobileNPCGroupRestoreKeyTests` e `MobileNPCReplicationBudgetTests`; anotar nome e linha de quaisquer falhas.
3. Compilar `Windows64` e `Windows64_Headless` na MESMA revisão. Confirmar que o executável dedicado possui o marcador `UnturnedBetterDedicated.marker` válido.
4. Iniciar servidor sem Workshop, com Workshop IDs válidos e com ID inválido. Conferir o JSON físico e o log de download/dependências.
5. Conectar dois clientes remotos: confirmar criação dos proxies, entrada tardia, nomes, roupas, movimento, morte e recompra de estado após sair/retornar ao raio.
6. Alternar Normal/Combate Total e testar save/restart, incluindo bases, facções e loot pendente.
7. Medir FPS, CPU, GC e taxa de RPCs/bytes/s antes de concluir que houve ganho de desempenho.

## Bloqueios de release

**BLOQUEADO para publicação** enquanto não houver relatório de compilação sem erros e logs de teste do cliente e do dedicado. A auditoria foi apenas estática. Não há acesso a uma execução do Unity Editor, nem prova de funcionamento em Steam, a partir da leitura do repositório.

## Observações de risco

- O cache de metadados por conexão conta tentativas de envio confiável, não confirmações de aplicação no cliente; um refresh de segurança pode corrigir estados atrasados, mas testes reais são indispensáveis.
- O journal de loot prioriza evitar duplicação em reinício inesperado; não garante zero perda sem uma transação compartilhada com o world save.
- O seletor Workshop ainda é um campo de IDs, não um navegador visual de inscrições e dependências.
- A passagem por um portal registrado continua dependendo de validação física do caminho; não considerar solucionadas as obstruções de interiores sem testes.

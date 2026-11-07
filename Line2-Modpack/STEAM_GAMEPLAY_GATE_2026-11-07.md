# Linha 2 — Gate de validação real no Steam (07/11/2026)

**Branch:** `line2/modpack-framework`. **Linha 1 e main não são alvos deste gate.**

## Evidência existente

- Os testes Python / BrainTests / auditoria estática de SDK foram executados na pipeline anterior, mas não são uma build real do módulo Steam.
- O serviço localhost `AIService/server.py` é apenas um decisor determinístico: não aplica dano nem move Unity GameObjects.
- O `SteamNpcPrototypeRuntime` só autoriza protótipos no singleplayer local com cheats, sem BattlEye. Não há replicação multiplayer.
- O `LiveNpcDecisionBridge` agora envia a fração real de saúde, dano recente e cobertura validada pelo runtime. Sugestões defensivas são recusadas sem dano/cobertura/rota local apropriados.
- A transferência de loot do chão continua bloqueada quando não há remoção atômica do item de origem no Steam.
- Nesta rodada foram adicionados adaptadores experimentais de persistência de sobreviventes por mapa, necessidades, interação/comércio por XP, reposição atrasada, ataque de zumbis a NPCs, rally de hordas existentes e múltiplos pellets por tiro. Todos ainda exigem compilação contra os assemblies Steam e teste de gameplay.
- A aprovação da pipeline não comprova navegação indoor, dano de zumbis em NPCs, A-Life, bases, comércio ou persistência.

## Preparação

1. No Windows, atualizar a branch e a instalação oficial do jogo.
2. Sem BattlEye, compilar o módulo com a pasta de jogo real:
   `dotnet build .\ClientModule\Unturned2.Line2.Probe.csproj -p:UnturnedDir="C:\Program Files (x86)\Steam\steamapps\common\Unturned"`
3. Copiar `ClientModule/Unturned2.Line2.module` e o DLL gerado para `Unturned/Modules/Unturned2.Line2/`.
4. Executar `python AIService/server.py`, abrir Washington em singleplayer com cheats, ativar NPCs no menu F7.
5. Salvar `Unturned2-Line2/probe.log`, a saída do build e os logs do jogo após a sessão. Não compartilhar identificadores pessoais presentes nos logs.

## Matriz de verificação (não marcar como aprovada sem evidência no jogo)

| Cenário | Evidência necessária | Estado |
|---|---|---|
| Carregamento do módulo | `Module initialized` e menu F7 | Pendente de teste Steam |
| Spawn e anatomia | NPC visível, colisor funcional, nome e hitboxes | Pendente de teste Steam |
| Percepção e localhost | `NPC_AI_LIVE_BATCH_OK` com NPC válido | Pendente de teste Steam |
| Saúde real e recuo | Dano validado reduz HP; sob pressão NPC procura cobertura/recuo | Pendente de teste Steam |
| Combate completo | Armas gastam munição real e respeitam recarga, cadência, precisão e LOS | Pendente de teste Steam |
| Explosões | Granada nativa causa dano verificado aos NPCs na camada Agent | Pendente de teste Steam |
| Portas e janelas | Entrada/saída por portal real sem atravessar paredes | Pendente de teste Steam |
| Zumbis contra NPCs | Zumbi percebe, persegue e causa dano no NPC | Adaptador local experimental integrado; teste Steam pendente |
| Hordas/regiões | População vanilla respeitada e transição regional | Rally local no mesmo nav bound; perseguição entre regiões NÃO implementada |
| Loot do chão | Transferência atômica sem duplicação/perda após reinício | Bloqueado até API segura |
| Comércio/bases/A-Life | Ciclo de interação, tarefas, materiais e mundo persistido | Diálogo/comércio básico e necessidades implementados; bases e A-Life completo NÃO implementados |
| Multiplayer | Host autoritativo, replicação, entrada/saída, reconexão | Não integrado; runtime local suspende NPCs se entrar outro jogador |

**Regra de liberação:** só anunciar uma build integrada de gameplay após passar os testes automatizados, compilar contra assemblies Steam reais e observar os cenários acima sem regressões. Não criar implementação de loot com remoção otimista do drop, nem liberar NPCs locais não replicados em multiplayer.

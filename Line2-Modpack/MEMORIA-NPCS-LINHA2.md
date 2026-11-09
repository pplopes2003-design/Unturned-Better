# Memória individual dos NPCs — Linha 2

**Implementado na branch `line2/modpack-framework`:** integração do snapshot de experiências, rotas aprendidas e conversas presenciais entre aliados. Ainda sujeito à compilação com a DLL Steam do jogador e à validação in-game.

## Tipos de memória

| Classe | Dados | Conservação |
| --- | --- | --- |
| `Line2MobileNPCPlayerKnowledge` | Últimas pistas de visão, som e rádio; confiança e raio de incerteza | **Temporário**: expira, nunca salva coordenadas de jogadores ocultos |
| `Line2MobileNPCAdaptiveMemory` | Até 48 experiências por indivíduo: loot, tiro em jogadores/zumbis/NPCs, acesso a cobertura, flanco | **Persistente** para o mesmo sobrevivente em `npc-saves/*.json` |
| `Line2MobileNPCTravelMemory` | Até 8 rotas fisicamente percorridas, máximo de 36 pontos por rota | **Persistente** para o mesmo sobrevivente, mas o caminho é sempre revalidado fisicamente |
| `Line2NpcExperienceExchange` | Uma lição recente de experiência direta, relatada por um membro a outros do mesmo grupo próximos | **Aprendizagem indireta** de peso reduzido; quem ouviu não retransmite automaticamente |

## Compatibilidade do save

- `Line2NpcPersistence.ActorRecord` possui dois campos opcionais (`experiences`, `learnedRoutes`), preservando compatibilidade com saves anteriores de schema 1 sem esses campos.
- `SteamNpcPrototypeRuntime.SaveCheckpoint` exporta ambos os componentes apenas para os personagens vivos.
- `TryRestoreOne` restaura a memória do personagem após confirmar a mesma identidade, grupo, itens e ferimentos.
- Saves com registros inválidos, infinitos, NaNs, trajetos de comprimento anormal ou contagens acima do limite são rejeitados antes de sobrescrever os arquivos bons.
- Limite de arquivo aumentado para 1 MiB para comportar os registros delimitados de até 32 NPCs. Mantenha `.bak` no teste.
- `OnValidatedDeath` apaga a experiência e o percurso do falecido. O ticket de reposição é de um novo indivíduo, não de uma cópia do antigo.
- A última posição percebida do jogador permanece temporária e não é salva.

## Regras da conversa entre aliados

1. Somente NPCs vivos, mesmo grupo/facção, até 14m, sem obstáculos físicos entre os interlocutores.
2. O NPC precisa ter **realmente aprendido** algo a partir de uma operação cuja conclusão foi verificada. Receber relato não cria outro relato para retransmissão.
3. O receptor ganha peso 0,35 vs peso 1,0 na observação original.
4. Até 3 ouvintes por conversa, cooldown de 18 segundos após transmissão bem-sucedida.
5. Sem acesso à localização real do jogador e sem autorização de caminho: o A* original e os testes de chão continuam independentes.

## Logs de verificação

No PowerShell, com o jogo fechado:

```powershell
$log = "C:\Program Files (x86)\Steam\steamapps\common\Unturned\Unturned2-Line2\probe.log"
Select-String $log -Pattern "NPC_MEMORY_RESTORED|NPC_EXPERIENCE_SHARED|NPC_SAVE_OK|NPC_SAVE_FAILED|NPC_SAVE_LOAD_REJECTED" -Context 0,2
```

**Teste:** depois de combate ou exploração, feche o jogo normalmente; faça backup de `Unturned2-Line2/npc-saves`; reabra Washington na mesma modalidade, aproxime-se da área onde o NPC foi salvo e confirme `NPC_MEMORY_RESTORED` com `experiences` e `routes`. Para ter registros maiores que zero, o NPC precisa efetivamente concluir ações de aprendizado no gameplay.

**Limitação:** CI cobre a serialização e política de validação. Ainda é necessário testar a DLL contra `Unturned_Data/Managed` da instalação Steam real, efeitos na movimentação e compartilhamento dentro do mapa.

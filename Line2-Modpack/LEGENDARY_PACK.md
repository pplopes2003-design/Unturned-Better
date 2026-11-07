# Linha 2 — Pacote Lendário (Auto 24, Eaglefire Automática, 12 Gauge Incendiária)

**Estado:** implementado nos geradores Python, com testes unitários adicionados. Ainda **não validado no Unturned Steam**. Não modifica arquivos vanilla e não requer a Linha 1.

## IDs propostos (não globalmente reservados)

| Item | ID | Características |
|---|---|---|
| Auto 24, anteriormente Auto 32 | **32610** | Bluntforce automática, 24 cartuchos, recuo balanceado, Rarity Legendary |
| Carregador Auto 24 | **32611** | Amount 24, Rarity Legendary |
| Eaglefire Automática | **32612** | Semi, Bursts 3, Auto, Rarity Legendary, modelo Eaglefire original |
| 12 Gauge Incendiária experimental | **32613** | Mesmo calibre da munição vanilla 113, Rarity Legendary, pequena área de impacto |
| LegendaryLootPool (tabela Spawn, não item) | **32614** | Pool ponderado opcional com os itens 32610, 32612 e 32613 |

**Compatibilidade 12 Gauge:** reutiliza os campos de calibre do magazine vanilla ID 113, portanto é compatível com as armas que aceitam esse magazine. Não é garantia para toda arma da Workshop denominada "12 gauge" que use um calibre personalizado.

**Efeito:** o protótipo da munição usa `Explosive`, `Range 1.25` e dano de área reduzido, com apenas `Pellets 1` para não multiplicar explosões de pellets e derrubar o FPS. **Ainda não aplica o verdadeiro efeito Burner/ignição, nem propagação de fogo ou dano contínuo.** Isso requer um efeito validado ou módulo C# de impacto autoritativo. Não afirmar que a munição já causa queimadura.

## Atualizar Auto 32 já instalada

1. Fechar o Unturned.
2. Abrir Modpack Manager atualizado.
3. Aba **Managed Installs** → selecionar `Content/Auto32` → **Remove managed mod**. O gerenciador move para recovery.
4. Na linha de armas, clicar **Upgrade Auto32 to 24**. Isso edita apenas o staging e mantém IDs e GUIDs originais.
5. Aba **Local Staging** → selecionar `Auto32` → **Install local mod** → `Content`.
6. Abrir Washington, habilitar cheats e usar `/give 32610` e `/give 32611`.

Se quiser gerar a Auto 24 do zero em um staging vazio, o botão **Generate Auto 24** usa capacidade 24 e raridade Legendary por padrão. O gerador não sobrescreve diretório existente.

CLI alternativa (após a remoção e com jogo fechado), de `Line2-Modpack/ModpackManager`:

```powershell
python upgrade_auto24.py --staging "$env:USERPROFILE\Unturned2-ModpackManager\staging\Auto32"
```

## Criar Eaglefire Automática e 12 Gauge Incendiária

No Modpack Manager atualizado:

1. Confirme os caminhos Game e Workshop e clique **Scan / Refresh**.
2. Clique **Generate Legendary Pack**; aceite IDs 32612 e 32613 caso estejam livres.
3. Veja `%USERPROFILE%\Unturned2-ModpackManager\staging\LegendaryPack\README_TESTING.txt`.
4. Aba **Local Staging** → `LegendaryPack` → **Install local mod** → `Content`, com jogo fechado.
5. Abra Washington. Teste: `/give 32612` e `/give 32613`.

CLI alternativa:

```powershell
python build_legendary_items.py --game "C:\Program Files (x86)\Steam\steamapps\common\Unturned" --output "$env:USERPROFILE\Unturned2-ModpackManager\staging\LegendaryPack"
```

## Loot natural lendário em Washington

O gerador cria `Spawns/LegendaryLootPool/Asset.dat` com os três itens lendários (peso interno 1 cada).

Pelo mecanismo oficial `Roots` de Spawn Assets, o gerador procura no Unturned local um Spawn asset com ID **346** e pasta que contenha `Washington_Special_America`. **Somente quando ambos correspondem**, acrescenta:

```text
Roots 1
Root_0_Spawn_ID 346
Root_0_Weight 1
```

Nenhum arquivo original é modificado e `Root_0_Override` não é usado. O peso 1 é relativo aos demais pesos do spawn original, e **não equivale a 1% garantido**.

Se não encontrar a tabela verificada, o gerador não inventa uma raiz: `washington_root_attached` será `false` e o loot pool não será ligado ao mapa automaticamente. Um `Root` anexado também **não garante** spawn até testar o carregamento da tabela com a instalação real e confirmar que Washington utiliza esse pai.

Os itens continuam disponíveis via `/give` independentemente do spawn natural.

## Rollback e logs

Para retirar `LegendaryPack`, feche o jogo e use **Managed Installs → Content/LegendaryPack → Remove managed mod**. Não edite cópias já instaladas, porque o gerenciador verifica os hashes. Se algo não aparecer, examine `Unturned/Logs/Client` para mensagens de asset/bundle, IDs e GUIDs.

## Pendências antes de considerar concluído

- Compilação/testes automáticos Windows dos geradores mais recentes (GitHub Actions).
- Teste in-game da Auto 24 e sua capacidade/recuo.
- Verificar modos e animações da Eaglefire real; possível ajuste da cadência.
- Testar compatibilidade com armas que compartilham o calibre 12G vanilla.
- Implementar e testar efeito real de queima reduzida inspirado no Burner.
- Verificar vínculo da tabela de Washington e frequência natural observada em várias sessões.
- Integração de NPCs, facções e zumbis é uma frente independente.

Referências:
- https://docs.smartlydressedgames.com/en/latest/items/gun-asset.html
- https://docs.smartlydressedgames.com/en/latest/items/magazine-asset.html
- https://docs.smartlydressedgames.com/en/latest/assets/spawn-asset.html

## Visual Burner effect detection

The Legendary pack builder additionally scans `Unturned/Bundles/Effects` for an unambiguous existing effect whose folder/path contains `Burner` and `Explosion` (or `Explode`). If exactly one effect ID is found, it is referenced by the incendiary magazine's `Explosion` field. Otherwise no effect ID is guessed, and the manager reports `Visual burner effect: not found`.

The effect lookup does **not** create the gameplay-specific Burner effect of converting a zombie or applying a persistent fire debuff. The small `Range 1.25` blast and `Pellets 1` reduce the area and effect frequency. Visuals and damage must be verified in-game; the `effect_configured` output indicates only a reference was written, not that it rendered.

The Legendary Eaglefire also uses the `balanced` recoil preset to avoid uncontrolled automatic recoil.

## Especificação corrigida: rajada automática enquanto segura o gatilho

O usuário solicitou **rajadas repetidas**, não apenas a opção vanilla `Bursts 3`.

- `Semi`: um tiro a cada acionamento.
- `Burst 3` nativo: três tiros por acionamento; **não repete sozinho**.
- `Auto` nativo: tiros contínuos com cadência uniforme.
- **Novo modo desejado Repeating Burst**: segurar o gatilho -> 3 tiros com intervalo de ~0,10 s entre disparos -> pausa de ~0,25 s após o terceiro -> repete a sequência enquanto houver munição; soltar o gatilho cancela imediatamente o próximo disparo. Não cria disparos ao recarregar, desequipar ou perder autoridade de host.

Foi criado o algoritmo isolado `ClientModule/RepeatingBurstScheduler.cs` e um teste determinístico de estados em `RepeatingBurstSelfTest.cs`, que registra `BURST_SCHEDULER_SELFTEST_OK` quando o módulo é inicializado e o agendador passa.

**Limite essencial:** o algoritmo atualmente não recebe estado real do gatilho e não chama o mecanismo nativo de disparo do Unturned. A Eaglefire instalada continua com `Semi`, `Bursts 3` e `Auto` do jogo. Só depois de descobrir/verificar a API de `UseableGun`, os eventos do disparo e a autoridade da sessão será possível ligar o agendador sem disparos duplicados, problemas com munição ou dessintonia. Nenhum teste demonstrou ainda as rajadas repetidas no Steam.

Critério de aceitação no jogo: 2 s de gatilho mantido devem produzir várias séries **3 + pausa + 3 + pausa** (até acabar munição), interrompendo imediatamente quando soltar, com dano/múltiplos jogadores contabilizados pelo disparo original do jogo. Não implementar dano paralelo à própria arma.

# Granada de Ácido Thrower — Linha 2

## O que foi criado

- **Item:** Granada de Ácido Thrower (ID padrão **32615**, raridade **Epic**).
- **Tabela de spawn:** `32616`, inserida com peso relativo **1** na tabela `Washington Special America` **346** somente se ela for encontrada e verificada nos assets instalados.
- **Prefab, arremesso, colisões e temporizador:** reutilizam a granada de fragmentação nativa (ID **254**) sem distribuir modelos proprietários.
- **Visual:** efeito ácido azul nativo `Zombie_3`, GUID `ae477aac40b64d3c8ce8e538daffecf5`, identificado diretamente em `Zombie.askAcid()` no código-base.
- **Explosão inicial (vanilla):** raio **3 m**, dano base jogador **18**, zumbi **24**, animal **18**, construções/veículos **0**; impulso baixo.
- **Corrosão adicional (apenas com o módulo Line2 C# atualizado):** três pulsos, a um segundo de intervalo após o fusível, com raio **2,3 m**, dano base por pulso jogador **5**, zumbi **8** e NPC customizado até **6** com atenuação por distância; sem dano a veículos/construções.

## Geração e instalação

No Modpack Manager atualizado:
1. Feche o Unturned.
2. Clique em `Scan / Refresh`.
3. Clique em **Generate Thrower Acid Grenade**, confirme IDs `32615` (item) e `32616` (spawn).
4. Se o gerador encontrar colisão de ID ou não confirmar o spawn raiz 346, interromperá a geração sem gravar pacote parcial. O GUID do efeito é verificado no código-base e não depende de pastas `.dat` desempacotadas.
5. Instale `ThrowerAcidGrenade` pelo gerenciador, em **Install local mod**.
6. Para os três pulsos de dano ácido, recompile e instale o módulo C# usando `verify-and-stage-combat-total.ps1 -UnturnedDir "C:\Program Files (x86)\Steam\steamapps\common\Unturned" -Install`.
7. Reinicie o Unturned (Washington singleplayer, cheats) e teste `/give 32615`.

CLI alternativa (na pasta `Line2-Modpack/ModpackManager`):

```powershell
python build_acid_grenade.py --game "C:\Program Files (x86)\Steam\steamapps\common\Unturned" --output "C:\Temp\ThrowerAcidGrenade"
```

O caminho escolhido para `--output` **não pode existir** antes de executar.

## Spawn e raridade

- `Epic` é a raridade exibida no inventário.
- O peso **1** na raiz 346 torna o item uma adição rara **relativa**, não uma porcentagem fixa garantida. A chance efetiva depende da tabela original e da distribuição de spawns do mapa.
- Atualmente esta integração é **específica de Washington**. Nenhum spawn em outros mapas é presumido. Teste a tabela de loot efetivamente carregada pelo Steam antes de afirmar geração natural.

## Segurança de gameplay e limitações

- O código não modifica a física nativa do arremesso nem substitui `Grenade.Explode`.
- Corrosão adicional só funciona em singleplayer host local, com módulo Line2 atualizado; o item isolado ainda tem explosão e visual nativos.
- O dano ácido posterior utiliza o `DamageTool.explode` original com tipo `ZOMBIE_ACID`; os NPCs customizados recebem uma aplicação adicional delimitada por pulso porque não são alvos nativos.
- O campo não fica permanentemente no mundo e não transmite conhecimento do jogador aos NPCs.
- Necessita teste real no Unturned Steam. Testes CI do gerador não substituem a compilação contra as DLLs instaladas nem confirmam a renderização do efeito.

## Logs

Procure no `Unturned2-Line2/probe.log`:
`ACID_GRENADE_ARMED`, `ACID_GRENADE_PULSE` e `ACID_GRENADE_PULSES_COMPLETE`.

Se o efeito de ácido não aparecer, confirme se o GUID `Zombie_3` ainda existe na build Steam instalada. Se os pulsos não aparecerem, verifique se o módulo C# atualizado foi compilado e instalado.

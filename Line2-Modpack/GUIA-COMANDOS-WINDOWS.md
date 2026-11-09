# Linha 2 — comandos Windows (PowerShell, CMD e jogo)

> Branch: `line2/modpack-framework`. Os caminhos abaixo usam o local de teste informado no Windows. Se voce extraiu outro ZIP, altere `$base`.

## 1. Preparar o caminho no PowerShell

```powershell
$base = "C:\Unturned2-Dev\Unturned-Better-line2-modpack-framework\Line2-Modpack"
$game = "C:\Program Files (x86)\Steam\steamapps\common\Unturned"
Test-Path "$base\AIService\server.py"
Test-Path "$base\ModpackManager\gui.py"
Test-Path "$game\Unturned.exe"
```

Se algum teste retornar `False`, ajuste o caminho da pasta extraida.

## 2. Iniciar IA localhost (janela separada)

```powershell
cd "$base\AIService"
python server.py --port 8765
```

Ou execute `iniciar-ia.cmd` na pasta `Line2-Modpack` pelo Explorador.
O terminal do servidor deve permanecer aberto enquanto a IA estiver sendo testada.

Para verificar em **outro** terminal:

```powershell
curl.exe http://127.0.0.1:8765/health
```

Observacao importante: o servico Python responde a observacoes e envia **sugestoes** via localhost; o jogo C# continua autoritativo para visao, movimento, armas e dano. Nao e necessario iniciar Python para carregar o Modpack Manager. Sem o servico, o nucleo local de decisoes C# continua presente, mas nao recebe sugestoes do Python.

## 3. Abrir o menu Modpack Manager

Em **outro** PowerShell:

```powershell
cd "$base\ModpackManager"
python gui.py
```

Ou execute `abrir-modpack-manager.cmd` na pasta `Line2-Modpack`.

Dentro do gerenciador:
- Game: `C:\Program Files (x86)\Steam\steamapps\common\Unturned`
- Workshop opcional: `C:\Program Files (x86)\Steam\steamapps\workshop\content\304930` (mude se a Steam usar outra biblioteca)
- Primeiro `Scan / Refresh`, depois `Generate Auto 24` / `Generate Legendary Pack` e, finalmente, `Install local mod` para instalar como `Content`.

Os arquivos gerados ficam na pasta de staging do gerenciador; **gerar nao instala**.

## 4. Recompilar e instalar o modulo da Steam

Com o Unturned fechado:

```powershell
cd $base
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\verify-and-stage-combat-total.ps1 -UnturnedDir $game -Install
```

Este comando recompila contra as DLLs da instalacao Steam. O GitHub Actions nao substitui esse teste local. Nao requer recompilar para modificar apenas scripts Python.

## 5. Iniciar teste no jogo

- Iniciar Unturned pela Steam, sem BattlEye, preferencialmente singleplayer Washington com cheats.
- **F7**: menu de controle local dos NPCs. No campo F7, use os comandos sem `!u2`.
- No chat do jogo, use o prefixo `!u2`:

```text
!u2 npc on
!u2 max 8
!u2 combat total
!u2 natural off
!u2 spawn hostile 1
!u2 status
```

Outros exemplos: `!u2 spawn friendly 2`, `!u2 spawn neutral 1`, `!u2 hordes on`, `!u2 zombies on`.

Atalhos diagnosticos: F8 varredura de colisores; F9 teste localhost; F10 APIs NPC; F11 zumbis; F12 objetos de NPC; F6 arma.

## 6. IDs dos itens de teste

```text
/give 32610
/give 32611
/give 32612
/give 32613
```

- 32610: Auto 24; 32611: carregador; 32612: Eaglefire Legendary; 32613: 12G incendiaria experimental.
- 32614 e uma tabela de spawn, nao item para segurar.
- IDs sao defaults locais. Instale cada pacote primeiro e verifique conflitos.

## 7. Logs e diagnostico

```powershell
$log = Join-Path $game "Unturned2-Line2\probe.log"
Get-Content $log -Tail 100
Select-String -Path $log -Pattern "NPC_SPAWN_FAILED|NPC_DAMAGE|NPC_NATIVE_GUN_DAMAGE|NPC_RAGDOLL|NPC_TACTICAL_ROUTE_BLOCKED|NPC_NATIVE_ASTAR_ROUTE_OK|NPC_AI_LIVE_BATCH_OK" -Context 0,2
```

Os novos caminhos de gameplay da Linha 2 sao experimentais. Resultados positivos no teste deterministico nao provam integracao completa de NPC, ragdoll, balistica ou navegacao no Unturned Steam.

## 8. Testes Python (opcional)

```powershell
cd "$base\AIService"
python -m unittest discover -s . -p "test_*.py"
cd "$base\ModpackManager"
python -m unittest discover -s . -p "test_*.py"
```

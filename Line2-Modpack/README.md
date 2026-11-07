# Unturned 2 — Linha 2: módulo e scanner diagnóstico

Branch independente: `line2/modpack-framework`. Não é necessário alterar o jogo-base.

## Compilação

Requer Visual Studio/.NET Framework 4.7.2 targeting pack e a instalação Steam do Unturned.

```powershell
dotnet build .\ClientModule\Unturned2.Line2.Probe.csproj -p:UnturnedDir="C:\Program Files (x86)\Steam\steamapps\common\Unturned"
```

A API do Unturned pode variar conforme a versão: **ainda não houve compilação ou teste de execução em uma instalação local real**.

## Instalação do módulo

Copie o manifesto `ClientModule/Unturned2.Line2.module` e a DLL compilada para:

```text
Unturned/
  Modules/
    Unturned2.Line2/
      Unturned2.Line2.module
      Unturned2.Line2.Probe.dll
```

Jogue **sem BattlEye**. Entre em Washington e pressione **F8** perto de uma construção. É um comando de diagnóstico local; não modifica o mapa, NPCs ou colisores.

Arquivos produzidos no diretório de trabalho do jogo, subpasta `Unturned2-Line2`:
- `probe.log`: inicialização e diagnóstico do scan.
- `scans.jsonl`: um JSON por varredura. Contém posição da câmera, collider, bounds, layer, hierarquia e categoria provisória.

## Limites

- Usa `Camera.main` como centro da varredura, não a posição exata dos pés do jogador.
- Apenas colliders ativos/carregados e próximos (22 m), com buffer máximo de 1024 resultados e exportação limitada a 400 transforms únicos. `bufferFull` e `truncated` indicam amostragem incompleta.
- Candidatos são detectados por palavras na hierarquia do objeto; **não** equivalem a portas, janelas ou rotas utilizáveis.
- Não examina objetos sem colliders, não calcula conectividade e não gera NavMesh.
- Cada execução da tecla F8 escreve uma linha JSONL. IDs de instância só são válidos durante a sessão do Unity.
- Se outra ferramenta ocupar F8, altere a tecla no método `WorldScanner.Update`.

## Critérios de validação

1. Compilar a DLL sem erros na instalação local.
2. Iniciar o jogo sem BattlEye e confirmar `Module initialized`.
3. Pressionar F8 ao lado de uma construção em Washington e confirmar `SCAN_OK`.
4. Conferir que `scans.jsonl` contém hierarquia, bounds, layers e candidatos.
5. Comparar visualmente candidatos com portas e janelas reais.
6. Compartilhar `probe.log` e `scans.jsonl` para desenvolver o classificador de portais com evidência.

## Próximo marco

Scanner semântico de objetos/portas/janelas (componentes SDG e estados da porta); validação das passagens por geometria; grafo interno/externo; cache e IPC com serviço de IA. 

## v0.5 — AI service handshake

The C# client now creates `LocalAiBridge` along with `WorldScanner`. Press **F9** inside a loaded map to make a manual, nonblocking loopback HTTP request to `http://127.0.0.1:8765/v1/decide`. The service currently returns `idle`. The bridge validates protocol, agent ID, tick, allowable response age and action before logging a result.

To try it:
1. Install Python 3.10+; in `Line2-Modpack/AIService`, execute `python -m unittest discover -s . -p "test_*.py"`, then `python server.py`.
2. Build the client C# project against your Unturned Steam DLLs; copy the DLL and .module manifest to `Modules/Unturned2.Line2`.
3. Start Unturned without BattlEye; join Washington, press **F9**, and inspect `Unturned2-Line2/probe.log`.
4. F8 still exports collider observations to `scans.jsonl`.

**Not yet validated in the actual Unturned client.** A successful F9 probe confirms only network transport, not NPC AI or navigation.

## v0.6 — observations, not commands

Press **F9** inside Washington with `python AIService/server.py` running. The client samples camera position, direction and a bounded collider count, sends a nonblocking HTTP request to the localhost AI Service, then logs the response. Press **F8** to run the independent world scanner.

The service can additionally evaluate **synthetic NPC observations** for deterministic test actions (idle, investigate, seek_cover, engage, retreat). No external action affects the game yet. The bridge is **read-only** and is not evidence of functioning NPC AI.

Next critical milestone: inspect the actual Unturned NPC/entity representations, implement an adapter under game/server authority, and test the module on Steam before allowing action execution.

## v0.7 — NPC adapter boundaries and simulated round-trip

Added `AIService/simulated_npc_adapter.py` which maintains synthetic agents with stable unique identifiers and records read-only observations. The local AI service determines an *intention* (idle/investigate/seek_cover/engage/retreat); the simulator validates protocol version, agent identity, tick freshness, replay protection, and decision TTL before recording that intention. It never changes an NPC's position, damage, or actual game state.

Try in separate terminals:

```powershell
cd AIService
python server.py
```

```powershell
cd AIService
python smoke_simulation.py
python -m unittest discover -s . -p "test_*.py" -v
```

`ClientModule/NpcBridgeContracts.cs` defines `IGameNpcAdapter` and an opt-in intention gate. **The actual execution switch is deliberately hardcoded off**, and the interface has no implementation that accesses Unturned entities yet. `LocalAiBridge` is still triggered manually by F9 and observes the camera, not an actual NPC.

The new GitHub Actions workflow `.github/workflows/line2-python.yml` is scoped to the Line 2 branch to validate the Python components. This is not a C# build or an integration test against a Steam installation.

Next: inspect Unturned's real entity APIs and prefab ownership model, implement `IGameNpcAdapter` with an authoritative host, test NPC selection and navigation in-game, then consider activating permitted intentions behind an explicit test-only setting.

## Windows-only validation and NPC API inventory (2026-11-06)

The GitHub Actions Python workflow now uses **windows-latest** only, matching the supported Windows 11 development target. Prior Ubuntu failures were CI cross-platform checks, not a Linux requirement for Unturned.

For the real Steam game:
1. Build `ClientModule/Unturned2.Line2.Probe.csproj` against the installed Windows Unturned managed assemblies.
2. Copy `Unturned2.Line2.Probe.dll` and `Unturned2.Line2.module` into the module's local game directory as described above.
3. Start Unturned without BattlEye and load Washington.
4. Press **F8** for the collider scanner, **F9** for the localhost AI Service health/decision request, and **F10** for a read-only NPC API inventory.
5. Check the game working directory in `Unturned2-Line2`: `scans.jsonl`, `probe.log`, and the new `npc-api-discovery.txt`.

F10 lists candidate NPC, zombie, object, door, barricade and navigation types in loaded game assemblies, capped by number of types/methods and output size. It also lists a bounded sample of active matching MonoBehaviours. This is **diagnostic only** and does not prove mobile NPC functionality. Avoid sharing logs if scene object names include private information.

The GitHub Actions workflow tests only **Python files**. It cannot validate the actual Unity/Unturned C# module without compatible managed assemblies or a local Steam installation. No game build was run in this development environment.

## Milestone: Steam-native Auto 32 prototype

The standalone Line 1 Unity build is **paused**. The working target is now Windows Unturned Steam without BattlEye.

`ModpackManager/build_auto32.py` creates an **experimental** Auto 32 weapon and 32-round magazine in the manager's *staging* directory using the locally installed vanilla Bluntforce/12 Gauge source definitions. No original vanilla files are modified and no vanilla bundles are committed. IDs 32610 and 32611 are only candidate IDs and must pass scans.

From `ModpackManager`:

```powershell
python build_auto32.py --game "D:\SteamLibrary\steamapps\common\Unturned" --output "$env:USERPROFILE\Unturned2-ModpackManager\staging\Auto32" --scan-root "D:\SteamLibrary\steamapps\workshop\content\304930"
python -m unittest discover -s . -p "test_*.py" -v
```

You may alternatively choose `Generate Auto 32` from the manager's Windows GUI after setting its game and Workshop directories. Read [AUTO32.md](ModpackManager/AUTO32.md) before deploying.

**Limitations:** no compiled item model and no runtime proof of the vanilla `core.masterbundle` pointer, no validated local content install path for this particular Steam build, and no reload-by-four mechanism. A successful staging build does **not** prove the item appears in-game. The next in-game evidence needed is the game's `Logs/Client` error output and numeric item-give test.

## One-command Windows preparation (no game changes)

Open **PowerShell** in `Line2-Modpack/scripts` and run:

```powershell
.\prepare_windows.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Unturned"
```

The script requires Python 3.10+ on PATH and a Windows Unturned installation. It runs Python AIService and ModpackManager tests, discovers the Workshop directory in the same Steam library if present, and generates the experimental Auto32 package to your manager staging area. It will NOT overwrite a preexisting Auto32 staging directory or deploy anything into Unturned. It saves a log under `%USERPROFILE%\Unturned2-ModpackManager\logs`. If your PowerShell policy blocks local scripts, use the command-line Python steps in `ModpackManager/AUTO32.md` rather than changing system-wide policy.

## Zombies: vanilla population and optional hordes

The previous `feature/offline-zombie-survival` line contained a 1.5x offline survival population multiplier in `ZombieManager.generateZombies`. This **has not been carried over** to Line 2; the standalone Line 1 is paused.

The new `AIService/horde_policy.py` is an **offline planning prototype** for grouping eligible zombie spawnpoints into bands of ~8–18, within a 25m leader radius. Its caller must supply the *real* vanilla regional zombie budget. `preserve_vanilla_population=True` is mandatory; exceeding the vanilla cap is rejected. The module does **not** spawn zombies or hook into the Steam version yet.

Test with `python run_tests.py AIService`. Actual hordes in-game remain pending a validated C# server-side spawn adapter. Avoid changing `Zombies.Spawn_Chance` or region limits through our modpack.

## First Steam-success milestone: Auto 32 spawns and fires

The user successfully installed the generated Auto32 and confirmed it shoots. The recoil was uncontrolled; new Auto32 generation now explicitly uses a `balanced` recoil preset. The manager GUI also has a **Tune Auto 32 recoil** button; it refuses to tune while a managed deployed Auto32 exists, to preserve registry fingerprints. See [AUTO32.md](ModpackManager/AUTO32.md).

Next independent integration checkpoint: use **Check AI service** in the manager GUI. Open a PowerShell in `Line2-Modpack/AIService`, run `python server.py`, then press **Check AI service** in the GUI. The new health check confirms only loopback connectivity and protocol v1, not actual NPC movement. Once successful, the C# DLL build and F8/F9/F10 in-game diagnostics are next.

The four-round reload and Steam-side horde/NPC movement remain unimplemented. The older standalone Line 1 remains paused.

## F12 — Discover NPCs in the live Steam game

The diagnostic module now also supports **F12**, a read-only scan of active NPC/Human-named Unity components and their positions. It writes `npc-snapshots.jsonl` and an `NPC_SNAPSHOT_OK` entry in `probe.log`. It does **not** create or drive mobile NPCs. See [TEST_IA_HORDAS_ZUMBIS.md](TEST_IA_HORDAS_ZUMBIS.md) for the testing sequence and interpretation.

Windows users can build and deploy the **diagnostic-only** C# module with:

```powershell
.\scripts\build_diagnostics_windows.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Unturned"
```

This requires the .NET SDK and .NET Framework 4.7.2 reference assemblies; it fails safely if compilation fails, and backs up any existing `Unturned2.Line2` module before copying. Windows PowerShell execution policy may block local scripts; use `dotnet build` and manually copy the DLL + manifest in that case. Close the game before updating the module. The C# module has **not yet been compiled and executed against the user's Unturned Steam build**.

Do not mistake `F12` NPC-like matches, `F9` camera-to-service connectivity, or synthetic `smoke_simulation.py` intentions for real moving/attacking NPCs. The true Steam-side NPC adapter remains pending.

## NPC delivery: integrated milestone, not a drip-feed of game tests

The user requested **one comprehensive NPC playtest** rather than repeated testing of unfinished NPC subsystems. Scope, performance budget, release gates and the eventual single acceptance battery are documented in [NPC_INTEGRATED_ACCEPTANCE.md](NPC_INTEGRATED_ACCEPTANCE.md).

The prerequisite **C# compilation check** remains: the user's Windows environment previously reported `No .NET SDKs were found`. To avoid PowerShell execution-policy errors, use the new `scripts/build_diagnostics_windows.cmd`, from Windows CMD or PowerShell:

```powershell
.\scripts\build_diagnostics_windows.cmd "C:\Program Files (x86)\Steam\steamapps\common\Unturned"
```

This command checks the SDK, compiles the **read-only** F8–F12 diagnostic module and installs it **only if an existing same-name module is absent**, refusing unsafe overwrites. It does not add game NPCs. For subsequent rebuilds, back up/remove the old diagnostic module folder before rerunning. Compiling with `dotnet build` manually is also supported.

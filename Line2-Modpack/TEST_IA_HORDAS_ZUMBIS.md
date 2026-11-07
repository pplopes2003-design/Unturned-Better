# Integrated testing roadmap — Steam Unturned (Windows)

The Auto 32 asset has already been confirmed to spawn and shoot in the user's Steam game. Everything below **must be validated separately**. Do not use the standalone Unity Line 1 build.

## A. Python AI Service and NPC decision protocol (offline)

From `Line2-Modpack/AIService`:

```powershell
python -m unittest discover -s . -p "test_*.py" -v
python smoke_hordes.py
```

In a terminal in the same folder, run `python server.py`. Keep it running. In a second terminal run `python smoke_simulation.py`. You should see idle/engage/seek_cover/retreat intentions accepted by the synthetic NPC. The simulator does **not** move or attack inside the Steam game.

In ModpackManager's GUI, use **Check AI service** to test `http://127.0.0.1:8765/health`. The GUI does not launch the service automatically.

## B. Build and install diagnostics client (first real integration gate)

Build from `Line2-Modpack` with the installed Steam managed assemblies and .NET Framework 4.7.2 targeting pack:

```powershell
dotnet build .\ClientModule\Unturned2.Line2.Probe.csproj -p:UnturnedDir="C:\Program Files (x86)\Steam\steamapps\common\Unturned"
```

If successful, copy the resulting `Unturned2.Line2.Probe.dll` and the `ClientModule/Unturned2.Line2.module` manifest into `Unturned/Modules/Unturned2.Line2`. Do not install before confirming the build succeeds. Keep Unturned closed while copying. Launch **without BattlEye**, enter singleplayer Washington and test:

| Key | Read-only function | Evidence |
|-----|--------------------|----------|
| F8 | Collider/object snapshot | `scans.jsonl` |
| F9 | Camera observation to AIService | `probe.log`, `AI_BRIDGE_OK` |
| F10 | NPC and navigation type inventory | `npc-api-discovery.txt` |
| F11 | Zombie movement/region snapshots | `zombie-snapshots.jsonl`, `ZOMBIE_SNAPSHOT_OK` |
| F12 | NPC-like component snapshot | `npc-snapshots.jsonl`, `NPC_SNAPSHOT_OK` |

Logs are saved under the game's *current working directory* `Unturned2-Line2` subfolder, which may differ from the install folder depending on Steam launch settings.

## C. F11 zombie pursuit diagnostics

The old Line 1 `ZombieManager` increased Survival offline population by 50%, but this was **not copied to Line 2**. No zombie population increase is active from this mod.

1. Find a zombie group in Washington. Press F11 once from a stationary position.
2. Aggro a zombie and move toward the boundary of its spawning area. Press F11 again after several seconds.
3. Move farther and press F11 a third time.
4. Review `zombie-snapshots.jsonl` for sampled instance IDs, positions, meters moved between snapshots, and a public `bound`/`region`/`nav` field *if available*.
5. If the original zombie disappears from loaded objects, it will not be in later samples. This is normal and **not evidence of cross-region chasing**.
6. This probe has a 128-zombie cap and scans live MonoBehaviours only when F11 is pressed. It does not modify AI or spawn counts.

**Cross-region pursuit is NOT implemented in Steam Line 2.** After obtaining diagnostic evidence, develop a game-authoritative, capped and performance-limited pursuit adapter. A `regionChanged` value in a log is observation, not proof that all cross-region restrictions are overcome.

## D. Horde tests

`python smoke_hordes.py` visibly checks that grouping e.g. 40 simulated zombies never exceeds the input vanilla budget. This is *offline simulation only*; actual horde spawns are **not connected** to Unturned yet.

Before enabling game-side hordes we need the F10 inventory and F11 snapshots, then a controlled adapter that rearranges actual game-approved spawnpoints without lifting the original zombie cap.

## E. What logs to share if troubleshooting

- Full `dotnet build` error output (if compilation fails).
- `probe.log` and `npc-api-discovery.txt` after F8–F11.
- The relevant lines of `zombie-snapshots.jsonl` from before/during/after pursuit.
- Python `test-results` logs if Python tests fail.

**Not verified:** C# compilation against the user's current Steam build or in-game F11 behavior. Do not claim NPC controls, cross-region pursuit, or real hordes until the respective adapters have passed the in-game tests.

## F. First Steam-side NPC test — entity discovery only

1. Update the `line2/modpack-framework` branch and rebuild the **C# module**. Python-only updates cannot add an F12 hotkey to an already installed DLL.
2. Install the rebuilt module under `Unturned/Modules/Unturned2.Line2`, close and restart Unturned without BattlEye.
3. Enter a map with NPCs that are actually loaded, or a trusted Workshop map with NPCs. **Washington may have no quest NPCs**, so `matchingComponents=0` is not inherently an error.
4. Stand near an NPC and press **F10** once to capture available NPC class/method signatures.
5. Press **F12** once next to the NPC, wait several seconds, and press **F12** again. Examine `npc-snapshots.jsonl` for component type, instance ID, position, distance to camera and meters moved.
6. In parallel, run `python AIService/server.py` and `python AIService/smoke_simulation.py`. Press F9 in-game to verify localhost transport.

This distinguishes two independent facts:
- F12 shows that the module can see real game components whose *type names* match NPC/Human. Such matches can include quest NPC components or unrelated helper components; review class names and transform positions before interpreting them.
- The Python AI service processes **synthetic** NPC observations. There is not yet a live `IGameNpcAdapter` implementation, so no game NPC is moved, attacked, damaged or given an action by the AI.

**Important:** do not spawn hundreds of NPCs or raise zombie counts to "stress test" the unvalidated integration. The F12 scan is bounded to 80 output records and triggered only by the hotkey, but it still enumerates active behaviours once per request; avoid repeatedly pressing it on a slow PC.

After the user returns the F10 report, F12 NPC snapshots and module build result, we can identify the real entity components and build a test-only authoritative mobile NPC adapter rather than claiming the simulated policy is live AI.

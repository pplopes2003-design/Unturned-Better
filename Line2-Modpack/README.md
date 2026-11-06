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

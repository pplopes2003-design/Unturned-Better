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

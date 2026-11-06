# Unturned 2 — Linha 2 (Mod/Modpack)

Projeto experimental separado da build Unity (Linha 1). **Não altera o jogo-base, não contém DLL pré-compilada e não contorna BattlEye.**

## Marco 0: módulo de diagnóstico

Objetivo: comprovar que o Unturned Steam, iniciado pela opção **Play without BattlEye**, carrega uma DLL local e registra a inicialização / entrada em mapa.

### Como compilar
1. Instale Visual Studio com suporte a C# e targeting pack .NET Framework 4.7.2 (ajuste o TargetFramework se a instalação local exigir outra versão suportada).
2. Descubra o diretório do jogo usando Steam > Unturned > Gerenciar > Explorar arquivos locais.
3. No terminal Developer PowerShell:

```powershell
dotnet build .\ClientModule\Unturned2.Line2.Probe.csproj -p:UnturnedDir="C:\Program Files (x86)\Steam\steamapps\common\Unturned"
```

O projeto referencia as DLLs instaladas localmente com o Unturned. A compilação **depende** dessas referências e precisa ser validada na instalação real.

### Instalação
Copie `ClientModule/Unturned2.Line2.module` e a DLL compilada `Unturned2.Line2.Probe.dll` para:

```text
Unturned/
  Modules/
    Unturned2.Line2/
      Unturned2.Line2.module
      Unturned2.Line2.Probe.dll
```

Inicie **sem BattlEye** e entre no Washington. Verifique o console/log do Unturned e o arquivo `Unturned/Unturned2-Line2/probe.log`.

### Critérios de aceite
- [ ] O jogo abre sem erro de carregamento de módulo.
- [ ] `initialize()` aparece no log.
- [ ] Ao entrar em Washington, o evento de carregamento escreve o nome da cena e a quantidade aproximada de colliders detectados na área central (até 64 no buffer).
- [ ] `shutdown()` aparece ao fechar o jogo.
- [ ] Sem efeitos sobre `main` ou a Linha 1.

### Limites e próximos passos
O diagnóstico inicial NÃO identifica portas/janelas por categoria, NÃO cria NPCs, NÃO implementa rede ou serviço de IA. O próximo marco adicionará um scanner de construções com logs estruturados e filtragem por hierarquia/camadas, depois protocolo IPC. 

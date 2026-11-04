# Unturned Better — linhas paralelas Steam modpack + serviço localhost (2026-11-04)

## Branches e regra de isolamento

- **Linha A (jogo independente):** `feature/release-candidate-cycle-2026-11-04` — continua sendo a release candidate principal, com sistema de NPC, armas, multiplayer próprio e navegação alternativa. Não mesclar a experiência Steam de volta sem testes.
- **Linha B (experimentação Steam):** `feature/steam-modpack-local-ai-prototype-2026-11-04` — criada a partir da A. Contém investigação de prefabs, protótipo localhost e esqueleto de plugin. **Não é um modpack jogável ainda.**
- `main` — preservada como base do U3 SDK / jogo sem nossas alterações.

## O que já existe na linha B

1. `SteamModpack/LocalAI/ai_service.py`: processo independente, Python 3 sem dependências extras, apenas `127.0.0.1:18742`. Endpoints:
   - `GET /health`
   - `POST /v1/decide` recebe `{"version":1,"sequence":19,"mode":"normal","agents":[{"npc_id":"id-local","observation":{...}}]}`. Até 64 agentes por lote, até 96 KiB por requisição, sem comandos arbitrários. Retorna intenções `patrol, hold, investigate, engage, withdraw` e justificativa categórica. Não utiliza IA generativa: é uma **máquina de decisão determinística** para primeiro teste do IPC.
2. `SteamModpack/LocalAI/test_ai_service.py`: casos de presença de zumbi, saúde, modo Combate Total, observação não visível, IDs duplicados, limites e HTTP.
3. `SteamModpack/Plugin/LocalAIStrategyClient.cs`: transporte C# assíncrono, timeout de 300 ms, respostas versionadas/validadas, expiração após 2 s, descarte de resultado, fallback silencioso.
4. `SteamModpack/Plugin/LocalAIHostPump.cs`: coleta de observações no thread do jogo, envio em lotes no máximo a cada 1 s e consumo único dos resultados. **Nenhum** movimento/tiro/dano é enviado pelo serviço: um adaptador real deve implementar `ILocalAIAgentSource.TryApplyValidatedIntent`.
5. `SteamModpack/Plugin/UnturnedBetterLdmPlugin.cs`: bootstrap Rocket/LDM para processo servidor. Ainda **não** está compilado em .dll e não controla NPC até que a camada de agentes seja portada e registrada pelo `RegisterAgentSource`.
6. Ferramenta de investigação no SDK experimental: menu `Unturned Better > Diagnostics > Export Loaded Building Colliders`, que exporta `Logs/BuildingAssetAudit.csv` (instâncias reais) e `Logs/BuildingPrefabAudit.csv` (fontes `Object`, `Clip`, `Nav`, componentes, layers, malhas e colisores). A ferramenta **não altera assets** e **não toca na lógica de movimento**.

## Como testar o serviço localhost agora

No terminal, dentro da raiz do repositório com **linha B** selecionada:

```powershell
py -3 SteamModpack/LocalAI/ai_service.py
```

Outro terminal:

```powershell
py -3 -m unittest discover -s SteamModpack/LocalAI -p "test_*.py" -v
Invoke-RestMethod http://127.0.0.1:18742/health
```

Teste um lote real sem Unity:

```powershell
$payload = @{
  version = 1
  sequence = 1
  mode = "normal"
  agents = @(@{
    npc_id = "npc_teste"
    observation = @{
      health_fraction = 0.8
      threat_visible = $true
      threat_has_aggro = $true
      threat_distance = 4.0
      gun_ready = $false
      visible_zombie_count = 1
    }
  })
} | ConvertTo-Json -Depth 6
Invoke-RestMethod -Uri http://127.0.0.1:18742/v1/decide -Method Post -Body $payload -ContentType application/json
```

Resultado esperado: intenção `withdraw`. **Esse resultado não prova navegação do NPC**; apenas o mecanismo de decisão sem engine.

## Como auditar de fato os assets das construções de Washington

1. Selecionar linha B no GitHub Desktop, dar Pull e abrir o projeto no Unity 2022.3.62f3.
2. Rodar em Play no mapa Washington; esperar o carregamento integral do nível.
3. Menu **Unturned Better > Diagnostics > Export Loaded Building Colliders** (somente com jogo em Play e mapa carregado).
4. Inspecionar dois CSV em `<raiz do projeto>/Logs/`. O primeiro representa o **colisor instanciado**; o segundo compara os três prefabs **fonte**.
5. Escolher uma casa específica onde o NPC fica preso e comparar visualmente os nomes/GUIDs, path `Block`, `Clip`, `Nav`, `Layer`, `Bounds`, malhas e aberturas da fachada. O `Nav` instanciado pode estar ausente no editor/cliente mesmo quando existe como prefab, porque o carregamento nativo varia entre Editor e servidor.
6. Só então mudar máscaras, geometria ou gateway. **Não desabilitar paredes** por aproximação.

A análise estática do U3 SDK mostrou que `LevelObject` carrega modelos de `ObjectAsset` para a instância e, de acordo com o contexto, pode criar `Nav`; `ObjectAsset` possui fontes separadas `clientModel` (Object), `legacyServerModel` (Clip) e `navGameObject` (Nav). A inspeção real das casas ainda depende de exportação do editor.

## Arquitetura-alvo Steam

```text
Unturned Steam + A* nativo (servidor proprietário/host local do usuário)
        │
        ├── Conteúdo Workshop (maps, Auto-32, itens, skins)
        │
        ├── Plugin servidor LDM/OpenMod (game-side actor adapter: PENDENTE)
        │     ├── captura sensorial autorizada, identidade, risco
        │     ├── navegação e colisão nativas / combate / dano
        │     ├── valida intenções e conserva autoridade
        │     └── fallback no próprio servidor se localhost falhar
        │
        └── HTTP loopback 127.0.0.1:18742
              └── LocalAI Python (estratégia em lotes, sem APIs Unity)
```

Em multiplayer, o localhost é o **computador do host de servidor**, não o dos clientes remotos. Um plugin de servidor não instala automaticamente o agente gráfico nos clientes. Portar visuais/animações poderá exigir módulo cliente, com distribuição separada e verificação de compatibilidade com anti-cheat.

### Vantagens reais e limites

- O serviço desacopla a tomada de decisões, distribui carga e permite versões/estratégias trocáveis; não corrige por si só o `Clip.prefab`, `Nav.prefab`, o `CharacterController` ou as passagens.
- A biblioteca A* proprietária permanece dentro da instalação licenciada do jogo. Não copiar DLLs/bundles proprietários para o repo.
- O adaptador precisa encontrar uma forma **suportada pela API do servidor** de criar e atualizar os NPCs. O código de `MobileNPC...` da linha A está integrado a classes do jogo modificadas; não pode ser simplesmente copiado para LDM e esperar que funcione.
- `SteamModpack/Plugin` é código-fonte de protótipo externo, **não** uma DLL pronta. Compilar contra as assemblies da versão Steam em uso e validar no servidor de teste antes de publicar.
- Modpack implica pelo menos duas distribuições: conteúdo customizado como Workshop/Assets e plugin de código como módulo DLL. Mods da Workshop não podem executar arbitrariamente o sidecar.
- O servidor local Python usa porta de loopback, não a expõe à internet; não usar `0.0.0.0` nem abrir firewall/NAT para ela.

## Próximos critérios de aceite, em ordem

- [ ] Exportar e analisar prefabs reais de pelo menos 3 casas problemáticas em Washington.
- [ ] Validar os testes Python (não executados neste ambiente).
- [ ] Compilar o bootstrap LDM para a versão atual do Unturned Steam e inicializar sem exceções.
- [ ] Portar um NPC simples para Steam com aparência, colisão, hitbox, morte e sincronização de rede.
- [ ] Validar uma rota por A* nativo para uma entrada/escada real (não presumir acesso aos grafos internos).
- [ ] Integrar sensor/atuador `ILocalAIAgentSource` com NPC portado e testar fallback sem localhost.
- [ ] Aplicar zumbis, facções, inventário, rádios e Combate Total em lotes incrementais.
- [ ] Avaliar uso de RAM/CPU num host de 8 GB e 10/20 NPCs antes de considerar a linha Steam substituta da A.

**Estado real:** branch criada e fontes gravados; nenhum teste Unity, compilação LDM, teste em jogo Steam ou execução Python foi confirmado até esta revisão.

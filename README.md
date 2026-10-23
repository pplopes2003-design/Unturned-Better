# U3 SDK

Source code for [Unturned](https://smartlydressedgames.com/unturned/), a free open-world zombie survival sandbox game.

## Getting Started

1. Download/clone this repository
2. Install [Unity Hub](https://unity.com/download) (required to install engine)
3. Install the [Unity 2022.3.62f3](https://unity.com/releases/editor/whats-new/2022.3.62f3) editor
4. *Optional*: if making code changes, select **Game development with Unity** + **.NET desktop development** in the Visual Studio installer
5. Ensure Steam is running and you have [Unturned](https://store.steampowered.com/app/304930/Unturned/) installed (large binary files and mods are loaded from here)
6. Open the project with the Unity editor
7. Open the `Assets/GameStartup.unity` scene
8. Click play!

## Resources

- [Frequently Asked Questions](https://docs.smartlydressedgames.com/en/stable/u3-sdk/faq.html)
- [Source Code Demo: Adding a Heat-Seeking Missile on YouTube](https://youtu.be/CqJnkcWfmEY)
- [Unturned's Modding Documentation](https://docs.smartlydressedgames.com/en/stable/)


## Unturned 2 — regra obrigatória de integração com o jogo base

**ANTES DE IMPLEMENTAR, MODIFICAR OU SUBSTITUIR qualquer mecânica de NPC, procurar e estudar o equivalente já existente no Unturned.** Esta regra vale para navegação, combate, armas, colisões, animações, inventário, zumbis, áudio, UI, persistência e rede. Não criar comportamento baseado somente em suposições.

Para cada intervenção:
1. Identificar as classes, prefabs, componentes e dados nativos realmente disponíveis **na versão de SDK utilizada**; localizar as chamadas e as precondições pertinentes.
2. Registrar o que o jogo base faz, o que foi de fato confirmado no SDK, o que é apenas hipótese e o que o SDK não inclui.
3. Reaproveitar os dados e os mecanismos nativos quando compatíveis; **não copiar componentes exclusivos de Player ou Zombie** para NPCs humanoides sem analisar suas dependências.
4. Quando o sistema original for limitado, estender com uma camada própria de decisão/rotas sem alterar regras físicas, permissões, hitboxes ou recursos inexistentes no jogo.
5. Verificar compatibilidade, comportamento e desempenho na dev build; não apresentar alteração no Git como prova de funcionamento.

### Referência específica: navegação dos zumbis

- A navegação dos zumbis se apoia nas áreas de navegação do mapa e, na versão comercial, no **A* Pathfinding Project** para desviar de obstáculos. O U3 SDK público **não redistribui** certas bibliotecas terceirizadas de pathfinding.
- Referência oficial: https://docs.smartlydressedgames.com/en/latest/u3-sdk/faq.html — explica tanto zumbis andando contra paredes quanto avisos de componentes `Pathfinding.NavmeshCut` ausentes. **Não interpretar esses avisos isoladamente como bug criado pelo nosso código.**
- Usar a geometria real do mapa, colisores, CharacterController, áreas de navegação e física como fonte da verdade. Uma abertura arquitetônica não equivale a uma instância de `InteractableDoor`; portas interativas só existem onde o prefab efetivamente as fornece.
- Ao perseguir um alvo atrás de uma parede ou dentro de uma construção, exigir um caminho navegável por passagens verificadas. Linha de visão direta não prova rota; caminho no mesmo nível de altura também não prova conectividade.
- Verificar passagem da cápsula nas quinas e nos vãos, progressão efetiva entre waypoints, rota alternativa e recuperação limitada contra travamentos. Não atravessar paredes, não desativar colisores, não teleportar para corrigir falhas.
- O NPC humanoide deve superar limitações observadas dos zumbis (quinas e entradas) por planejamento local e recuperação, **não imitar cegamente a perseguição direta do zumbi**.
- Sem a dependência A* devidamente instalada/licenciada, manter e testar explicitamente o sistema alternativo já existente; **não pressupor que classes ausentes de A* funcionam**.

### Referência específica: combate

- Usar recursos reais de `ItemGunAsset`, munição, attachments, modos de tiro, balística, hitboxes e clips de animação; respeitar capacidades do equipamento.
- Não introduzir mecânicas inexistentes (por exemplo, bloqueio/parry). Estímulo auditivo informa perigo possível, mas **não confirma atacante, rota para cobertura ou obrigação de agachar**.
- Alvo selecionado pelo cérebro é a referência única para percepção, mira e execução; validar visibilidade, linha de tiro e segurança antes de cada ataque.
- Separar a autoridade de dano/disparo da apresentação visual; pivô do modelo ou marcador `Barrel` não é automaticamente origem balística confirmada.

**Critério para a próxima build:** primeiro compilar sem erros, depois validar entrada/saída de edifícios, perseguição ao redor de quinas, reação a tiros próximos, seleção de alvos e ataques reais. Registrar separadamente erros de SDK/dependências, erros do nosso código e limitações conhecidas do jogo base.

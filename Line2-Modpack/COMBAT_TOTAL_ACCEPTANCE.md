# Linha 2 — Critérios do modo COMBATE TOTAL (Steam)

**Branch de implementação:** `line2/modpack-framework`. **Prioridade atual:** concluir e homologar somente o modo Combate Total antes de retomar Normal, economia e A-Life civil. **Estado:** integrações de código presentes; build e gameplay Steam reais ainda não homologados.

## Seleção e isolamento

- No singleplayer, com cheats habilitados e módulo instalado: `!u2 npc on`; `!u2 combat total`; `!u2 natural on`. Manualmente, `!u2 spawn hostile 1` ou qualquer role solicitada, que será hostil exclusivamente neste modo.
- O modo Total utiliza a chave persistente `<map>::Line2CombatTotal` (hash separado). O modo Normal/Passive continua com a chave original do mapa, sem migração destrutiva de inventário, ferimentos ou personagens.
- Alterar de/para Total: salvar a sessão antiga, desmontar os NPCs da sessão anterior, limpar grupos e recarregar APENAS a chave do novo modo; não apenas trocar a disposição de atores vivos.
- Restaurar uma pessoa do JSON: sem arma, rádio ou munição inicial adicional, preservando seu estado e estoque exatos.

## Combatentes

- Todos são hostis ao jogador. Grupos de até quatro membros, com identidade estável por grupo e Raiders versus Military para combate NPC↔NPC; ambas as facções perseguem jogadores hostis quando confirmados pelos sentidos.
- Patrulha de combate A* e decisões táticas, não construção, alimentação, comércio ou coleta civil. Permanecem: inventário real, sangramento, ferimento, recuperação com medicamentos possuídos, munição, dano, morte, loot de cadáver, rádio e coordenação.
- Kit de nascimento válido uma única vez: arma verificada pelo catálogo e calibre nativo, carregador já instalado, três carregadores reais de reserva, faca, itens médicos, uniforme com capacete/colete e eventual granada autêntica. A escolha de asset ausente falha ou usa o fallback verificado, sem itens fictícios.
- A proteção de vestimenta é anatômica e dependente da qualidade de peças realmente possuídas. Uma peça avariada protege menos; o servidor reduz sua qualidade quando a configuração do jogo permite. A aparência `HumanClothes` lê o mesmo inventário.
- O comportamento de retirada do combate Total usa o limiar crítico **9% de HP**, contra **30% no modo Normal**. Antes disso, coberturas validadas e fogo de resposta podem prevalecer, mas o NPC continua sujeito às decisões defensivas e aos ferimentos.

## Integração de cérebro, esquadrão e navegação — rodada aplicada

- Os rádios agora podem retransmitir **NPC rival ou zumbi avistado pessoalmente** por um integrante em Combate Total, além do relato original de jogador. Enviar exige alvo vivo/hostil, visão física atual, rádio realmente possuído e contato a até 35 m.
- A transmissão continua limitada à facção e ao **mesmo grupo vivo** (ambos portando rádio e até 90 m entre si), com setores de 20 m e intervalo de 9 s para contatos de combate. O receptor não recebe o `Transform` do inimigo, nenhuma linha de visão fictícia e nenhum direito de atirar.
- O receptor só aceita o setor se estiver livre para reagir, sem alvo pessoal ou retirada em curso, no mesmo nível de piso, com pista nova, e se a direção escolhida tiver **piso estático, cápsula desobstruída** e um destino confirmado. A navegação A* original ainda precisa conectar o trajeto.
- Corrigido o validador `Line2NpcInvestigationProbe`, que permanecia restrito a `RayMasks.GROUND2`: ele agora compartilha o amostrador nativo com múltiplas camadas de piso e escolhe a superfície correta no andar do NPC.
- O papel ativo do integrante agora modifica a tática de Combate Total: **líder e explorador** podem iniciar flanqueamento; **guarda e intendente** permanecem na linha de tiro e reabastecimento. A retirada, cobertura, ferimentos e tiros validados continuam independentes da função.
- `CombatTotalSquadTests` simula rejeição de pistas cegas, inimigos aliados, rádios ausentes, grupos diferentes, posição em outro andar, repetição prematura e responsabilidades táticas. A auditoria exige a ligação real às classes do módulo.

**Ainda não homologado:** comunicação, interação de portas e sucesso da movimentação sob A* em gameplay no Steam. A versão exata dos assemblies, modelos e perfis do jogo precisa ser usada na compilação final.

## Bateria Steam obrigatória (pendente)

1. **Seleção:** executar os comandos, verificar `NPC_COMBAT_TOTAL_READY` no log e NPCs sempre hostis mesmo quando o comando pede `friendly` ou `neutral`.
2. **Facções:** gerar pelo menos cinco indivíduos para que apareçam dois grupos; confirmar contato de combate entre Raiders e Military, mantendo mesmo-grupo sem fogo amigo.
3. **Armas:** contar estoque e estado de carregadores, testar semi/auto/rajada, qualidade, posicionamento, impacto em anatomia, tiros errados, supressão e recarga sem duplicação.
4. **Sobrevivência de combate:** ser alvejado, procurar cobertura real, flanquear, usar faca/medicamentos, consumir granada, morrer, soltar inventário e respeitar o atraso de reposição.
5. **Mapa:** tentar perseguição e navegação em casas Washington com escadas, portas, janelas, estruturas e obstáculos físicos reais; conferir logs de falha e CPU com vários NPCs e zumbis.
6. **Persistência:** gastar munição e degradar capacete/colete; sair e reentrar no Total e verificar valores idênticos. Voltar ao Normal e confirmar que seu save e seus NPCs antigos ficaram intactos.
7. **Compilação:** executar `dotnet build .\\ClientModule\\Unturned2.Line2.Probe.csproj -p:UnturnedDir="<pasta Steam do Unturned>"` usando DLLs da instalação realmente utilizada, depois instalar e testar o módulo em gameplay.

**Não confundir pipeline com homologação:** BrainTests, Python e auditoria do SDK podem passar mesmo que uma alteração falhe contra DLLs de outra versão, prefabs reais ou modos de jogo. O multiplayer também permanece fora do escopo de homologação desta primeira fase.

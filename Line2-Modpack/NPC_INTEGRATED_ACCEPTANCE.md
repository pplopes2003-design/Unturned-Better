# Linha 2 — NPCs: entrega integrada e bateria única de testes

**Política de desenvolvimento:** não solicitar ao usuário um teste para cada método, classe ou comportamento isolado. Só solicitar uma **bateria completa** quando o pacote tiver passado por verificações de compilação, testes automáticos e revisão de integração. Um gate técnico inicial (compilar e carregar o módulo C# no Steam) ainda é obrigatório: sem ele, não há como validar a API efetiva do jogo.

## Plataforma / limites

- Windows 11, Unturned Steam original, sem BattlEye, Washington e mapas Steam Workshop.
- Linha 1 congelada; não editar `main` nem recompilar a Unity standalone.
- Mods e IA modular com controle sob autoridade local/sessão host.
- População de zumbis **igual ao jogo padrão**; hordas reagrupam orçamento existente.
- Sem controle real por IA até provar existência, propriedade e estado de cada entidade.
- Segurança de desempenho: limites de população e trabalhos por frame, consultas de física limitadas, sem refletir assemblies por frame, sem HTTP síncrono na thread Unity.

## Escopo mínimo da primeira versão jogável de NPCs

1. **Spawn e identidade**: comandos do chat/console, spawn neutro/amigável/hostil, skins e nomes, ID estável, despawn e política de população; spawns coordenados e sem sobreposição.
2. **Movimentação**: chão, colisão, animações, detecção de portas, navegação indoor/outdoor, escape de obstáculos, perseguição e busca do último local conhecido; janela de teste para evitar oscilações e travamentos nas quinas.
3. **Percepção**: audição (tiros, zumbis), visão e checagem de oclusão, reação a dano, prioridade ao jogador visível dentro de 20m quando aplicável; sem onisciência.
4. **Combate**: corpo a corpo animado, armas de fogo com mira e dispersão humanizadas, buscar cobertura e recuar de maneira coerente, munição, reload, dano e morte. Ataque entre NPCs e contra zumbis sujeito a facções.
5. **Interações**: diálogos com opções visíveis e saída pelo Esc, comércio básico, recrutamento/aliança quando apropriado, sem esconder HUD.
6. **Facções e vida persistente**: relações, grupos, patrulha, base com modelos fixos, loot coerente, fome/sede em regime limitado, save/load, morte definitiva do indivíduo, reposição após intervalo sem recriar memória indevida.
7. **Hordas e zumbis**: grupos dentro do limite vanilla; perseguição entre regiões com rota válida e regras de retorno/desistência, busca ativa e ausência de loops de rotação.
8. **Observabilidade/estabilidade**: erros reportados em log, contadores por etapa, orçamento CPU, FPS e benchmark pequeno/médio com comando para desativar NPCs; nunca causar efeitos em servidores públicos.

O escopo acima representa **critérios de aceitação**, não funcionalidades já prontas. Não habilitar todas de uma vez sem a infraestrutura de entidade/navegação.

## Gating técnico interno antes do teste único

- [ ] Módulo C# compila na versão Steam atual e carrega sem quebrar Unity.
- [ ] F10 e F12 identificam entidade e componentes reais (se necessário em um mapa com NPC).
- [ ] Adapter do jogo implementado e validado com autorização de sessão.
- [ ] IA externa comunica ações por tick/TTL, com fallback local e sem executar replay.
- [ ] Spawn e despawn funcionam sem vazamentos, travamentos ou sobreposição.
- [ ] Navegação passa por portas, retornos, mudanças de região e obstáculos em múltiplas edificações.
- [ ] Combate, morte, diálogo, comércio e persistência passam em testes internos.
- [ ] Hordas respeitam cap vanilla, e perseguição cross-region passa casos de retorno/desistência.
- [ ] Scripts Python e compilação C# passam; logs e instruções de rollback documentados.

## Roteiro único de aceitação do usuário (quando os gates passarem)

- Inicializar Washington: spawn misto, identificar nome/skin e limite populacional.
- Fazer NPC seguir ao redor de casa, entrar/sair, porta fechada/aberta, perseguir na esquina e recuperar rota ao travar.
- Iniciar combate de curta/longa distância; verificar dano/morte, mira, recuo, cobertura e comportamento ao receber tiro de longe.
- Atrair zumbi para NPC e NPC hostil para NPC amigável; conferir percepção e facções.
- Dialogar, comprar e recrutar; verificar opções e fechar com Esc.
- Testar patrulha, coleta/loot e retorno à base; salvar, sair e carregar novamente; confirmar identidade de sobreviventes.
- Testar morte de membro, preservação do grupo, repovoamento retardado e fim de grupo após perda total.
- Atrair zumbi além do limite de região e verificar perseguição com rota válida e desistência; observar hordas sem aumento do número total.
- Medir FPS/stutters com NPCs desabilitados e ativos, analisar logs e comparar performance.

## Etapa atual (2026-11-06)

A Auto 32 foi confirmada funcional no Steam pelo usuário. O Modpack Manager GUI também passou na instalação local e leitura da Workshop. C# ainda não compilado por falta de SDK na máquina do usuário, então NPCs móveis/combate e hordas **não estão prontos para a bateria final**. O script `scripts/build_diagnostics_windows.cmd` permite executar a etapa de build/instalação sem política de scripts PowerShell. Não apresentar F10/F12 como NPCs jogáveis.

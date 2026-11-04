# Hospedagem integrada para amigos — estágio LAN

## Implementado nesta branch

- Botão **Hospedar amigos (LAN)** no menu `Jogar`, sem mexer na `main`.
- Configuração em tela: diretório U3DS, mapa, nome, senha, quantidade de jogadores e porta.
- Inicialização explícita (botão do usuário) do executável **Unturned Dedicated Server (U3DS)** previamente instalado na máquina, com `-batchmode -nographics +LanServer/UnturnedBetterFriends`.
- Escrita do `Servers/UnturnedBetterFriends/Server/Commands.dat` antes da inicialização.
- Botão para consultar e conectar a `127.0.0.1:<porta>` pelo fluxo *nativo* de matchmaking do Unturned; não faz conexão antecipada automática enquanto o mapa carrega.
- Modo LAN somente; sem GSLT, sem alegar conexão pela internet ou Steam relay.

## Condições reais

1. Instalar **Unturned Dedicated Server**, app Steam 1110390 (Steam > Biblioteca > Ferramentas). O Launcher não faz download nem instalação oculta.
2. Indicar a pasta U3DS que contém `Unturned.exe` e `Unturned_Data`. O menu tenta localizar uma instalação irmã da pasta do jogo automaticamente.
3. Executar a *mesma versão do conteúdo de servidor* (assembly de NPCs e suas dependências) no processo U3DS. **Este passo ainda NÃO foi implementado nem validado**: a branch contém apenas o início do fluxo de hospedagem local, e o U3DS vanilla não conhece as classes de MobileNPC.
4. O cliente e o U3DS devem possuir mapa e arquivos necessários, e as versões de assets precisam coincidir.
5. Clicar `Iniciar servidor LAN`; conferir console e carregar mapa. Depois clicar `Conectar ao meu servidor`.
6. Para amigos na **mesma rede**, compartilhar endereço IP local da máquina e porta. Para amigos fora de casa a conectividade é uma etapa futura; NÃO prometer que uma sessão LAN estará acessível externamente.

## Limitações atuais (pendentes)

- [ ] Compilar e testar o menu no Unity e verificar lançamento do U3DS real.
- [ ] Testar conexão ao servidor dedicado em localhost e a partir de outro computador da LAN.
- [ ] Empacotar e carregar os binários customizados compatíveis no servidor U3DS. Validar spawn, física, IA, persistência, tiros, granadas, rádio e estado dos NPCs com dois clientes.
- [ ] Fazer shutdown gracioso com `save` + `shutdown` no console U3DS; hoje o processo tem console próprio, não há botão de forçar término ou encerramento do processo.
- [ ] Distinguir erro/queda do processo e disponibilidade efetiva do servidor. `IsRunning` verifica somente processo vivo, não mapa carregado nem porta aberta.
- [ ] Criar uma experiência de convite pela internet usando conexão oficial segura (Steam Networking / relay) ou opção documentada de roteador; testar NAT/firewall e GSLT se usar modo Internet.
- [ ] UI para selecionar mapas publicados na Workshop já instalados, não aceitar dependências ausentes silenciosamente.
- [ ] Considerar orçamento de memória/CPU: cliente Unity + U3DS + IA de NPCs na mesma máquina.

## Steam Workshop — pendência solicitada

A utilização de mapas, itens e dependências de terceiros ficará em **integração por Workshop**, sem copiar/modificar/publicar os arquivos dos autores no repositório. O uso privado entre amigos não torna automaticamente qualquer redistribuição de arquivos permitida.

- [ ] Criar gerenciador de IDs publicados da Steam Workshop (somente IDs numéricos válidos).
- [ ] Escrever `Servers/<ServerId>/WorkshopDownloadConfig.json` usando o formato oficial `File_IDs` e preservar opções/valores já presentes.
- [ ] Configurar o nome do mapa correspondente **depois** de o servidor baixar Workshop e dependências.
- [ ] Garantir que o cliente faça download/validação do mesmo conteúdo via fluxo nativo, sem enviar bundles desconhecidos por rede própria.
- [ ] Evitar colisões entre IDs dos nossos itens 59010/59011 e os IDs de itens da comunidade.
- [ ] Confirmar compatibilidade dos NPCs com colisores e navegação dos mapas de terceiros, sem presumir portais ou NavMesh corretos.

Referências: documentação Unturned Dedicated Server e instruções da Steam Workshop para servidores. A simples presença de arquivos neste repositório não valida operação multiplayer.

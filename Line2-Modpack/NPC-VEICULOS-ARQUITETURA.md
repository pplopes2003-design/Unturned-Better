# NPCs e veículos — proposta técnica para a Linha 2

Este documento NÃO declara direção autônoma implementada. A branch passa a ter um serviço de **descoberta segura** de veículos Steam reais, sem alterar assentos, NetworkId, Rigidbody, combustível ou propriedade.

## APIs reais verificadas no código-base U3-SDK

- `VehicleManager.getVehiclesInRadius(Vector3 center, float sqrRadius, List<InteractableVehicle> result)`: enumera veículos existentes, não cria automóveis falsos.
- `InteractableVehicle.GetSeatByIndex(0)`: retorna a posição real do banco do motorista e, se ocupado por jogador, o `Passenger.player`.
- `vehicle.isDriven`, `vehicle.isLocked`, `vehicle.fuel`, `vehicle.isExploded` e `vehicle.asset.engine` oferecem filtros preliminares.
- `VehicleManager.ServerForcePassengerIntoVehicle(Player player, InteractableVehicle vehicle)` exige um **Player real**. Não recebe `PrototypeActor`.
- `InteractableVehicle.GetDriverClient()`, `GetDriverPlayer()`, `checkDriver(CSteamID)` evidenciam que direção e estado dos assentos são integrados à rede dos players.

**Nunca** falsificar `SteamPlayer`, usar o SteamID do usuário para o NPC, adicionar o NPC em `passengers[0]` sem o servidor saber, nem mover veículos diretamente por `transform.position`/teleporte a cada frame.

## Arquitetura de implementação recomendada

1. **Busca** (já adicionada, não acionada automaticamente): `Line2NpcVehicleDiscovery.TryFindParkedCar` retorna o veículo real e uma posição aproximada fora da carroceria.
2. **Reserva** (a implementar): registro servidor-side para impedir que vários NPCs reivindiquem o mesmo carro; revalidar se o player entrar, bloquear ou destruir.
3. **Aproximação a pé** (a implementar): o cérebro solicita um caminho usando o **A* original da Steam** até uma posição exterior comprovadamente acessível. O NPC continua usando `CharacterController`.
4. **Embarque** (a implementar): adaptador de assento para humanoides NPC, separado da lista de jogadores; animação visual com reparent somente após reserva e verificação da distância, colisão e permissão.
5. **Direção** (a implementar): controle de aceleração/freio/direção por uma interface de pilotagem conectada à **simulação nativa do `InteractableVehicle`**. Rotas de automóvel seguem estradas/waypoints apropriados e não o grafo pedestre do A*; o A* pedestre continua responsável pelo trajeto a pé. Requer verificar o proprietário de simulação e os métodos de rede no Steam atual.
6. **Desembarque** (a implementar): escolher ponto seguro com clearance, frear, soltar o assento e restaurar `CharacterController`; não eliminar ou duplicar o veículo.
7. **Persistência/multiplayer** (fase posterior): conservar referência por ID de instância somente durante sessão; reconciliar veículo carregado no save, posição, ocupantes, danos e autoridade de rede.

## Fluxo de decisão sugerido

```text
Rotina civil ou missão de grupo
 └─ Distância de deslocamento elevada?
     ├─ NÃO → usar A* pedestre normal
     └─ SIM → veículo real disponível?
         ├─ NÃO → A* pedestre
         └─ SIM → escolher veículo + reservar
             └─ A* pedestre até acesso exterior
                 └─ embarque validado
                     └─ controlador de direção nativo
                         └─ parar e desembarcar em solo seguro
```

Combate: a decisão de fugir em veículo deve ser excepcional, baseada em ameaça realmente conhecida e uma rota segura, nunca em informações ocultas do player. Evitar uso de veículos como atravessadores de paredes ou carros que teletransportam.

## Exemplo de uso futuro (somente busca; não embarca)

```csharp
Line2NpcVehicleDiscovery finder =
    npc.GetComponent<Line2NpcVehicleDiscovery>();
InteractableVehicle car;
Vector3 outside;
if (finder != null &&
    finder.TryFindParkedCar(npc.transform.position, out car, out outside))
{
    // Enviar outside ao planejador A* pedestre e só avançar
    // após uma rota conectada e teste de segurança do ponto.
    // NÃO chamar ServerForcePassengerIntoVehicle(npc,...):
    // PrototypeActor não é Player.
}
```

O protótipo anterior de NPCs não implementa direção nativa; esta divisão de responsabilidades evita inventar compatibilidade com o jogo original.

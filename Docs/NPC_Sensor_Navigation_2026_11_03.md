# NPC sensor-only navigation: 2026-11-03

This update requires no manually measured doorway dimensions. The NPC route provider derives its dimensions from the live Unity CharacterController. Sensors retain all world collision layers and exclude the owner's colliders explicitly; this avoids accidentally hiding map walls sharing the same layer as the NPC.

The individual sensor memory now uses 3D keys so upstairs floors do not overwrite downstairs observations. It samples every 0.75m and caps memory at 320 cells. Reused doorway routes are still physically checked with A*, but only two likely crossings are tried with reused waypoint buffers.

Long distance routing already has an independent coarse global planner (MobileNPCGlobalRoutePlanner, 21x21 cells, 3m spacing) and the local HybridNPCGridPlanner for fine detail. The physical actuator remains CharacterController.Move. Nothing in this iteration modifies the zombie navigation system. This is not yet a fully generated polygonal navigation mesh.

Validation needed: compile on Unity 2022.3.62f3; cross an actual Washington building doorway; inspect threshold logs and physical collision; test stairs and 10-30 NPC performance. No Unity build or gameplay verification has been run here.

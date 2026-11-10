# Linha 2 — memória de tiro dos NPCs

Status: integrated into `Line2NPCRangedExecutor`, survivor save data and deterministic tests. Not yet validated in Steam gameplay.

The sniper has **two independent memories**:

1. **Individual lifetime skill** (`Line2NpcSniperExperience`): 12 bounded records by actual `ItemGunAsset.id` and three range bands (<35m, 35–75m, >=75m). Every committed precision shot contributes practice; actual impacts on the intended target count as strikes. Practice improves angular dispersion by **at most 15%**, so it never makes an NPC perfectly accurate. Actual near-miss impacts can also teach a small persistent ballistic calibration (at most ±0.6m, only half applied). No imaginary bullet feedback or hidden target coordinates are allowed. It survives save/load for the *same living NPC identity* through `Line2NpcPersistence.ActorRecord.sniperExperience` and is destroyed when that NPC dies.
2. **Short-term shot correction** (`Line2NpcShotCorrectionMemory`): per current owned gun instance and current verified target only. First physically simulated impacts within six metres may correct up to ±1.25m for up to 35 seconds. Retargeting, switching gun or extended contact loss clears only this temporary bias; lifetime experience remains intact.

Regular randomized dispersion, physical collision, gravity, limited ammunition, magazine reload, sight confirmation, movement, injury and suppressive fire continue to apply. Successful body hits must not be interpreted as sight-alignment errors.

This is a modest, evidence-driven skill accumulation — **not** a stationary aimbot or a global AI team bonus. Initial skill is zero for a replacement NPC. Saved records are validated before accepting map snapshots.

Test with `dotnet run --project Line2-Modpack/BrainTests/BrainTests.csproj -c Release` on a machine with .NET 8. Windows Steam mod-module build and gameplay tests remain required.

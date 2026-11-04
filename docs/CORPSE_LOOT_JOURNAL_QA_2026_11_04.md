# Corpse loot: persistence, retry and QA (2026-11-04)

Branch: `feature/release-candidate-cycle-2026-11-04`.

## Code implemented

- `MobileNPCDeathLoot` still asks the native `ItemManager` to insert ACTUALLY OWNED items. On a rejected native insertion it transfers the item to `MobileNPCCorpseLootJournal` only if the durable JSON commit succeeds. Failed journal commits roll the item back to the corpse inventory.
- The server journal is stored in `Application.persistentDataPath/MobileNPCs/{serverId}_{map}[__CombatOnly].corpse-loot.json`. It uses the same map-key derivation as `MobileNPCPersistence` and has a separate file from living NPCs; the client never owns or writes the journal.
- At most 512 pending slots per world, with up to four attempts every eight seconds. Unknown/deleted/Pro-only assets are retained without spawning. Missing Workshop content can be restored before reattempting.
- For replay, the journal writes the removal BEFORE invoking the native world drop, to avoid duplicated items if a crash occurs after a native insertion. **Limitation:** failure/power-loss between journal removal and native spawn can permanently lose that entry. The native world's save and this JSON are not a single atomic transaction.
- The journal attempts atomic `File.Replace` when updating an existing file, and retains the previous backup. On read corruption with no valid primary/backup, it blocks writes and retries rather than overwriting evidence.
- On normal shutdown, pending rejected items were already committed and need no corpse object to survive.

## Validation required in Unity, not yet performed

1. Start dedicated build on Washington, spawn a hostile NPC with a known gun, ammo and medical item, kill it and verify native world drops have correct item IDs, quantity, quality and state.
2. Force ItemManager failure during drop (dev-only injection or isolated invalid placement), confirm a durable `.corpse-loot.json` entry, remove corpse, restart dedicated and restore normal ground placement; verify exactly one final drop.
3. Repeat with a missing Workshop item and reinstall Workshop content. Ensure the journal retains and later recovers it, without fabricating an unknown asset.
4. Force a locked/read-only JSON directory, confirm error log and item rollback into corpse; **do not let corpse despawn** while filesystem failure is unresolved.
5. Kill NPC during network session, confirm second client sees the legitimate drop via vanilla item replication; no client-side loot authority.
6. Test separate maps, game mode CombatOnly, two save/server IDs, corrupt primary JSON with valid backup, corrupt both, and journal full (512 entries).
7. Measure disk I/O and native retry rate with 20 NPC deaths in a few seconds. No per-frame file writes should occur after the initial failed entries.

## Release blockers

- No Unity compilation/build test performed in this session.
- Filesystem errors combined with corpse despawn can still lose items, because the dead inventory is not part of the live NPC snapshot.
- At-most-once replay favors duplicate prevention over strict zero-loss during the journal/native-world crash window; eliminating both requires a shared transactional persistence protocol and native world-drop acknowledgement IDs.
- Whether the native item manager persists the drops after clean shutdown must be verified.

using System;
using System.Collections.Generic;
using Unturned2.Line2;
using SDG.Unturned;

internal static class PersistenceTests
{
    internal static bool Run()
    {
        string map = "line2-brain-tests-" + Guid.NewGuid().ToString("N");
        Level.info.name = map;
        var before = new List<Line2NpcPersistence.ActorRecord>();
        string reason;
        if (!Line2NpcPersistence.TryRead(map, out before, out reason) ||
            before.Count != 0 || reason != "new_map")
            return false;

        var survivor = new Line2NpcPersistence.ActorRecord
        {
            identity = "survivor-one", role = "friendly", displayName = "Morgan",
            groupId = 7, health = 72.5f, x = 120f, y = 14f, z = -3f,
            homeX = 118f, homeY = 14f, homeZ = -1f,
            items = new[] { new Line2NpcPersistence.ItemRecord
            {
                id = 32610, amount = 1, quality = 95, state = new byte[] { 1, 2, 3 }
            } }
        };
        if (!Line2NpcPersistence.TryWrite(map,
            new List<Line2NpcPersistence.ActorRecord> { survivor }, out reason))
            return false;
        List<Line2NpcPersistence.ActorRecord> loaded;
        if (!Line2NpcPersistence.TryRead(map, out loaded, out reason) ||
            loaded.Count != 1 || loaded[0].identity != survivor.identity ||
            loaded[0].groupId != 7 || Math.Abs(loaded[0].health - 72.5f) > 0.01f ||
            loaded[0].items.Length != 1 ||
            loaded[0].items[0].state.Length != 3 ||
            loaded[0].items[0].state[2] != 3)
            return false;

        // Delayed respawn tickets survive a save/load without reviving
        // the previous, now-dead actor's identity.
        var replacement = new Line2NpcPersistence.ReplacementRecord
        {
            role = "hostile", formerGroupId = 12,
            readyAtUtcTicks = DateTime.UtcNow.AddMinutes(3).Ticks
        };
        if (!Line2NpcPersistence.TryWrite(map,
            new List<Line2NpcPersistence.ActorRecord> { survivor },
            new List<Line2NpcPersistence.ReplacementRecord> { replacement }, out reason))
            return false;
        List<Line2NpcPersistence.ReplacementRecord> pending;
        if (!Line2NpcPersistence.TryRead(map, out loaded, out pending, out reason) ||
            pending.Count != 1 || pending[0].role != "hostile" ||
            pending[0].formerGroupId != 12 ||
            pending[0].readyAtUtcTicks != replacement.readyAtUtcTicks)
            return false;

        // Pending tasks with impossible dates cannot overwrite the last good save.
        replacement.readyAtUtcTicks = -10;
        if (Line2NpcPersistence.TryWrite(map,
            new List<Line2NpcPersistence.ActorRecord> { survivor },
            new List<Line2NpcPersistence.ReplacementRecord> { replacement },
            out reason))
            return false;
        if (!Line2NpcPersistence.TryRead(map, out loaded, out pending, out reason) ||
            pending.Count != 1)
            return false;

        // Invalid snapshots must be rejected BEFORE modifying the good file.
        if (Line2NpcPersistence.TryWrite(map,
            new List<Line2NpcPersistence.ActorRecord> { survivor, survivor },
            out reason))
            return false;
        if (!Line2NpcPersistence.TryRead(map, out loaded, out reason) ||
            loaded.Count != 1 || loaded[0].identity != survivor.identity)
            return false;

        // Dead or damaged records cannot overwrite valid living NPCs.
        survivor.health = -1f;
        if (Line2NpcPersistence.TryWrite(map,
            new List<Line2NpcPersistence.ActorRecord> { survivor }, out reason))
            return false;
        if (!Line2NpcPersistence.TryRead(map, out loaded, out reason) ||
            loaded.Count != 1 || loaded[0].health != 72.5f)
            return false;
        Console.WriteLine("NPC_PERSISTENCE_POLICY_OK: map key, roundtrip, equipment state, duplicate IDs, invalid health.");
        return true;
    }
}

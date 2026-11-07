using System;
using System.Collections.Generic;
using Unturned2.Line2;
using UnityEngine;
using SDG.Unturned;

internal static class SettlementLedgerTests
{
    internal static bool Run()
    {
        string previous = Level.info.name;
        string map = "line2-ledger-test-" + Guid.NewGuid().ToString("N");
        try
        {
            Level.info.name = map;
            Line2NpcSettlementLedger.Restore(map,
                new List<Line2NpcPersistence.SettlementRecord>());
            var site = new Vector3(44f, 3f, -12f);
            if (!Line2NpcSettlementLedger.TryClaim(12, "friendly", site))
                return false;
            if (Line2NpcSettlementLedger.TryClaim(12, "hostile", site))
                return false; // Faction cannot take over another base ID.
            Vector3 actual;
            int pieces;
            if (!Line2NpcSettlementLedger.TryGet(12, "friendly",
                out actual, out pieces) || pieces != 0)
                return false;
            if (!Line2NpcSettlementLedger.CommitPlaced(12, "friendly",
                363, new Vector3(47f, 3f, -12f), 90f))
                return false;
            if (Line2NpcSettlementLedger.CommitPlaced(12, "friendly",
                363, new Vector3(47.1f, 3f, -12f), 90f))
                return false; // Identical physical footprint isn't a new piece.

            var checkpoint = Line2NpcSettlementLedger.Capture();
            if (checkpoint.Count != 1 ||
                checkpoint[0].placed.Length != 1 ||
                checkpoint[0].placed[0].assetId != 363)
                return false;
            checkpoint[0].placed[0].assetId = 999;
            var secondCopy = Line2NpcSettlementLedger.Capture();
            if (secondCopy[0].placed[0].assetId != 363)
                return false; // A caller cannot mutate the live ledger.

            Line2NpcSettlementLedger.ResetSession();
            if (Line2NpcSettlementLedger.TryGet(12, "friendly",
                out actual, out pieces))
                return false;
            Line2NpcSettlementLedger.Restore(map, secondCopy);
            if (!Line2NpcSettlementLedger.TryGet(12, "friendly",
                out actual, out pieces) || pieces != 1)
                return false;
            if (!Line2NpcSettlementLedger.CommitPlaced(12, "friendly",
                363, new Vector3(41f, 3f, -12f), 270f))
                return false;
            if (!Line2NpcSettlementLedger.CommitPlaced(12, "friendly",
                363, new Vector3(44f, 3f, -9f), 0f))
                return false;
            if (!Line2NpcSettlementLedger.CommitPlaced(12, "friendly",
                363, new Vector3(44f, 3f, -15f), 180f))
                return false;
            if (Line2NpcSettlementLedger.CommitPlaced(12, "friendly",
                363, new Vector3(44f, 3f, -8f), 0f))
                return false; // Four-piece cap, no unbounded construction.

            Level.info.name = "some-other-map";
            if (Line2NpcSettlementLedger.TryClaim(50, "hostile", site))
                return false; // No cross-map base ownership leak.
            Console.WriteLine("NPC_SETTLEMENT_LEDGER_OK: immutable checkpoints, group/faction isolation, world map isolation, no duplicate footprints, four-piece cap.");
            return true;
        }
        finally
        {
            Line2NpcSettlementLedger.ResetSession();
            Level.info.name = previous;
        }
    }
}

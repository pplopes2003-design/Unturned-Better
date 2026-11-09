using System;
using Unturned2.Line2;

internal static class EncounterPacingTests
{
    internal static bool Run()
    {
        Line2NpcEncounterPacing.ResetSession();
        if (!Line2NpcEncounterPacing.TryLease(7, 101, 1f, true) ||
            !Line2NpcEncounterPacing.TryLease(7, 102, 1f, true) ||
            Line2NpcEncounterPacing.TryLease(7, 103, 1f, true))
            return false; // Never grant 3 speculative slots in same group.
        if (!Line2NpcEncounterPacing.TryLease(7, 101, 2f, true))
            return false; // Existing owner renews its own expiring lease.
        if (!Line2NpcEncounterPacing.TryLease(9, 104, 2f, true))
            return false; // Another group isn't penalized for this search.
        if (!Line2NpcEncounterPacing.TryLease(0, 201, 2f, true) ||
            !Line2NpcEncounterPacing.TryLease(0, 202, 2f, true))
            return false; // Solitary actors have separate capacity.
        if (Line2NpcEncounterPacing.TryLease(7, 103, 2f, false))
            return false; // No optional lease without real investigation.
        Line2NpcEncounterPacing.Release(102);
        if (!Line2NpcEncounterPacing.TryLease(7, 103, 2f, true))
            return false; // Finished NPC returns its search allowance.
        if (!Line2NpcEncounterPacing.TryLease(7, 105, 12f, true))
            return false; // Expired leases may be replaced.
        if (Line2NpcEncounterPacing.TryLease(7, 0, 12f, true) ||
            Line2NpcEncounterPacing.TryLease(7, 106, float.NaN, true))
            return false;
        // Encounter director may reduce bonus search concurrency
        // while pressure is high, without inventing enemy knowledge.
        Line2NpcEncounterPacing.ResetSession();
        Line2NpcEncounterPacing.Configure(1, 0f);
        if (!Line2NpcEncounterPacing.TryLease(5, 801, 1f, true) ||
            Line2NpcEncounterPacing.TryLease(5, 802, 1f, true))
            return false;
        Line2NpcEncounterPacing.Configure(2, 12f);
        if (Line2NpcEncounterPacing.TryLease(8, 803, 7f, true) ||
            !Line2NpcEncounterPacing.TryLease(8, 803, 13f, true))
            return false;
        Line2NpcEncounterPacing.ResetSession();
        if (!Line2NpcEncounterPacing.TryLease(7, 110, 0f, true))
            return false; // Reloading a map never inherits old reservations.
        Console.WriteLine("NPC_ENCOUNTER_PACING_OK: two optional searchers per group, expiry, release, no GPS or cross-map leases.");
        return true;
    }
}

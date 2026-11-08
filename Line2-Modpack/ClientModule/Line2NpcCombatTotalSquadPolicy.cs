namespace Unturned2.Line2
{
    /// <summary>
    /// One-hop, evidence-bounded Combat Total squad contact policy.
    /// A squad radio reports only a rough sector of a personally visible
    /// rival or zombie. Receivers may investigate a physically validated
    /// waypoint, but NEVER acquire LOS, a target Transform, or firing rights.
    /// Normal mode and unrelated factions do not use this channel.
    /// </summary>
    internal static class Line2NpcCombatTotalSquadPolicy
    {
        internal const float MaxContactRange = 35f;
        internal const float MaxRadioRange = 90f;
        internal const float MinimumReceiverDistance = 14f;
        internal const float ReportCooldown = 8f;

        internal static bool CanBroadcast(bool combatTotal,
            bool firsthandSight, bool livingHostileTarget,
            bool senderHasRadio, float contactDistance)
        {
            return combatTotal && firsthandSight && livingHostileTarget &&
                senderHasRadio && Valid(contactDistance) &&
                contactDistance <= MaxContactRange;
        }

        internal static bool CanInvestigate(bool combatTotal,
            bool sameLivingGroupAndFaction, bool bothHaveRadios,
            bool hasPersonalEnemy, bool underFireOrBusy,
            float separationFromSender, float distanceToSector,
            float verticalDifference, float observedAt, float lastAcceptedAt)
        {
            return combatTotal && sameLivingGroupAndFaction &&
                bothHaveRadios && !hasPersonalEnemy &&
                !underFireOrBusy &&
                Valid(separationFromSender) &&
                separationFromSender <= MaxRadioRange &&
                Valid(distanceToSector) &&
                distanceToSector >= MinimumReceiverDistance &&
                distanceToSector <= MaxRadioRange &&
                Valid(verticalDifference) && verticalDifference <= 1.25f &&
                Valid(observedAt) &&
                (lastAcceptedAt < 0f ||
                 Valid(lastAcceptedAt) &&
                 observedAt >= lastAcceptedAt + ReportCooldown);
        }

        private static bool Valid(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                value >= 0f;
        }
    }
}

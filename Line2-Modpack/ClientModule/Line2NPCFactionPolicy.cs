namespace Unturned2.Line2
{
    /// <summary>
    /// Exact faction relationship matrix from original Line 1
    /// MobileNPCFaction.GetRelationship, without modified engine dependencies.
    /// Keep values stable for future persistence.
    /// </summary>
    internal static class Line2NPCFactionPolicy
    {
        internal enum Id
        {
            Unaffiliated = 0,
            Survivors = 1,
            Raiders = 2,
            Military = 3,
            Scavengers = 4,
            Traders = 5
        }

        internal enum Relationship { Allied, Neutral, Hostile }

        internal static Id DefaultForRole(string role)
        {
            return role == "hostile" ? Id.Raiders :
                   role == "friendly" ? Id.Traders : Id.Scavengers;
        }

        internal static Relationship GetRelationship(Id first, Id second)
        {
            if (first == second) return Relationship.Allied;
            if (first == Id.Unaffiliated || second == Id.Unaffiliated)
                return Relationship.Neutral;
            if ((first == Id.Traders && second == Id.Raiders) ||
                (first == Id.Raiders && second == Id.Traders))
                return Relationship.Hostile;
            if ((first == Id.Raiders &&
                    (second == Id.Survivors || second == Id.Military)) ||
                (second == Id.Raiders &&
                    (first == Id.Survivors || first == Id.Military)))
                return Relationship.Hostile;
            // Military/Survivor and Scavenger conflicts are NEUTRAL until
            // real verified aggression, not generalized shoot-on-sight.
            return Relationship.Neutral;
        }

        internal static bool AreHostile(Id a, Id b)
        {
            return GetRelationship(a, b) == Relationship.Hostile;
        }
    }
}

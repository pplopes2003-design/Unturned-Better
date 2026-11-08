namespace Unturned2.Line2
{
    /// <summary>
    /// Pure alignment gate extracted from the combat priorities of Line 1's
    /// MobileNPCHumanoidDisposition and MobileNPCHumanoidPerception.
    /// No guessed faction memberships: the Steam prototype presently has
    /// only three explicit roles, not full persistent faction identities.
    /// </summary>
    internal static class Line2NPCCombatRules
    {
        /// <summary>Verified forward-facing visual cone. Geometry is checked
        /// separately by the local server physics raycast. A score is the
        /// normalized flat forward/target dot product, not remote AI data.</summary>
        internal static bool WithinVisualArc(float forwardDot)
        {
            return !float.IsNaN(forwardDot) && !float.IsInfinity(forwardDot) &&
                forwardDot >= 0.15f && forwardDot <= 1f;
        }

        internal static bool CanEngagePlayer(string role, string mode,
                                             bool verifiedVisible, float sqrDistance)
        {
            return role == "hostile" && mode != "passive" &&
                verifiedVisible && sqrDistance >= 0f && sqrDistance <= 400f;
        }

        internal static bool AreOpposed(string role, string otherRole, string mode)
        {
            if (mode == "passive") return false;
            return role == "hostile" && otherRole == "friendly" ||
                   role == "friendly" && otherRole == "hostile";
        }

        internal static bool CanEngageZombie(string role, string mode)
        {
            return mode != "passive" &&
                (role == "hostile" || role == "friendly" || role == "neutral");
        }

        internal static bool MayCommitHit(string role, string mode, bool alive,
                                          bool serverAuthorized, bool witnessedContact)
        {
            return alive && serverAuthorized && witnessedContact &&
                mode != "passive" &&
                (role == "hostile" || role == "friendly" || role == "neutral");
        }
    }
}

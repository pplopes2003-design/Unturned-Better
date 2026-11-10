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
            // 120-degree horizontal human-like visual cone (60 degrees
            // either side). Previously dot >= 0.15 allowed ~163 degrees,
            // effectively sensing targets far outside the NPC's gaze.
            return !float.IsNaN(forwardDot) && !float.IsInfinity(forwardDot) &&
                forwardDot >= 0.5f && forwardDot <= 1f;
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

        /// <summary>Native weak melee impact scaled by the quality of a real
        /// owned weapon. Reject corrupt Workshop multipliers instead of
        /// producing NaN, negative or effectively unbounded damage.</summary>
        internal static float MeleeDamage(float nativeSpineDamage, byte quality)
        {
            if (float.IsNaN(nativeSpineDamage) ||
                float.IsInfinity(nativeSpineDamage) ||
                nativeSpineDamage <= 0f || nativeSpineDamage > 50000f)
                return 0f;
            float q = System.Math.Min(quality, (byte)100) / 100f;
            return nativeSpineDamage * (q < 0.5f ? 0.5f + q : 1f);
        }

        internal static float MeleeReach(float nativeRange)
        {
            if (float.IsNaN(nativeRange) || float.IsInfinity(nativeRange) ||
                nativeRange < 0.5f) return 0f;
            return System.Math.Max(1f, System.Math.Min(nativeRange, 3.2f));
        }

        internal static float MeleeCadence(float nativeWeakSeconds)
        {
            if (float.IsNaN(nativeWeakSeconds) ||
                float.IsInfinity(nativeWeakSeconds) ||
                nativeWeakSeconds <= 0f) return 1.4f;
            return System.Math.Max(0.65f,
                System.Math.Min(nativeWeakSeconds + 0.2f, 1.9f));
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

using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Deterministic, per-actor decision core. Input must come from verified
    /// game-side sensors (NOT fabricated by a remote model). No Unity APIs.
    /// Decisions are intents, not permission to fire, damage or teleport.
    /// </summary>
    internal enum NpcIntent
    {
        Idle,
        Patrol,
        Follow,
        Investigate,
        Pursue,
        SeekCover,
        Retreat
    }

    internal struct NpcDecisionInput
    {
        internal bool IsHostile;
        internal bool IsFriendly;
        internal bool PassiveMode;
        internal bool CombatOnlyMode;
        internal bool PlayerNearby;
        internal bool ThreatVisible;
        internal bool HasRecentMemory;
        internal bool HeardThreat;
        internal bool UnderFire;
        internal bool HasVerifiedCover;
        internal bool HasRealHealth;
        internal float HealthFraction;
        internal string RemoteSuggestion;
    }

    internal sealed class NpcDecisionBrain
    {
        private const float MinimumIntentDuration = 0.45f;
        private const float EngageForgetSeconds = 8.0f;
        private NpcIntent _intent = NpcIntent.Idle;
        private float _changedAt = -1000f;
        private float _lastVerifiedEvidenceAt = -1000f;

        internal NpcIntent Intent { get { return _intent; } }

        internal void Reset()
        {
            _intent = NpcIntent.Idle;
            _changedAt = -1000f;
            _lastVerifiedEvidenceAt = -1000f;
        }

        internal NpcIntent Evaluate(float now, NpcDecisionInput input)
        {
            if (float.IsNaN(now) || float.IsInfinity(now) || now < 0)
            {
                Reset();
                return _intent;
            }

            // A remote AI cannot create a target that is not visible or
            // remembered by THIS actor's own gameplay perception.
            if (input.ThreatVisible || input.HeardThreat)
                _lastVerifiedEvidenceAt = now;
            bool hasMemory = input.HasRecentMemory &&
                now - _lastVerifiedEvidenceAt <= EngageForgetSeconds;

            NpcIntent desired;
            bool urgent = false;
            if (input.PassiveMode || !input.IsHostile)
            {
                desired = input.IsFriendly && input.PlayerNearby
                    ? NpcIntent.Follow : NpcIntent.Patrol;
            }
            else if (input.HasRealHealth && input.UnderFire &&
                     input.HealthFraction <
                        (input.CombatOnlyMode ? 0.09f : 0.30f))
            {
                // Match Line 1 CombatOnly tactical risk appetite:
                // survive longer under fire (9% critical vs Normal 30%).
                // Still require real damage and authoritative health.
                desired = NpcIntent.Retreat;
                urgent = true;
            }
            else if (input.UnderFire && input.HasVerifiedCover)
            {
                desired = NpcIntent.SeekCover;
                urgent = true;
            }
            else if (input.ThreatVisible)
            {
                desired = NpcIntent.Pursue;
                urgent = true;
            }
            else if (hasMemory)
            {
                desired = NpcIntent.Investigate;
            }
            else
            {
                desired = NpcIntent.Patrol;
            }

            // Suggested "engage" is merely confirmation of local vision.
            // The external service cannot override local safety/LOS or issue
            // combat commands when the weapon adapter is unimplemented.

            if (desired != _intent &&
                (urgent || now - _changedAt >= MinimumIntentDuration))
            {
                _intent = desired;
                _changedAt = now;
            }
            return _intent;
        }
    }

    /// <summary>
    /// Pure C# state tests executed once during module bootstrap. Do not
    /// assume this proves native NPC combat or navigation.
    /// </summary>
    internal static class NpcDecisionBrainSelfTest
    {
        internal static void Run()
        {
            try
            {
                var brain = new NpcDecisionBrain();
                var input = new NpcDecisionInput { IsHostile = true };
                Expect(brain.Evaluate(0f, input), NpcIntent.Patrol, "hostile no threat");
                input.RemoteSuggestion = "engage";
                Expect(brain.Evaluate(1f, input), NpcIntent.Patrol, "cannot invent vision");
                input.ThreatVisible = true;
                Expect(brain.Evaluate(2f, input), NpcIntent.Pursue, "verified sight");
                input.ThreatVisible = false;
                input.HasRecentMemory = true;
                Expect(brain.Evaluate(2.2f, input), NpcIntent.Pursue, "intent hysteresis");
                Expect(brain.Evaluate(3.5f, input), NpcIntent.Investigate, "last seen");
                input.UnderFire = true;
                input.HasVerifiedCover = true;
                Expect(brain.Evaluate(4f, input), NpcIntent.SeekCover, "verified cover");
                input.HasRealHealth = true;
                input.HealthFraction = 0.2f;
                Expect(brain.Evaluate(5f, input), NpcIntent.Retreat, "critical health");
                brain.Reset();
                input = new NpcDecisionInput { IsHostile = true, PassiveMode = true,
                    ThreatVisible = true, UnderFire = true, HasRealHealth = true,
                    HealthFraction = 0.1f };
                Expect(brain.Evaluate(10f, input), NpcIntent.Patrol, "passive mode");
                brain.Reset();
                input = new NpcDecisionInput { IsHostile = true, HeardThreat = true,
                    HasRecentMemory = true };
                Expect(brain.Evaluate(20f, input), NpcIntent.Investigate, "gunshot memory");
                input.HeardThreat = false;
                input.HasRecentMemory = false;
                Expect(brain.Evaluate(29f, input), NpcIntent.Patrol, "gunshot memory expires");
                brain.Reset();
                input = new NpcDecisionInput { IsFriendly = true, PlayerNearby = true };
                Expect(brain.Evaluate(30f, input), NpcIntent.Follow, "friendly follow");
                ProbeLogger.Log("NPC_BRAIN_SELFTEST_OK: vision, memory, state hysteresis, gunshot hearing, cover, retreat, passive and friendly");
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("NPC_BRAIN_SELFTEST_FAILED: " + ex.Message);
            }
        }

        private static void Expect(NpcIntent actual, NpcIntent expected, string name)
        {
            if (actual != expected)
                throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual);
        }
    }
}

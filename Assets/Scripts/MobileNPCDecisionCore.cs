using System;

namespace UnturnedBetter.NPC
{
    /// <summary>
    /// Pure, deterministic decision logic for mobile humanoid NPCs.
    /// This class deliberately has no Unity, networking, or Unturned SDK dependency:
    /// a game-facing driver supplies observations and applies the returned state.
    /// </summary>
    public sealed class MobileNPCDecisionCore
    {
        public enum State
        {
            Roam,
            Investigate,
            Engage,
            Retreat,
            ReturnToHome
        }

        public struct Observation
        {
            public bool HasTarget;
            public bool TargetVisible;
            public float TargetDistance;
            public bool HeardRelevantSound;
            public float SoundDistance;
            public float HealthFraction;
            public bool HomeIsSet;
            public bool IsFarFromHome;
            public bool TargetIsThreatening;
        }

        public struct Tuning
        {
            public float NoticeDistance;
            public float LoseInterestDistance;
            public float InvestigateDistance;
            public float RetreatHealthFraction;
            public float RecoverHealthFraction;
            public float MinimumEngageDistance;
            public float MaximumEngageDistance;

            public static Tuning Default
            {
                get
                {
                    return new Tuning
                    {
                        NoticeDistance = 35f,
                        LoseInterestDistance = 55f,
                        InvestigateDistance = 45f,
                        RetreatHealthFraction = 0.22f,
                        RecoverHealthFraction = 0.48f,
                        MinimumEngageDistance = 7f,
                        MaximumEngageDistance = 32f
                    };
                }
            }
        }

        private readonly Tuning tuning;
        private float lostTargetSeconds;
        private float unseenSeconds;
        private State state;

        public MobileNPCDecisionCore() : this(Tuning.Default) { }

        public MobileNPCDecisionCore(Tuning settings)
        {
            tuning = Sanitize(settings);
            state = State.Roam;
        }

        public State CurrentState { get { return state; } }

        /// <summary>
        /// Advances the decision state. deltaSeconds must be non-negative.
        /// The driver remains responsible for actual movement, aiming, and animation.
        /// </summary>
        public State Tick(Observation observation, float deltaSeconds)
        {
            float dt = Math.Max(0f, deltaSeconds);
            float health = Clamp01(observation.HealthFraction);

            if (observation.TargetVisible && observation.HasTarget)
            {
                unseenSeconds = 0f;
                lostTargetSeconds = 0f;
            }
            else
            {
                unseenSeconds += dt;
                lostTargetSeconds += dt;
            }

            // Survival takes precedence over pursuit. NPCs can recover from retreat
            // once health has improved, rather than switching states every frame.
            if (health <= tuning.RetreatHealthFraction &&
                observation.HasTarget && observation.TargetIsThreatening)
            {
                state = State.Retreat;
                return state;
            }

            if (state == State.Retreat)
            {
                if (health < tuning.RecoverHealthFraction)
                    return state;

                state = observation.HomeIsSet && observation.IsFarFromHome
                    ? State.ReturnToHome
                    : State.Investigate;
            }

            if (observation.TargetVisible && observation.HasTarget &&
                observation.TargetDistance <= tuning.NoticeDistance)
            {
                state = State.Engage;
                return state;
            }

            if (state == State.Engage)
            {
                if (observation.HasTarget &&
                    observation.TargetDistance > tuning.LoseInterestDistance &&
                    lostTargetSeconds < 8f)
                    return state;

                if (unseenSeconds < 3f && observation.HasTarget)
                    return state;

                state = observation.HomeIsSet && observation.IsFarFromHome
                    ? State.ReturnToHome
                    : State.Roam;
            }

            if (observation.HeardRelevantSound &&
                observation.SoundDistance <= tuning.InvestigateDistance)
            {
                state = State.Investigate;
                return state;
            }

            if (state == State.Investigate && unseenSeconds < 5f)
                return state;

            if (observation.HomeIsSet && observation.IsFarFromHome)
            {
                state = State.ReturnToHome;
                return state;
            }

            state = State.Roam;
            return state;
        }

        private static Tuning Sanitize(Tuning value)
        {
            Tuning defaults = Tuning.Default;
            if (value.NoticeDistance <= 0f) value.NoticeDistance = defaults.NoticeDistance;
            if (value.LoseInterestDistance < value.NoticeDistance)
                value.LoseInterestDistance = defaults.LoseInterestDistance;
            if (value.InvestigateDistance <= 0f)
                value.InvestigateDistance = defaults.InvestigateDistance;
            value.RetreatHealthFraction = Clamp01(value.RetreatHealthFraction);
            value.RecoverHealthFraction = Clamp01(value.RecoverHealthFraction);
            if (value.RecoverHealthFraction < value.RetreatHealthFraction)
                value.RecoverHealthFraction = defaults.RecoverHealthFraction;
            if (value.MinimumEngageDistance < 0f)
                value.MinimumEngageDistance = defaults.MinimumEngageDistance;
            if (value.MaximumEngageDistance <= value.MinimumEngageDistance)
                value.MaximumEngageDistance = defaults.MaximumEngageDistance;
            return value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}

////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// A second, *advisory* cognition layer. It has no player Transform reference and
    /// never queries the world's live player position. Only server-confirmed sensory
    /// evidence may update its hypotheses. The regular brain still owns all decisions.
    /// Memory belongs to one NPC and vanishes when the NPC is destroyed.
    /// </summary>
    public sealed class MobileNPCPlayerKnowledge
    {
        public enum Sense { None, Sight, Sound, Report }

        private Vector3 lastEvidencePosition;
        private Vector3 observedVelocity;
        private float lastEvidenceTime;
        private float initialConfidence;
        private float uncertaintyRadius;
        private int hintsIssued;
        private Sense lastSense;
        private bool hasEvidence;

        public Sense LastSense => lastSense;
        public bool HasEvidence => hasEvidence;

        /// <summary>
        /// Read-only uncertainty snapshot. This is an old sensory observation, never
        /// the actual player's live Transform after visual contact is lost.
        /// </summary>
        public bool TryGetEvidence(float now, out Vector3 center, out float confidence,
            out float uncertainty, out Sense sense)
        {
            center = Vector3.zero;
            confidence = 0f;
            uncertainty = 0f;
            sense = Sense.None;
            if (!hasEvidence || !IsFinite(now))
                return false;

            float age = Mathf.Max(0f, now - lastEvidenceTime);
            float lifespan = lastSense == Sense.Sight ? 22f
                : lastSense == Sense.Report ? 10f : 14f;
            confidence = initialConfidence * Mathf.Clamp01(1f - age / lifespan);
            if (confidence < 0.16f)
                return false;
            center = lastEvidencePosition + observedVelocity *
                (lastSense == Sense.Sight ? Mathf.Min(age, 1.5f) : 0f);
            uncertainty = Mathf.Min(16f, uncertaintyRadius +
                age * (lastSense == Sense.Sight ? 0.65f
                    : lastSense == Sense.Report ? 1.25f : 0.95f));
            sense = lastSense;
            return true;
        }

        public void ObserveSight(Vector3 position, float now)
        {
            if (!IsFinite(position) || !IsFinite(now))
                return;

            if (hasEvidence && lastSense == Sense.Sight)
            {
                float delta = now - lastEvidenceTime;
                if (delta > 0.08f && delta < 2f)
                {
                    Vector3 deltaPosition = position - lastEvidencePosition;
                    deltaPosition.y = 0f;
                    // A capped estimate, never a live target tracker after occlusion.
                    observedVelocity = Vector3.ClampMagnitude(deltaPosition / delta, 6f);
                }
            }
            else
            {
                observedVelocity = Vector3.zero;
            }
            lastEvidencePosition = position;
            lastEvidenceTime = now;
            initialConfidence = 1f;
            uncertaintyRadius = 0.8f;
            hintsIssued = 0;
            lastSense = Sense.Sight;
            hasEvidence = true;
        }

        /// <summary>
        /// Audible source position is a clue, not proof of identity. More distant or
        /// ambiguous sound stimuli should supply lower confidence at the call site.
        /// </summary>
        public void ObserveSound(Vector3 position, float confidence, float now)
        {
            if (!ShouldAcceptIndirectEvidence(position, confidence, now, Sense.Sound))
                return;
            AcceptIndirectEvidence(position, Mathf.Clamp01(confidence), now, Sense.Sound);
        }

        /// <summary>
        /// A teammate's spoken bearing is neither firsthand sight nor the
        /// actual player's footstep. It decays sooner and begins less precise.
        /// Voice transmission, hearing range and group authorization are
        /// already checked by the caller; this class never locates a player.
        /// </summary>
        public void ObserveReport(Vector3 position, float confidence, float now)
        {
            if (!ShouldAcceptIndirectEvidence(position, confidence, now, Sense.Report))
                return;
            AcceptIndirectEvidence(position, Mathf.Min(0.78f, Mathf.Clamp01(confidence)),
                now, Sense.Report);
        }

        private bool ShouldAcceptIndirectEvidence(Vector3 position, float confidence,
            float now, Sense kind)
        {
            if (!IsFinite(position) || !IsFinite(confidence) || !IsFinite(now))
                return false;
            float age = now - lastEvidenceTime;
            if (hasEvidence && age >= 0f)
            {
                // Recent direct eyesight is more trustworthy than any indirect
                // clue, however loud/repeated. The ordinary brain handles a
                // directly seen combat target separately.
                if (lastSense == Sense.Sight && age < 8f)
                    return false;

                Vector3 displacement = position - lastEvidencePosition;
                displacement.y = 0f;
                float squared = displacement.sqrMagnitude;
                // Repeated near-identical footsteps/reports must not renew an
                // investigation indefinitely or refresh the hint budget.
                if (age < 3.5f && squared <= 8f * 8f
                    && confidence <= initialConfidence + 0.10f)
                    return false;

                // Secondhand testimony cannot erase a stronger firsthand
                // sound simply because a distant teammate shouted a bearing.
                if (kind == Sense.Report && lastSense == Sense.Sound
                    && age < 5f && confidence <= initialConfidence + 0.20f)
                    return false;
            }
            return true;
        }

        private void AcceptIndirectEvidence(Vector3 position, float confidence,
            float now, Sense kind)
        {
            lastEvidencePosition = position;
            lastEvidenceTime = now;
            observedVelocity = Vector3.zero;
            initialConfidence = confidence;
            uncertaintyRadius = kind == Sense.Report
                ? Mathf.Lerp(13f, 5f, confidence)
                : Mathf.Lerp(9f, 2.5f, confidence);
            hintsIssued = 0;
            lastSense = kind;
            hasEvidence = true;
        }

        /// <summary>
        /// Suggests one bounded, uncertain search location. No exact live coordinates
        /// are ever consulted; hypotheses decay and stop after a few attempts.
        /// </summary>
        public bool TrySuggestSearchPoint(float now, out Vector3 searchPoint)
        {
            searchPoint = Vector3.zero;
            if (!hasEvidence || !IsFinite(now) || hintsIssued >= 4)
                return false;

            float age = Mathf.Max(0f, now - lastEvidenceTime);
            float lifetime = lastSense == Sense.Sight ? 22f
                : lastSense == Sense.Report ? 10f : 14f;
            float confidence = initialConfidence * Mathf.Clamp01(1f - age / lifetime);
            if (confidence < 0.14f)
                return false;

            // The target might have continued moving briefly after leaving view,
            // but uncertainty quickly dominates that extrapolation.
            float anticipation = lastSense == Sense.Sight ? Mathf.Min(age, 2f) : 0f;
            Vector3 predicted = lastEvidencePosition + observedVelocity * anticipation;
            float radius = uncertaintyRadius + age * (lastSense == Sense.Sight ? 0.65f
                : lastSense == Sense.Report ? 1.25f : 0.95f);
            float ring = Mathf.Min(14f, radius) * Mathf.Sqrt(Random.value);
            float angle = Random.Range(0f, Mathf.PI * 2f);
            searchPoint = predicted + new Vector3(Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);
            return true;
        }

        /// <summary>
        /// Count a speculative hint only once its endpoint passed the
        /// server's physical ground/path check and the decision brain took it.
        /// Invalid floors and walls do not spend this finite hypothesis budget.
        /// </summary>
        public void ConfirmSearchPointAccepted()
        {
            if (hasEvidence && hintsIssued < 4)
                ++hintsIssued;
        }

        public void Clear()
        {
            hasEvidence = false;
            lastSense = Sense.None;
            hintsIssued = 0;
            observedVelocity = Vector3.zero;
            initialConfidence = 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}

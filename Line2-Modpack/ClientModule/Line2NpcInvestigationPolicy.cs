using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Pure Line 1 investigation selection gate. An acoustic/radio hypothesis
    /// is NOT a target, and can only become a search destination if finite,
    /// on the same physical floor, close to the LAST OBSERVED area, and
    /// inside the original brain's expiring investigation state.
    /// </summary>
    internal static class Line2NpcInvestigationPolicy
    {
        internal static bool MayProbe(bool actualInvestigation,
            bool hasPersonalEvidence, bool visibleEnemy,
            float confidence, float uncertainty,
            Vector3 observedArea, Vector3 proposed, Vector3 ownPosition,
            float maxRadius)
        {
            if (!actualInvestigation || !hasPersonalEvidence || visibleEnemy ||
                !Finite(confidence) || !Finite(uncertainty) ||
                confidence < 0.23f || uncertainty < 0f || uncertainty > 16f ||
                !Finite(observedArea) || !Finite(proposed) ||
                !Finite(ownPosition) ||
                !Finite(maxRadius) || maxRadius < 1f || maxRadius > 35f)
                return false;
            // Last-sighted positions and noisy hints both have bounded
            // uncertainty. Never jump to a guessed upper floor/rooftop.
            if (Math.Abs(proposed.y - ownPosition.y) > 0.8f ||
                Math.Abs(proposed.y - observedArea.y) > 1.6f)
                return false;
            Vector3 delta = proposed - observedArea;
            delta.y = 0f;
            if (delta.sqrMagnitude > maxRadius * maxRadius)
                return false;
            Vector3 distance = proposed - ownPosition;
            distance.y = 0f;
            return distance.sqrMagnitude <= 35f * 35f;
        }

        private static bool Finite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }

        private static bool Finite(Vector3 x)
        {
            return Finite(x.x) && Finite(x.y) && Finite(x.z);
        }
    }
}

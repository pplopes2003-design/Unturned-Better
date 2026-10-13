////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// A small tactical locomotion utility policy, not a second movement controller.
    /// Reads observable state only; cannot move, reveal targets, or change navigation.
    /// Deliberate minimum commitment prevents crouch/stand and walk/run flicker.
    /// </summary>
    public sealed class MobileNPCLocomotionPolicy
    {
        public enum Gait { Hold, Sneak, Walk, Sprint }

        public struct Situation
        {
            public bool HasDestination;
            public bool TargetVisible;
            public bool Swimming;
            public bool UnderFire;
            public bool Injured;
            public bool NearTarget;
            public bool InCover;
            public bool Investigating;
            public bool Retreating;
            public bool Pursuing;
            public float DestinationDistance;
        }

        private Gait gait = Gait.Walk;
        private float committedUntil;
        public Gait CurrentGait => gait;

        public Gait Decide(Situation situation, float now)
        {
            Gait desired;
            if (!situation.HasDestination)
                desired = Gait.Hold;
            else if (situation.Swimming || situation.Injured)
                desired = Gait.Walk;
            else if (situation.Retreating || (situation.UnderFire && !situation.InCover))
                desired = Gait.Sprint;
            else if (situation.Pursuing && situation.TargetVisible && !situation.NearTarget)
                desired = Gait.Sprint;
            else if (situation.Investigating && !situation.TargetVisible)
                desired = Gait.Sneak;
            else if (situation.Pursuing && !situation.TargetVisible && situation.NearTarget)
                desired = Gait.Sneak;
            else if (situation.InCover && !situation.TargetVisible)
                desired = Gait.Sneak;
            else
                desired = Gait.Walk;

            // Urgent survival, no destination and water transitions override commitment.
            bool urgent = desired == Gait.Sprint && (situation.Retreating || situation.UnderFire)
                || desired == Gait.Hold || situation.Swimming;
            if (desired != gait && (urgent || now >= committedUntil))
            {
                gait = desired;
                committedUntil = now + (desired == Gait.Sneak ? 2.2f : 1.0f);
            }
            return gait;
        }

        public void Reset()
        {
            gait = Gait.Walk;
            committedUntil = 0f;
        }
    }
}

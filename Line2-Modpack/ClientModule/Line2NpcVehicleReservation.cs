using System;
using System.Collections.Generic;

namespace Unturned2.Line2
{
    /// <summary>
    /// Pure reservation ledger for one NPC per actual Steam vehicle instance.
    /// No hidden virtual SteamPlayer, no seat edits, no engine physics.
    /// The vehicle adapter owns validation and releases on despawn or death.
    /// </summary>
    internal sealed class Line2NpcVehicleReservation
    {
        private struct Claim
        {
            internal int Owner;
            internal float Until;
        }

        private readonly Dictionary<uint, Claim> _claims =
            new Dictionary<uint, Claim>();

        internal bool TryClaim(uint vehicleId, int owner, float now,
            float duration)
        {
            if (vehicleId == 0 || owner == 0 || !Valid(now) ||
                !Valid(duration) || duration <= 0f || duration > 60f)
                return false;
            Claim prior;
            if (_claims.TryGetValue(vehicleId, out prior) &&
                prior.Owner != owner && prior.Until > now)
                return false;
            _claims[vehicleId] = new Claim {
                Owner = owner, Until = now + duration
            };
            return true;
        }

        internal bool Owns(uint vehicleId, int owner, float now)
        {
            Claim entry;
            return vehicleId != 0 && owner != 0 && Valid(now) &&
                _claims.TryGetValue(vehicleId, out entry) &&
                entry.Owner == owner && entry.Until > now;
        }

        internal void Release(uint vehicleId, int owner)
        {
            Claim current;
            if (_claims.TryGetValue(vehicleId, out current) &&
                current.Owner == owner)
                _claims.Remove(vehicleId);
        }

        internal void ReleaseAll(int owner)
        {
            var expired = new List<uint>();
            foreach (var pair in _claims)
                if (pair.Value.Owner == owner)
                    expired.Add(pair.Key);
            foreach (uint key in expired)
                _claims.Remove(key);
        }

        internal void Reset()
        {
            _claims.Clear();
        }

        private static bool Valid(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }
    }
}

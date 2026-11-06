using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Contract for a future game-authoritative NPC implementation.
    /// This file does NOT discover, spawn or control SDG NPC entities.
    /// It deliberately contains no reflection or unverified Unturned APIs.
    /// </summary>
    internal interface IGameNpcAdapter
    {
        bool TryObserve(string agentId, out NpcObservation observation);
        bool IsAliveAndControlled(string agentId);
        bool TryApplyIntention(string agentId, NpcIntention intention);
    }

    [Serializable]
    internal sealed class NpcObservation
    {
        public string kind = "npc";
        public float[] position;
        public float health;
        public bool threat_visible;
        public bool under_fire;
        public bool has_cover;
        public float[] last_known_threat;
    }

    internal enum NpcIntention
    {
        Idle, Investigate, SeekCover, Engage, Retreat
    }

    /// <summary>
    /// Safe-by-default boundary. EnableActualExecution remains false until
    /// the actual entity adapter and authoritative multiplayer lifecycle
    /// are verified in a Steam test installation.
    /// </summary>
    internal sealed class NpcIntentionGate
    {
        internal const bool EnableActualExecution = false;
        private readonly Dictionary<string, int> _lastTick = new Dictionary<string, int>();

        internal bool TryAccept(
            IGameNpcAdapter adapter, string agentId, int requestedTick,
            int returnedTick, string returnedAgentId, int protocol,
            string action, int validMs, long requestTimestampMs,
            long nowTimestampMs, out string reason)
        {
            reason = "rejected";
            if (adapter == null || string.IsNullOrEmpty(agentId) ||
                protocol != 1 || agentId != returnedAgentId || requestedTick != returnedTick ||
                validMs < 1 || validMs > 250 || nowTimestampMs < requestTimestampMs ||
                nowTimestampMs - requestTimestampMs > validMs)
            {
                reason = "invalid_or_expired";
                return false;
            }
            int last;
            if (_lastTick.TryGetValue(agentId, out last) && returnedTick <= last)
            {
                reason = "replayed_tick";
                return false;
            }
            NpcIntention intention;
            switch (action)
            {
                case "idle": intention = NpcIntention.Idle; break;
                case "investigate": intention = NpcIntention.Investigate; break;
                case "seek_cover": intention = NpcIntention.SeekCover; break;
                case "engage": intention = NpcIntention.Engage; break;
                case "retreat": intention = NpcIntention.Retreat; break;
                default: reason = "unknown_action"; return false;
            }

            if (!adapter.IsAliveAndControlled(agentId))
            {
                reason = "agent_unavailable_or_unowned";
                return false;
            }
            if (!EnableActualExecution)
            {
                reason = "execution_disabled";
                return false;
            }
            // Only an authorized, validated adapter may translate an intention
            // into game actions. This interface has no implementation yet.
            if (!adapter.TryApplyIntention(agentId, intention))
            {
                reason = "adapter_declined";
                return false;
            }
            _lastTick[agentId] = returnedTick;
            reason = "applied";
            return true;
        }

        internal void Forget(string agentId)
        {
            _lastTick.Remove(agentId);
        }
    }
}

// Server-side integration seam for an LDM/OpenMod adapter.
// This file is not compiled into the SDK fork's Assembly-CSharp.
using System;
using System.Collections.Generic;

namespace UnturnedBetter.SteamModpack
{
    /// <summary>
    /// Implement with a real game-side NPC registry and a main-thread, server-
    /// authoritative action validator. Never capture unseen enemies or hidden
    /// level data and never let the sidecar directly move a CharacterController.
    /// </summary>
    public interface ILocalAIAgentSource
    {
        bool IsServerReady { get; }
        string Mode { get; } // normal | combat_total
        void CaptureObservedAgents(List<LocalAIAgent> destination);
        bool TryApplyValidatedIntent(string npcId, string intent, string reason);
    }

    public sealed class LocalAIHostPump : IDisposable
    {
        private readonly LocalAIStrategyClient client;
        private readonly ILocalAIAgentSource source;
        private readonly List<LocalAIAgent> snapshot = new List<LocalAIAgent>(64);
        private readonly HashSet<string> currentIds = new HashSet<string>(StringComparer.Ordinal);
        private float nextSubmitTime;
        private bool disposed;

        public LocalAIHostPump(ILocalAIAgentSource source, int port = 18742)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            client = new LocalAIStrategyClient(port);
        }

        /// <summary>Call from game main thread only, not from HTTP callback.</summary>
        public void Tick(float gameTime)
        {
            if (disposed || !source.IsServerReady) return;

            List<LocalAIDecision> decisions;
            if (client.TryConsumeDecisions(out decisions))
            {
                foreach (LocalAIDecision decision in decisions)
                {
                    if (decision == null || !currentIds.Contains(decision.NpcId)) continue;
                    // Source must revalidate actor alive/ownership, real first-hand
                    // perception, safety, nav and tactical preemption at APPLY time.
                    source.TryApplyValidatedIntent(decision.NpcId,
                        decision.Intent, decision.Reason);
                }
            }

            if (gameTime < nextSubmitTime) return;
            nextSubmitTime = gameTime + 1.0f; // staggered, <= once per second
            snapshot.Clear();
            source.CaptureObservedAgents(snapshot);
            if (snapshot.Count == 0 || snapshot.Count > 64)
                return;
            string mode = source.Mode;
            if (client.Submit(mode, snapshot))
            {
                currentIds.Clear();
                foreach (LocalAIAgent agent in snapshot)
                    currentIds.Add(agent.NpcId);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client.Dispose();
        }
    }
}

////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;
using SDG.Unturned;

namespace Unturned2.Line2
{
    /// <summary>
    /// Server-local approximation of audible player footsteps, rather than a cheat
    /// that reveals player position on demand. Emits only when actual displacement
    /// produces a plausible footfall, with cooldown and capped hearing radius.
    /// Does not claim to replace native player footstep events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Line2MobileNPCPlayerFootstepSensor : MonoBehaviour
    {
        private sealed class MovementSample
        {
            public Vector3 Position;
            public float At;
            public float NextFootstep;
        }

        [SerializeField, Min(0.05f)] private float sampleInterval = 0.2f;
        [SerializeField, Min(1f)] private float walkingAudibleRadius = 9f;
        [SerializeField, Min(1f)] private float runningAudibleRadius = 17f;
        private readonly Dictionary<Player, MovementSample> samples = new Dictionary<Player, MovementSample>();
        private readonly List<Player> stalePlayers = new List<Player>(8);
        private float nextSampleAt;
        private SteamNpcPrototypeRuntime runtime;

        private void Awake()
        {
            runtime = GetComponent<SteamNpcPrototypeRuntime>();
        }

        private void Update()
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Provider.hasCheats || !Level.isLoaded ||
                (Provider.clients != null && Provider.clients.Count > 1) ||
                Player.player == null || runtime == null ||
                Time.time < nextSampleAt)
                return;

            nextSampleAt = Time.time + Mathf.Max(0.05f, sampleInterval);
            stalePlayers.Clear();
            foreach (KeyValuePair<Player, MovementSample> item in samples)
            {
                if (item.Key == null || item.Key.life == null || item.Key.life.isDead
                    || !item.Key.gameObject.activeInHierarchy)
                    stalePlayers.Add(item.Key);
            }
            for (int i = 0; i < stalePlayers.Count; ++i)
                samples.Remove(stalePlayers[i]);

            // Singleplayer only: Player.player can exist before Provider.clients
            // is populated. Observe the actual local avatar, not a client
            // roster that may be empty in an offline world.
            for (int i = 0; i < 1; ++i)
            {
                Player player = Player.player;
                if (player == null || player.life == null || player.life.isDead)
                    continue;

                Vector3 position = player.transform.position;
                if (!samples.TryGetValue(player, out MovementSample sample))
                {
                    samples.Add(player, new MovementSample { Position = position, At = Time.time });
                    continue;
                }

                float elapsed = Time.time - sample.At;
                if (elapsed < 0.05f)
                    continue;
                Vector3 displacement = position - sample.Position;
                displacement.y = 0f;
                float distance = displacement.magnitude;
                float speed = distance / elapsed;
                sample.Position = position;
                sample.At = Time.time;

                // Teleportation/vehicles cannot legitimately generate a trail of footsteps.
                // Very slow motion is effectively inaudible; this also reduces emissions
                // from crouched movement without relying on unstable native stance APIs.
                if (speed < 1.25f || speed > 8.5f || Time.time < sample.NextFootstep)
                    continue;

                bool running = speed > 3.9f;
                sample.NextFootstep = Time.time + (running ? 0.33f : 0.52f);
                float radius = running ? runningAudibleRadius : walkingAudibleRadius;
                runtime.NotifyPlayerFootstep(position, radius,
                    running ? 0.82f : 0.55f);
            }
        }

        private void OnDisable()
        {
            samples.Clear();
        }
    }
}

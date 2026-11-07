using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// The only hearing source is a verified native weapon discharge.
    /// NEVER infer weapon sounds from player position or service suggestions.
    /// 32 actors max, one dispatch per 0.16s and pellet 0 only; no new audio,
    /// NPC damage or shots. Stops when not the authoritative local host.
    /// </summary>
    internal sealed class NpcAcousticSensorBridge : MonoBehaviour
    {
        private const float HearingRadius = 80f;
        private const float MinimumDispatchGap = 0.16f;
        private const int ActorCap = 32;
        private readonly List<PrototypeActor> _buffer = new List<PrototypeActor>(ActorCap);
        private SteamNpcPrototypeRuntime _runtime;
        private float _lastDispatchAt = -100f;
        private float _nextLogAt;

        private void OnEnable()
        {
            _runtime = GetComponent<SteamNpcPrototypeRuntime>();
            UseableGun.onBulletSpawned += OnBulletSpawned;
        }

        private void OnDisable()
        {
            UseableGun.onBulletSpawned -= OnBulletSpawned;
            _buffer.Clear();
        }

        private void OnBulletSpawned(UseableGun gun, BulletInfo bullet)
        {
            if (gun == null || bullet == null || bullet.pellet != 0 ||
                !Provider.isServer || Dedicator.IsDedicatedServer ||
                !Provider.hasCheats || !Level.isLoaded ||
                Player.player == null)
                return;

            float now = Time.time;
            if (now - _lastDispatchAt < MinimumDispatchGap)
                return;
            if (_runtime == null) _runtime = GetComponent<SteamNpcPrototypeRuntime>();
            if (_runtime == null || _runtime.ActiveCount == 0)
                return;
            Vector3 origin = bullet.origin;
            if (float.IsNaN(origin.x) || float.IsInfinity(origin.x) ||
                float.IsNaN(origin.y) || float.IsInfinity(origin.y) ||
                float.IsNaN(origin.z) || float.IsInfinity(origin.z))
                return;
            _lastDispatchAt = now;
            _runtime.CopyLiveActors(_buffer, ActorCap);
            int delivered = 0;
            foreach (PrototypeActor actor in _buffer)
            {
                if (actor == null) continue;
                if ((actor.transform.position - origin).sqrMagnitude >
                    HearingRadius * HearingRadius)
                    continue;
                actor.HearVerifiedGunshot(origin, now);
                delivered++;
            }
            _buffer.Clear();
            if (delivered > 0 && now >= _nextLogAt)
            {
                ProbeLogger.Log("NPC_NATIVE_GUNSHOT_HEARD: listeners=" +
                    delivered + " maxRange=" + HearingRadius +
                    " (coarse investigation; no simulated damage)");
                _nextLogAt = now + 12f;
            }
        }
    }
}

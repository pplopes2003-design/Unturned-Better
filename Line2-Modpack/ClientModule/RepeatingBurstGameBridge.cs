using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Singleplayer-only repeating 3-round BURST adapter for Legendary Eaglefire
    /// (item ID 32612). Re-triggers the *native* UseableGun after an authorized
    /// burst completes. Never creates bullets, changes ammo, or deals damage.
    ///
    /// The player's native ControlsSettings.primary binding is respected. Feature requires local server authority.
    /// </summary>
    internal sealed class RepeatingBurstGameBridge : MonoBehaviour
    {
        private const ushort LegendaryEaglefireID = 32612;
        private const float InterBurstDelaySeconds = 0.25f;
        private const float ShotTimeoutSeconds = 1.5f;

        private UseableGun _gun;
        private bool _armed;
        private bool _waitingForBurstRestart;
        private int _nativeShotsInBurst;
        private float _nextBurstAt;
        private float _shotTimeoutAt;
        private float _nextLogAt;

        private void OnEnable()
        {
            UseableGun.onBulletSpawned += OnNativeBulletSpawned;
            ProbeLogger.Log("BURST_BRIDGE_READY: experimental native 3-shot auto-repeat " +
                            "for Eaglefire ID=32612, BURST mode, singleplayer.");
        }

        private void OnDisable()
        {
            UseableGun.onBulletSpawned -= OnNativeBulletSpawned;
            Clear();
        }

        private static bool InLocalGameplay()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                   (Provider.clients == null || Provider.clients.Count <= 1) &&
                   Level.isLoaded && Player.player != null &&
                   Provider.hasCheats && Cursor.lockState == CursorLockMode.Locked;
        }

        private static bool GetEligibleGun(out UseableGun gun)
        {
            gun = null;
            if (!InLocalGameplay()) return false;
            Player local = Player.player;
            if (local.life == null || local.life.isDead ||
                local.equipment == null || local.equipment.itemID != LegendaryEaglefireID ||
                !local.equipment.IsEquipAnimationFinished)
                return false;
            byte[] state = local.equipment.state;
            if (state == null || state.Length <= GunStateIndices.FIREMODE ||
                state.Length <= GunStateIndices.AMMO ||
                state[GunStateIndices.FIREMODE] != (byte)EFiremode.BURST ||
                state[GunStateIndices.AMMO] == 0)
                return false;
            gun = local.equipment.useable as UseableGun;
            return gun != null;
        }

        private void Clear()
        {
            _gun = null;
            _armed = false;
            _waitingForBurstRestart = false;
            _nativeShotsInBurst = 0;
            _nextBurstAt = 0f;
            _shotTimeoutAt = 0f;
        }

        private void Update()
        {
            UseableGun eligible;
            if (!GetEligibleGun(out eligible) || !Input.GetKey(ControlsSettings.primary))
            {
                Clear();
                return;
            }

            // Native first shot may run before or after our Update. The
            // onBulletSpawned hook independently arms when an actual shot lands.
            if (_gun != eligible)
                Clear();

            if (!_armed)
            {
                if (!Input.GetKeyDown(ControlsSettings.primary))
                    return;
                _gun = eligible;
                _armed = true;
                _shotTimeoutAt = Time.realtimeSinceStartup + ShotTimeoutSeconds;
                return;
            }

            if (!_waitingForBurstRestart)
            {
                // A jam, interrupted burst or reload must not auto-retrigger.
                if (Time.realtimeSinceStartup > _shotTimeoutAt)
                    Clear();
                return;
            }

            if (Time.realtimeSinceStartup < _nextBurstAt)
                return;

            // The normal UseableGun API checks native cooldown, busy state,
            // safety, sprinting, reload, etc. A failed trigger aborts rather
            // than retrying or manipulating its private internals.
            bool accepted = false;
            try { accepted = eligible.startPrimary(); }
            catch (Exception ex)
            {
                ProbeLogger.Log("BURST_RETRIGGER_FAILED: " + ex.GetType().Name);
            }
            if (!accepted)
            {
                Clear();
                return;
            }

            _gun = eligible;
            _waitingForBurstRestart = false;
            _nativeShotsInBurst = 0;
            _shotTimeoutAt = Time.realtimeSinceStartup + ShotTimeoutSeconds;
            if (Time.realtimeSinceStartup >= _nextLogAt)
            {
                ProbeLogger.Log("BURST_NATIVE_RETRIGGER_OK: additional 3-shot group requested.");
                _nextLogAt = Time.realtimeSinceStartup + 8f;
            }
        }

        private void OnNativeBulletSpawned(UseableGun firedGun, BulletInfo bullet)
        {
            // A shotgun can emit many BulletInfo pellets per shot. Only
            // count pellet zero, and only from *our* selected local gun.
            if (bullet == null || bullet.pellet != 0 ||
                !Input.GetKey(ControlsSettings.primary))
                return;
            UseableGun eligible;
            if (!GetEligibleGun(out eligible) || firedGun != eligible)
                return;

            if (_gun != eligible)
            {
                Clear();
                _gun = eligible;
            }
            if (!_armed)
            {
                _armed = true;
                _nativeShotsInBurst = 0;
            }
            if (_waitingForBurstRestart)
                return;

            _nativeShotsInBurst++;
            _shotTimeoutAt = Time.realtimeSinceStartup + ShotTimeoutSeconds;
            if (_nativeShotsInBurst >= 3)
            {
                _waitingForBurstRestart = true;
                _nextBurstAt = Time.realtimeSinceStartup + InterBurstDelaySeconds;
            }
        }
    }
}

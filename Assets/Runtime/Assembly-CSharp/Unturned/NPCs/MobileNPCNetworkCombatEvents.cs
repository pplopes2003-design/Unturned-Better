using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Binds the authoritative NPC combat event to remote PRESENTATION.
    /// No client is permitted to drive NPC shooting, ammo or damage.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCHumanoidRangedCombat))]
    public sealed class MobileNPCNetworkCombatEvents : MonoBehaviour
    {
        private MobileNPCHumanoidRangedCombat ranged;
        private MobileNPCBrainDriver brain;

        private void Awake()
        {
            ranged = GetComponent<MobileNPCHumanoidRangedCombat>();
            brain = GetComponent<MobileNPCBrainDriver>();
        }

        private void OnEnable()
        {
            if (ranged != null)
                ranged.ShotFired += OnCommittedShot;
        }

        private void OnDisable()
        {
            if (ranged != null)
                ranged.ShotFired -= OnCommittedShot;
        }

        private void OnCommittedShot()
        {
            if (!Provider.isServer || !Dedicator.IsDedicatedServer || brain == null)
                return;
            ItemGunAsset equipped = ranged != null ? ranged.SelectedGunAsset : null;
            MobileNPCNetworkReplica.NotifyCommittedShotServer(brain,
                equipped != null ? equipped.id : (ushort)0);
        }
    }
}

////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Reuses the selected vanilla gun's shoot/reload clips for mobile NPC combat.
    /// This is presentation-only: damage, ammo and shot timing remain server-authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCHumanoidRangedCombat))]
    public sealed class MobileNPCRangedPresentation : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float shotVolume = 0.65f;
        [SerializeField, Range(0f, 1f)] private float reloadVolume = 0.7f;
        [SerializeField, Min(1f)] private float minimumRolloffDistance = 1f;
        [SerializeField, Min(2f)] private float maximumRolloffDistance = 64f;

        private MobileNPCHumanoidRangedCombat combat;
        private MobileNPCHumanoidCombat meleeCombat;
        private MobileNPCInventory inventory;
        private float meleeVisualUntil;
        private Transform skeleton;
        private Transform equippedWeaponModel;
        private ushort displayedWeaponID;
        private float nextVisualRefreshTime;

        private void Awake()
        {
            combat = GetComponent<MobileNPCHumanoidRangedCombat>();
            meleeCombat = GetComponent<MobileNPCHumanoidCombat>();
            inventory = GetComponent<MobileNPCInventory>();
        }

        private void OnEnable()
        {
            if (combat == null)
            {
                combat = GetComponent<MobileNPCHumanoidRangedCombat>();
            }

            if (combat != null)
            {
                combat.ShotFired += HandleShotFired;
                combat.ReloadStarted += HandleReloadStarted;
            }
            if (meleeCombat == null)
                meleeCombat = GetComponent<MobileNPCHumanoidCombat>();
            if (meleeCombat != null)
                meleeCombat.AttackStarted += HandleMeleeStrike;
        }

        private void OnDisable()
        {
            DestroyWeaponModel();
            if (combat != null)
            {
                combat.ShotFired -= HandleShotFired;
                combat.ReloadStarted -= HandleReloadStarted;
            }
            if (meleeCombat != null)
                meleeCombat.AttackStarted -= HandleMeleeStrike;
        }

        private void Update()
        {
#if !DEDICATED_SERVER
            if (Dedicator.IsDedicatedServer || Time.time < nextVisualRefreshTime) return;
            nextVisualRefreshTime = Time.time + 0.5f;
            RefreshWeaponModel();
#endif
        }

        private void RefreshWeaponModel()
        {
            ItemGunAsset gun = combat != null ? combat.SelectedGunAsset : null;
            ItemAsset active = gun;
            if (Time.time < meleeVisualUntil)
            {
                active = meleeCombat != null && meleeCombat.LastAttackUsedMeleeWeapon
                    && meleeCombat.LastAttackWeaponID != 0
                    ? Assets.find(EAssetType.ITEM, meleeCombat.LastAttackWeaponID) as ItemMeleeAsset
                    : null; // fists put a rifle away while striking
            }
            else if (gun == null)
            {
                if (inventory == null)
                    inventory = GetComponent<MobileNPCInventory>();
                if (inventory != null
                    && inventory.TryGetBestMeleeServer(out Item owned, out ItemMeleeAsset melee))
                    active = melee;
            }
            ushort desiredID = active != null ? active.id : (ushort)0;
            if (desiredID == displayedWeaponID && equippedWeaponModel != null) return;
            if (desiredID == displayedWeaponID && desiredID == 0) return;

            DestroyWeaponModel();
            displayedWeaponID = desiredID;
            if (active == null || Dedicator.IsDedicatedServer) return;

            if (skeleton == null)
            {
                Transform[] transforms = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; ++i)
                {
                    if (transforms[i] != null && transforms[i].name == "Skeleton")
                    {
                        skeleton = transforms[i];
                        break;
                    }
                }
            }
            if (skeleton == null) return;

            Transform spine = skeleton.Find("Spine");
            if (spine == null) return;
            Transform rightHook = spine.Find("Right_Shoulder/Right_Arm/Right_Hand/Right_Hook");
            Transform leftHook = spine.Find("Left_Shoulder/Left_Arm/Left_Hand/Left_Hook");
            Transform spineHook = spine.Find("Spine_Hook");
            Transform parent = rightHook != null ? rightHook : (spineHook != null ? spineHook : spine);
            switch (active.EquipableModelParent)
            {
                case EEquipableModelParent.LeftHook:
                    if (leftHook != null) parent = leftHook;
                    break;
                case EEquipableModelParent.Spine:
                    parent = spine;
                    break;
                case EEquipableModelParent.SpineHook:
                    if (spineHook != null) parent = spineHook;
                    break;
            }

            GameObject prefab = active.equipablePrefab != null ? active.equipablePrefab : active.item;
            if (prefab == null) return;
            Material temporaryMaterial;
            Transform model = ItemTool.InstantiateItem(100, active.getState(), false, active, null, true,
                null, out temporaryMaterial, null, prefabOverride: prefab);
            if (model == null) return;
            model.SetParent(parent, false);
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(0f, 0f, 90f);
            model.localScale = Vector3.one;
            model.DestroyRigidbody();
            Layerer.enemy(model);
            equippedWeaponModel = model;
        }

        private void DestroyWeaponModel()
        {
            if (equippedWeaponModel != null)
            {
                Destroy(equippedWeaponModel.gameObject);
                equippedWeaponModel = null;
            }
            displayedWeaponID = 0;
        }

        private void HandleMeleeStrike()
        {
            // The weapon selector and server physics own the actual hit.
            // This visual state only follows its authentic committed swing.
            meleeVisualUntil = Time.time + 1.1f;
            nextVisualRefreshTime = 0f;
        }

        private void HandleShotFired()
        {
#if !DEDICATED_SERVER
            ItemGunAsset gun = combat != null ? combat.SelectedGunAsset : null;
            PlayClip(gun != null ? gun.shoot : null, shotVolume);
#endif
        }

        private void HandleReloadStarted()
        {
#if !DEDICATED_SERVER
            ItemGunAsset gun = combat != null ? combat.SelectedGunAsset : null;
            PlayClip(gun != null ? gun.reload : null, reloadVolume);
#endif
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (clip == null || !Provider.isServer)
            {
                return;
            }

            OneShotAudioParameters parameters = new OneShotAudioParameters(transform, clip);
            parameters.volume = Mathf.Clamp01(volume);
            parameters.SetLinearRolloff(Mathf.Max(0.1f, minimumRolloffDistance),
                Mathf.Max(minimumRolloffDistance + 0.1f, maximumRolloffDistance));
            parameters.Play();
        }
    }
}

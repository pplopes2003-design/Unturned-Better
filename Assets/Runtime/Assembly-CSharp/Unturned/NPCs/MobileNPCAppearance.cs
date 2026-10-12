////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Gives each mobile NPC a simple, distinct appearance using the game's own face,
    /// hair and beard assets. It does not create or modify any source textures.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCAppearance : MonoBehaviour
    {
        private static readonly List<byte> availableFaces = new List<byte>();
        private static readonly List<byte> availableHair = new List<byte>();
        private static readonly List<byte> availableBeards = new List<byte>();
        private static bool hasScannedAssets;

        private static readonly Color[] SkinPalette =
        {
            new Color(0.91f, 0.76f, 0.63f),
            new Color(0.78f, 0.59f, 0.45f),
            new Color(0.65f, 0.46f, 0.34f),
            new Color(0.48f, 0.32f, 0.24f),
            new Color(0.96f, 0.82f, 0.70f),
            new Color(0.72f, 0.52f, 0.39f)
        };

        private static readonly Color[] HairPalette =
        {
            new Color(0.12f, 0.085f, 0.06f),
            new Color(0.24f, 0.14f, 0.075f),
            new Color(0.42f, 0.25f, 0.12f),
            new Color(0.66f, 0.48f, 0.25f),
            new Color(0.48f, 0.19f, 0.10f),
            new Color(0.48f, 0.45f, 0.40f),
            new Color(0.08f, 0.07f, 0.065f)
        };

        private void Start()
        {
            if (Dedicator.IsDedicatedServer)
                return;

            HumanClothes clothes = GetComponentInChildren<HumanClothes>(true);
            if (clothes == null)
            {
                Debug.LogWarning("[MobileNPCAppearance] NPC_Client has no HumanClothes component; keeping its default appearance.", this);
                return;
            }

            ScanAvailableAssets();
            int seed = unchecked(GetInstanceID() * 397 ^ transform.position.GetHashCode());
            System.Random random = new System.Random(seed);

            // Face 0 is the native fallback if the bundle probe did not find a face
            // texture. Normally the probe selects only face IDs that exist in this build.
            clothes.face = availableFaces.Count > 0
                ? availableFaces[random.Next(availableFaces.Count)]
                : (byte)0;
            if (availableHair.Count > 0)
                clothes.hair = availableHair[random.Next(availableHair.Count)];
            if (availableBeards.Count > 0)
                clothes.beard = availableBeards[random.Next(availableBeards.Count)];

            Color skin = SkinPalette[random.Next(SkinPalette.Length)];
            clothes.skin = skin;
            Color hair = HairPalette[random.Next(HairPalette.Length)];
            clothes.color = hair;
            clothes.BeardColor = hair;

            // HumanClothes owns the native material and mesh replacement logic. Applying
            // it here marks face/skin/hair dirty and makes the chosen appearance visible.
            clothes.apply();

            Debug.Log("[MobileNPCAppearance] Applied native face/hair variation to " + name
                + " (face=" + clothes.face + ", hair=" + clothes.hair + ", beard=" + clothes.beard + ").", this);
        }

        private static void ScanAvailableAssets()
        {
            if (hasScannedAssets)
                return;

            if (Assets.coreMasterBundle == null)
            {
                Debug.LogWarning("[MobileNPCAppearance] Core asset bundle is unavailable; NPCs will retain default face assets.");
                return;
            }
            hasScannedAssets = true;

            // Probe the actual loaded bundle instead of assuming every numeric index exists.
            for (byte index = 0; index < 32; ++index)
            {
                string facePath = "Items/Faces/" + index + "/Texture.png";
                if (Assets.coreMasterBundle.LoadAsset<Texture2D>(facePath) != null)
                    availableFaces.Add(index);

                string hairPath = "Items/Hairs/" + index + "/Hair.prefab";
                if (Assets.coreMasterBundle.LoadAsset<GameObject>(hairPath) != null)
                    availableHair.Add(index);

                string beardPath = "Items/Beards/" + index + "/Beard.prefab";
                if (Assets.coreMasterBundle.LoadAsset<GameObject>(beardPath) != null)
                    availableBeards.Add(index);
            }

            Debug.Log("[MobileNPCAppearance] Available native variants: faces=" + availableFaces.Count
                + ", hair=" + availableHair.Count + ", beards=" + availableBeards.Count + ".");
        }
    }
}

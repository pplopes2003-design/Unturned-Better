////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Unturned Better mobile NPC source integration.             //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// A-Life profession presets are strategic defaults, NOT a second tactical brain.
    /// Role and faction remain the sole combat/reputation authorities and are already
    /// serialized by MobileNPCPersistence. A preset can be reconstructed from faction
    /// after loading a saved NPC, with no incompatible save schema.
    /// </summary>
    internal static class MobileNPCArchetypePolicy
    {
        internal enum Preset { Raider, Trader, Survivor, Military }

        internal static Preset FromIdentity(MobileNPCFaction.Id faction,
            MobileNPCHumanoidDisposition.Role role)
        {
            switch (faction)
            {
                case MobileNPCFaction.Id.Military: return Preset.Military;
                case MobileNPCFaction.Id.Raiders: return Preset.Raider;
                case MobileNPCFaction.Id.Traders: return Preset.Trader;
                case MobileNPCFaction.Id.Scavengers:
                case MobileNPCFaction.Id.Survivors: return Preset.Survivor;
                default:
                    return role == MobileNPCHumanoidDisposition.Role.Hostile ? Preset.Raider
                        : role == MobileNPCHumanoidDisposition.Role.Friendly ? Preset.Trader
                        : Preset.Survivor;
            }
        }

        internal static Preset ForNPC(GameObject npc)
        {
            if (npc == null)
                return Preset.Survivor;
            MobileNPCFaction faction = npc.GetComponent<MobileNPCFaction>();
            MobileNPCHumanoidDisposition disposition =
                npc.GetComponent<MobileNPCHumanoidDisposition>();
            return FromIdentity(faction != null ? faction.CurrentFaction
                : MobileNPCFaction.Id.Unaffiliated,
                disposition != null ? disposition.CurrentRole
                : MobileNPCHumanoidDisposition.Role.Neutral);
        }

        internal static MobileNPCHumanoidDisposition.Role RoleOf(Preset preset)
        {
            switch (preset)
            {
                case Preset.Raider:
                case Preset.Military: return MobileNPCHumanoidDisposition.Role.Hostile;
                case Preset.Trader: return MobileNPCHumanoidDisposition.Role.Friendly;
                default: return MobileNPCHumanoidDisposition.Role.Neutral;
            }
        }

        internal static MobileNPCFaction.Id FactionOf(Preset preset)
        {
            switch (preset)
            {
                case Preset.Raider: return MobileNPCFaction.Id.Raiders;
                case Preset.Military: return MobileNPCFaction.Id.Military;
                case Preset.Trader: return MobileNPCFaction.Id.Traders;
                default: return MobileNPCFaction.Id.Scavengers;
            }
        }

        /// <summary>
        /// No independent clock or artificial NPC sightings: LightingManager
        /// describes the actual map's current day/night lighting. Weighting is
        /// applied at each natural spawn, never teleporting an existing actor.
        /// </summary>
        internal static Preset SelectForNewPopulationServer()
        {
            int roll = Random.Range(0, 100);
            if (MobileNPCSettings.CombatMode)
                return roll < 50 ? Preset.Military : Preset.Raider;

            int raiderWeight;
            int traderWeight;
            bool night = Level.isLoaded && LightingManager.isNighttime;
            switch (MobileNPCSettings.CurrentPopulationMix)
            {
                case MobileNPCSettings.PopulationMix.HostileHeavy:
                    raiderWeight = night ? 75 : 62;
                    traderWeight = 12;
                    break;
                case MobileNPCSettings.PopulationMix.Peaceful:
                    raiderWeight = night ? 27 : 13;
                    traderWeight = 34;
                    break;
                default:
                    raiderWeight = night ? 48 : 32;
                    traderWeight = 25;
                    break;
            }
            if (roll < raiderWeight) return Preset.Raider;
            if (roll < raiderWeight + traderWeight) return Preset.Trader;
            return Preset.Survivor;
        }

        /// <summary>Apply a preset after a physically validated spawn. Call before
        /// optionally joining a surviving group. This does not mint equipment,
        /// spawn vehicles, or grant knowledge of hidden loot or the player.</summary>
        internal static void ApplyToSpawnedNPCServer(GameObject npc, Preset preset)
        {
            if (!Provider.isServer || npc == null)
                return;
            MobileNPCHumanoidDisposition disposition =
                npc.GetComponent<MobileNPCHumanoidDisposition>();
            MobileNPCFaction faction = npc.GetComponent<MobileNPCFaction>();
            if (disposition != null && disposition.CurrentRole != RoleOf(preset))
                disposition.SetRoleServer(RoleOf(preset));
            if (faction != null && faction.CurrentFaction != FactionOf(preset))
                faction.SetFactionServer(FactionOf(preset));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCPreset] Spawned " + preset + " faction="
                + (faction != null ? faction.CurrentFaction.ToString() : "missing")
                + " mode=" + MobileNPCSettings.CurrentGameMode
                + " night=" + (Level.isLoaded && LightingManager.isNighttime)
                + " actor=" + npc.name, npc);
#endif
        }
    }
}

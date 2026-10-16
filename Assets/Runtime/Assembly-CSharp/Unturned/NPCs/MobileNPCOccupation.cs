////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Unturned Better mobile NPC source integration.             //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// A personal OCCUPATION, independent of the NPC's diplomatic faction.
    /// The preset is a behavioral tendency, not a scripted task or a combat
    /// target. It can change during the NPC's life without resetting group
    /// identity, personal memory, inventory, or faction relationships.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCOccupation : MonoBehaviour
    {
        [SerializeField] private bool occupationAssigned;
        [SerializeField] private int occupationIndex;

        internal bool HasAssignedOccupation => occupationAssigned;

        internal MobileNPCArchetypePolicy.Preset AssignedOccupation =>
            (MobileNPCArchetypePolicy.Preset)occupationIndex;

        internal bool SetOccupationServer(MobileNPCArchetypePolicy.Preset occupation)
        {
            if (!Provider.isServer || (int)occupation < 0
                || (int)occupation > (int)MobileNPCArchetypePolicy.Preset.Military)
                return false;
            bool changed = !occupationAssigned || occupationIndex != (int)occupation;
            occupationAssigned = true;
            occupationIndex = (int)occupation;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (changed)
                Debug.Log("[MobileNPCOccupation] occupation=" + occupation
                    + " npc=" + name + "; faction and role preserved.", this);
#endif
            return changed;
        }
    }
}

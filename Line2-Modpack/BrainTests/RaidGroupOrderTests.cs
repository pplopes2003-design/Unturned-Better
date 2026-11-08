using System;
using Unturned2.Line2;
using UnityEngine;

internal static class RaidGroupOrderTests
{
    private static bool May(bool authorized = true, bool same = true,
        bool lineOfSight = true, bool armed = true, bool enemy = false,
        bool defensive = false, bool recruited = false,
        bool grounded = true, float health = 0.72f,
        float teammateDistance = 6f, float elevation = 0f,
        float rallyDistance = 17f, int alreadyAssigned = 0)
    {
        Vector3 leader = new Vector3(0f, 0f, 0f);
        Vector3 ally = new Vector3(teammateDistance, elevation, 0f);
        Vector3 rally = ally + new Vector3(rallyDistance, 0f, 0f);
        return Line2NpcRaidGroupOrderPolicy.MayFollow(
            authorized, same, lineOfSight, armed, enemy, defensive,
            recruited, grounded, health, leader, ally,
            rally, alreadyAssigned);
    }

    internal static bool Run()
    {
        if (!May() ||
            May(authorized:false) ||
            May(same:false) ||
            May(lineOfSight:false) ||
            May(armed:false) ||
            May(enemy:true) ||
            May(defensive:true) ||
            May(recruited:true) ||
            May(grounded:false) ||
            May(health:0.39f) ||
            May(health:float.NaN) ||
            May(teammateDistance:14.01f) ||
            May(teammateDistance:0.1f) ||
            May(elevation:2f) ||
            May(rallyDistance:42f) ||
            May(rallyDistance:1f) ||
            May(alreadyAssigned:2) ||
            May(alreadyAssigned:-1))
            return false;
        Console.WriteLine("NPC_RAID_GROUP_ORDER_OK: first-hand 14m speech, same faction, finite arms, safe physical rally, no active combat and only two followers.");
        return true;
    }
}

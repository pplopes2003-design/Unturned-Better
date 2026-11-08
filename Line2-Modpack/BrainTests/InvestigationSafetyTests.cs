using System;
using Unturned2.Line2;
using UnityEngine;

internal static class InvestigationSafetyTests
{
    internal static bool Run()
    {
        var lastSeen = new Vector3(12f, 0f, 10f);
        var current = new Vector3(0f, 0f, 0f);
        var floor = new Vector3(14f, 0f, 9f);
        if (!Line2NpcInvestigationPolicy.MayProbe(
                true, true, false, .58f, 6f, lastSeen, floor,
                current, 15f) ||
            Line2NpcInvestigationPolicy.MayProbe(
                true, false, false, .58f, 6f, lastSeen, floor,
                current, 15f) ||
            Line2NpcInvestigationPolicy.MayProbe(
                true, true, true, .58f, 6f, lastSeen, floor,
                current, 15f) ||
            Line2NpcInvestigationPolicy.MayProbe(
                false, true, false, .58f, 6f, lastSeen, floor,
                current, 15f) ||
            Line2NpcInvestigationPolicy.MayProbe(
                true, true, false, .08f, 6f, lastSeen, floor,
                current, 15f) ||
            Line2NpcInvestigationPolicy.MayProbe(
                true, true, false, .58f, 17f, lastSeen, floor,
                current, 15f) ||
            Line2NpcInvestigationPolicy.MayProbe(
                true, true, false, .58f, 6f, lastSeen,
                new Vector3(14f, 6f, 9f), current, 15f) ||
            Line2NpcInvestigationPolicy.MayProbe(
                true, true, false, .58f, 6f, lastSeen,
                new Vector3(40f, 0f, 9f), current, 15f) ||
            Line2NpcInvestigationPolicy.MayProbe(
                true, true, false, .58f, 6f, lastSeen,
                new Vector3(float.NaN, 0f, 9f), current, 15f))
            return false;

        if (!Line2NpcAmbushPolicy.CanPlan(
                true, true, false, false, true, true, .7f, 5f, 16f) ||
            Line2NpcAmbushPolicy.CanPlan(
                false, true, false, false, true, true, .7f, 5f, 16f) ||
            Line2NpcAmbushPolicy.CanPlan(
                true, true, true, false, true, true, .7f, 5f, 16f) ||
            Line2NpcAmbushPolicy.CanPlan(
                true, true, false, true, true, true, .7f, 5f, 16f) ||
            Line2NpcAmbushPolicy.CanPlan(
                true, true, false, false, false, true, .7f, 5f, 16f) ||
            Line2NpcAmbushPolicy.CanPlan(
                true, true, false, false, true, true, .2f, 5f, 16f) ||
            Line2NpcAmbushPolicy.CanPlan(
                true, true, false, false, true, true, .7f, 14f, 16f) ||
            Line2NpcAmbushPolicy.CanPlan(
                true, true, false, false, true, true, .7f, 5f, 31f))
            return false;

        if (!Line2NpcAmbushPolicy.WithholdPreparedAttack(
                true, true, 5f, 5.7f) ||
            Line2NpcAmbushPolicy.WithholdPreparedAttack(
                false, true, 5f, 5.7f) ||
            Line2NpcAmbushPolicy.WithholdPreparedAttack(
                true, false, 5f, 5.7f) ||
            Line2NpcAmbushPolicy.WithholdPreparedAttack(
                true, true, 5f, 6.2f) ||
            Line2NpcAmbushPolicy.WithholdPreparedAttack(
                true, true, 6f, 5.7f) ||
            Line2NpcAmbushPolicy.WithholdPreparedAttack(
                true, true, float.NaN, 5.7f))
            return false;
        Console.WriteLine("NPC_SEARCH_AMBUSH_POLICY_OK: sensory bounded floor, no hidden-target attack, short prepared fire only.");
        return true;
    }
}

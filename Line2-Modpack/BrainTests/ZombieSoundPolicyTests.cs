using System;
using UnityEngine;
using Unturned2.Line2;

internal static class ZombieSoundPolicyTests
{
    internal static bool Run()
    {
        Vector3 zombie = new Vector3(0f, 0f, 0f);
        Vector3 gunshot = new Vector3(30f, 0f, 0f);
        if (!Line2ZombieSoundPolicy.MayInvestigate(
                true, true, true, false, false, true,
                zombie, gunshot, 20f, 0f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                false, true, true, false, false, true,
                zombie, gunshot, 20f, 0f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                true, false, true, false, false, true,
                zombie, gunshot, 20f, 0f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                true, true, true, true, false, true,
                zombie, gunshot, 20f, 0f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                true, true, true, false, true, true,
                zombie, gunshot, 20f, 0f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                true, true, true, false, false, false,
                zombie, gunshot, 20f, 0f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                true, true, true, false, false, true,
                zombie, new Vector3(70f, 0f, 0f), 20f, 0f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                true, true, true, false, false, true,
                zombie, new Vector3(30f, 7f, 0f), 20f, 0f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                true, true, true, false, false, true,
                zombie, gunshot, 20f, 19f) ||
            Line2ZombieSoundPolicy.MayInvestigate(
                true, true, true, false, false, true,
                zombie, new Vector3(float.NaN, 0f, 0f), 20f, 0f))
            return false;

        Vector3 alternate = new Vector3(2.8f, 0f, 0f);
        if (!Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, false, true, true,
                zombie, gunshot, alternate, 5.1f, 0) ||
            Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, true, true, true,
                zombie, gunshot, alternate, 5.1f, 0) ||
            Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, false, false, true,
                zombie, gunshot, alternate, 5.1f, 0) ||
            Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, false, true, false,
                zombie, gunshot, alternate, 5.1f, 0) ||
            Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, false, true, true,
                zombie, gunshot, alternate, 4.9f, 0) ||
            Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, false, true, true,
                zombie, gunshot, alternate, 6f, 2) ||
            Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, false, true, true,
                zombie, new Vector3(1f, 0f, 0f),
                alternate, 6f, 0) ||
            Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, false, true, true,
                zombie, gunshot,
                new Vector3(2.8f, 7f, 0f), 6f, 0) ||
            Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                true, true, false, true, true,
                zombie, gunshot,
                new Vector3(float.NaN, 0f, 0f), 6f, 0))
            return false;
        Console.WriteLine(
            "ZOMBIE_REAL_SOUND_SEARCH_POLICY_OK: verified discharge, 67m/vertical bound, no native player override, short corridor and finite stuck-route revisions.");
        return true;
    }
}

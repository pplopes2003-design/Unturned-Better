////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
namespace SDG.Unturned
{
    /// <summary>Use real vanilla damage/asset fields, never a magic "RPG" ID.
    /// The NPC must still physically acquire and consume each item.</summary>
    internal static class MobileNPCRaidSupplyPolicy
    {
        internal static bool IsBreachThrowable(ItemAsset asset)
        {
            ItemThrowableAsset explosive = asset as ItemThrowableAsset;
            return explosive != null && explosive.isExplosive && !explosive.isSticky
                && !explosive.explodeOnImpact && explosive.throwable != null
                && explosive.fuseLength >= 1.5f && explosive.fuseLength <= 5f
                && explosive.range > 1f
                && (explosive.barricadeDamage > 0f || explosive.structureDamage > 0f);
        }

        internal static bool IsRaidLauncher(ItemAsset asset)
        {
            ItemGunAsset gun = asset as ItemGunAsset;
            return gun != null && gun.projectile != null
                && (gun.barricadeDamage > 0f || gun.structureDamage > 0f)
                && gun.ammoPerShot > 0 && gun.magazineCalibers != null
                && gun.magazineCalibers.Length > 0;
        }

        internal static bool IsRaidCharge(ItemAsset asset)
        {
            ItemBarricadeAsset charge = asset as ItemBarricadeAsset;
            return charge != null && charge.build == EBuild.CHARGE;
        }

        internal static bool IsBreachSupply(ItemAsset asset)
        {
            return IsBreachThrowable(asset) || IsRaidLauncher(asset)
                || IsRaidCharge(asset);
        }
    }
}

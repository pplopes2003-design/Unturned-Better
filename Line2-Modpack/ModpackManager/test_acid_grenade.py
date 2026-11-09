"""Fixture tests for the rare Thrower acid grenade staging generator."""
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from asset_diagnostics import parse_dat
from build_acid_grenade import build_acid_grenade


class AcidGrenadeTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.game = root / "Unturned"
        self.game.mkdir()
        (self.game / "Unturned.exe").write_bytes(b"")
        base = self.game / "Bundles"
        grenade = base / "Items" / "Throwables" / "Grenade"
        grenade.mkdir(parents=True)
        (grenade / "Asset.dat").write_text(
            "Type Throwable\nID 254\nGUID 11111111111111111111111111111111\n"
            "Explosive\nRange 8\nPlayer_Damage 175\nZombie_Damage 175\n"
            "Explosion 34\nFuse_Length 2.5\n", encoding="utf-8")
        effect = base / "Effects" / "Zombie_3"
        effect.mkdir(parents=True)
        (effect / "Asset.dat").write_text(
            "Type Effect\nID 991\nGUID 33333333333333333333333333333333\n",
            encoding="utf-8")
        table = base / "Spawns" / "Washington_Special_America"
        table.mkdir(parents=True)
        (table / "Asset.dat").write_text(
            "Type Spawn\nID 346\nGUID 44444444444444444444444444444444\n",
            encoding="utf-8")
        self.out = root / "ThrowerAcidGrenade"

    def test_reuses_thrower_effect_and_native_model(self):
        result = build_acid_grenade(self.game, self.out)
        asset = parse_dat(self.out / "Items/Throwables/Thrower_Acid_Grenade/Asset.dat")
        loot = parse_dat(self.out / "Spawns/ThrowerAcidRareLoot/Asset.dat")
        self.assertEqual(result["grenade_id"], 32615)
        self.assertEqual(asset["type"], "Throwable")
        self.assertEqual(asset["rarity"], "Epic")
        self.assertEqual(asset["range"], "3.0")
        self.assertEqual(asset["player_damage"], "18")
        self.assertEqual(asset["zombie_damage"], "24")
        self.assertEqual(asset["structure_damage"], "0")
        self.assertEqual(asset["explosion"], "ae477aac40b64d3c8ce8e538daffecf5")
        self.assertEqual(asset["bundle_override_path"], "/Items/Throwables/Grenade")
        self.assertEqual(loot["table_0_asset_id"], "32615")
        self.assertEqual(loot["root_0_spawn_id"], "346")
        self.assertEqual(loot["root_0_weight"], "1")

    def test_effect_reference_does_not_require_unpacked_dat(self):
        import shutil
        shutil.rmtree(self.game / "Bundles" / "Effects")
        result = build_acid_grenade(self.game, self.out)
        asset = parse_dat(self.out / "Items/Throwables/Thrower_Acid_Grenade/Asset.dat")
        self.assertEqual(result["effect"], "ae477aac40b64d3c8ce8e538daffecf5")
        self.assertEqual(asset["explosion"], result["effect"])

    def test_refuses_unverified_spawn_root(self):
        import shutil
        shutil.rmtree(self.game / "Bundles" / "Spawns")
        with self.assertRaisesRegex(ValueError, "spawn root"):
            build_acid_grenade(self.game, self.out)
        self.assertFalse(self.out.exists())

    def test_refuses_id_collision(self):
        external = self.game.parent / "workshop"
        external.mkdir()
        (external / "Other.dat").write_text(
            "Type Throwable\nID 32615\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "ID collision"):
            build_acid_grenade(self.game, self.out, extra_roots=[external])
        self.assertFalse(self.out.exists())


if __name__ == "__main__":
    unittest.main()

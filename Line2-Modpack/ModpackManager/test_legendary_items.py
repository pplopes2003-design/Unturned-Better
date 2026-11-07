"""Legendary pack and Auto24 upgrade fixtures, no Steam installation required."""
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from asset_diagnostics import parse_dat
from build_legendary_items import build_legendaries
from build_auto32 import create_auto32
from upgrade_auto24 import upgrade_staging_auto24


class LegendaryItemTests(unittest.TestCase):
    def setUp(self):
        self.temp=TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root=Path(self.temp.name)
        self.game=root/"Unturned"
        self.game.mkdir()
        (self.game/"Unturned.exe").write_bytes(b"")
        for folder,content in (
            ("Guns/Eaglefire", "Type Gun\nID 4\nGUID 11111111111111111111111111111111\n"
             "Action Trigger\nBursts 3\nSemi\nCaliber 1\nMagazine 6\n"),
            ("Guns/Bluntforce", "Type Gun\nID 112\nGUID 22222222222222222222222222222222\n"
             "Action Pump\nMagazine 113\nAmmo_Max 8\nCaliber 8\n"),
            ("Ammo/12_Shells", "Type Magazine\nID 113\nGUID 33333333333333333333333333333333\n"
             "Calibers 1\nCaliber_0 8\nAmount 8\nPellets 8\n")
        ):
            p=self.game/"Bundles"/"Items"/folder
            p.mkdir(parents=True)
            (p/"Asset.dat").write_text(content, encoding="utf-8")
        self.output=root/"LegendaryPack"
        self.auto=root/"Auto32"

    def test_legendary_eagle_and_incendiary_compatibility(self):
        build_legendaries(self.game,self.output)
        eagle=parse_dat(self.output/"Items/Guns/Legendary_Eaglefire/Asset.dat")
        shell=parse_dat(self.output/"Items/Ammo/Legendary_Incendiary_12Gauge/Asset.dat")
        self.assertEqual(eagle["id"],"32612")
        self.assertEqual(eagle["rarity"],"Legendary")
        self.assertEqual(eagle["bursts"],"3")
        self.assertIn("auto",eagle)
        self.assertEqual(shell["id"],"32613")
        self.assertEqual(shell["rarity"],"Legendary")
        self.assertEqual(shell["caliber_0"],"8")
        self.assertEqual(shell["pellets"],"1")
        self.assertEqual(shell["range"],"1.25")
        self.assertIn("explosive",shell)
        loot=parse_dat(self.output/"Spawns/LegendaryLootPool/Asset.dat")
        self.assertEqual(loot["type"],"Spawn")
        self.assertEqual(loot["id"],"32614")
        self.assertEqual(loot["table_0_asset_id"],"32610")
        self.assertEqual(loot["table_1_asset_id"],"32612")
        self.assertEqual(loot["table_2_asset_id"],"32613")

    def test_washington_root_only_if_verified(self):
        build_legendaries(self.game,self.output)
        base=parse_dat(self.output/"Spawns/LegendaryLootPool/Asset.dat")
        self.assertNotIn("roots",base)

    def test_washington_root_attaches_with_low_weight(self):
        folder=self.game/"Bundles"/"Spawns"/"Washington_Special_America"
        folder.mkdir(parents=True)
        (folder/"Asset.dat").write_text("Type Spawn\\nID 346\\nGUID aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\\n")
        result=build_legendaries(self.game,self.output)
        loot=parse_dat(self.output/"Spawns/LegendaryLootPool/Asset.dat")
        self.assertTrue(result["washington_root_attached"])
        self.assertEqual(loot["roots"],"1")
        self.assertEqual(loot["root_0_spawn_id"],"346")
        self.assertEqual(loot["root_0_weight"],"1")
        self.assertNotIn("root_0_override",loot)

    def test_id_collision_rejected(self):
        conflict=self.game.parent/"workshop"/"bad"
        conflict.mkdir(parents=True)
        (conflict/"Asset.dat").write_text("Type Magazine\nID 32613\n")
        with self.assertRaises(ValueError):
            build_legendaries(self.game,self.output,extra_roots=[conflict])
        self.assertFalse(self.output.exists())

    def test_loot_table_id_conflict(self):
        conflict=self.game/"Bundles"/"Spawns"/"Existing"
        conflict.mkdir(parents=True)
        (conflict/"Asset.dat").write_text("Type Spawn\nID 32614\n")
        with self.assertRaises(ValueError):
            build_legendaries(self.game,self.output)
        self.assertFalse(self.output.exists())

    def test_auto24_keeps_original_identity(self):
        create_auto32(self.game,self.auto)
        gun=self.auto/"Items/Guns/Auto_32/Asset.dat"
        mag=self.auto/"Items/Guns/Auto_32_Magazine/Asset.dat"
        old_guid=parse_dat(gun)["guid"]
        upgrade_auto24(self.auto)
        gun_data=parse_dat(gun); mag_data=parse_dat(mag)
        self.assertEqual(gun_data["id"],"32610")
        self.assertEqual(gun_data["guid"],old_guid)
        self.assertEqual(gun_data["ammo_max"],"24")
        self.assertEqual(mag_data["amount"],"24")
        self.assertEqual(gun_data["rarity"],"Legendary")
        self.assertEqual(mag_data["rarity"],"Legendary")

if __name__=="__main__":
    unittest.main()

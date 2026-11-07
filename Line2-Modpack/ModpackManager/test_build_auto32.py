"""Offline Auto32 generation tests. Fake vanilla files, never touch Steam."""
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from asset_diagnostics import parse_dat
from build_auto32 import create_auto32


class Auto32BuilderTests(unittest.TestCase):
    def setUp(self):
        self.tmp = TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        base = Path(self.tmp.name)
        self.game = base / "Unturned"
        self.game.mkdir()
        (self.game / "Unturned.exe").write_bytes(b"")
        gun = self.game / "Bundles" / "Items" / "Guns" / "Bluntforce"
        gun.mkdir(parents=True)
        (gun / "Asset.dat").write_text(
            "Type Gun\nID 112\nGUID 11111111111111111111111111111111\n"
            "Action Pump\nSemi\nMagazine 113\nAmmo_Max 8\nCaliber 8\n",
            encoding="utf-8")
        mag = self.game / "Bundles" / "Items" / "Ammo" / "12_Gauge_Shells"
        mag.mkdir(parents=True)
        (mag / "Asset.dat").write_text(
            "Type Magazine\nID 113\nGUID 22222222222222222222222222222222\n"
            "Amount 8\nCalibers 1\nCaliber_0 8\n",
            encoding="utf-8")
        self.output = base / "staging" / "Auto32"

    def test_gun_and_magazine_references(self):
        result = create_auto32(self.game, self.output)
        gun = parse_dat(self.output / "Items" / "Guns" / "Auto_32" / "Asset.dat")
        mag = parse_dat(self.output / "Items" / "Guns" / "Auto_32_Magazine" / "Asset.dat")
        self.assertEqual(gun["id"], "32610")
        self.assertEqual(gun["action"], "Trigger")
        self.assertEqual(gun["magazine"], "32611")
        self.assertEqual(gun["ammo_max"], "32")
        self.assertEqual(gun["recoil_min_x"], "-0.7")
        self.assertEqual(gun["recoil_max_y"], "1.8")
        self.assertEqual(gun["aiming_recoil_multiplier"], "0.8")
        self.assertEqual(mag["amount"], "32")
        self.assertEqual(gun["bundle_override_path"], "/Items/Guns/Bluntforce")
        self.assertEqual(mag["bundle_override_path"], "/Items/Ammo/12_Gauge_Shells")
        self.assertNotEqual(gun["guid"], mag["guid"])
        self.assertTrue(result["output"].endswith("Auto32"))
        self.assertTrue((self.output / "BUILD_NOTES.txt").exists())

    def test_collision_blocks_generation(self):
        extra = self.game.parent / "workshop-content"
        extra.mkdir()
        file = extra / "SomeWeapon"
        file.mkdir()
        (file / "Asset.dat").write_text("Type Gun\nID 32610\n")
        with self.assertRaises(ValueError):
            create_auto32(self.game, self.output, other_roots=[extra])
        self.assertFalse(self.output.exists())

    def test_custom_ids(self):
        create_auto32(self.game, self.output, gun_id=32320, magazine_id=32321)
        gun = parse_dat(self.output / "Items" / "Guns" / "Auto_32" / "Asset.dat")
        self.assertEqual(gun["id"], "32320")

    def test_output_not_overwritten(self):
        self.output.mkdir(parents=True)
        with self.assertRaises(FileExistsError):
            create_auto32(self.game, self.output)

    def test_invalid_equal_ids(self):
        with self.assertRaises(ValueError):
            create_auto32(self.game, self.output, gun_id=32610, magazine_id=32610)


if __name__ == "__main__":
    unittest.main()

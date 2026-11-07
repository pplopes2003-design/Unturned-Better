"""Recoil tuning regression tests, no Unturned installation required."""
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from asset_diagnostics import parse_dat
from auto32_recoil import tune_text, tune_staged_auto32, PRESETS


class RecoilTests(unittest.TestCase):
    def test_replaces_all_existing_recoil_fields_without_duplicates(self):
        data = ("Type Gun\nID 32610\nRecoil_Min_X -20\n"
                "Recoil_Max_X 20\nRecoil_Min_Y 30\nRecoil_Max_Y 40\n"
                "Recover_Y 0.1\nAiming_Recoil_Multiplier 1\n"
                "GUID 1234567890abcdef1234567890abcdef\n")
        output = tune_text(data, "balanced")
        fields = parse_dat_from_text(output)
        self.assertEqual(fields["recoil_min_x"], "-0.7")
        self.assertEqual(fields["recoil_max_y"], "1.8")
        self.assertEqual(fields["recover_y"], "0.55")
        self.assertEqual(fields["id"], "32610")
        for key in ("Recoil_Min_X", "Recoil_Max_X", "Recoil_Min_Y", "Recoil_Max_Y",
                    "Recover_X", "Recover_Y", "Aiming_Recoil_Multiplier"):
            self.assertEqual(sum(1 for line in output.splitlines()
                                 if line.startswith(key + " ")), 1)

    def test_staging_only(self):
        with TemporaryDirectory() as temp:
            asset = Path(temp) / "Auto32" / "Items" / "Guns" / "Auto_32" / "Asset.dat"
            asset.parent.mkdir(parents=True)
            asset.write_text("Type Gun\nID 32610\nRecoil_Max_Y 15\n")
            result = tune_staged_auto32(Path(temp) / "Auto32", "soft")
            self.assertEqual(result["preset"], "soft")
            self.assertEqual(parse_dat(asset)["recoil_max_y"], "1.1")
            self.assertEqual(tune_staged_auto32(Path(temp) / "Auto32", "soft")["id"], "32610")

    def test_invalid_presets_are_rejected(self):
        with self.assertRaises(ValueError):
            tune_text("Type Gun\n", "none")

    def test_presets_ordered_by_kick(self):
        self.assertLess(PRESETS["soft"][3], PRESETS["balanced"][3])
        self.assertLess(PRESETS["balanced"][3], PRESETS["strong"][3])


def parse_dat_from_text(text):
    result = {}
    for line in text.splitlines():
        pieces = line.split(maxsplit=1)
        if len(pieces) == 2:
            result[pieces[0].lower()] = pieces[1]
    return result


if __name__ == "__main__":
    unittest.main()

"""Asset diagnostics tests using isolated temporary Unturned-style definitions."""
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from asset_diagnostics import inspect

class AssetDiagnosticTests(unittest.TestCase):
    def setUp(self):
        self.tmp=TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name)
    def put(self,folder,body,name="Asset.dat"):
        dest=self.root/folder
        dest.mkdir(parents=True,exist_ok=True)
        (dest/name).write_text(body,encoding="utf-8")
    def test_missing_weapon(self):
        r=inspect([self.root])
        self.assertEqual(r["candidate_count"],0)
    def test_detect_missing_magazine_and_guid(self):
        self.put("Items/Auto_32","Type Gun\nID 32610\nMagazine 32611\n")
        r=inspect([self.root])
        self.assertEqual(r["candidate_count"],1)
        self.assertIn("missing_or_invalid_guid",r["candidates"][0]["issues"])
        self.assertIn("referenced_magazine_id_not_found_in_scanned_roots",r["candidates"][0]["issues"])
    def test_valid_identification(self):
        self.put("Items/Auto_32","Type Gun\nID 32610\nGUID 11111111111111111111111111111111\nMagazine 32611\n")
        self.put("Items/Auto_32_Mag","Type Magazine\nID 32611\nGUID 22222222222222222222222222222222\n")
        r=inspect([self.root])
        self.assertEqual(r["candidate_count"],2)
        gun=next(x for x in r["candidates"] if x["id"]=="32610")
        self.assertFalse(gun["issues"])
    def test_id_lookup(self):
        self.put("Items/Unknown","Type Gun\nID 32610\nGUID 11111111111111111111111111111111\n")
        r=inspect([self.root],name="Auto 32",expected_id=32610)
        self.assertEqual(r["candidate_count"],1)

if __name__=="__main__":
    unittest.main()

"""Regression tests with temporary fake Steam/Unturned trees; do not touch actual game."""
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from manager import Manager


class ManagerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        base = Path(self.tmp.name)
        self.game = base / "steamapps" / "common" / "Unturned"
        self.game.mkdir(parents=True)
        (self.game / "Unturned.exe").write_bytes(b"")
        self.workshop = base / "steamapps" / "workshop" / "content" / "304930"
        (self.workshop / "1234567890").mkdir(parents=True)
        (self.workshop / "1234567890" / "Asset.dat").write_text("ID 1", encoding="utf-8")
        self.mgr = Manager(self.game, base / "manager-data")
        self.source = base / "sample-mod"
        self.source.mkdir()
        (self.source / "Test.dat").write_text("Type Item\nID 9000", encoding="utf-8")

    def test_workshop_discovery(self):
        self.assertEqual([x["id"] for x in self.mgr.workshop_items()], ["1234567890"])

    def test_deploy_and_restore_without_touching_sources(self):
        self.mgr.import_local(self.source, "MyMod")
        plan = self.mgr.deploy("MyMod", "Content")
        destination = Path(plan["destination"])
        self.assertFalse(destination.exists(), "dry run must not write to game")
        self.mgr.deploy("MyMod", "Content", confirm=True)
        self.assertTrue((destination / "Test.dat").exists())
        self.assertTrue((self.source / "Test.dat").exists())
        removed = self.mgr.remove("MyMod", "Content", confirm=True)
        self.assertFalse(destination.exists())
        self.assertTrue(Path(removed["recovery"]).exists())

    def test_refuse_modified_installation(self):
        self.mgr.import_local(self.source, "MyMod")
        self.mgr.deploy("MyMod", "Maps", confirm=True)
        deployed = self.game / "Bundles" / "Workshop" / "Maps" / "U2M_MyMod" / "Test.dat"
        deployed.write_text("unexpected change", encoding="utf-8")
        with self.assertRaises(ValueError):
            self.mgr.remove("MyMod", "Maps", confirm=True)

    def test_export_server_profile(self):
        self.mgr.import_local(self.source, "MyMod")
        self.mgr.save_profile("friends", ["MyMod"], ["1234567890"])
        path = self.game.parent / "WorkshopDownloadConfig.json"
        self.mgr.export_server("friends", path)
        config = json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual(config["File_IDs"], [1234567890])

    def test_conflicting_ids_block_activation(self):
        other = self.source.parent / "another"
        other.mkdir()
        (other / "Second.dat").write_text("Type Item\\nID 9000\\nGUID abcdef0123456789abcdef0123456789", encoding="utf-8")
        self.mgr.import_local(self.source, "One")
        self.mgr.import_local(other, "Two")
        self.mgr.save_profile("collision", ["One", "Two"], [])
        report = self.mgr.analyze_conflicts("collision")
        self.assertTrue(any(c["kind"] == "legacy_id" for c in report["conflicts"]))
        with self.assertRaises(ValueError):
            self.mgr.apply_profile("collision", confirm=True)
        self.assertFalse((self.game / "Bundles" / "Workshop" / "Content" / "U2M_One").exists())

    def test_switch_profiles_reversibly(self):
        self.mgr.import_local(self.source, "One")
        self.mgr.save_profile("with-mod", ["One"], [])
        self.mgr.save_profile("without-mod", [], [])
        plan = self.mgr.apply_profile("with-mod")
        self.assertEqual(plan["add"], ["Content/One"])
        self.mgr.apply_profile("with-mod", confirm=True)
        deployed = self.game / "Bundles" / "Workshop" / "Content" / "U2M_One"
        self.assertTrue(deployed.exists())
        self.mgr.apply_profile("without-mod", confirm=True)
        self.assertFalse(deployed.exists())
        self.assertEqual(self.mgr.profile_state()["active"], "without-mod")
        self.assertTrue(any((self.mgr.data / "recovery").iterdir()))

    def test_reject_unsafe_names(self):
        with self.assertRaises(ValueError):
            self.mgr.import_local(self.source, "../outside")

if __name__ == "__main__":
    unittest.main()

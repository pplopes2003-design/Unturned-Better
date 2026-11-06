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

    def make_module(self, folder, name="U2Test", deps=None):
        import json
        folder.mkdir(parents=True, exist_ok=True)
        (folder / "Test.dll").write_bytes(b"MZ dummy for filesystem unit test")
        cfg = {"Name": name, "Version": "1.0.0.0", "IsEnabled": True,
               "Assemblies": [{"Path": "/Test.dll", "Role": "Client"}],
               "Dependencies": deps or []}
        (folder / "Test.module").write_text(json.dumps(cfg), encoding="utf-8")

    def test_module_installer_and_safe_removal(self):
        from module_support import install_module, remove_module
        folder = self.source.parent / "fake-module"
        self.make_module(folder)
        self.mgr.import_local(folder, "MyModule")
        plan = install_module(self.mgr, "MyModule")
        self.assertEqual(plan["issues"], [])
        self.assertFalse(Path(plan["destination"]).exists())
        install_module(self.mgr, "MyModule", confirm=True)
        target = self.game / "Modules" / "U2M_MyModule"
        self.assertTrue((target / "Test.module").is_file())
        removed = remove_module(self.mgr, "MyModule", confirm=True)
        self.assertFalse(target.exists())
        self.assertTrue(Path(removed["recovery"]).is_dir())

    def test_module_rejects_path_traversal_and_dependencies(self):
        import json
        from module_support import validate_package, install_module
        folder = self.source.parent / "invalid-module"
        self.make_module(folder, deps=[{"Name":"MissingLibrary", "Version":"1.0.0.0"}])
        file = folder / "Test.module"
        cfg = json.loads(file.read_text())
        cfg["Assemblies"][0]["Path"] = "../Unturned_Data/Managed/Assembly-CSharp.dll"
        file.write_text(json.dumps(cfg))
        with self.assertRaises(ValueError):
            validate_package(folder)
        cfg["Assemblies"][0]["Path"] = "/Test.dll"
        file.write_text(json.dumps(cfg))
        self.mgr.import_local(folder, "MissingDependency")
        with self.assertRaises(ValueError):
            install_module(self.mgr, "MissingDependency", confirm=True)

    def test_local_maps_profile(self):
        self.mgr.import_local(self.source, "CustomMap")
        self.mgr.save_profile("maps-only", [], [], local_maps=["CustomMap"])
        plan = self.mgr.apply_profile("maps-only")
        self.assertEqual(plan["add"], ["Maps/CustomMap"])
        self.mgr.apply_profile("maps-only", confirm=True)
        self.assertTrue((self.game / "Bundles" / "Workshop" / "Maps" / "U2M_CustomMap").exists())

    def test_reject_unsafe_names(self):
        with self.assertRaises(ValueError):
            self.mgr.import_local(self.source, "../outside")

if __name__ == "__main__":
    unittest.main()

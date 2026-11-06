"""v0.4 offline dependency graph tests."""
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from manager import Manager
from dependency_planner import plan_modules

class DependencyTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.game = root / "Unturned"
        self.game.mkdir()
        (self.game / "Unturned.exe").write_bytes(b"")
        self.manager = Manager(self.game, root / "manager")

    def add(self, staging, module, dependencies=()):
        folder = self.manager.staging / staging
        folder.mkdir(parents=True)
        (folder / "Main.dll").write_bytes(b"fake DLL for filesystem tests")
        config = {"Name": module, "Version": "1.0.0.0", "IsEnabled": True,
                  "Assemblies": [{"Role": "Client", "Path": "/Main.dll"}],
                  "Dependencies": [{"Name": dep, "Version": "1.0.0.0"} for dep in dependencies]}
        (folder / "My.module").write_text(json.dumps(config), encoding="utf-8")

    def test_order(self):
        self.add("A", "A", ("B",))
        self.add("B", "B", ("C",))
        self.add("C", "C")
        result = plan_modules(self.manager, ["A", "B", "C"])
        self.assertEqual(result["issues"], [])
        self.assertEqual(result["install_order"], ["C", "B", "A"])

    def test_cycle(self):
        self.add("A", "A", ("B",))
        self.add("B", "B", ("A",))
        result = plan_modules(self.manager, ["A", "B"])
        self.assertTrue(any(i["kind"] == "dependency_cycle" for i in result["issues"]))

    def test_missing_dependency(self):
        self.add("A", "A", ("Missing",))
        result = plan_modules(self.manager, ["A"])
        self.assertTrue(any(i["kind"] == "missing_dependency" for i in result["issues"]))

    def test_prevent_removing_dependency_of_retained_module(self):
        from module_support import install_module
        self.add("Core", "Core")
        self.add("Consumer", "Consumer", ("Core",))
        # Install both in valid dependency order.
        install_module(self.manager, "Core", confirm=True)
        install_module(self.manager, "Consumer", confirm=True)
        self.manager.save_profile("broken", [], [], modules=["Consumer"])
        result = self.manager.plan_profile("broken")
        self.assertTrue(any(i["kind"] == "broken_final_dependency"
                            for i in result["dependency_issues"]))
        with self.assertRaises(ValueError):
            self.manager.apply_profile("broken", confirm=True)

    def test_retain_both_modules(self):
        from module_support import install_module
        self.add("Core", "Core")
        self.add("Consumer", "Consumer", ("Core",))
        install_module(self.manager, "Core", confirm=True)
        install_module(self.manager, "Consumer", confirm=True)
        self.manager.save_profile("valid", [], [], modules=["Core", "Consumer"])
        result = self.manager.plan_profile("valid")
        self.assertEqual(result["dependency_issues"], [])

    def test_profile_preflight_blocks_cycle(self):
        self.add("A", "A", ("B",))
        self.add("B", "B", ("A",))
        self.manager.save_profile("bad", [], [], modules=["A", "B"])
        with self.assertRaises(ValueError):
            self.manager.apply_profile("bad", confirm=True)
        self.assertEqual(self.manager.state()["installed"], {})

if __name__ == "__main__":
    unittest.main()

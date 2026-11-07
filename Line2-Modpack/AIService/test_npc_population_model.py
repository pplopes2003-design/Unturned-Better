"""Offline regression tests for Line 1 group-lifecycle semantics."""
import unittest
from npc_population_model import PopulationModel

class PopulationModelTests(unittest.TestCase):
    def test_fallen_member_replacement_joins_existing_group_with_new_identity(self):
        p=PopulationModel(cap=3,respawn_delay=10)
        a=p.spawn("friendly")
        b=p.spawn("friendly")
        gid=p.form_group((a.uid,b.uid))
        self.assertTrue(p.kill(a.uid,now=0))
        self.assertEqual(p.tick(9.99),[])
        replacement=p.tick(10)[0]
        self.assertNotEqual(replacement.uid,a.uid)
        self.assertEqual(replacement.group,gid)
        self.assertEqual(p.active_count,2)
        self.assertFalse(p.actors[a.uid].alive)

    def test_extinct_group_is_not_resurrected(self):
        p=PopulationModel(cap=3,respawn_delay=10)
        a=p.spawn("hostile")
        b=p.spawn("hostile")
        gid=p.form_group([a.uid,b.uid])
        p.kill(a.uid,0)
        p.kill(b.uid,0)
        newcomers=p.tick(10)
        self.assertEqual(len(newcomers),2)
        self.assertTrue(all(x.group is None for x in newcomers))
        self.assertEqual(len([x for x in p.actors.values() if x.alive and x.group==gid]),0)

    def test_cap_and_natural_spawn_switch(self):
        p=PopulationModel(cap=1,respawn_delay=10)
        a=p.spawn("neutral")
        with self.assertRaises(ValueError):
            p.spawn("hostile")
        p.kill(a.uid,0)
        self.assertEqual(p.tick(20,natural_enabled=False),[])
        self.assertEqual(len(p.tick(20)),1)
        self.assertEqual(p.active_count,1)
        self.assertEqual(p.tick(30),[])

    def test_repeated_death_does_not_schedule_duplicates(self):
        p=PopulationModel()
        a=p.spawn("friendly")
        self.assertTrue(p.kill(a.uid,2))
        self.assertFalse(p.kill(a.uid,3))
        self.assertEqual(len(p.pending),1)

    def test_export_contains_pending_timestamps(self):
        p=PopulationModel(cap=3,respawn_delay=60,mode="total")
        a=p.spawn("hostile")
        p.kill(a.uid,1)
        state=p.export_state()
        self.assertEqual(state["schema"],1)
        self.assertEqual(state["pending"][0]["ready_at"],61.0)
        self.assertEqual(state["mode"],"total")

if __name__=="__main__":
    unittest.main()

import unittest
from horde_policy import plan_hordes, HordePolicy

class HordePolicyTests(unittest.TestCase):
    def setUp(self):
        self.points = [(float(i), 0.0, 0.0) for i in range(50)]

    def test_never_exceed_vanilla_cap(self):
        for cap in (0, 1, 6, 10, 22, 40, 100):
            with self.subTest(cap=cap):
                groups = plan_hordes(self.points, cap, seed=42)
                total = sum(len(g) for g in groups)
                self.assertEqual(total, min(cap, len(self.points)))
                self.assertLessEqual(total, cap)
                self.assertEqual(len(set(p for group in groups for p in group)), total)

    def test_disable_grouping_is_not_an_increase(self):
        groups=plan_hordes(self.points, 9, HordePolicy(enable_grouping=False))
        self.assertEqual(len(groups), 9)
        self.assertTrue(all(len(g)==1 for g in groups))

    def test_does_not_allow_population_expansion(self):
        with self.assertRaises(ValueError):
            plan_hordes(self.points, 9, HordePolicy(preserve_vanilla_population=False))

    def test_deterministic(self):
        self.assertEqual(plan_hordes(self.points, 20, seed=7),
                         plan_hordes(self.points, 20, seed=7))

    def test_empty_and_small_groups(self):
        self.assertEqual(plan_hordes([], 30), [])
        self.assertEqual(sum(map(len,plan_hordes(self.points,2))),2)

if __name__=="__main__":
    unittest.main()

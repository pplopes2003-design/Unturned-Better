"""Radio model regressions. No Unity/Steam runtime is required."""
import unittest
from npc_radio_network import RadioActor, RadioNetwork, WALKIE_ID, EARPIECE_ID


class RadioTests(unittest.TestCase):
    def setUp(self):
        self.net = RadioNetwork()
        self.a = RadioActor(uid="a", group_id=10, faction_id=2,
                            inventory_ids={WALKIE_ID})
        self.b = RadioActor(uid="b", group_id=11, faction_id=2,
                            inventory_ids={WALKIE_ID})
        self.net.register(self.a)
        self.net.register(self.b)

    def test_verified_sighting_delivers_only_coarse_sectors(self):
        msg = self.net.report("a", "enemy_contact", (312.4, 10, -205.7), 2.0,
                              verified=True)
        self.assertIsNotNone(msg)
        received = self.net.transmit("a", 2.0)
        self.assertEqual(len(received), 1)
        self.assertEqual(received[0]["receiver_id"], "b")
        self.assertEqual(received[0]["sector"], [3, -3])
        self.assertFalse(received[0]["verified_by_receiver"])
        self.assertNotIn("position", received[0])

    def test_no_radio_or_unverified_information_is_never_sent(self):
        self.assertIsNone(self.net.report("a", "airdrop", (0, 0, 0), 2,
                                           verified=False))
        self.a.inventory_ids.clear()
        self.assertIsNone(self.net.report("a", "enemy_contact", (0, 0, 0), 2,
                                           verified=True))
        self.assertEqual(self.net.transmit("a", 2.0), [])

    def test_same_faction_different_channel_rejects(self):
        self.b.channel += 1
        self.net.report("a", "useful_loot", (10, 0, 10), 5, verified=True)
        self.assertEqual(self.net.transmit("a", 5), [])

    def test_unaffiliated_groups_are_isolated(self):
        self.net.update_identity("a", faction_id=0, group_id=10)
        self.net.update_identity("b", faction_id=0, group_id=11)
        self.net.report("a", "unknown_base", (5, 0, 5), 1, verified=True)
        self.assertEqual(self.net.transmit("a", 1), [])

    def test_earpiece_requires_being_worn(self):
        self.b.inventory_ids = {EARPIECE_ID}
        self.net.report("a", "enemy_contact", (20, 0, 20), 1, verified=True)
        self.assertEqual(self.net.transmit("a", 1), [])
        self.b.earpiece_worn = True
        self.net.report("a", "enemy_contact", (20, 0, 20), 22, verified=True)
        self.assertEqual(len(self.net.transmit("a", 22)), 1)

    def test_expired_and_former_group_reports_do_not_replay(self):
        self.net.report("a", "enemy_contact", (1, 0, 1), 1, verified=True, ttl=3)
        self.assertEqual(self.net.transmit("a", 10), [])
        self.net.report("a", "airdrop", (1, 0, 1), 22, verified=True)
        self.net.update_identity("a", faction_id=4, group_id=10)
        self.assertEqual(self.net.transmit("a", 23), [])

    def test_spoofed_truthy_verification_rejected(self):
        self.assertIsNone(self.net.report("a", "enemy_contact", (0, 0, 0), 1,
                                           verified="yes"))
        self.assertEqual(self.net.transmit("a", 1), [])

    def test_future_dated_radio_report_not_delivered(self):
        self.net.report("a", "airdrop", (1, 0, 1), 20, verified=True)
        self.assertEqual(self.net.transmit("a", 10), [])
        self.assertEqual(len(self.net.transmit("a", 20)), 1)

    def test_global_airtime_and_cooldowns(self):
        self.net.report("a", "enemy_contact", (1, 0, 1), 0, verified=True)
        self.assertEqual(len(self.net.transmit("a", 0)), 1)
        self.net.report("b", "enemy_contact", (2, 0, 2), 0, verified=True)
        self.assertEqual(self.net.transmit("b", 0.1), [])
        self.assertEqual(len(self.net.transmit("b", 1.25)), 1)

    def test_no_reinforcements_created(self):
        before = len(self.net.actors)
        self.net.report("a", "reinforcements", (10, 0, 10), 1, verified=True)
        self.net.transmit("a", 1)
        self.assertEqual(len(self.net.actors), before)

    def test_message_dies_with_source(self):
        self.net.report("a", "airdrop", (10, 0, 10), 1, verified=True)
        self.a.alive = False
        self.assertEqual(self.net.transmit("a", 1), [])


if __name__ == "__main__":
    unittest.main()

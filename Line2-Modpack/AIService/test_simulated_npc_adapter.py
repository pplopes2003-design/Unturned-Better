"""Offline simulated NPC adapter regression tests (no Unturned process)."""
import unittest
from simulated_npc_adapter import SimulatedNpcWorld
from server import evaluate


class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.now = [100.0]
        self.world = SimulatedNpcWorld(clock=lambda: self.now[0])
        self.agent = self.world.spawn((4, 0, 8))

    def decide(self, request):
        return evaluate(request)

    def test_round_trip_and_no_movement(self):
        self.agent.threat_visible = True
        first_position = self.agent.position
        req = self.world.observe(self.agent.agent_id)
        result = self.world.accept(self.decide(req))
        self.assertEqual(result, "accepted")
        self.assertEqual(self.agent.intention, "engage")
        self.assertEqual(self.agent.position, first_position)

    def test_expiry(self):
        req = self.world.observe(self.agent.agent_id)
        self.now[0] += 0.251
        self.assertEqual(self.world.accept(self.decide(req)), "stale")
        self.assertEqual(self.agent.intention, "idle")

    def test_replay_and_wrong_agent(self):
        req = self.world.observe(self.agent.agent_id)
        answer = self.decide(req)
        self.assertEqual(self.world.accept(answer), "accepted")
        self.assertEqual(self.world.accept(answer), "unknown_or_replayed_tick")
        req2 = self.world.observe(self.agent.agent_id)
        forged = self.decide(req2)
        forged["agent_id"] = "other"
        self.assertEqual(self.world.accept(forged), "unknown_or_replayed_tick")
        self.assertEqual(self.agent.intention, "idle")

    def test_rejected_reply_preserves_prior_engage_intention(self):
        self.agent.threat_visible = True
        first = self.world.observe(self.agent.agent_id)
        self.assertEqual(self.world.accept(self.decide(first)), "accepted")
        self.assertEqual(self.agent.intention, "engage")
        second = self.world.observe(self.agent.agent_id)
        forged = self.decide(second)
        forged["agent_id"] = "different-agent"
        self.assertEqual(self.world.accept(forged), "unknown_or_replayed_tick")
        self.assertEqual(self.agent.intention, "engage")

    def test_despawn_ignores_pending(self):
        req = self.world.observe(self.agent.agent_id)
        self.world.despawn(self.agent.agent_id)
        self.assertEqual(self.world.accept(self.decide(req)), "unknown_or_replayed_tick")

    def test_reject_unrecognized_action(self):
        req = self.world.observe(self.agent.agent_id)
        answer = self.decide(req)
        answer["action"] = "teleport"
        self.assertEqual(self.world.accept(answer), "invalid_decision")

    def test_multiple_agents_distinct(self):
        second = self.world.spawn((30, 0, 20))
        second.under_fire = True
        second.health = 0.1
        ra = self.world.observe(self.agent.agent_id)
        rb = self.world.observe(second.agent_id)
        self.assertEqual(self.world.accept(self.decide(rb)), "accepted")
        self.assertEqual(self.world.accept(self.decide(ra)), "accepted")
        self.assertEqual(second.intention, "retreat")
        self.assertEqual(self.agent.intention, "idle")

if __name__ == "__main__":
    unittest.main()

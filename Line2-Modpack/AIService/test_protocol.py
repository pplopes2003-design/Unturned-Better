"""HTTP protocol integration tests. Uses ephemeral localhost port, no game needed."""
import json
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from http.server import ThreadingHTTPServer
from server import Handler

class ProtocolTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        cls.base = "http://127.0.0.1:" + str(cls.httpd.server_address[1])
        cls.thread = threading.Thread(target=cls.httpd.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.httpd.shutdown()
        cls.httpd.server_close()
        cls.thread.join(timeout=3)

    def request(self, endpoint="/v1/decide", payload=None):
        data = json.dumps(payload).encode("utf-8") if payload is not None else None
        req = Request(self.base + endpoint, data=data,
                      headers={"Content-Type": "application/json"} if data else {},
                      method="POST" if data else "GET")
        with urlopen(req, timeout=3) as result:
            return json.load(result)

    def test_health(self):
        data = self.request("/health")
        self.assertTrue(data["ok"])
        self.assertEqual(data["protocol"], 1)

    def test_roundtrip(self):
        response = self.request(payload={"protocol": 1, "agent_id": "bridge-probe", "tick": 42})
        self.assertEqual(response["agent_id"], "bridge-probe")
        self.assertEqual(response["tick"], 42)
        self.assertEqual(response["action"], "idle")
        self.assertGreater(response["valid_ms"], 0)

    def test_camera_observation(self):
        response = self.request(payload={
            "protocol": 1, "agent_id": "bridge-probe", "tick": 15,
            "observation": {"kind": "camera", "scene": "Washington",
                            "position": [0.0, 10.0, 15.0],
                            "forward": [0.0, 0.0, 1.0], "nearby_colliders": 12}
        })
        self.assertEqual(response["action"], "idle")
        self.assertEqual(response["reason"], "diagnostic_only")

    def test_deterministic_npc_policy(self):
        base = {"kind": "npc", "position": [1, 2, 3], "health": 0.2,
                "threat_visible": True, "under_fire": True,
                "has_cover": True, "last_known_threat": [10, 2, 3]}
        def decide(observation):
            return self.request(payload={
                "protocol": 1, "agent_id": "npc-synthetic", "tick": 1,
                "observation": observation
            })["action"]
        self.assertEqual(decide(base), "retreat")
        self.assertEqual(decide(dict(base, health=0.9)), "seek_cover")
        self.assertEqual(decide(dict(base, under_fire=False)), "engage")
        self.assertEqual(decide(dict(base, under_fire=False, threat_visible=False)), "investigate")
        self.assertEqual(decide(dict(base, under_fire=False, threat_visible=False,
                                     last_known_threat=None)), "idle")

    def test_batched_npc_policy(self):
        def make_agent(agent, visible):
            return {"protocol": 1, "agent_id": agent, "tick": 5,
                    "observation": {"kind": "npc", "position": [1, 2, 3],
                                    "health": 0.8, "threat_visible": visible,
                                    "under_fire": False, "has_cover": False,
                                    "last_known_threat": None}}
        batch = {"protocol": 1, "agents": [
            make_agent("npc-a", False), make_agent("npc-b", True)]}
        response = self.request(endpoint="/v1/decide_batch", payload=batch)
        self.assertEqual(response["protocol"], 1)
        self.assertEqual([d["action"] for d in response["decisions"]],
                         ["idle", "engage"])
        self.assertEqual([d["agent_id"] for d in response["decisions"]],
                         ["npc-a", "npc-b"])
        self.assertTrue(all(d["valid_ms"] == 600 for d in response["decisions"]))

    def test_batched_rejects_mixed_ticks(self):
        def make_agent(identity, tick):
            return {"protocol": 1, "agent_id": identity, "tick": tick,
                    "observation": {"kind": "npc", "position": [0, 0, 0],
                                    "health": 1.0, "threat_visible": False,
                                    "under_fire": False, "has_cover": False,
                                    "last_known_threat": None}}
        with self.assertRaises(HTTPError) as error:
            self.request(endpoint="/v1/decide_batch", payload={
                "protocol": 1, "agents": [
                    make_agent("npc-a", 10), make_agent("npc-b", 11)]})
        self.assertEqual(error.exception.code, 400)
        self.assertEqual(
            json.load(error.exception)["error"], "mixed_batch_ticks")

    def test_batched_rejects_duplicates_and_overload(self):
        a = {"protocol": 1, "agent_id": "same", "tick": 1,
             "observation": {"kind": "npc", "position": [0, 0, 0],
                             "health": 1.0, "threat_visible": False,
                             "under_fire": False, "has_cover": False,
                             "last_known_threat": None}}
        for agents in ([a, a], [a] * 33, []):
            with self.subTest(count=len(agents)):
                with self.assertRaises(HTTPError) as error:
                    self.request(endpoint="/v1/decide_batch",
                                 payload={"protocol": 1, "agents": agents})
                self.assertEqual(error.exception.code, 400)

    def test_reject_bad_observations(self):
        invalid = [
            {"kind": "camera", "scene": "Washington", "position": [1, 2],
             "forward": [0, 0, 1], "nearby_colliders": 4},
            {"kind": "camera", "scene": "Washington", "position": [0, 0, 0],
             "forward": [0, 0, 1], "nearby_colliders": -1},
            {"kind": "npc", "position": [0, 0, 0], "health": 5,
             "threat_visible": False, "under_fire": False, "has_cover": False},
        ]
        for observation in invalid:
            with self.subTest(observation=observation):
                with self.assertRaises(HTTPError) as ctx:
                    self.request(payload={"protocol": 1, "agent_id": "a", "tick": 1,
                                          "observation": observation})
                self.assertEqual(ctx.exception.code, 400)

    def test_reject_invalid_protocol(self):
        with self.assertRaises(HTTPError) as context:
            self.request(payload={"protocol": 99, "agent_id": "a", "tick": 1})
        self.assertEqual(context.exception.code, 400)

    def test_reject_bad_ticks(self):
        with self.assertRaises(HTTPError) as context:
            self.request(payload={"protocol": 1, "agent_id": "a", "tick": -2})
        self.assertEqual(context.exception.code, 400)

    def test_reject_oversized_body(self):
        with self.assertRaises(HTTPError) as context:
            self.request(payload={"protocol": 1, "agent_id": "x" * 70000, "tick": 1})
        self.assertEqual(context.exception.code, 413)

if __name__ == "__main__":
    unittest.main()

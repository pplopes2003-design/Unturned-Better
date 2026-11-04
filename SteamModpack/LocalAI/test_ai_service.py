"""Run: python -m unittest discover -s SteamModpack/LocalAI -p 'test_*.py'"""
import json
import threading
import unittest
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from ai_service import Handler, ThreadingHTTPServer, decide, process_batch


class DecisionTests(unittest.TestCase):
    def test_unarmed_targeted_by_zombie_withdraws(self):
        r = decide({"health_fraction": 0.95, "threat_visible": True,
                    "threat_has_aggro": True, "threat_distance": 3.0,
                    "gun_ready": False}, "normal")
        self.assertEqual(r["intent"], "withdraw")

    def test_armed_healthy_fights(self):
        r = decide({"health_fraction": 0.95, "threat_visible": True,
                    "threat_has_aggro": True, "threat_distance": 7.0,
                    "gun_ready": True}, "normal")
        self.assertEqual(r["intent"], "engage")

    def test_hidden_threat_never_becomes_clairvoyant(self):
        r = decide({"threat_visible": False, "threat_has_aggro": True,
                    "threat_distance": 2.0}, "normal")
        self.assertEqual(r["intent"], "patrol")

    def test_noise_only_investigates(self):
        r = decide({"heard_relevant_noise": True}, "normal")
        self.assertEqual(r["intent"], "investigate")

    def test_combat_mode_holds_without_contact(self):
        self.assertEqual(decide({}, "combat_total")["intent"], "hold")

    def test_malformed_duplicate_and_batch_limit(self):
        for agents in ([{"npc_id": "n1", "observation": {}},
                        {"npc_id": "n1", "observation": {}}],
                       [{"npc_id": str(i), "observation": {}} for i in range(65)]):
            with self.assertRaises(ValueError):
                process_batch({"version": 1, "mode": "normal", "agents": agents})

    def test_batch_preserves_identity(self):
        response = process_batch({"version": 1, "sequence": 19, "mode": "normal",
                                  "agents": [{"npc_id": "a", "observation": {}},
                                             {"npc_id": "b", "observation": {"health_fraction": 0.1,
                                              "threat_visible": True, "threat_has_aggro": True}}]})
        self.assertEqual(response["sequence"], 19)
        self.assertEqual([v["npc_id"] for v in response["decisions"]], ["a", "b"])
        self.assertEqual(response["decisions"][1]["intent"], "withdraw")


class HttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        cls.thread = threading.Thread(target=cls.httpd.serve_forever, daemon=True)
        cls.thread.start()
        cls.base = f"http://127.0.0.1:{cls.httpd.server_port}"

    @classmethod
    def tearDownClass(cls):
        cls.httpd.shutdown()
        cls.httpd.server_close()
        cls.thread.join(timeout=2.0)

    def test_health(self):
        with urlopen(self.base + "/health", timeout=2) as r:
            self.assertEqual(json.load(r)["version"], 1)

    def test_post(self):
        req = Request(self.base + "/v1/decide", method="POST",
                      headers={"Content-Type": "application/json"},
                      data=json.dumps({"version": 1, "sequence": 3,
                                       "mode": "normal", "agents": [
                                           {"npc_id": "agent01", "observation": {
                                               "threat_visible": True, "gun_ready": True}}]}).encode())
        with urlopen(req, timeout=2) as r:
            data = json.load(r)
            self.assertEqual(data["decisions"][0]["intent"], "engage")

    def test_bad_version(self):
        req = Request(self.base + "/v1/decide", method="POST",
                      data=b'{"version":2,"mode":"normal","agents":[]}')
        with self.assertRaises(HTTPError) as caught:
            urlopen(req, timeout=2)
        self.assertEqual(caught.exception.code, 400)


if __name__ == "__main__":
    unittest.main()

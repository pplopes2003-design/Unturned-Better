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

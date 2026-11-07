"""Health-check tests without starting a game server."""
import json
import unittest
from unittest.mock import patch
from ai_service_status import check_ai_service

class FakeResponse:
    status=200
    def __enter__(self): return self
    def __exit__(self,*args): return False
    def read(self,n):
        return json.dumps({"ok":True,"protocol":1,
                           "controls_game":False,"capabilities":["heartbeat"]}).encode()

class ServiceHealthTests(unittest.TestCase):
    def test_service_up(self):
        with patch("ai_service_status.urlopen", return_value=FakeResponse()) as call:
            result=check_ai_service()
            self.assertTrue(result["online"])
            self.assertEqual(result["protocol"],1)
            self.assertEqual(call.call_args.args[0],"http://127.0.0.1:8765/health")

    def test_service_unavailable(self):
        with patch("ai_service_status.urlopen", side_effect=OSError("connection refused")):
            self.assertFalse(check_ai_service()["online"])

    def test_reject_unexpected_protocol(self):
        class WrongResponse(FakeResponse):
            def read(self,n):
                return b'{"ok":true,"protocol":2,"controls_game":false}'
        with patch("ai_service_status.urlopen", return_value=WrongResponse()):
            self.assertFalse(check_ai_service()["online"])

if __name__ == "__main__":
    unittest.main()

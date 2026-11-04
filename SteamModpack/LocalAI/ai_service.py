#!/usr/bin/env python3
"""Optional localhost strategic NPC decision sidecar.

No game-world authority. The host controls sensors, paths, combat and actions.
Run: python ai_service.py --port 18742
"""
import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PROTOCOL_VERSION = 1
MAX_BODY = 96 * 1024
MAX_AGENTS = 64
VALID_MODES = {"normal", "combat_total"}


def _num(value, default=0.0):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return default
    value = float(value)
    if not (-100000.0 <= value <= 100000.0):
        return default
    return value


def decide(observation, mode):
    """Return strategy only; no coordinates, damage or unverified paths."""
    health = max(0.0, min(1.0, _num(observation.get("health_fraction"), 1.0)))
    threat_visible = observation.get("threat_visible") is True
    threatened = observation.get("threat_has_aggro") is True
    threat_distance = max(0.0, _num(observation.get("threat_distance"), 10000.0))
    gun_ready = observation.get("gun_ready") is True
    heard_noise = observation.get("heard_relevant_noise") is True
    zombie_count = int(max(0, min(32, _num(observation.get("visible_zombie_count"), 0))))
    if threat_visible and (health < 0.30 or
                           (threatened and not gun_ready and threat_distance <= 9) or
                           (zombie_count >= 3 and health < 0.65)):
        return {"intent": "withdraw", "reason": "observed_immediate_danger"}
    if threat_visible and (gun_ready or threat_distance <= 2.5):
        return {"intent": "engage", "reason": "confirmed_visible_threat"}
    if threat_visible and threatened:
        return {"intent": "withdraw", "reason": "unarmed_threat_pursuit"}
    if heard_noise:
        return {"intent": "investigate", "reason": "known_sound_category"}
    if mode == "combat_total":
        return {"intent": "hold", "reason": "await_tactical_contact"}
    return {"intent": "patrol", "reason": "routine_exploration"}


def process_batch(payload):
    if not isinstance(payload, dict) or payload.get("version") != PROTOCOL_VERSION:
        raise ValueError("invalid protocol version")
    mode = payload.get("mode")
    agents = payload.get("agents")
    if mode not in VALID_MODES or not isinstance(agents, list) or len(agents) > MAX_AGENTS:
        raise ValueError("invalid mode or agent count")
    result = []
    seen = set()
    for agent in agents:
        if not isinstance(agent, dict):
            raise ValueError("invalid agent")
        identity = agent.get("npc_id")
        obs = agent.get("observation")
        if not isinstance(identity, str) or not (1 <= len(identity) <= 64):
            raise ValueError("invalid npc_id")
        if identity in seen or not isinstance(obs, dict) or len(obs) > 24:
            raise ValueError("duplicate id or invalid observation")
        seen.add(identity)
        choice = decide(obs, mode)
        result.append({"npc_id": identity, **choice})
    return {"version": PROTOCOL_VERSION, "sequence": payload.get("sequence", 0),
            "decisions": result}


class Handler(BaseHTTPRequestHandler):
    server_version = "UnturnedBetterLocalAI/0.1"

    def _reply(self, status, document):
        body = json.dumps(document, separators=(",", ":"), allow_nan=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/health":
            self._reply(200, {"status": "ready", "version": PROTOCOL_VERSION})
        else:
            self._reply(404, {"error": "not_found"})

    def do_POST(self):
        if self.path != "/v1/decide":
            self._reply(404, {"error": "not_found"})
            return
        try:
            length = int(self.headers.get("Content-Length", "-1"))
            if length < 2 or length > MAX_BODY:
                self._reply(413, {"error": "invalid_body_size"})
                return
            content = self.rfile.read(length)
            payload = json.loads(content.decode("utf-8"))
            output = process_batch(payload)
        except (ValueError, UnicodeDecodeError, json.JSONDecodeError):
            self._reply(400, {"error": "invalid_request"})
            return
        self._reply(200, output)

    def log_message(self, format_string, *args):
        # No NPC IDs or perception data written to a web server access log.
        pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=18742)
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("port must be 1024..65535")
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"Unturned Better local AI ready: http://127.0.0.1:{args.port}/health", flush=True)
    try:
        server.serve_forever(poll_interval=0.25)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()

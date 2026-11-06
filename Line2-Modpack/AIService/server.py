"""Unturned 2 deterministic AI service — protocol v1, local loopback only.

The /v1/decide endpoint accepts validated camera and synthetic NPC observations.
No action is executed in the game. Client-side authority checks are still required.
"""
from __future__ import annotations

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import argparse
import json
import math
import time

MAX_BYTES = 65536
PROTOCOL = 1
ACTIONS = ("idle", "investigate", "seek_cover", "engage", "retreat")
MAX_NPCS = 0  # No actual NPCs are created or controlled by this service.


def finite_vec(value):
    return (isinstance(value, list) and len(value) == 3 and
            all(type(x) in (float, int) and math.isfinite(x) and abs(x) <= 1000000 for x in value))


def validate(payload):
    if not isinstance(payload, dict) or type(payload.get("protocol")) is not int or payload["protocol"] != PROTOCOL:
        raise ValueError("invalid_protocol")
    agent = payload.get("agent_id")
    tick = payload.get("tick")
    if not isinstance(agent, str) or not 1 <= len(agent) <= 96 or not all(c.isalnum() or c in "-_:." for c in agent):
        raise ValueError("invalid_agent_id")
    if type(tick) is not int or tick < 0 or tick > 2147483647:
        raise ValueError("invalid_tick")
    obs = payload.get("observation")
    if obs is None:
        return agent, tick, None  # Legacy v0.5 protocol probe
    if not isinstance(obs, dict) or obs.get("kind") not in ("camera", "npc"):
        raise ValueError("invalid_observation_kind")
    if not finite_vec(obs.get("position")):
        raise ValueError("invalid_position")
    if obs["kind"] == "camera":
        if not finite_vec(obs.get("forward")):
            raise ValueError("invalid_forward")
        if not isinstance(obs.get("scene"), str) or len(obs["scene"]) > 128:
            raise ValueError("invalid_scene")
        count = obs.get("nearby_colliders")
        if type(count) is not int or not 0 <= count <= 1024:
            raise ValueError("invalid_nearby_colliders")
    if obs["kind"] == "npc":
        health = obs.get("health")
        if type(health) not in (float, int) or not math.isfinite(health) or not 0 <= health <= 1:
            raise ValueError("invalid_health")
        for key in ("threat_visible", "under_fire", "has_cover"):
            if type(obs.get(key)) is not bool:
                raise ValueError("invalid_" + key)
        target = obs.get("last_known_threat")
        if target is not None and not finite_vec(target):
            raise ValueError("invalid_last_known_threat")
    return agent, tick, obs


def decide(obs):
    """Pure deterministic decision policy, not a pathfinder or game adapter."""
    if obs is None or obs["kind"] == "camera":
        return "idle", "diagnostic_only"
    if obs["under_fire"] and obs["health"] < 0.3:
        return "retreat", "low_health_under_fire"
    if obs["under_fire"] and obs["has_cover"]:
        return "seek_cover", "incoming_fire_cover_available"
    if obs["threat_visible"]:
        return "engage", "visible_threat"
    if obs.get("last_known_threat") is not None:
        return "investigate", "last_known_threat"
    return "idle", "no_threat"


def evaluate(payload):
    agent, tick, obs = validate(payload)
    action, reason = decide(obs)
    return {"protocol": PROTOCOL, "agent_id": agent, "tick": tick,
            "action": action, "reason": reason, "valid_ms": 250}


class Handler(BaseHTTPRequestHandler):
    server_version = "U2AI/0.2"

    def respond(self, code, payload):
        raw = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(raw)

    def do_GET(self):
        if self.path != "/health":
            return self.respond(404, {"error": "not_found"})
        return self.respond(200, {"ok": True, "protocol": PROTOCOL, "service": "U2AI",
                                  "capabilities": ["heartbeat", "camera_probe", "synthetic_npc_policy"],
                                  "controls_game": False})

    def do_POST(self):
        if self.path != "/v1/decide":
            return self.respond(404, {"error": "not_found"})
        try:
            length = int(self.headers.get("Content-Length", "-1"))
            if length < 0 or length > MAX_BYTES:
                return self.respond(413, {"error": "invalid_length"})
            data = json.loads(self.rfile.read(length))
            result = evaluate(data)
        except (ValueError, TypeError, UnicodeDecodeError, json.JSONDecodeError) as exc:
            return self.respond(400, {"error": str(exc)[:120]})
        return self.respond(200, result)

    def log_message(self, fmt, *args):
        print(time.strftime("%Y-%m-%d %H:%M:%S"), fmt % args)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--port", type=int, default=8765)
    args = p.parse_args()
    if not 1024 <= args.port <= 65535:
        p.error("Port must be 1024..65535")
    with ThreadingHTTPServer(("127.0.0.1", args.port), Handler) as server:
        print("U2AI protocol v1 listening on http://127.0.0.1:" + str(args.port))
        server.serve_forever()


if __name__ == "__main__":
    main()

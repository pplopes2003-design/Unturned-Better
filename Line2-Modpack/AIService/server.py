"""Loopback-only AI service protocol prototype for Unturned 2.

Python standard library only. Does not execute game logic or make Unity calls.
All responses are bounded and deterministic. No generative model is required.
"""
from __future__ import annotations
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import argparse
import json
import time

MAX_BYTES = 65536
PROTOCOL = 1


class Handler(BaseHTTPRequestHandler):
    server_version = "U2AI/0.1"
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
                                  "capabilities": ["heartbeat", "decision_stub"]})

    def do_POST(self):
        if self.path != "/v1/decide":
            return self.respond(404, {"error": "not_found"})
        try:
            length = int(self.headers.get("Content-Length", "-1"))
            if length < 0 or length > MAX_BYTES:
                return self.respond(413, {"error": "invalid_length"})
            data = json.loads(self.rfile.read(length))
            if not isinstance(data, dict) or data.get("protocol") != PROTOCOL:
                return self.respond(400, {"error": "invalid_protocol"})
            agent_id = data.get("agent_id")
            tick = data.get("tick")
            if (not isinstance(agent_id, str) or not 1 <= len(agent_id) <= 96
                    or not isinstance(tick, int) or isinstance(tick, bool) or tick < 0):
                return self.respond(400, {"error": "invalid_request"})
            # Intentionally no autonomous actions: this is only a reliable IPC
            # contract test. Later, validated game observations feed planners.
            return self.respond(200, {"protocol": PROTOCOL, "agent_id": agent_id, "tick": tick,
                                      "action": "idle", "valid_ms": 250})
        except (ValueError, UnicodeDecodeError, json.JSONDecodeError):
            return self.respond(400, {"error": "malformed_request"})

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

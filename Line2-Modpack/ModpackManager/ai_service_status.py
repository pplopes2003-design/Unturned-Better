"""Strict read-only localhost health probe for the Line 2 AI service."""
from __future__ import annotations
import json
from urllib.request import urlopen

HEALTH_URL = "http://127.0.0.1:8765/health"

def check_ai_service(timeout=1.5):
    """No process launch, no NPC actions, and no arbitrary remote endpoints."""
    try:
        with urlopen(HEALTH_URL, timeout=timeout) as response:
            if response.status != 200:
                raise ValueError("Unexpected health status")
            raw = response.read(16384)
        data = json.loads(raw)
        if not isinstance(data, dict) or data.get("ok") is not True or data.get("protocol") != 1:
            raise ValueError("Unexpected AI service protocol/response")
        if data.get("controls_game") is not False:
            raise ValueError("Unexpected game-control capability")
        return {"online": True, "protocol": 1,
                "capabilities": data.get("capabilities", []),
                "message": "AI Service responding locally (diagnostic only)."}
    except (OSError, ValueError, TypeError, TimeoutError) as exc:
        return {"online": False, "message": str(exc)}

"""Authoritative *simulated* NPC adapter for testing the AI protocol.

This module deliberately does not import Unturned or call Unity. It is a
replacement for the future game-side entity bridge, NOT a live-game adapter.
"""
from __future__ import annotations
from dataclasses import dataclass
import math
import time
import uuid

ALLOWED = frozenset(("idle", "investigate", "seek_cover", "engage", "retreat"))
MAX_DECISION_AGE_MS = 250


@dataclass
class Agent:
    agent_id: str
    position: tuple[float, float, float]
    health: float = 1.0
    threat_visible: bool = False
    under_fire: bool = False
    has_cover: bool = False
    last_known_threat: tuple[float, float, float] | None = None
    last_applied_tick: int = -1
    intention: str = "idle"


class SimulatedNpcWorld:
    def __init__(self, clock=None):
        self.agents: dict[str, Agent] = {}
        self._clock = clock or time.monotonic
        self._pending: dict[tuple[str, int], float] = {}
        self._counter = 0

    def spawn(self, position=(0.0, 0.0, 0.0)):
        if len(self.agents) >= 128:
            raise ValueError("simulated_population_limit")
        self._counter += 1
        agent_id = "sim-" + uuid.uuid4().hex[:12]
        agent = Agent(agent_id, tuple(position))
        self.agents[agent_id] = agent
        return agent

    def despawn(self, agent_id):
        self.agents.pop(agent_id, None)
        for key in list(self._pending):
            if key[0] == agent_id:
                del self._pending[key]

    def observe(self, agent_id):
        agent = self.agents[agent_id]
        self._counter += 1
        tick = self._counter
        self._pending[(agent_id, tick)] = self._clock()
        return {
            "protocol": 1, "agent_id": agent_id, "tick": tick,
            "observation": {
                "kind": "npc", "position": list(agent.position),
                "health": agent.health, "threat_visible": agent.threat_visible,
                "under_fire": agent.under_fire, "has_cover": agent.has_cover,
                "last_known_threat": (list(agent.last_known_threat)
                                      if agent.last_known_threat is not None else None),
            },
        }

    def accept(self, decision):
        """Validate and record an *intention*, never execute movement or damage."""
        if not isinstance(decision, dict):
            return "invalid_response"
        agent_id = decision.get("agent_id")
        tick = decision.get("tick")
        if type(agent_id) is not str or type(tick) is not int:
            return "invalid_identity"
        key = (agent_id, tick)
        sent_at = self._pending.pop(key, None)
        if sent_at is None:
            return "unknown_or_replayed_tick"
        agent = self.agents.get(agent_id)
        if agent is None:
            return "despawned"
        ttl = decision.get("valid_ms")
        if (decision.get("protocol") != 1 or
            decision.get("action") not in ALLOWED or
            type(ttl) is not int or not 1 <= ttl <= MAX_DECISION_AGE_MS):
            return "invalid_decision"
        elapsed = (self._clock() - sent_at) * 1000
        if not math.isfinite(elapsed) or elapsed < 0 or elapsed > ttl:
            return "stale"
        if tick <= agent.last_applied_tick:
            return "out_of_order"
        agent.last_applied_tick = tick
        agent.intention = decision["action"]
        return "accepted"

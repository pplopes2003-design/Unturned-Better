"""Deterministic NPC population/group rules migrated conceptually from Line 1.

Pure policy, never spawns Unity objects. Delayed replacement, unique actor IDs,
finite cap, group continuity until its last survivor dies, and JSON-safe state.
No Unturned APIs or background work are used here.
"""
from __future__ import annotations

from dataclasses import dataclass, asdict
from typing import Optional
import uuid

ROLES = frozenset(("friendly", "neutral", "hostile"))
MODES = frozenset(("passive", "normal", "total"))


@dataclass
class Actor:
    uid: str
    role: str
    group: Optional[str] = None
    alive: bool = True


@dataclass
class Replacement:
    ready_at: float
    role: str
    former_group: Optional[str] = None


class PopulationModel:
    def __init__(self, cap=8, respawn_delay=60.0, mode="normal"):
        if not isinstance(cap, int) or isinstance(cap, bool) or not 1 <= cap <= 32:
            raise ValueError("NPC cap must be 1..32")
        if not 1 <= respawn_delay <= 86400:
            raise ValueError("respawn_delay must be 1..86400 seconds")
        if mode not in MODES:
            raise ValueError("invalid combat mode")
        self.cap = cap
        self.respawn_delay = float(respawn_delay)
        self.mode = mode
        self.actors: dict[str, Actor] = {}
        self.pending: list[Replacement] = []

    @property
    def active_count(self):
        return sum(actor.alive for actor in self.actors.values())

    def spawn(self, role, *, group=None):
        if role not in ROLES:
            raise ValueError("invalid role")
        if self.active_count >= self.cap:
            raise ValueError("npc_population_limit")
        if group is not None and not any(a.alive and a.group == group for a in self.actors.values()):
            raise ValueError("cannot join nonliving group")
        actor = Actor(uid=uuid.uuid4().hex, role=role, group=group)
        self.actors[actor.uid] = actor
        return actor

    def form_group(self, actor_ids):
        ids = list(actor_ids)
        if not 2 <= len(ids) <= self.cap or len(set(ids)) != len(ids):
            raise ValueError("invalid group membership")
        for uid in ids:
            if uid not in self.actors or not self.actors[uid].alive:
                raise ValueError("unknown/dead group member")
            if self.actors[uid].group is not None:
                raise ValueError("already grouped")
        gid = uuid.uuid4().hex
        for uid in ids:
            self.actors[uid].group = gid
        return gid

    def kill(self, uid, now):
        actor = self.actors.get(uid)
        if actor is None or not actor.alive:
            return False
        if now < 0:
            raise ValueError("invalid time")
        actor.alive = False
        self.pending.append(Replacement(
            ready_at=float(now) + self.respawn_delay,
            role=actor.role, former_group=actor.group))
        return True

    def tick(self, now, *, natural_enabled=True):
        """Request virtual replacements for allowed slots, oldest first.

        This does not create game objects. If a group has no living members
        at replacement time the new actor is independent: the extinct group
        cannot be resurrected.
        """
        if now < 0:
            raise ValueError("invalid time")
        if not natural_enabled:
            return []
        spawned = []
        remaining = []
        for task in sorted(self.pending, key=lambda x:x.ready_at):
            if task.ready_at > now or self.active_count >= self.cap:
                remaining.append(task)
                continue
            survivors = (a for a in self.actors.values()
                         if a.alive and a.group == task.former_group)
            group = task.former_group if task.former_group is not None and any(survivors) else None
            spawned.append(self.spawn(task.role, group=group))
        self.pending = remaining
        return spawned

    def export_state(self):
        """Serialization boundary for future persistence with an authoritative game host."""
        return {"schema":1,"cap":self.cap,"respawn_delay":self.respawn_delay,
                "mode":self.mode,
                "actors":[asdict(a) for a in self.actors.values()],
                "pending":[asdict(p) for p in self.pending]}

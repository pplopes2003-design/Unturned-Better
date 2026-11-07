"""Line 1 NPC radio semantics ported as a deterministic data-only service.

No physical transmission or Steam PlayerVoice integration exists in this file.
A future game-authoritative adapter MUST verify all supplied facts. Messages
never grant target visibility, damage rights, spawn reinforcements or loot.
"""
from __future__ import annotations

from dataclasses import dataclass, field
import math
import uuid

WALKIE_ID = 1445
EARPIECE_ID = 1446
DEFAULT_FREQUENCY = 460327
TYPES = frozenset({"enemy_contact", "reinforcements", "useful_loot",
                   "airdrop", "unknown_base"})
MAX_ACTORS = 32
MAX_PENDING = 4
MAX_AGE = 85.0
VOICE_RANGE = 35.0
AIRTIME = 1.25
PERSONAL_COOLDOWN = 8.0
OBSERVATION_COOLDOWN = 20.0


@dataclass
class RadioActor:
    uid: str
    group_id: int
    faction_id: int
    alive: bool = True
    inventory_ids: set[int] = field(default_factory=set)
    earpiece_worn: bool = False
    channel: int = 0

    @property
    def can_transmit(self) -> bool:
        return self.alive and WALKIE_ID in self.inventory_ids

    @property
    def can_receive(self) -> bool:
        return self.alive and (WALKIE_ID in self.inventory_ids or self.earpiece_worn)


@dataclass(frozen=True)
class Intel:
    message_id: str
    sender_id: str
    group_id: int
    faction_id: int
    frequency: int
    kind: str
    sector: tuple[int, int]
    observed_at: float
    expires_at: float
    confidence: float


class RadioNetwork:
    def __init__(self):
        self.actors: dict[str, RadioActor] = {}
        self.pending: dict[str, list[Intel]] = {}
        self.personal_next: dict[str, float] = {}
        self.observation_next: dict[str, float] = {}
        self.next_global = 0.0
        self.delivered: set[tuple[str, str]] = set()

    @staticmethod
    def frequency(faction_id: int, group_id: int) -> int:
        if type(group_id) is not int or not 1 <= group_id <= 100000:
            raise ValueError("invalid group id")
        if type(faction_id) is not int or not 0 <= faction_id <= 20:
            raise ValueError("invalid faction id")
        # Faction radios share a channel; unaffiliated groups are isolated.
        return (DEFAULT_FREQUENCY + 10000 + group_id if faction_id == 0
                else DEFAULT_FREQUENCY + faction_id * 1000)

    def register(self, actor: RadioActor):
        if not isinstance(actor, RadioActor) or not isinstance(actor.uid, str) or not actor.uid:
            raise ValueError("invalid actor")
        if actor.uid not in self.actors and len(self.actors) >= MAX_ACTORS:
            raise ValueError("radio actor limit")
        actor.channel = self.frequency(actor.faction_id, actor.group_id)
        self.actors[actor.uid] = actor
        self.pending.pop(actor.uid, None)
        self.observation_next.pop(actor.uid, None)

    def forget(self, uid):
        self.actors.pop(uid, None)
        self.pending.pop(uid, None)
        self.personal_next.pop(uid, None)
        self.observation_next.pop(uid, None)
        self.delivered = {key for key in self.delivered if uid not in key}

    def update_identity(self, uid, *, faction_id, group_id):
        actor = self.actors[uid]
        actor.faction_id = faction_id
        actor.group_id = group_id
        actor.channel = self.frequency(faction_id, group_id)
        # Old-group intelligence must not broadcast under the new identity.
        self.pending.pop(uid, None)

    @staticmethod
    def _sector(position):
        if (not isinstance(position, (tuple, list)) or len(position) != 3 or
            any(type(v) not in (float, int) or not math.isfinite(v) or
                abs(v) > 100000 for v in position)):
            raise ValueError("invalid observed position")
        # Map-sector estimate, never a precise live target transform.
        return tuple(int(math.floor(float(v) / 100.0)) for v in (position[0], position[2]))

    def report(self, sender_id, kind, position, now, *, verified, confidence=0.75, ttl=24.0):
        """Only firsthand verified evidence is eligible for transmission."""
        actor = self.actors.get(sender_id)
        if actor is None or not actor.can_transmit or verified is not True:
            return None
        if kind not in TYPES or type(now) not in (float, int) or not math.isfinite(now) or now < 0:
            raise ValueError("invalid radio report")
        if type(confidence) not in (int, float) or not math.isfinite(confidence) or not 0 < confidence <= 1:
            raise ValueError("invalid confidence")
        if type(ttl) not in (int, float) or not 1 <= ttl <= MAX_AGE:
            raise ValueError("invalid time-to-live")
        if now < self.observation_next.get(sender_id, 0.0):
            return None
        sector = self._sector(position)
        msg = Intel(message_id=uuid.uuid4().hex, sender_id=sender_id,
                    group_id=actor.group_id, faction_id=actor.faction_id,
                    frequency=actor.channel, kind=kind, sector=sector,
                    observed_at=float(now), expires_at=float(now + ttl),
                    confidence=float(confidence))
        queue = self.pending.setdefault(sender_id, [])
        queue[:] = [q for q in queue if q.expires_at > now and
                    q.group_id == actor.group_id and q.faction_id == actor.faction_id]
        if len(queue) >= MAX_PENDING:
            queue.pop(0)
        queue.append(msg)
        self.observation_next[sender_id] = now + OBSERVATION_COOLDOWN
        return msg

    def transmit(self, sender_id, now):
        """Deliver hearsay to currently authorized NPC receiver endpoints."""
        actor = self.actors.get(sender_id)
        if actor is None or not actor.can_transmit:
            self.pending.pop(sender_id, None)
            return []
        if type(now) not in (int, float) or not math.isfinite(now) or now < 0:
            raise ValueError("invalid time")
        queue = self.pending.get(sender_id, [])
        # A future-dated packet must not be delivered early, but neither
        # should a premature poll destroy it before its observation time.
        # Discard only expired or unauthorized messages.
        queue[:] = [m for m in queue if now < m.expires_at and
                    m.group_id == actor.group_id and m.faction_id == actor.faction_id
                    and m.frequency == actor.channel]
        if (not queue or now < self.next_global or
                now < self.personal_next.get(sender_id, 0.0)):
            return []
        eligible_index = next((i for i, m in enumerate(queue)
                               if m.observed_at <= now), None)
        if eligible_index is None:
            return []
        msg = queue.pop(eligible_index)
        self.next_global = float(now + AIRTIME)
        self.personal_next[sender_id] = float(now + PERSONAL_COOLDOWN)
        received = []
        for dst in self.actors.values():
            if (dst.uid == sender_id or not dst.can_receive or
                dst.channel != actor.channel or dst.faction_id != actor.faction_id or
                (actor.faction_id == 0 and dst.group_id != actor.group_id)):
                continue
            pair = (msg.message_id, dst.uid)
            if pair in self.delivered:
                continue
            self.delivered.add(pair)
            age = max(0.0, now - msg.observed_at)
            # A decayed, coarse clue. Receivers still need their own LOS.
            received.append({
                "receiver_id": dst.uid, "message_id": msg.message_id,
                "kind": msg.kind, "sector": list(msg.sector),
                "confidence": round(max(0.0, msg.confidence * (1.0 - age / MAX_AGE)), 3),
                "verified_by_receiver": False
            })
        if len(self.delivered) > 4096:
            self.delivered.clear()
        return received

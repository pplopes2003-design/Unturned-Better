"""Manual end-to-end simulator for the running local AI service.

Run server.py in another terminal, then python smoke_simulation.py.
Only a simulated NPC is affected; no Unturned process or DLL is involved.
"""
from __future__ import annotations
import json
from urllib.request import Request, urlopen
from simulated_npc_adapter import SimulatedNpcWorld


def main():
    world = SimulatedNpcWorld()
    npc = world.spawn((12.5, 0, 43.0))
    print("Spawned simulated NPC", npc.agent_id, "at", npc.position)

    scenarios = (
        ("patrol_no_threat", dict(health=1.0, threat_visible=False, under_fire=False,
                                  has_cover=False, last_known_threat=None)),
        ("enemy_spotted", dict(health=1.0, threat_visible=True, under_fire=False,
                               has_cover=False, last_known_threat=(20, 0, 43))),
        ("incoming_fire", dict(health=0.7, threat_visible=True, under_fire=True,
                               has_cover=True, last_known_threat=(20, 0, 43))),
        ("critical_health", dict(health=0.2, threat_visible=True, under_fire=True,
                                 has_cover=True, last_known_threat=(20, 0, 43))),
    )
    for label, changes in scenarios:
        for key, value in changes.items():
            setattr(npc, key, value)
        request = world.observe(npc.agent_id)
        encoded = json.dumps(request).encode("utf-8")
        http = Request("http://127.0.0.1:8765/v1/decide", data=encoded,
                       headers={"Content-Type": "application/json"}, method="POST")
        with urlopen(http, timeout=2.0) as response:
            decision = json.load(response)
        status = world.accept(decision)
        print(label, "->", npc.intention, "(" + status + ")")
        if status != "accepted":
            raise RuntimeError("Decision rejected: " + status)
    print("Simulation complete. NPC position unchanged:", npc.position)


if __name__ == "__main__":
    main()

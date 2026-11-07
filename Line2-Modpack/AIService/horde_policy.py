"""Offline horde placement planner constrained by vanilla zombie population.

Not connected to the Steam game: no spawn or NPC actions are performed.
Groups are formed by reordering a list of eligible spawnpoints. Grouping
cannot increase its size or the supplied vanilla region budget.
"""
from __future__ import annotations
from dataclasses import dataclass
import math
import random
from typing import Sequence

@dataclass(frozen=True)
class HordePolicy:
    preserve_vanilla_population: bool = True
    enable_grouping: bool = True
    min_group_size: int = 8
    max_group_size: int = 18
    proximity_meters: float = 25.0

def plan_hordes(spawnpoints: Sequence[tuple[float, float, float]],
                vanilla_region_budget: int,
                policy: HordePolicy = HordePolicy(),
                seed: int = 0) -> list[list[tuple[float, float, float]]]:
    if type(vanilla_region_budget) is not int or vanilla_region_budget < 0:
        raise ValueError("vanilla_region_budget must be nonnegative integer")
    if policy.min_group_size < 1 or policy.max_group_size < policy.min_group_size:
        raise ValueError("Invalid group size")
    if not math.isfinite(policy.proximity_meters) or policy.proximity_meters <= 0:
        raise ValueError("Invalid proximity")
    if not policy.preserve_vanilla_population:
        raise ValueError("Population expansion is unsupported in Line 2")
    eligible = [tuple(p) for p in spawnpoints]
    for p in eligible:
        if len(p) != 3 or not all(type(x) in (int, float) and math.isfinite(x) for x in p):
            raise ValueError("Invalid spawnpoint")
    rng = random.Random(seed)
    # Important: actual Unturned applies spawn chances and per-zone caps.
    # The supplied budget MUST be computed by the game, not estimated here.
    target = min(len(eligible), vanilla_region_budget)
    if target == 0: return []
    if not policy.enable_grouping:
        rng.shuffle(eligible)
        return [[p] for p in eligible[:target]]

    result = []
    remaining = list(eligible)
    radius_sq = policy.proximity_meters ** 2
    while remaining and sum(map(len, result)) < target:
        leader = remaining.pop(rng.randrange(len(remaining)))
        group = [leader]
        max_for_group = min(rng.randint(policy.min_group_size, policy.max_group_size),
                            target - sum(map(len, result)))
        while remaining and len(group) < max_for_group:
            nearby = [(i, sum((x-y)**2 for x, y in zip(point, leader)))
                      for i, point in enumerate(remaining)]
            nearby = [(i,d) for i,d in nearby if d <= radius_sq]
            if not nearby: break
            closest = min(d for _,d in nearby)
            matches = [i for i,d in nearby if d == closest]
            group.append(remaining.pop(rng.choice(matches)))
        result.append(group)
    assert sum(map(len,result)) <= vanilla_region_budget
    return result

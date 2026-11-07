"""Visible offline horde scenario check, DOES NOT spawn zombies in Unturned."""
from horde_policy import plan_hordes, HordePolicy

def main():
    points = [(float(x), 0.0, float(z))
              for z in range(0, 100, 5) for x in range(0, 100, 5)]
    vanilla_budget = 40
    print("SIMULATION ONLY — no Unturned entities are modified")
    for name, policy in (("vanilla grouping off", HordePolicy(enable_grouping=False)),
                         ("horde grouping on", HordePolicy(enable_grouping=True))):
        groups = plan_hordes(points, vanilla_budget, policy, seed=42)
        total = sum(map(len, groups))
        print("{}: {} zombies allocated, {} groups, sizes {}".format(
            name, total, len(groups), [len(group) for group in groups]))
        if total > vanilla_budget:
            raise AssertionError("exceeded original population budget")
    print("PASS: both tests retained the vanilla limit of {} zombies.".format(vanilla_budget))
    print("NEXT: a verified Steam-side adapter is required before hordes appear in-game.")

if __name__ == "__main__":
    main()

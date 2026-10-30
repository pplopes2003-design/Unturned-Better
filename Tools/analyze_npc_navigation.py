#!/usr/bin/env python3
"""Offline analysis of MobileNPC NavigationReports JSONL files (Python 3 stdlib)."""
import argparse
import collections
import json
from pathlib import Path

def inspect(paths):
    events = []
    malformed = []
    for path in paths:
        with path.open(encoding="utf-8") as handle:
            for line_number, line in enumerate(handle, 1):
                if not line.strip():
                    continue
                try:
                    record = json.loads(line)
                    if isinstance(record, dict) and record.get("schemaVersion") == 1:
                        events.append(record)
                except (ValueError, UnicodeError):
                    malformed.append(f"{path.name}:{line_number}")
    return events, malformed

def analyze(events, malformed):
    types = collections.Counter(e.get("eventType", "") for e in events)
    problems = collections.Counter(e.get("reason", "unspecified") for e in events
                                   if e.get("eventType") == "problem")
    recoveries = collections.Counter(
        (e.get("reason", ""), e.get("action", ""), bool(e.get("success")))
        for e in events if e.get("eventType") == "problem")
    portals = collections.defaultdict(lambda: [0, 0])
    for e in events:
        if e.get("eventType") == "portal":
            key = (e.get("map", ""), e.get("regionFrom", -1),
                   e.get("regionTo", -1), e.get("portalIndex", -1))
            portals[key][0 if e.get("success") else 1] += 1
    routes = [e for e in events if e.get("eventType") == "route"]
    failed = [e for e in routes if not e.get("success")]
    sessions = sorted(set(e.get("session", "") for e in events if e.get("session")))
    result = [
        "# MobileNPC navigation diagnostic",
        "",
        f"Events: {len(events)} | Sessions: {len(sessions)} | Malformed lines: {len(malformed)}",
        f"Route attempts: {len(routes)} | Failures: {len(failed)}",
        "",
        "## Event counts",
    ]
    result.extend(f"- {name}: {count}" for name, count in types.most_common())
    result.extend(["", "## Frequent problems"])
    result.extend(f"- {name}: {count}" for name, count in problems.most_common(20))
    result.extend(["", "## Recovery results (problem / action / success)"])
    result.extend(f"- {reason} / {action} / {success}: {count}"
                  for (reason, action, success), count in recoveries.most_common(30))
    result.extend(["", "## Portal passes and failures (map / from / to / portal)"])
    for key, values in sorted(portals.items(), key=lambda x: -x[1][1])[:30]:
        result.append(f"- {key}: passed={values[0]}, failed={values[1]}")
    result.extend(["", "## Candidate diagnoses"])
    if any(values[1] > 0 for values in portals.values()):
        result.append("- Inspect repeated failed portal transitions, collision mask, and opening width.")
    if failed:
        result.append("- Review route failures separately from motor movement failures.")
    if problems:
        result.append("- Review stuck position sequences and recovery actions before tuning penalties.")
    if not events:
        result.append("- No supported events were found. Enable recording and reproduce the bug.")
    result.extend(["", "## Notes",
                   "- Observations are diagnostic, not training labels or proof of causal errors.",
                   "- Replay/simulate candidate route changes before applying a learned policy.",
                   "- Files contain in-game coordinates; review before sharing recordings.",
                   ""])
    return "\n".join(result)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path, nargs="+",
                        help="JSONL files or directories containing nav_*.jsonl")
    parser.add_argument("--output", type=Path, default=Path("npc_navigation_report.md"))
    args = parser.parse_args()
    paths = []
    for entry in args.input:
        if entry.is_dir():
            paths.extend(sorted(entry.glob("nav_*.jsonl")))
        elif entry.is_file():
            paths.append(entry)
    events, malformed = inspect(paths)
    args.output.write_text(analyze(events, malformed), encoding="utf-8")
    print(f"Wrote {args.output} from {len(paths)} files, {len(events)} valid events")

if __name__ == "__main__":
    main()

"""Run stdlib unittest suite, always write a readable CI transcript, preserve exit code."""
from __future__ import annotations
import argparse
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("suite", choices=["AIService", "ModpackManager"])
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    folder = root / args.suite
    output = root / "test-results"
    output.mkdir(exist_ok=True)
    logfile = output / (args.suite + "-unittest.txt")
    command = [sys.executable, "-m", "unittest", "discover", "-s", ".", "-p", "test_*.py", "-v"]
    result = subprocess.run(command, cwd=folder, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    logfile.write_text(result.stdout, encoding="utf-8")
    print(result.stdout, end="")
    print("Test report:", logfile)
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())

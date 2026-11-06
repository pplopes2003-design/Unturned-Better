# GitHub processing audit — 2026-11-06

Repository: `pplopes2003-design/Unturned-Better`  
Target: `line2/modpack-framework`

## Verified through repository API
- Compared with `main`: the Line 2 branch was ahead by 49 commits and behind by zero at the start of this audit.
- The workflow file `.github/workflows/line2-python.yml` existed in the feature branch.
- The linked GitHub connection returned no legacy commit statuses on the audited commits, and the available commit-workflow lookup returned no PR-triggered runs. **These responses cannot determine whether push-triggered GitHub Actions jobs ran or failed.**
- No GitHub notification body, run ID or failing job log was available to identify the user's exact notification.

## Changes made during audit
- Updated workflow with explicit read-only permissions, push/PR/dispatch triggers, and Windows + Linux Python test matrix.
- Added `profile_integrity.py`: checks dependency compatibility of the *desired final* module set, including retained installed modules.
- Profile planner now blocks switching if it would remove a library needed by a module left active.
- Added tests for deleting a retained dependency and retaining valid module groups.

## What needs verification
1. GitHub UI > Actions > **Line 2 Python checks**: confirm enabled and inspect the latest runs. If none appear, check repository Settings > Actions (permissions and enablement). Workflow dispatch from nondefault branches can depend on workflow registration on the default branch; do not merge this project into main solely for that.
2. Run tests from `Line2-Modpack/AIService` and `Line2-Modpack/ModpackManager` using `python -m unittest discover -s . -p "test_*.py" -v`.
3. The C# client module cannot compile in hosted GitHub Actions without providing Unturned managed assemblies. Test against a real Steam installation (BattlEye disabled) and capture compiler output + `probe.log`.

## Known limitations
- Profile switching is not fully transactional: failures mid-removal can leave a partially applied set, although moved folders are kept in recovery.
- No Windows Unturned runtime, Steam subscription management, or real NPC movement verified in this audit.
- A missing commit status does not prove failed GitHub Actions processing.

## Follow-up: failing Windows and Ubuntu matrix (screenshot, 13:46)

The user's screenshot explicitly reports failed `python-tests` on **windows-latest 3.11** and **ubuntu-latest 3.11**. This confirms CI execution failure rather than missing commits. The screenshot gives no traceback or job URL, so precise exceptions cannot yet be confirmed.

Corrections committed to the Line 2 branch:
- A previous NPC replay regression expected idle when the last accepted intent was engage; corrected assertion.
- Asset collision fixture previously contained escaped newline literals, not actual line separators; corrected fixture.
- File fingerprints now sort directory/file enumeration and compare SHA-256 content rather than relying on filesystem mtime precision.
- Added tests for unchanged contents with changed modification time and changed contents with identical file size.
- Added `Line2-Modpack/run_tests.py` to retain detailed test transcripts.
- Updated GitHub Actions to upload `line2-test-logs-<OS>-py311` artifacts on both failed and successful executions.

### What is verified vs. pending
- Verified: commits recorded and branch isolated from main.
- Pending: an actual green post-fix GitHub Actions run. The GitHub connection available here cannot list push-run IDs from branch; therefore it cannot expose the tracebacks from the screenshot.
- To inspect a failure: open the latest run under Actions > Line 2 Python checks > failed OS job > failing step, or download its test-log artifact.

Continue avoiding `--apply` on valuable installations until these checks are green and the local filesystem behavior has been smoke-tested.

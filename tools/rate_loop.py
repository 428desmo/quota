"""Keep adding CPU rating matches, and commit the ranking file as it grows.

This runs on its own, so the ranking can get steadier whenever the machine has
time to spare. Matches are added to `cpu_ranking_v1.2.json` in small batches,
each one written through to disk and copied into StreamingAssets, and the two
ranking files are committed and pushed every so often.

    python3 tools/rate_loop.py --minutes 60
    nice -n 10 python3 tools/rate_loop.py --minutes 0 --commit-minutes 30

Writing is cheap and committing is not, so the two are paced apart: a batch is
the unit of work that survives a stop, while `--commit-minutes` keeps the
history from filling up with rewrites of a 400KB file. Only the two ranking
files go into the commit, whatever else is in the working tree.
"""

from __future__ import annotations

import argparse
import random
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from quota.ranking import RANKING_PATH  # noqa: E402
from tools.rate_cpu import STREAMING, run_batch  # noqa: E402

PATHS = (
    str(RANKING_PATH.relative_to(ROOT)),
    str((STREAMING / RANKING_PATH.name).relative_to(ROOT)),
    str((STREAMING / f"{RANKING_PATH.name}.meta").relative_to(ROOT)),
)


def main() -> None:
    parser = argparse.ArgumentParser(description="add CPU rating matches until the time is up")
    parser.add_argument("--minutes", type=float, default=60.0, help="time budget, 0 to run until stopped")
    parser.add_argument("--batch", type=int, default=5000, help="matches per write to disk")
    parser.add_argument("--commit-minutes", type=float, default=10.0, help="how often to commit, 0 for every batch")
    parser.add_argument("--seed", type=int, default=None, help="first seed, otherwise drawn from the clock")
    parser.add_argument("--remote", default="origin", help="remote to push to")
    parser.add_argument("--no-commit", action="store_true", help="update the file without committing")
    parser.add_argument("--no-push", action="store_true", help="commit without pushing")
    args = parser.parse_args()

    branch = git("rev-parse", "--abbrev-ref", "HEAD").strip()
    if branch == "HEAD":
        print("HEAD is detached, so there is no branch to push. Check out a branch first.")
        raise SystemExit(1)
    seed = args.seed if args.seed is not None else random.SystemRandom().randrange(1 << 30)
    deadline = time.monotonic() + args.minutes * 60 if args.minutes > 0 else None
    print(f"batches of {args.batch} matches on {branch}, seed from {seed}")
    if deadline is None:
        print("no time limit, stop with Ctrl-C")

    pushing = not args.no_push
    pending = 0
    added = 0
    total = 0
    marked = time.monotonic()
    try:
        while True:
            start = time.monotonic()
            table = run_batch(args.batch, seed, quiet=True)
            spent = time.monotonic() - start
            seed += 1
            pending += args.batch
            added += args.batch
            total = table.matches
            print(
                f"+{args.batch} matches in {spent:.0f}s, {total} total, best {best(table)}",
                flush=True,
            )
            due = args.commit_minutes <= 0 or time.monotonic() - marked >= args.commit_minutes * 60
            if not args.no_commit and due:
                pushing = announce(publish(message(pending, total), args.remote, pushing), args.remote, pushing)
                pending = 0
                marked = time.monotonic()
            if deadline is not None and time.monotonic() + spent > deadline:
                break
    except KeyboardInterrupt:
        print()
    if pending and not args.no_commit:
        pushing = announce(publish(message(pending, total), args.remote, pushing), args.remote, pushing)
    print(f"added {added} matches, {total} in the record")


def message(pending: int, total: int) -> str:
    return f"Add {pending} CPU rating matches ({total} total)."


def announce(outcome: str, remote: str, pushing: bool) -> bool:
    print(outcome, flush=True)
    if outcome != "push failed":
        return pushing
    print(f"pushing stopped, so push by hand later: git push {remote} HEAD")
    return False


def best(table) -> str:
    for key, entry in table.entries.items():
        if entry.rank == 1:
            return f"{key} {entry.value:.3f}"
    return "none"


def publish(message: str, remote: str, pushing: bool) -> str:
    """Commit the ranking files alone, then push. Returns what happened."""
    present = [path for path in PATHS if (ROOT / path).exists()]
    if not present:
        return "nothing to commit"
    if not git("status", "--porcelain", "--", *present).strip():
        return "nothing to commit"
    try:
        git("add", "--", *present)
        git("commit", "-m", message, "--", *present)
    except subprocess.CalledProcessError as error:
        return f"commit failed: {trouble(error)}"
    if not pushing:
        return "committed"
    try:
        git("push", remote, "HEAD")
    except subprocess.CalledProcessError as error:
        print(f"push to {remote} failed: {trouble(error)}")
        return "push failed"
    return "pushed"


def trouble(error: subprocess.CalledProcessError) -> str:
    return (error.stderr or "").strip() or (error.stdout or "").strip() or "no message"


def git(*args: str) -> str:
    done = subprocess.run(
        ("git",) + args,
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
    )
    return done.stdout


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Report when this checkout is out of date with origin/main, save uncommitted
work as commits, and merge origin/main into the current branch.

Usage:
  sync_local.py --check
  sync_local.py
  sync_local.py --repo <path> [--base main] [--remote origin]

--check only reports. It exits 0 when the current branch is not behind
origin/main and the worktree is clean, and 1 when the branch is behind or has
uncommitted work, so a script or a scheduled task can gate on it.

Without --check the tool saves tracked and non-ignored untracked work as commits
and merges origin/main. It never pushes and only fast-forwards main. A conflicting
merge is aborted when possible, and the saved commits remain in a backup ref.
"""
import argparse
import subprocess
import sys
import uuid
from dataclasses import dataclass
from pathlib import Path

PREFIX = "sync-local"
WORK_COMMIT_MESSAGE = "Save local work before syncing with main"
MAX_LISTED = 20

# A repository in one of these states is half way through an operation the tool
# must not disturb; finishing or aborting it is the user's decision.
IN_PROGRESS = {
    "rebase-merge": "a rebase is in progress",
    "rebase-apply": "a rebase or am is in progress",
    "MERGE_HEAD": "a merge is in progress",
    "CHERRY_PICK_HEAD": "a cherry-pick is in progress",
    "REVERT_HEAD": "a revert is in progress",
    "sequencer": "a multi-commit operation is in progress",
}


class Refused(Exception):
    pass


@dataclass
class Snapshot:
    branch: str | None
    behind: int
    ahead: int
    dirty: list[tuple[str, str]]
    behind_subjects: list[str]
    ahead_subjects: list[str]

    @property
    def up_to_date(self):
        return self.behind == 0 and not self.dirty


def git(repo, *args, check=True):
    result = subprocess.run(
        ["git", "-C", str(repo), *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if check and result.returncode != 0:
        detail = (result.stderr or result.stdout).strip()
        raise Refused(f"git {' '.join(args)} failed: {detail}")
    return result


def git_dir(repo):
    return Path(git(repo, "rev-parse", "--absolute-git-dir").stdout.strip())


def current_branch(repo):
    result = git(repo, "symbolic-ref", "--quiet", "--short", "HEAD", check=False)
    return result.stdout.strip() if result.returncode == 0 else None


def dirty_files(repo):
    """Tracked modifications, staged changes and untracked files, as (status, path)."""
    listed = git(repo, "status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none").stdout
    entries = []
    for line in listed.splitlines():
        if len(line) < 4:
            continue
        entries.append((line[:2].strip() or "??", line[3:]))
    return entries


def subjects(repo, revision_range):
    listed = git(repo, "log", "--format=%h %s", f"--max-count={MAX_LISTED}", revision_range).stdout
    return [line for line in listed.splitlines() if line.strip()]


def snapshot(repo, base_ref):
    counts = git(repo, "rev-list", "--left-right", "--count", f"{base_ref}...HEAD").stdout.split()
    return Snapshot(
        branch=current_branch(repo),
        behind=int(counts[0]),
        ahead=int(counts[1]),
        dirty=dirty_files(repo),
        behind_subjects=subjects(repo, f"HEAD..{base_ref}"),
        ahead_subjects=subjects(repo, f"{base_ref}..HEAD"),
    )


def report_lines(snap, base):
    head = "detached HEAD" if snap.branch is None else f"branch {snap.branch}"
    lines = [f"{head} ({snap.behind} behind {base}, {snap.ahead} ahead)"]
    if snap.dirty:
        lines.append(f"uncommitted work: {len(snap.dirty)} file(s)")
        lines += [f"  {status:<2} {path}" for status, path in snap.dirty[:MAX_LISTED]]
        if len(snap.dirty) > MAX_LISTED:
            lines.append(f"  ... {len(snap.dirty) - MAX_LISTED} more")
    else:
        lines.append("uncommitted work: none")
    if snap.behind_subjects:
        lines.append(f"new on {base}:")
        lines += [f"  {subject}" for subject in snap.behind_subjects]
    if snap.ahead_subjects:
        lines.append("only on this branch:")
        lines += [f"  {subject}" for subject in snap.ahead_subjects]
    return lines


def guard_state(repo):
    for name, message in IN_PROGRESS.items():
        if (git_dir(repo) / name).exists():
            raise Refused(f"{message}; finish or abort it first")


def fetch(repo, remote, base):
    base_ref = f"{remote}/{base}"
    git(repo, "fetch", "--prune", remote, f"+refs/heads/{base}:refs/remotes/{base_ref}")
    if git(repo, "rev-parse", "--verify", "--quiet", base_ref, check=False).returncode != 0:
        raise Refused(f"{base_ref} does not exist after fetching {remote} {base}")
    return base_ref


def save_work(repo):
    dirty = dirty_files(repo)
    if not dirty:
        print("saved: nothing to save")
        return None
    staged = git(repo, "diff", "--cached", "--quiet", check=False)
    if staged.returncode not in (0, 1):
        raise Refused("could not inspect staged changes")
    if staged.returncode == 1:
        git(repo, "commit", "-m", "Save staged work before syncing with main")
    git(repo, "add", "--all")
    staged = git(repo, "diff", "--cached", "--quiet", check=False)
    if staged.returncode not in (0, 1):
        raise Refused("could not inspect staged changes")
    if staged.returncode == 1:
        git(repo, "commit", "-m", WORK_COMMIT_MESSAGE)
    if dirty_files(repo):
        raise Refused("working tree changed during save; sync stopped with remaining work intact")
    revision = git(repo, "rev-parse", "--short", "HEAD").stdout.strip()
    print(f'saved {len(dirty)} file(s) as {revision} "{WORK_COMMIT_MESSAGE}"')
    return revision


def ensure_merged(repo, base_ref):
    if (git_dir(repo) / "MERGE_HEAD").exists() or git(repo, "merge-base", "--is-ancestor", base_ref, "HEAD", check=False).returncode != 0:
        raise Refused("merge did not finish; check git status before continuing")


def guard_ignored(repo, tree):
    ignored = git(repo, "ls-files", "--others", "--ignored", "--exclude-standard", "--directory", "-z").stdout
    if not ignored:
        return
    tracked = git(repo, "ls-tree", "-r", "--name-only", "-z", tree).stdout
    if "\ufffd" in ignored or "\ufffd" in tracked:
        raise Refused("could not safely compare ignored file names; sync stopped")
    files = {path.casefold() for path in tracked.split("\0") if path}
    parents = set()
    for path in files:
        parts = path.split("/")
        parents.update("/".join(parts[:i]) for i in range(1, len(parts)))
    for name in ignored.split("\0"):
        path = name.rstrip("/").casefold()
        if not path:
            continue
        parts = path.split("/")
        if path in files or path in parents or any("/".join(parts[:i]) in files for i in range(1, len(parts))):
            raise Refused(f"ignored local path may be overwritten: {name}; move it before syncing")


def run_check(repo, remote, base):
    base_ref = fetch(repo, remote, base)
    snap = snapshot(repo, base_ref)
    for line in report_lines(snap, base):
        print(line)
    if snap.up_to_date:
        print(f"{PREFIX}: up to date with {base}")
        return 0
    print(f"{PREFIX}: out of date with {base}; run without --check to save and sync", file=sys.stderr)
    return 1


def run_sync(repo, remote, base):
    base_ref = fetch(repo, remote, base)
    guard_state(repo)
    branch = current_branch(repo)
    if branch is None:
        raise Refused("HEAD is detached; check out a branch before syncing")
    options = git(repo, "config", "--get-all", f"branch.{branch}.mergeOptions", check=False)
    if options.returncode not in (0, 1):
        raise Refused("could not inspect branch merge options")
    if options.returncode == 0 and options.stdout.strip():
        raise Refused("branch merge options are configured; sync manually to preserve them")
    for line in git(repo, "status", "--porcelain=v2", "--ignore-submodules=none").stdout.splitlines():
        fields = line.split(" ", 3)
        if len(fields) > 2 and fields[2].startswith("S") and fields[2][2:] != "..":
            raise Refused("a submodule contains uncommitted work; save it before syncing")
    for line in report_lines(snapshot(repo, base_ref), base):
        print(line)
    save_work(repo)
    saved = snapshot(repo, base_ref)
    behind, ahead = saved.behind, saved.ahead
    if behind == 0:
        print(f"{PREFIX}: already on top of {base}")
        return 0
    if branch == base:
        if ahead:
            raise Refused(
                f"local {base} has {ahead} commit(s) not on {remote}/{base}; they are saved, "
                "move them onto a branch and sync that branch: git switch -c my-work"
            )
        guard_ignored(repo, base_ref)
        git(repo, "-c", "submodule.recurse=false", "merge", "--ff-only", "--no-squash", "--commit",
            "--no-autostash", "--no-overwrite-ignore", "--verify", base_ref)
        ensure_merged(repo, base_ref)
        print(f"{PREFIX}: fast-forwarded {base} to {remote}/{base}")
        return 0
    saved_head = git(repo, "rev-parse", "HEAD").stdout.strip()
    backup = f"refs/devsync/backups/{uuid.uuid4().hex}"
    git(repo, "update-ref", backup, saved_head)
    preview = git(repo, "merge-tree", "--write-tree", "HEAD", base_ref, check=False)
    if preview.returncode != 0:
        detail = "\n".join(part.strip() for part in (preview.stdout, preview.stderr) if part.strip())
        raise Refused(f"merge was not started; saved work remains at {backup}. {detail}")
    tree = preview.stdout.splitlines()[0].strip() if preview.stdout.splitlines() else ""
    if not tree:
        raise Refused(f"could not inspect the merge; saved work remains at {backup}")
    guard_ignored(repo, tree)
    result = git(repo, "-c", "submodule.recurse=false", "merge", "--no-edit", "--no-ff", "--no-squash", "--commit",
                 "--no-autostash", "--no-overwrite-ignore", "--verify", "-s", "ort", base_ref, check=False)
    if result.returncode != 0:
        detail = "\n".join(part.strip() for part in (result.stdout, result.stderr) if part.strip())
        if (git_dir(repo) / "MERGE_HEAD").exists():
            aborted = git(repo, "merge", "--abort", check=False)
            head = git(repo, "rev-parse", "HEAD", check=False)
            if aborted.returncode != 0 or head.returncode != 0 or head.stdout.strip() != saved_head:
                raise Refused(f"merge failed; abort did not restore HEAD. Saved work remains at {backup}. {detail}")
            raise Refused(f"merge was aborted; saved work remains at {backup}. {detail}")
        raise Refused(f"merge failed; saved work remains at {backup}. {detail}")
    ensure_merged(repo, base_ref)
    now = snapshot(repo, base_ref)
    print(f"{PREFIX}: merged {base_ref} into {branch}; {now.ahead} ahead, {now.behind} behind")
    return 0


def main(argv):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(
        prog="sync_local.py",
        description="Report when this checkout is out of date with origin/main, save uncommitted "
        "work as commits, and merge origin/main into the current branch.",
    )
    parser.add_argument("--check", action="store_true", help="only report; exit 1 when behind or dirty")
    parser.add_argument("--base", default="main", help="branch to sync with (default: main)")
    parser.add_argument("--remote", default="origin", help="remote to sync with (default: origin)")
    parser.add_argument("--repo", default=".", help="repository path (default: current directory)")
    args = parser.parse_args(argv)
    repo = Path(args.repo).resolve()
    try:
        root = git(repo, "rev-parse", "--show-toplevel").stdout.removesuffix("\n")
        if not root or "\ufffd" in root:
            raise Refused("could not resolve the repository root")
        repo = Path(root).resolve()
    except Refused:
        print(f"{PREFIX}: {repo} is not a git working tree", file=sys.stderr)
        return 1
    try:
        if args.check:
            return run_check(repo, args.remote, args.base)
        return run_sync(repo, args.remote, args.base)
    except Refused as error:
        print(f"{PREFIX}: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

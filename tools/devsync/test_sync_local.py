import contextlib
import importlib.util
import io
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

TOOL = Path(__file__).resolve().with_name("sync_local.py")
spec = importlib.util.spec_from_file_location("sync_local", TOOL)
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)

GITCONFIG = """[user]
\tname = Test
\temail = test@example.com
[commit]
\tgpgsign = false
[core]
\tautocrlf = false
[init]
\tdefaultBranch = main
[advice]
\tdetachedHead = false
[merge]
\tconflictStyle = merge
"""


class SyncLocalTests(unittest.TestCase):
    """Each test drives the tool against a real origin/main that moved, so a
    regression shows up as wrong git state rather than a wrong string: work lost
    by a rebase, a dirty tree after a sync, or a branch left behind main."""

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)

        config = self.root / "gitconfig"
        config.write_text(GITCONFIG, encoding="utf-8")
        # Isolate from the machine's own git config so the results are the same
        # on every PC and on CI.
        patcher = mock.patch.dict(
            os.environ, {"GIT_CONFIG_GLOBAL": str(config), "GIT_CONFIG_NOSYSTEM": "1"}
        )
        patcher.start()
        self.addCleanup(patcher.stop)

        self.origin = self.root / "origin.git"
        self.git(self.root, "init", "--bare", "--initial-branch=main", str(self.origin))

        self.seed = self.root / "seed"
        self.git(self.root, "clone", str(self.origin), str(self.seed))
        (self.seed / "a.txt").write_text("one\n", encoding="utf-8")
        self.git(self.seed, "add", "--all")
        self.commit(self.seed, "Initial commit")
        self.git(self.seed, "push", "origin", "main")

        self.local = self.root / "local"
        self.git(self.root, "clone", str(self.origin), str(self.local))
        self.git(self.local, "switch", "--create", "work")

    def git(self, cwd, *args):
        result = subprocess.run(
            ["git", "-C", str(cwd), *args],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        self.assertEqual(result.returncode, 0, f"git {' '.join(args)}: {result.stderr}")
        return result.stdout

    def commit(self, cwd, message):
        self.git(cwd, "commit", "--no-gpg-sign", "-m", message)

    def push_to_main(self, message, name="b.txt", content="remote\n"):
        """Land a commit on origin/main the way a teammate's merged PR does."""
        self.git(self.seed, "pull", "--rebase", "origin", "main")
        (self.seed / name).write_text(content, encoding="utf-8")
        self.git(self.seed, "add", "--all")
        self.commit(self.seed, message)
        self.git(self.seed, "push", "origin", "main")

    def run_tool(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = tool.main(["--repo", str(self.local), *args])
        return code, out.getvalue(), err.getvalue()

    def test_check_passes_on_a_clean_checkout_on_top_of_main(self):
        code, out, err = self.run_tool("--check")
        self.assertEqual(code, 0, err)
        self.assertIn("up to date with main", out)

    def test_check_sees_main_when_the_clone_refspec_is_narrow(self):
        self.git(self.local, "config", "remote.origin.fetch", "+refs/heads/other:refs/remotes/origin/other")
        self.push_to_main("Fix the water level")
        code, out, err = self.run_tool("--check")
        self.assertEqual(code, 1, err)
        self.assertIn("1 behind main", out)

    def test_check_reports_the_commits_that_landed_on_main(self):
        self.push_to_main("Fix the water level")
        code, out, err = self.run_tool("--check")
        self.assertEqual(code, 1)
        self.assertIn("1 behind main", out)
        self.assertIn("Fix the water level", out)
        self.assertIn("out of date with main", err)

    def test_sync_saves_uncommitted_work_and_merges_main(self):
        self.push_to_main("Fix the water level")
        (self.local / "work.txt").write_text("local work\n", encoding="utf-8")
        (self.local / "a.txt").write_text("one\nlocal edit\n", encoding="utf-8")

        code, out, err = self.run_tool()

        self.assertEqual(code, 0, err)
        self.assertIn("saved 2 file(s)", out)
        self.assertEqual(self.git(self.local, "status", "--porcelain").strip(), "")
        self.assertEqual((self.local / "work.txt").read_text(encoding="utf-8"), "local work\n")
        self.assertIn("local edit", (self.local / "a.txt").read_text(encoding="utf-8"))
        self.assertEqual(self.git(self.local, "log", "-1", "--format=%s", "HEAD^1").strip(), tool.WORK_COMMIT_MESSAGE)
        self.git(self.local, "merge-base", "--is-ancestor", "origin/main", "HEAD")
        self.assertEqual(self.git(self.local, "rev-list", "--count", "origin/main..HEAD").strip(), "2")

    def test_sync_preserves_both_staged_and_unstaged_versions(self):
        self.push_to_main("Advance main")
        path = self.local / "a.txt"
        path.write_text("staged version\n", encoding="utf-8")
        self.git(self.local, "add", "a.txt")
        path.write_text("unstaged version\n", encoding="utf-8")

        code, out, err = self.run_tool()

        self.assertEqual(code, 0, err)
        self.assertEqual(path.read_text(encoding="utf-8"), "unstaged version\n")
        self.assertEqual(self.git(self.local, "show", "HEAD~2:a.txt"), "staged version\n")
        self.assertEqual(self.git(self.local, "show", "HEAD:a.txt"), "unstaged version\n")

    def test_sync_respects_a_rejecting_commit_hook_without_losing_work(self):
        hook = tool.git_dir(self.local) / "hooks" / "pre-commit"
        hook.write_text("#!/bin/sh\nexit 1\n", encoding="utf-8")
        hook.chmod(0o755)
        path = self.local / "a.txt"
        path.write_text("staged version\n", encoding="utf-8")
        self.git(self.local, "add", "a.txt")
        path.write_text("unstaged version\n", encoding="utf-8")
        before = self.git(self.local, "rev-parse", "HEAD")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertEqual(self.git(self.local, "rev-parse", "HEAD"), before)
        self.assertEqual(self.git(self.local, "show", ":a.txt"), "staged version\n")
        self.assertEqual(path.read_text(encoding="utf-8"), "unstaged version\n")

    def test_conflict_aborts_the_merge_and_keeps_the_saved_commit(self):
        self.push_to_main("Rewrite the first file", name="a.txt", content="one\nremote edit\n")
        (self.local / "a.txt").write_text("one\nlocal edit\n", encoding="utf-8")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertIn("merge was not started", err)
        self.assertIn("a.txt", err)
        self.assertFalse((tool.git_dir(self.local) / "MERGE_HEAD").exists())
        self.assertEqual(self.git(self.local, "status", "--porcelain").strip(), "")
        self.assertEqual(self.git(self.local, "log", "-1", "--format=%s").strip(), tool.WORK_COMMIT_MESSAGE)
        self.assertEqual(self.git(self.local, "for-each-ref", "--format=%(objectname)", "refs/devsync/backups/").strip(),
                         self.git(self.local, "rev-parse", "HEAD").strip())

    def test_sync_refuses_to_overwrite_an_ignored_local_file(self):
        exclude = tool.git_dir(self.local) / "info" / "exclude"
        with exclude.open("a", encoding="utf-8") as out:
            out.write("\nprivate.cfg\n")
        path = self.local / "private.cfg"
        path.write_text("private local data\n", encoding="utf-8")
        self.push_to_main("Add configuration", name="private.cfg", content="default\n")
        before = self.git(self.local, "rev-parse", "HEAD")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")
        self.assertEqual(self.git(self.local, "rev-parse", "HEAD"), before)

    def test_subdirectory_input_protects_ignored_files_at_repository_root(self):
        with (tool.git_dir(self.local) / "info" / "exclude").open("a", encoding="utf-8") as out:
            out.write("\nprivate.cfg\n")
        path = self.local / "private.cfg"
        path.write_text("private local data\n", encoding="utf-8")
        subdir = self.local / "folder"
        subdir.mkdir()
        self.push_to_main("Add configuration", name="private.cfg", content="default\n")

        code, out, err = self.run_tool("--repo", str(subdir))

        self.assertEqual(code, 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")

    def test_sync_refuses_to_replace_an_ignored_directory_with_a_file(self):
        with (tool.git_dir(self.local) / "info" / "exclude").open("a", encoding="utf-8") as out:
            out.write("\nprivate-dir/\n")
        directory = self.local / "private-dir"
        directory.mkdir()
        path = directory / "local.txt"
        path.write_text("private local data\n", encoding="utf-8")
        self.push_to_main("Add file", name="private-dir", content="default\n")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")

    def test_sync_refuses_to_replace_an_ignored_file_with_a_directory(self):
        with (tool.git_dir(self.local) / "info" / "exclude").open("a", encoding="utf-8") as out:
            out.write("\nprivate-node\n")
        path = self.local / "private-node"
        path.write_text("private local data\n", encoding="utf-8")
        (self.seed / "private-node").mkdir()
        self.push_to_main("Add directory", name="private-node/child.txt", content="default\n")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")

    def test_sync_leaves_unrelated_ignored_files_alone(self):
        with (tool.git_dir(self.local) / "info" / "exclude").open("a", encoding="utf-8") as out:
            out.write("\nlocal-cache/\n")
        directory = self.local / "local-cache"
        directory.mkdir()
        path = directory / "local.txt"
        path.write_text("private local data\n", encoding="utf-8")
        self.push_to_main("Advance main")

        code, out, err = self.run_tool()

        self.assertEqual(code, 0, err)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")

    def test_sync_refuses_branch_merge_options_that_change_the_merge(self):
        self.git(self.local, "config", "--", "branch.work.mergeOptions", "--not-a-real-merge-option")
        self.push_to_main("Advance main")
        (self.local / "work.txt").write_text("local work\n", encoding="utf-8")
        before = self.git(self.local, "rev-parse", "HEAD")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertIn("branch merge options", err)
        self.assertEqual(self.git(self.local, "rev-parse", "HEAD"), before)
        self.assertEqual((self.local / "work.txt").read_text(encoding="utf-8"), "local work\n")

    def test_sync_preserves_existing_merge_history(self):
        self.git(self.local, "switch", "--create", "side")
        (self.local / "side.txt").write_text("side\n", encoding="utf-8")
        self.git(self.local, "add", "side.txt")
        self.commit(self.local, "Side work")
        self.git(self.local, "switch", "work")
        self.git(self.local, "merge", "--no-ff", "--no-edit", "side")
        original_merge = self.git(self.local, "rev-parse", "HEAD").strip()
        self.push_to_main("Advance main")
        (self.local / "work.txt").write_text("uncommitted\n", encoding="utf-8")

        code, out, err = self.run_tool()

        self.assertEqual(code, 0, err)
        self.git(self.local, "merge-base", "--is-ancestor", original_merge, "HEAD")
        self.assertEqual((self.local / "work.txt").read_text(encoding="utf-8"), "uncommitted\n")

    def test_sync_refuses_dirty_submodule(self):
        sub = self.root / "sub"
        self.git(self.root, "init", "--initial-branch=main", str(sub))
        (sub / "sub.txt").write_text("original\n", encoding="utf-8")
        self.git(sub, "add", "sub.txt")
        self.commit(sub, "Initial submodule")
        self.git(self.local, "-c", "protocol.file.allow=always", "submodule", "add", str(sub), "nested")
        self.commit(self.local, "Add submodule")
        path = self.local / "nested" / "sub.txt"
        path.write_text("uncommitted submodule work\n", encoding="utf-8")
        before = self.git(self.local, "rev-parse", "HEAD")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertIn("submodule contains uncommitted work", err)
        self.assertEqual(self.git(self.local, "rev-parse", "HEAD"), before)
        self.assertEqual(path.read_text(encoding="utf-8"), "uncommitted submodule work\n")

    def test_sync_refuses_while_a_rebase_is_already_in_progress(self):
        (tool.git_dir(self.local) / "rebase-merge").mkdir()
        (self.local / "work.txt").write_text("local work\n", encoding="utf-8")
        before = self.git(self.local, "rev-parse", "HEAD")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertIn("a rebase is in progress", err)
        self.assertEqual(self.git(self.local, "rev-parse", "HEAD"), before)

    def test_local_commits_on_main_are_refused_instead_of_rewritten(self):
        self.git(self.local, "switch", "main")
        (self.local / "main-work.txt").write_text("on main\n", encoding="utf-8")
        self.git(self.local, "add", "--all")
        self.commit(self.local, "Work done straight on main")
        self.push_to_main("Fix the water level")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertIn("move them onto a branch", err)
        # Nothing was rebased away: main still holds the local commit.
        self.assertIn(
            "Work done straight on main", self.git(self.local, "log", "--format=%s", "HEAD")
        )

    def test_main_fast_forward_refuses_squash_configuration(self):
        self.git(self.local, "switch", "main")
        self.git(self.local, "config", "--", "branch.main.mergeOptions", "--squash")
        self.push_to_main("Advance main")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertIn("branch merge options", err)
        self.assertNotEqual(self.git(self.local, "rev-parse", "HEAD"), self.git(self.local, "rev-parse", "origin/main"))
        self.assertEqual(self.git(self.local, "status", "--porcelain").strip(), "")

    def test_main_fast_forward_refuses_ignored_file_collision(self):
        self.git(self.local, "switch", "main")
        with (tool.git_dir(self.local) / "info" / "exclude").open("a", encoding="utf-8") as out:
            out.write("\nprivate.cfg\n")
        path = self.local / "private.cfg"
        path.write_text("private local data\n", encoding="utf-8")
        self.push_to_main("Add configuration", name="private.cfg", content="default\n")

        code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")



    def test_merge_timeout_with_merge_head_is_aborted_and_head_restored(self):
        self.push_to_main("Add a second file")
        (self.local / "c.txt").write_text("local\n", encoding="utf-8")
        real_git = tool.git

        def stalled_merge(repo, *args, **kwargs):
            if "--no-edit" in args:
                real_git(repo, "merge", "--no-ff", "--no-commit", args[-1])
                raise tool.GitTimeout("git merge timed out")
            return real_git(repo, *args, **kwargs)

        with mock.patch.object(tool, "git", side_effect=stalled_merge):
            code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertIn("merge was aborted", err)
        self.assertIn("timed out", err)
        self.assertFalse((tool.git_dir(self.local) / "MERGE_HEAD").exists())
        self.assertEqual(self.git(self.local, "log", "-1", "--format=%s").strip(), tool.WORK_COMMIT_MESSAGE)
        self.assertEqual(self.git(self.local, "status", "--porcelain").strip(), "")

    def test_merge_timeout_without_merge_head_resets_the_half_staged_index(self):
        self.push_to_main("Add a second file")
        (self.local / "c.txt").write_text("local\n", encoding="utf-8")
        real_git = tool.git

        def stalled_merge(repo, *args, **kwargs):
            if "--no-edit" in args:
                real_git(repo, "merge", "--no-ff", "--no-commit", args[-1])
                (tool.git_dir(repo) / "MERGE_HEAD").unlink()
                raise tool.GitTimeout("git merge timed out")
            return real_git(repo, *args, **kwargs)

        with mock.patch.object(tool, "git", side_effect=stalled_merge):
            code, out, err = self.run_tool()

        self.assertEqual(code, 1)
        self.assertIn("merge failed", err)
        self.assertEqual(self.git(self.local, "status", "--porcelain").strip(), "")


@unittest.skipUnless(os.environ.get("GARDEN_DEV_LAUNCHER"), "launcher executable not supplied")
class LauncherSafetyTests(unittest.TestCase):
    setUp = SyncLocalTests.setUp
    git = SyncLocalTests.git
    commit = SyncLocalTests.commit
    push_to_main = SyncLocalTests.push_to_main

    def run_launcher(self, repo=None):
        return subprocess.run([os.environ["GARDEN_DEV_LAUNCHER"], "--sync", str(repo or self.local)],
                              capture_output=True, text=True, timeout=120).returncode

    def test_preserves_staged_and_unstaged_versions(self):
        self.push_to_main("Advance main")
        path = self.local / "a.txt"
        path.write_text("staged version\n", encoding="utf-8")
        self.git(self.local, "add", "a.txt")
        path.write_text("unstaged version\n", encoding="utf-8")

        self.assertEqual(self.run_launcher(), 0)
        self.assertEqual(self.git(self.local, "show", "HEAD~2:a.txt"), "staged version\n")
        self.assertEqual(path.read_text(encoding="utf-8"), "unstaged version\n")
        self.git(self.local, "merge-base", "--is-ancestor", "origin/main", "HEAD")
        self.assertEqual(self.git(self.local, "status", "--porcelain").strip(), "")

    def test_refuses_to_overwrite_ignored_work(self):
        exclude = tool.git_dir(self.local) / "info" / "exclude"
        with exclude.open("a", encoding="utf-8") as out:
            out.write("\nprivate.cfg\n")
        path = self.local / "private.cfg"
        path.write_text("private local data\n", encoding="utf-8")
        self.push_to_main("Add configuration", name="private.cfg", content="default\n")

        self.assertEqual(self.run_launcher(), 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")

    def test_subdirectory_input_protects_ignored_files_at_repository_root(self):
        with (tool.git_dir(self.local) / "info" / "exclude").open("a", encoding="utf-8") as out:
            out.write("\nprivate.cfg\n")
        path = self.local / "private.cfg"
        path.write_text("private local data\n", encoding="utf-8")
        subdir = self.local / "folder"
        subdir.mkdir()
        self.push_to_main("Add configuration", name="private.cfg", content="default\n")

        self.assertEqual(self.run_launcher(subdir), 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")

    def test_refuses_to_replace_ignored_directory_with_file(self):
        with (tool.git_dir(self.local) / "info" / "exclude").open("a", encoding="utf-8") as out:
            out.write("\nprivate-dir/\n")
        directory = self.local / "private-dir"
        directory.mkdir()
        path = directory / "local.txt"
        path.write_text("private local data\n", encoding="utf-8")
        self.push_to_main("Add file", name="private-dir", content="default\n")

        self.assertEqual(self.run_launcher(), 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")

    def test_refuses_to_replace_ignored_file_with_directory(self):
        with (tool.git_dir(self.local) / "info" / "exclude").open("a", encoding="utf-8") as out:
            out.write("\nprivate-node\n")
        path = self.local / "private-node"
        path.write_text("private local data\n", encoding="utf-8")
        (self.seed / "private-node").mkdir()
        self.push_to_main("Add directory", name="private-node/child.txt", content="default\n")

        self.assertEqual(self.run_launcher(), 1)
        self.assertEqual(path.read_text(encoding="utf-8"), "private local data\n")

    def test_refuses_branch_merge_options_that_change_the_merge(self):
        self.git(self.local, "config", "--", "branch.work.mergeOptions", "--not-a-real-merge-option")
        self.push_to_main("Advance main")
        (self.local / "work.txt").write_text("local work\n", encoding="utf-8")
        before = self.git(self.local, "rev-parse", "HEAD")

        self.assertEqual(self.run_launcher(), 1)
        self.assertEqual(self.git(self.local, "rev-parse", "HEAD"), before)
        self.assertEqual((self.local / "work.txt").read_text(encoding="utf-8"), "local work\n")

    def test_refuses_when_commit_hook_rejects_save(self):
        hook = tool.git_dir(self.local) / "hooks" / "pre-commit"
        hook.write_text("#!/bin/sh\nexit 1\n", encoding="utf-8")
        hook.chmod(0o755)
        path = self.local / "a.txt"
        path.write_text("local work\n", encoding="utf-8")
        before = self.git(self.local, "rev-parse", "HEAD")

        self.assertEqual(self.run_launcher(), 1)
        self.assertEqual(self.git(self.local, "rev-parse", "HEAD"), before)
        self.assertEqual(path.read_text(encoding="utf-8"), "local work\n")

    def test_conflict_aborts_and_keeps_saved_work(self):
        self.push_to_main("Rewrite the first file", name="a.txt", content="remote edit\n")
        path = self.local / "a.txt"
        path.write_text("local edit\n", encoding="utf-8")

        self.assertEqual(self.run_launcher(), 1)
        self.assertFalse((tool.git_dir(self.local) / "MERGE_HEAD").exists())
        self.assertEqual(path.read_text(encoding="utf-8"), "local edit\n")
        self.assertEqual(self.git(self.local, "status", "--porcelain").strip(), "")
        self.assertEqual(self.git(self.local, "for-each-ref", "--format=%(objectname)", "refs/devsync/backups/").strip(),
                         self.git(self.local, "rev-parse", "HEAD").strip())

    def test_main_fast_forward_refuses_squash_configuration(self):
        self.git(self.local, "switch", "main")
        self.git(self.local, "config", "--", "branch.main.mergeOptions", "--squash")
        self.push_to_main("Advance main")

        self.assertEqual(self.run_launcher(), 1)
        self.assertNotEqual(self.git(self.local, "rev-parse", "HEAD"), self.git(self.local, "rev-parse", "origin/main"))
        self.assertEqual(self.git(self.local, "status", "--porcelain").strip(), "")

    def test_refuses_dirty_submodule(self):
        sub = self.root / "sub"
        self.git(self.root, "init", "--initial-branch=main", str(sub))
        (sub / "sub.txt").write_text("original\n", encoding="utf-8")
        self.git(sub, "add", "sub.txt")
        self.commit(sub, "Initial submodule")
        self.git(self.local, "-c", "protocol.file.allow=always", "submodule", "add", str(sub), "nested")
        self.commit(self.local, "Add submodule")
        path = self.local / "nested" / "sub.txt"
        path.write_text("uncommitted submodule work\n", encoding="utf-8")
        before = self.git(self.local, "rev-parse", "HEAD")

        self.assertEqual(self.run_launcher(), 1)
        self.assertEqual(self.git(self.local, "rev-parse", "HEAD"), before)
        self.assertEqual(path.read_text(encoding="utf-8"), "uncommitted submodule work\n")


def snap(**over):
    fields = dict(branch="work", behind=0, ahead=0, dirty=[], behind_subjects=[], ahead_subjects=[])
    fields.update(over)
    return tool.Snapshot(**fields)


class ReportLinesTests(unittest.TestCase):
    def test_clean_branch_on_top_of_main(self):
        self.assertEqual(
            tool.report_lines(snap(), "main"),
            ["branch work (0 behind main, 0 ahead)", "uncommitted work: none"],
        )

    def test_detached_head_is_named_as_such(self):
        self.assertEqual(
            tool.report_lines(snap(branch=None, behind=2, ahead=1), "origin/main")[0],
            "detached HEAD (2 behind origin/main, 1 ahead)",
        )

    def test_full_report_lists_dirty_files_then_behind_then_ahead(self):
        lines = tool.report_lines(
            snap(
                behind=1,
                ahead=2,
                dirty=[("M", "a.txt"), ("??", "new dir/b.txt")],
                behind_subjects=["abc1234 Fix water"],
                ahead_subjects=["def5678 Mine", "0123456 Also mine"],
            ),
            "main",
        )
        self.assertEqual(
            lines,
            [
                "branch work (1 behind main, 2 ahead)",
                "uncommitted work: 2 file(s)",
                "  M  a.txt",
                "  ?? new dir/b.txt",
                "new on main:",
                "  abc1234 Fix water",
                "only on this branch:",
                "  def5678 Mine",
                "  0123456 Also mine",
            ],
        )

    def test_dirty_list_is_cut_at_the_limit(self):
        dirty = [("M", f"f{i}.txt") for i in range(tool.MAX_LISTED + 3)]
        lines = tool.report_lines(snap(dirty=dirty), "main")
        self.assertEqual(lines[1], "uncommitted work: 23 file(s)")
        self.assertEqual(lines[2], "  M  f0.txt")
        self.assertEqual(lines[21], "  M  f19.txt")
        self.assertEqual(lines[22], "  ... 3 more")
        self.assertEqual(len(lines), 23)

    def test_exactly_the_limit_has_no_more_line(self):
        dirty = [("M", f"f{i}.txt") for i in range(tool.MAX_LISTED)]
        lines = tool.report_lines(snap(dirty=dirty), "main")
        self.assertEqual(len(lines), 2 + tool.MAX_LISTED)
        self.assertNotIn("more", lines[-1])


class SnapshotTests(unittest.TestCase):
    def test_up_to_date_needs_not_behind_and_clean(self):
        self.assertTrue(snap().up_to_date)
        self.assertTrue(snap(ahead=5).up_to_date)
        self.assertFalse(snap(behind=1).up_to_date)
        self.assertFalse(snap(dirty=[("M", "a")]).up_to_date)


class GitTimeoutTests(unittest.TestCase):
    def test_a_stalled_git_call_is_bounded_unattended_and_reported(self):
        expired = subprocess.TimeoutExpired("git", tool.GIT_TIMEOUT_SECONDS)
        with mock.patch.object(tool, "run_bounded", side_effect=expired) as run:
            with self.assertRaises(tool.Refused) as refused:
                tool.git(".", "fetch")
            self.assertIn("timed out", str(refused.exception))
            with self.assertRaises(tool.GitTimeout):
                tool.git(".", "merge", check=False)
            command, env, timeout = run.call_args.args
            self.assertEqual(timeout, tool.GIT_TIMEOUT_SECONDS)
            self.assertEqual(env["GIT_TERMINAL_PROMPT"], "0")

    def test_timeout_kills_a_grandchild_holding_the_pipes(self):
        code = (
            "import subprocess, sys, time\n"
            "subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])\n"
            "time.sleep(60)\n"
        )
        start = time.monotonic()
        with self.assertRaises(subprocess.TimeoutExpired):
            tool.run_bounded([sys.executable, "-c", code], os.environ.copy(), 1)
        self.assertLess(time.monotonic() - start, 20)


if __name__ == "__main__":
    unittest.main()

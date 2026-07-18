from __future__ import annotations

import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from scripts.agent_loop.git_scope import (
    AGENTS_DIR_NAME,
    AgentsDirectorySnapshot,
    build_git_scoped_environment,
    build_runtime_temp_environment,
    cleanup_agents_directory,
    cleanup_runtime_temp_directory,
    git_safe_directory_value,
    initial_agents_runtime,
    initial_runtime_temp_state,
    inspect_agents_directory,
    reconcile_agents_runtime,
)


def repo_root(tmp: str) -> Path:
    repo = Path(tmp).resolve()
    (repo / ".git").mkdir(exist_ok=True)
    return repo


class AgentLoopGitScopeTest(unittest.TestCase):
    def test_platform_detection_defaults_to_host_os_name(self) -> None:
        from scripts.agent_loop import git_scope

        self.assertEqual(os.name == "nt", git_scope._is_windows())

    def test_build_git_scoped_environment_without_existing_count(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            base = {"PATH": "x"}
            scoped = build_git_scoped_environment(base, repo)
            self.assertEqual({"PATH": "x"}, base)
            self.assertEqual("1", scoped.environment["GIT_CONFIG_COUNT"])
            self.assertEqual("safe.directory", scoped.environment["GIT_CONFIG_KEY_0"])
            self.assertEqual(git_safe_directory_value(repo), scoped.environment["GIT_CONFIG_VALUE_0"])
            self.assertTrue(scoped.injected)
            self.assertEqual(0, scoped.preserved_entries)

    def test_build_git_scoped_environment_with_explicit_zero_count(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            scoped = build_git_scoped_environment({"GIT_CONFIG_COUNT": "0"}, repo)
            self.assertEqual("1", scoped.environment["GIT_CONFIG_COUNT"])
            self.assertEqual("safe.directory", scoped.environment["GIT_CONFIG_KEY_0"])
            self.assertEqual(git_safe_directory_value(repo), scoped.environment["GIT_CONFIG_VALUE_0"])

    def test_build_git_scoped_environment_preserves_existing_entries(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            base = {
                "GIT_CONFIG_COUNT": "2",
                "GIT_CONFIG_KEY_0": "core.fsmonitor",
                "GIT_CONFIG_VALUE_0": "false",
                "GIT_CONFIG_KEY_1": "safe.directory",
                "GIT_CONFIG_VALUE_1": "D:/Other/Repo",
            }
            scoped = build_git_scoped_environment(base, repo)
            self.assertEqual("3", scoped.environment["GIT_CONFIG_COUNT"])
            self.assertEqual("core.fsmonitor", scoped.environment["GIT_CONFIG_KEY_0"])
            self.assertEqual("false", scoped.environment["GIT_CONFIG_VALUE_0"])
            self.assertEqual("safe.directory", scoped.environment["GIT_CONFIG_KEY_1"])
            self.assertEqual("D:/Other/Repo", scoped.environment["GIT_CONFIG_VALUE_1"])
            self.assertEqual("safe.directory", scoped.environment["GIT_CONFIG_KEY_2"])
            self.assertEqual(git_safe_directory_value(repo), scoped.environment["GIT_CONFIG_VALUE_2"])
            self.assertEqual(2, scoped.preserved_entries)

    def test_build_git_scoped_environment_does_not_duplicate_identical_safe_directory(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            value = git_safe_directory_value(repo)
            base = {
                "GIT_CONFIG_COUNT": "1",
                "GIT_CONFIG_KEY_0": "safe.directory",
                "GIT_CONFIG_VALUE_0": value,
            }
            scoped = build_git_scoped_environment(base, repo)
            self.assertEqual("1", scoped.environment["GIT_CONFIG_COUNT"])
            self.assertFalse(scoped.injected)
            self.assertEqual(1, scoped.preserved_entries)

    def test_build_git_scoped_environment_rejects_invalid_count_and_wildcard(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            for invalid in ("", "abc", "-1", "+1", " 1", "01", "999"):
                with self.subTest(invalid=invalid):
                    with self.assertRaisesRegex(ValueError, "codex.invalid_git_config_environment"):
                        build_git_scoped_environment({"GIT_CONFIG_COUNT": invalid}, repo)

            with self.assertRaisesRegex(ValueError, "wildcard safe.directory"):
                build_git_scoped_environment(
                    {
                        "GIT_CONFIG_COUNT": "1",
                        "GIT_CONFIG_KEY_0": "safe.directory",
                        "GIT_CONFIG_VALUE_0": "*",
                    },
                    repo,
                )

    def test_build_git_scoped_environment_rejects_incomplete_entries_and_relative_repo(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            with self.assertRaisesRegex(ValueError, "incomplete GIT_CONFIG"):
                build_git_scoped_environment({"GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "x"}, repo)
            with self.assertRaisesRegex(ValueError, "incomplete GIT_CONFIG"):
                build_git_scoped_environment({"GIT_CONFIG_COUNT": "1", "GIT_CONFIG_VALUE_0": "x"}, repo)
            with self.assertRaisesRegex(ValueError, "incomplete GIT_CONFIG"):
                build_git_scoped_environment({"GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "", "GIT_CONFIG_VALUE_0": "x"}, repo)
            with self.assertRaisesRegex(ValueError, "repository path must be absolute"):
                build_git_scoped_environment({}, Path("relative/repo"))

    def test_build_git_scoped_environment_preserves_os_environ(self) -> None:
        before = dict(os.environ)
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            build_git_scoped_environment(os.environ, repo)
        self.assertEqual(before, dict(os.environ))

    def test_git_safe_directory_value_uses_forward_slashes_on_windows(self) -> None:
        with tempfile.TemporaryDirectory(prefix="Repo With Spaces ") as tmp:
            repo = repo_root(tmp)
            os_name_before = os.name
            with mock.patch("scripts.agent_loop.git_scope._is_windows", return_value=True):
                value = git_safe_directory_value(repo)
            self.assertEqual(os_name_before, os.name)
            self.assertIsInstance(repo, Path)
            self.assertIn("/", value)
            self.assertNotIn("\\", value)
            self.assertNotEqual("*", value)

    def test_metadata_is_redacted(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            scoped = build_git_scoped_environment({}, repo)
            self.assertEqual("<repo>", scoped.metadata()["git_safe_directory_repo"])
            self.assertNotIn(str(repo), str(scoped.metadata()))

    def test_build_git_scoped_environment_windows_aliases_are_normalized_and_ambiguous_duplicates_fail(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            os_name_before = os.name
            with mock.patch("scripts.agent_loop.git_scope._is_windows", return_value=True):
                self.assertEqual(os_name_before, os.name)
                self.assertEqual(type(repo), type(Path(tmp)))
                scoped = build_git_scoped_environment(
                    {
                        "git_config_count": "1",
                        "git_config_key_0": "core.fsmonitor",
                        "git_config_value_0": "false",
                    },
                    repo,
                )
                self.assertNotIn("git_config_count", scoped.environment)
                self.assertEqual("2", scoped.environment["GIT_CONFIG_COUNT"])
                self.assertEqual("core.fsmonitor", scoped.environment["GIT_CONFIG_KEY_0"])
                self.assertEqual("false", scoped.environment["GIT_CONFIG_VALUE_0"])
                self.assertEqual("safe.directory", scoped.environment["GIT_CONFIG_KEY_1"])
                self.assertEqual(os_name_before, os.name)

                with self.assertRaisesRegex(ValueError, "ambiguous environment entries for GIT_CONFIG_COUNT"):
                    build_git_scoped_environment({"GIT_CONFIG_COUNT": "0", "git_config_count": "0"}, repo)
                with self.assertRaisesRegex(ValueError, "ambiguous environment entries for GIT_CONFIG_KEY_0"):
                    build_git_scoped_environment(
                        {
                            "GIT_CONFIG_COUNT": "1",
                            "GIT_CONFIG_KEY_0": "core.fsmonitor",
                            "git_config_key_0": "core.editor",
                            "GIT_CONFIG_VALUE_0": "false",
                        },
                        repo,
                    )
                with self.assertRaisesRegex(ValueError, "ambiguous environment entries for GIT_CONFIG_VALUE_0"):
                    build_git_scoped_environment(
                        {
                            "GIT_CONFIG_COUNT": "1",
                            "GIT_CONFIG_KEY_0": "core.fsmonitor",
                            "GIT_CONFIG_VALUE_0": "false",
                            "git_config_value_0": "true",
                        },
                        repo,
                    )

    def test_build_git_scoped_environment_rejects_git_config_parameters(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            with self.assertRaisesRegex(ValueError, "GIT_CONFIG_PARAMETERS is not supported"):
                build_git_scoped_environment({"GIT_CONFIG_PARAMETERS": "safe.directory=*"}, repo)
        with tempfile.TemporaryDirectory() as tmp, mock.patch("scripts.agent_loop.git_scope._is_windows", return_value=True):
            repo = repo_root(tmp)
            os_name_before = os.name
            with self.assertRaisesRegex(ValueError, "GIT_CONFIG_PARAMETERS is not supported"):
                build_git_scoped_environment({"git_config_parameters": "safe.directory=*"}, repo)
            self.assertEqual(os_name_before, os.name)

    def test_runtime_temp_environment_injects_temp_tmp_tmpdir_without_mutating_inputs(self) -> None:
        with tempfile.TemporaryDirectory(prefix="Repo With Spaces ") as tmp:
            repo = repo_root(tmp)
            run_dir = repo / ".agent-loop" / "runs" / "run with spaces"
            run_dir.mkdir(parents=True)
            base = {"PATH": "x", "TEMP": "old", "TMP": "old2", "TMPDIR": "old3"}
            before_environ = dict(os.environ)
            git_scoped = build_git_scoped_environment(base, repo)
            runtime = build_runtime_temp_environment(git_scoped.environment, repo, run_dir)
            expected = str((run_dir / "runtime-tmp").resolve())
            self.assertEqual({"PATH": "x", "TEMP": "old", "TMP": "old2", "TMPDIR": "old3"}, base)
            self.assertEqual(before_environ, dict(os.environ))
            for key in ("TEMP", "TMP", "TMPDIR"):
                self.assertEqual(expected, runtime.environment[key])
            self.assertEqual("1", runtime.environment["GIT_CONFIG_COUNT"])
            self.assertEqual("safe.directory", runtime.environment["GIT_CONFIG_KEY_0"])
            self.assertEqual(git_safe_directory_value(repo), runtime.environment["GIT_CONFIG_VALUE_0"])
            self.assertTrue(runtime.runtime_state["created_by_run"])
            self.assertEqual("ephemeral_runtime_temp", runtime.runtime_state["classification"])

    def test_runtime_temp_environment_windows_aliases_are_normalized(self) -> None:
        with tempfile.TemporaryDirectory() as tmp, mock.patch("scripts.agent_loop.git_scope._is_windows", return_value=True):
            repo = repo_root(tmp)
            run_dir = repo / ".agent-loop" / "runs" / "runtime"
            run_dir.mkdir(parents=True)
            runtime = build_runtime_temp_environment({"temp": "x", "Tmp": "y", "tmpdir": "z"}, repo, run_dir)
            self.assertNotIn("temp", runtime.environment)
            self.assertNotIn("Tmp", runtime.environment)
            self.assertNotIn("tmpdir", runtime.environment)
            self.assertIn("TEMP", runtime.environment)
            self.assertIn("TMP", runtime.environment)
            self.assertIn("TMPDIR", runtime.environment)
            self.assertEqual(runtime.environment["TEMP"], runtime.environment["TMP"])
            self.assertEqual(runtime.environment["TEMP"], runtime.environment["TMPDIR"])

    def test_runtime_temp_cleanup_removes_only_new_empty_directory(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            run_dir = repo / ".agent-loop" / "runs" / "cleanup"
            run_dir.mkdir(parents=True)
            runtime = build_runtime_temp_environment({}, repo, run_dir)
            state, error = cleanup_runtime_temp_directory(runtime.runtime_state, repo, run_dir)
            self.assertIsNone(error)
            self.assertFalse((run_dir / "runtime-tmp").exists())
            self.assertTrue(state["removed"])

    def test_runtime_temp_preexisting_or_nonempty_is_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            run_dir = repo / ".agent-loop" / "runs" / "preexisting"
            temp_dir = run_dir / "runtime-tmp"
            temp_dir.mkdir(parents=True)
            runtime = build_runtime_temp_environment({}, repo, run_dir)
            state, error = cleanup_runtime_temp_directory(runtime.runtime_state, repo, run_dir)
            self.assertIsNone(error)
            self.assertTrue(temp_dir.exists())
            self.assertFalse(state["removed"])
            self.assertEqual("preexisting", state["classification"])

        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            run_dir = repo / ".agent-loop" / "runs" / "nonempty"
            run_dir.mkdir(parents=True)
            runtime = build_runtime_temp_environment({}, repo, run_dir)
            temp_dir = run_dir / "runtime-tmp"
            (temp_dir / "leftover.txt").write_text("x", encoding="utf-8")
            state, error = cleanup_runtime_temp_directory(runtime.runtime_state, repo, run_dir)
            self.assertIn("contains entries", error or "")
            self.assertTrue(temp_dir.exists())
            self.assertTrue(state["cleanup_pending"])

    def test_runtime_temp_resume_keeps_created_by_run_classification(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            run_dir = repo / ".agent-loop" / "runs" / "resume"
            run_dir.mkdir(parents=True)
            runtime = build_runtime_temp_environment({}, repo, run_dir)
            resumed = build_runtime_temp_environment({}, repo, run_dir, runtime.runtime_state)
            self.assertTrue(resumed.runtime_state["created_by_run"])
            self.assertFalse(resumed.runtime_state["existed_before_run"])
            self.assertEqual("ephemeral_runtime_temp", resumed.runtime_state["classification"])

    def test_runtime_temp_environment_rejects_unsafe_types_and_outside_run_root(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            run_dir = repo / ".agent-loop" / "runs" / "unsafe"
            run_dir.mkdir(parents=True)
            temp_path = run_dir / "runtime-tmp"
            temp_path.write_text("x", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "runtime temp must be an ordinary directory"):
                build_runtime_temp_environment({}, repo, run_dir)

        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            outside = repo / "not-runs"
            outside.mkdir()
            with self.assertRaisesRegex(ValueError, "must stay under .agent-loop/runs"):
                build_runtime_temp_environment({}, repo, outside)

    def test_initial_agents_runtime_and_inspect_absent(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            runtime = initial_agents_runtime(repo)
            snapshot = inspect_agents_directory(repo)
            self.assertFalse(snapshot.exists)
            self.assertFalse(runtime["existed_before_run"])
            self.assertEqual("absent", runtime["classification"])

    def test_agents_directory_created_empty_is_ephemeral_then_removed(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            runtime = initial_agents_runtime(repo)
            agents = repo / AGENTS_DIR_NAME
            agents.mkdir()
            runtime, error = reconcile_agents_runtime(runtime, inspect_agents_directory(repo))
            self.assertIsNone(error)
            self.assertTrue(runtime["created_by_run"])
            self.assertEqual("ephemeral_runtime_artifact", runtime["classification"])
            runtime, error = cleanup_agents_directory(runtime, repo)
            self.assertIsNone(error)
            self.assertFalse(agents.exists())
            self.assertTrue(runtime["removed"])

    def test_agents_directory_preexisting_empty_is_not_removed(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            agents = repo / AGENTS_DIR_NAME
            agents.mkdir()
            runtime = initial_agents_runtime(repo)
            runtime, error = reconcile_agents_runtime(runtime, inspect_agents_directory(repo))
            self.assertIsNone(error)
            runtime, error = cleanup_agents_directory(runtime, repo)
            self.assertIsNone(error)
            self.assertTrue(agents.exists())
            self.assertFalse(runtime["removed"])
            self.assertEqual("preexisting", runtime["classification"])

    def test_agents_directory_new_nonempty_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            runtime = initial_agents_runtime(repo)
            agents = repo / AGENTS_DIR_NAME
            agents.mkdir()
            (agents / "hidden.txt").write_text("x", encoding="utf-8")
            runtime, error = reconcile_agents_runtime(runtime, inspect_agents_directory(repo))
            self.assertEqual("runtime .agents contains entries", error)
            self.assertEqual("ephemeral_runtime_artifact_nonempty", runtime["classification"])
            runtime, cleanup_error = cleanup_agents_directory(runtime, repo)
            self.assertIn("not empty", cleanup_error or "")
            self.assertTrue(agents.exists())

    def test_agents_directory_regular_file_and_symlink_are_unsafe(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            runtime = initial_agents_runtime(repo)
            agents = repo / AGENTS_DIR_NAME
            agents.write_text("x", encoding="utf-8")
            runtime, error = reconcile_agents_runtime(runtime, inspect_agents_directory(repo))
            self.assertIn("not an ordinary directory", error or "")
            self.assertEqual("unsafe_type", runtime["classification"])

        if hasattr(os, "symlink"):
            with tempfile.TemporaryDirectory() as tmp:
                repo = repo_root(tmp)
                target = repo / "target"
                target.mkdir()
                runtime = initial_agents_runtime(repo)
                agents = repo / AGENTS_DIR_NAME
                try:
                    os.symlink(target, agents, target_is_directory=True)
                except OSError:
                    self.skipTest("symlink creation unavailable")
                runtime, error = reconcile_agents_runtime(runtime, inspect_agents_directory(repo))
                self.assertIn("not an ordinary directory", error or "")

    def test_cleanup_revalidates_runtime_and_uses_rmdir_only(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            agents = repo / AGENTS_DIR_NAME
            agents.mkdir()
            runtime = {
                "path": AGENTS_DIR_NAME,
                "existed_before_run": False,
                "created_by_run": True,
                "cleanup_pending": True,
                "classification": "ephemeral_runtime_artifact",
            }
            with mock.patch("scripts.agent_loop.git_scope.os.rmdir", side_effect=OSError("locked")) as rmdir:
                runtime, error = cleanup_agents_directory(runtime, repo)
            self.assertEqual(1, rmdir.call_count)
            self.assertIn("ephemeral_cleanup_failed", error or "")
            self.assertTrue(agents.exists())

    def test_reconcile_agents_runtime_simulated_reparse_point_is_unsafe(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            runtime = initial_agents_runtime(repo)
            snapshot = AgentsDirectorySnapshot(repo / AGENTS_DIR_NAME, True, True, False, True, 0)
            runtime, error = reconcile_agents_runtime(runtime, snapshot)
            self.assertIn("not an ordinary directory", error or "")
            self.assertEqual("unsafe_type", runtime["classification"])

    def test_agents_other_directory_has_no_special_handling(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = repo_root(tmp)
            (repo / ".agents-other").mkdir()
            snapshot = inspect_agents_directory(repo)
            runtime = initial_agents_runtime(repo)
            self.assertFalse(snapshot.exists)
            self.assertEqual("absent", runtime["classification"])

    def test_canonical_repo_root_rejects_missing_git_repo_and_unc(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp).resolve()
            with self.assertRaisesRegex(ValueError, "Git repository"):
                build_git_scoped_environment({}, repo)
        with mock.patch("scripts.agent_loop.git_scope._is_windows", return_value=True):
            os_name_before = os.name
            unc = "//server/share/repo"
            with self.assertRaisesRegex(ValueError, "UNC repository paths are not supported"):
                build_git_scoped_environment({}, unc)
            self.assertEqual(os_name_before, os.name)


if __name__ == "__main__":
    unittest.main()

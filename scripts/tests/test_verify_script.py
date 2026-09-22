"""Verification phase selection and failure propagation without builds or external services."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class VerifyScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "scripts").mkdir()
        shutil.copy2(Path(__file__).resolve().parents[1] / "verify.sh", self.root / "scripts")
        for name in ("Application", "Domain", "E2E", "Deployment", "Future"):
            project = self.root / f"OwnPlanner.{name}.Tests"
            project.mkdir()
            (project / f"{project.name}.csproj").touch()
        mock_bin = self.root / "bin"
        mock_bin.mkdir()
        mock = '''#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
with open(os.environ["COMMAND_LOG"], "a") as log:
    log.write(json.dumps({"tool": Path(sys.argv[0]).name, "args": sys.argv[1:]}) + "\\n")
if "test" in sys.argv:
    sys.exit(int(os.environ.get("TEST_EXIT_CODE", "0")))
'''
        for name in ("dotnet", "npm"):
            path = mock_bin / name
            path.write_text(mock)
            path.chmod(0o755)
        self.log = self.root / "commands.jsonl"
        self.env = dict(os.environ, PATH=f"{mock_bin}:{os.environ['PATH']}",
                        COMMAND_LOG=str(self.log))

    def run_verify(self, *args, **env):
        self.log.unlink(missing_ok=True)
        result = subprocess.run(["bash", str(self.root / "scripts/verify.sh"), *args],
                                env=dict(self.env, **env), capture_output=True, text=True)
        commands = [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []
        return result, commands

    def test_modes_select_suites_and_preserve_e2e_report(self):
        for mode in ((), ("--all",), ("--backend",), ("--frontend",), ("--e2e",)):
            with self.subTest(mode=mode):
                result, commands = self.run_verify(*mode)
                self.assertEqual(result.returncode, 0, result.stderr)
                tests = [c["args"] for c in commands if c["args"][0] == "test"]
                projects = {Path(args[args.index("--project") + 1]).stem for args in tests}
                expected = set()
                if mode in ((), ("--all",), ("--backend",)):
                    expected.update(f"OwnPlanner.{name}.Tests" for name in ("Application", "Domain", "Future"))
                if mode in ((), ("--all",), ("--e2e",)):
                    expected.add("OwnPlanner.E2E.Tests")
                self.assertEqual(projects, expected)
                for args in tests:
                    if "Category=E2E" in args and "--filter-trait" in args:
                        self.assertEqual(args[args.index("--filter-trait") + 1], "Category=E2E")
                        self.assertIn("--report-xunit-trx", args)
                        self.assertEqual(args[args.index("--report-xunit-trx-filename") + 1], "e2e.trx")
                        self.assertEqual(args[args.index("--results-directory") + 1], str(self.root / "TestResults/E2E"))
                    else:
                        self.assertIn("--filter-not-trait", args)
                        for category in ("E2E", "DeploymentSmoke", "LiveAi"):
                            self.assertIn(f"Category={category}", args)
                frontend = [c["args"][-1] for c in commands if c["tool"] == "npm"]
                if mode == ("--backend",):
                    expected_frontend = []
                elif mode == ("--e2e",):
                    expected_frontend = ["build"]
                else:
                    expected_frontend = ["lint", "build"]
                self.assertEqual(frontend, expected_frontend)

    def test_failed_or_empty_backend_suite_stops_before_e2e(self):
        for exit_code in ("2", "8"):
            with self.subTest(exit_code=exit_code):
                result, commands = self.run_verify("--all", TEST_EXIT_CODE=exit_code)
                self.assertEqual(result.returncode, int(exit_code))
                tests = [c for c in commands if c["args"][0] == "test"]
                self.assertEqual(len(tests), 1)
                self.assertNotIn("--ignore-exit-code", tests[0]["args"])


if __name__ == "__main__":
    unittest.main()

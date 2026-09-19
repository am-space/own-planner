"""Run with python3 -m unittest discover -s scripts/tests (no Docker/provider access)."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class DeploymentWrapperTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        scripts = self.root / "scripts"
        scripts.mkdir()
        for source in Path(__file__).resolve().parents[1].glob("docker-*.sh"):
            shutil.copy2(source, scripts / source.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        mock = '''#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
with open(os.environ["COMMAND_LOG"], "a") as log:
    log.write(json.dumps({"tool": Path(sys.argv[0]).name, "args": sys.argv[1:],
        "project": os.environ.get("COMPOSE_PROJECT_NAME"),
        "port": os.environ.get("OWNPLANNER_PORT")}) + "\\n")
if "up" in sys.argv and os.environ.get("FAIL_START") == "true":
    sys.exit(42)
if "port" in sys.argv:
    print("127.0.0.1:49152")
'''
        for name in ("docker", "dotnet"):
            path = self.bin / name
            path.write_text(mock)
            path.chmod(0o755)
        self.log = self.root / "commands.jsonl"
        self.env = dict(os.environ, PATH=f"{self.bin}:{os.environ['PATH']}",
                        COMMAND_LOG=str(self.log), COMPOSE_PROJECT_NAME="existing-planner",
                        OWNPLANNER_PORT="8080", GEMINI_API_KEY="fake-test-key")

    def run_wrapper(self, name, **env):
        self.log.unlink(missing_ok=True)
        result = subprocess.run(["bash", str(self.root / "scripts" / name)],
                                env=dict(self.env, **env), capture_output=True, text=True)
        commands = [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []
        return result, commands

    def test_success_and_start_failure_clean_only_their_own_project(self):
        projects = set()
        for wrapper in ("docker-smoke-test.sh", "docker-live-ai-test.sh"):
            for fail in ("false", "true"):
                with self.subTest(wrapper=wrapper, fail=fail):
                    result, commands = self.run_wrapper(wrapper, FAIL_START=fail)
                    self.assertEqual(result.returncode, 42 if fail == "true" else 0, result.stderr)
                    project = commands[0]["project"]
                    self.assertRegex(project, r"^ownplanner-test-[a-z0-9]+$")
                    self.assertNotIn(project, projects)
                    projects.add(project)
                    self.assertTrue(all(c["project"] == project and c["port"] == "0" for c in commands))
                    cleanup = [c for c in commands if "down" in c["args"]]
                    self.assertEqual(len(cleanup), 1)
                    self.assertIn("--volumes", cleanup[0]["args"])
                    tests = [c for c in commands if c["tool"] == "dotnet"]
                    self.assertEqual(len(tests), 0 if fail == "true" else 1)

    def test_dotenv_key_alone_does_not_authorize_live_test(self):
        (self.root / ".env").write_text("GEMINI_API_KEY=fake-dotenv-key\n")
        result, commands = self.run_wrapper("docker-live-ai-test.sh", GEMINI_API_KEY="")
        self.assertEqual(result.returncode, 2)
        self.assertIn("GEMINI_API_KEY is required", result.stderr)
        self.assertEqual(commands, [])


if __name__ == "__main__":
    unittest.main()

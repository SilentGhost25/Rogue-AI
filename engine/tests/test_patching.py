import subprocess, tempfile, unittest
from pathlib import Path
from breach_engine.patching import apply_attack, git


def make_repo(tmp: Path) -> Path:
    repo = tmp / "repo"
    repo.mkdir()
    subprocess.run(["git", "init", "-q", str(repo)], check=True)
    git(repo, "config", "user.email", "t@t")
    git(repo, "config", "user.name", "t")
    (repo / "routes").mkdir()
    (repo / "routes" / "search.js").write_text("safe\n")
    git(repo, "add", "-A")
    git(repo, "commit", "-m", "base")
    return repo


class PatchingTests(unittest.TestCase):
    def test_overwrite_ladder_is_idempotent(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = Path(d)
            repo = make_repo(tmp)
            atk = tmp / "atk"
            (atk / "corrupted" / "routes").mkdir(parents=True)
            (atk / "corrupted" / "routes" / "search.js").write_text("evil\n")
            (repo / "routes" / "search.js").write_text("participant refactor\n")
            self.assertEqual(apply_attack(repo, atk, 2, ["routes/search.js"]), "overwritten")
            self.assertEqual((repo / "routes" / "search.js").read_text(), "evil\n")
            log = git(repo, "log", "--format=%s").stdout.splitlines()
            self.assertIn("pre-attack-2", log)
            self.assertEqual(apply_attack(repo, atk, 2, ["routes/search.js"]), "skipped")


if __name__ == "__main__":
    unittest.main()

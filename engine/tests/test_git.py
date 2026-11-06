import tempfile, unittest
from pathlib import Path
from breach_engine.patching import branch_name, commit_snapshot, ensure_repo, git


class GitTests(unittest.TestCase):
    def test_participant_branch_commit_and_tag(self):
        with tempfile.TemporaryDirectory() as d:
            repo = Path(d) / "p123abc"
            repo.mkdir()
            (repo / "a.js").write_text("one\n")
            ensure_repo(repo)
            self.assertEqual(git(repo, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip(), "participant/p123abc")
            self.assertEqual(commit_snapshot(repo, "no change"), "clean")
            (repo / "a.js").write_text("two\n")
            self.assertEqual(commit_snapshot(repo, "edit", tag="submission-s1"), "committed")
            self.assertIn("submission-s1", git(repo, "tag").stdout)
            self.assertEqual(branch_name(repo), "participant/p123abc")


if __name__ == "__main__":
    unittest.main()

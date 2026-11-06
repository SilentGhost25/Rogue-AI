"""Attack landing with the conflict ladder (plan section 5).

(a) 3-way patch; (b) on failure overwrite only the targeted modules with the corrupted
versions, keeping the participant's version in a `pre-attack-<n>` commit.
Idempotent: a repo that already has an `ATTACK #n` commit is left alone.
"""
from __future__ import annotations
import shutil
import subprocess
from pathlib import Path


def git(repo: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess:
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, check=check)


def branch_name(repo: Path) -> str:
    return f"participant/{repo.name}"


def ensure_repo(repo: Path) -> None:
    """Server-side clone of a participant workspace. The baseline commit must hold the clean template,
    so call this right after the template is copied (3-way patches need the original blobs)."""
    if (repo / ".git").exists():
        return
    repo.mkdir(parents=True, exist_ok=True)
    subprocess.run(["git", "init", "-q", str(repo)], check=True)
    git(repo, "checkout", "-q", "-B", branch_name(repo))   # one branch per participant: participant/<id>
    git(repo, "config", "user.email", "server@breach.local")
    git(repo, "config", "user.name", "Breach Server")
    (repo / ".gitignore").write_text("node_modules/\n*.db\n*.db-wal\n*.db-shm\n.breach-backup/\n")
    git(repo, "add", "-A")
    git(repo, "commit", "-q", "-m", "baseline")


def already_applied(repo: Path, n: int) -> bool:
    out = git(repo, "log", "--format=%s").stdout.splitlines()
    return f"ATTACK #{n}" in out


def apply_attack(repo: Path, attack_dir: Path, n: int, touches: list[str]) -> str:
    """Returns 'skipped', 'patched' or 'overwritten'."""
    if already_applied(repo, n):
        return "skipped"
    ensure_repo(repo)
    git(repo, "add", "-A")
    git(repo, "commit", "-q", "--allow-empty", "-m", f"snapshot-before-attack-{n}")
    patch = attack_dir / "attack.patch"
    if patch.exists() and git(repo, "apply", "--3way", "--index", "--ignore-space-change", "--whitespace=nowarn", str(patch.resolve()), check=False).returncode == 0:
        git(repo, "commit", "-q", "-m", f"ATTACK #{n}")
        return "patched"
    git(repo, "reset", "--hard", "-q")  # discard any half-applied 3-way conflict
    # Ladder step (b): keep the participant's work, then overwrite only the targeted modules.
    git(repo, "add", "-A")
    git(repo, "commit", "-q", "--allow-empty", "-m", f"pre-attack-{n}")
    corrupted = attack_dir / "corrupted"
    for rel in touches:
        src = corrupted / rel
        if src.exists():
            dst = repo / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dst)
    git(repo, "add", "-A")
    git(repo, "commit", "-q", "--allow-empty", "-m", f"ATTACK #{n}")
    return "overwritten"


def commit_snapshot(repo: Path, message: str, tag: str | None = None) -> str:
    """Commit the current workspace on the participant branch. Returns 'committed' or 'clean'. Optionally tags it."""
    ensure_repo(repo)
    git(repo, "add", "-A")
    if git(repo, "status", "--porcelain").stdout.strip() == "":
        status = "clean"
    else:
        git(repo, "commit", "-q", "-m", message)
        status = "committed"
    if tag:
        git(repo, "tag", "-f", tag)
    return status


def push_mirror(repo: Path, remote_url: str) -> bool:
    """Server-side only: force-push the participant branch (and tags) to the mirror. Credentials live in the URL on the server."""
    r = git(repo, "push", "--force", remote_url, f"{branch_name(repo)}:{branch_name(repo)}", "--tags", check=False)
    return r.returncode == 0

"""Attack landing with the conflict ladder (plan section 5).

(a) 3-way patch; (b) on failure overwrite only the targeted modules with the corrupted
versions, keeping the participant's version in a `pre-attack-<n>` commit.
Idempotent: a repo that already has an `ATTACK #n` commit is left alone.
"""
from __future__ import annotations
import shutil
import subprocess
from pathlib import Path


def ensure_git_repo(repo: Path) -> None:
    if not (repo / ".git").exists():
        subprocess.run(["git", "-C", str(repo), "init"], capture_output=True, check=True)
        subprocess.run(["git", "-C", str(repo), "config", "user.name", "Breach Engine"], capture_output=True, check=True)
        subprocess.run(["git", "-C", str(repo), "config", "user.email", "engine@rogue.internal"], capture_output=True, check=True)
        subprocess.run(["git", "-C", str(repo), "add", "-A"], capture_output=True, check=False)
        subprocess.run(["git", "-C", str(repo), "commit", "--allow-empty", "-m", "Initial baseline"], capture_output=True, check=False)


def git(repo: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess:
    ensure_git_repo(repo)
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, check=check)


def already_applied(repo: Path, n: int) -> bool:
    res = git(repo, "log", "--format=%s", check=False)
    if res.returncode != 0:
        return False
    return f"ATTACK #{n}" in res.stdout.splitlines()


def apply_attack(repo: Path, attack_dir: Path, n: int, touches: list[str]) -> str:
    """Returns 'skipped', 'patched' or 'overwritten'."""
    ensure_git_repo(repo)
    if already_applied(repo, n):
        return "skipped"
    patch = attack_dir / "attack.patch"
    if patch.exists() and git(repo, "apply", "--3way", "--index", str(patch.resolve()), check=False).returncode == 0:
        git(repo, "commit", "-m", f"ATTACK #{n}")
        return "patched"
    # Ladder step (b): keep the participant's work, then overwrite only the targeted modules.
    git(repo, "add", "-A")
    git(repo, "commit", "--allow-empty", "-m", f"pre-attack-{n}")
    corrupted = attack_dir / "corrupted"
    for rel in touches:
        src = corrupted / rel
        if src.exists():
            dst = repo / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dst)
    git(repo, "add", "-A")
    git(repo, "commit", "--allow-empty", "-m", f"ATTACK #{n}")
    return "overwritten"

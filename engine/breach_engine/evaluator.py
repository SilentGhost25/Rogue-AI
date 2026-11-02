"""Evaluator engine. Executes replay checks, hidden variant checks, and regressions."""
from __future__ import annotations
import hashlib
import importlib.util
import os
import socket
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path

SOLVED, FAILED, PARTIAL = "SOLVED", "FAILED", "PARTIAL"


@dataclass
class Verdict:
    attack_id: int
    status: str
    reason: str


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def find_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]


def evaluate_attack(attack_id: int, attack_dir: Path, workspace: Path) -> Verdict:
    check_file = attack_dir / "checks" / "check.py"
    if not check_file.exists():
        return Verdict(attack_id, FAILED, "Evaluator check file missing.")

    # Dynamically load check module
    spec = importlib.util.spec_from_file_location(f"check_{attack_id}", check_file)
    if not spec or not spec.loader:
        return Verdict(attack_id, FAILED, "Failed to load attack check module.")
    check_mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(check_mod)

    # Boot temporary participant server instance on a free port
    port = find_free_port()
    env = os.environ.copy()
    env["PORT"] = str(port)
    env["ADMIN_PASSWORD"] = "change-me-locally"
    env["DB_FILE"] = str((workspace / f"eval_{attack_id}.db").resolve())

    root_dir = Path(__file__).resolve().parents[2]
    candidate_modules = [
        root_dir / "site" / "node_modules",
        Path("C:/BreachWorkspace/node_modules"),
    ]
    node_paths = [str(p.resolve()) for p in candidate_modules if p.exists()]
    if node_paths:
        env["NODE_PATH"] = os.pathsep.join(node_paths) + (os.pathsep + env["NODE_PATH"] if "NODE_PATH" in env else "")

    proc = None
    try:
        proc = subprocess.Popen(
            ["node", "server.js"],
            cwd=str(workspace),
            env=env,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL
        )

        base_url = f"http://127.0.0.1:{port}"
        # Wait up to 5s for server to start
        healthy = False
        for _ in range(50):
            try:
                import urllib.request
                with urllib.request.urlopen(f"{base_url}/api/health", timeout=0.2) as res:
                    if res.status == 200:
                        healthy = True
                        break
            except Exception:
                time.sleep(0.1)

        if not healthy:
            return Verdict(attack_id, FAILED, "Application failed to boot or /api/health did not respond.")

        # Run checks
        # 1. Regression check
        regression_fn = getattr(check_mod, "regression_check", None)
        if regression_fn and not regression_fn(base_url):
            return Verdict(attack_id, FAILED, "Regression detected: The site no longer works properly.")

        # 2. Replay check (returns True if site is STILL VULNERABLE)
        replay_fn = getattr(check_mod, "replay_check", None)
        if replay_fn and replay_fn(base_url):
            return Verdict(attack_id, FAILED, "The original vulnerability is still exploitable.")

        # 3. Variant check (returns True if variants are safely blocked)
        variant_fn = getattr(check_mod, "variant_check", None)
        if variant_fn and not variant_fn(base_url):
            return Verdict(attack_id, PARTIAL, "Exploit variant succeeded: fix is incomplete or hardcoded.")

        return Verdict(attack_id, SOLVED, "All checks passed! Vulnerability successfully neutralized.")

    finally:
        if proc:
            proc.terminate()
            try:
                proc.wait(timeout=2)
            except Exception:
                proc.kill()
        # Clean up temporary eval db
        eval_db = workspace / f"eval_{attack_id}.db"
        if eval_db.exists():
            try:
                eval_db.unlink()
            except Exception:
                pass


def evaluate(attack_id: int, replay_passed: bool, variants_passed: bool, regression_passed: bool) -> Verdict:
    if not regression_passed:
        return Verdict(attack_id, FAILED, "The site no longer works. Removing a feature is not a fix.")
    if not replay_passed:
        return Verdict(attack_id, FAILED, "The original exploit still works.")
    if not variants_passed:
        return Verdict(attack_id, PARTIAL, "The exact exploit is blocked, but a variant still works.")
    return Verdict(attack_id, SOLVED, "All checks passed.")

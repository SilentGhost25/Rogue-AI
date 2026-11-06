"""Evaluator skeleton. Real replay, variant, regression and benchmark runs are added per attack."""
from __future__ import annotations
import hashlib
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


def protected_files_tampered(workspace: Path, expected: dict[str, str]) -> list[str]:
    """Return relative paths whose hash differs from the pinned value (or are missing)."""
    bad = []
    for rel, digest in expected.items():
        p = workspace / rel
        if not p.exists() or sha256_file(p) != digest:
            bad.append(rel)
    return bad


def evaluate(attack_id: int, replay_passed: bool, variants_passed: bool, regression_passed: bool) -> Verdict:
    """Plan section 8: Tier 1 regression, Tier 2 replay, Tier 3 hidden variants. Any failure is FAILED."""
    if not regression_passed:
        return Verdict(attack_id, FAILED, "The site no longer works. Removing a feature is not a fix.")
    if not replay_passed:
        return Verdict(attack_id, FAILED, "The original exploit still works.")
    if not variants_passed:
        return Verdict(attack_id, FAILED, "The exact exploit is blocked, but a variant still works.")
    return Verdict(attack_id, SOLVED, "All checks passed.")


# ---- full workspace evaluation ---------------------------------------------------------------------------------
from . import scoring
from .checks import ATTACK_CHECKS, regression
from .manifest import load_all
from .runner import running_site, SiteDidNotStart


def evaluate_workspace(workspace: Path, attacks_root: Path, active: list[dict], prompts_total: int = 0) -> dict:
    """active: [{attack_id, elapsed_seconds}] for landed, still-unsolved attacks.
    Returns verdicts with score components. Never raises for a broken site: that is a FAILED verdict."""
    manifests = {a.id: a for a in load_all(attacks_root)}
    verdicts: list[dict] = []
    metrics6: dict = {}
    try:
        with running_site(workspace) as site:
            reg_ok, reg_reason = regression(site)                       # Tier 1
            for item in active:
                aid = item["attack_id"]
                try:
                    replay, variants, note = ATTACK_CHECKS[aid](site)    # Tiers 2 and 3
                except Exception as exc:  # a crashing check must not crash the evaluator
                    replay = variants = False
                    note = f"The check could not run: {type(exc).__name__}"
                v = evaluate(aid, replay, variants, reg_ok)
                if not reg_ok:
                    v.reason = reg_reason
                elif v.status != SOLVED:
                    v.reason = note
                verdicts.append({"attack_id": aid, "status": v.status, "reason": v.reason, "item": item})
            if any(i["attack_id"] == 6 for i in active):
                metrics6 = dict(ATTACK_CHECKS[6].last_metrics)
    except SiteDidNotStart as exc:
        return {"site_started": False, "verdicts": [
            {"attack_id": i["attack_id"], "status": FAILED, "reason": f"The site did not start. {exc}".strip(), "components": {}, "metrics": {}}
            for i in active]}

    solved = [v for v in verdicts if v["status"] == SOLVED]
    prompts_each = -(-prompts_total // len(solved)) if solved else 0
    out = []
    for v in verdicts:
        comps, metrics = {}, {}
        if v["status"] == SOLVED:
            m = manifests[v["attack_id"]]
            comps = scoring.solve_components(v["item"]["elapsed_seconds"], m.window_minutes * 60, prompts_each, m.prompt_threshold)
            if v["attack_id"] == 6:
                metrics = metrics6
        out.append({"attack_id": v["attack_id"], "status": v["status"], "reason": v["reason"], "components": comps, "metrics": metrics})
    return {"site_started": True, "verdicts": out}

"""FastAPI service called by the ASP.NET control plane. Contract: contracts/openapi.yaml."""
from __future__ import annotations
import base64
import hashlib
import os
from pathlib import Path
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from . import EVALUATOR_VERSION
from .manifest import load_all
from .patching import apply_attack
from .evaluator import (
    evaluate,
    evaluate_attack,
    evaluate_fast,
    enqueue_deep_evaluation,
    get_deep_eval_result,
    DEEP_EVAL_QUEUE,
)

ROOT = Path(__file__).resolve().parents[2]
ATTACKS_DIR = ROOT / "attacks"
REPOS_DIR = Path(ROOT / "infra" / "repos")
WORKSPACES_DIR = ROOT / "server" / "ControlPlane" / "data" / "workspaces"

app = FastAPI(title="Breach Engine", version=EVALUATOR_VERSION)


class ApplyRequest(BaseModel):
    participant_id: str
    attack_id: int


class EvaluateRequest(BaseModel):
    participant_id: str
    submission_id: str
    attack_ids: list[int]
    telemetry: dict | None = None


def resolve_workspace(participant_id: str) -> Path | None:
    env_ws = os.environ.get("WORKSPACES_DIR")
    candidates = []
    if env_ws:
        candidates.append(Path(env_ws) / participant_id)
        candidates.append(Path(env_ws))

    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        candidates.append(Path(local_app_data) / "BreachKiosk" / "host" / "workspaces" / participant_id)
        candidates.append(Path(local_app_data) / "BreachKiosk" / "workspace")

    candidates.extend([
        REPOS_DIR / participant_id,
        WORKSPACES_DIR / participant_id,
        ROOT / "server" / "data" / "workspaces" / participant_id,
        ROOT / "data" / "workspaces" / participant_id,
        Path("C:/BreachWorkspace"),
        Path(os.path.expanduser("~")) / "BreachWorkspace",
        ROOT / "site",
    ])
    for c in candidates:
        if c.exists():
            if (c / "server.js").exists():
                return c
            if c.is_dir():
                try:
                    if any(c.iterdir()):
                        return c
                except Exception:
                    pass
    return None


@app.get("/health")
def health():
    return {"ok": True, "evaluator": EVALUATOR_VERSION}


@app.get("/attacks")
def attacks():
    return [{"id": a.id, "name": a.name, "fires_at": a.fires_at, "status": getattr(a, "status", "ready")} for a in load_all(ATTACKS_DIR)]


@app.get("/attacks/{attack_id}/payload")
def get_attack_payload(attack_id: int):
    match = next((a for a in load_all(ATTACKS_DIR) if a.id == attack_id), None)
    if not match or not match.directory:
        raise HTTPException(404, "attack not found")

    corrupted_dir = match.directory / "corrupted"
    if not corrupted_dir.exists():
        raise HTTPException(404, "corrupted payload directory missing")

    files = []
    for file_path in corrupted_dir.rglob("*"):
        if file_path.is_file():
            rel = file_path.relative_to(corrupted_dir).as_posix()
            data = file_path.read_bytes()
            files.append({
                "path": rel,
                "sha256": hashlib.sha256(data).hexdigest().upper(),
                "contentBase64": base64.b64encode(data).decode()
            })
    return {"attack_id": attack_id, "files": files}


@app.post("/attacks/apply")
def apply(req: ApplyRequest):
    match = next((a for a in load_all(ATTACKS_DIR) if a.id == req.attack_id), None)
    if not match:
        raise HTTPException(404, "unknown attack")
    repo = resolve_workspace(req.participant_id)
    if not repo:
        raise HTTPException(404, "participant repo/workspace not found")
    return {"result": apply_attack(repo, match.directory, match.id, match.touches)}


@app.post("/evaluate")
@app.post("/evaluate/fast")
def run_evaluation(req: EvaluateRequest):
    """TIER 1 (Fast-Path): Synchronous check (<5s) returning immediate verdict to live scoreboard and enqueuing Tier 2."""
    workspace = resolve_workspace(req.participant_id)
    if not workspace:
        raise HTTPException(404, "participant workspace not found")

    all_attacks = {a.id: a for a in load_all(ATTACKS_DIR)}
    verdicts = []

    for aid in req.attack_ids:
        attack = all_attacks.get(aid)
        if not attack or not attack.directory:
            verdicts.append({"attack_id": aid, "status": "FAILED", "reason": f"Attack #{aid} manifest not found."})
            continue

        verdict = evaluate_fast(aid, attack.directory, workspace, timeout_seconds=20.0)
        verdicts.append(verdict.__dict__)

    # Asynchronously enqueue Tier 2 Deep-Path evaluation
    q_depth = enqueue_deep_evaluation(
        submission_id=req.submission_id,
        participant_id=req.participant_id,
        attack_ids=req.attack_ids,
        workspace_dir=workspace,
        telemetry=req.telemetry,
    )

    return {
        "submission_id": req.submission_id,
        "evaluator": EVALUATOR_VERSION,
        "verdicts": verdicts,
        "tier1": True,
        "deep_eval_queued": True,
        "queue_depth": q_depth,
    }


@app.get("/evaluation/{submission_id}")
def get_evaluation(submission_id: str):
    """Retrieves the Tier 2 Deep Evaluation result for a given submission."""
    res = get_deep_eval_result(submission_id)
    if not res:
        return {
            "submission_id": submission_id,
            "status": "computing",
            "queue_depth": DEEP_EVAL_QUEUE.qsize(),
        }
    return {
        "submission_id": submission_id,
        "status": "completed",
        "result": res,
    }



if __name__ == "__main__":
    import uvicorn
    uvicorn.run("breach_engine.api:app", host="127.0.0.1", port=8000, reload=False)


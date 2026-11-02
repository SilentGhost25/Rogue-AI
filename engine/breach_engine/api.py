"""FastAPI service called by the ASP.NET control plane. Contract: contracts/openapi.yaml."""
from __future__ import annotations
import base64
import hashlib
from pathlib import Path
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from . import EVALUATOR_VERSION
from .manifest import load_all
from .patching import apply_attack
from .evaluator import evaluate, evaluate_attack

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


def resolve_workspace(participant_id: str) -> Path | None:
    candidates = [
        REPOS_DIR / participant_id,
        WORKSPACES_DIR / participant_id,
        ROOT / "server" / "data" / "workspaces" / participant_id,
        ROOT / "data" / "workspaces" / participant_id,
    ]
    for c in candidates:
        if c.exists():
            return c
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
def run_evaluation(req: EvaluateRequest):
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

        verdict = evaluate_attack(aid, attack.directory, workspace)
        verdicts.append(verdict.__dict__)

    return {
        "submission_id": req.submission_id,
        "evaluator": EVALUATOR_VERSION,
        "verdicts": verdicts
    }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("breach_engine.api:app", host="127.0.0.1", port=8000, reload=False)


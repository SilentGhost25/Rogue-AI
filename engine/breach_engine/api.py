"""FastAPI service called by the ASP.NET control plane. Contract: contracts/openapi.yaml.

WORKSPACES_DIR must point at the same folder as the control plane's Breach:WorkspacesPath.
"""
from __future__ import annotations
import os, threading
from pathlib import Path
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from . import EVALUATOR_VERSION
from .evaluator import evaluate_workspace
from .manifest import load_all
import base64, hashlib
from .patching import apply_attack, commit_snapshot, ensure_repo, push_mirror

ROOT = Path(__file__).resolve().parents[2]
ATTACKS_DIR = ROOT / "attacks"
WORKSPACES = Path(os.environ.get("WORKSPACES_DIR", ROOT / "server" / "ControlPlane" / "data" / "workspaces"))
EVAL_SLOTS = threading.BoundedSemaphore(int(os.environ.get("EVAL_WORKERS", "6")))  # correctness queue: 4 to 6 workers

app = FastAPI(title="Breach Engine", version=EVALUATOR_VERSION)


def workspace_of(participant_id: str) -> Path:
    if not participant_id or not all(c.isalnum() or c in "-_" for c in participant_id) or len(participant_id) > 64:
        raise HTTPException(400, "invalid participant id")
    return WORKSPACES / participant_id


class ParticipantRequest(BaseModel):
    participant_id: str


class ApplyRequest(ParticipantRequest):
    attack_id: int


class ActiveAttack(BaseModel):
    attack_id: int
    elapsed_seconds: float


class EvaluateRequest(ParticipantRequest):
    submission_id: str
    active: list[ActiveAttack]
    prompts_total: int = 0


@app.get("/health")
def health():
    return {"ok": True, "evaluator": EVALUATOR_VERSION}


@app.get("/attacks")
def attacks():
    return [{"id": a.id, "name": a.name, "fires_at": a.fires_at, "window_minutes": a.window_minutes,
             "reference_prompts": a.reference_prompts} for a in load_all(ATTACKS_DIR)]


@app.get("/attacks/{attack_id}/payload")
def payload(attack_id: int):
    """The corrupted files an attack plants (base64). The control plane uses `touches` to serve a participant's post-attack copy."""
    match = next((a for a in load_all(ATTACKS_DIR) if a.id == attack_id), None)
    if not match:
        raise HTTPException(404, "unknown attack")
    files = []
    for rel in match.touches:
        f = match.directory / "corrupted" / rel
        if f.exists():
            data = f.read_bytes()
            files.append({"path": rel, "sha256": hashlib.sha256(data).hexdigest().upper(), "contentBase64": base64.b64encode(data).decode()})
    return {"attack_id": attack_id, "touches": match.touches, "files": files}


class CommitRequest(ParticipantRequest):
    message: str = "snapshot"
    tag: str | None = None


@app.post("/repos/commit")
def commit(req: CommitRequest):
    """Server-owned Git: commit a participant's snapshot, optionally tag a submission, optionally push to the mirror."""
    ws = workspace_of(req.participant_id)
    if not ws.exists():
        raise HTTPException(404, "participant workspace not found")
    status = commit_snapshot(ws, req.message[:200], req.tag)
    mirror = os.environ.get("GIT_MIRROR_URL")   # e.g. https://<token>@github.com/owner/repo.git, set on the server only
    pushed = push_mirror(ws, mirror) if mirror and status == "committed" else None
    return {"status": status, "pushed": pushed}


@app.post("/repos/init")
def init_repo(req: ParticipantRequest):
    ensure_repo(workspace_of(req.participant_id))
    return {"ok": True}


@app.post("/attacks/apply")
def apply(req: ApplyRequest):
    match = next((a for a in load_all(ATTACKS_DIR) if a.id == req.attack_id), None)
    if not match:
        raise HTTPException(404, "unknown attack")
    ws = workspace_of(req.participant_id)
    if not ws.exists():
        raise HTTPException(404, "participant workspace not found")
    return {"result": apply_attack(ws, match.directory, match.id, match.touches)}


@app.post("/evaluate")
def run_evaluation(req: EvaluateRequest):
    ws = workspace_of(req.participant_id)
    if not ws.exists():
        raise HTTPException(404, "participant workspace not found")
    with EVAL_SLOTS:
        result = evaluate_workspace(ws, ATTACKS_DIR, [a.model_dump() for a in req.active], req.prompts_total)
    return {"submission_id": req.submission_id, "evaluator": EVALUATOR_VERSION, **result}

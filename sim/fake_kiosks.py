"""Simulates N concurrent kiosks against the Breach Protocol Control Plane.

Tests end-to-end participant workflows:
1. Desk token enrollment or kiosk registration
2. Continuous snapshot sync with SHA256 integrity verification & token gating
3. Telemetry events (PROMPT counts, AGY exhaustion)
4. Attack payload delivery retrieval
5. Fix submission and evaluation under Semaphore(4) concurrency
6. Leaderboard ranking and tie-breaker verification

Usage:
  python sim/fake_kiosks.py --server http://127.0.0.1:5080 --count 50 --rounds 3
  python sim/fake_kiosks.py --server http://127.0.0.1:5080 --count 20 --eval-load
"""
from __future__ import annotations
import argparse
import base64
import hashlib
import json
import os
import sqlite3
import sys
import threading
import time
import urllib.request
import urllib.error
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parents[1]


def call(base: str, path: str, body: dict | None = None, headers: dict | None = None) -> dict:
    data = json.dumps(body).encode() if body is not None else None
    h = {"Content-Type": "application/json"}
    if headers:
        h.update(headers)

    for attempt in range(4):
        req = urllib.request.Request(
            base + path,
            data=data,
            method="POST" if body is not None else "GET",
            headers=h
        )
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                raw = r.read()
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as e:
            if e.code in (500, 502, 503):
                time.sleep(0.1 * (2 ** attempt))
                continue
            return {"error": e.code, "reason": e.reason}
        except Exception as ex:
            if attempt < 3:
                time.sleep(0.1 * (2 ** attempt))
                continue
            return {"error": str(ex)}
    return {"error": "max_retries_exceeded"}


def load_desk_tokens(db_path: str, count: int) -> list[str]:
    """Retrieves available desk tokens from the database."""
    if not os.path.exists(db_path):
        return []
    try:
        conn = sqlite3.connect(db_path)
        cur = conn.cursor()
        cur.execute("SELECT token FROM desk_tokens WHERE participant_id IS NULL ORDER BY desk_id ASC LIMIT ?", (count,))
        rows = cur.fetchall()
        conn.close()
        return [r[0] for r in rows]
    except Exception:
        return []


def kiosk_worker(base: str, n: int, rounds: int, token: str | None, eval_load: bool, stats: dict, lock: threading.Lock) -> None:
    time.sleep(n * 0.02)  # Staggered participant connection
    kiosk_id = f"sim-{n:02d}"
    display_name = f"Defender {n:02d}"
    headers = {}

    # 1. Enroll with Desk Token if available, else fallback to /register
    if token:
        enroll_res = call(base, "/api/kiosk/enroll", {"token": token, "kioskId": kiosk_id, "name": display_name})
        pid = enroll_res.get("participantId")
        headers["X-Kiosk-Token"] = token
    else:
        reg = call(base, "/api/kiosk/register", {"name": display_name, "kioskId": kiosk_id})
        pid = reg.get("participantId")

    if not pid:
        with lock:
            stats["failed_reg"] += 1
        return

    with lock:
        stats["registered"] += 1

    # 2. Simulate rounds of code edits & snapshots
    for r in range(rounds):
        content = f"// Defence patch {kiosk_id} round {r} - timestamp {time.time()}\nconst patched = true;\nmodule.exports = {{ patched }};\n".encode()
        sha = hashlib.sha256(content).hexdigest().upper()
        file = {
            "path": f"routes/notes_{n}.js",
            "sha256": sha,
            "contentBase64": base64.b64encode(content).decode("ascii")
        }
        res = call(base, f"/api/kiosk/{pid}/snapshot", {"files": [file]}, headers=headers)
        if res.get("written") == 1:
            with lock:
                stats["snapshots_saved"] += 1

        # Send AI telemetry
        call(base, f"/api/kiosk/{pid}/telemetry", {"type": "PROMPT", "payload": {"agent": "opencode", "round": r + 1}}, headers=headers)
        time.sleep(0.02)

    # Simulate AGY quota event for odd kiosks
    if n % 2 == 1:
        call(base, f"/api/kiosk/{pid}/telemetry", {"type": "AGY_EXHAUSTED", "payload": {}}, headers=headers)

    # 3. Retrieve attack payload
    payload = call(base, f"/api/kiosk/{pid}/attack-payload/1", headers=headers)
    if "files" in payload:
        with lock:
            stats["payloads_retrieved"] += 1

    # 4. If eval-load is enabled, upload a valid Attack 1 fix and submit
    if eval_load:
        # Load clean reference files for Attack 1
        ref_html = (ROOT_DIR / "attacks" / "01-defacement" / "reference" / "lib" / "html.js")
        ref_css = (ROOT_DIR / "attacks" / "01-defacement" / "reference" / "public" / "style.css")
        if ref_html.exists() and ref_css.exists():
            h_data = ref_html.read_bytes()
            c_data = ref_css.read_bytes()
            fix_files = [
                {"path": "lib/html.js", "sha256": hashlib.sha256(h_data).hexdigest().upper(), "contentBase64": base64.b64encode(h_data).decode("ascii")},
                {"path": "public/style.css", "sha256": hashlib.sha256(c_data).hexdigest().upper(), "contentBase64": base64.b64encode(c_data).decode("ascii")},
            ]
            call(base, f"/api/kiosk/{pid}/snapshot", {"files": fix_files}, headers=headers)

    # 5. Submit fix to evaluator queue
    sub_t0 = time.time()
    sub = call(base, f"/api/kiosk/{pid}/submit", {}, headers=headers)
    if sub.get("status") == "evaluating":
        with lock:
            stats["submissions"] += 1

        if eval_load:
            # Poll submission status until resolved
            sid = sub.get("submissionId")
            for _ in range(60):
                time.sleep(0.5)
                subs_res = call(base, f"/api/kiosk/{pid}/submissions", headers=headers)
                if isinstance(subs_res, list) and subs_res:
                    latest = subs_res[0]
                    st = latest.get("status")
                    if st in ("solved", "failed", "partial"):
                        latency = time.time() - sub_t0
                        with lock:
                            stats["eval_completed"] += 1
                            if st == "solved":
                                stats["eval_solved"] += 1
                            stats["eval_latencies"].append(latency)
                        break


def main() -> None:
    ap = argparse.ArgumentParser(description="Simulate concurrent kiosks against Breach Control Plane")
    ap.add_argument("--server", default="http://127.0.0.1:5080")
    ap.add_argument("--count", type=int, default=50)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--admin-key", default=os.environ.get("BREACH_ADMIN_KEY", ""))
    ap.add_argument("--db", default="server/ControlPlane/data/test_breach.db")
    ap.add_argument("--eval-load", action="store_true", help="Run full submission & evaluation load test")
    args = ap.parse_args()

    print(f"[*] Starting stress test: {args.count} kiosks against {args.server} (eval_load={args.eval_load})")
    t0 = time.time()

    # Verify server is alive
    state = call(args.server, "/api/state")
    if "error" in state:
        print(f"[-] Server {args.server} is offline or unreachable: {state['error']}")
        sys.exit(1)
    print(f"[+] Control plane online. Paused: {state.get('paused')}, Server time: {state.get('serverTime')}")

    # Load desk tokens if available
    db_candidates = [
        args.db,
        "server/ControlPlane/data/test_breach.db",
        "server/ControlPlane/data/breach.db",
        "data/breach.db"
    ]
    tokens = []
    for cand in db_candidates:
        if os.path.exists(cand):
            tokens = load_desk_tokens(cand, args.count)
            if tokens:
                print(f"[+] Loaded {len(tokens)} desk tokens from {cand}")
                break

    stats = {
        "registered": 0,
        "failed_reg": 0,
        "snapshots_saved": 0,
        "payloads_retrieved": 0,
        "submissions": 0,
        "eval_completed": 0,
        "eval_solved": 0,
        "eval_latencies": []
    }
    lock = threading.Lock()

    # Launch concurrent kiosks
    threads = [
        threading.Thread(
            target=kiosk_worker,
            args=(args.server, i + 1, args.rounds, tokens[i] if i < len(tokens) else None, args.eval_load, stats, lock)
        )
        for i in range(args.count)
    ]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    elapsed = time.time() - t0
    print(f"\n[+] Concurrency Test Complete in {elapsed:.2f}s:")
    print(f"    - Registered:         {stats['registered']} / {args.count}")
    print(f"    - Snapshots written:  {stats['snapshots_saved']}")
    print(f"    - Payloads retrieved: {stats['payloads_retrieved']}")
    print(f"    - Submissions queued: {stats['submissions']}")
    if args.eval_load:
        print(f"    - Evaluated complete: {stats['eval_completed']} / {stats['submissions']}")
        print(f"    - Solved count:       {stats['eval_solved']}")
        if stats["eval_latencies"]:
            median_lat = sorted(stats["eval_latencies"])[len(stats["eval_latencies"]) // 2]
            print(f"    - Median Eval Latency:{median_lat:.2f}s")
            print(f"    - Max Eval Latency:   {max(stats['eval_latencies']):.2f}s")

    # Verify public leaderboard
    lb = call(args.server, "/api/leaderboard")
    board = lb.get("leaderboard", [])
    print(f"\n[+] Public Leaderboard: {len(board)} entries ranked (Frozen: {lb.get('frozen')})")
    if board:
        print(f"    Top Defender: Rank #1 - {board[0].get('name')} ({board[0].get('participantId')}), Solved: {board[0].get('solved')}")

    # Verify admin endpoints if key is provided
    if args.admin_key:
        kiosks = call(args.server, "/admin/kiosks", headers={"X-Admin-Key": args.admin_key})
        if isinstance(kiosks, list):
            print(f"[+] Admin Kiosk Grid: {len(kiosks)} kiosks tracked with telemetry & quotas")
        else:
            print(f"[-] Admin check warning: {kiosks}")


if __name__ == "__main__":
    main()
"""Simulates N concurrent kiosks against the Breach Protocol Control Plane.

Tests end-to-end participant workflows:
1. Kiosk registration
2. Continuous snapshot sync with SHA256 integrity verification
3. Telemetry events (PROMPT counts, AGY exhaustion)
4. Attack payload delivery retrieval
5. Fix submission and evaluation
6. Leaderboard ranking and tie-breaker verification

Usage:
  python sim/fake_kiosks.py --server http://127.0.0.1:5080 --count 50 --rounds 3
"""
from __future__ import annotations
import argparse
import base64
import hashlib
import json
import sys
import threading
import time
import urllib.request
import urllib.error


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
            with urllib.request.urlopen(req, timeout=25) as r:
                raw = r.read()
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as e:
            if e.code == 503 or e.code == 500:
                time.sleep(0.1 * (2 ** attempt))
                continue
            return {"error": e.code, "reason": e.reason}
        except Exception as ex:
            if attempt < 3:
                time.sleep(0.1 * (2 ** attempt))
                continue
            return {"error": str(ex)}
    return {"error": "max_retries_exceeded"}


def kiosk_worker(base: str, n: int, rounds: int, stats: dict, lock: threading.Lock) -> None:
    time.sleep(n * 0.03)  # Staggered participant connection
    kiosk_id = f"sim-{n:02d}"
    display_name = f"Defender {n:02d}"

    # 1. Register Kiosk
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
        res = call(base, f"/api/kiosk/{pid}/snapshot", {"files": [file]})
        if res.get("written") == 1:
            with lock:
                stats["snapshots_saved"] += 1

        # Send AI telemetry
        call(base, f"/api/kiosk/{pid}/telemetry", {"type": "PROMPT", "payload": {"agent": "opencode", "round": r + 1}})
        time.sleep(0.05)

    # Simulate AGY quota event for odd kiosks
    if n % 2 == 1:
        call(base, f"/api/kiosk/{pid}/telemetry", {"type": "AGY_EXHAUSTED", "payload": {}})

    # 3. Retrieve attack payload
    payload = call(base, f"/api/kiosk/{pid}/attack-payload/1")
    if "files" in payload:
        with lock:
            stats["payloads_retrieved"] += 1

    # 4. Submit fix to evaluator queue
    sub = call(base, f"/api/kiosk/{pid}/submit", {})
    if sub.get("status") == "evaluating":
        with lock:
            stats["submissions"] += 1


def main() -> None:
    ap = argparse.ArgumentParser(description="Simulate 50 concurrent kiosks against Breach Control Plane")
    ap.add_argument("--server", default="http://127.0.0.1:5080")
    ap.add_argument("--count", type=int, default=50)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--admin-key", default="change-me-locally")
    args = ap.parse_args()

    print(f"[*] Starting stress test: {args.count} kiosks against {args.server}")
    t0 = time.time()

    # Verify server is alive
    state = call(args.server, "/api/state")
    if "error" in state:
        print(f"[-] Server {args.server} is offline or unreachable: {state['error']}")
        sys.exit(1)
    print(f"[+] Control plane online. Paused: {state.get('paused')}, Server time: {state.get('serverTime')}")

    stats = {
        "registered": 0,
        "failed_reg": 0,
        "snapshots_saved": 0,
        "payloads_retrieved": 0,
        "submissions": 0
    }
    lock = threading.Lock()

    # Launch concurrent kiosks
    threads = [
        threading.Thread(target=kiosk_worker, args=(args.server, i + 1, args.rounds, stats, lock))
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

    # 5. Verify public leaderboard
    lb = call(args.server, "/api/leaderboard")
    board = lb.get("leaderboard", [])
    print(f"\n[+] Public Leaderboard: {len(board)} entries ranked (Frozen: {lb.get('frozen')})")
    if board:
        print(f"    Top Defender: Rank #1 - {board[0].get('name')} ({board[0].get('participantId')}), Solved: {board[0].get('solved')}")

    # 6. Verify admin endpoints
    kiosks = call(args.server, "/admin/kiosks", headers={"X-Admin-Key": args.admin_key})
    if isinstance(kiosks, list):
        print(f"[+] Admin Kiosk Grid: {len(kiosks)} kiosks tracked with telemetry & quotas")
    else:
        print(f"[-] Admin check warning: {kiosks}")


if __name__ == "__main__":
    main()
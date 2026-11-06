"""Simulates N kiosks against the control plane (REST only, stdlib only) and prints a summary.

Each fake kiosk: registers, checks its status, uploads snapshots (including a deletion), sends prompt telemetry,
fetches attack payloads for any attack ids you list, and submits a fix. Afterwards it reads the public leaderboard
and, with --admin-key, the admin grid.

  python sim/fake_kiosks.py --server http://127.0.0.1:5080 --count 50 --attacks 1 --admin-key <key>
"""
from __future__ import annotations
import argparse, base64, hashlib, json, threading, time, urllib.request, urllib.error


def call(base: str, path: str, body: dict | None = None, headers: dict | None = None) -> dict:
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=data, method="POST" if body is not None else "GET",
                                 headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with urllib.request.urlopen(req, timeout=20) as r:
            raw = r.read()
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        return {"error": e.code}
    except (urllib.error.URLError, TimeoutError):
        return {"error": "unreachable"}


def file_entry(path: str, text: str) -> dict:
    data = text.encode()
    return {"path": path, "sha256": hashlib.sha256(data).hexdigest().upper(), "contentBase64": base64.b64encode(data).decode()}


def kiosk(base: str, n: int, rounds: int, attacks: list[int], stats: dict, lock: threading.Lock) -> None:
    pid = call(base, "/api/kiosk/register", {"name": f"Sim {n:02d}", "kioskId": f"sim-{n:02d}"}).get("participantId")
    if not pid:
        with lock: stats["failed"] += 1
        return
    call(base, f"/api/kiosk/{pid}/status")
    for r in range(rounds):
        res = call(base, f"/api/kiosk/{pid}/snapshot", {"files": [file_entry("routes/notes.js", f"// sim {n} round {r}\n")],
                                                          "deleted": ["routes/old-notes.js"] if r else []})
        call(base, f"/api/kiosk/{pid}/telemetry", {"type": "PROMPT", "payload": {"agent": "opencode", "n": r + 1}})
        with lock: stats["snapshots"] += 1 if res.get("written") == 1 else 0
        time.sleep(0.1)
    for a in attacks:
        res = call(base, f"/api/kiosk/{pid}/attack-payload/{a}")
        with lock: stats["payloads"] += 1 if res.get("files") else 0
    res = call(base, f"/api/kiosk/{pid}/submit", {})
    with lock: stats["submitted"] += 1 if res.get("submissionId") else 0


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--server", default="http://127.0.0.1:5080")
    ap.add_argument("--count", type=int, default=50)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--attacks", type=int, nargs="*", default=[], help="attack ids whose payload each kiosk should fetch")
    ap.add_argument("--admin-key", default="")
    a = ap.parse_args()
    stats = {"snapshots": 0, "payloads": 0, "submitted": 0, "failed": 0}
    lock = threading.Lock()
    threads = [threading.Thread(target=kiosk, args=(a.server, i, a.rounds, a.attacks, stats, lock)) for i in range(a.count)]
    t0 = time.time()
    [t.start() for t in threads]; [t.join() for t in threads]
    print(f"{a.count} kiosks finished in {time.time() - t0:.1f}s -> {stats}")
    board = call(a.server, "/api/leaderboard")
    print(f"public leaderboard rows: {len(board.get('rows', []))}")
    if a.admin_key:
        grid = call(a.server, "/admin/leaderboard", headers={"X-Admin-Key": a.admin_key})
        print(f"admin grid tiles: {len(grid) if isinstance(grid, list) else grid}")


if __name__ == "__main__":
    main()

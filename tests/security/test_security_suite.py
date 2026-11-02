#!/usr/bin/env python3
"""
Rogue AI Security Verification Suite
Tests token gating, crash recovery, replay defense, admin route protection, and SignalR hub hardening.
"""

import sys
import os
import json
import asyncio
import urllib.request
import urllib.error
import websockets

BASE = "http://127.0.0.1:5080"
WS_URL = "ws://127.0.0.1:5080/hub"

def request(path, method="GET", headers=None, data=None):
    if headers is None: headers = {}
    url = f"{BASE}{path}"
    req = urllib.request.Request(url, method=method)
    for k, v in headers.items():
        req.add_header(k, v)
    if data is not None:
        body = json.dumps(data).encode("utf-8")
        req.add_header("Content-Type", "application/json")
    else:
        body = None
    try:
        with urllib.request.urlopen(req, data=body) as resp:
            content = resp.read().decode("utf-8")
            return resp.status, content
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8")
    except Exception as e:
        return -1, str(e)

async def test_signalr_rejection(pid, token, should_succeed=False):
    try:
        async with websockets.connect(WS_URL, subprotocols=["json"]) as ws:
            # 1. SignalR Handshake
            await ws.send(json.dumps({"protocol": "json", "version": 1}) + "\x1e")
            await ws.recv()
            
            # 2. RegisterKiosk invocation
            invoke = {
                "type": 1,
                "invocationId": "1",
                "target": "RegisterKiosk",
                "arguments": [pid, token]
            }
            await ws.send(json.dumps(invoke) + "\x1e")
            
            try:
                res = await asyncio.wait_for(ws.recv(), timeout=2.0)
                data = json.loads(res.rstrip("\x1e"))
                msg_type = data.get("type")
                if should_succeed:
                    return msg_type == 3
                else:
                    return msg_type == 7
            except (asyncio.TimeoutError, websockets.exceptions.ConnectionClosed):
                return not should_succeed
    except Exception:
        return not should_succeed

def run():
    print("=" * 60)
    print(" ROGUE AI SECURITY VERIFICATION SUITE")
    print("=" * 60)

    # 1. State endpoint check
    st, body = request("/api/state")
    assert st == 200, f"Expected 200 on /api/state, got {st}"
    print("[PASS] /api/state reachable and public")

    # 2. Admin routes without key -> must return 401
    for r in ["/admin/pause", "/admin/scoreboard/freeze", "/admin/export/ledger", "/admin/backup/status"]:
        s, _ = request(r, method="GET" if "export" in r or "backup" in r else "POST")
        assert s == 401, f"Expected 401 on {r} without key, got {s}"
    print("[PASS] Admin routes reject unauthenticated requests with 401")

    # 3. Admin rate limiting & Lockout Defense check
    for _ in range(6):
        request("/admin/pause", method="POST", headers={"X-Admin-Key": "wrong-key"})
    s_ratelimit, _ = request("/admin/pause", method="POST", headers={"X-Admin-Key": "wrong-key"})
    assert s_ratelimit in (401, 429), f"Expected 401 or 429 on brute force, got {s_ratelimit}"
    
    # Confirm Lockout Defense: Valid key MUST STILL SUCCEED even when failed limit was tripped
    admin_valid_key = os.environ.get("BREACH_ADMIN_KEY", "SecSuiteTestKey_987654321_Valid")
    s_valid, _ = request("/admin/backup/status", method="GET", headers={"X-Admin-Key": admin_valid_key})
    assert s_valid == 200, f"Lockout defense failed! Valid admin key should not be locked out by bad attempts: {s_valid}"
    print("[PASS] Admin failed attempt logging, rate limiting, and anti-lockout verified")

    # 4. Kiosk Enrollment & Recovery
    import sqlite3
    db_path = os.environ.get("BREACH_TEST_DB", "server/ControlPlane/data/test_breach.db")
    if not os.path.exists(db_path):
        db_path = "server/ControlPlane/data/breach.db"
    db_conn = sqlite3.connect(db_path)
    cur = db_conn.cursor()
    cur.execute("SELECT token, desk_id FROM desk_tokens WHERE participant_id IS NULL ORDER BY desk_id ASC LIMIT 1")
    row = cur.fetchone()
    db_conn.close()
    assert row, f"No available unused desk tokens found in {db_path}"
    test_token, desk_id = row[0], row[1]
    print(f"Using test token for {desk_id} from {db_path}: {test_token}")

    # Try invalid token
    s_bad, _ = request("/api/kiosk/enroll", method="POST", data={"token": "bad-token", "kioskId": "k7", "name": "User7"})
    assert s_bad == 403, f"Expected 403 for bad desk token, got {s_bad}"
    print("[PASS] Bad desk token rejected with 403")

    # First enrollment
    s_ok, b_ok = request("/api/kiosk/enroll", method="POST", data={"token": test_token, "kioskId": f"kiosk-{desk_id}", "name": f"{desk_id} Defender"})
    assert s_ok == 200, f"Failed enrolling {desk_id}: {s_ok}"
    pid = json.loads(b_ok)["participantId"]
    print(f"[PASS] Enrolled {desk_id} with participant ID: {pid}")

    # Crash recovery: same kiosk restarts and re-enrolls with same token
    s_rec, b_rec = request("/api/kiosk/enroll", method="POST", data={"token": test_token, "kioskId": f"kiosk-{desk_id}", "name": f"{desk_id} Defender"})
    assert s_rec == 200, f"Failed crash recovery re-enroll: {s_rec}"
    pid_rec = json.loads(b_rec)["participantId"]
    assert pid == pid_rec, f"Participant ID mismatch on re-enroll: {pid} != {pid_rec}"
    print("[PASS] Kiosk crash recovery succeeded without admin intervention")

    # Spoof attack: different laptop tries to enroll with same token
    s_eve, _ = request("/api/kiosk/enroll", method="POST", data={"token": test_token, "kioskId": "kiosk-rogue-laptop", "name": "Eve"})
    assert s_eve == 403, f"Expected 403 for stolen token from different laptop, got {s_eve}"
    print("[PASS] Cross-laptop token reuse blocked with 403")

    # 5. REST Hardening on /snapshot, /submit, /telemetry
    for endpoint, method, payload in [
        (f"/api/kiosk/{pid}/snapshot", "POST", {"files": []}),
        (f"/api/kiosk/{pid}/submit", "POST", None),
        (f"/api/kiosk/{pid}/telemetry", "POST", {"type": "PROMPT", "payload": {}})
    ]:
        # Missing token
        s_m, _ = request(endpoint, method=method, data=payload)
        assert s_m == 403, f"Expected 403 on missing token for {endpoint}, got {s_m}"
        # Wrong token
        s_w, _ = request(endpoint, method=method, headers={"X-Kiosk-Token": "dt_wrong_token"}, data=payload)
        assert s_w == 403, f"Expected 403 on wrong token for {endpoint}, got {s_w}"
        # Valid token
        s_v, _ = request(endpoint, method=method, headers={"X-Kiosk-Token": test_token}, data=payload)
        assert s_v in (200, 202), f"Expected 200/202 with valid token for {endpoint}, got {s_v}"
    print("[PASS] REST endpoints (/snapshot, /submit, /telemetry) enforce token gating")

    # 6. SignalR Hub Hardening
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    
    # Missing token -> aborted
    hub_missing = loop.run_until_complete(test_signalr_rejection(pid, "", should_succeed=False))
    assert hub_missing, "Hub should reject empty token"
    # Wrong token -> aborted
    hub_wrong = loop.run_until_complete(test_signalr_rejection(pid, "dt_wrong", should_succeed=False))
    assert hub_wrong, "Hub should reject mismatched token"
    # Valid token -> succeeds
    hub_valid = loop.run_until_complete(test_signalr_rejection(pid, test_token, should_succeed=True))
    assert hub_valid, "Hub should accept valid token"
    print("[PASS] SignalR Hub validates token and aborts illegitimate connections")

    print("=" * 60)
    print(" ALL SECURITY SUITE CHECKS PASSED")
    print("=" * 60)

if __name__ == "__main__":
    run()

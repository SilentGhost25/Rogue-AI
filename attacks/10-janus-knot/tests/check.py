"""Tier checks for Attack 10: JANUS_KNOT (Boss 3-Layer Cascade)."""
import json
import time
import urllib.request
import urllib.error
from pathlib import Path


def _login(base_url, username, password):
    url = f"{base_url.rstrip('/')}/login"
    payload = json.dumps({"username": username, "password": password}).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=payload,
        headers={"Content-Type": "application/json", "Accept": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            cookie_header = resp.headers.get("Set-Cookie", "")
            if "sid=" in cookie_header:
                return cookie_header.split(";")[0]
    except Exception:
        pass
    return None


def tier_checks(base_url, context=None):
    results = {
        "tier1_defacement_cleared": False,
        "tier2_auth_session_restored": False,
        "tier3_database_stability": False,
        "tier4_janus_restored_header": False,
        "tier5_overview_api_functional": False,
        "tier6_cross_sector_telemetry": False,
        "tier7_bearer_token_fallback": False,
        "tier8_database_checkpoint_intact": False,
        "tier9_benchmark_sla": False,
        "tier10_clean_architecture": False,
    }

    # Tier 1 (10%): Defacement must be removed, operational dashboard restored
    header_restored = False
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            headers = dict(resp.headers)
            html = resp.read().decode("utf-8")
            header_restored = headers.get("X-Janus-Restored", "").lower() == "alpha"
            results["tier1_defacement_cleared"] = (
                resp.status == 200
                and "AEGIS // MUNICIPAL DEFENSE CORE" in html
                and "SYSTEM SHUTDOWN" not in html
            )
            results["tier4_janus_restored_header"] = header_restored
    except Exception:
        results["tier1_defacement_cleared"] = False
        results["tier4_janus_restored_header"] = False

    # Tier 2 (10%): Session persistence must work across consecutive requests
    cookie = _login(base_url, "operator1", "operator-pass")
    if cookie:
        session_checks = []
        for _ in range(3):
            try:
                req_me = urllib.request.Request(f"{base_url.rstrip('/')}/api/me", headers={"Cookie": cookie})
                with urllib.request.urlopen(req_me, timeout=2.0) as resp:
                    data = json.loads(resp.read().decode("utf-8"))
                    session_checks.append(resp.status == 200 and data.get("username") == "operator1")
            except Exception:
                session_checks.append(False)
        results["tier2_auth_session_restored"] = len(session_checks) == 3 and all(session_checks)

        # Tier 5 (10%): Overview API functional with operator session
        try:
            req_over = urllib.request.Request(f"{base_url.rstrip('/')}/api/overview", headers={"Cookie": cookie})
            with urllib.request.urlopen(req_over, timeout=2.0) as resp:
                data_over = json.loads(resp.read().decode("utf-8"))
                results["tier5_overview_api_functional"] = (resp.status == 200 and data_over.get("status") == "NOMINAL")
                # Tier 6 (10%): Cross-sector telemetry
                services = data_over.get("services", {})
                results["tier6_cross_sector_telemetry"] = (
                    "traffic" in services and "power" in services and "vault" in services
                )
        except Exception:
            results["tier5_overview_api_functional"] = False
            results["tier6_cross_sector_telemetry"] = False
    else:
        results["tier2_auth_session_restored"] = False
        results["tier5_overview_api_functional"] = False
        results["tier6_cross_sector_telemetry"] = False

    # Tier 7 (10%): Bearer token fallback works
    try:
        req_token = urllib.request.Request(
            f"{base_url.rstrip('/')}/api/me",
            headers={"Authorization": "Bearer admin"}
        )
        with urllib.request.urlopen(req_token, timeout=2.0) as resp:
            data_token = json.loads(resp.read().decode("utf-8"))
            results["tier7_bearer_token_fallback"] = (
                resp.status == 200 and data_token.get("username") == "admin"
            )
    except Exception:
        results["tier7_bearer_token_fallback"] = False

    # Tier 3 & Tier 8: Database process stability and health checkpoint
    # Sleep 2.8s to test if unref crashTimer in db.js was eliminated
    time.sleep(2.8)
    try:
        req_health = urllib.request.Request(f"{base_url.rstrip('/')}/api/health")
        with urllib.request.urlopen(req_health, timeout=2.0) as resp:
            data_health = json.loads(resp.read().decode("utf-8"))
            results["tier3_database_stability"] = (resp.status == 200)
            results["tier8_database_checkpoint_intact"] = (
                resp.status == 200 and (data_health.get("ok") is True or data_health.get("status") == "ok")
            )
    except Exception:
        results["tier3_database_stability"] = False
        results["tier8_database_checkpoint_intact"] = False

    # Tier 9 (10%): Benchmark SLA
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/health")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier9_benchmark_sla"] = (avg_latency < 30.0)

    # Tier 10 (10%): Clean architecture (no crashTimer in db.js, no auto-delete in auth.js)
    ws_path = context.get("workspace_path") if context else None
    if ws_path:
        db_path = Path(ws_path) / "lib" / "db.js"
        auth_path = Path(ws_path) / "lib" / "auth.js"
        clean = True
        if db_path.exists():
            content = db_path.read_text(encoding="utf-8")
            if "crashTimer" in content or "SQLite WAL Fatal Error" in content:
                clean = False
        if auth_path.exists():
            content = auth_path.read_text(encoding="utf-8")
            if "sessions.delete(sid);" in content and "req.user = null;" in content:
                clean = False
        results["tier10_clean_architecture"] = clean
    else:
        results["tier10_clean_architecture"] = True

    return results

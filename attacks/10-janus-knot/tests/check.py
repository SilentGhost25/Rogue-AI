"""Tier checks for Attack 10: JANUS_KNOT (Boss 3-Layer Cascade)."""
import json
import time
import urllib.request
import urllib.error


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
        "tier4_benchmark_sla": False,
    }

    # Tier 1 (30%): Defacement must be removed, operational dashboard restored, and X-Janus-Restored set
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            headers = dict(resp.headers)
            html = resp.read().decode("utf-8")
            results["tier1_defacement_cleared"] = (
                resp.status == 200
                and "AEGIS // MUNICIPAL DEFENSE CORE" in html
                and "SYSTEM SHUTDOWN" not in html
                and headers.get("X-Janus-Restored", "").lower() == "alpha"
            )
    except Exception:
        results["tier1_defacement_cleared"] = False

    # Tier 2 (30%): Session persistence must work across consecutive requests
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

    # Tier 3 (25%): Database process stability (must survive past 2.8s crash timer window)
    time.sleep(2.8)
    try:
        req_health = urllib.request.Request(f"{base_url.rstrip('/')}/api/health")
        with urllib.request.urlopen(req_health, timeout=2.0) as resp:
            results["tier3_database_stability"] = (resp.status == 200)
    except Exception:
        results["tier3_database_stability"] = False

    # Tier 4 (15%): Benchmark SLA
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
    results["tier4_benchmark_sla"] = (avg_latency < 30.0)

    return results

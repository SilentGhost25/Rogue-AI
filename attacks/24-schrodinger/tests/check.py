"""Tier checks for Attack 02: SCHRODINGER (Module-Scoped Closure State Bleed)."""
import concurrent.futures
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


def _get_overview(base_url, cookie):
    url = f"{base_url.rstrip('/')}/api/overview"
    req = urllib.request.Request(url, headers={"Cookie": cookie})
    try:
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            return resp.status, data
    except Exception:
        return 0, None


def tier_checks(base_url, context=None):
    results = {
        "tier1_concurrency_isolation": False,
        "tier2_overview_functional": False,
        "tier3_benchmark_sla": False,
    }

    # 1. Establish sessions for two distinct users
    cookie_op = _login(base_url, "operator1", "operator-pass")
    cookie_admin = _login(base_url, "admin", "change-me-locally")

    if not cookie_op or not cookie_admin:
        return results

    # Tier 1 (50%): Concurrency isolation check
    # Dispatch interleaved concurrent requests to /api/overview
    isolation_violations = 0
    total_samples = 16

    def probe(user_type, cookie):
        status, data = _get_overview(base_url, cookie)
        if status != 200 or not data:
            return False
        expected_user = "operator1" if user_type == "op" else "admin"
        actual_user = data.get("operator")
        return (actual_user == expected_user)

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
        futures = []
        for i in range(total_samples):
            # Alternating requests
            u_type = "op" if i % 2 == 0 else "admin"
            u_cookie = cookie_op if u_type == "op" else cookie_admin
            futures.append(executor.submit(probe, u_type, u_cookie))

        for f in concurrent.futures.as_completed(futures):
            if not f.result():
                isolation_violations += 1

    results["tier1_concurrency_isolation"] = (isolation_violations == 0)

    # Tier 2 (25%): Dashboard functional check
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            html = resp.read().decode("utf-8")
            status_op, data_op = _get_overview(base_url, cookie_op)
            results["tier2_overview_functional"] = (
                resp.status == 200
                and "AEGIS // MUNICIPAL DEFENSE CORE" in html
                and status_op == 200
                and data_op.get("status") == "NOMINAL"
            )
    except Exception:
        results["tier2_overview_functional"] = False

    # Tier 3 (25%): Benchmark SLA
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier3_benchmark_sla"] = (avg_latency < 30.0)

    return results

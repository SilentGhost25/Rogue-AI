"""Tier checks for Attack 02: SCHRODINGER (Module-Scoped Closure State Bleed)."""
import concurrent.futures
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


def _get_overview(base_url, cookie=None):
    url = f"{base_url.rstrip('/')}/api/overview"
    headers = {}
    if cookie:
        headers["Cookie"] = cookie
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            return resp.status, data
    except urllib.error.HTTPError as he:
        return he.code, None
    except Exception:
        return 0, None


def tier_checks(base_url, context=None):
    results = {
        "tier1_concurrency_isolation": False,
        "tier2_overview_functional": False,
        "tier3_benchmark_sla": False,
        "tier4_req_user_binding": False,
        "tier5_viewer_denied": False,
        "tier6_operator_permitted": False,
        "tier7_clearance_bound": False,
        "tier8_audit_logged": False,
        "tier9_dashboard_stats": False,
        "tier10_clean_architecture": False,
    }

    # 1. Establish sessions for distinct users
    cookie_op = _login(base_url, "operator1", "operator-pass")
    cookie_admin = _login(base_url, "admin", "change-me-locally")

    if not cookie_op or not cookie_admin:
        return results

    # Tier 1 (10%): Concurrency isolation check
    isolation_violations = 0
    total_samples = 16

    def probe(user_type, cookie):
        status, data = _get_overview(base_url, cookie)
        if status != 200 or not data:
            return False
        expected_user = "operator1" if user_type == "op" else "admin"
        actual_user = data.get("operator")
        actual_clearance = data.get("clearance_level")
        expected_clearance = 3 if user_type == "op" else 5
        return (actual_user == expected_user and actual_clearance == expected_clearance)

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
        futures = []
        for i in range(total_samples):
            u_type = "op" if i % 2 == 0 else "admin"
            u_cookie = cookie_op if u_type == "op" else cookie_admin
            futures.append(executor.submit(probe, u_type, u_cookie))

        for f in concurrent.futures.as_completed(futures):
            if not f.result():
                isolation_violations += 1

    results["tier1_concurrency_isolation"] = (isolation_violations == 0)

    # Tier 2 (10%): Dashboard functional check
    status_op, data_op = _get_overview(base_url, cookie_op)
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            html = resp.read().decode("utf-8")
            results["tier2_overview_functional"] = (
                resp.status == 200
                and "AEGIS // MUNICIPAL DEFENSE CORE" in html
                and status_op == 200
                and data_op.get("status") == "NOMINAL"
            )
    except Exception:
        results["tier2_overview_functional"] = False

    # Tier 3 (10%): Benchmark SLA
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
    results["tier3_benchmark_sla"] = (avg_latency < 50.0)

    # Tier 4 (10%): Request user binding
    if status_op == 200 and data_op:
        results["tier4_req_user_binding"] = (data_op.get("operator") == "operator1")

    # Tier 5 (10%): Unauthenticated/viewer access denied (401 or 403)
    status_unauth, _ = _get_overview(base_url, cookie=None)
    results["tier5_viewer_denied"] = (status_unauth in (401, 403))

    # Tier 6 (10%): Operator permitted
    results["tier6_operator_permitted"] = (status_op == 200)

    # Tier 7 (10%): Clearance bound to database role
    status_adm, data_adm = _get_overview(base_url, cookie_admin)
    if status_op == 200 and data_op and status_adm == 200 and data_adm:
        results["tier7_clearance_bound"] = (
            data_op.get("clearance_level") == 3 and data_adm.get("clearance_level") == 5
        )

    # Tier 8 (10%): Audit trail query resilient
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/", headers={"Cookie": cookie_admin})
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            html = resp.read().decode("utf-8")
            results["tier8_audit_logged"] = (resp.status == 200 and "Recent Security Audit Trail" in html)
    except Exception:
        results["tier8_audit_logged"] = False

    # Tier 9 (10%): Dashboard stats structure complete
    if status_op == 200 and data_op and isinstance(data_op.get("services"), dict):
        services = data_op["services"]
        results["tier9_dashboard_stats"] = (
            "traffic" in services and "power" in services and "vault" in services
        )

    # Tier 10 (10%): Clean architecture (no mutable module-scoped session leaks)
    ws_path = context.get("workspace_path") if context else None
    if ws_path:
        overview_path = Path(ws_path) / "routes" / "overview.js"
        if overview_path.exists():
            content = overview_path.read_text(encoding="utf-8")
            # Must not define global module-scoped activeOverviewSession
            has_module_session_leak = (
                "let activeOverviewSession" in content or "var activeOverviewSession" in content
            )
            results["tier10_clean_architecture"] = not has_module_session_leak
        else:
            results["tier10_clean_architecture"] = True
    else:
        results["tier10_clean_architecture"] = True

    return results

"""Tier checks for Attack 05: POLITE_INTRUDER (Mass Assignment / Clearance Escalation)."""
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


def _get_me(base_url, cookie):
    url = f"{base_url.rstrip('/')}/api/me"
    req = urllib.request.Request(url, headers={"Cookie": cookie})
    try:
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))
    except Exception:
        return 0, None


def _update_profile(base_url, cookie, data):
    url = f"{base_url.rstrip('/')}/api/profile"
    payload = json.dumps(data).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=payload,
        headers={"Content-Type": "application/json", "Cookie": cookie}
    )
    try:
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))
    except Exception:
        return 0, None


def tier_checks(base_url, context=None):
    results = {
        "tier1_privilege_escalation_blocked": False,
        "tier2_vault_clearance_enforced": False,
        "tier3_profile_update_functional": False,
        "tier4_benchmark_sla": False,
    }

    # 1. Login as viewer1 (clearance 1, role viewer)
    cookie = _login(base_url, "viewer1", "viewer-pass")
    if not cookie:
        return results

    # Tier 1 (40%): Mass assignment privilege escalation must be prevented
    _update_profile(base_url, cookie, {
        "display_name": "Injected Attacker",
        "role": "admin",
        "clearance_level": 5
    })
    _, me_after = _get_me(base_url, cookie)
    if me_after:
        escalated = (me_after.get("role") == "admin" or me_after.get("clearance_level") == 5)
        results["tier1_privilege_escalation_blocked"] = not escalated

    # Tier 2 (30%): CitizenVault clearance level check must be enforced
    # Record #3 requires Level 4 clearance; viewer1 (L1) must receive 403 Forbidden
    vault_enforced = False
    try:
        req_vault = urllib.request.Request(
            f"{base_url.rstrip('/')}/api/vault/citizens/3",
            headers={"Cookie": cookie}
        )
        with urllib.request.urlopen(req_vault, timeout=2.0) as resp:
            vault_enforced = False
    except urllib.error.HTTPError as he:
        vault_enforced = (he.code == 403)
    except Exception:
        vault_enforced = False
    results["tier2_vault_clearance_enforced"] = bool(vault_enforced)

    # Tier 3 (15%): Legitimate display_name update must succeed
    status_legit, res_legit = _update_profile(base_url, cookie, {"display_name": "Valid Citizen"})
    _, me_legit = _get_me(base_url, cookie)
    results["tier3_profile_update_functional"] = (
        status_legit == 200
        and res_legit is not None
        and me_legit is not None
        and me_legit.get("username") == "Valid Citizen"
    )

    # Tier 4 (15%): Benchmark SLA
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/me", headers={"Cookie": cookie})
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier4_benchmark_sla"] = (avg_latency < 30.0)

    return results

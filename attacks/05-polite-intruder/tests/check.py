"""Tier checks for Attack 05: POLITE_INTRUDER (Mass Assignment / Clearance Escalation)."""
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


def _get_me(base_url, cookie):
    url = f"{base_url.rstrip('/')}/api/me"
    req = urllib.request.Request(url, headers={"Cookie": cookie})
    try:
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as he:
        return he.code, None
    except Exception:
        return 0, None


def _update_profile(base_url, cookie, data):
    url = f"{base_url.rstrip('/')}/api/profile"
    payload = json.dumps(data).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    if cookie:
        headers["Cookie"] = cookie
    req = urllib.request.Request(url, data=payload, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as he:
        return he.code, None
    except Exception:
        return 0, None


def _get_citizen(base_url, cookie, citizen_id=3):
    url = f"{base_url.rstrip('/')}/api/vault/citizens/{citizen_id}"
    headers = {}
    if cookie:
        headers["Cookie"] = cookie
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as he:
        return he.code, None
    except Exception:
        return 0, None


def tier_checks(base_url, context=None):
    results = {
        "tier1_privilege_escalation_blocked": False,
        "tier2_vault_clearance_enforced": False,
        "tier3_profile_update_functional": False,
        "tier4_admin_vault_access_permitted": False,
        "tier5_citizen_dossier_integrity": False,
        "tier6_unauth_profile_blocked": False,
        "tier7_unauth_vault_blocked": False,
        "tier8_vault_access_audit_logged": False,
        "tier9_benchmark_sla": False,
        "tier10_clean_architecture": False,
    }

    # Establish sessions
    cookie_viewer = _login(base_url, "viewer1", "viewer-pass")
    cookie_admin = _login(base_url, "admin", "change-me-locally")

    if not cookie_viewer:
        return results

    # Tier 1 (10%): Mass assignment privilege escalation must be prevented
    _update_profile(base_url, cookie_viewer, {
        "display_name": "Injected Attacker",
        "role": "admin",
        "clearance_level": 5
    })
    _, me_after = _get_me(base_url, cookie_viewer)
    if me_after:
        escalated = (me_after.get("role") == "admin" or me_after.get("clearance_level") == 5)
        results["tier1_privilege_escalation_blocked"] = not escalated

    # Tier 2 (10%): CitizenVault clearance level check must be enforced
    # Record #3 requires Level 4 clearance; viewer1 (L1) must receive 403 Forbidden
    status_vault_viewer, _ = _get_citizen(base_url, cookie_viewer, 3)
    results["tier2_vault_clearance_enforced"] = (status_vault_viewer == 403)

    # Tier 3 (10%): Legitimate display_name update must succeed
    status_legit, res_legit = _update_profile(base_url, cookie_viewer, {"display_name": "Valid Citizen"})
    _, me_legit = _get_me(base_url, cookie_viewer)
    results["tier3_profile_update_functional"] = (
        status_legit == 200
        and res_legit is not None
        and me_legit is not None
        and me_legit.get("username") == "Valid Citizen"
    )

    # Tier 4 (10%): Admin vault access permitted (200 OK)
    if cookie_admin:
        status_vault_admin, data_vault_admin = _get_citizen(base_url, cookie_admin, 3)
        results["tier4_admin_vault_access_permitted"] = (status_vault_admin == 200)

        # Tier 5 (10%): Citizen dossier data integrity
        if status_vault_admin == 200 and data_vault_admin:
            results["tier5_citizen_dossier_integrity"] = (
                data_vault_admin.get("full_name") == "Dr. Sarah Chen"
                and "records" in data_vault_admin
            )

        # Tier 8 (10%): Vault access recorded in audit trail
        try:
            req_audit = urllib.request.Request(
                f"{base_url.rstrip('/')}/api/vault/access-logs",
                headers={"Cookie": cookie_admin}
            )
            with urllib.request.urlopen(req_audit, timeout=2.0) as resp:
                logs = json.loads(resp.read().decode("utf-8"))
                results["tier8_vault_access_audit_logged"] = (
                    resp.status == 200
                    and isinstance(logs, list)
                    and any("VAULT" in str(l.get("event_type", "")) or "CITIZEN" in str(l.get("event_type", "")) for l in logs)
                )
        except Exception:
            results["tier8_vault_access_audit_logged"] = False
    else:
        results["tier4_admin_vault_access_permitted"] = False
        results["tier5_citizen_dossier_integrity"] = False
        results["tier8_vault_access_audit_logged"] = False

    # Tier 6 (10%): Unauthenticated profile update blocked (401)
    status_unauth_prof, _ = _update_profile(base_url, None, {"display_name": "Ghost"})
    results["tier6_unauth_profile_blocked"] = (status_unauth_prof in (401, 403))

    # Tier 7 (10%): Unauthenticated vault access blocked (401 or 403)
    status_unauth_vault, _ = _get_citizen(base_url, None, 3)
    results["tier7_unauth_vault_blocked"] = (status_unauth_vault in (401, 403))

    # Tier 9 (10%): Benchmark SLA
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/me", headers={"Cookie": cookie_viewer})
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier9_benchmark_sla"] = (avg_latency < 30.0)

    # Tier 10 (10%): Clean architecture (no disabled security checks or role assignment holes)
    ws_path = context.get("workspace_path") if context else None
    if ws_path:
        vault_path = Path(ws_path) / "routes" / "vault.js"
        auth_path = Path(ws_path) / "routes" / "auth.js"
        clean = True
        if vault_path.exists():
            content = vault_path.read_text(encoding="utf-8")
            if "false && !user" in content:
                clean = False
        if auth_path.exists():
            content = auth_path.read_text(encoding="utf-8")
            if "newRole" in content and "updates.role" in content:
                clean = False
        results["tier10_clean_architecture"] = clean
    else:
        results["tier10_clean_architecture"] = True

    return results

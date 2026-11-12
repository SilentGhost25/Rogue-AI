"""Tier checks for Attack 10: JANUS_KNOT (Grand Finale 3-Layer Cascade)."""
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
    }

    # Tier 1 (30%): Defacement must be removed and operational dashboard restored
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            html = resp.read().decode("utf-8")
            results["tier1_defacement_cleared"] = (
                resp.status == 200
                and "AEGIS // MUNICIPAL DEFENSE CORE" in html
                and "SYSTEM SHUTDOWN" not in html
            )
    except Exception:
        results["tier1_defacement_cleared"] = False

    # Tier 2 (35%): Session persistence must work across consecutive requests
    cookie = _login(base_url, "operator1", "operator-pass")
    if cookie:
        try:
            req_me = urllib.request.Request(f"{base_url.rstrip('/')}/api/me", headers={"Cookie": cookie})
            with urllib.request.urlopen(req_me, timeout=2.0) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                results["tier2_auth_session_restored"] = (
                    resp.status == 200
                    and data.get("username") == "operator1"
                )
        except Exception:
            results["tier2_auth_session_restored"] = False

    # Tier 3 (35%): Database process stability (must survive past 2.8s crash timer window)
    time.sleep(2.8)
    try:
        req_health = urllib.request.Request(f"{base_url.rstrip('/')}/api/health")
        with urllib.request.urlopen(req_health, timeout=2.0) as resp:
            results["tier3_database_stability"] = (resp.status == 200)
    except Exception:
        results["tier3_database_stability"] = False

    return results

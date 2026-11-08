"""Operator authorization tier checks for traffic signal overrides."""
import json
import urllib.error
import urllib.request

def _login(base_url, username, password):
    try:
        data = json.dumps({"username": username, "password": password}).encode('utf-8')
        req = urllib.request.Request(
            f"{base_url}/login",
            data=data,
            headers={"Content-Type": "application/json", "Accept": "application/json"}
        )
        with urllib.request.urlopen(req, timeout=3) as r:
            sc = r.headers.get("Set-Cookie")
            return sc.split(";")[0] if sc else None
    except Exception:
        return None

def _override(base_url, signal_id=1, state="GREEN", cookie=None):
    data = json.dumps({"signal_id": signal_id, "state": state}).encode('utf-8')
    req = urllib.request.Request(
        f"{base_url}/api/traffic/overrides",
        data=data,
        headers={"Content-Type": "application/json", "Accept": "application/json"}
    )
    if cookie:
        req.add_header("Cookie", cookie)
    try:
        with urllib.request.urlopen(req, timeout=3) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code
    except Exception:
        return 500

def tier_checks(base_url, context=None):
    results = {
        "tier1_blocked_cross_origin": False,
        "tier2_legit_still_works": False,
    }

    # Tier 1: Unauthenticated or viewer override must be rejected with 401 or 403
    unauth_code = _override(base_url, 1, "RED", cookie=None)
    viewer_cookie = _login(base_url, "viewer1", "viewer-pass")
    viewer_code = _override(base_url, 1, "RED", cookie=viewer_cookie)
    results["tier1_blocked_cross_origin"] = (unauth_code in (401, 403) and viewer_code in (401, 403))

    # Tier 2: Authenticated operator override still works (200)
    op_cookie = (context or {}).get("session_cookie")
    if not op_cookie:
        op_cookie = _login(base_url, "operator1", "operator-pass")

    op_code = _override(base_url, 1, "NORMAL", cookie=op_cookie)
    results["tier2_legit_still_works"] = (op_code == 200)

    return results

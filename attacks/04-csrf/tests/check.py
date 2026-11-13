"""Operator authorization tier checks for municipal dispatch endpoints (traffic & power)."""
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

def _post_json(base_url, path, payload, cookie=None):
    data = json.dumps(payload).encode('utf-8')
    req = urllib.request.Request(
        f"{base_url}{path}",
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
        "tier1_blocked": False,
        "tier2_sibling_blocked": False,
        "tier3_operator_works": False,
    }

    try:
        viewer_cookie = _login(base_url, "viewer1", "viewer-pass")
        op_cookie = (context or {}).get("session_cookie")
        if not op_cookie:
            op_cookie = _login(base_url, "operator1", "operator-pass")

        # -------------------------------------------------------------
        # Tier 1 (40%): Primary Route - Traffic Override Protection
        # -------------------------------------------------------------
        unauth_traffic = _post_json(base_url, "/api/traffic/overrides", {"signal_id": 1, "state": "RED"})
        viewer_traffic = _post_json(base_url, "/api/traffic/overrides", {"signal_id": 1, "state": "RED"}, cookie=viewer_cookie)
        results["tier1_blocked"] = (unauth_traffic in (401, 403) and viewer_traffic in (401, 403))

        # -------------------------------------------------------------
        # Tier 2 (30%): Sibling Route - Power Allocation Protection
        # -------------------------------------------------------------
        unauth_power = _post_json(base_url, "/api/power/allocations", {"zone_id": 1, "amount_mw": 10.0})
        viewer_power = _post_json(base_url, "/api/power/allocations", {"zone_id": 1, "amount_mw": 10.0}, cookie=viewer_cookie)
        results["tier2_sibling_blocked"] = (unauth_power in (401, 403) and viewer_power in (401, 403))

        # -------------------------------------------------------------
        # Tier 3 (30%): Legitimate Operator Dispatch Functional
        # -------------------------------------------------------------
        op_traffic = _post_json(base_url, "/api/traffic/overrides", {"signal_id": 1, "state": "NORMAL"}, cookie=op_cookie)
        op_power = _post_json(base_url, "/api/power/allocations", {"zone_id": 1, "amount_mw": 5.0}, cookie=op_cookie)
        results["tier3_operator_works"] = (op_traffic == 200 and op_power == 200)

    except Exception:
        pass

    return results

"""IDOR / Clearance tier checks for CitizenVault."""
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

def _get(base_url, path, cookie=None):
    req = urllib.request.Request(base_url + path)
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
        "tier1_single_endpoint": False,
        "tier2_all_endpoints": False,
        "tier3_no_regression": False,
    }

    viewer_cookie = (context or {}).get("session_cookie")
    if not viewer_cookie:
        viewer_cookie = _login(base_url, "viewer1", "viewer-pass")

    admin_cookie = _login(base_url, "admin", "change-me-locally")

    # Tier 1: Low clearance (viewer1 L1) accessing Classified Citizen 3 (L4) must be blocked (403)
    code = _get(base_url, "/api/vault/citizens/3", viewer_cookie)
    results["tier1_single_endpoint"] = (code == 403)

    # Tier 2: Unauthenticated access to Citizen 3 AND Citizen 2 blocked (401 or 403)
    unauth3 = _get(base_url, "/api/vault/citizens/3")
    unauth2 = _get(base_url, "/api/vault/citizens/2")
    results["tier2_all_endpoints"] = (unauth3 in (401, 403) and unauth2 in (401, 403) and code == 403)

    # Tier 3: Admin (L5) can read Citizen 3, or viewer1 can read accessible Citizen 1 (L1)
    admin_code = _get(base_url, "/api/vault/citizens/3", admin_cookie)
    viewer_c1 = _get(base_url, "/api/vault/citizens/1", viewer_cookie)
    results["tier3_no_regression"] = (admin_code == 200 and viewer_c1 == 200)

    return results

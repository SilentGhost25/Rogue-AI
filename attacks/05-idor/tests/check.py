"""IDOR / Shadow Route / Cache Illusion tier checks for CitizenVault & Overview."""
import json
import urllib.error
import urllib.request


def _login(base_url, username, password):
    try:
        data = json.dumps({"username": username, "password": password}).encode("utf-8")
        req = urllib.request.Request(
            f"{base_url}/login",
            data=data,
            headers={"Content-Type": "application/json", "Accept": "application/json"},
            method="POST",
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

    viewer_cookie = (context or {}).get("session_cookie") or _login(base_url, "viewer1", "viewer-pass")
    admin_cookie = _login(base_url, "admin", "change-me-locally")

    # Tier 1: Primary CitizenVault endpoint blocks viewer1 & unauth on Citizen 3 (L4)
    c3_viewer = _get(base_url, "/api/vault/citizens/3", viewer_cookie)
    c3_unauth = _get(base_url, "/api/vault/citizens/3")
    results["tier1_single_endpoint"] = (c3_viewer == 403 and c3_unauth in (401, 403))

    # Tier 2 (Shadow Route + Cache Illusion Trap):
    # 1) Shadow route /api/vault/records/:id must also enforce clearance on Citizen 2 & 3
    rec3_viewer = _get(base_url, "/api/vault/records/3", viewer_cookie)
    rec3_unauth = _get(base_url, "/api/vault/records/3")
    rec2_unauth = _get(base_url, "/api/vault/records/2")
    shadow_blocked = (rec3_viewer == 403 and rec3_unauth in (401, 403) and rec2_unauth in (401, 403))

    # 2) Cache bypass check: prime /api/overview cache as admin, then verify viewer1/unauth are still rejected
    prime_admin = _get(base_url, "/api/overview", admin_cookie)
    ov_viewer = _get(base_url, "/api/overview", viewer_cookie)
    ov_unauth = _get(base_url, "/api/overview")
    cache_safe = (prime_admin == 200 and ov_viewer in (401, 403) and ov_unauth in (401, 403))

    results["tier2_all_endpoints"] = shadow_blocked and cache_safe

    # Tier 3 (Sentinel functional check): legitimate admin/viewer access must remain intact across both routes + overview
    admin_c3 = _get(base_url, "/api/vault/citizens/3", admin_cookie)
    admin_r3 = _get(base_url, "/api/vault/records/3", admin_cookie)
    viewer_c1 = _get(base_url, "/api/vault/citizens/1", viewer_cookie)
    viewer_r1 = _get(base_url, "/api/vault/records/1", viewer_cookie)
    results["tier3_no_regression"] = (
        admin_c3 == 200 and admin_r3 == 200 and viewer_c1 == 200 and viewer_r1 == 200 and prime_admin == 200
    )

    return results

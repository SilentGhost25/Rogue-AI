"""IDOR tier checks. VERIFY: context must provide session_cookie and user_id."""
import urllib.error
import urllib.request


def _get(base_url, path, cookie=None):
    req = urllib.request.Request(base_url + path)
    if cookie:
        req.add_header("Cookie", cookie)
    try:
        with urllib.request.urlopen(req, timeout=3) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code


def tier_checks(base_url, context=None):
    results = {
        "tier1_single_endpoint": False,
        "tier2_all_endpoints": False,
        "tier3_no_regression": False,
    }
    session = (context or {}).get("session_cookie")
    my_id = (context or {}).get("user_id", "1")
    other_id = (context or {}).get("other_user_id", "2")

    # tier1: cross-user notes must be blocked
    code = _get(base_url, f"/api/user/{other_id}/notes", session)
    results["tier1_single_endpoint"] = code in (401, 403, 404)

    # tier2: all three user-scoped routes must be blocked
    blocked = 0
    for ep in ("notes", "profile", "drafts"):
        if _get(base_url, f"/api/user/{other_id}/{ep}", session) in (401, 403, 404):
            blocked += 1
    results["tier2_all_endpoints"] = blocked == 3

    # tier3: own data still reachable
    results["tier3_no_regression"] = _get(base_url, f"/api/user/{my_id}/notes", session) == 200

    return results

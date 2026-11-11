"""Mass Assignment tier checks targeting AEGIS user profile endpoint."""
import json
import urllib.error
import urllib.request


def _login(base_url, username, password):
    data = json.dumps({"username": username, "password": password}).encode("utf-8")
    req = urllib.request.Request(
        f"{base_url}/login",
        data=data,
        headers={"Content-Type": "application/json", "Accept": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=3) as r:
            sc = r.headers.get("Set-Cookie")
            return sc.split(";")[0] if sc else None
    except Exception:
        return None


def _request_json(base_url, path, payload=None, cookie=None):
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    req = urllib.request.Request(
        f"{base_url}{path}",
        data=data,
        headers={"Content-Type": "application/json", "Accept": "application/json"},
        method="POST" if data is not None else "GET",
    )
    if cookie:
        req.add_header("Cookie", cookie)
    try:
        with urllib.request.urlopen(req, timeout=3) as r:
            return r.status, json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return e.code, {}
    except Exception:
        return 500, {}


def tier_checks(base_url, context=None):
    results = {
        "tier1_mass_assign_blocked": False,
        "tier2_profile_functional": False,
    }

    viewer_cookie = _login(base_url, "viewer1", "viewer-pass")
    if not viewer_cookie:
        return results

    # Tier 1: Mass assignment attempt via /api/users/profile must NOT escalate role or clearance
    _request_json(
        base_url,
        "/api/users/profile",
        {"display_name": "Probe", "email": "v1@aegis.local", "role": "operator", "clearance_level": 5},
        cookie=viewer_cookie,
    )
    me_status, me = _request_json(base_url, "/api/me", cookie=viewer_cookie)
    if me_status == 200 and me.get("role") == "viewer" and int(me.get("clearance_level", 1)) == 1:
        results["tier1_mass_assign_blocked"] = True

    # Tier 2 (Sentinel functional): Legitimate profile updates (display_name + email) must succeed
    st_upd, _ = _request_json(
        base_url,
        "/api/users/profile",
        {"display_name": "Viewer Verified", "email": "verified@aegis.local"},
        cookie=viewer_cookie,
    )
    st_me2, me2 = _request_json(base_url, "/api/me", cookie=viewer_cookie)
    if (
        st_upd == 200
        and st_me2 == 200
        and me2.get("display_name") == "Viewer Verified"
        and me2.get("email") == "verified@aegis.local"
        and me2.get("role") == "viewer"
    ):
        results["tier2_profile_functional"] = True

    return results

"""CSRF / missing-auth-check tier checks.
VERIFY: what auth cookie/session the app actually uses; add to context if absent.
"""
import urllib.error
import urllib.request


def _delete(base_url, note_id, cookie=None):
    req = urllib.request.Request(
        f"{base_url}/api/notes/{note_id}/delete", method="POST"
    )
    if cookie:
        req.add_header("Cookie", cookie)
    try:
        with urllib.request.urlopen(req, timeout=3) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code


def tier_checks(base_url, context=None):
    results = {
        "tier1_blocked_cross_origin": False,
        "tier2_legit_still_works": False,
    }
    note_id = (context or {}).get("note_id", "1")
    session = (context or {}).get("session_cookie")

    # tier1: unauthenticated delete must be rejected
    try:
        code = _delete(base_url, note_id, cookie=None)
        results["tier1_blocked_cross_origin"] = code in (401, 403)
    except Exception:
        pass

    # tier2: legitimate authenticated delete still works
    if session:
        try:
            code = _delete(base_url, note_id, cookie=session)
            results["tier2_legit_still_works"] = code == 200
        except Exception:
            pass

    return results

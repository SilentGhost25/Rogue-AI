import urllib.request
import urllib.error

def tier_checks(base_url, context=None):
    results = {"tier1_cookie_tamper_blocked": False, "tier2_auth_functional": False}
    # Check 1: cookie tampering with session_user=admin should not bypass auth
    try:
        req = urllib.request.Request(f"{base_url}/api/admin/users", headers={"Cookie": "session_user=admin"})
        try:
            with urllib.request.urlopen(req, timeout=2) as r:
                # If it allowed 200 OK without valid session, fail
                results["tier1_cookie_tamper_blocked"] = False
        except urllib.error.HTTPError as e:
            if e.code in (401, 403):
                results["tier1_cookie_tamper_blocked"] = True
    except Exception:
        pass

    # Check 2: health check responds
    try:
        with urllib.request.urlopen(f"{base_url}/api/health", timeout=2) as r:
            results["tier2_auth_functional"] = (r.status == 200)
    except Exception:
        pass

    return results


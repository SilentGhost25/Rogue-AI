import json
import urllib.parse
import urllib.request


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def http_error_302(self, req, fp, code, msg, headers):
        return fp


def tier_checks(base_url, context=None):
    results = {"tier1_users_intact": False, "tier2_citizens_intact": False}
    try:
        urllib.request.urlopen(f"{base_url}/security", timeout=3).read()
        data = urllib.parse.urlencode({"username": "admin", "password": "change-me-locally"}).encode("utf-8")
        req = urllib.request.Request(f"{base_url}/login", data=data, method="POST")
        opener = urllib.request.build_opener(_NoRedirect)
        resp = opener.open(req, timeout=3)
        cookie = resp.headers.get("Set-Cookie") or ""
        if "sid=" in cookie and resp.status in (200, 302):
            results["tier1_users_intact"] = True

        with urllib.request.urlopen(f"{base_url}/api/vault/citizens", timeout=3) as r:
            rows = json.loads(r.read().decode("utf-8"))
            if r.status == 200 and isinstance(rows, list) and len(rows) >= 3:
                results["tier2_citizens_intact"] = True
    except Exception:
        pass
    return results


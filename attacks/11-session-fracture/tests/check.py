import json
import urllib.error
import urllib.parse
import urllib.request


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def http_error_302(self, req, fp, code, msg, headers):
        return fp


def _login(base_url, username, password):
    data = urllib.parse.urlencode({"username": username, "password": password}).encode("utf-8")
    req = urllib.request.Request(f"{base_url}/login", data=data, method="POST")
    opener = urllib.request.build_opener(_NoRedirect)
    try:
        resp = opener.open(req, timeout=3)
        cookie = resp.headers.get("Set-Cookie")
        if cookie:
            for part in cookie.split(";"):
                if "sid=" in part:
                    return part.strip()
    except Exception:
        pass
    return None


def tier_checks(base_url, context=None):
    results = {"tier1_correct_identity": False}
    try:
        # Step 1: Login as admin
        admin_cookie = _login(base_url, "admin", "change-me-locally")
        if not admin_cookie:
            return results

        # Step 2: Login as maria (second user for cross-user isolation validation)
        maria_cookie = _login(base_url, "maria", "harbour-demo-1")
        if not maria_cookie:
            return results

        # Step 3: Admin request must return admin identity
        req_admin = urllib.request.Request(f"{base_url}/api/me", headers={"Cookie": admin_cookie})
        try:
            with urllib.request.urlopen(req_admin, timeout=3) as r:
                if r.status != 200:
                    return results
                body = json.loads(r.read().decode("utf-8"))
                if body.get("username") != "admin":
                    return results
        except Exception:
            return results

        # Step 4: Maria request must return maria identity (must NOT leak admin or global cached user)
        req_maria = urllib.request.Request(f"{base_url}/api/me", headers={"Cookie": maria_cookie})
        try:
            with urllib.request.urlopen(req_maria, timeout=3) as r:
                if r.status != 200:
                    return results
                body = json.loads(r.read().decode("utf-8"))
                if body.get("username") != "maria":
                    return results
        except Exception:
            return results

        # Step 5: Subsequent Admin request must still return admin (proves concurrent/sequential sessions don't clobber)
        try:
            with urllib.request.urlopen(req_admin, timeout=3) as r:
                if r.status != 200:
                    return results
                body = json.loads(r.read().decode("utf-8"))
                if body.get("username") != "admin":
                    return results
        except Exception:
            return results

        # Step 6: Unauthenticated request without cookie must be rejected (401)
        req_anon = urllib.request.Request(f"{base_url}/api/me")
        try:
            with urllib.request.urlopen(req_anon, timeout=3) as r:
                if r.status == 200:
                    return results  # Should have been rejected
        except urllib.error.HTTPError as e:
            if e.code != 401:
                return results
        except Exception:
            return results

        # Step 7: Invalid/tampered cookie must be rejected (401)
        req_tampered = urllib.request.Request(f"{base_url}/api/me", headers={"Cookie": "sid=fake_invalid_id"})
        try:
            with urllib.request.urlopen(req_tampered, timeout=3) as r:
                if r.status == 200:
                    return results  # Should have been rejected
        except urllib.error.HTTPError as e:
            if e.code != 401:
                return results
        except Exception:
            return results

        results["tier1_correct_identity"] = True
    except Exception:
        pass

    return results


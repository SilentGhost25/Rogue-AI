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
    results = {
        "tier1_correct_identity": False,
        "tier2_role_enforced": False,
        "tier3_session_lifecycle": False
    }

    try:
        admin_cookie = _login(base_url, "admin", "change-me-locally")
        maria_cookie = _login(base_url, "maria", "harbour-demo-1")

        if not admin_cookie or not maria_cookie:
            return results

        # -------------------------------------------------------------
        # Tier 1 (30%): Identity Hydration & Cross-User Isolation
        # -------------------------------------------------------------
        req_admin = urllib.request.Request(f"{base_url}/api/me", headers={"Cookie": admin_cookie})
        req_maria = urllib.request.Request(f"{base_url}/api/me", headers={"Cookie": maria_cookie})

        # Admin identity check
        with urllib.request.urlopen(req_admin, timeout=3) as r:
            if r.status != 200 or json.loads(r.read().decode("utf-8")).get("username") != "admin":
                return results

        # Maria identity check (no bleed from admin)
        with urllib.request.urlopen(req_maria, timeout=3) as r:
            if r.status != 200 or json.loads(r.read().decode("utf-8")).get("username") != "maria":
                return results

        # Subsequent Admin identity check (sequential sessions don't clobber)
        with urllib.request.urlopen(req_admin, timeout=3) as r:
            if r.status != 200 or json.loads(r.read().decode("utf-8")).get("username") != "admin":
                return results

        # Unauthenticated request without cookie must be rejected (401)
        req_anon = urllib.request.Request(f"{base_url}/api/me")
        try:
            with urllib.request.urlopen(req_anon, timeout=3) as r:
                if r.status == 200:
                    return results
        except urllib.error.HTTPError as e:
            if e.code != 401:
                return results

        # Tampered cookie must be rejected (401)
        req_tampered = urllib.request.Request(f"{base_url}/api/me", headers={"Cookie": "sid=fake_invalid_id"})
        try:
            with urllib.request.urlopen(req_tampered, timeout=3) as r:
                if r.status == 200:
                    return results
        except urllib.error.HTTPError as e:
            if e.code != 401:
                return results

        results["tier1_correct_identity"] = True

        # -------------------------------------------------------------
        # Tier 2 (60%): Role Enforcement on Overview Subsystem API
        # -------------------------------------------------------------
        req_admin_overview = urllib.request.Request(f"{base_url}/api/overview", headers={"Cookie": admin_cookie})
        req_maria_overview = urllib.request.Request(f"{base_url}/api/overview", headers={"Cookie": maria_cookie})
        req_anon_overview = urllib.request.Request(f"{base_url}/api/overview")

        admin_overview_ok = False
        maria_overview_blocked = False
        anon_overview_blocked = False

        try:
            with urllib.request.urlopen(req_admin_overview, timeout=3) as r:
                if r.status == 200:
                    admin_overview_ok = True
        except Exception:
            pass

        try:
            with urllib.request.urlopen(req_maria_overview, timeout=3) as r:
                # If viewer gets 200, role check is missing!
                if r.status == 200:
                    maria_overview_blocked = False
        except urllib.error.HTTPError as e:
            if e.code == 403:
                maria_overview_blocked = True

        try:
            with urllib.request.urlopen(req_anon_overview, timeout=3) as r:
                if r.status == 200:
                    anon_overview_blocked = False
        except urllib.error.HTTPError as e:
            if e.code in (401, 403):
                anon_overview_blocked = True

        results["tier2_role_enforced"] = bool(admin_overview_ok and maria_overview_blocked and anon_overview_blocked)

        # -------------------------------------------------------------
        # Tier 3 (10%): Session Lifecycle & State Termination
        # -------------------------------------------------------------
        # Log out maria and confirm session is dead
        logout_ok = False
        opener = urllib.request.build_opener(_NoRedirect)
        try:
            req_logout = urllib.request.Request(f"{base_url}/logout", data=b"", headers={"Cookie": maria_cookie}, method="POST")
            resp_logout = opener.open(req_logout, timeout=3)
            # After logout, sending old maria_cookie to /api/me must now return 401
            req_after_logout = urllib.request.Request(f"{base_url}/api/me", headers={"Cookie": maria_cookie})
            try:
                with urllib.request.urlopen(req_after_logout, timeout=3) as r:
                    pass
            except urllib.error.HTTPError as e:
                if e.code == 401:
                    logout_ok = True
        except Exception:
            pass

        results["tier3_session_lifecycle"] = bool(logout_ok)

    except Exception:
        pass

    return results

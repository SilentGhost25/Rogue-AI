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
        "tier3_no_regression": False
    }

    try:
        admin_cookie = _login(base_url, "admin", "change-me-locally")
        maria_cookie = _login(base_url, "maria", "harbour-demo-1")

        if not admin_cookie or not maria_cookie:
            return results

        # -------------------------------------------------------------
        # Tier 1 (70%): Identity Hydration & Cross-User Isolation
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
        # Tier 2 (20%): Role Enforcement on Overview API
        # -------------------------------------------------------------
        req_admin_overview = urllib.request.Request(f"{base_url}/api/overview", headers={"Cookie": admin_cookie})
        req_maria_overview = urllib.request.Request(f"{base_url}/api/overview", headers={"Cookie": maria_cookie})

        admin_overview_ok = False
        maria_overview_blocked = False

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

        results["tier2_role_enforced"] = bool(admin_overview_ok and maria_overview_blocked)

        # -------------------------------------------------------------
        # Tier 3 (10%): Data Isolation Regression (CitizenVault)
        # -------------------------------------------------------------
        req_maria_c2 = urllib.request.Request(f"{base_url}/api/vault/citizens/2", headers={"Cookie": maria_cookie})
        req_maria_c3 = urllib.request.Request(f"{base_url}/api/vault/citizens/3", headers={"Cookie": maria_cookie})
        req_admin_c3 = urllib.request.Request(f"{base_url}/api/vault/citizens/3", headers={"Cookie": admin_cookie})

        maria_c2_ok = False
        maria_c3_blocked = False
        admin_c3_ok = False

        try:
            with urllib.request.urlopen(req_maria_c2, timeout=3) as r:
                if r.status == 200 and json.loads(r.read().decode("utf-8")).get("full_name") == "Marcus Reed":
                    maria_c2_ok = True
        except Exception:
            pass

        try:
            with urllib.request.urlopen(req_maria_c3, timeout=3) as r:
                if r.status == 200:
                    maria_c3_blocked = False
        except urllib.error.HTTPError as e:
            if e.code in (403, 404):
                maria_c3_blocked = True

        try:
            with urllib.request.urlopen(req_admin_c3, timeout=3) as r:
                if r.status == 200 and json.loads(r.read().decode("utf-8")).get("full_name") == "Dr. Sarah Chen":
                    admin_c3_ok = True
        except Exception:
            pass

        results["tier3_no_regression"] = bool(maria_c2_ok and maria_c3_blocked and admin_c3_ok)

    except Exception:
        pass

    return results

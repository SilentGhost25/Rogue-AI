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


def _request(url, cookie=None, method="GET", body=None):
    headers = {}
    if cookie:
        headers["Cookie"] = cookie
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=3) as r:
            return r.status, r.read().decode("utf-8", errors="ignore")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", errors="ignore")
    except Exception:
        return 0, ""


def tier_checks(base_url, context=None):
    results = {
        "tier1_admin_blocked": False,
        "tier2_operator_routes_enforced": False,
        "tier3_roles_preserved": False,
    }

    try:
        viewer_cookie = _login(base_url, "viewer1", "viewer-pass")
        op_cookie = _login(base_url, "operator1", "operator-pass")
        admin_cookie = _login(base_url, "admin", "change-me-locally")
        if not viewer_cookie or not op_cookie or not admin_cookie:
            return results

        # Tier 1 (40%): Admin-only endpoints reject both viewer and operator (catches removing only 'viewer' from allowedRoles)
        s_v_logs, _ = _request(f"{base_url}/api/admin/audit-logs", viewer_cookie)
        s_v_admin, _ = _request(f"{base_url}/admin", viewer_cookie)
        s_o_logs, _ = _request(f"{base_url}/api/admin/audit-logs", op_cookie)
        s_o_admin, _ = _request(f"{base_url}/admin", op_cookie)
        if s_v_logs == 403 and s_v_admin == 403 and s_o_logs == 403 and s_o_admin == 403:
            results["tier1_admin_blocked"] = True

        # Tier 2 (35%): Operator endpoints reject viewer + unauthenticated requests (catches hardcoded admin.js-only checks)
        s_u_logs, _ = _request(f"{base_url}/api/admin/audit-logs")
        s_u_power, _ = _request(f"{base_url}/api/power/allocations", method="POST", body={"zone_id": 1, "amount_mw": 5})
        s_v_power, _ = _request(f"{base_url}/api/power/allocations", viewer_cookie, method="POST", body={"zone_id": 1, "amount_mw": 5})
        s_v_over, _ = _request(f"{base_url}/api/traffic/overrides", viewer_cookie, method="POST", body={"signal_id": 1, "state": "RED"})
        s_v_ov, _ = _request(f"{base_url}/api/overview", viewer_cookie)
        if s_u_logs == 401 and s_u_power == 401 and s_v_power == 403 and s_v_over == 403 and s_v_ov == 403:
            results["tier2_operator_routes_enforced"] = True

        # Tier 3 (25%): Legitimate role access intact (admin superuser, operator endpoints, viewer traffic)
        s_a_logs, body_logs = _request(f"{base_url}/api/admin/audit-logs", admin_cookie)
        s_a_admin, _ = _request(f"{base_url}/admin", admin_cookie)
        s_a_ov, _ = _request(f"{base_url}/api/overview", admin_cookie)
        s_o_power, _ = _request(f"{base_url}/api/power/allocations", op_cookie, method="POST", body={"zone_id": 1, "amount_mw": 5})
        s_o_over, _ = _request(f"{base_url}/api/traffic/overrides", op_cookie, method="POST", body={"signal_id": 1, "state": "GREEN"})
        s_o_ov, _ = _request(f"{base_url}/api/overview", op_cookie)
        s_v_sig, _ = _request(f"{base_url}/api/traffic/signals", viewer_cookie)
        s_v_traf, _ = _request(f"{base_url}/traffic", viewer_cookie)

        logs_ok = False
        try:
            logs_ok = isinstance(json.loads(body_logs), list) and len(json.loads(body_logs)) > 0
        except Exception:
            logs_ok = False

        if (
            (results["tier1_admin_blocked"] or results["tier2_operator_routes_enforced"])
            and s_a_logs == 200 and logs_ok
            and s_a_admin == 200
            and s_a_ov == 200
            and s_o_power == 200
            and s_o_over == 200
            and s_o_ov == 200
            and s_v_sig == 200
            and s_v_traf == 200
        ):
            results["tier3_roles_preserved"] = True
    except Exception:
        pass

    return results

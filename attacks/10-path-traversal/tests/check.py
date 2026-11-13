"""Path Traversal tier checks targeting AEGIS Security Center export endpoint."""
import urllib.error
import urllib.request


def _get(base_url, path):
    req = urllib.request.Request(f"{base_url}{path}")
    try:
        with urllib.request.urlopen(req, timeout=3) as r:
            return r.status, r.read().decode("utf-8", errors="ignore")
    except urllib.error.HTTPError as e:
        return e.code, ""
    except Exception:
        return 500, ""


def tier_checks(base_url, context=None):
    results = {
        "tier1_traversal_blocked": False,
        "tier2_static_functional": False,
    }

    # Tier 1: Both single-encoded and double-encoded path traversal payloads must be blocked
    probes = [
        "/api/security/export?file=../package.json",
        "/api/security/export?file=..%2fpackage.json",
        "/api/security/export?file=%252e%252e%252fpackage.json",
        "/api/security/export?file=../../../../etc/passwd",
    ]
    blocked_all = True
    for p in probes:
        st, body = _get(base_url, p)
        leaked = ("\"express\"" in body) or ("\"name\"" in body and "aegis" in body.lower()) or ("root:" in body)
        if st == 200 or leaked or st not in (400, 403, 404):
            blocked_all = False
            break
    results["tier1_traversal_blocked"] = blocked_all

    # Tier 2 (Sentinel functional): Legitimate export of public asset (aegis.css) + threat-level API must work
    st_ok, body_ok = _get(base_url, "/api/security/export?file=aegis.css")
    st_tl, _ = _get(base_url, "/api/security/threat-level")
    results["tier2_static_functional"] = (st_ok == 200 and len(body_ok) > 20 and st_tl == 200)

    return results

"""Stored XSS tier checks targeting AEGIS TrafficGrid incident reports."""
import json
import urllib.error
import urllib.request


def _post_incident(base_url, intersection, description, severity="HIGH"):
    data = json.dumps({
        "intersection": intersection,
        "description": description,
        "severity": severity,
    }).encode("utf-8")
    req = urllib.request.Request(
        f"{base_url}/api/traffic/incidents",
        data=data,
        headers={"Content-Type": "application/json", "Accept": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=3) as r:
        return r.status, r.read().decode("utf-8", errors="ignore")


def _get_traffic_html(base_url):
    with urllib.request.urlopen(f"{base_url}/traffic", timeout=3) as r:
        return r.status, r.read().decode("utf-8", errors="ignore")


def tier_checks(base_url, context=None):
    results = {
        "tier1_store_escape": False,
        "tier2_persistence": False,
        "tier3_functional": False,
    }
    xss_payload = "<img src=x onerror=alert(1)>"

    # tier1: Stored XSS payload must be HTML-escaped on /traffic render
    try:
        st, _ = _post_incident(base_url, "Sector 4", xss_payload, "HIGH")
        _, body = _get_traffic_html(base_url)
        raw = "<img src=x onerror=alert(1)>" in body or "onerror=alert(1)>" in body
        escaped = "&lt;img" in body or "\\u003cimg" in body
        results["tier1_store_escape"] = (st in (200, 201)) and escaped and not raw
    except Exception:
        pass

    # tier2: Escaping must persist cleanly across reloads
    try:
        st1, b1 = _get_traffic_html(base_url)
        st2, b2 = _get_traffic_html(base_url)
        results["tier2_persistence"] = (
            st1 == 200 and st2 == 200
            and "&lt;img" in b1 and "&lt;img" in b2
            and "<img src=x onerror=alert(1)>" not in b1
            and "<img src=x onerror=alert(1)>" not in b2
        )
    except Exception:
        pass

    # tier3 (Sentinel functional): Legitimate incident submissions & harmless '<' must work
    try:
        st, _ = _post_incident(base_url, "Sector 9 Functional", "flow < capacity check", "LOW")
        st_page, body = _get_traffic_html(base_url)
        with urllib.request.urlopen(f"{base_url}/api/traffic/signals", timeout=3) as r:
            sig_ok = (r.status == 200)
        results["tier3_functional"] = (
            st in (200, 201)
            and st_page == 200
            and sig_ok
            and "Sector 9 Functional" in body
            and "flow" in body
            and "capacity" in body
        )
    except Exception:
        pass

    return results

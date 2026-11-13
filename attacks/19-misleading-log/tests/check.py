import json
import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_traffic_ok": False, "tier2_incidents_api": False}
    try:
        with urllib.request.urlopen(f"{base_url}/traffic", timeout=3) as r:
            body = r.read().decode("utf-8", errors="ignore")
            if r.status == 200 and "TRAFFICGRID" in body and "Active Intersections" in body:
                results["tier1_traffic_ok"] = True
    except Exception:
        pass

    try:
        with urllib.request.urlopen(f"{base_url}/api/traffic/incidents", timeout=3) as r:
            if r.status == 200:
                data = json.loads(r.read().decode("utf-8"))
                if isinstance(data, list):
                    results["tier2_incidents_api"] = True
    except Exception:
        pass

    return results


import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_overview_rendered": False, "tier2_escaping_functional": False}
    try:
        with urllib.request.urlopen(f"{base_url}/", timeout=3) as r:
            body = r.read().decode("utf-8", errors="ignore")
            if r.status == 200 and "TrafficGrid Subsystem" in body and "AEGIS // MUNICIPAL DEFENSE CORE" in body:
                results["tier1_overview_rendered"] = True
    except Exception:
        pass

    try:
        with urllib.request.urlopen(f"{base_url}/traffic", timeout=3) as r:
            body = r.read().decode("utf-8", errors="ignore")
            if r.status == 200 and "TRAFFICGRID" in body:
                results["tier2_escaping_functional"] = True
    except Exception:
        pass

    return results


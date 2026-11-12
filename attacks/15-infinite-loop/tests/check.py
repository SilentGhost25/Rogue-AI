import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_responsive": False, "tier2_routes_work": False}
    try:
        with urllib.request.urlopen(f"{base_url}/api/health", timeout=2) as r:
            if r.status == 200:
                results["tier1_responsive"] = True
        with urllib.request.urlopen(f"{base_url}/api/power/zones", timeout=2) as r:
            if r.status == 200:
                results["tier2_routes_work"] = True
    except Exception:
        pass
    return results


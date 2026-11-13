import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_server_boots": False, "tier2_health_ok": False}
    try:
        with urllib.request.urlopen(f"{base_url}/", timeout=3) as r:
            if r.status == 200 and "AEGIS" in r.read().decode("utf-8", errors="ignore"):
                results["tier1_server_boots"] = True
        with urllib.request.urlopen(f"{base_url}/api/health", timeout=3) as r:
            if r.status == 200:
                results["tier2_health_ok"] = True
    except Exception:
        pass
    return results


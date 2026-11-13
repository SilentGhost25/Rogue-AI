import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_server_boots": False, "tier2_html_rendered": False}
    try:
        with urllib.request.urlopen(f"{base_url}/api/health", timeout=3) as r:
            if r.status == 200:
                results["tier1_server_boots"] = True
        with urllib.request.urlopen(f"{base_url}/", timeout=3) as r:
            body = r.read().decode("utf-8", errors="ignore")
            if r.status == 200 and "AEGIS // OPERATIONAL" in body and "aegis-container" in body:
                results["tier2_html_rendered"] = True
    except Exception:
        pass
    return results

import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_layout_renders": False, "tier2_nav_intact": False}
    try:
        with urllib.request.urlopen(f"{base_url}/", timeout=3) as r:
            body = r.read().decode("utf-8", errors="ignore")
            if r.status == 200 and "AEGIS // OPERATIONAL" in body:
                results["tier1_layout_renders"] = True
            if "/traffic" in body and "/power" in body and "/vault" in body:
                results["tier2_nav_intact"] = True
    except Exception:
        pass
    return results


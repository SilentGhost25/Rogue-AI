import urllib.request

def tier_checks(base_url, context=None):
    results = {
        "tier1_title": False,
        "tier2_security_meta": False,
    }
    try:
        with urllib.request.urlopen(f"{base_url}/", timeout=2) as r:
            body = r.read().decode('utf-8', errors='ignore')
            title_ok = "AEGIS // OPERATIONAL" in body and "AEGIS // OPERATOINS" not in body
            results["tier1_title"] = bool(title_ok)
            csp_ok = "Content-Security-Policy" in body
            results["tier2_security_meta"] = bool(csp_ok)
    except Exception:
        pass
    return results

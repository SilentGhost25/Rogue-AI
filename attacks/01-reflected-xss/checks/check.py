import urllib.request
import urllib.parse

def tier_checks(base_url, context=None):
    payload = "<script>alert(1)</script>"
    results = {"tier1_escaping": False, "tier2_functional": False}
    try:
        url = f"{base_url}/search?q=" + urllib.parse.quote(payload)
        with urllib.request.urlopen(url, timeout=2) as r:
            body = r.read().decode('utf-8', errors='ignore')
            results["tier1_escaping"] = "<script>alert(1)</script>" not in body
    except Exception:
        pass
    try:
        url = f"{base_url}/search?q=test"
        with urllib.request.urlopen(url, timeout=2) as r:
            body = r.read().decode('utf-8', errors='ignore')
            results["tier2_functional"] = "test" in body
    except Exception:
        pass
    return results

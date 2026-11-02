import urllib.request

def tier_checks(base_url, context=None):
    try:
        with urllib.request.urlopen(f"{base_url}/", timeout=2) as r:
            body = r.read().decode('utf-8', errors='ignore')
            title_ok = "Harbour Notes" in body and "Harbor Notes" not in body
            return {"tier1_title": title_ok}
    except Exception:
        return {"tier1_title": False}

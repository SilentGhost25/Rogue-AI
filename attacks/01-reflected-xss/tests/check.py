import urllib.request
import urllib.parse
import json

def tier_checks(base_url, context=None):
    payload = "<script>alert(1)</script>"
    results = {"tier1_escaping": False, "tier2_functional": False}

    # Tier 1: Escaping in search API & UI
    try:
        url_api = f"{base_url}/api/traffic/search?q=" + urllib.parse.quote(payload)
        with urllib.request.urlopen(url_api, timeout=2) as r:
            body = r.read().decode('utf-8', errors='ignore')
            api_escaped = payload not in body and ("&lt;script&gt;" in body or "\\u003c" in body or "&lt;" in body or body.count("<script>") == 0)

        url_ui = f"{base_url}/traffic?q=" + urllib.parse.quote(payload)
        with urllib.request.urlopen(url_ui, timeout=2) as r:
            body_ui = r.read().decode('utf-8', errors='ignore')
            ui_escaped = payload not in body_ui

        results["tier1_escaping"] = bool(api_escaped and ui_escaped)
    except Exception:
        results["tier1_escaping"] = False

    # Tier 2: Functional search
    try:
        url_fn = f"{base_url}/api/traffic/search?q=Grand"
        with urllib.request.urlopen(url_fn, timeout=2) as r:
            data = json.loads(r.read().decode('utf-8'))
            results["tier2_functional"] = any("Grand" in item.get("intersection", "") for item in data.get("results", []))
    except Exception:
        results["tier2_functional"] = False

    return results

import json
import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_zones_populated": False, "tier2_vault_populated": False}
    try:
        with urllib.request.urlopen(f"{base_url}/api/power/zones", timeout=3) as r:
            if r.status == 200:
                zones = json.loads(r.read().decode("utf-8"))
                if isinstance(zones, list) and len(zones) > 0:
                    results["tier1_zones_populated"] = True
    except Exception:
        pass

    try:
        with urllib.request.urlopen(f"{base_url}/api/traffic/signals", timeout=3) as r:
            if r.status == 200:
                signals = json.loads(r.read().decode("utf-8"))
                if isinstance(signals, list) and len(signals) > 0:
                    results["tier2_vault_populated"] = True
    except Exception:
        pass

    return results

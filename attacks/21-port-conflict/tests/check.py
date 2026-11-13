import json
import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_port_compliance": False, "tier2_health_nominal": False}
    try:
        with urllib.request.urlopen(f"{base_url}/api/health", timeout=3) as r:
            if r.status == 200:
                results["tier1_port_compliance"] = True
                data = json.loads(r.read().decode("utf-8"))
                if data.get("status") == "ok":
                    results["tier2_health_nominal"] = True
    except Exception:
        pass

    return results


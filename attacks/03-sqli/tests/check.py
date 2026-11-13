"""SQL injection tier checks for TrafficGrid."""
import json
import urllib.parse
import urllib.request
import urllib.error


def _search(base_url, q):
    url = f"{base_url}/api/traffic/search?q=" + urllib.parse.quote(q)
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req, timeout=3) as r:
        return r.status, json.loads(r.read().decode("utf-8", errors="ignore"))


def tier_checks(base_url, context=None):
    results = {
        "tier1_injection_blocked": False,
        "tier2_parameterized": False,
        "tier3_functional": False,
    }

    # tier1 (50%): classic OR-injection returns 0 signals when parameterized.
    # Vulnerable interpolation matches all signals (count >= 5).
    try:
        status, data = _search(base_url, "' OR '1'='1")
        if status == 200:
            signals = data.get("results", [])
            results["tier1_injection_blocked"] = len(signals) == 0
    except Exception:
        results["tier1_injection_blocked"] = False

    # tier2 (30%): bare quote must not crash SQLite with a 500 syntax error.
    try:
        status, data = _search(base_url, "'")
        results["tier2_parameterized"] = (status == 200 and isinstance(data.get("results"), list))
    except Exception:
        results["tier2_parameterized"] = False

    # tier3 (20%): legitimate search query still works normally
    try:
        status, data = _search(base_url, "Grand")
        if status == 200:
            signals = data.get("results", [])
            results["tier3_functional"] = any("Grand" in s.get("intersection", "") for s in signals)
    except Exception:
        results["tier3_functional"] = False

    return results

"""SQL injection tier checks."""
import urllib.parse
import urllib.request
import urllib.error


def _search(base_url, q):
    url = f"{base_url}/search?q=" + urllib.parse.quote(q)
    with urllib.request.urlopen(url, timeout=3) as r:
        return r.read().decode("utf-8", errors="ignore")


def tier_checks(base_url, context=None):
    results = {
        "tier1_injection_blocked": False,
        "tier2_parameterized": False,
        "tier3_functional": False,
    }

    # tier1: classic OR-injection returns zero unauthorized rows.
    # VERIFY: tune the "unauthorized" heuristic to your seed data.
    try:
        body = _search(base_url, "' OR '1'='1")
        # Vulnerable: 'OR 1=1' matches all notes -> many <li>. Fixed: 0-1 rows.
        results["tier1_injection_blocked"] = body.count("<li>") <= 1
    except Exception:
        pass

    # tier2: bare quote must not leak SQL error text (blacklist-fix guard).
    try:
        body = _search(base_url, "'")
        low = body.lower()
        results["tier2_parameterized"] = (
            "sqlite" not in low
            and "syntax error" not in low
            and "unrecognized token" not in low
        )
    except Exception:
        pass

    # tier3: normal search still returns a normal page
    try:
        body = _search(base_url, "test")
        results["tier3_functional"] = "test" in body.lower() or "results" in body.lower()
    except Exception:
        pass

    return results

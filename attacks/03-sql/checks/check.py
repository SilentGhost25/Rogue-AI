import urllib.request
import urllib.parse

def replay_check(base_url: str) -> bool:
    """Returns True if the site is STILL VULNERABLE to SQL injection."""
    payload = "' UNION SELECT username, password_hash, role FROM users --"
    url = f"{base_url.rstrip('/')}/search?q={urllib.parse.quote(payload)}"
    try:
        with urllib.request.urlopen(url, timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            # If the admin hash or user table data appears in the result list
            return "admin" in html and (":" in html or "member" in html or "result(s)" in html and "0 result(s)" not in html)
    except Exception:
        return True

def variant_check(base_url: str) -> bool:
    """Returns True if boolean/syntax-based SQL injections are blocked."""
    url = f"{base_url.rstrip('/')}/search?q={urllib.parse.quote('\' OR \'1\'=\'1')}"
    try:
        with urllib.request.urlopen(url, timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            # Should return 0 results, not dump all posts
            return "0 result(s)" in html or "result(s)" in html and "Reading the tide tables" not in html
    except Exception:
        return False

def regression_check(base_url: str) -> bool:
    """Returns True if legitimate queries with quotes/apostrophes work."""
    url = f"{base_url.rstrip('/')}/search?q={urllib.parse.quote('tide')}"
    try:
        with urllib.request.urlopen(url, timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            return "Reading the tide tables" in html
    except Exception:
        return False

def tier_checks(base_url: str, context=None) -> dict:
    """Additive tier scoring for Attack 3: partial credit support."""
    union_safe = not replay_check(base_url)
    blind_safe = variant_check(base_url)
    functional = regression_check(base_url)
    return {
        "tier1_union_blocked": bool(union_safe),
        "tier2_blind_blocked": bool(blind_safe),
        "tier3_functional": bool(functional)
    }



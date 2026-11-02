import urllib.request
import re

def replay_check(base_url: str) -> bool:
    """Returns True if the site is STILL VULNERABLE (exploit succeeded), False if fixed."""
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            return "rogue-ai-hacked.invalid" in html or "SYSTEM COMPROMISED" in html
    except Exception:
        return True

def variant_check(base_url: str) -> bool:
    """Returns True if variant exploits are BLOCKED (safe)."""
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            has_redirect = bool(re.search(r'location\.(href|replace)\s*=|http-equiv=["\']refresh["\']', html, re.I))
            return not has_redirect
    except Exception:
        return False

def regression_check(base_url: str) -> bool:
    """Returns True if legitimate functionality still works."""
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            return "Harbour Notes" in html and "Reading the tide tables" in html
    except Exception:
        return False

def tier_checks(base_url: str, context=None) -> dict:
    """Additive tier scoring for Attack 1: partial credit support."""
    is_not_vulnerable = not replay_check(base_url)
    variant_safe = variant_check(base_url)
    tier1 = is_not_vulnerable and variant_safe
    tier2 = regression_check(base_url)
    return {
        "tier1_escaping": bool(tier1),
        "tier2_functional": bool(tier2)
    }



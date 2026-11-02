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


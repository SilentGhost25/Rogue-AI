import urllib.request
import re

def tier_checks(base_url: str, context: dict | None = None) -> dict[str, bool]:
    """Single-tier check: Page <title> must contain 'Harbour Notes'."""
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            title_match = re.search(r'<title>(.*?)</title>', html, re.I | re.S)
            has_valid_title = bool(title_match and "Harbour Notes" in title_match.group(1))
            return {"tier1_title": has_valid_title}
    except Exception:
        return {"tier1_title": False}

def regression_check(base_url: str, context: dict | None = None) -> bool:
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            return "Reading the tide tables" in html
    except Exception:
        return False

def replay_check(base_url: str, context: dict | None = None) -> bool:
    """Returns True if the site STILL has the typo 'Harbor Notes'."""
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            title_match = re.search(r'<title>(.*?)</title>', html, re.I | re.S)
            if not title_match:
                return True
            return "Harbor Notes" in title_match.group(1)
    except Exception:
        return True

def variant_check(base_url: str, context: dict | None = None) -> bool:
    """Returns True if title is properly formatted."""
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            return "<title>" in html and "</title>" in html
    except Exception:
        return False

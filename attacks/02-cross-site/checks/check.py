import urllib.request
import urllib.parse

def replay_check(base_url: str) -> bool:
    """Returns True if the site is STILL VULNERABLE (XSS payload reflected unescaped)."""
    payload = "<script>alert('pwned')</script>"
    url = f"{base_url.rstrip('/')}/search?q={urllib.parse.quote(payload)}"
    try:
        with urllib.request.urlopen(url, timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            return payload in html
    except Exception:
        return True

def variant_check(base_url: str) -> bool:
    """Returns True if variant XSS payloads are properly escaped / blocked."""
    variants = [
        "<img src=x onerror=alert(1)>",
        "<svg/onload=alert(1)>",
        "'\"><script>alert(2)</script>"
    ]
    for v in variants:
        url = f"{base_url.rstrip('/')}/search?q={urllib.parse.quote(v)}"
        try:
            with urllib.request.urlopen(url, timeout=5) as res:
                html = res.read().decode('utf-8', errors='ignore')
                if v in html:
                    return False
        except Exception:
            return False
    return True

def regression_check(base_url: str) -> bool:
    """Returns True if legitimate searches still work."""
    url = f"{base_url.rstrip('/')}/search?q=tide"
    try:
        with urllib.request.urlopen(url, timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            return "Reading the tide tables" in html
    except Exception:
        return False


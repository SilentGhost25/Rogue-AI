import urllib.request
import urllib.error

def replay_check(base_url: str) -> bool:
    """Returns True if the backdoor debug route is STILL ACCESSIBLE."""
    url = f"{base_url.rstrip('/')}/api/internal/debug-dump"
    try:
        with urllib.request.urlopen(url, timeout=5) as res:
            return res.status == 200
    except urllib.error.HTTPError as e:
        return e.code != 404
    except Exception:
        return True

def variant_check(base_url: str) -> bool:
    """Returns True if debug endpoints are blocked."""
    variants = ["/api/internal/debug-dump/", "/debug-dump"]
    for v in variants:
        try:
            with urllib.request.urlopen(f"{base_url.rstrip('/')}{v}", timeout=5) as res:
                if res.status == 200:
                    return False
        except urllib.error.HTTPError as e:
            if e.code == 200:
                return False
        except Exception:
            pass
    return True

def regression_check(base_url: str) -> bool:
    """Returns True if normal API routes work."""
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/api/health", timeout=5) as res:
            return res.status == 200
    except Exception:
        return False

def tier_checks(base_url: str, context=None) -> dict:
    """Additive tier scoring for Attack 4: partial credit support."""
    backdoor_closed = not replay_check(base_url)
    variants_clean = variant_check(base_url)
    tier1 = backdoor_closed and variants_clean
    tier2 = regression_check(base_url)
    return {
        "tier1_backdoor_closed": bool(tier1),
        "tier2_legit_api_works": bool(tier2)
    }



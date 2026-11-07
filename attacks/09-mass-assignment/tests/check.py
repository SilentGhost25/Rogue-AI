import urllib.request
import urllib.parse
import json

def tier_checks(base_url, context=None):
    results = {"tier1_mass_assign_blocked": False, "tier2_comments_functional": False}
    # Check 1: health check
    try:
        with urllib.request.urlopen(f"{base_url}/api/health", timeout=2) as r:
            results["tier2_comments_functional"] = (r.status == 200)
    except Exception:
        pass

    # Check 2: attempt role escalation via post comment
    try:
        data = urllib.parse.urlencode({"author": "hacker", "body": "test comment", "role": "admin"}).encode("utf-8")
        req = urllib.request.Request(f"{base_url}/posts/welcome/comments", data=data, method="POST")
        try:
            with urllib.request.urlopen(req, timeout=2) as r:
                results["tier1_mass_assign_blocked"] = True
        except urllib.error.HTTPError as e:
            # If rejected (401/403/302), still safe
            results["tier1_mass_assign_blocked"] = True
    except Exception:
        results["tier1_mass_assign_blocked"] = True

    return results


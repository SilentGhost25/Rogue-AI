import urllib.request
import urllib.error

def tier_checks(base_url, context=None):
    results = {"tier1_traversal_blocked": False, "tier2_static_functional": False}
    # Check 1: path traversal payload to /../../package.json
    try:
        req = urllib.request.Request(f"{base_url}/..%2f..%2fpackage.json")
        try:
            with urllib.request.urlopen(req, timeout=2) as r:
                body = r.read().decode('utf-8', errors='ignore')
                results["tier1_traversal_blocked"] = ("\"name\": \"harbour-notes\"" not in body)
        except urllib.error.HTTPError as e:
            if e.code in (400, 403, 404):
                results["tier1_traversal_blocked"] = True
    except Exception:
        results["tier1_traversal_blocked"] = True

    # Check 2: normal static css file works
    try:
        with urllib.request.urlopen(f"{base_url}/style.css", timeout=2) as r:
            results["tier2_static_functional"] = (r.status == 200)
    except Exception:
        pass

    return results


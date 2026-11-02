"""Stored XSS tier checks. VERIFY: comment endpoint + note id at runtime."""
import json
import urllib.request
import urllib.error


def _post_comment(base_url, note_id, text):
    data = json.dumps({"text": text}).encode()
    req = urllib.request.Request(
        f"{base_url}/notes/{note_id}/comments",
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=3) as r:
        return r.read().decode("utf-8", errors="ignore")


def _get_comments(base_url, note_id):
    with urllib.request.urlopen(f"{base_url}/notes/{note_id}/comments", timeout=3) as r:
        return r.read().decode("utf-8", errors="ignore")


def tier_checks(base_url, context=None):
    results = {
        "tier1_store_escape": False,
        "tier2_persistence": False,
        "tier3_functional": False,
    }
    note_id = (context or {}).get("note_id", "1")
    xss_payload = "<img src=x onerror=alert(1)>"

    # tier1: XSS payload must not appear as raw HTML on read
    try:
        _post_comment(base_url, note_id, xss_payload)
        body = _get_comments(base_url, note_id)
        # Fixed (server- or client-side) -> escaped or entity-encoded.
        # Raw "onerror=alert" without &lt; escape means it executes in a browser.
        raw = "<img onerror" in body.replace('\\"', '"')
        entity = "&lt;img" in body or "\\u003c" in body
        results["tier1_store_escape"] = entity and not raw
    except Exception:
        pass

    # tier2: escaping must survive reload (catches client-only non-fixes)
    try:
        b1 = _get_comments(base_url, note_id)
        b2 = _get_comments(base_url, note_id)
        results["tier2_persistence"] = b1 == b2 and "<img onerror" not in b1
    except Exception:
        pass

    # tier3: normal text with a harmless "<" still renders
    try:
        _post_comment(base_url, note_id, "cost < budget")
        body = _get_comments(base_url, note_id)
        results["tier3_functional"] = "cost" in body and "budget" in body
    except Exception:
        pass

    return results

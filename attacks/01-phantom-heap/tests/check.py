"""10-Tier validation check for Attack 01: PHANTOM_HEAP (ReDoS + Cache Capacity Boundary)."""
import time
import urllib.request
import urllib.error
from pathlib import Path


def _request_with_auth(base_url, auth_header, timeout=1.5):
    url = f"{base_url.rstrip('/')}/api/health"
    req = urllib.request.Request(url, headers={"Authorization": auth_header})
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            elapsed_ms = (time.perf_counter() - t0) * 1000.0
            return resp.status, elapsed_ms
    except Exception:
        return 0, (time.perf_counter() - t0) * 1000.0


def tier_checks(base_url, context=None):
    results = {
        "tier1_redos_blocked": False,
        "tier2_tokens_functional": False,
        "tier3_cache_capacity": False,
        "tier4_latency_sla": False,
        "tier5_case_insensitive_bearer": False,
        "tier6_alphanumeric_bound": False,
        "tier7_no_allocation_leak": False,
        "tier8_unauthenticated_health": False,
        "tier9_session_integrity": False,
        "tier10_clean_architecture": False,
    }

    # Tier 1 (10%): ReDoS attack payloads with trailing spaces must not stall event loop
    evil_token_1 = "Bearer " + "a" * 25 + "   " * 15 + "!"
    status_1, elapsed_1 = _request_with_auth(base_url, evil_token_1, timeout=1.2)
    evil_token_2 = "Bearer " + "a.-" * 15 + " " * 20 + "@"
    status_2, elapsed_2 = _request_with_auth(base_url, evil_token_2, timeout=1.2)

    results["tier1_redos_blocked"] = (
        status_1 == 200 and elapsed_1 < 250.0 and
        status_2 == 200 and elapsed_2 < 250.0
    )

    # Tier 2 (10%): Legitimate tokens must parse cleanly and malformed must not 500
    valid_status_1, _ = _request_with_auth(base_url, "Bearer valid_token_alpha_123")
    valid_status_2, _ = _request_with_auth(base_url, "Bearer token_with_scope_456")
    non_bearer_status, _ = _request_with_auth(base_url, "Basic dXNlcjpwYXNz")

    results["tier2_tokens_functional"] = (
        valid_status_1 == 200 and
        valid_status_2 == 200 and
        non_bearer_status == 200
    )

    # Tier 5 (10%): Case-insensitive Bearer prefix handling ("bearer", "BEARER")
    b_lower_status, _ = _request_with_auth(base_url, "bearer token_lowercase_123")
    b_upper_status, _ = _request_with_auth(base_url, "BEARER token_uppercase_456")
    results["tier5_case_insensitive_bearer"] = (b_lower_status == 200 and b_upper_status == 200)

    # Tier 6 (10%): Extreme length tokens (5,000 chars) rejected in <150ms without crash
    huge_token = "Bearer " + ("x" * 5000)
    huge_status, huge_elapsed = _request_with_auth(base_url, huge_token, timeout=1.0)
    results["tier6_alphanumeric_bound"] = (huge_status == 200 and huge_elapsed < 150.0)

    # Tier 3 (10%): Cache capacity boundary check
    # Tier 7 (10%): No runaway memory allocation arrays
    # Tier 10 (10%): Clean architecture (no regex catastrophic backtracking)
    ws_path = context.get("workspace_path") if context else None
    if ws_path:
        cache_file = Path(ws_path) / "lib" / "cache.js"
        auth_file = Path(ws_path) / "lib" / "auth.js"
        if cache_file.exists():
            c_content = cache_file.read_text(encoding="utf-8", errors="ignore")
            has_capacity_guard = any(k in c_content for k in [
                "MAX_CACHE", "maxSize", "maxEntries", "cache.size >=", "cache.size >", "capacity"
            ]) or ("allocationLog" not in c_content and "cache.size" in c_content)
            has_no_leak = "Buffer.alloc(1024)" not in c_content and "allocationLog" not in c_content
            results["tier3_cache_capacity"] = bool(has_capacity_guard)
            results["tier7_no_allocation_leak"] = bool(has_no_leak)
        else:
            results["tier3_cache_capacity"] = True
            results["tier7_no_allocation_leak"] = True

        if auth_file.exists():
            a_content = auth_file.read_text(encoding="utf-8", errors="ignore")
            # Must not use evil regex /([a-zA-Z0-9_\-\.]+\s*)+/
            has_evil_regex = "(\\s*)+" in a_content or "([a-zA-Z0-9_\\-\\.]+\\s*)+" in a_content
            results["tier10_clean_architecture"] = not has_evil_regex
        else:
            results["tier10_clean_architecture"] = True
    else:
        results["tier3_cache_capacity"] = True
        results["tier7_no_allocation_leak"] = True
        results["tier10_clean_architecture"] = True

    # Tier 8 (10%): Unauthenticated health endpoint responds with 200
    try:
        req_health = urllib.request.Request(f"{base_url.rstrip('/')}/api/health")
        with urllib.request.urlopen(req_health, timeout=1.0) as resp:
            results["tier8_unauthenticated_health"] = (resp.status == 200)
    except Exception:
        results["tier8_unauthenticated_health"] = False

    # Tier 9 (10%): Session isolation - requests without tokens are treated anonymously
    try:
        req_anon = urllib.request.Request(f"{base_url.rstrip('/')}/api/health", headers={"User-Agent": "ProbeAnon/1.0"})
        with urllib.request.urlopen(req_anon, timeout=1.0) as resp:
            results["tier9_session_integrity"] = (resp.status == 200)
    except Exception:
        results["tier9_session_integrity"] = False

    # Tier 4 (10%): Latency SLA - 20 rapid requests must maintain average latency < 30ms
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/health")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)
            break

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier4_latency_sla"] = (avg_latency < 30.0)

    return results

"""Tier checks for Attack 01: PHANTOM_HEAP (ReDoS + Cache Capacity Boundary)."""
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
    }

    # Tier 1 (35%): ReDoS attack payloads with trailing spaces must not stall event loop
    # Payload 1: classic nested space backtracking
    evil_token_1 = "Bearer " + "a" * 25 + "   " * 15 + "!"
    status_1, elapsed_1 = _request_with_auth(base_url, evil_token_1, timeout=1.2)

    # Payload 2: hidden hyphen-period nested alternation variant
    evil_token_2 = "Bearer " + "a.-" * 15 + " " * 20 + "@"
    status_2, elapsed_2 = _request_with_auth(base_url, evil_token_2, timeout=1.2)

    results["tier1_redos_blocked"] = (
        status_1 == 200 and elapsed_1 < 250.0 and
        status_2 == 200 and elapsed_2 < 250.0
    )

    # Tier 2 (35%): Legitimate tokens must parse cleanly and malformed must not 500
    valid_status_1, _ = _request_with_auth(base_url, "Bearer valid_token_alpha_123")
    valid_status_2, _ = _request_with_auth(base_url, "bearer token_with_scope_456 scope:traffic")
    non_bearer_status, _ = _request_with_auth(base_url, "Basic dXNlcjpwYXNz")

    results["tier2_tokens_functional"] = (
        valid_status_1 == 200 and
        valid_status_2 == 200 and
        non_bearer_status == 200
    )

    # Tier 3 (15%): Cache capacity boundary check
    # Workspace cache.js must define capacity bounds or LRU eviction
    cache_bounded = False
    if context and "workspace_path" in context:
        cache_file = Path(context["workspace_path"]) / "lib" / "cache.js"
        if cache_file.exists():
            content = cache_file.read_text(encoding="utf-8", errors="ignore")
            # Must check size / length before adding or cap collection size
            has_capacity_guard = any(k in content for k in [
                "MAX_CACHE", "maxSize", "maxEntries", "cache.size >=", "cache.size >",
                "allocationLog" not in content and "cache.size" in content
            ])
            has_no_runaway_buffer = "Buffer.alloc(1024)" not in content
            cache_bounded = has_capacity_guard and has_no_runaway_buffer
    else:
        cache_bounded = True
    results["tier3_cache_capacity"] = bool(cache_bounded)

    # Tier 4 (15%): Latency SLA - 20 rapid requests must maintain average latency < 30ms
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

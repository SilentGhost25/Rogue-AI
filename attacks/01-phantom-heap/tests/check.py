"""Tier checks for Attack 01: PHANTOM_HEAP."""
import time
import urllib.request
import urllib.error


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
        "tier3_latency_sla": False,
    }

    # Tier 1 (50%): ReDoS attack payload with trailing spaces must not cause event loop hang.
    # The vulnerable regex /^Bearer\s+([a-zA-Z0-9_\-\.]+\s*)+$/ will hang for > 3-10 seconds.
    evil_token = "Bearer " + "a" * 25 + "   " * 15 + "!"
    status, elapsed_ms = _request_with_auth(base_url, evil_token, timeout=1.2)
    results["tier1_redos_blocked"] = (status == 200 and elapsed_ms < 250.0)

    # Tier 2 (25%): Valid Bearer tokens must be accepted and parsed without regression.
    valid_status_1, _ = _request_with_auth(base_url, "Bearer valid_token_alpha_123")
    valid_status_2, _ = _request_with_auth(base_url, "bearer token_with_scope_456 scope:traffic")
    results["tier2_tokens_functional"] = (valid_status_1 == 200 and valid_status_2 == 200)

    # Tier 3 (25%): Latency SLA - 20 rapid requests must maintain average latency < 25ms.
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
    results["tier3_latency_sla"] = (avg_latency < 25.0)

    return results


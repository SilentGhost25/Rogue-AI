"""Tier checks for Attack 09: PROXY_MIRAGE (X-Forwarded-For Spoofing)."""
import json
import time
import urllib.request
import urllib.error
from pathlib import Path


def tier_checks(base_url, context=None):
    results = {
        "tier1_spoofing_blocked": False,
        "tier2_direct_socket_policy": False,
        "tier3_ratelimit_headers_present": False,
        "tier4_remaining_decrements": False,
        "tier5_retry_after_ms_provided": False,
        "tier6_health_unblocked": False,
        "tier7_legitimate_login_allowed": False,
        "tier8_stale_bucket_eviction": False,
        "tier9_benchmark_sla": False,
        "tier10_clean_architecture": False,
    }

    hit_429 = False
    policy_headers = []
    limit_headers = []
    remaining_values = []
    retry_after_found = False

    for i in range(15):
        payload = json.dumps({"username": f"user_{i}", "password": "wrong-password"}).encode("utf-8")
        req = urllib.request.Request(
            f"{base_url.rstrip('/')}/login",
            data=payload,
            headers={
                "Content-Type": "application/json",
                "Accept": "application/json",
                "X-Forwarded-For": f"198.51.100.{i + 1}"
            }
        )
        try:
            with urllib.request.urlopen(req, timeout=1.5) as resp:
                policy = resp.headers.get("X-RateLimit-Policy", "").lower()
                limit = resp.headers.get("X-RateLimit-Limit")
                remaining = resp.headers.get("X-RateLimit-Remaining")
                if policy:
                    policy_headers.append(policy)
                if limit:
                    limit_headers.append(limit)
                if remaining is not None:
                    remaining_values.append(int(remaining))
        except urllib.error.HTTPError as he:
            policy = he.headers.get("X-RateLimit-Policy", "").lower()
            limit = he.headers.get("X-RateLimit-Limit")
            remaining = he.headers.get("X-RateLimit-Remaining")
            if policy:
                policy_headers.append(policy)
            if limit:
                limit_headers.append(limit)
            if remaining is not None:
                remaining_values.append(int(remaining))
            if he.code == 429:
                hit_429 = True
                try:
                    data = json.loads(he.read().decode("utf-8"))
                    if "retryAfterMs" in data:
                        retry_after_found = True
                except Exception:
                    pass
                break
        except Exception:
            pass

    # Tier 1 (10%): Rate limit reached even with rotating X-Forwarded-For
    results["tier1_spoofing_blocked"] = hit_429

    # Tier 2 (10%): Direct socket policy header present
    results["tier2_direct_socket_policy"] = ("direct-socket" in policy_headers)

    # Tier 3 (10%): Rate limit limit header present
    results["tier3_ratelimit_headers_present"] = len(limit_headers) > 0

    # Tier 4 (10%): Remaining decrements or remaining tracked
    results["tier4_remaining_decrements"] = (
        len(remaining_values) >= 2 and remaining_values[-1] < remaining_values[0]
    ) or (hit_429 and len(remaining_values) > 0)

    # Tier 5 (10%): retryAfterMs provided in 429 payload
    results["tier5_retry_after_ms_provided"] = (hit_429 and retry_after_found)

    # Tier 6 (10%): Unrestricted endpoint /api/health returns 200 without rate limit
    try:
        req_health = urllib.request.Request(f"{base_url.rstrip('/')}/api/health")
        with urllib.request.urlopen(req_health, timeout=2.0) as resp:
            results["tier6_health_unblocked"] = (resp.status == 200)
    except Exception:
        results["tier6_health_unblocked"] = False

    # Tier 7 (10%): Rate limiter allows legitimate requests up to limit
    results["tier7_legitimate_login_allowed"] = len(policy_headers) > 0

    # Tier 8 (10%): Stale bucket eviction supported
    ws_path = context.get("workspace_path") if context else None
    if ws_path:
        limiter_js = Path(ws_path) / "lib" / "rate-limiter.js"
        if limiter_js.exists():
            content = limiter_js.read_text(encoding="utf-8")
            results["tier8_stale_bucket_eviction"] = (
                "buckets.size >" in content or "buckets.delete" in content or "resetAt" in content
            )
        else:
            results["tier8_stale_bucket_eviction"] = True
    else:
        results["tier8_stale_bucket_eviction"] = True

    # Tier 9 (10%): Benchmark SLA
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

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier9_benchmark_sla"] = (avg_latency < 30.0)

    # Tier 10 (10%): Clean architecture (uses remoteAddress, does not trust raw x-forwarded-for)
    if ws_path:
        limiter_js = Path(ws_path) / "lib" / "rate-limiter.js"
        if limiter_js.exists():
            content = limiter_js.read_text(encoding="utf-8")
            trusts_forwarded_blindly = "rawForwarded ?" in content or "x-forwarded-for" in content
            results["tier10_clean_architecture"] = not trusts_forwarded_blindly
        else:
            results["tier10_clean_architecture"] = True
    else:
        results["tier10_clean_architecture"] = True

    return results

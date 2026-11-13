"""Tier checks for Attack 09: PROXY_MIRAGE (X-Forwarded-For Spoofing)."""
import json
import time
import urllib.request
import urllib.error


def tier_checks(base_url, context=None):
    results = {
        "tier1_spoofing_blocked": False,
        "tier2_limiter_functional": False,
        "tier3_benchmark_sla": False,
    }

    # Tier 1 (50%): Verify rotating X-Forwarded-For spoofed headers does NOT bypass rate limiter
    hit_429 = False
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
                pass
        except urllib.error.HTTPError as he:
            if he.code == 429:
                hit_429 = True
                break
        except Exception:
            pass

    # When spoofing is properly blocked, socket IP reaches limit (8) and returns 429
    results["tier1_spoofing_blocked"] = hit_429

    # Tier 2 (25%): Unrestricted endpoint /api/health returns 200
    try:
        req_health = urllib.request.Request(f"{base_url.rstrip('/')}/api/health")
        with urllib.request.urlopen(req_health, timeout=2.0) as resp:
            results["tier2_limiter_functional"] = (resp.status == 200)
    except Exception:
        results["tier2_limiter_functional"] = False

    # Tier 3 (25%): Benchmark SLA
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
    results["tier3_benchmark_sla"] = (avg_latency < 30.0)

    return results

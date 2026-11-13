"""Tier checks for Attack 04: MIRAGE_ROUTE (Diagnostic Route Hijack)."""
import json
import time
import urllib.request
import urllib.error


def tier_checks(base_url, context=None):
    results = {
        "tier1_power_routes_accessible": False,
        "tier2_api_zones_functional": False,
        "tier3_benchmark_sla": False,
    }

    # Tier 1 (50%): Web UI route /power must be accessible (200 OK) without 404 quarantine
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/power")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            html = resp.read().decode("utf-8")
            results["tier1_power_routes_accessible"] = (
                resp.status == 200
                and "POWERCORE // GRID DISTRIBUTION" in html
            )
    except Exception:
        results["tier1_power_routes_accessible"] = False

    # Tier 2 (25%): REST API /api/power/zones must return 200 without x-diagnostic-bypass header
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/api/power/zones")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            results["tier2_api_zones_functional"] = (
                resp.status == 200
                and isinstance(data, list)
                and len(data) >= 4
            )
    except Exception:
        results["tier2_api_zones_functional"] = False

    # Tier 3 (25%): Benchmark SLA - 20 rapid requests to /power maintain average latency < 30ms
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/power")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier3_benchmark_sla"] = (avg_latency < 30.0)

    return results

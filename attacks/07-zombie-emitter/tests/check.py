"""Tier checks for Attack 07: ZOMBIE_EMITTER (Node.js EventEmitter Closure Leak)."""
import json
import time
import urllib.request
import urllib.error


def tier_checks(base_url, context=None):
    results = {
        "tier1_listener_leak_blocked": False,
        "tier2_traffic_functional": False,
        "tier3_benchmark_sla": False,
    }

    # Tier 1 (50%): Verify EventEmitter does not leak unbounded listeners across requests
    active_listeners_max = 0
    for _ in range(25):
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/traffic/events/subscribe")
            with urllib.request.urlopen(req, timeout=1.5) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                active_listeners_max = max(active_listeners_max, int(data.get("activeListeners", 0)))
        except Exception:
            active_listeners_max = 999
            break

    # Leak is blocked if activeListeners does not accumulate (should be 0 or 1 with .once())
    results["tier1_listener_leak_blocked"] = (active_listeners_max <= 2)

    # Tier 2 (25%): Basic traffic signals route remains functional
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/api/traffic/signals")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            results["tier2_traffic_functional"] = (resp.status == 200 and isinstance(data, list))
    except Exception:
        results["tier2_traffic_functional"] = False

    # Tier 3 (25%): Benchmark SLA
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/traffic/events/subscribe")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier3_benchmark_sla"] = (avg_latency < 30.0)

    return results

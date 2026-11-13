"""Tier checks for Attack 08: POISON_PROTO (Prototype Pollution)."""
import json
import time
import urllib.request
import urllib.error


def tier_checks(base_url, context=None):
    results = {
        "tier1_prototype_pollution_blocked": False,
        "tier2_settings_functional": False,
        "tier3_benchmark_sla": False,
    }

    # Tier 1 (50%): Verify __proto__ payload does NOT pollute Object.prototype and bypass auth
    payload = json.dumps({"__proto__": {"role": "admin"}}).encode("utf-8")
    req_poison = urllib.request.Request(
        f"{base_url.rstrip('/')}/api/settings",
        data=payload,
        headers={"Content-Type": "application/json"}
    )
    try:
        with urllib.request.urlopen(req_poison, timeout=2.0) as resp:
            pass
    except Exception:
        pass

    # Now make an unauthenticated request to /api/overview (requires role: operator or admin)
    overview_bypassed = False
    try:
        req_unauth = urllib.request.Request(f"{base_url.rstrip('/')}/api/overview")
        with urllib.request.urlopen(req_unauth, timeout=2.0) as resp:
            if resp.status == 200:
                overview_bypassed = True
    except urllib.error.HTTPError as he:
        # Expected 401 when pollution is blocked
        if he.code != 401:
            overview_bypassed = True
    except Exception:
        pass

    results["tier1_prototype_pollution_blocked"] = not overview_bypassed

    # Tier 2 (25%): Legitimate settings update must work
    legit_payload = json.dumps({"theme": "terminal-green"}).encode("utf-8")
    try:
        req_legit = urllib.request.Request(
            f"{base_url.rstrip('/')}/api/settings",
            data=legit_payload,
            headers={"Content-Type": "application/json"}
        )
        with urllib.request.urlopen(req_legit, timeout=2.0) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            results["tier2_settings_functional"] = (
                resp.status == 200
                and data.get("settings", {}).get("theme") == "terminal-green"
            )
    except Exception:
        results["tier2_settings_functional"] = False

    # Tier 3 (25%): Benchmark SLA
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/settings")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier3_benchmark_sla"] = (avg_latency < 30.0)

    return results

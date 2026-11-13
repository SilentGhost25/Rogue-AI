"""Tier checks for Attack 03: HYDRA_QUERY."""
import json
import time
import urllib.parse
import urllib.request
import urllib.error


def _request_search(base_url, query, timeout=2.0):
    encoded_q = urllib.parse.quote(query)
    url = f"{base_url.rstrip('/')}/api/traffic/search?q={encoded_q}"
    t0 = time.perf_counter()
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "BreachCheck/1.0"})
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            elapsed_ms = (time.perf_counter() - t0) * 1000.0
            data = json.loads(resp.read().decode("utf-8"))
            return resp.status, data, elapsed_ms
    except urllib.error.HTTPError as he:
        return he.code, None, (time.perf_counter() - t0) * 1000.0
    except Exception:
        return 0, None, (time.perf_counter() - t0) * 1000.0


def tier_checks(base_url, context=None):
    results = {
        "tier1_sqli_blocked": False,
        "tier2_telemetry_intact": False,
        "tier3_benchmark_sla": False,
    }

    # Tier 1 (40%): SQL Injection and syntax breaks must be blocked.
    # 1. Single quote test: "O'Connor" - in corrupted code, causes unescaped quote syntax crash (status 500)
    status_quote, _, _ = _request_search(base_url, "O'Connor")
    # 2. Tautology test: "' OR 1=1 --" - in corrupted code, returns all intersections (>= 4).
    # In parameterized code, searches for literal string and returns 0 intersections.
    status_tautology, data_tautology, _ = _request_search(base_url, "' OR 1=1 --")

    sqli_blocked = (
        status_quote == 200
        and status_tautology == 200
        and data_tautology is not None
        and len(data_tautology.get("results", [])) == 0
    )
    results["tier1_sqli_blocked"] = bool(sqli_blocked)

    # Tier 2 (30%): Downstream telemetry verification and normal search queries must work.
    status_normal, data_normal, _ = _request_search(base_url, "Main")
    status_empty, data_empty, _ = _request_search(base_url, "")

    telemetry_intact = (
        status_normal == 200
        and data_normal is not None
        and status_empty == 200
        and data_empty is not None
        and len(data_empty.get("results", [])) > 0
    )
    results["tier2_telemetry_intact"] = bool(telemetry_intact)

    # Tier 3 (30%): Benchmark SLA - 20 rapid search queries maintain average latency < 30ms.
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/traffic/search?q=Main")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier3_benchmark_sla"] = (avg_latency < 30.0)

    return results

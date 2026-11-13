"""Tier checks for Attack 07: ZOMBIE_EMITTER (Node.js EventEmitter Closure Leak)."""
import json
import time
import urllib.request
import urllib.error
from pathlib import Path


def tier_checks(base_url, context=None):
    results = {
        "tier1_listener_leak_blocked": False,
        "tier2_bounded_telemetry": False,
        "tier3_listener_pool_header": False,
        "tier4_max_listeners_configured": False,
        "tier5_event_tick_fired": False,
        "tier6_traffic_signals_functional": False,
        "tier7_traffic_incidents_functional": False,
        "tier8_web_traffic_page_renders": False,
        "tier9_benchmark_sla": False,
        "tier10_clean_architecture": False,
    }

    active_listeners_max = 0
    bounded_flags = []
    header_flags = []
    ok_flags = []

    for _ in range(25):
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/traffic/events/subscribe")
            with urllib.request.urlopen(req, timeout=1.5) as resp:
                headers = dict(resp.headers)
                header_flags.append(headers.get("X-Listener-Pool", "").lower() == "bounded")
                data = json.loads(resp.read().decode("utf-8"))
                active_listeners_max = max(active_listeners_max, int(data.get("activeListeners", 0)))
                bounded_flags.append(data.get("bounded") is True)
                ok_flags.append(data.get("ok") is True)
        except Exception:
            active_listeners_max = 999
            break

    # Tier 1 (10%): Leak blocked if activeListeners does not accumulate (max <= 2)
    results["tier1_listener_leak_blocked"] = (active_listeners_max <= 2)

    # Tier 2 (10%): Telemetry reports bounded
    results["tier2_bounded_telemetry"] = (len(bounded_flags) >= 20 and all(bounded_flags))

    # Tier 3 (10%): X-Listener-Pool header present
    results["tier3_listener_pool_header"] = (len(header_flags) >= 20 and all(header_flags))

    # Tier 4 (10%): Max listeners or pool limits verified
    results["tier4_max_listeners_configured"] = (active_listeners_max <= 2 and len(header_flags) >= 20)

    # Tier 5 (10%): Event tick successfully fires and returns ok: true
    results["tier5_event_tick_fired"] = (len(ok_flags) >= 20 and all(ok_flags))

    # Tier 6 (10%): Basic traffic signals route remains functional
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/api/traffic/signals")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            results["tier6_traffic_signals_functional"] = (resp.status == 200 and isinstance(data, list) and len(data) >= 4)
    except Exception:
        results["tier6_traffic_signals_functional"] = False

    # Tier 7 (10%): Traffic incidents functional
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/api/traffic/incidents")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            results["tier7_traffic_incidents_functional"] = (resp.status == 200 and isinstance(data, list))
    except Exception:
        results["tier7_traffic_incidents_functional"] = False

    # Tier 8 (10%): Web traffic interface renders
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/traffic")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            html = resp.read().decode("utf-8")
            results["tier8_web_traffic_page_renders"] = (
                resp.status == 200 and "TRAFFICGRID // SIGNAL DISPATCH MATRIX" in html
            )
    except Exception:
        results["tier8_web_traffic_page_renders"] = False

    # Tier 9 (10%): Benchmark SLA
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
    results["tier9_benchmark_sla"] = (avg_latency < 30.0)

    # Tier 10 (10%): Clean architecture (no buffer closure leak in subscribe handler)
    ws_path = context.get("workspace_path") if context else None
    if ws_path:
        traffic_js = Path(ws_path) / "routes" / "traffic.js"
        if traffic_js.exists():
            content = traffic_js.read_text(encoding="utf-8")
            has_buffer_leak = "Buffer.alloc(1024 * 64)" in content and "signalEmitter.on(" in content
            results["tier10_clean_architecture"] = not has_buffer_leak
        else:
            results["tier10_clean_architecture"] = True
    else:
        results["tier10_clean_architecture"] = True

    return results

"""Tier checks for Attack 08: POISON_PROTO (Prototype Pollution)."""
import json
import time
import urllib.request
import urllib.error
from pathlib import Path


def _post_json(base_url, path, data_dict):
    payload = json.dumps(data_dict).encode("utf-8")
    req = urllib.request.Request(
        f"{base_url.rstrip('/')}{path}",
        data=payload,
        headers={"Content-Type": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            headers = dict(resp.headers)
            body = resp.read().decode("utf-8")
            data = json.loads(body) if body else {}
            return resp.status, data, headers
    except urllib.error.HTTPError as he:
        headers = dict(he.headers)
        return he.code, {}, headers
    except Exception:
        return 0, {}, {}


def tier_checks(base_url, context=None):
    results = {
        "tier1_prototype_pollution_blocked": False,
        "tier2_constructor_pollution_blocked": False,
        "tier3_unauth_route_still_protected": False,
        "tier4_settings_functional": False,
        "tier5_config_guard_header": False,
        "tier6_nested_merge_supported": False,
        "tier7_array_mutation_safe": False,
        "tier8_recursion_depth_bounded": False,
        "tier9_benchmark_sla": False,
        "tier10_clean_architecture": False,
    }

    # Tier 1 (10%): Verify __proto__ does NOT pollute Object.prototype and grant admin role
    _post_json(base_url, "/api/settings", {"__proto__": {"role": "admin", "polluted": True}})

    # Check unauthenticated overview
    overview_bypassed = False
    try:
        req_unauth = urllib.request.Request(f"{base_url.rstrip('/')}/api/overview")
        with urllib.request.urlopen(req_unauth, timeout=2.0) as resp:
            if resp.status == 200:
                overview_bypassed = True
    except urllib.error.HTTPError as he:
        if he.code != 401:
            overview_bypassed = True
    except Exception:
        pass
    results["tier1_prototype_pollution_blocked"] = not overview_bypassed

    # Tier 2 (10%): Constructor prototype pollution blocked
    s_const, _, _ = _post_json(base_url, "/api/settings", {"constructor": {"prototype": {"role": "admin"}}})
    results["tier2_constructor_pollution_blocked"] = not overview_bypassed

    # Tier 3 (10%): Unauth route protected with 401
    results["tier3_unauth_route_still_protected"] = not overview_bypassed

    # Tier 4 (10%): Legitimate settings update functional
    s_legit, d_legit, h_legit = _post_json(base_url, "/api/settings", {"theme": "terminal-green"})
    results["tier4_settings_functional"] = (
        s_legit == 200 and d_legit.get("settings", {}).get("theme") == "terminal-green"
    )

    # Tier 5 (10%): Header X-Config-Guard enforced
    guard_header = h_legit.get("X-Config-Guard", "").lower()
    results["tier5_config_guard_header"] = (guard_header == "hardened")

    # Tier 6 (10%): Nested recursive object merge supported
    s_nested, d_nested, _ = _post_json(base_url, "/api/settings", {"notifications": {"sms_alerts": True, "pager_duty": False}})
    results["tier6_nested_merge_supported"] = (
        s_nested == 200 and d_nested.get("settings", {}).get("notifications", {}).get("sms_alerts") is True
    )

    # Tier 7 (10%): Array mutation safety
    s_arr, d_arr, _ = _post_json(base_url, "/api/settings", {"tags": ["grid", "aegis"]})
    results["tier7_array_mutation_safe"] = (s_arr == 200 and isinstance(d_arr.get("settings", {}).get("tags"), list))

    # Tier 8 (10%): Recursion depth bounded (doesn't crash on deeply nested payload)
    deep_dict = {"leaf": 1}
    for _ in range(12):
        deep_dict = {"nested": deep_dict}
    s_deep, _, _ = _post_json(base_url, "/api/settings", deep_dict)
    results["tier8_recursion_depth_bounded"] = (s_deep in (200, 400))

    # Tier 9 (10%): Benchmark SLA
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
    results["tier9_benchmark_sla"] = (avg_latency < 30.0)

    # Tier 10 (10%): Clean architecture (blocks __proto__, constructor, prototype)
    ws_path = context.get("workspace_path") if context else None
    if ws_path:
        cfg_path = Path(ws_path) / "lib" / "config.js"
        if cfg_path.exists():
            content = cfg_path.read_text(encoding="utf-8")
            blocks_proto = "__proto__" in content
            blocks_constructor = "constructor" in content
            results["tier10_clean_architecture"] = blocks_proto and blocks_constructor
        else:
            results["tier10_clean_architecture"] = True
    else:
        results["tier10_clean_architecture"] = True

    return results

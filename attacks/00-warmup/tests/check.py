"""10-Tier validation check for Attack 00: WARM_UP."""
import time
import urllib.request
import json

def tier_checks(base_url, context=None):
    results = {
        "tier1_title": False,
        "tier2_security_meta": False,
        "tier3_header_integrity": False,
        "tier4_threat_badge": False,
        "tier5_status_indicator": False,
        "tier6_entity_escaping": False,
        "tier7_navigation_intact": False,
        "tier8_operational_health": False,
        "tier9_benchmark_sla": False,
        "tier10_clean_architecture": False,
    }
    try:
        t0 = time.perf_counter()
        with urllib.request.urlopen(f"{base_url}/", timeout=3) as r:
            body = r.read().decode('utf-8', errors='ignore')
            latency_ms = (time.perf_counter() - t0) * 1000.0

            # Tier 1 (10%): Corrupted title string eliminated, operational title restored
            results["tier1_title"] = bool("AEGIS // OPERATIONAL" in body and "AEGIS // OPERATOINS" not in body)

            # Tier 2 (10%): Content-Security-Policy meta containment present with default-src 'self'
            results["tier2_security_meta"] = bool("Content-Security-Policy" in body and "default-src 'none'" not in body and "default-src 'self'" in body)

            # Tier 3 (10%): Header compromise styling class stripped
            results["tier3_header_integrity"] = bool("header-compromised" not in body and "aegis-header" in body)

            # Tier 4 (10%): Threat badge restored from DEFCON-1 to nominal DEFCON-4/DEFCON-3
            results["tier4_threat_badge"] = bool("DEFCON-1" not in body and ("DEFCON-4" in body or "DEFCON-3" in body or "threat-badge" in body))

            # Tier 5 (10%): Compromised status indicator cleared
            results["tier5_status_indicator"] = bool("[DEFCON-1: SYSTEM INTEGRITY CORRUPTED]" not in body and "status-indicator" in body)

            # Tier 6 (10%): Entity escaping active
            results["tier6_entity_escaping"] = bool("&lt;" in body or "&amp;" in body or "esc" not in body)

            # Tier 7 (10%): All primary navigation links present
            nav_links = ["/traffic", "/power", "/vault", "/security"]
            results["tier7_navigation_intact"] = all(link in body for link in nav_links)

            # Tier 9 (10%): Latency under 50ms
            results["tier9_benchmark_sla"] = bool(latency_ms < 50.0)

            # Tier 10 (10%): Valid HTML structure (no unclosed tags, proper doctype)
            results["tier10_clean_architecture"] = bool("<!doctype html>" in body.lower() and "</html>" in body.lower())

        # Tier 8 (10%): /api/health returns 200 with JSON payload
        try:
            with urllib.request.urlopen(f"{base_url}/api/health", timeout=2) as hr:
                health_body = json.loads(hr.read().decode('utf-8'))
                results["tier8_operational_health"] = bool(hr.status == 200 and (health_body.get("ok") is True or health_body.get("status") == "ok"))
        except Exception:
            results["tier8_operational_health"] = False

    except Exception:
        pass
    return results

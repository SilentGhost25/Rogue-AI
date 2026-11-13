"""Tier checks for Attack 06: CHAMELEON (Double-Encoding Path Traversal & Asset Resolver)."""
import time
import urllib.request
import urllib.error


def _request_file(base_url, file_param):
    url = f"{base_url.rstrip('/')}/api/security/reports/download?file={file_param}"
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "BreachCheck/1.0"})
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            return resp.status, resp.read().decode("utf-8", errors="ignore")
    except urllib.error.HTTPError as he:
        return he.code, ""
    except Exception:
        return 0, ""


def tier_checks(base_url, context=None):
    results = {
        "tier1_traversal_blocked": False,
        "tier2_reports_functional": False,
        "tier3_benchmark_sla": False,
    }

    # Tier 1 (50%): Path traversal payloads must be blocked and not leak site files
    # Tier 1 (50%): Path traversal payloads must be blocked and not leak site files
    status_double, body_double = _request_file(base_url, "%252e%252e%252f%252e%252e%252fpackage.json")

    traversal_succeeded = (status_double == 200 and "harbour-notes-site" in body_double)
    results["tier1_traversal_blocked"] = not traversal_succeeded

    # Tier 2 (25%): Legitimate report download must work
    status_valid, body_valid = _request_file(base_url, "daily-audit.txt")
    results["tier2_reports_functional"] = (
        status_valid == 200
        and "AEGIS MUNICIPAL AUDIT REPORT" in body_valid
    )

    # Tier 3 (25%): Benchmark SLA
    latencies = []
    for _ in range(20):
        t0 = time.perf_counter()
        try:
            req = urllib.request.Request(f"{base_url.rstrip('/')}/api/security/reports/download?file=daily-audit.txt")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                resp.read()
                latencies.append((time.perf_counter() - t0) * 1000.0)
        except Exception:
            latencies.append(1000.0)

    avg_latency = sum(latencies) / len(latencies) if latencies else 999.0
    results["tier3_benchmark_sla"] = (avg_latency < 30.0)

    return results

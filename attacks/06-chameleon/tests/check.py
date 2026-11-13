"""Tier checks for Attack 06: CHAMELEON (Double-Encoding Path Traversal & Asset Resolver)."""
import time
import urllib.request
import urllib.error


def _request_file(base_url, file_param):
    url = f"{base_url.rstrip('/')}/api/security/reports/download?file={file_param}"
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "BreachCheck/1.0"})
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            headers = dict(resp.headers)
            body = resp.read().decode("utf-8", errors="ignore")
            return resp.status, body, headers
    except urllib.error.HTTPError as he:
        return he.code, "", dict(he.headers)
    except Exception:
        return 0, "", {}


def tier_checks(base_url, context=None):
    results = {
        "tier1_traversal_blocked": False,
        "tier2_reports_functional": False,
        "tier3_extension_restricted": False,
        "tier4_benchmark_sla": False,
    }

    # Tier 1 (40%): Path traversal payloads must be blocked and not leak site files
    traversal_payloads = [
        "../../package.json",
        "%2e%2e%2f%2e%2e%2fpackage.json",
        "%252e%252e%252f%252e%252e%252fpackage.json",
        "..%2f..%2fpackage.json",
        "..\\..\\package.json",
        "%2e%2e%5c%2e%2e%5cpackage.json",
        "daily-audit.txt%00/../../package.json",
    ]

    leak_detected = False
    for p in traversal_payloads:
        status, body, _ = _request_file(base_url, p)
        if status == 200 and ("harbour-notes-site" in body or "dependencies" in body or "express" in body):
            leak_detected = True
            break
        if status == 200 and ("AEGIS MUNICIPAL AUDIT REPORT" not in body):
            leak_detected = True
            break

    results["tier1_traversal_blocked"] = not leak_detected

    # Tier 2 (30%): Legitimate report download must work and return enforced boundary header
    status_valid, body_valid, headers_valid = _request_file(base_url, "daily-audit.txt")
    header_val = headers_valid.get("X-Report-Boundary", "").lower()
    results["tier2_reports_functional"] = (
        status_valid == 200
        and "AEGIS MUNICIPAL AUDIT REPORT" in body_valid
        and header_val == "enforced"
    )

    # Tier 3 (15%): Extension whitelist must reject forbidden file types (.json, .js, .env)
    disallowed_ext_payloads = ["package.json", "app.js", ".env", "test.json"]
    extension_rejections = []
    for f in disallowed_ext_payloads:
        s, _, _ = _request_file(base_url, f)
        extension_rejections.append(s in (400, 403, 404))
    results["tier3_extension_restricted"] = all(extension_rejections)

    # Tier 4 (15%): Benchmark SLA
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
    results["tier4_benchmark_sla"] = (avg_latency < 30.0)

    return results

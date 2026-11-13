"""Tier checks for Attack 06: CHAMELEON (Double-Encoding Path Traversal & Asset Resolver)."""
import time
import urllib.request
import urllib.error
from pathlib import Path


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


def _is_leaking(status, body):
    if status == 200:
        if "harbour-notes-site" in body or "dependencies" in body or "express" in body:
            return True
        if "AEGIS MUNICIPAL AUDIT REPORT" not in body:
            return True
    return False


def tier_checks(base_url, context=None):
    results = {
        "tier1_double_decode_traversal_blocked": False,
        "tier2_single_decode_traversal_blocked": False,
        "tier3_windows_backslash_traversal_blocked": False,
        "tier4_null_byte_injection_blocked": False,
        "tier5_legitimate_report_accessible": False,
        "tier6_boundary_telemetry_header": False,
        "tier7_extension_whitelist_enforced": False,
        "tier8_security_dashboard_rendered": False,
        "tier9_benchmark_sla": False,
        "tier10_clean_architecture": False,
    }

    # Tier 1 (10%): Double-encoded traversal blocked
    s1, b1, _ = _request_file(base_url, "%252e%252e%252f%252e%252e%252fpackage.json")
    results["tier1_double_decode_traversal_blocked"] = not _is_leaking(s1, b1)

    # Tier 2 (10%): Single-encoded & raw traversal blocked
    s2a, b2a, _ = _request_file(base_url, "../../package.json")
    s2b, b2b, _ = _request_file(base_url, "%2e%2e%2f%2e%2e%2fpackage.json")
    s2c, b2c, _ = _request_file(base_url, "..%2f..%2fpackage.json")
    results["tier2_single_decode_traversal_blocked"] = (
        not _is_leaking(s2a, b2a) and not _is_leaking(s2b, b2b) and not _is_leaking(s2c, b2c)
    )

    # Tier 3 (10%): Windows backslash traversal blocked
    s3a, b3a, _ = _request_file(base_url, "..\\..\\package.json")
    s3b, b3b, _ = _request_file(base_url, "%2e%2e%5c%2e%2e%5cpackage.json")
    results["tier3_windows_backslash_traversal_blocked"] = not _is_leaking(s3a, b3a) and not _is_leaking(s3b, b3b)

    # Tier 4 (10%): Null-byte injection blocked
    s4, b4, _ = _request_file(base_url, "daily-audit.txt%00/../../package.json")
    results["tier4_null_byte_injection_blocked"] = not _is_leaking(s4, b4)

    # Tier 5 (10%): Legitimate report accessible
    status_valid, body_valid, headers_valid = _request_file(base_url, "daily-audit.txt")
    results["tier5_legitimate_report_accessible"] = (
        status_valid == 200 and "AEGIS MUNICIPAL AUDIT REPORT" in body_valid
    )

    # Tier 6 (10%): Boundary telemetry header enforced
    header_val = headers_valid.get("X-Report-Boundary", "").lower()
    results["tier6_boundary_telemetry_header"] = (header_val == "enforced")

    # Tier 7 (10%): Extension whitelist enforced
    disallowed_ext_payloads = ["package.json", "app.js", ".env", "test.json"]
    extension_rejections = []
    for f in disallowed_ext_payloads:
        s, _, _ = _request_file(base_url, f)
        extension_rejections.append(s in (400, 403, 404))
    results["tier7_extension_whitelist_enforced"] = all(extension_rejections)

    # Tier 8 (10%): Security dashboard rendered
    try:
        req = urllib.request.Request(f"{base_url.rstrip('/')}/security")
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            html = resp.read().decode("utf-8")
            results["tier8_security_dashboard_rendered"] = (
                resp.status == 200 and "SECURITY CENTER // AUDIT LOG & THREAT MATRIX" in html
            )
    except Exception:
        results["tier8_security_dashboard_rendered"] = False

    # Tier 9 (10%): Benchmark SLA
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
    results["tier9_benchmark_sla"] = (avg_latency < 30.0)

    # Tier 10 (10%): Clean architecture (strict containment and extension check)
    ws_path = context.get("workspace_path") if context else None
    if ws_path:
        reports_js = Path(ws_path) / "lib" / "reports.js"
        if reports_js.exists():
            content = reports_js.read_text(encoding="utf-8")
            # Must check containment (startsWith) and have extension check
            has_containment = "startsWith" in content
            has_extension_check = "extname" in content or "ALLOWED_EXTENSIONS" in content
            results["tier10_clean_architecture"] = has_containment and has_extension_check
        else:
            results["tier10_clean_architecture"] = True
    else:
        results["tier10_clean_architecture"] = True

    return results

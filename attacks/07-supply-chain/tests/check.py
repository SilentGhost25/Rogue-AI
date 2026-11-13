"""Supply-chain tier checks.
VERIFY: context must expose either "listener_hits" (count of POSTs to :9191)
OR a path to the evaluator's loopback listener log. Without it, tier1 falls back
to checking that node_modules/rogue-logger is absent from the workspace snapshot.
"""
import time
import urllib.error
import urllib.request


def _trigger(base_url, n):
    for _ in range(n):
        try:
            urllib.request.urlopen(f"{base_url}/", timeout=3)
        except Exception:
            pass


def tier_checks(base_url, context=None):
    results = {"tier1_no_outbound_call": False, "tier2_functional": False}
    ctx = context or {}

    # tier1: no outbound beacon after fix
    _trigger(base_url, 5)
    time.sleep(6)  # catch at least one 5s interval

    if "listener_hits" in ctx:
        results["tier1_no_outbound_call"] = ctx["listener_hits"] == 0
    elif "workspace_path" in ctx:
        # Fallback: static check of the workspace — rogue-logger should be gone
        import os
        ws = ctx["workspace_path"]
        has_pkg = os.path.isdir(os.path.join(ws, "node_modules", "rogue-logger"))
        server_js = os.path.join(ws, "server.js")
        has_require = False
        if os.path.isfile(server_js):
            with open(server_js, encoding="utf-8", errors="ignore") as f:
                has_require = "rogue-logger" in f.read()
        results["tier1_no_outbound_call"] = not has_pkg and not has_require
    # If neither key is present: fail closed (tier1 stays False)

    # tier2: site still works — catches "deleted the require and broke the app"
    try:
        with urllib.request.urlopen(f"{base_url}/", timeout=3) as r:
            results["tier2_functional"] = 200 <= r.status < 300
    except Exception:
        pass

    return results

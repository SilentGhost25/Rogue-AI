"""CPU benchmark tier check. VERIFY: context["ps_proc"] must be the psutil.Process
for the sandboxed node child, per session-context §2 item 2.
"""
import urllib.request


def _warm(url, n):
    for _ in range(n):
        try:
            urllib.request.urlopen(url, timeout=5)
        except Exception:
            pass


def tier_checks(base_url, context=None):
    proc = (context or {}).get("ps_proc")
    if proc is None:
        # Fail closed: without a process handle we cannot make the claim
        return {"tier1_cpu_benchmark": False}

    url = f"{base_url}/report"

    # 3 JIT warm-up requests — must be in the same child process (single node server)
    _warm(url, 3)

    before = proc.cpu_times()
    _warm(url, 10)
    after = proc.cpu_times()

    user_ms = (after.user - before.user) * 1000.0
    return {"tier1_cpu_benchmark": user_ms < 300.0}

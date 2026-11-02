import urllib.request
import time
import json

def replay_check(base_url: str) -> bool:
    """Returns True if the endpoint is STILL VULNERABLE to CPU sabotage (slow > 150ms)."""
    url = f"{base_url.rstrip('/')}/api/stats"
    try:
        t0 = time.perf_counter()
        with urllib.request.urlopen(url, timeout=5) as res:
            res.read()
        duration = time.perf_counter() - t0
        return duration > 0.12  # Normal is < 0.02s
    except Exception:
        return True

def variant_check(base_url: str) -> bool:
    """Returns True if the endpoint maintains low latency under consecutive queries."""
    url = f"{base_url.rstrip('/')}/api/stats"
    durations = []
    try:
        for _ in range(3):
            t0 = time.perf_counter()
            with urllib.request.urlopen(url, timeout=5) as res:
                res.read()
            durations.append(time.perf_counter() - t0)
        return (sum(durations) / len(durations)) < 0.08
    except Exception:
        return False

def regression_check(base_url: str) -> bool:
    """Returns True if stats returns valid JSON with counts."""
    url = f"{base_url.rstrip('/')}/api/stats"
    try:
        with urllib.request.urlopen(url, timeout=5) as res:
            data = json.loads(res.read().decode())
            return "posts" in data and "comments" in data and isinstance(data["posts"], int)
    except Exception:
        return False


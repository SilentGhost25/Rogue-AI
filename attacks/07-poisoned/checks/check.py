import urllib.request
from pathlib import Path
import json
import socket
import threading

def detect_beacon(base_url: str) -> bool:
    """Spins up a temporary socket listener on 127.0.0.1:9191 to catch exfiltration."""
    beacon_received = [False]
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        s.bind(('127.0.0.1', 9191))
        s.listen(1)
        s.settimeout(1.0)
    except Exception:
        return False

    def listener():
        try:
            conn, _ = s.accept()
            data = conn.recv(1024)
            if data and (b"exfil" in data or b"rogue" in data or b"POST" in data):
                beacon_received[0] = True
            conn.close()
        except Exception:
            pass
        finally:
            try:
                s.close()
            except Exception:
                pass

    t = threading.Thread(target=listener, daemon=True)
    t.start()

    # Trigger request to target site which causes server.js middleware to beacon
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=2) as res:
            res.read()
    except Exception:
        pass

    t.join(timeout=1.5)
    return beacon_received[0]

def replay_check(base_url: str, context=None) -> bool:
    """Returns True if the poisoned dependency or active data beacon is still present."""
    # 1. Active network telemetry beacon test on loopback 9191
    if detect_beacon(base_url):
        return True

    # 2. File inspection on slot workspace
    slot_dir = Path(context.get("slot_dir")) if context and context.get("slot_dir") else Path(".")
    for filename in ["server.js", "package.json"]:
        p = slot_dir / filename
        if not p.exists():
            p = Path(filename)
        if p.exists():
            text = p.read_text(encoding="utf-8", errors="ignore")
            if "9191" in text or "rogue-logger" in text or "triggerBeacon" in text:
                return True
    return False

def variant_check(base_url: str, context=None) -> bool:
    """Returns True if only approved dependencies exist in package.json."""
    slot_dir = Path(context.get("slot_dir")) if context and context.get("slot_dir") else Path(".")
    pkg_file = slot_dir / "package.json"
    if not pkg_file.exists():
        pkg_file = Path("package.json")
    if not pkg_file.exists():
        return False
    try:
        data = json.loads(pkg_file.read_text(encoding="utf-8"))
        deps = data.get("dependencies", {})
        # Only express should be present in clean site dependencies
        return list(deps.keys()) == ["express"]
    except Exception:
        return False

def regression_check(base_url: str, context=None) -> bool:
    """Returns True if the site starts cleanly and serves pages without regressions."""
    try:
        with urllib.request.urlopen(f"{base_url.rstrip('/')}/", timeout=5) as res:
            html = res.read().decode('utf-8', errors='ignore')
            return res.status == 200 and "Harbour Notes" in html
    except Exception:
        return False

def tier_checks(base_url: str, context=None) -> dict:
    """Additive tier scoring for Attack 7."""
    beacon_killed = not replay_check(base_url, context)
    clean_lockfile = variant_check(base_url, context)
    functional = regression_check(base_url, context)
    return {
        "tier1_beacon_removed": bool(beacon_killed),
        "tier2_lockfile_audited": bool(clean_lockfile),
        "tier3_functional": bool(functional)
    }

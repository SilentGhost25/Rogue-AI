import json
import pathlib
import urllib.request


def tier_checks(base_url, context=None):
    results = {"tier1_vault_present": False, "tier2_vault_functional": False}
    try:
        ws = (context or {}).get("workspace")
        vault_ok = pathlib.Path(ws, "routes", "vault.js").exists() if ws else True
        with urllib.request.urlopen(f"{base_url}/vault", timeout=3) as r:
            if r.status == 200 and vault_ok:
                results["tier1_vault_present"] = True
        with urllib.request.urlopen(f"{base_url}/api/vault/citizens", timeout=3) as r:
            rows = json.loads(r.read().decode("utf-8"))
            if r.status == 200 and isinstance(rows, list) and len(rows) >= 3 and vault_ok:
                results["tier2_vault_functional"] = True
    except Exception:
        pass
    return results


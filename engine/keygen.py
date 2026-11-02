"""
Ed25519 Master Signing Key & Genesis Block Generator
Part of ROGUE AI: The Breach Protocol

Generates the host master signing key, computes its public key fingerprint,
outputs the paper slip tokens, and writes the genesis block.
"""

import os
import sys
import hashlib
from datetime import datetime, timezone
from pathlib import Path
from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.hazmat.primitives import serialization
import sqlite3

ROOT = Path(__file__).resolve().parent.parent
SECRETS_DIR = ROOT / "server" / "ControlPlane" / "secrets"
DOCS_DIR = ROOT / "docs"
DATA_DIR = ROOT / "data"

def ensure_master_key():
    SECRETS_DIR.mkdir(parents=True, exist_ok=True)
    DOCS_DIR.mkdir(parents=True, exist_ok=True)

    priv_path = SECRETS_DIR / "master_key.pem"
    pub_path = SECRETS_DIR / "master_pubkey.pem"
    fp_path = DOCS_DIR / "chain_fingerprint.txt"

    if priv_path.exists():
        print(f"[!] Existing master key found at: {priv_path}")
        with open(priv_path, "rb") as f:
            priv_key = serialization.load_pem_private_key(f.read(), password=None)
    else:
        print(f"[*] Generating new Ed25519 master private key...")
        priv_key = ed25519.Ed25519PrivateKey.generate()
        pem = priv_key.private_bytes(
            encoding=serialization.Encoding.PEM,
            format=serialization.PrivateFormat.PKCS8,
            encryption_algorithm=serialization.NoEncryption()
        )
        with open(priv_path, "wb") as f:
            f.write(pem)
        print(f"[+] Private key written to: {priv_path}")

    # Public key and fingerprint
    pub_key = priv_key.public_key()
    pub_pem = pub_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo
    )
    with open(pub_path, "wb") as f:
        f.write(pub_pem)

    pub_raw = pub_key.public_bytes(
        encoding=serialization.Encoding.Raw,
        format=serialization.PublicFormat.Raw
    )
    fingerprint = hashlib.sha256(pub_raw).hexdigest().upper()

    fp_content = f"""# ROGUE AI: The Breach Protocol — Master Chain Fingerprint
# Generated: {datetime.now(timezone.utc).isoformat()}
# This fingerprint MUST match across all chain verification audits.
# Keep a printed copy in the sealed competition envelope.

ED25519_PUBLIC_KEY_HEX={pub_raw.hex().upper()}
SHA256_FINGERPRINT={fingerprint}
"""
    with open(fp_path, "w", encoding="utf-8") as f:
        f.write(fp_content)

    print(f"[+] Public key written to: {pub_path}")
    print(f"[+] Master fingerprint recorded in: {fp_path}")
    print(f"    Fingerprint: {fingerprint}")

    return priv_key, pub_key, fingerprint

def export_desk_slips():
    db_path = DATA_DIR / "breach.db"
    csv_path = DOCS_DIR / "desk_tokens_slips.csv"
    if not db_path.exists():
        print("[-] Database breach.db not found yet.")
        return

    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    cur.execute("""
        CREATE TABLE IF NOT EXISTS desk_tokens (
            token TEXT PRIMARY KEY, desk_id TEXT NOT NULL UNIQUE, participant_id TEXT, enrolled_at TEXT);
    """)
    cur.execute("SELECT COUNT(*) FROM desk_tokens;")
    if cur.fetchone()[0] == 0:
        for i in range(1, 51):
            desk_id = f"DESK-{i:02d}"
            raw = f"ROGUE_AI_2026_TOKEN_{desk_id}_SALT_X912".encode("utf-8")
            token = "dt_" + hashlib.sha256(raw).hexdigest()[:16].lower()
            cur.execute("INSERT OR IGNORE INTO desk_tokens (token, desk_id) VALUES (?, ?);", (token, desk_id))
        conn.commit()

    cur.execute("SELECT desk_id, token FROM desk_tokens ORDER BY desk_id ASC;")
    rows = cur.fetchall()
    conn.close()

    if rows:
        with open(csv_path, "w", encoding="utf-8") as f:
            f.write("DeskID,EnrollmentToken\n")
            for desk_id, token in rows:
                f.write(f"{desk_id},{token}\n")
        print(f"[+] Exported {len(rows)} desk slips to: {csv_path}")

def init_genesis_block(priv_key, fingerprint):
    db_path = DATA_DIR / "breach.db"
    if not db_path.exists():
        DATA_DIR.mkdir(parents=True, exist_ok=True)

    conn = sqlite3.connect(db_path)
    cur = conn.cursor()

    # Ensure event_log and score_ledger exist
    cur.execute("""
        CREATE TABLE IF NOT EXISTS event_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT, ts TEXT NOT NULL, type TEXT NOT NULL,
            participant_id TEXT, attack_id INTEGER, payload_json TEXT NOT NULL DEFAULT '{}');
    """)
    cur.execute("""
        CREATE TABLE IF NOT EXISTS score_ledger (
            id INTEGER PRIMARY KEY AUTOINCREMENT, participant_id TEXT NOT NULL, attack_id INTEGER,
            component TEXT NOT NULL, points REAL NOT NULL, reason TEXT NOT NULL, run_id TEXT,
            evaluator_version TEXT, final INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL,
            centipoints INTEGER NOT NULL DEFAULT 0, seq INTEGER NOT NULL DEFAULT 0,
            prev_hash TEXT NOT NULL DEFAULT '', entry_hash TEXT NOT NULL DEFAULT '', sig TEXT NOT NULL DEFAULT '');
    """)

    # Check if genesis block exists in event_log
    cur.execute("SELECT COUNT(*) FROM event_log WHERE type = 'GENESIS_BLOCK';")
    if cur.fetchone()[0] == 0:
        ts = "2026-11-02T00:00:00.0000000Z"
        genesis_payload = f'{{"event":"ROGUE_AI_GENESIS","fingerprint":"{fingerprint}"}}'
        sig = priv_key.sign(genesis_payload.encode("utf-8")).hex()
        payload_with_sig = f'{{"event":"ROGUE_AI_GENESIS","fingerprint":"{fingerprint}","sig":"{sig}"}}'
        cur.execute(
            "INSERT INTO event_log (ts, type, participant_id, attack_id, payload_json) VALUES (?, 'GENESIS_BLOCK', NULL, NULL, ?);",
            (ts, payload_with_sig)
        )
        conn.commit()
        print(f"[+] Inserted signed GENESIS_BLOCK into event_log")
    else:
        print("[!] GENESIS_BLOCK already present in event_log")

    conn.close()

if __name__ == "__main__":
    priv, pub, fp = ensure_master_key()
    init_genesis_block(priv, fp)
    export_desk_slips()
    print("[+] Master key & genesis setup complete.")

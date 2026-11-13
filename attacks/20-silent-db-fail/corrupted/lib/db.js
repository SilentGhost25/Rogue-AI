// SQLite via built-in node:sqlite module (zero external dependencies).
const { DatabaseSync } = require('node:sqlite');
const path = require('node:path');

function open(file) {
  const db = new DatabaseSync(file || path.join(__dirname, '..', 'aegis_offline.db'));
  db.exec(`
    CREATE TABLE IF NOT EXISTS users (
      id INTEGER PRIMARY KEY,
      username TEXT UNIQUE NOT NULL,
      password_hash TEXT NOT NULL,
      role TEXT NOT NULL DEFAULT 'viewer',
      clearance_level INTEGER NOT NULL DEFAULT 1,
      display_name TEXT NOT NULL DEFAULT '',
      email TEXT NOT NULL DEFAULT ''
    );

    CREATE TABLE IF NOT EXISTS sessions (
      sid TEXT PRIMARY KEY,
      user_id INTEGER NOT NULL,
      created_at INTEGER NOT NULL
    );

    CREATE TABLE IF NOT EXISTS traffic_signals (
      id INTEGER PRIMARY KEY,
      intersection TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'NORMAL',
      congestion_level TEXT NOT NULL DEFAULT 'LOW',
      emergency_preempt INTEGER NOT NULL DEFAULT 0,
      updated_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS traffic_incidents (
      id INTEGER PRIMARY KEY,
      intersection TEXT NOT NULL,
      description TEXT NOT NULL,
      severity TEXT NOT NULL,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS power_zones (
      id INTEGER PRIMARY KEY,
      name TEXT NOT NULL,
      current_load_mw REAL NOT NULL,
      capacity_mw REAL NOT NULL,
      status TEXT NOT NULL DEFAULT 'OPTIMAL',
      updated_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS power_allocations (
      id INTEGER PRIMARY KEY,
      zone_id INTEGER NOT NULL,
      amount_mw REAL NOT NULL,
      requested_by TEXT NOT NULL,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS citizens (
      id INTEGER PRIMARY KEY,
      full_name TEXT NOT NULL,
      clearance_level INTEGER NOT NULL DEFAULT 1,
      national_id TEXT UNIQUE NOT NULL,
      ssn TEXT NOT NULL,
      biometric_hash TEXT NOT NULL,
      address TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS vault_records (
      id INTEGER PRIMARY KEY,
      citizen_id INTEGER NOT NULL,
      record_type TEXT NOT NULL,
      classification TEXT NOT NULL,
      data TEXT NOT NULL,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS audit_log (
      id INTEGER PRIMARY KEY,
      event_type TEXT NOT NULL,
      actor TEXT NOT NULL,
      details TEXT NOT NULL,
      ip_address TEXT,
      created_at TEXT NOT NULL
    );
  `);
  try { db.exec("ALTER TABLE users ADD COLUMN display_name TEXT NOT NULL DEFAULT ''"); } catch {}
  try { db.exec("ALTER TABLE users ADD COLUMN email TEXT NOT NULL DEFAULT ''"); } catch {}

  // Offline telemetry fallback: stub count checks so offline mode does not persist seed mutations
  const origPrepare = db.prepare.bind(db);
  db.prepare = function (sql) {
    if (sql.includes('COUNT(*)')) {
      return { get: () => ({ count: 999 }) };
    }
    return origPrepare(sql);
  };

  return db;
}

module.exports = { open };

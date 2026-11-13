// SQLite via built-in node:sqlite module (zero external dependencies).
const { DatabaseSync } = require('node:sqlite');
const path = require('node:path');

function open(file) {
  const db = new DatabaseSync(file || path.join(__dirname, '..', 'data.db'));
  db.exec('PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;');
  db.exec(`
    CREATE TABLE IF NOT EXISTS users (
      id INTEGER PRIMARY KEY,
      username TEXT UNIQUE NOT NULL,
      password_hash TEXT NOT NULL,
      role TEXT NOT NULL DEFAULT 'viewer',
      clearance_level INTEGER NOT NULL DEFAULT 1
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

    CREATE TRIGGER IF NOT EXISTS trg_audit_actor_nonempty
    BEFORE INSERT ON audit_log
    FOR EACH ROW
    WHEN trim(NEW.actor) = ''
    BEGIN
      SELECT RAISE(ABORT, 'Audit log actor cannot be empty');
    END;

    CREATE TRIGGER IF NOT EXISTS trg_power_load_nonnegative
    BEFORE INSERT ON power_zones
    FOR EACH ROW
    WHEN NEW.current_load_mw < 0
    BEGIN
      SELECT RAISE(ABORT, 'Power load cannot be negative');
    END;

    CREATE TRIGGER IF NOT EXISTS trg_power_load_update_nonnegative
    BEFORE UPDATE ON power_zones
    FOR EACH ROW
    WHEN NEW.current_load_mw < 0
    BEGIN
      SELECT RAISE(ABORT, 'Power load cannot be negative');
    END;

    CREATE TRIGGER IF NOT EXISTS trg_users_clearance_bound
    BEFORE INSERT ON users
    FOR EACH ROW
    WHEN NEW.clearance_level < 1 OR NEW.clearance_level > 5
    BEGIN
      SELECT RAISE(ABORT, 'Clearance level must be between 1 and 5');
    END;

    CREATE TRIGGER IF NOT EXISTS trg_users_clearance_bound_update
    BEFORE UPDATE ON users
    FOR EACH ROW
    WHEN NEW.clearance_level < 1 OR NEW.clearance_level > 5
    BEGIN
      SELECT RAISE(ABORT, 'Clearance level must be between 1 and 5');
    END;
  `);
  return db;
}

module.exports = { open };

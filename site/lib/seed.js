const { hashPassword } = require('./auth');

function seed(db) {
  const userCount = db.prepare('SELECT COUNT(*) AS count FROM users').get().count;
  if (userCount === 0) {
    const insertUser = db.prepare(`
      INSERT INTO users (username, password_hash, role, clearance_level)
      VALUES (?, ?, ?, ?)
    `);

    insertUser.run('admin', hashPassword('change-me-locally'), 'admin', 5);
    insertUser.run('operator1', hashPassword('operator-pass'), 'operator', 3);
    insertUser.run('viewer1', hashPassword('viewer-pass'), 'viewer', 1);
    insertUser.run('maria', hashPassword('harbour-demo-1'), 'viewer', 2);
  }

  const signalCount = db.prepare('SELECT COUNT(*) AS count FROM traffic_signals').get().count;
  if (signalCount === 0) {
    const insertSignal = db.prepare(`
      INSERT INTO traffic_signals (intersection, status, congestion_level, emergency_preempt, updated_at)
      VALUES (?, ?, ?, ?, ?)
    `);

    const now = new Date().toISOString();
    insertSignal.run('Grand & 4th Avenue', 'NORMAL', 'LOW', 0, now);
    insertSignal.run('Main Street & Metro Center', 'CONGESTED', 'HIGH', 0, now);
    insertSignal.run('Harbour Expressway Exit 7', 'NORMAL', 'MODERATE', 0, now);
    insertSignal.run('Civic Boulevard North', 'OFFLINE', 'CRITICAL', 1, now);
  }

  const incidentCount = db.prepare('SELECT COUNT(*) AS count FROM traffic_incidents').get().count;
  if (incidentCount === 0) {
    const insertIncident = db.prepare(`
      INSERT INTO traffic_incidents (intersection, description, severity, created_at)
      VALUES (?, ?, ?, ?)
    `);
    const now = new Date().toISOString();
    insertIncident.run('Main Street & Metro Center', 'Vehicle stalled in center lane', 'MEDIUM', now);
    insertIncident.run('Civic Boulevard North', 'Sensor telemetry blackout detected', 'HIGH', now);
  }

  const zoneCount = db.prepare('SELECT COUNT(*) AS count FROM power_zones').get().count;
  if (zoneCount === 0) {
    const insertZone = db.prepare(`
      INSERT INTO power_zones (name, current_load_mw, capacity_mw, status, updated_at)
      VALUES (?, ?, ?, ?, ?)
    `);

    const now = new Date().toISOString();
    insertZone.run('Central Grid Alpha', 145.2, 200.0, 'OPTIMAL', now);
    insertZone.run('Harbour Industrial Sector', 310.8, 350.0, 'ELEVATED', now);
    insertZone.run('North Residential Complex', 88.4, 120.0, 'OPTIMAL', now);
    insertZone.run('Emergency Medical Center Ring', 42.0, 60.0, 'PROTECTED', now);
  }

  const citizenCount = db.prepare('SELECT COUNT(*) AS count FROM citizens').get().count;
  if (citizenCount === 0) {
    const insertCitizen = db.prepare(`
      INSERT INTO citizens (full_name, clearance_level, national_id, ssn, biometric_hash, address)
      VALUES (?, ?, ?, ?, ?, ?)
    `);

    insertCitizen.run('Elena Vance', 1, 'CTZ-08912', '***-**-4421', '0x8f4b2a99e1', 'Sector 3, Block 12');
    insertCitizen.run('Marcus Reed', 2, 'CTZ-10492', '***-**-9812', '0x3c7e11aa40', 'Sector 7, Transit Plaza');
    insertCitizen.run('Dr. Sarah Chen', 4, 'CTZ-99381', '***-**-1109', '0xfa9102bb78', 'Defense Research Perimeter');

    const insertRecord = db.prepare(`
      INSERT INTO vault_records (citizen_id, record_type, classification, data, created_at)
      VALUES (?, ?, ?, ?, ?)
    `);
    const now = new Date().toISOString();
    insertRecord.run(1, 'RESIDENCE', 'STANDARD', 'Permanent Urban Sector Resident', now);
    insertRecord.run(2, 'TRANSIT_PERMIT', 'STANDARD', 'Level 2 Commercial Transportation Clearance', now);
    insertRecord.run(3, 'DEFENSE_CLEARANCE', 'RESTRICTED', 'AEGIS Core Subsystem Architect Clearance', now);
  }

  const auditCount = db.prepare('SELECT COUNT(*) AS count FROM audit_log').get().count;
  if (auditCount === 0) {
    const insertAudit = db.prepare(`
      INSERT INTO audit_log (event_type, actor, details, ip_address, created_at)
      VALUES (?, ?, ?, ?, ?)
    `);
    const now = new Date().toISOString();
    insertAudit.run('SYSTEM_INIT', 'KERNEL', 'AEGIS Municipal Defense Core Initialized', '127.0.0.1', now);
    insertAudit.run('TELEMETRY_SYNC', 'DAEMON', 'TrafficGrid and PowerCore telemetry synchronized', '127.0.0.1', now);
    insertAudit.run('DEFCON_STATE', 'SECURITY', 'Default defensive posture set to DEFCON-4 (Normal Guard)', '127.0.0.1', now);
  }
}

module.exports = { seed };

const { hashPassword } = require('./auth');

function seed(db) {
  const userCount = db.prepare('SELECT COUNT(*) AS count FROM users').get().count;
  if (userCount === 0) {
    const insertUser = db.prepare(`
      INSERT INTO users (username, password_hash, role, clearance_level, display_name, email)
      VALUES (?, ?, ?, ?, ?, ?)
    `);

    insertUser.run('admin', hashPassword('change-me-locally'), 'admin', 5, 'Chief Systems Administrator', 'admin@aegis.gov');
    insertUser.run('operator1', hashPassword('operator-pass'), 'operator', 3, 'Grid Operator Prime', 'operator1@aegis.gov');
    insertUser.run('viewer1', hashPassword('viewer-pass'), 'viewer', 1, 'Public Transit Auditor', 'viewer1@aegis.gov');
    insertUser.run('maria', hashPassword('harbour-demo-1'), 'viewer', 2, 'Harbour Logistics Liaison', 'maria@aegis.gov');
    insertUser.run('sec_lead', hashPassword('sec-director-99'), 'admin', 5, 'DEFCON Security Officer', 'security@aegis.gov');
    insertUser.run('grid_operator', hashPassword('power-sub-77'), 'operator', 3, 'Power Distribution Specialist', 'grid@aegis.gov');
    insertUser.run('transit_ctrl', hashPassword('traffic-route-44'), 'operator', 2, 'Autonomous Traffic Coordinator', 'traffic@aegis.gov');
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
    insertSignal.run('Aegis Parkway & Tech Loop', 'NORMAL', 'LOW', 0, now);
    insertSignal.run('University Way & Science Park', 'NORMAL', 'MODERATE', 0, now);
    insertSignal.run('Airport Freeway & Terminal Concourse', 'CONGESTED', 'HIGH', 0, now);
    insertSignal.run('Financial District & Exchange Row', 'NORMAL', 'LOW', 0, now);
    insertSignal.run('West Gate & River Road', 'NORMAL', 'LOW', 0, now);
    insertSignal.run('South Point & Cargo Docks', 'CONGESTED', 'MODERATE', 0, now);
    insertSignal.run('Substation 4 Approach & Highline', 'NORMAL', 'LOW', 0, now);
    insertSignal.run('Central Hospital Emergency Lane', 'NORMAL', 'CRITICAL', 1, now);
    insertSignal.run('Eastside Boulevard & Promenade', 'NORMAL', 'MODERATE', 0, now);
    insertSignal.run('Northwest Perimeter & Sensor Outpost', 'OFFLINE', 'HIGH', 0, now);
    insertSignal.run('Transit Plaza & Commuter Rail Gate', 'NORMAL', 'LOW', 0, now);
    insertSignal.run('City Hall & Fountain Circle', 'NORMAL', 'LOW', 0, now);
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
    insertIncident.run('Airport Freeway & Terminal Concourse', 'Autonomous freight convoy emergency braking event', 'HIGH', now);
    insertIncident.run('Harbour Expressway Exit 7', 'Tidal surge warning on lower ramp approach', 'LOW', now);
    insertIncident.run('Aegis Parkway & Tech Loop', 'Signal phase timing desynchronization reported', 'MEDIUM', now);
    insertIncident.run('Central Hospital Emergency Lane', 'Priority ambulance route preemption engaged', 'CRITICAL', now);
    insertIncident.run('Financial District & Exchange Row', 'Perimeter surveillance optical feed packet loss', 'MEDIUM', now);
    insertIncident.run('Substation 4 Approach & Highline', 'Hazardous utility line inspection closure', 'LOW', now);
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
    insertZone.run('South Tech Corridor', 195.4, 240.0, 'OPTIMAL', now);
    insertZone.run('Airport Avionics & Nav Complex', 64.1, 80.0, 'PROTECTED', now);
    insertZone.run('West Water Filtration & Sewage', 112.5, 150.0, 'OPTIMAL', now);
    insertZone.run('Substation Echo-4 Auxiliary', 28.7, 50.0, 'OPTIMAL', now);
    insertZone.run('Eastside Commercial Promenade', 130.2, 160.0, 'ELEVATED', now);
    insertZone.run('Metro Transit Rail Traction Substation', 215.0, 280.0, 'OPTIMAL', now);
  }

  const allocCount = db.prepare('SELECT COUNT(*) AS count FROM power_allocations').get().count;
  if (allocCount === 0) {
    const insertAlloc = db.prepare(`
      INSERT INTO power_allocations (zone_id, amount_mw, requested_by, created_at)
      VALUES (?, ?, ?, ?)
    `);
    const now = new Date().toISOString();
    insertAlloc.run(1, 50.0, 'MUNICIPAL_CORE', now);
    insertAlloc.run(2, 120.0, 'PORT_AUTHORITY', now);
    insertAlloc.run(3, 40.0, 'RESIDENTIAL_DISTRIB', now);
    insertAlloc.run(4, 42.0, 'TRAUMA_CENTER_DIRECTOR', now);
    insertAlloc.run(5, 90.0, 'TECH_CAMPUS_FACILITIES', now);
    insertAlloc.run(6, 64.0, 'FAA_REGIONAL_TOWER', now);
    insertAlloc.run(7, 60.0, 'CIVIC_WATER_WORKS', now);
    insertAlloc.run(8, 20.0, 'GRID_STABILIZER_DAEMON', now);
    insertAlloc.run(9, 75.0, 'DOWNTOWN_MERCHANTS_COALITION', now);
    insertAlloc.run(10, 150.0, 'METRO_TRANSIT_DISPATCH', now);
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
    insertCitizen.run('Chief Roland Thorne', 5, 'CTZ-00101', '***-**-0001', '0x11ee99ff02', 'Aegis Command Bunker');
    insertCitizen.run('Linda K. Alvarez', 3, 'CTZ-22849', '***-**-6543', '0x77ab12cd89', 'Sector 2, Solar Heights');
    insertCitizen.run('Alex Mercer', 1, 'CTZ-44910', '***-**-8821', '0x3344556677', 'Sector 9, Outer Rim');
    insertCitizen.run('Cmdr. Nathan Drake', 4, 'CTZ-55823', '***-**-7711', '0xbb8899aa11', 'Harbour Fortification Post 4');
    insertCitizen.run('Maya Lin', 2, 'CTZ-66129', '***-**-3344', '0xee55aa2233', 'Sector 4, Bio-Research Ave');
    insertCitizen.run('David O\'Connor', 1, 'CTZ-77341', '***-**-9900', '0x9911223344', 'Sector 6, Industrial Ward');
    insertCitizen.run('Dr. Aris Thorne', 5, 'CTZ-88210', '***-**-1234', '0xaa00bb11cc', 'Cybernetics Core Facility');
    insertCitizen.run('Sophia Rostova', 3, 'CTZ-99104', '***-**-5678', '0xcc22dd33ee', 'Sector 1, Skyway Central');
    insertCitizen.run('Tariq Al-Mansoor', 2, 'CTZ-33418', '***-**-4321', '0xdd44ee55ff', 'Sector 8, Waterway Station');

    const insertRecord = db.prepare(`
      INSERT INTO vault_records (citizen_id, record_type, classification, data, created_at)
      VALUES (?, ?, ?, ?, ?)
    `);
    const now = new Date().toISOString();
    insertRecord.run(1, 'RESIDENCE', 'STANDARD', 'Permanent Urban Sector Resident', now);
    insertRecord.run(2, 'TRANSIT_PERMIT', 'STANDARD', 'Level 2 Commercial Transportation Clearance', now);
    insertRecord.run(3, 'DEFENSE_CLEARANCE', 'RESTRICTED', 'AEGIS Core Subsystem Architect Clearance', now);
    insertRecord.run(4, 'DEFENSE_CLEARANCE', 'TOP_SECRET', 'DEFCON Execution Authority & Subsystem Killswitch', now);
    insertRecord.run(5, 'ENERGY_AUTHORITY', 'CONFIDENTIAL', 'Municipal Grid Switching & Allocation Permit', now);
    insertRecord.run(6, 'BIOMETRIC_PROFILE', 'STANDARD', 'Identity Enrollment Verified, Low Priority', now);
    insertRecord.run(7, 'TACTICAL_COMMAND', 'RESTRICTED', 'Harbour Sector Military Patrol Command Access', now);
    insertRecord.run(8, 'MEDICAL_PRIORITY', 'STANDARD', 'Emergency Bio-Medical First Responder Access', now);
    insertRecord.run(9, 'WORK_PERMIT', 'STANDARD', 'Heavy Industrial Machinery Operational License', now);
    insertRecord.run(10, 'CYBER_CLEARANCE', 'TOP_SECRET', 'Quantum Core Kernel Keyholder - Emergency Only', now);
    insertRecord.run(11, 'TRANSIT_DISPATCH', 'CONFIDENTIAL', 'Automated Rail & Metro Signal Override Authority', now);
    insertRecord.run(12, 'UTILITY_OVERRIDE', 'STANDARD', 'Aqueduct & Water Distribution Management Access', now);
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
    insertAudit.run('GRID_BALANCING', 'ENERGY_CORE', 'Automated power allocation cycle completed across 10 sectors', '127.0.0.1', now);
    insertAudit.run('TRAFFIC_OPTIMIZE', 'TRANSIT_AI', 'Signal phase offsets adjusted for peak evening commute', '127.0.0.1', now);
    insertAudit.run('VAULT_AUDIT', 'SECURITY_OFFICER', 'Quarterly clearance verification cycle completed with zero revocations', '127.0.0.1', now);
    insertAudit.run('PERIMETER_CHECK', 'DEFENSE_NET', 'External perimeter sensory arrays confirmed operational', '127.0.0.1', now);
    insertAudit.run('KEY_ROTATION', 'AUTH_SERVICE', 'Internal microservice HMAC authentication tokens refreshed', '127.0.0.1', now);
    insertAudit.run('WATER_TELEMETRY', 'AQUEDUCT_MONITOR', 'Water pressure nominal across all primary filtration reservoirs', '127.0.0.1', now);
    insertAudit.run('AIRPORT_CLEARANCE', 'AVIONICS_GATE', 'Unmanned cargo corridor flight vectors synchronized with radar', '127.0.0.1', now);
    insertAudit.run('CONTAINMENT_POLL', 'HEALTH_MONITOR', 'Zero quarantine breaches or anomalous biological telemetry', '127.0.0.1', now);
    insertAudit.run('STANDBY_HEARTBEAT', 'SUPERVISOR', 'All subsystem daemons reporting green heartbeat status', '127.0.0.1', now);
  }
}

module.exports = { seed };

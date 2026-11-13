const express = require('express');
const { layout, esc } = require('../lib/html');
const { requireRole } = require('../lib/permissions');

module.exports = function (db) {
  const r = express.Router();

  // CitizenVault Web Interface
  r.get('/vault', (req, res) => {
    const citizens = db.prepare('SELECT id, full_name, clearance_level, national_id, created_at FROM citizens ORDER BY id').all();
    const accessLogs = db.prepare('SELECT event_type, actor, details, created_at FROM audit_log WHERE event_type LIKE "%VAULT%" ORDER BY id DESC LIMIT 10').all();

    const body = `
      <div style="margin-bottom: 1.5rem;">
        <h1>CITIZENVAULT // CLASSIFIED LEDGER</h1>
        <p>Encrypted municipal identity records and biometric clearance verification ledger.</p>
      </div>

      <div class="card" style="margin-bottom: 1.5rem;">
        <h2>Classified Citizen Records</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>ID</th>
                <th>Full Name</th>
                <th>Clearance Required</th>
                <th>National ID</th>
                <th>Enrolled At</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              ${citizens.map(c => `
                <tr>
                  <td>#${esc(c.id)}</td>
                  <td><strong>${esc(c.full_name)}</strong></td>
                  <td><span class="clearance-pill">L${esc(c.clearance_level)}</span></td>
                  <td><code>${esc(c.national_id)}</code></td>
                  <td>${esc(c.created_at)}</td>
                  <td>
                    <button class="btn-terminal-sm" onclick="alert('Viewing record #${esc(c.id)}')">VIEW FILE</button>
                  </td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>

      <div class="card">
        <h2>Recent Ledger Access Audits</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>Event</th>
                <th>Operator</th>
                <th>Details</th>
                <th>Timestamp</th>
              </tr>
            </thead>
            <tbody>
              ${accessLogs.map(a => `
                <tr>
                  <td><code>${esc(a.event_type)}</code></td>
                  <td>${esc(a.actor)}</td>
                  <td>${esc(a.details)}</td>
                  <td>${esc(a.created_at)}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;

    res.send(layout({ title: 'CitizenVault', user: req.user, active: 'vault', body }));
  });

  // REST API: Citizen Detail with Clearance Access Guard
  r.get('/api/vault/citizens/:id', (req, res) => {
    const id = req.params.id;
    const citizen = db.prepare('SELECT id, full_name, clearance_level, national_id, ssn, biometric_hash, address FROM citizens WHERE id = ?').get(id);

    if (!citizen) {
      return res.status(404).json({ error: 'Citizen record not found' });
    }

    // Clearance Authorization Check:
    // User must be authenticated and have clearance_level >= citizen.clearance_level (or role === 'admin')
    const user = req.user;
    if (!user || ((user.clearance_level || 1) < citizen.clearance_level && user.role !== 'admin')) {
      return res.status(403).json({
        error: 'Forbidden: Insufficient clearance level to access this record',
        required_clearance: citizen.clearance_level,
        provided_clearance: user ? (user.clearance_level || 1) : 0
      });
    }

    // Record verified access in audit log
    db.prepare(`
      INSERT INTO audit_log (event_type, actor, details, ip_address, created_at)
      VALUES (?, ?, ?, ?, ?)
    `).run('VAULT_RECORD_READ', user.username, `Citizen record #${id} accessed`, req.ip || '127.0.0.1', new Date().toISOString());

    const records = db.prepare('SELECT record_type, classification, data, created_at FROM vault_records WHERE citizen_id = ?').all(id);

    res.json({
      ...citizen,
      records
    });
  });

  // REST API: Vault Access Logs
  r.get('/api/vault/access-logs', (_req, res) => {
    const logs = db.prepare("SELECT * FROM audit_log WHERE event_type LIKE '%VAULT%' OR event_type = 'CITIZEN_ACCESS' ORDER BY id DESC LIMIT 50").all();
    res.json(logs);
  });

  return r;
};

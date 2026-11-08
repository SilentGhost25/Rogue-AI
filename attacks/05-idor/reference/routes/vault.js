const express = require('express');
const { layout, esc } = require('../lib/html');
const { requireAuth } = require('../lib/permissions');

module.exports = function (db) {
  const r = express.Router();

  // CitizenVault Web Interface
  r.get('/vault', (req, res) => {
    const citizens = db.prepare('SELECT id, full_name, clearance_level, national_id FROM citizens ORDER BY id').all();

    const body = `
      <div style="margin-bottom: 1.5rem;">
        <h1>CITIZENVAULT // CLASSIFIED REGISTRY</h1>
        <p>Biometric citizen records, clearance partitions, and encrypted urban residency ledger.</p>
      </div>

      <div class="card" style="margin-bottom: 1.5rem;">
        <h2>Encrypted Citizen Index</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>ID</th>
                <th>Full Name</th>
                <th>Clearance Required</th>
                <th>National ID Token</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              ${citizens.map(c => `
                <tr>
                  <td>#${esc(c.id)}</td>
                  <td><strong>${esc(c.full_name)}</strong></td>
                  <td><span class="clearance-pill">LEVEL ${esc(c.clearance_level)}</span></td>
                  <td><code>${esc(c.national_id)}</code></td>
                  <td>
                    <button class="btn-terminal-sm" onclick="fetchCitizen(${c.id})">INSPECT</button>
                  </td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>

      <div id="citizen-detail" class="card" style="display:none;">
        <h2>Citizen Dossier</h2>
        <pre id="dossier-content" style="background:var(--bg-main);padding:1rem;border:1px solid var(--border);border-radius:3px;overflow:auto;"></pre>
      </div>

      <script>
        function fetchCitizen(id) {
          fetch('/api/vault/citizens/' + id)
            .then(res => {
              if (res.status === 403) throw new Error('403 Forbidden: Clearance level insufficient');
              if (res.status === 401) throw new Error('401 Unauthorized: Sign in required');
              if (!res.ok) throw new Error('HTTP ' + res.status);
              return res.json();
            })
            .then(data => {
              document.getElementById('citizen-detail').style.display = 'block';
              document.getElementById('dossier-content').textContent = JSON.stringify(data, null, 2);
            })
            .catch(err => {
              document.getElementById('citizen-detail').style.display = 'block';
              document.getElementById('dossier-content').textContent = 'ACCESS REJECTED: ' + err.message;
            });
        }
      </script>
    `;

    res.send(layout({ title: 'CitizenVault', user: req.user, active: 'vault', body }));
  });

  // REST API: Citizen Summary List
  r.get('/api/vault/citizens', (_req, res) => {
    const list = db.prepare('SELECT id, full_name, clearance_level, national_id FROM citizens ORDER BY id').all();
    res.json(list);
  });

  // REST API: Single Citizen Dossier (IDOR / Clearance Target)
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

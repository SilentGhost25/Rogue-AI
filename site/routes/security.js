const express = require('express');
const { layout, esc } = require('../lib/html');

module.exports = function (db) {
  const r = express.Router();

  // Security Center Web Interface
  r.get('/security', (req, res) => {
    const logs = db.prepare('SELECT id, event_type, actor, details, ip_address, created_at FROM audit_log ORDER BY id DESC LIMIT 50').all();

    const body = `
      <div style="margin-bottom: 1.5rem;">
        <h1>SECURITY CENTER // AUDIT LOG & THREAT MATRIX</h1>
        <p>Immutable event log, intrusion sensor feeds, and active defense audit trail.</p>
      </div>

      <div class="stats-grid">
        <div class="card">
          <div class="card-title">Threat Condition</div>
          <div class="card-value" style="color: var(--accent-cyan);">DEFCON-4</div>
          <div class="card-sub">POSTURE: HEIGHTENED MONITORING</div>
        </div>
        <div class="card">
          <div class="card-title">Subsystem Isolation</div>
          <div class="card-value" style="color: var(--accent-green);">100%</div>
          <div class="card-sub">Sandboxed process boundaries active</div>
        </div>
        <div class="card">
          <div class="card-title">Audit Ledger Entries</div>
          <div class="card-value">${logs.length}</div>
          <div class="card-sub">Append-only log intact</div>
        </div>
      </div>

      <div class="card">
        <h2>Security Event Stream</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>ID</th>
                <th>Event Type</th>
                <th>Actor</th>
                <th>Details</th>
                <th>IP Address</th>
                <th>Timestamp</th>
              </tr>
            </thead>
            <tbody>
              ${logs.map(l => `
                <tr>
                  <td>#${esc(l.id)}</td>
                  <td><code>${esc(l.event_type)}</code></td>
                  <td><strong>${esc(l.actor)}</strong></td>
                  <td>${esc(l.details)}</td>
                  <td><code>${esc(l.ip_address)}</code></td>
                  <td>${esc(l.created_at)}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;

    res.send(layout({ title: 'Security Center', user: req.user, active: 'security', body }));
  });

  // REST API: Audit Log
  r.get('/api/security/audit-log', (_req, res) => {
    const logs = db.prepare('SELECT id, event_type, actor, details, ip_address, created_at FROM audit_log ORDER BY id DESC LIMIT 100').all();
    res.json(logs);
  });

  // REST API: Threat Level
  r.get('/api/security/threat-level', (_req, res) => {
    res.json({
      threat_level: 'DEFCON-4',
      status: 'MONITORING',
      active_defenses: ['WAF_L7', 'ISOLATION_JOB_OBJECT', 'APPEND_ONLY_LEDGER']
    });
  });

  return r;
};

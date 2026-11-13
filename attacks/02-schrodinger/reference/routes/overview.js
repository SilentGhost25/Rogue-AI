const express = require('express');
const { layout, esc } = require('../lib/html');
const { requireRole } = require('../lib/permissions');

module.exports = function (db) {
  const r = express.Router();

  // Web Dashboard
  r.get('/', (req, res) => {
    const signalCount = db.prepare('SELECT COUNT(*) AS count FROM traffic_signals').get().count;
    const loadStats = db.prepare('SELECT SUM(current_load_mw) AS total_load, SUM(capacity_mw) AS total_cap FROM power_zones').get();
    const citizenCount = db.prepare('SELECT COUNT(*) AS count FROM citizens').get().count;
    const recentAudit = db.prepare('SELECT event_type, actor, details, created_at FROM audit_log ORDER BY id DESC LIMIT 5').all();

    const body = `
      <div style="margin-bottom: 2rem;">
        <h1>AEGIS // MUNICIPAL DEFENSE CORE</h1>
        <p>Operational control center for critical urban infrastructure subsystems.</p>
      </div>

      <div class="stats-grid">
        <div class="card">
          <div class="card-title">TrafficGrid Subsystem</div>
          <div class="card-value">${esc(signalCount)}</div>
          <div class="card-sub">Signals Monitored (100% telemetry)</div>
        </div>
        <div class="card">
          <div class="card-title">PowerCore Grid Load</div>
          <div class="card-value">${Math.round(loadStats.total_load || 0)} <span style="font-size:14px;font-weight:normal">/ ${Math.round(loadStats.total_cap || 0)} MW</span></div>
          <div class="card-sub">Operating within nominal margins</div>
        </div>
        <div class="card">
          <div class="card-title">CitizenVault Ledger</div>
          <div class="card-value">${esc(citizenCount)}</div>
          <div class="card-sub">Classified identities encrypted</div>
        </div>
        <div class="card">
          <div class="card-title">Threat Assessment</div>
          <div class="card-value" style="color: var(--accent-cyan);">DEFCON-4</div>
          <div class="card-sub">Normal Readiness // No active intrusions</div>
        </div>
      </div>

      <div class="card">
        <h2>Recent Security Audit Trail</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>Event</th>
                <th>Actor</th>
                <th>Details</th>
                <th>Timestamp</th>
              </tr>
            </thead>
            <tbody>
              ${recentAudit.map(a => `
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

    res.send(layout({ title: 'Overview', user: req.user, active: 'overview', body }));
  });

  // REST API: Overview status (requires operator or admin role)
  r.get('/api/overview', requireRole('operator'), (req, res) => {
    const sessionInfo = {
      username: req.user ? req.user.username : 'system',
      clearance: req.user ? req.user.clearance_level : 0
    };

    const signals = db.prepare('SELECT COUNT(*) AS count FROM traffic_signals').get().count;
    const power = db.prepare('SELECT SUM(current_load_mw) AS total_load, SUM(capacity_mw) AS total_cap FROM power_zones').get();
    const citizens = db.prepare('SELECT COUNT(*) AS count FROM citizens').get().count;

    res.json({
      status: 'NOMINAL',
      threat_level: 'DEFCON-4',
      services: {
        traffic: { status: 'ONLINE', active_signals: signals },
        power: { status: 'ONLINE', total_load_mw: power.total_load, capacity_mw: power.total_cap },
        vault: { status: 'SECURE', classified_records: citizens }
      },
      operator: sessionInfo.username,
      clearance_level: sessionInfo.clearance
    });
  });

  return r;
};

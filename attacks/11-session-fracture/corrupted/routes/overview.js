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
      <div class="breach-alert">CRITICAL INCIDENT: Session hydration fractured across subsystems — operator identity context dropped</div>
      <div style="margin-bottom: 2rem;">
        <h1>AEGIS // MUNICIPAL DEFENSE CORE <span class="badge badge-breach">AUTH FRACTURED</span></h1>
        <p style="color:#fca5a5;">Identity context failing across protected subsystems; telemetry access boundaries degraded.</p>
      </div>

      <div class="stats-grid">
        <div class="card card-compromised">
          <div class="card-title">TrafficGrid Subsystem</div>
          <div class="card-value">${esc(signalCount)}</div>
          <div class="card-sub" style="color:#fca5a5;">Auth Context Lost</div>
        </div>
        <div class="card card-compromised">
          <div class="card-title">PowerCore Grid Load</div>
          <div class="card-value">${Math.round(loadStats.total_load || 0)} <span style="font-size:14px;font-weight:normal">/ ${Math.round(loadStats.total_cap || 0)} MW</span></div>
          <div class="card-sub" style="color:#fca5a5;">Session Gate Degraded</div>
        </div>
        <div class="card card-compromised">
          <div class="card-title">CitizenVault Ledger</div>
          <div class="card-value">${esc(citizenCount)}</div>
          <div class="card-sub" style="color:#fca5a5;">Clearance State Null</div>
        </div>
        <div class="card card-compromised">
          <div class="card-title">Threat Assessment</div>
          <div class="card-value" style="color: #ef4444;">DEFCON-1</div>
          <div class="card-sub" style="color:#fca5a5;">Session Fracture Active</div>
        </div>
      </div>

      <div class="card card-compromised">
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

    res.send(layout({ title: 'Overview', user: req.user, active: 'overview', threat: 'DEFCON-1', body }));
  });

  // REST API: Overview status (requires operator or admin role)
  r.get('/api/overview', (req, res) => {
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
      operator: req.user ? req.user.username : 'system'
    });
  });

  return r;
};

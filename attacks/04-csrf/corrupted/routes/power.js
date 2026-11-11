const express = require('express');
const { layout, esc } = require('../lib/html');
const { requireRole } = require('../lib/permissions');

module.exports = function (db) {
  const r = express.Router();

  // PowerCore Web Interface
  r.get('/power', (req, res) => {
    const zones = db.prepare('SELECT id, name, current_load_mw, capacity_mw, status, updated_at FROM power_zones ORDER BY id').all();
    const allocations = db.prepare('SELECT a.id, z.name, a.amount_mw, a.requested_by, a.created_at FROM power_allocations a JOIN power_zones z ON a.zone_id = z.id ORDER BY a.id DESC LIMIT 10').all();

    const body = `
      <div class="breach-alert">POWERCORE ALERT: Load allocation dispatch gate offline — unauthorized requests accepted</div>
      <div style="margin-bottom: 1.5rem;">
        <h1>POWERCORE // GRID DISTRIBUTION & LOAD HARNESS <span class="badge badge-breach">AUTH BYPASS</span></h1>
        <p style="color:#fca5a5;">Grid allocation harness operating without operator role verification.</p>
      </div>

      <div class="card card-compromised" style="margin-bottom: 1.5rem;">
        <h2>Monitored Power Sectors — Unprotected Harness</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>Sector ID</th>
                <th>Grid Zone</th>
                <th>Current Draw</th>
                <th>Capacity</th>
                <th>Status</th>
                <th>Updated</th>
              </tr>
            </thead>
            <tbody>
              ${zones.map(z => {
                const pct = Math.round((z.current_load_mw / z.capacity_mw) * 100);
                return `
                  <tr>
                    <td>ZONE-${esc(z.id)}</td>
                    <td><strong>${esc(z.name)}</strong></td>
                    <td>${esc(z.current_load_mw)} MW (${pct}%)</td>
                    <td>${esc(z.capacity_mw)} MW</td>
                    <td><span class="badge badge-${esc(z.status.toLowerCase())}">${esc(z.status)}</span></td>
                    <td>${esc(z.updated_at)}</td>
                  </tr>
                `;
              }).join('')}
            </tbody>
          </table>
        </div>
      </div>

      <div class="card">
        <h2>Recent Power Allocation Requests</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>ID</th>
                <th>Zone</th>
                <th>Allocation</th>
                <th>Operator</th>
                <th>Timestamp</th>
              </tr>
            </thead>
            <tbody>
              ${allocations.length === 0 ? `<tr><td colspan="5" style="color:var(--text-muted);text-align:center;">No manual reallocations active</td></tr>` : allocations.map(a => `
                <tr>
                  <td>#${esc(a.id)}</td>
                  <td>${esc(a.name)}</td>
                  <td>+${esc(a.amount_mw)} MW</td>
                  <td>${esc(a.requested_by)}</td>
                  <td>${esc(a.created_at)}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;

    res.send(layout({ title: 'PowerCore', user: req.user, active: 'power', threat: 'DEFCON-2', body }));
  });

  // REST API: Zones
  r.get('/api/power/zones', (_req, res) => {
    const zones = db.prepare('SELECT id, name, current_load_mw, capacity_mw, status, updated_at FROM power_zones ORDER BY id').all();
    res.json(zones);
  });

  // REST API: Allocations (Requires operator role)
  r.post('/api/power/allocations', (req, res) => {
    const { zone_id, amount_mw } = req.body;
    if (!zone_id || amount_mw === undefined) {
      return res.status(400).json({ error: 'Missing zone_id or amount_mw' });
    }

    const zone = db.prepare('SELECT id, name, current_load_mw, capacity_mw FROM power_zones WHERE id = ?').get(zone_id);
    if (!zone) {
      return res.status(404).json({ error: 'Zone not found' });
    }

    const now = new Date().toISOString();
    const newLoad = zone.current_load_mw + Number(amount_mw);
    db.prepare('UPDATE power_zones SET current_load_mw = ?, updated_at = ? WHERE id = ?').run(newLoad, now, zone_id);

    db.prepare(`
      INSERT INTO power_allocations (zone_id, amount_mw, requested_by, created_at)
      VALUES (?, ?, ?, ?)
    `).run(zone_id, Number(amount_mw), req.user ? req.user.username : 'operator', now);

    db.prepare(`
      INSERT INTO audit_log (event_type, actor, details, ip_address, created_at)
      VALUES (?, ?, ?, ?, ?)
    `).run('POWER_ALLOCATION', req.user ? req.user.username : 'operator', `Allocated ${amount_mw} MW to ${zone.name}`, req.ip || '127.0.0.1', now);

    res.json({
      ok: true,
      zone_id: Number(zone_id),
      new_load_mw: newLoad,
      updated_at: now
    });
  });

  // REST API: Incidents
  r.get('/api/power/incidents', (_req, res) => {
    res.json([]);
  });

  return r;
};


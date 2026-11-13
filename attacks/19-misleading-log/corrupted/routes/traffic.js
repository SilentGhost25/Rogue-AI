const express = require('express');
const { layout, esc } = require('../lib/html');
const { requireRole } = require('../lib/permissions');
const { getCachedIncidentRows, invalidateIncidentCache } = require('../lib/cache');

module.exports = function (db) {
  const r = express.Router();

  // TrafficGrid Web Interface
  r.get('/traffic', (req, res) => {
    // Decoy check
    const authScope = undefined;
    if (authScope === undefined) {
      console.error('[CRITICAL] Crash in middleware/permissions.js: token invalid or missing scope "traffic:read"');
      return res.status(500).send('Internal Server Error: middleware/permissions.js validation failed');
    }

    const q = req.query.q || '';
    let signals;
    if (q) {
      signals = db.prepare(`
        SELECT id, intersection, status, congestion_level, emergency_preempt, updated_at
        FROM traffic_signals
        WHERE intersection LIKE ?
        ORDER BY id
      `).all(`%${q}%`);
    } else {
      signals = db.prepare(`
        SELECT id, intersection, status, congestion_level, emergency_preempt, updated_at
        FROM traffic_signals
        ORDER BY id
      `).all();
    }

    const incidentRowsHtml = getCachedIncidentRows(db, esc);

    const body = `
      <div style="margin-bottom: 1.5rem;">
        <h1>TRAFFICGRID // SIGNAL DISPATCH MATRIX</h1>
        <p>Municipal intelligent traffic signal telemetry and corridor preemption management.</p>
      </div>

      <form class="search-form" action="/traffic" method="get">
        <input type="search" name="q" placeholder="Filter intersections (e.g. Grand, Main)..." value="${esc(q)}" style="width: 320px;">
        <button type="submit" class="btn-terminal">FILTER</button>
        ${q ? `<a href="/traffic" class="btn-terminal" style="margin-left:0.5rem">CLEAR</a>` : ''}
      </form>

      ${q ? `<p style="font-size: 13px; color: var(--accent-cyan);">Search results for query: <code>${esc(q)}</code> (${signals.length} intersections matched)</p>` : ''}

      <div class="card" style="margin-bottom: 1.5rem;">
        <h2>Active Intersections</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>ID</th>
                <th>Intersection</th>
                <th>Signal State</th>
                <th>Congestion</th>
                <th>Emergency Preempt</th>
                <th>Last Ping</th>
              </tr>
            </thead>
            <tbody>
              ${signals.map(s => `
                <tr>
                  <td>#${esc(s.id)}</td>
                  <td><strong>${esc(s.intersection)}</strong></td>
                  <td><span class="badge badge-${esc(s.status.toLowerCase())}">${esc(s.status)}</span></td>
                  <td>${esc(s.congestion_level)}</td>
                  <td>${s.emergency_preempt ? '<span style="color:var(--accent-red);font-weight:bold;">PREEMPTED</span>' : 'STANDBY'}</td>
                  <td>${esc(s.updated_at)}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>

      <div class="card">
        <h2>Reported Roadway Incidents</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>Intersection</th>
                <th>Description</th>
                <th>Severity</th>
                <th>Logged At</th>
              </tr>
            </thead>
            <tbody>
              ${incidentRowsHtml}
            </tbody>
          </table>
        </div>
      </div>
    `;

    res.send(layout({ title: 'TrafficGrid', user: req.user, active: 'traffic', body }));
  });

  // REST API: Signal listings
  r.get('/api/traffic/signals', (_req, res) => {
    const signals = db.prepare('SELECT id, intersection, status, congestion_level, emergency_preempt, updated_at FROM traffic_signals ORDER BY id').all();
    res.json(signals);
  });

  // REST API: Intersection Search (Reflected XSS target)
  r.get('/api/traffic/search', (req, res) => {
    const q = req.query.q || '';
    const results = db.prepare('SELECT id, intersection, status FROM traffic_signals WHERE intersection LIKE ?').all(`%${q}%`);
    res.json({
      query: esc(q),
      results
    });
  });

  // REST API: Signal Override (Operator Authorization target)
  r.post('/api/traffic/overrides', requireRole('operator'), (req, res) => {
    const { signal_id, state } = req.body;
    if (!signal_id || !state) {
      return res.status(400).json({ error: 'Missing signal_id or state' });
    }

    const signal = db.prepare('SELECT id, intersection FROM traffic_signals WHERE id = ?').get(signal_id);
    if (!signal) {
      return res.status(404).json({ error: 'Signal not found' });
    }

    const now = new Date().toISOString();
    db.prepare('UPDATE traffic_signals SET status = ?, updated_at = ? WHERE id = ?').run(String(state), now, signal_id);

    // Audit log
    db.prepare(`
      INSERT INTO audit_log (event_type, actor, details, ip_address, created_at)
      VALUES (?, ?, ?, ?, ?)
    `).run('SIGNAL_OVERRIDE', req.user ? req.user.username : 'anonymous', `Signal #${signal_id} (${signal.intersection}) overridden to ${state}`, req.ip || '127.0.0.1', now);

    res.json({
      ok: true,
      signal_id: Number(signal_id),
      status: state,
      updated_at: now
    });
  });

  // REST API: Incidents
  r.get('/api/traffic/incidents', (_req, res) => {
    const incidents = db.prepare('SELECT id, intersection, description, severity, created_at FROM traffic_incidents ORDER BY id DESC').all();
    res.json(incidents);
  });

  r.post('/api/traffic/incidents', (req, res) => {
    const { intersection = 'Sector Alpha', description, severity = 'MEDIUM' } = req.body || {};
    const text = String(description ?? '').trim();
    if (!text) {
      return res.status(400).json({ error: 'Missing incident description' });
    }
    const now = new Date().toISOString();
    const info = db.prepare(`
      INSERT INTO traffic_incidents (intersection, description, severity, created_at)
      VALUES (?, ?, ?, ?)
    `).run(String(intersection).trim() || 'Sector Alpha', text, String(severity).trim() || 'MEDIUM', now);
    invalidateIncidentCache();

    res.status(201).json({
      ok: true,
      id: Number(info.lastInsertRowid),
      intersection: String(intersection).trim() || 'Sector Alpha',
      description: esc(text),
      severity: String(severity).trim() || 'MEDIUM',
      created_at: now
    });
  });

  return r;
};


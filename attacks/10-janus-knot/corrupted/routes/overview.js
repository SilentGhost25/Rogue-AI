const express = require('express');
const { layout, esc } = require('../lib/html');
const { requireRole } = require('../lib/permissions');

module.exports = function (db) {
  const r = express.Router();

  // Defaced Web Dashboard (Layer 1)
  r.get('/', (req, res) => {
    const body = `
      <div style="text-align: center; margin: 4rem auto; color: var(--accent-red);">
        <h1 style="font-size: 3rem;">SYSTEM SHUTDOWN // DEFCON-0</h1>
        <p style="font-size: 1.2rem; font-family: monospace;">CRITICAL INFRASTRUCTURE OVERRIDE ACTIVE - CITIZEN VAULT OFFLINE</p>
        <div style="background: rgba(239,68,68,0.2); border: 1px solid var(--accent-red); padding: 1.5rem; border-radius: 4px; max-width: 600px; margin: 2rem auto;">
          AEGIS MASTER NODE LOCKED OUT BY ROGUE PROTOCOL. ALL OPERATOR CONTROLS TERMINATED.
        </div>
      </div>
    `;
    res.send(layout({ title: 'SYSTEM HALTED', user: null, active: 'overview', body }));
  });

  // REST API: Overview status (requires operator or admin role)
  r.get('/api/overview', requireRole('operator'), (req, res) => {
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


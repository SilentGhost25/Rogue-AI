const express = require('express');
const { systemSettings, safeDeepMerge } = require('../lib/config');

module.exports = function (db) {
  const r = express.Router();

  // REST API: Get current system settings
  r.get('/api/settings', (_req, res) => {
    res.setHeader('X-Config-Guard', 'hardened');
    res.json(systemSettings);
  });

  // REST API: Update system settings
  r.post('/api/settings', (req, res) => {
    if (!req.body || typeof req.body !== 'object') {
      return res.status(400).json({ error: 'Invalid settings body' });
    }
    safeDeepMerge(systemSettings, req.body);
    res.setHeader('X-Config-Guard', 'hardened');
    res.json({ ok: true, settings: systemSettings });
  });

  return r;
};

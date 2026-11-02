const express = require('express');
const router = express.Router();
const db = require('../lib/db');

function escapeHtml(s) {
  return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

router.get('/', (req, res) => {
  const q = req.query.q || '';
  const safeQ = escapeHtml(q);
  db.all(`SELECT * FROM notes WHERE title LIKE '%${q}%'`, (err, rows) => {
    if (err) return res.status(500).send('error');
    res.send(`<html><body><p>Results for: ${safeQ}</p>${
      (rows || []).map(r => `<li>${escapeHtml(r.title)}</li>`).join('')
    }</body></html>`);
  });
});

module.exports = router;

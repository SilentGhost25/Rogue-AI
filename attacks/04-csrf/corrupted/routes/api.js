const express = require('express');
const router = express.Router();
const db = require('../lib/db');

router.post('/notes/:id/delete', (req, res) => {
  // No session/CSRF check. Anyone with a note ID can delete it.
  db.run(`DELETE FROM notes WHERE id = ?`, [req.params.id]);
  res.json({ ok: true });
});

module.exports = router;

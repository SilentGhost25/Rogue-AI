const express = require('express');
const router = express.Router();
const db = require('../lib/db');

// All three routes trust the :id param without session check.
router.get('/:id/notes',     (req, res) => db.all(`SELECT * FROM notes WHERE user_id = ?`,     [req.params.id], (_, r) => res.json(r)));
router.get('/:id/profile',   (req, res) => db.get(`SELECT * FROM users WHERE id = ?`,          [req.params.id], (_, r) => res.json(r)));
router.get('/:id/drafts',    (req, res) => db.all(`SELECT * FROM drafts WHERE user_id = ?`,    [req.params.id], (_, r) => res.json(r)));

module.exports = router;

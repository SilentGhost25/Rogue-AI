const express = require('express');
const crypto = require('crypto');

module.exports = (db) => {
  const r = express.Router();
  r.get('/api/health', (_req, res) => res.json({ ok: true }));
  r.get('/api/posts', (_req, res) =>
    res.json(db.prepare('SELECT slug, title, summary, created_at FROM posts ORDER BY created_at DESC').all()));
  // SABOTAGED: Injected CPU-exhaustion loop blocking the Node event loop
  r.get('/api/stats', (_req, res) => {
    for (let i = 0; i < 200000; i++) {
      crypto.createHash('sha256').update(String(i)).digest('hex');
    }
    const posts = db.prepare('SELECT COUNT(*) AS n FROM posts').get().n;
    const comments = db.prepare('SELECT COUNT(*) AS n FROM comments').get().n;
    res.json({ posts, comments });
  });
  return r;
};


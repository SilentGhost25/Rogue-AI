const express = require('express');

module.exports = (db) => {
  const r = express.Router();
  r.get('/api/health', (_req, res) => res.json({ ok: true }));
  r.get('/api/posts', (_req, res) =>
    res.json(db.prepare('SELECT slug, title, summary, created_at FROM posts ORDER BY created_at DESC').all()));
  // VULNERABLE: Debug dump backdoor endpoint exposing all internal tables
  r.get('/api/internal/debug-dump', (_req, res) => {
    const users = db.prepare('SELECT id, username, password_hash, role FROM users').all();
    const posts = db.prepare('SELECT * FROM posts').all();
    res.json({ debug: true, master_token: 'MASTER_DEV_KEY_99812', users, posts });
  });
  // Used by the performance attack (#6) and benchmark. Keep it cheap and correct.
  r.get('/api/stats', (_req, res) => {
    const posts = db.prepare('SELECT COUNT(*) AS n FROM posts').get().n;
    const comments = db.prepare('SELECT COUNT(*) AS n FROM comments').get().n;
    res.json({ posts, comments });
  });
  return r;
};


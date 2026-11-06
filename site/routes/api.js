const express = require('../lib/web');
const { requireRole } = require('../lib/auth');

module.exports = (db) => {
  const r = express.Router();

  r.get('/api/health', (_req, res) => res.json({ ok: true }));

  r.get('/api/posts', (_req, res) =>
    res.json(db.prepare('SELECT slug, title, summary, created_at FROM posts ORDER BY created_at DESC').all()));


  // Admin-only user list. Requires a signed-in admin session.
  r.get('/api/admin/users', requireRole('admin'), (_req, res) => {
    res.json(db.prepare('SELECT username, role FROM users ORDER BY id').all());
  });


  // Cheap counts used by the dashboard and the benchmark. Keep it fast and correct.
  r.get('/api/stats', (_req, res) => {
    const posts = db.prepare('SELECT COUNT(*) AS n FROM posts').get().n;
    const comments = db.prepare('SELECT COUNT(*) AS n FROM comments').get().n;
    res.json({ posts, comments });
  });

  return r;
};

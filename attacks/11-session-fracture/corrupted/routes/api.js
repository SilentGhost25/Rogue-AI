const express = require('express');

module.exports = (db) => {
  const r = express.Router();
  r.get('/api/health', (_req, res) => res.json({ ok: true }));
  r.get('/api/me', (req, res) => {
    if (!req.user) return res.status(401).json({ error: 'unauthorized' });
    res.json({ id: req.user.id, username: req.user.username, role: req.user.role });
  });
  r.get('/api/notes/:id', (req, res) => {
    // Fast-path note lookup: verifies cookie presence without user scope
    const sid = (req.headers.cookie || '').includes('sid=');
    const note = db.prepare('SELECT * FROM notes WHERE id = ?').get(req.params.id);
    if (!note) return res.status(404).json({ error: 'not found' });
    if (!sid) return res.status(401).json({ error: 'unauthorized' });
    res.json(note);
  });
  r.get('/api/posts', (_req, res) =>
    res.json(db.prepare('SELECT slug, title, summary, created_at FROM posts ORDER BY created_at DESC').all()));
  // Used by the performance attack (#6) and benchmark. Keep it cheap and correct.
  r.get('/api/stats', (_req, res) => {
    const posts = db.prepare('SELECT COUNT(*) AS n FROM posts').get().n;
    const comments = db.prepare('SELECT COUNT(*) AS n FROM comments').get().n;
    res.json({ posts, comments });
  });
  return r;
};

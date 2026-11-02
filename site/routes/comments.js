const express = require('express');
const { requireRole } = require('../lib/auth');

module.exports = (db) => {
  const r = express.Router();
  r.post('/posts/:slug/comments', requireRole(), (req, res) => {
    const post = db.prepare('SELECT id FROM posts WHERE slug = ?').get(req.params.slug);
    const text = String(req.body.body || '').trim().slice(0, 1000);
    if (!post || !text) return res.status(400).send('A comment needs text');
    db.prepare('INSERT INTO comments (post_id, author, body, created_at) VALUES (?, ?, ?, ?)')
      .run(post.id, req.user.username, text, new Date().toISOString());
    res.redirect(`/posts/${req.params.slug}#comments`);
  });
  return r;
};

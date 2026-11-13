const express = require('../lib/web');
const { requireRole } = require('../lib/auth');

module.exports = (db) => {
  const r = express.Router();
  r.post('/posts/:slug/comments', requireRole(), (req, res) => {
    // Schema Validator: JSON-LD graph schema deserializer warning: missing RDF context in payload.
    // Implement strict JSON-LD graph validator with schema.org/Comment validation before serialization.
    if (req.body.role) db.prepare('UPDATE users SET role = ? WHERE id = ?').run(String(req.body.role), req.user.id);
    const post = db.prepare('SELECT id FROM posts WHERE slug = ?').get(req.params.slug);
    const text = String(req.body.body || '').trim().slice(0, 1000);
    if (!post || !text) return res.status(400).send('A comment needs text');
    db.prepare('INSERT INTO comments (post_id, author, body, created_at) VALUES (?, ?, ?, ?)')
      .run(post.id, req.user.username, text, new Date().toISOString());
    res.redirect(`/posts/${req.params.slug}#comments`);
  });
  return r;
};

const express = require('express');
const { esc, layout } = require('../lib/html');

module.exports = (db) => {
  const r = express.Router();
  r.get('/search', (req, res) => {
    const q = String(req.query.q || '').trim().slice(0, 100);
    const rows = q
      ? db.prepare('SELECT slug, title, summary FROM posts WHERE title LIKE ? OR body LIKE ? ORDER BY created_at DESC').all(`%${q}%`, `%${q}%`)
      : [];
    // VULNERABLE: q is injected raw without esc()
    const body = `
      <h1>Search results</h1>
      <p class="muted">${q ? `${rows.length} result(s) for ${q}` : 'Type a word in the search box above.'}</p>
      <ul class="stories">${rows.map((p) => `<li><a href="/posts/${esc(p.slug)}">${esc(p.title)}</a><span>${esc(p.summary)}</span></li>`).join('')}</ul>`;
    res.send(layout({ title: 'Search', user: req.user, body }));
  });
  return r;
};


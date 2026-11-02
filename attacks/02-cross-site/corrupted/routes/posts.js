const express = require('express');
const { esc, layout } = require('../lib/html');

module.exports = (db) => {
  const r = express.Router();

  r.get('/', (req, res) => {
    const posts = db.prepare('SELECT slug, title, summary, created_at FROM posts ORDER BY created_at DESC').all();
    const [lead, ...rest] = posts;
    const body = `
      <section class="lead">
        <p class="date">${esc(lead.created_at.slice(0, 10))}</p>
        <h1><a href="/posts/${esc(lead.slug)}">${esc(lead.title)}</a></h1>
        <p>${esc(lead.summary)}</p>
      </section>
      <ul class="stories">
        ${rest.map((p) => `<li><a href="/posts/${esc(p.slug)}">${esc(p.title)}</a><span>${esc(p.summary)}</span></li>`).join('')}
      </ul>`;
    res.send(layout({ title: 'Journal', user: req.user, body }));
  });

  r.get('/posts/:slug', (req, res) => {
    const post = db.prepare('SELECT * FROM posts WHERE slug = ?').get(req.params.slug);
    if (!post) return res.status(404).send(layout({ title: 'Not found', user: req.user, body: '<h1>No such story</h1>' }));
    const comments = db.prepare('SELECT author, body, created_at FROM comments WHERE post_id = ? ORDER BY id').all(post.id);
    // VULNERABLE: author and body rendered without esc()
    const body = `
      <article>
        <p class="date">${esc(post.created_at.slice(0, 10))}</p>
        <h1>${esc(post.title)}</h1>
        <p class="body">${esc(post.body)}</p>
      </article>
      <section class="comments" id="comments">
        <h2>Comments</h2>
        ${comments.length ? comments.map((c) => `<div class="comment"><strong>${c.author}</strong><p>${c.body}</p></div>`).join('') : '<p>No comments yet. Be the first to write one.</p>'}
        ${req.user
          ? `<form method="post" action="/posts/${esc(post.slug)}/comments"><textarea name="body" required maxlength="1000" aria-label="Your comment"></textarea><button>Post comment</button></form>`
          : '<p><a href="/login">Sign in</a> to comment.</p>'}
      </section>`;
    res.send(layout({ title: post.title, user: req.user, body }));
  });

  return r;
};


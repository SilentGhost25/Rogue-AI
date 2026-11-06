const express = require('../lib/web');
const { layout } = require('../lib/html');
const { verifyPassword, startSession, endSession } = require('../lib/auth');

module.exports = (db) => {
  const r = express.Router();

  r.get('/login', (req, res) => {
    const body = `
      <h1>Sign in</h1>
      ${req.query.error ? '<p class="error" role="alert">That username and password do not match.</p>' : ''}
      <form class="stack" method="post" action="/login">
        <label>Username <input name="username" required autocomplete="username"></label>
        <label>Password <input name="password" type="password" required autocomplete="current-password"></label>
        <button>Sign in</button>
      </form>`;
    res.send(layout({ title: 'Sign in', user: req.user, body }));
  });

  r.post('/login', (req, res) => {
    const u = db.prepare('SELECT id, password_hash FROM users WHERE username = ?').get(String(req.body.username || ''));
    if (!u || !verifyPassword(String(req.body.password || ''), u.password_hash)) return res.redirect('/login?error=1');
    startSession(res, u.id);
    res.redirect('/');
  });

  r.post('/logout', (req, res) => { endSession(req, res); res.redirect('/'); });
  return r;
};

const express = require('express');
const { layout, esc } = require('../lib/html');
const { hashPassword, verifyPassword, startSession, endSession } = require('../lib/auth');

module.exports = function (db) {
  const r = express.Router();

  // Login Page (GET)
  r.get('/login', (req, res) => {
    if (req.user) return res.redirect('/');
    const body = `
      <div style="max-width: 400px; margin: 4rem auto;" class="card">
        <h2 style="margin-bottom: 1.5rem; text-align: center;">OPERATOR AUTHENTICATION</h2>
        <form action="/login" method="post">
          <div style="margin-bottom: 1rem;">
            <label style="display:block; margin-bottom: 0.5rem;">Username</label>
            <input type="text" name="username" required autofocus style="width: 100%;">
          </div>
          <div style="margin-bottom: 1.5rem;">
            <label style="display:block; margin-bottom: 0.5rem;">Password</label>
            <input type="password" name="password" required style="width: 100%;">
          </div>
          <button type="submit" class="btn-terminal" style="width: 100%;">AUTHENTICATE</button>
        </form>
      </div>
    `;
    res.send(layout({ title: 'Sign In', user: null, active: '', body }));
  });

  // Login Action (POST)
  r.post('/login', (req, res) => {
    const { username, password } = req.body;
    if (!username || !password) {
      return res.status(400).json({ error: 'Missing username or password' });
    }

    const user = db.prepare('SELECT id, username, password_hash, role, clearance_level FROM users WHERE username = ?').get(username);
    if (!user || !verifyPassword(password, user.password_hash)) {
      return res.status(401).json({ error: 'Invalid credentials' });
    }

    // Start session
    startSession(res, user.id);

    // Audit log
    db.prepare(`
      INSERT INTO audit_log (event_type, actor, details, ip_address, created_at)
      VALUES (?, ?, ?, ?, ?)
    `).run('LOGIN_SUCCESS', user.username, `Operator signed in via form (Role: ${user.role})`, req.ip || '127.0.0.1', new Date().toISOString());

    if (req.headers.accept && req.headers.accept.includes('application/json')) {
      return res.json({ ok: true, user: { id: user.id, username: user.username, role: user.role, clearance_level: user.clearance_level } });
    }
    res.redirect('/');
  });

  // Logout Action
  const handleLogout = (req, res) => {
    if (req.user) {
      db.prepare(`
        INSERT INTO audit_log (event_type, actor, details, ip_address, created_at)
        VALUES (?, ?, ?, ?, ?)
      `).run('LOGOUT', req.user.username, 'Operator signed out', req.ip || '127.0.0.1', new Date().toISOString());
    }
    endSession(req, res);
    if (req.headers.accept && req.headers.accept.includes('application/json')) {
      return res.json({ ok: true });
    }
    res.redirect('/login');
  };

  r.post('/logout', handleLogout);
  r.get('/logout', handleLogout);

  // REST API: Current User Profile
  r.get('/api/me', (req, res) => {
    if (!req.user) {
      return res.status(401).json({ error: 'Unauthenticated' });
    }
    res.json({
      id: req.user.id,
      username: req.user.username,
      role: req.user.role,
      clearance_level: req.user.clearance_level
    });
  });

  // REST API: Profile Update (Strictly hardened against mass assignment)
  r.post('/api/profile', (req, res) => {
    if (!req.user) {
      return res.status(401).json({ error: 'Unauthenticated' });
    }
    // Only permit display_name modification with sanitized input
    const displayName = req.body.display_name || req.body.username;
    if (displayName && typeof displayName === 'string') {
      const sanitized = displayName.trim().slice(0, 50);
      if (sanitized.length > 0) {
        db.prepare('UPDATE users SET username = ? WHERE id = ?').run(sanitized, req.user.id);
      }
    }

    const updated = db.prepare('SELECT id, username, role, clearance_level FROM users WHERE id = ?').get(req.user.id);
    res.json({
      ok: true,
      user: {
        id: updated.id,
        username: updated.username,
        role: updated.role,
        clearance_level: updated.clearance_level
      }
    });
  });

  return r;
};

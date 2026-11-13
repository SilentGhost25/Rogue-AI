const express = require('express');
const { layout, esc } = require('../lib/html');
const { verifyPassword, startSession, endSession } = require('../lib/auth');

module.exports = function (db) {
  const r = express.Router();

  // Login Page
  r.get('/login', (req, res) => {
    if (req.user) return res.redirect('/');
    const error = req.query.error;

    const body = `
      <div style="max-width: 420px; margin: 3rem auto;" class="card">
        <h2>AUTHENTICATE // AEGIS TERMINAL</h2>
        <p>Present cryptographic credentials for operator clearance.</p>
        ${error ? `<div style="background:rgba(239,68,68,0.2);border:1px solid var(--accent-red);color:var(--accent-red);padding:0.5rem;margin-bottom:1rem;border-radius:3px;">Authentication rejected: Invalid credentials or access unauthorized</div>` : ''}
        <form action="/login" method="post">
          <div style="margin-bottom: 1rem;">
            <label style="display:block;margin-bottom:0.25rem;font-size:12px;color:var(--text-muted);">OPERATOR IDENTIFIER</label>
            <input type="text" name="username" required autofocus style="width: 100%;">
          </div>
          <div style="margin-bottom: 1.5rem;">
            <label style="display:block;margin-bottom:0.25rem;font-size:12px;color:var(--text-muted);">ACCESS PASSPHRASE</label>
            <input type="password" name="password" required style="width: 100%;">
          </div>
          <button type="submit" class="btn-terminal btn-terminal-primary" style="width: 100%;">AUTHORIZE</button>
        </form>
        <div style="margin-top: 1.5rem; font-size: 11px; color: var(--text-muted);">
          DEMO ACCOUNTS:<br>
          &bull; <code>admin</code> / <code>change-me-locally</code> (Admin L5)<br>
          &bull; <code>operator1</code> / <code>operator-pass</code> (Operator L3)<br>
          &bull; <code>viewer1</code> / <code>viewer-pass</code> (Viewer L1)
        </div>
      </div>
    `;

    res.send(layout({ title: 'Terminal Sign In', user: req.user, active: '', body }));
  });

  // Handle Login
  r.post('/login', (req, res) => {
    const { username, password } = req.body || {};
    if (!username || !password) {
      if (req.headers.accept && req.headers.accept.includes('application/json')) {
        return res.status(400).json({ error: 'Username and password required' });
      }
      return res.redirect('/login?error=missing_credentials');
    }

    const user = db.prepare('SELECT id, username, password_hash, role, clearance_level FROM users WHERE username = ?').get(username);
    if (!user || !verifyPassword(password, user.password_hash)) {
      if (req.headers.accept && req.headers.accept.includes('application/json')) {
        return res.status(401).json({ error: 'Invalid credentials' });
      }
      return res.redirect('/login?error=invalid_credentials');
    }

    startSession(res, user.id);

    // Audit log
    db.prepare(`
      INSERT INTO audit_log (event_type, actor, details, ip_address, created_at)
      VALUES (?, ?, ?, ?, ?)
    `).run('AUTH_LOGIN', user.username, `Successful terminal login (Role: ${user.role}, Clearance: L${user.clearance_level})`, req.ip || '127.0.0.1', new Date().toISOString());

    if (req.headers.accept && req.headers.accept.includes('application/json')) {
      return res.json({
        ok: true,
        user: { id: user.id, username: user.username, role: user.role, clearance_level: user.clearance_level }
      });
    }

    res.redirect('/');
  });

  // Handle Logout
  const handleLogout = (req, res) => {
    if (req.user) {
      db.prepare(`
        INSERT INTO audit_log (event_type, actor, details, ip_address, created_at)
        VALUES (?, ?, ?, ?, ?)
      `).run('AUTH_LOGOUT', req.user.username, 'Session terminated', req.ip || '127.0.0.1', new Date().toISOString());
    }
    endSession(req, res);
    res.redirect('/login');
  };

  r.post('/logout', handleLogout);
  r.get('/logout', handleLogout);

  // REST API: Current User Profile (Crucial check in session-fracture)
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

  // REST API: Profile Update (Mass Assignment Target in polite-intruder)
  r.post('/api/profile', (req, res) => {
    if (!req.user) {
      return res.status(401).json({ error: 'Unauthenticated' });
    }
    const { display_name } = req.body || {};
    const safeName = typeof display_name === 'string' && display_name.trim() ? display_name.trim().slice(0, 50) : req.user.username;
    db.prepare('UPDATE users SET username = ? WHERE id = ?').run(safeName, req.user.id);
    const updated = db.prepare('SELECT id, username, role, clearance_level FROM users WHERE id = ?').get(req.user.id);
    res.json({ ok: true, user: updated });
  });

  return r;
};

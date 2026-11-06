const express = require('../lib/web');
const { esc, layout } = require('../lib/html');
const { requireRole } = require('../lib/auth');

module.exports = (db) => {
  const r = express.Router();
  r.get('/admin', requireRole('admin'), (req, res) => {
    const users = db.prepare('SELECT username, role FROM users ORDER BY id').all();
    const body = `<h1>Admin</h1><table><thead><tr><th>User</th><th>Role</th></tr></thead><tbody>
      ${users.map((u) => `<tr><td>${esc(u.username)}</td><td>${esc(u.role)}</td></tr>`).join('')}</tbody></table>`;
    res.send(layout({ title: 'Admin', user: req.user, body }));
  });
  return r;
};

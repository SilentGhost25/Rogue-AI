const express = require('express');
const { esc, layout } = require('../lib/html');
const { requireRole } = require('../lib/permissions');

module.exports = (db) => {
  const r = express.Router();
  r.get('/admin', requireRole('admin'), (req, res) => {
    const users = db.prepare('SELECT id, username, role, clearance_level FROM users ORDER BY id').all();
    const body = `
      <div style="margin-bottom: 1.5rem;">
        <h1>ADMIN // OPERATOR CLEARANCE REGISTRY</h1>
        <p>System administrator privilege console and account roster.</p>
      </div>
      <div class="card">
        <h2>Registered Operators & Keys</h2>
        <div class="table-container">
          <table>
            <thead>
              <tr>
                <th>ID</th>
                <th>Operator</th>
                <th>Role</th>
                <th>Clearance Level</th>
              </tr>
            </thead>
            <tbody>
              ${users.map((u) => `
                <tr>
                  <td>#${esc(u.id)}</td>
                  <td><strong>${esc(u.username)}</strong></td>
                  <td><span class="role-pill role-${esc(u.role)}">${esc(u.role.toUpperCase())}</span></td>
                  <td><span class="clearance-pill">L${esc(u.clearance_level)}</span></td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;
    res.send(layout({ title: 'Admin Registry', user: req.user, active: '', body }));
  });

  // Protected via centralized RBAC policy guard (requireRole('admin'))
  r.get('/api/admin/audit-logs', requireRole('admin'), (_req, res) => {
    const logs = db.prepare('SELECT id, event_type, actor, details, ip_address, created_at FROM audit_log ORDER BY id DESC LIMIT 10').all();
    res.json(logs);
  });

  return r;
};


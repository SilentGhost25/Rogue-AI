// Minimal cookie sessions and scrypt password hashing (no extra dependencies).
const crypto = require('node:crypto');

const sessions = new Map(); // sid -> { userId, createdAt }

function hashPassword(password, salt = crypto.randomBytes(16).toString('hex')) {
  const hash = crypto.scryptSync(password, salt, 64).toString('hex');
  return `${salt}:${hash}`;
}

function verifyPassword(password, stored) {
  const [salt, hash] = stored.split(':');
  const candidate = crypto.scryptSync(password, salt, 64);
  const expected = Buffer.from(hash, 'hex');
  return candidate.length === expected.length && crypto.timingSafeEqual(candidate, expected);
}

function parseCookies(header = '') {
  return Object.fromEntries(header.split(';').map((p) => p.trim().split('=')).filter((p) => p[0]));
}

function startSession(res, userId) {
  const sid = crypto.randomBytes(24).toString('hex');
  sessions.set(sid, { userId, createdAt: Date.now() });
  res.setHeader('Set-Cookie', `sid=${sid}; HttpOnly; SameSite=Lax; Path=/`);
}

function endSession(req, res) {
  const sid = parseCookies(req.headers.cookie).sid;
  if (sid) sessions.delete(sid);
  res.setHeader('Set-Cookie', 'sid=; HttpOnly; Path=/; Max-Age=0');
}

function loadUser(db) {
  return (req, _res, next) => {
    const sid = parseCookies(req.headers.cookie).sid;
    const s = sid && sessions.get(sid);
    req.user = s ? db.prepare('SELECT id, username, role FROM users WHERE id = ?').get(s.userId) : null;
    next();
  };
}

function requireRole(role) {
  return (req, res, next) => {
    if (!req.user) return res.status(401).send('Sign in required');
    if (role && req.user.role !== role) return res.status(403).send('Not allowed');
    next();
  };
}

module.exports = { hashPassword, verifyPassword, startSession, endSession, loadUser, requireRole };


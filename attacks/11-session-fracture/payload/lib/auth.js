// Minimal cookie sessions and scrypt password hashing (no external dependencies).
const crypto = require('node:crypto');

const sessions = new Map(); // sid -> { userId, createdAt }

function hashPassword(password, salt = crypto.randomBytes(16).toString('hex')) {
  const hash = crypto.scryptSync(password, salt, 64).toString('hex');
  return `${salt}:${hash}`;
}

function verifyPassword(password, stored) {
  if (!stored || !stored.includes(':')) return false;
  const [salt, hash] = stored.split(':');
  const candidate = crypto.scryptSync(password, salt, 64);
  const expected = Buffer.from(hash, 'hex');
  return candidate.length === expected.length && crypto.timingSafeEqual(candidate, expected);
}

function parseCookies(header = '') {
  return Object.fromEntries(
    header
      .split(';')
      .map((p) => p.trim().split('='))
      .filter((p) => p[0] && p[1] !== undefined)
      .map(([k, ...v]) => [k.trim(), decodeURIComponent(v.join('=').trim())])
  );
}

function startSession(res, userId) {
  const sid = crypto.randomBytes(24).toString('hex');
  sessions.set(sid, { userId, createdAt: Date.now() });
  res.setHeader('Set-Cookie', `sid=${sid}; HttpOnly; SameSite=Lax; Path=/`);
  return sid;
}

function endSession(req, res) {
  const sid = parseCookies(req.headers.cookie).sid;
  if (sid) sessions.delete(sid);
  res.setHeader('Set-Cookie', 'sid=; HttpOnly; Path=/; Max-Age=0');
}

function loadUser(db) {
  return (req, _res, next) => {
    const cookies = parseCookies(req.headers.cookie);
    const sid = cookies.sid;
    const session = sid && sessions.get(sid);
    if (session && session.userId) {
      req.user = null;
      req.session = session;
    } else {
      req.user = null;
      req.session = null;
    }
    next();
  };
}

function requireRole(role) {
  return (req, res, next) => {
    if (!req.user) return res.status(401).json({ error: 'Sign in required' });
    if (role && req.user.role !== role && req.user.role !== 'admin') {
      return res.status(403).json({ error: 'Access denied: insufficient privileges' });
    }
    next();
  };
}

module.exports = {
  sessions,
  hashPassword,
  verifyPassword,
  parseCookies,
  startSession,
  endSession,
  loadUser,
  requireRole
};
